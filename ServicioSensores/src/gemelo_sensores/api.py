"""Rutas de la API REST v1 (ver docs/CONTRATO_API.md)."""

from __future__ import annotations

import asyncio
import json
from datetime import UTC, datetime, timedelta
from typing import Annotated, Any, Literal

from fastapi import APIRouter, Body, Depends, HTTPException, Query, Request, Response
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel, Field, ValidationError

from . import ajustes as aj
from .catalogo import PROPIEDADES
from .gemelo import Gemelo, Observacion, iso

router = APIRouter(prefix="/api/v1")
JSON_PATCH = {"application/merge-patch+json": {"schema": {"type": "object"}}, "application/json": {"schema": {"type": "object"}}}

TIPOS_EVENTO = ["open_windows", "close_windows", "class_start", "class_end", "heating_off", "heating_on",
                "fire_alarm", "power_outage", "water_leak", "sensor_failure"]


def gemelo(request: Request) -> Gemelo:
    return request.app.state.gemelo


G = Annotated[Gemelo, Depends(gemelo)]


class Problema(HTTPException):
    """Error que se serializa como application/problem+json (RFC 9457)."""

    def __init__(self, status: int, title: str, detail: str | None = None, tipo: str = "about:blank") -> None:
        super().__init__(status_code=status, detail=detail)
        self.title, self.tipo = title, tipo


FEMENINOS = {"Estancia"}


def no_encontrado(que: str, id_: str) -> Problema:
    a = "a" if que in FEMENINOS else "o"
    ningun = "ninguna" if a == "a" else "ningún"
    return Problema(404, f"{que} no encontrad{a}", f"No existe {ningun} {que.lower()} con id '{id_}'.", "/problems/not-found")


# ---------------------------------------------------------------- utilidades REST

def paginar(request: Request, response: Response, items: list, limit: int, offset: int, a_json=lambda x: x) -> dict:
    total = len(items)
    pagina = items[offset : offset + limit]
    enlaces = []
    url = request.url
    if offset + limit < total:
        enlaces.append(f'<{url.include_query_params(offset=offset + limit, limit=limit)}>; rel="next"')
    if offset > 0:
        enlaces.append(f'<{url.include_query_params(offset=max(0, offset - limit), limit=limit)}>; rel="prev"')
    if enlaces:
        response.headers["Link"] = ", ".join(enlaces)
    response.headers["X-Total-Count"] = str(total)
    return {"items": [a_json(x) for x in pagina], "total": total, "limit": limit, "offset": offset}


Limit = Annotated[int, Query(ge=1, le=1000, description="Elementos por página")]
Offset = Annotated[int, Query(ge=0, description="Desplazamiento")]


def con_etag(request: Request, doc: Any, etag: str | None = None) -> Response:
    cuerpo = json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode()
    if etag is None:
        import hashlib

        etag = '"' + hashlib.sha1(cuerpo).hexdigest()[:20] + '"'
    cabeceras = {"ETag": etag, "Cache-Control": "no-cache"}
    if request.headers.get("if-none-match") == etag:
        return Response(status_code=304, headers=cabeceras)
    return Response(cuerpo, media_type="application/json", headers=cabeceras)


async def leer_parche(request: Request) -> dict:
    tipo = request.headers.get("content-type", "").split(";")[0].strip()
    if tipo not in ("application/merge-patch+json", "application/json"):
        raise Problema(415, "Tipo de contenido no admitido", "Usa application/merge-patch+json o application/json.")
    try:
        parche = json.loads(await request.body() or b"{}")
    except json.JSONDecodeError as e:
        raise Problema(400, "JSON mal formado", str(e)) from e
    if not isinstance(parche, dict):
        raise Problema(400, "El parche debe ser un objeto JSON")
    return parche


def validar[T: BaseModel](tipo: type[T], datos: dict) -> T:
    try:
        return tipo.model_validate(datos)
    except ValidationError as e:
        raise Problema(422, "Datos no válidos", "; ".join(f"{'.'.join(map(str, x['loc']))}: {x['msg']}" for x in e.errors()),
                       "/problems/validation") from e


# ---------------------------------------------------------------- raíz y sistema

@router.get("", summary="Raíz de la API", tags=["sistema"])
def raiz() -> dict:
    return {
        "name": "Gemelo digital EPCC: API de sensores",
        "version": "1.0.0",
        "links": {k: f"/api/v1/{k}" for k in [
            "buildings", "spaces", "sensors", "observed-properties", "observations/latest", "observations/stream",
            "state", "source-policy", "settings", "simulation", "simulation/scenarios", "simulation/events",
            "upstream", "health", "docs", "openapi.json"]},
    }


@router.get("/health", summary="Salud del servicio", tags=["sistema"])
def salud(g: G) -> JSONResponse:
    enlace = g.cliente.estado
    upstream_ok = not g.ajustes.upstream.enabled or enlace.reachable is not False
    retraso_tick = (datetime.now(UTC) - g._ultimo_tick_real).total_seconds() if g._ultimo_tick_real else 0
    sim_ok = retraso_tick < g.ajustes.simulation.tick_s * 4 + 5
    status = "ok" if upstream_ok and sim_ok else "degraded"
    cuerpo = {"status": status, "checks": {
        "simulation": {"status": "ok" if sim_ok else "fail", "last_tick": iso(g._ultimo_tick_real)},
        "upstream": {"status": "ok" if upstream_ok else "fail", "enabled": g.ajustes.upstream.enabled,
                     "last_success": iso(enlace.last_success)},
    }}
    return JSONResponse(cuerpo, status_code=200 if sim_ok else 503)


@router.get("/upstream", summary="Estado del enlace con SmartPoliTech", tags=["sistema"])
def upstream(g: G) -> dict:
    e = g.cliente.estado
    reales = [s for s in g.catalogo.sensores.values() if s.real_available]
    frescos = sum(1 for s in reales if g.real_fresca(s))
    return {
        "base_url": g.ajustes.upstream.base_url, "enabled": g.ajustes.upstream.enabled, "reachable": e.reachable,
        "last_poll": iso(e.last_poll), "last_success": iso(e.last_success), "latency_ms": e.latency_ms,
        "poll_duration_ms": e.poll_duration_ms, "poll_interval_s": g.ajustes.upstream.poll_interval_s,
        "next_poll": iso(e.last_poll + timedelta(seconds=g.ajustes.upstream.poll_interval_s)) if e.last_poll else None,
        "stale_after_s": g.ajustes.upstream.stale_after_s,
        "real_sensors": len(reales), "polled_sensors": e.sensors_polled,
        "sensors_with_data": len(g.cliente.cache), "fresh_sensors": frescos,
        "quarantined_measurements": sorted(m for m, n in g.cliente._cuarentena.items() if n > 0),
        "errors": [{"time": iso(x["time"]), "message": x["message"]} for x in e.errors],
    }


@router.post("/upstream/refresh", status_code=202, summary="Forzar un sondeo de SmartPoliTech", tags=["sistema"])
def refrescar(g: G) -> dict:
    g.pedir_sondeo()
    return {"status": "accepted", "links": {"upstream": "/api/v1/upstream"}}


# ---------------------------------------------------------------- catálogo

def edificio_json(g: Gemelo, b) -> dict:
    return {
        "id": b.id, "name": b.name, "unity_id": b.unity_id, "floors": b.floors,
        "space_count": sum(1 for e in g.catalogo.espacios.values() if e.building_id == b.id),
        "sensor_count": sum(1 for s in g.catalogo.sensores.values() if s.building_id == b.id),
        "links": {"self": f"/api/v1/buildings/{b.id}", "spaces": f"/api/v1/spaces?building={b.id}",
                  "sensors": f"/api/v1/sensors?building={b.id}", "state": f"/api/v1/buildings/{b.id}/state"},
    }


def espacio_json(g: Gemelo, e) -> dict:
    return {
        "id": e.id, "building_id": e.building_id, "floor": e.floor, "type": e.type, "code": e.code, "name": e.name,
        "unity_room": e.unity_room, "area_m2": e.area_m2,
        "sensor_count": len(g.catalogo.sensores_de_espacio(e.id)),
        "links": {"self": f"/api/v1/spaces/{e.id}", "building": f"/api/v1/buildings/{e.building_id}",
                  "sensors": f"/api/v1/sensors?space={e.id}", "state": f"/api/v1/spaces/{e.id}/state"},
    }


@router.get("/buildings", summary="Edificios", tags=["catálogo"])
def edificios(g: G, request: Request, response: Response, limit: Limit = 100, offset: Offset = 0) -> dict:
    return paginar(request, response, list(g.catalogo.edificios.values()), limit, offset, lambda b: edificio_json(g, b))


@router.get("/buildings/{building_id}", summary="Edificio", tags=["catálogo"])
def edificio(g: G, building_id: str) -> dict:
    if building_id not in g.catalogo.edificios:
        raise no_encontrado("Edificio", building_id)
    return edificio_json(g, g.catalogo.edificios[building_id])


@router.get("/spaces", summary="Estancias", tags=["catálogo"])
def espacios(g: G, request: Request, response: Response, building: str | None = None, floor: str | None = None,
             type: str | None = None, has_unity_room: bool | None = None, q: str | None = None,
             limit: Limit = 100, offset: Offset = 0) -> dict:
    items = [e for e in g.catalogo.espacios.values()
             if (not building or e.building_id == building) and (not floor or e.floor == floor)
             and (not type or e.type == type) and (has_unity_room is None or bool(e.unity_room) == has_unity_room)
             and (not q or q.lower() in f"{e.id} {e.name or ''} {e.unity_room or ''}".lower())]
    return paginar(request, response, items, limit, offset, lambda e: espacio_json(g, e))


@router.get("/spaces/{space_id}", summary="Estancia", tags=["catálogo"])
def espacio(g: G, space_id: str) -> dict:
    if space_id not in g.catalogo.espacios:
        raise no_encontrado("Estancia", space_id)
    return espacio_json(g, g.catalogo.espacios[space_id])


@router.get("/sensors", summary="Sensores", tags=["catálogo"])
def sensores(g: G, request: Request, response: Response, building: str | None = None, space: str | None = None,
             kind: str | None = None, mode: aj.Modo | None = None,
             source: Literal["real", "simulated", "none"] | None = None, real_available: bool | None = None,
             q: str | None = None, limit: Limit = 100, offset: Offset = 0) -> dict:
    items = [s for s in g.catalogo.sensores.values()
             if (not building or s.building_id == building) and (not space or s.space_id == space)
             and (not kind or s.kind == kind) and (real_available is None or s.real_available == real_available)
             and (not mode or g.modo(s) == mode) and (not source or g.fuente_efectiva(s) == source)
             and (not q or q.lower() in f"{s.id} {s.name}".lower())]
    return paginar(request, response, items, limit, offset, g.sensor_json)


def _sensor(g: Gemelo, sensor_id: str):
    if sensor_id not in g.catalogo.sensores:
        raise no_encontrado("Sensor", sensor_id)
    return g.catalogo.sensores[sensor_id]


@router.get("/sensors/{sensor_id}", summary="Sensor", tags=["catálogo"])
def sensor(g: G, sensor_id: str) -> dict:
    return g.sensor_json(_sensor(g, sensor_id))


class ParcheSensor(BaseModel):
    mode: aj.Modo | None = Field(None, description="Modo propio del sensor; null quita la excepción")


@router.patch("/sensors/{sensor_id}", summary="Cambiar el modo de un sensor", tags=["fuentes"],
              openapi_extra={"requestBody": {"content": JSON_PATCH, "required": True}})
async def parchear_sensor(g: G, sensor_id: str, request: Request) -> dict:
    s = _sensor(g, sensor_id)
    parche = await leer_parche(request)
    if set(parche) - {"mode"}:
        raise Problema(422, "Campo no modificable", f"Solo se puede cambiar 'mode' (recibido: {sorted(parche)}).", "/problems/validation")
    p = validar(ParcheSensor, parche)
    if p.mode in ("real", "auto") and not s.real_available:
        raise Problema(409, "El sensor no existe en SmartPoliTech", f"'{s.id}' es virtual: solo puede servir datos simulados.",
                       "/problems/sensor-virtual")
    nueva = g.politica.model_copy(deep=True)
    if p.mode is None:
        nueva.sensors.pop(s.id, None)
    else:
        nueva.sensors[s.id] = p.mode
    g.aplicar_politica(nueva)
    return g.sensor_json(s)


@router.get("/observed-properties", summary="Propiedades observadas", tags=["catálogo"])
def propiedades() -> dict:
    items = [{"name": p.name, "unit": p.unit, "type": p.type, "description": p.description} for p in PROPIEDADES.values()]
    return {"items": items, "total": len(items), "limit": len(items), "offset": 0}


# ---------------------------------------------------------------- observaciones

@router.get("/sensors/{sensor_id}/observations/latest", summary="Última observación de un sensor", tags=["observaciones"])
def ultima(g: G, sensor_id: str) -> dict:
    s = _sensor(g, sensor_id)
    if s.id not in g.servidas:
        raise Problema(404, "Sin observaciones", f"'{s.id}' no tiene ahora ningún dato (modo {g.modo(s)}).", "/problems/no-data")
    return g.servidas[s.id].a_json()


def _fecha(v: str | None, nombre: str) -> datetime | None:
    if v is None:
        return None
    try:
        d = datetime.fromisoformat(v.replace("Z", "+00:00"))
    except ValueError as e:
        raise Problema(400, "Fecha no válida", f"'{nombre}' debe ser ISO 8601 (recibido '{v}').") from e
    return d if d.tzinfo else d.replace(tzinfo=UTC)


@router.get("/sensors/{sensor_id}/observations", summary="Histórico de un sensor", tags=["observaciones"])
async def historico(g: G, sensor_id: str, request: Request, response: Response,
                    from_: Annotated[str | None, Query(alias="from", description="ISO 8601; por defecto, hace una hora")] = None,
                    to: Annotated[str | None, Query(description="ISO 8601; por defecto, ahora")] = None,
                    source: Annotated[Literal["real", "simulated"] | None, Query(description="Por defecto, la fuente efectiva")] = None,
                    limit: Limit = 500) -> dict:
    s = _sensor(g, sensor_id)
    fuente = source or ("real" if g.fuente_efectiva(s) == "real" else "simulated")
    if fuente == "real":
        hasta = _fecha(to, "to") or datetime.now(UTC)
        desde = _fecha(from_, "from") or hasta - timedelta(hours=1)
        if desde >= hasta:
            raise Problema(400, "Intervalo vacío", "'from' debe ser anterior a 'to'.")
        if not s.real_available:
            raise Problema(409, "El sensor no existe en SmartPoliTech", f"'{s.id}' es virtual.", "/problems/sensor-virtual")
        try:
            lecturas = await g.cliente.historico(s, desde, hasta, limit)
        except Exception as e:  # noqa: BLE001
            raise Problema(502, "SmartPoliTech no responde", f"{type(e).__name__}: {e}", "/problems/upstream") from e
        obs = [Observacion(s.id, r.time, "real", r.values) for r in lecturas]
    else:
        hasta = _fecha(to, "to") or g.sim_time
        desde = _fecha(from_, "from") or hasta - timedelta(hours=1)
        obs = [o for o in reversed(g.historial.get(s.id, ())) if desde <= o.time <= hasta][:limit]
    response.headers["X-Data-Source"] = fuente
    return {"items": [o.a_json() for o in obs], "total": len(obs), "limit": limit, "offset": 0,
            "from": iso(desde), "to": iso(hasta), "source": fuente}


def _filtrar(g: Gemelo, building, space, kind):
    return [s for s in g.catalogo.sensores.values()
            if (not building or s.building_id == building) and (not space or s.space_id == space) and (not kind or s.kind == kind)]


@router.get("/observations/latest", summary="Última observación de cada sensor", tags=["observaciones"])
def ultimas(g: G, request: Request, building: str | None = None, space: str | None = None, kind: str | None = None) -> Response:
    items = [g.servidas[s.id].a_json() for s in _filtrar(g, building, space, kind) if s.id in g.servidas]
    return con_etag(request, {"items": items, "total": len(items), "limit": len(items), "offset": 0})


@router.get("/observations/stream", summary="Observaciones en directo (Server-Sent Events)", tags=["observaciones"],
            response_class=StreamingResponse, responses={200: {"content": {"text/event-stream": {}}}})
async def flujo(g: G, request: Request, building: str | None = None, space: str | None = None, kind: str | None = None):
    sus = g.suscribir(building, space, kind)

    async def eventos():
        try:
            yield "retry: 3000\n\n"
            # Estado inicial: el cliente no tiene que esperar a que algo cambie
            for s in _filtrar(g, building, space, kind):
                if s.id in g.servidas:
                    yield f"event: observation\ndata: {json.dumps(g.servidas[s.id].a_json(), ensure_ascii=False)}\n\n"
            while not await request.is_disconnected():
                try:
                    obs = await asyncio.wait_for(sus.cola.get(), g.ajustes.stream.heartbeat_s)
                    yield f"event: observation\ndata: {json.dumps(obs.a_json(), ensure_ascii=False)}\n\n"
                except TimeoutError:
                    yield ": ping\n\n"
        finally:
            g.desuscribir(sus)

    return StreamingResponse(eventos(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})


# ---------------------------------------------------------------- estado agregado

@router.get("/spaces/{space_id}/state", summary="Estado agregado de una estancia", tags=["estado"])
def estado_espacio(g: G, space_id: str, request: Request) -> Response:
    if space_id not in g.catalogo.espacios:
        raise no_encontrado("Estancia", space_id)
    return con_etag(request, g.estado_espacio(g.catalogo.espacios[space_id]))


@router.get("/buildings/{building_id}/state", summary="Estado agregado de un edificio", tags=["estado"])
def estado_edificio(g: G, building_id: str, request: Request) -> Response:
    if building_id not in g.catalogo.edificios:
        raise no_encontrado("Edificio", building_id)
    return con_etag(request, g.estado_edificio(building_id))


@router.get("/state", summary="Estado de todo el campus (para Unity)", tags=["estado"])
def estado_campus(g: G, request: Request) -> Response:
    doc, etag = g.estado_campus()
    return con_etag(request, doc, etag)


# ---------------------------------------------------------------- política de fuentes y ajustes

@router.get("/source-policy", summary="Política de fuentes", tags=["fuentes"], response_model=aj.PoliticaFuentes)
def politica(g: G) -> aj.PoliticaFuentes:
    return g.politica


def _validar_politica(g: Gemelo, p: aj.PoliticaFuentes) -> None:
    c = g.catalogo
    malos = ([f"buildings.{k}" for k in p.buildings if k not in c.edificios] + [f"spaces.{k}" for k in p.spaces if k not in c.espacios]
             + [f"sensors.{k}" for k in p.sensors if k not in c.sensores]
             + [f"kinds.{k}" for k in p.kinds if k not in {s.kind for s in c.sensores.values()}])
    if malos:
        raise Problema(422, "Claves desconocidas en la política", ", ".join(malos[:20]), "/problems/validation")


@router.put("/source-policy", summary="Reemplazar la política de fuentes", tags=["fuentes"], response_model=aj.PoliticaFuentes)
def poner_politica(g: G, nueva: Annotated[aj.PoliticaFuentes, Body()]) -> aj.PoliticaFuentes:
    _validar_politica(g, nueva)
    g.aplicar_politica(nueva)
    return g.politica


@router.patch("/source-policy", summary="Modificar la política de fuentes (merge patch)", tags=["fuentes"],
              response_model=aj.PoliticaFuentes, openapi_extra={"requestBody": {"content": JSON_PATCH, "required": True}})
async def parchear_politica(g: G, request: Request) -> aj.PoliticaFuentes:
    nueva = validar(aj.PoliticaFuentes, aj.merge_patch(g.politica.model_dump(), await leer_parche(request)))
    _validar_politica(g, nueva)
    g.aplicar_politica(nueva)
    return g.politica


@router.get("/settings", summary="Ajustes", tags=["ajustes"], response_model=aj.Ajustes)
def ajustes(g: G) -> aj.Ajustes:
    return g.ajustes


@router.patch("/settings", summary="Modificar los ajustes (merge patch)", tags=["ajustes"], response_model=aj.Ajustes,
              openapi_extra={"requestBody": {"content": JSON_PATCH, "required": True}})
async def parchear_ajustes(g: G, request: Request) -> aj.Ajustes:
    g.aplicar_ajustes(validar(aj.Ajustes, aj.merge_patch(g.ajustes.model_dump(), await leer_parche(request))))
    return g.ajustes


# ---------------------------------------------------------------- simulación

class EstadoSimulacion(BaseModel):
    running: bool
    time_scale: float = Field(ge=0.1, le=3600)
    clock: str = Field(description="'live' o una fecha ISO 8601 desde la que arranca el reloj simulado")
    sim_time: str | None = None
    model_time: str | None = Field(None, description="Hora que usa el modelo (difiere en escenarios anclados a una fecha)")
    scenario: str
    weather_source: Literal["real", "simulated"]
    weather_in_use: str | None = Field(None, description="Clima que usa de verdad el modelo (real solo si hay datos frescos)")


@router.get("/simulation", summary="Estado de la simulación", tags=["simulación"], response_model=EstadoSimulacion)
def simulacion(g: G) -> dict:
    return g.estado_simulacion()


@router.patch("/simulation", summary="Controlar la simulación (merge patch)", tags=["simulación"], response_model=EstadoSimulacion,
              openapi_extra={"requestBody": {"content": JSON_PATCH, "required": True}})
async def parchear_simulacion(g: G, request: Request) -> dict:
    parche = await leer_parche(request)
    for campo in ("sim_time", "model_time", "weather_in_use"):  # solo lectura
        parche.pop(campo, None)
    nuevo = validar(EstadoSimulacion, aj.merge_patch(g.estado_simulacion(), parche))
    if nuevo.scenario != g.sim.escenario:
        try:
            g.sim.poner_escenario(nuevo.scenario)
        except KeyError as e:
            raise Problema(422, "Escenario desconocido", f"'{nuevo.scenario}' no existe; ver /api/v1/simulation/scenarios.",
                           "/problems/validation") from e
    if "clock" in parche:
        if nuevo.clock == "live":
            g.clock, g.time_scale = "live", 1.0
            g.sim_time = datetime.now(UTC)
        else:
            inicio = _fecha(nuevo.clock, "clock")
            g.clock, g.sim_time = iso(inicio), inicio
        g._ultimo_tick_real = datetime.now(UTC)  # el salto de reloj empieza a contar desde ahora
    if "time_scale" in parche:
        g.time_scale = nuevo.time_scale
        if g.clock == "live" and nuevo.time_scale != 1:
            g.clock = iso(g.sim_time)  # acelerar el tiempo saca el reloj del directo
    g.running = nuevo.running
    g.weather_source = nuevo.weather_source
    g.tick()  # avanza ya, para que la respuesta traiga la hora y el escenario nuevos aplicados
    return g.estado_simulacion()


@router.get("/simulation/scenarios", summary="Escenarios de simulación", tags=["simulación"])
def escenarios(g: G) -> dict:
    items = [{"id": e.id, "name": e.name, "description": e.description, "active": e.id == g.sim.escenario}
             for e in g.sim.escenarios()]
    return {"items": items, "total": len(items), "limit": len(items), "offset": 0}


def evento_json(e) -> dict:
    return {"id": e.id, "type": e.type, "target": e.target, "start": iso(e.start), "duration_s": e.duration_s,
            "params": e.params, "active": e.active, "links": {"self": f"/api/v1/simulation/events/{e.id}"}}


class NuevoEvento(BaseModel):
    type: Literal[tuple(TIPOS_EVENTO)]  # type: ignore[valid-type]
    target: str = Field(description="Id de sensor, estancia o edificio, o 'campus'")
    duration_s: float | None = Field(None, gt=0, description="Sin duración, dura hasta que se borre")
    params: dict = {}


@router.get("/simulation/events", summary="Eventos de simulación", tags=["simulación"])
def eventos(g: G, active: bool | None = None) -> dict:
    items = [evento_json(e) for e in g.sim.eventos() if active is None or e.active == active]
    return {"items": items, "total": len(items), "limit": len(items), "offset": 0}


@router.post("/simulation/events", status_code=201, summary="Inyectar un evento", tags=["simulación"])
def crear_evento(g: G, evento: NuevoEvento, response: Response) -> dict:
    try:
        e = g.sim.crear_evento(evento.type, evento.target, evento.duration_s, evento.params, g.sim_time)
    except ValueError as ex:
        raise Problema(422, "Evento no válido", str(ex), "/problems/validation") from ex
    g.tick(avanzar=False)
    response.headers["Location"] = f"/api/v1/simulation/events/{e.id}"
    return evento_json(e)


@router.get("/simulation/events/{event_id}", summary="Evento de simulación", tags=["simulación"])
def evento(g: G, event_id: str) -> dict:
    for e in g.sim.eventos():
        if e.id == event_id:
            return evento_json(e)
    raise no_encontrado("Evento", event_id)


@router.delete("/simulation/events/{event_id}", status_code=204, summary="Terminar un evento", tags=["simulación"])
def borrar_evento(g: G, event_id: str) -> Response:
    try:
        g.sim.terminar_evento(event_id)
    except KeyError as e:
        raise no_encontrado("Evento", event_id) from e
    g.tick(avanzar=False)
    return Response(status_code=204)
