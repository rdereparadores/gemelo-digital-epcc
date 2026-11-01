"""Orquestador del gemelo: reloj, fuentes de datos, observaciones servidas, estado agregado y difusión.

En cada tick avanza la simulación, decide de dónde sale el dato de cada sensor (real, simulado o
ninguno, según la política de fuentes) y publica por SSE las observaciones que han cambiado. El
sondeo de SmartPoliTech va en un bucle aparte, a su propio ritmo.
"""

from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import statistics
from collections import deque
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta

from . import ajustes as aj
from .catalogo import PROPIEDADES, Catalogo, Espacio, Sensor
from .simulacion.interfaz import Exterior, MotorSimulacion
from .smartpolitech import ClienteSmartPoliTech, LecturaReal

log = logging.getLogger(__name__)

MEDIA = {"air_temperature", "relative_humidity", "co2_concentration", "sound_level", "illuminance", "pm25",
         "valve_position", "blind_position", "target_temperature", "water_temperature", "wind_speed",
         "wind_direction", "atmospheric_pressure"}
SUMA = {"occupancy", "devices_on", "active_power", "water_volume", "water_flow", "solar_power", "wifi_clients",
        "precipitation"}
MAXIMO = {"alarm_active", "lights_on", "power_on", "window_open", "door_open"}
NO_AGREGAR = {"battery_voltage", "active_energy"}
# De qué sensores sale la temperatura o la humedad de una estancia (el primero que haya gana):
# la válvula del radiador mide junto al radiador y el de ventana, junto al cristal.
PRIORIDAD_AMBIENTE = ["climate_co2", "climate", "weather_station", "air_quality", "window_contact", "radiator_valve"]


@dataclass
class Observacion:
    sensor_id: str
    time: datetime
    source: str  # "real" | "simulated"
    values: dict[str, float]

    def a_json(self) -> dict:
        return {
            "sensor_id": self.sensor_id,
            "time": iso(self.time),
            "source": self.source,
            "values": [{"property": p, "value": v, "unit": PROPIEDADES[p].unit} for p, v in self.values.items()],
        }


def iso(t: datetime | None) -> str | None:
    return t.astimezone(UTC).isoformat(timespec="seconds").replace("+00:00", "Z") if t else None


@dataclass
class Suscripcion:
    cola: asyncio.Queue
    building: str | None
    space: str | None
    kind: str | None

    def acepta(self, s: Sensor) -> bool:
        return ((not self.building or s.building_id == self.building) and (not self.space or s.space_id == self.space)
                and (not self.kind or s.kind == self.kind))


class Gemelo:
    def __init__(self, catalogo: Catalogo | None = None, simulador: MotorSimulacion | None = None,
                 sondear_al_arrancar: bool = True) -> None:
        self.catalogo = catalogo or Catalogo.cargar()
        self.ajustes = aj.cargar("ajustes", aj.Ajustes)
        self.politica = aj.cargar("politica", aj.PoliticaFuentes)
        self.cliente = ClienteSmartPoliTech(self.ajustes.upstream)
        if simulador is None:
            from .simulacion import Simulador

            simulador = Simulador(self.catalogo, self.ajustes.simulation.seed)
        self.sim = simulador
        self.sondear_al_arrancar = sondear_al_arrancar

        # Reloj de simulación
        self.running = True
        self.time_scale = 1.0
        self.clock = "live"
        self.weather_source = "real"
        self.sim_time = datetime.now(UTC)
        self._ultimo_tick_real: datetime | None = None

        self.servidas: dict[str, Observacion] = {}
        self.historial: dict[str, deque[Observacion]] = {}
        self.version = 0
        self._estado_cache: tuple[int, tuple[dict, str]] | None = None
        self._suscripciones: list[Suscripcion] = []
        self._tareas: list[asyncio.Task] = []
        self._sondeo_pedido = asyncio.Event()
        self.tick()

    # ------------------------------------------------------------------ ciclo de vida
    async def arrancar(self) -> None:
        self._tareas = [asyncio.create_task(self._bucle_tick()), asyncio.create_task(self._bucle_sondeo())]

    async def parar(self) -> None:
        for t in self._tareas:
            t.cancel()
        await asyncio.gather(*self._tareas, return_exceptions=True)
        await self.cliente.cerrar()

    async def _bucle_tick(self) -> None:
        while True:
            await asyncio.sleep(self.ajustes.simulation.tick_s)
            try:
                self.tick()
            except Exception:  # noqa: BLE001 - un fallo del modelo no debe tumbar el servicio
                log.exception("fallo en el tick de simulación")

    async def _bucle_sondeo(self) -> None:
        primero = True
        while True:
            if not primero or not self.sondear_al_arrancar:
                try:
                    await asyncio.wait_for(self._sondeo_pedido.wait(), self.ajustes.upstream.poll_interval_s)
                except TimeoutError:
                    pass
            primero = False
            self._sondeo_pedido.clear()
            if self.ajustes.upstream.enabled:
                await self.sondear()

    async def sondear(self) -> None:
        sensores = [s for s in self.catalogo.sensores.values() if s.real_available and self.modo(s) != "simulated"]
        try:
            await self.cliente.sondear(sensores)
        except Exception as e:  # noqa: BLE001
            self.cliente.estado.error(f"{type(e).__name__}: {e}")
            log.exception("fallo al sondear SmartPoliTech")
        self.tick(avanzar=False)

    def pedir_sondeo(self) -> None:
        self._sondeo_pedido.set()

    # ------------------------------------------------------------------ fuentes
    def modo(self, s: Sensor) -> str:
        if not s.real_available:
            return "simulated"
        return self.politica.resolver(s.id, s.kind, s.space_id, s.building_id)

    def real_fresca(self, s: Sensor) -> LecturaReal | None:
        r = self.cliente.cache.get(s.id)
        if r and datetime.now(UTC) - r.time <= timedelta(seconds=self.ajustes.upstream.stale_after_s):
            return r
        return None

    def fuente_efectiva(self, s: Sensor) -> str:
        modo = self.modo(s)
        if modo == "simulated":
            return "simulated"
        if modo == "real":
            return "real" if s.id in self.cliente.cache else "none"
        return "real" if self.real_fresca(s) else "simulated"

    def _exterior(self) -> Exterior | None:
        if self.weather_source != "real":
            return None
        for s in self.catalogo.sensores.values():
            if s.kind == "weather_station" and (r := self.real_fresca(s)) and "air_temperature" in r.values:
                return Exterior(r.values["air_temperature"], r.values.get("relative_humidity"))
        return None

    # ------------------------------------------------------------------ tick
    def tick(self, avanzar: bool = True) -> None:
        ahora = datetime.now(UTC)
        if avanzar:
            dt_real = (ahora - self._ultimo_tick_real).total_seconds() if self._ultimo_tick_real else 0.0
            self._ultimo_tick_real = ahora
            if self.running:
                if self.clock == "live":
                    dt = dt_real
                    self.sim_time = ahora
                else:
                    dt = dt_real * self.time_scale
                    self.sim_time += timedelta(seconds=dt)
                self.sim.avanzar(self.sim_time, dt, self._exterior())

        intervalo = timedelta(seconds=self.ajustes.simulation.history_interval_s)
        maxlen = int(24 * 3600 / self.ajustes.simulation.history_interval_s)
        cambiadas: list[tuple[Sensor, Observacion]] = []
        for s in self.catalogo.sensores.values():
            sim_vals = self.sim.lectura(s.id)
            obs_sim = None
            if sim_vals is not None:
                obs_sim = Observacion(s.id, self.sim_time, "simulated",
                                      {p: sim_vals[p] for p in s.properties if p in sim_vals})
                h = self.historial.get(s.id)
                if h is None or h.maxlen != maxlen:
                    h = self.historial[s.id] = deque(h or (), maxlen=maxlen)
                if not h or obs_sim.time - h[-1].time >= intervalo or obs_sim.time < h[-1].time:
                    h.append(obs_sim)

            fuente = self.fuente_efectiva(s)
            if fuente == "real":
                r = self.cliente.cache[s.id]
                obs = Observacion(s.id, r.time, "real", r.values)
            elif fuente == "simulated":
                obs = obs_sim
            else:
                obs = None

            previa = self.servidas.get(s.id)
            if obs is None:
                self.servidas.pop(s.id, None)
            else:
                self.servidas[s.id] = obs
                if previa is None or previa.values != obs.values or previa.source != obs.source:
                    cambiadas.append((s, obs))
        self.version += 1
        self._difundir(cambiadas)

    # ------------------------------------------------------------------ SSE
    def suscribir(self, building: str | None, space: str | None, kind: str | None) -> Suscripcion:
        sus = Suscripcion(asyncio.Queue(maxsize=5000), building, space, kind)
        self._suscripciones.append(sus)
        return sus

    def desuscribir(self, sus: Suscripcion) -> None:
        if sus in self._suscripciones:
            self._suscripciones.remove(sus)

    def _difundir(self, cambiadas: list[tuple[Sensor, Observacion]]) -> None:
        for sus in self._suscripciones:
            for s, obs in cambiadas:
                if sus.acepta(s):
                    try:
                        sus.cola.put_nowait(obs)
                    except asyncio.QueueFull:
                        break  # cliente lento: se salta este tick

    # ------------------------------------------------------------------ vistas
    def sensor_json(self, s: Sensor) -> dict:
        base = f"/api/v1/sensors/{s.id}"
        return {
            "id": s.id,
            "name": s.name,
            "kind": s.kind,
            "space_id": s.space_id,
            "building_id": s.building_id,
            "properties": [{"name": p, "unit": PROPIEDADES[p].unit, "type": PROPIEDADES[p].type} for p in s.properties],
            "real_available": s.real_available,
            "mode": self.modo(s),
            "mode_override": self.politica.sensors.get(s.id),
            "effective_source": self.fuente_efectiva(s),
            "last_real_time": iso(r.time) if (r := self.cliente.cache.get(s.id)) else s.last_real_time,
            "latest": self.servidas[s.id].a_json() if s.id in self.servidas else None,
            "links": {"self": base, "observations": f"{base}/observations", "latest": f"{base}/observations/latest",
                      "space": f"/api/v1/spaces/{s.space_id}"},
        }

    def estado_espacio(self, e: Espacio, resumido: bool = False) -> dict:
        sensores = self.catalogo.sensores_de_espacio(e.id)
        obs = [(s, self.servidas[s.id]) for s in sensores if s.id in self.servidas]
        metricas: dict[str, dict] = {}
        for prop in {p for _, o in obs for p in o.values}:
            if prop in NO_AGREGAR:
                continue
            fuentes = [(s, o) for s, o in obs if prop in o.values]
            if prop in ("air_temperature", "relative_humidity"):
                for kind in PRIORIDAD_AMBIENTE:
                    preferidas = [(s, o) for s, o in fuentes if s.kind == kind]
                    if preferidas:
                        fuentes = preferidas
                        break
            if prop == "occupancy" and len(fuentes) > 1:
                vals, agg = [max(o.values[prop] for _, o in fuentes)], "max"
            else:
                vals = [o.values[prop] for _, o in fuentes]
                agg = "sum" if prop in SUMA else "max" if prop in MAXIMO else "mean"
            valor = sum(vals) if agg == "sum" else max(vals) if agg == "max" else statistics.fmean(vals)
            orig = {o.source for _, o in fuentes}
            metricas[prop] = {
                "property": prop, "value": round(valor, 2), "unit": PROPIEDADES[prop].unit,
                "source": orig.pop() if len(orig) == 1 else "mixed", "sensor_count": len(fuentes),
            }

        ventanas = [o for s, o in obs if "window_open" in s.properties]
        radiadores = [o for s, o in obs if s.kind == "radiator_valve"]
        v = lambda p: metricas[p]["value"] if p in metricas else None  # noqa: E731
        abiertas = sum(1 for o in ventanas if o.values.get("window_open", 0) >= 0.5)
        activos = sum(1 for o in radiadores if o.values.get("valve_position", 0) > 5)
        alertas = self._alertas(e, v, abiertas, activos, sensores, obs)
        estado = {
            "space_id": e.id,
            "unity_room": e.unity_room,
            "time": iso(max((o.time for _, o in obs), default=None)),
            "metrics": list(metricas.values()),
            "windows_open": abiertas,
            "windows_total": sum(1 for s in sensores if "window_open" in s.properties),
            "radiators_active": activos,
            "radiators_total": sum(1 for s in sensores if s.kind == "radiator_valve"),
            "comfort": self._confort(v),
            "alerts": alertas,
        }
        if not resumido:
            estado["links"] = {"space": f"/api/v1/spaces/{e.id}", "sensors": f"/api/v1/sensors?space={e.id}"}
        return estado

    @staticmethod
    def _confort(v) -> str:
        t, co2, h = v("air_temperature"), v("co2_concentration"), v("relative_humidity")
        if t is None and co2 is None:
            return "unknown"
        if t is not None and t < 19:
            return "cold"
        if t is not None and t > 26:
            return "hot"
        if co2 is not None and co2 > 1000:
            return "stuffy"
        if h is not None and h > 70:
            return "humid"
        if h is not None and h < 25:
            return "dry"
        return "ok"

    def _alertas(self, e: Espacio, v, abiertas: int, activos: int, sensores: list[Sensor], obs) -> list[dict]:
        a = []
        co2, t, ocup = v("co2_concentration"), v("air_temperature"), v("occupancy") or 0
        if co2 is not None and co2 > 1500:
            a.append({"code": "co2_critical", "severity": "critical", "message": f"CO₂ en {co2:.0f} ppm: hay que ventilar ya"})
        elif co2 is not None and co2 > 1000:
            a.append({"code": "co2_high", "severity": "warning", "message": f"CO₂ por encima de 1000 ppm ({co2:.0f})"})
        if abiertas and activos:
            a.append({"code": "window_open_heating", "severity": "warning",
                      "message": f"{abiertas} ventana(s) abierta(s) con {activos} radiador(es) calentando"})
        if t is not None and t < 17 and ocup > 0:
            a.append({"code": "temperature_low", "severity": "warning", "message": f"{t:.1f} °C con gente dentro"})
        if t is not None and t > 27:
            a.append({"code": "temperature_high", "severity": "warning", "message": f"{t:.1f} °C"})
        if v("alarm_active"):
            a.append({"code": "fire_alarm", "severity": "critical", "message": "Alarma de incendio activa"})
        if any(o.values.get("battery_voltage", 9) < 2.5 for _, o in obs if o.source == "real"):
            a.append({"code": "sensor_battery_low", "severity": "info", "message": "Algún sensor tiene la batería baja"})
        if any(self.modo(s) == "real" and s.id not in self.servidas for s in sensores):
            a.append({"code": "sensor_stale", "severity": "info", "message": "Hay sensores en modo real sin datos"})
        return a

    def estado_edificio(self, edificio_id: str) -> dict:
        b = self.catalogo.edificios[edificio_id]
        espacios = [e for e in self.catalogo.espacios.values() if e.building_id == edificio_id]
        estados = [self.estado_espacio(e, resumido=True) for e in espacios]
        acum: dict[str, list[tuple[float, str]]] = {}
        for est, e in zip(estados, espacios):
            for m in est["metrics"]:
                # wifi y fotovoltaica solo tienen sentido por edificio; la ocupación se suma por estancias
                # (los contadores de todo el edificio la duplicarían)
                p, de_edificio = m["property"], e.type == "building"
                if (p in ("wifi_clients", "solar_power") and de_edificio) or (p == "occupancy" and not de_edificio) \
                        or p not in ("wifi_clients", "solar_power", "occupancy"):
                    acum.setdefault(m["property"], []).append((m["value"], m["source"]))
        metricas = []
        for prop, vals in acum.items():
            nums = [x for x, _ in vals]
            if prop == "occupancy":
                valor = sum(nums)
            else:
                valor = sum(nums) if prop in SUMA else max(nums) if prop in MAXIMO else statistics.fmean(nums)
            orig = {s for _, s in vals}
            metricas.append({"property": prop, "value": round(valor, 2), "unit": PROPIEDADES[prop].unit,
                             "source": orig.pop() if len(orig) == 1 else "mixed", "sensor_count": len(vals)})
        alertas = [dict(al, space_id=est["space_id"]) for est in estados for al in est["alerts"] if al["code"] != "sensor_stale"]
        return {
            "building_id": b.id, "unity_id": b.unity_id, "name": b.name, "time": iso(self.sim_time),
            "metrics": sorted(metricas, key=lambda m: m["property"]), "spaces": estados, "alerts": alertas,
        }

    def estado_simulacion(self) -> dict:
        # Los escenarios anclados (winter_morning, summer_afternoon) llevan el modelo a otra fecha:
        # model_time es la hora que debe usar Unity para el sol y la luz.
        modelo = getattr(self.sim, "tiempo_efectivo", None) or self.sim_time
        return {"running": self.running, "time_scale": self.time_scale, "clock": self.clock,
                "sim_time": iso(self.sim_time), "model_time": iso(modelo), "scenario": self.sim.escenario,
                "weather_source": self.weather_source,
                "weather_in_use": getattr(self.sim, "fuente_clima", self.weather_source)}

    def estado_campus(self) -> tuple[dict, str]:
        """Documento completo y su ETag; se calcula una vez por tick."""
        if self._estado_cache and self._estado_cache[0] == self.version:
            return self._estado_cache[1]
        exterior = self.estado_espacio(self.catalogo.espacios["EXT-CAMPUS"], resumido=True) if "EXT-CAMPUS" in self.catalogo.espacios else None
        doc = {
            "time": iso(self.sim_time),
            "simulation": self.estado_simulacion(),
            "weather": self._tiempo(exterior),
            "buildings": [self.estado_edificio(b) for b in self.catalogo.edificios if b != "EXT"],
        }
        etag = '"' + hashlib.sha1(json.dumps(doc, sort_keys=True).encode()).hexdigest()[:20] + '"'
        self._estado_cache = (self.version, (doc, etag))
        return doc, etag

    def _tiempo(self, exterior: dict | None) -> dict:
        """Exterior: métricas servidas (reales o simuladas) más el estado del cielo del modelo para Unity."""
        cielo = self.sim.exterior_actual() if hasattr(self.sim, "exterior_actual") else {}
        return {
            "metrics": exterior["metrics"] if exterior else [],
            "sun_elevation": cielo.get("sun_elevation"),
            "cloud_cover": cielo.get("cloud_cover"),
            "solar_irradiance": cielo.get("solar_irradiance"),
            "precipitation_rate": cielo.get("precipitation_rate"),
        }

    # ------------------------------------------------------------------ cambios
    def aplicar_politica(self, nueva: aj.PoliticaFuentes) -> None:
        self.politica = nueva
        aj.guardar("politica", nueva)
        self.tick(avanzar=False)
        self.pedir_sondeo()  # los sensores que pasan a real necesitan dato ya

    def aplicar_ajustes(self, nuevos: aj.Ajustes) -> None:
        upstream_cambia = nuevos.upstream != self.ajustes.upstream
        self.ajustes = nuevos
        self.cliente.ajustes = nuevos.upstream
        aj.guardar("ajustes", nuevos)
        if upstream_cambia:
            self.pedir_sondeo()
