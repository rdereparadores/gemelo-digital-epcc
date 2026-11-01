"""Histórico agregado por estancia y por edificio (para las gráficas del gemelo en Unity).

`GET /api/v1/spaces/{id}/history` y `GET /api/v1/buildings/{id}/history` devuelven una serie por propiedad,
agregada igual que `SpaceState` y `BuildingState` (media, suma o máximo, con la misma prioridad de sensores
para la temperatura y la humedad), en cubos de `step_s` segundos.

De dónde salen los datos:
- Simulados: del búfer de 24 h del servicio (`Gemelo.historial`). Si el búfer no cubre la ventana pedida
  (el servicio acaba de arrancar o el reloj de la simulación ha saltado), el tramo que falta se reconstruye
  con otra instancia del simulador que arranca al principio de la ventana, con el mismo escenario y sin
  eventos. Se guarda en una caché pequeña, así que solo la primera consulta tras un salto tarda algo.
- Reales: los sensores que se están sirviendo como reales (o todos los que existen en SmartPoliTech con
  `source=real`) se leen del histórico de SmartPoliTech. Si no devuelve nada a tiempo, con `source=auto`
  se usan los simulados de ese sensor.
"""

from __future__ import annotations

import asyncio
import math
import statistics
from array import array
from collections import OrderedDict
from datetime import datetime, timedelta
from typing import Annotated, Literal

from fastapi import APIRouter, Query, Request

from .api import Problema, _fecha, gemelo, no_encontrado
from .catalogo import PROPIEDADES, Sensor
from .gemelo import MAXIMO, NO_AGREGAR, PRIORIDAD_AMBIENTE, SUMA, Gemelo, iso

router = APIRouter(prefix="/api/v1", tags=["estado"])

MAX_CUBOS = 1440
TIEMPO_REAL_S = 12.0  # tope para las consultas al histórico de SmartPoliTech de una petición

Muestra = tuple[datetime, dict[str, float], str]  # (instante, valores, fuente)


# ---------------------------------------------------------------- agregación (la misma que gemelo.estado_espacio)

def _agregacion(prop: str) -> str:
    return "sum" if prop in SUMA else "max" if prop in MAXIMO else "mean"


def agregar_estancia(muestras: list[tuple[Sensor, dict[str, float], set[str]]], props: list[str]) -> dict[str, tuple[float, set[str]]]:
    """Valor de cada propiedad en una estancia a partir del valor de cada uno de sus sensores."""
    salida: dict[str, tuple[float, set[str]]] = {}
    for prop in props:
        fuentes = [(s, v, f) for s, v, f in muestras if prop in v]
        if not fuentes:
            continue
        if prop in ("air_temperature", "relative_humidity"):
            for kind in PRIORIDAD_AMBIENTE:
                preferidas = [x for x in fuentes if x[0].kind == kind]
                if preferidas:
                    fuentes = preferidas
                    break
        vals = [v[prop] for _, v, _ in fuentes]
        if prop == "occupancy" and len(fuentes) > 1:
            valor = max(vals)
        else:
            agg = _agregacion(prop)
            valor = sum(vals) if agg == "sum" else max(vals) if agg == "max" else statistics.fmean(vals)
        salida[prop] = (valor, set().union(*(f for *_, f in fuentes)))
    return salida


def agregar_edificio(por_estancia: list[tuple[str, dict[str, tuple[float, set[str]]]]]) -> dict[str, tuple[float, set[str]]]:
    """Como gemelo.estado_edificio: la ocupación se suma por estancias; wifi y fotovoltaica, solo las del edificio."""
    acum: dict[str, list[tuple[float, set[str]]]] = {}
    for tipo, metricas in por_estancia:
        de_edificio = tipo == "building"
        for p, x in metricas.items():
            if (p in ("wifi_clients", "solar_power") and de_edificio) or (p == "occupancy" and not de_edificio) \
                    or p not in ("wifi_clients", "solar_power", "occupancy"):
                acum.setdefault(p, []).append(x)
    salida = {}
    for p, vals in acum.items():
        nums = [v for v, _ in vals]
        agg = "sum" if p == "occupancy" else _agregacion(p)
        valor = sum(nums) if agg == "sum" else max(nums) if agg == "max" else statistics.fmean(nums)
        salida[p] = (valor, set().union(*(f for _, f in vals)))
    return salida


# ---------------------------------------------------------------- búfer simulado y reconstrucción

def _cola(g: Gemelo, sensor_id: str, limite: timedelta) -> list:
    """Tramo continuo más reciente del búfer de un sensor (un salto de reloj corta la línea temporal)."""
    h = g.historial.get(sensor_id)
    if not h:
        return []
    tramo = [h[-1]]
    for o in reversed(h):
        if o is tramo[-1]:
            continue
        previa = tramo[-1].time
        if o.time >= previa or previa - o.time > limite:
            break
        tramo.append(o)
    tramo.reverse()
    return tramo


class Historico:
    """Caché de reconstrucciones del simulado y del histórico real de SmartPoliTech."""

    def __init__(self, g: Gemelo) -> None:
        self.g = g
        self._reconstrucciones: OrderedDict[tuple, tuple[datetime, dict[str, dict[str, array]]]] = OrderedDict()
        self._reales: OrderedDict[tuple, tuple[datetime, list[Muestra]]] = OrderedDict()
        self._lock = asyncio.Lock()

    @staticmethod
    def de(g: Gemelo) -> Historico:
        h = getattr(g, "_historico_agregado", None)
        if h is None:
            h = Historico(g)
            g._historico_agregado = h  # type: ignore[attr-defined]
        return h

    def _limite_hueco(self) -> timedelta:
        sim = self.g.ajustes.simulation
        return timedelta(seconds=max(3 * sim.history_interval_s, 3 * sim.tick_s * max(1.0, self.g.time_scale)))

    async def simulados(self, sensores: list[Sensor], desde: datetime, hasta: datetime) -> tuple[dict[str, list[Muestra]], datetime | None]:
        """Muestras simuladas de cada sensor en [desde, hasta] y hasta dónde llega la reconstrucción (o None)."""
        g = self.g
        limite = self._limite_hueco()
        colas = {s.id: _cola(g, s.id, limite) for s in sensores}
        inicios = [c[0].time for c in colas.values() if c]
        inicio_tramo = min(inicios) if inicios else hasta
        salida = {sid: [(o.time, o.values, "simulated") for o in c if desde <= o.time <= hasta] for sid, c in colas.items()}
        paso = timedelta(seconds=g.ajustes.simulation.history_interval_s)
        if inicio_tramo - desde <= paso * 1.5:
            return salida, None
        # Una reconstrucción de 24 h para todo el campus por línea temporal (cambia con cada salto de reloj)
        fin = datetime.fromtimestamp(math.ceil(inicio_tramo.timestamp() / paso.total_seconds()) * paso.total_seconds(),
                                     inicio_tramo.tzinfo)
        k = (g.clock, fin)
        rec = self._reconstrucciones.get(k)
        if rec is None:
            async with self._lock:
                rec = self._reconstrucciones.get(k)
                if rec is None:
                    rec = await asyncio.to_thread(_reconstruir, type(g.sim), g.catalogo, g.ajustes.simulation.seed,
                                                  g.sim.escenario, fin - timedelta(hours=24), fin, paso)
                    self._reconstrucciones[k] = rec
                    while len(self._reconstrucciones) > 2:
                        self._reconstrucciones.popitem(last=False)
        t0, series = rec
        corte = min(inicio_tramo, hasta)
        for s in sensores:
            por_prop = series.get(s.id)
            if not por_prop:
                continue
            previas = []
            nk = len(next(iter(por_prop.values())))
            for i in range(nk):
                t = t0 + i * paso
                if t < desde or t >= corte:
                    continue
                valores = {p: a[i] for p, a in por_prop.items() if not math.isnan(a[i])}
                if valores:
                    previas.append((t, valores, "simulated"))
            salida[s.id] = previas + salida.get(s.id, [])
        # La reconstrucción cubre 24 h antes del búfer: una ventana más antigua queda vacía y no se puede decir
        # que se haya reconstruido (antes devolvía reconstructed_until con todas las series vacías)
        return salida, (corte if hasta >= t0 else None)

    async def reales(self, sensores: list[Sensor], desde: datetime, hasta: datetime) -> dict[str, list[Muestra]]:
        """Histórico real de SmartPoliTech de cada sensor (los que no responden a tiempo se quedan fuera)."""
        g = self.g
        salida: dict[str, list[Muestra]] = {}
        ahora = datetime.now(desde.tzinfo)

        async def uno(s: Sensor) -> None:
            k = (s.id, int(desde.timestamp() // 60), int(hasta.timestamp() // 60))
            if (c := self._reales.get(k)) and (ahora - c[0]).total_seconds() < 120:
                salida[s.id] = c[1]
                return
            lecturas = await g.cliente.historico(s, desde, hasta, 5000)
            muestras = [(r.time, r.values, "real") for r in reversed(lecturas)]
            self._reales[k] = (ahora, muestras)
            while len(self._reales) > 512:
                self._reales.popitem(last=False)
            salida[s.id] = muestras

        tareas = [asyncio.create_task(uno(s)) for s in sensores]
        if tareas:
            hechas, pendientes = await asyncio.wait(tareas, timeout=TIEMPO_REAL_S)
            for t in pendientes:
                t.cancel()
            for t in hechas:
                if t.exception() is not None:
                    pass  # SmartPoliTech caído o sin datos: se queda fuera
        return salida


def _reconstruir(clase, catalogo, semilla: int, escenario: str, desde: datetime, hasta: datetime,
                 paso: timedelta) -> tuple[datetime, dict[str, dict[str, array]]]:
    """Simula de nuevo [desde, hasta) con otra instancia del motor (arranque en caliente en `desde`, sin eventos)."""
    sim = clase(catalogo, semilla)
    try:
        sim.poner_escenario(escenario)
    except KeyError:
        pass
    n = math.ceil((hasta - desde) / paso)
    sensores = list(catalogo.sensores.values())
    series = {s.id: {p: array("d", [math.nan]) * n for p in s.properties if p not in NO_AGREGAR} for s in sensores}
    t = desde
    sim.avanzar(t, 0.0, None)
    for i in range(n):
        for s in sensores:
            v = sim.lectura(s.id)
            if v is not None:
                for p, a in series[s.id].items():
                    if p in v:
                        a[i] = v[p]
        t += paso
        sim.avanzar(t, paso.total_seconds(), None)
    return desde, series


# ---------------------------------------------------------------- consulta

def _cubos(muestras: list[Muestra], desde: datetime, paso_s: float, n: int, props: set[str]) -> dict[int, tuple[dict[str, float], set[str]]]:
    """Valor de un sensor en cada cubo: media de sus lecturas (máximo para los booleanos de alarma, ventana…)."""
    acum: dict[int, dict[str, list[float]]] = {}
    fuentes: dict[int, set[str]] = {}
    for t, valores, fuente in muestras:
        i = int((t - desde).total_seconds() // paso_s)
        if i == n:
            i = n - 1  # el instante final cae en el último cubo
        if not 0 <= i < n:
            continue
        d = acum.setdefault(i, {})
        for p, v in valores.items():
            if p in props:
                d.setdefault(p, []).append(v)
        fuentes.setdefault(i, set()).add(fuente)
    return {i: ({p: (max(vs) if p in MAXIMO else statistics.fmean(vs)) for p, vs in d.items()}, fuentes[i])
            for i, d in acum.items() if d}


async def historial(g: Gemelo, espacios: list, por_edificio: bool, properties: str | None, hours: float,
                    step_s: float, to: str | None, source: str) -> dict:
    sensores = [s for e in espacios for s in g.catalogo.sensores_de_espacio(e.id)]
    presentes = {p for s in sensores for p in s.properties} - NO_AGREGAR
    if properties:
        props = [p.strip() for p in properties.split(",") if p.strip()]
        malas = [p for p in props if p not in PROPIEDADES]
        if malas:
            raise Problema(422, "Propiedad desconocida", f"{', '.join(malas)}; ver /api/v1/observed-properties.", "/problems/validation")
        props = [p for p in props if p not in NO_AGREGAR]
    else:
        props = sorted(presentes)
    n = math.ceil(hours * 3600 / step_s)
    if n > MAX_CUBOS:
        raise Problema(422, "Demasiados puntos", f"hours/step_s da {n} cubos (máximo {MAX_CUBOS}).", "/problems/validation")
    hasta = _fecha(to, "to") or g.sim_time
    desde = hasta - timedelta(seconds=n * step_s)
    pset = set(props)
    sensores = [s for s in sensores if pset.intersection(s.properties)]

    h = Historico.de(g)
    muestras: dict[str, list[Muestra]] = {}
    reconstruido = None
    if source != "real":
        muestras, reconstruido = await h.simulados(sensores, desde, hasta)
    if source != "simulated":
        candidatos = [s for s in sensores if s.real_available and (source == "real" or g.fuente_efectiva(s) == "real")]
        for sid, m in (await h.reales(candidatos, desde, hasta)).items():
            if m:
                muestras[sid] = m  # el real manda sobre el simulado de ese sensor
    if source == "real":
        muestras = {sid: m for sid, m in muestras.items() if m and m[0][2] == "real"}

    # Cubo a cubo: valor de cada sensor → estancia → (edificio)
    por_sensor = {s.id: _cubos(muestras.get(s.id, []), desde, step_s, n, pset) for s in sensores}
    sensores_de: dict[str, list[Sensor]] = {}
    for s in sensores:
        sensores_de.setdefault(s.space_id, []).append(s)
    series: dict[str, list[dict]] = {p: [] for p in props}
    fuentes_serie: dict[str, set[str]] = {p: set() for p in props}
    for i in range(n):
        por_estancia = []
        for e in espacios:
            m = [(s, *por_sensor[s.id][i]) for s in sensores_de.get(e.id, []) if i in por_sensor[s.id]]
            if m:
                por_estancia.append((e.type, agregar_estancia(m, props)))
        if not por_estancia:
            continue
        valores = agregar_edificio(por_estancia) if por_edificio else por_estancia[0][1]
        t = iso(desde + timedelta(seconds=i * step_s))
        for p, (v, f) in valores.items():
            if p in series:
                series[p].append({"time": t, "value": round(v, 2)})
                fuentes_serie[p] |= f
    return {
        "from": iso(desde), "to": iso(hasta), "step_s": step_s, "source": source,
        "reconstructed_until": iso(reconstruido),
        "series": [{"property": p, "unit": PROPIEDADES[p].unit, "aggregation": _agregacion(p) if p != "occupancy" else "sum",
                    "source": (next(iter(fuentes_serie[p])) if len(fuentes_serie[p]) == 1 else "mixed" if fuentes_serie[p] else "none"),
                    "points": series[p]} for p in props],
    }


Props = Annotated[str | None, Query(description="Propiedades separadas por comas (por defecto, todas las que haya)")]
Horas = Annotated[float, Query(gt=0, le=24, description="Horas hacia atrás desde `to`")]
Paso = Annotated[float, Query(ge=60, le=3600, description="Segundos por punto")]
Hasta = Annotated[str | None, Query(description="ISO 8601; por defecto, la hora de la simulación")]
Fuente = Annotated[Literal["auto", "simulated", "real"], Query(description="auto: real donde se sirve real, simulado en el resto")]


@router.get("/spaces/{space_id}/history", summary="Histórico agregado de una estancia")
async def historial_espacio(request: Request, space_id: str, properties: Props = None, hours: Horas = 6, step_s: Paso = 300,
                            to: Hasta = None, source: Fuente = "auto") -> dict:
    g = gemelo(request)
    if space_id not in g.catalogo.espacios:
        raise no_encontrado("Estancia", space_id)
    doc = await historial(g, [g.catalogo.espacios[space_id]], False, properties, hours, step_s, to, source)
    return {"space_id": space_id, **doc}


@router.get("/buildings/{building_id}/history", summary="Histórico agregado de un edificio")
async def historial_edificio(request: Request, building_id: str, properties: Props = None, hours: Horas = 6, step_s: Paso = 300,
                             to: Hasta = None, source: Fuente = "auto") -> dict:
    g = gemelo(request)
    if building_id not in g.catalogo.edificios:
        raise no_encontrado("Edificio", building_id)
    espacios = [e for e in g.catalogo.espacios.values() if e.building_id == building_id]
    doc = await historial(g, espacios, True, properties, hours, step_s, to, source)
    return {"building_id": building_id, **doc}
