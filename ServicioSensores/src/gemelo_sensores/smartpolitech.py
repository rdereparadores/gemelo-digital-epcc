"""Cliente de los datos reales de SmartPoliTech.

SmartPoliTech publica un Grafana con acceso anónimo de lectura y sus paneles consultan un InfluxDB
1.x a través del proxy de fuentes de datos (`/api/datasources/proxy/<id>/query`). Este cliente usa
esa misma vía: consultas InfluxQL de solo lectura, siempre acotadas en el tiempo, agrupadas en pocas
peticiones y en serie, para no cargar un servidor que no es nuestro.
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from datetime import UTC, datetime
from zoneinfo import ZoneInfo

import httpx

from .ajustes import AjustesUpstream
from .catalogo import Sensor

log = logging.getLogger(__name__)
MADRID = ZoneInfo("Europe/Madrid")
CONSULTAS_SIMULTANEAS = 4
CUARENTENA_SONDEOS = 10
# Rangos válidos: las válvulas con la sonda rota mandan -40 °C y 0 % de humedad
RANGOS = {"air_temperature": (-30, 60), "relative_humidity": (0.5, 100), "co2_concentration": (250, 10000),
          "water_temperature": (0, 110), "target_temperature": (5, 35)}


def _valido(prop: str, v: float | None) -> bool:
    if v is None:
        return False
    lo, hi = RANGOS.get(prop, (float("-inf"), float("inf")))
    return lo <= v <= hi


@dataclass
class LecturaReal:
    time: datetime
    values: dict[str, float]


@dataclass
class EstadoEnlace:
    reachable: bool | None = None
    last_poll: datetime | None = None
    last_success: datetime | None = None
    latency_ms: float | None = None
    poll_duration_ms: float | None = None
    sensors_polled: int = 0
    sensors_with_data: int = 0
    errors: list[dict] = field(default_factory=list)

    def error(self, mensaje: str) -> None:
        self.errors.insert(0, {"time": datetime.now(UTC), "message": mensaje})
        del self.errors[20:]


def _campo(expr: str) -> list[str]:
    """Campos de InfluxDB que usa una expresión del catálogo ('a+b|transformación')."""
    return expr.split("|")[0].split("+")


def _transformar(expr: str, fila: dict) -> float | None:
    base, _, tr = expr.partition("|")
    crudos = [fila.get(c) for c in base.split("+")]
    if any(v is None for v in crudos):
        return None
    try:
        v = sum(float(x) for x in crudos)
    except (TypeError, ValueError):
        return None
    match tr:
        case "" | "num":
            return v
        case "bool":
            return 1.0 if v > 0 else 0.0
        case "valvula":  # válvulas MClimate Vicki: apertura = (1 - posición / recorrido) · 100
            rango = fila.get("motorRange")
            return round(max(0.0, min(100.0, (1 - v / float(rango)) * 100)), 1) if rango else None
        case "persiana":
            return 100.0 if v > 0 else 0.0
        case "kwh_desde_wh":
            return v / 1000
        case "presencia":
            return 1.0
        case "agua_hoy":  # se resuelve aparte con una suma desde medianoche
            return v
    return v


class ClienteSmartPoliTech:
    def __init__(self, ajustes: AjustesUpstream) -> None:
        self.ajustes = ajustes
        self.estado = EstadoEnlace()
        self.cache: dict[str, LecturaReal] = {}
        self._http = httpx.AsyncClient(headers={"User-Agent": "gemelo-epcc/0.1 (TFG, Universidad de Extremadura)"})
        self._lock = asyncio.Lock()
        self._cuarentena: dict[str, int] = {}  # medición -> sondeos que se salta

    async def cerrar(self) -> None:
        await self._http.aclose()

    async def _consultar(self, sentencias: list[str]) -> list[dict]:
        a = self.ajustes
        t0 = time.perf_counter()
        r = await self._http.get(
            f"{a.base_url.rstrip('/')}/api/datasources/proxy/{a.datasource_id}/query",
            params={"db": a.database, "q": ";".join(sentencias), "epoch": "s"},
            timeout=a.timeout_s,
        )
        self.estado.latency_ms = round((time.perf_counter() - t0) * 1000, 1)
        r.raise_for_status()
        return r.json()["results"]

    async def sondear(self, sensores: list[Sensor]) -> int:
        """Pide el último dato de cada sensor (dentro de la ventana de frescura). Devuelve cuántos tienen dato."""
        async with self._lock:
            return await self._sondear(sensores)

    async def _sondear(self, sensores: list[Sensor]) -> int:
        t0 = time.perf_counter()
        self.estado.last_poll = datetime.now(UTC)
        por_medicion: dict[str, list[Sensor]] = {}
        for s in sensores:
            if s.upstream:
                por_medicion.setdefault(s.upstream["measurement"], []).append(s)
        ventana = int(max(self.ajustes.lookback_s, self.ajustes.stale_after_s))
        medianoche = datetime.now(MADRID).replace(hour=0, minute=0, second=0, microsecond=0).astimezone(UTC)

        sentencias: list[tuple[str, str, str]] = []  # (medición, tipo de consulta, sentencia)
        for m, ss in por_medicion.items():
            if self._cuarentena.get(m, 0) > 0:
                self._cuarentena[m] -= 1
                continue
            # SELECT * y no una lista de campos: en algunas mediciones elegir campos cuelga el InfluxDB
            sentencias.append((m, "ultimo", f'SELECT * FROM "{m}" WHERE time > now() - {ventana}s ORDER BY time DESC LIMIT 1'))
            agua = [c for s in ss for e in s.upstream["fields"].values() if e.endswith("|agua_hoy") for c in _campo(e)]
            if agua:
                suma = " + ".join(f'sum("{c}")' for c in sorted(set(agua)))
                sentencias.append((m, "agua", f'SELECT ({suma}) * 10 AS litros FROM "{m}" WHERE time >= \'{medianoche:%Y-%m-%dT%H:%M:%SZ}\''))

        filas: dict[str, dict] = {}
        litros: dict[str, float] = {}
        # Una sentencia por petición y pocas a la vez: varias sentencias en una misma petición hacen que el
        # proxy de Grafana se cuelgue, aunque cada una por separado responda en ~0,1 s.
        sem = asyncio.Semaphore(CONSULTAS_SIMULTANEAS)

        async def una(m: str, q: str) -> dict:
            async with sem:
                try:
                    return (await self._consultar([q]))[0]
                except (httpx.HTTPError, KeyError, ValueError, IndexError) as e:
                    self._cuarentena[m] = CUARENTENA_SONDEOS
                    self.estado.error(f"{m}: {type(e).__name__}; en cuarentena {CUARENTENA_SONDEOS} sondeos")
                    return {"error": None}

        resultados = await asyncio.gather(*(una(m, q) for m, _, q in sentencias))
        ok = any("error" not in r for r in resultados)
        for (m, tipo, _), res in zip(sentencias, resultados, strict=True):
            if "error" in res:
                if res["error"]:
                    self.estado.error(f"{m}: {res['error']}"[:300])
                continue
            serie = (res.get("series") or [None])[0]
            if not serie:
                continue
            fila = dict(zip(serie["columns"], serie["values"][0], strict=True))
            if tipo == "ultimo":
                filas[m] = fila
            elif fila.get("litros") is not None:
                litros[m] = float(fila["litros"])

        self.estado.reachable = ok or not sentencias
        if ok:
            self.estado.last_success = datetime.now(UTC)
        con_dato = 0
        for m, ss in por_medicion.items():
            fila = filas.get(m)
            if not fila:
                continue
            t = datetime.fromtimestamp(fila["time"], UTC)
            for s in ss:
                valores = {}
                for prop, expr in s.upstream["fields"].items():
                    v = litros.get(m) if expr.endswith("|agua_hoy") else _transformar(expr, fila)
                    if _valido(prop, v):
                        valores[prop] = round(v, 3)
                if valores:
                    self.cache[s.id] = LecturaReal(t, valores)
                    con_dato += 1
        self.estado.sensors_polled = len(sensores)
        self.estado.sensors_with_data = con_dato
        self.estado.poll_duration_ms = round((time.perf_counter() - t0) * 1000, 1)
        return con_dato

    async def historico(self, sensor: Sensor, desde: datetime, hasta: datetime, limite: int) -> list[LecturaReal]:
        """Lecturas reales entre dos instantes, la más reciente primero."""
        if not sensor.upstream:
            return []
        m, mapa = sensor.upstream["measurement"], sensor.upstream["fields"]
        if any(e.endswith("|agua_hoy") for e in mapa.values()):
            return []  # el contador de agua solo tiene sentido como acumulado del día
        q = (f'SELECT * FROM "{m}" WHERE time >= \'{desde:%Y-%m-%dT%H:%M:%SZ}\' '
             f'AND time <= \'{hasta:%Y-%m-%dT%H:%M:%SZ}\' ORDER BY time DESC LIMIT {int(limite)}')
        async with self._lock:
            res = (await self._consultar([q]))[0]
        serie = (res.get("series") or [None])[0]
        if not serie:
            return []
        salida = []
        for vals in serie["values"]:
            fila = dict(zip(serie["columns"], vals))
            valores = {p: round(v, 3) for p, e in mapa.items() if _valido(p, v := _transformar(e, fila))}
            if valores:
                salida.append(LecturaReal(datetime.fromtimestamp(fila["time"], UTC), valores))
        return salida
