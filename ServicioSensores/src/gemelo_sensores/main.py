"""Aplicación FastAPI: API REST v1, panel de control estático y tareas de fondo."""

from __future__ import annotations

import logging
import os
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.gzip import GZipMiddleware
from fastapi.responses import JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from starlette.exceptions import HTTPException as StarletteHTTPException

from .api import Problema, router
from .gemelo import Gemelo
from .historico import router as router_historico

PANEL = Path(__file__).resolve().parents[2] / "panel"
PROBLEMA = "application/problem+json"

DESCRIPCION = """
Puente entre los sensores de **SmartPoliTech** (Escuela Politécnica de Cáceres, UEx) y el gemelo
digital del campus en Unity.

Cada sensor sirve datos **reales** (SmartPoliTech), **simulados** (motor propio) o **auto** (real si hay
un dato reciente; si no, simulado), según la [política de fuentes](#/fuentes/get_source_policy).

- Errores en `application/problem+json` (RFC 9457).
- Colecciones paginadas con `limit`/`offset` y cabecera `Link` (RFC 8288).
- `PATCH` con JSON Merge Patch (RFC 7396).
- Estado con `ETag` / `If-None-Match`, para sondear barato desde Unity.
- Observaciones en directo por Server-Sent Events.
"""


def crear_app(gemelo: Gemelo | None = None, tareas: bool = True) -> FastAPI:
    @asynccontextmanager
    async def vida(app: FastAPI):
        app.state.gemelo = gemelo or Gemelo()
        if tareas:
            await app.state.gemelo.arrancar()
        yield
        await app.state.gemelo.parar()

    app = FastAPI(
        title="Gemelo digital EPCC: API de sensores",
        version="1.0.0",
        description=DESCRIPCION,
        lifespan=vida,
        openapi_url="/api/v1/openapi.json",
        docs_url="/api/v1/docs",
        redoc_url="/api/v1/redoc",
        license_info={"name": "Datos: SmartPoliTech (UEx)"},
    )
    app.add_middleware(GZipMiddleware, minimum_size=2048)  # /state pesa ~300 kB sin comprimir
    app.add_middleware(
        CORSMiddleware,
        allow_origins=os.environ.get("GEMELO_CORS", "*").split(","),
        allow_methods=["*"],
        allow_headers=["*"],
        expose_headers=["ETag", "Link", "Location", "X-Total-Count", "X-Data-Source"],
    )

    def problema(request: Request, status: int, title: str, detail: str | None, tipo: str = "about:blank") -> JSONResponse:
        cuerpo = {"type": tipo, "title": title, "status": status, "instance": request.url.path}
        if detail:
            cuerpo["detail"] = detail
        return JSONResponse(cuerpo, status_code=status, media_type=PROBLEMA)

    @app.exception_handler(Problema)
    async def _problema(request: Request, e: Problema):
        return problema(request, e.status_code, e.title, e.detail, e.tipo)

    @app.exception_handler(StarletteHTTPException)
    async def _http(request: Request, e: StarletteHTTPException):
        titulos = {404: "No encontrado", 405: "Método no permitido"}
        return problema(request, e.status_code, titulos.get(e.status_code, "Error"), str(e.detail) if e.detail else None)

    @app.exception_handler(RequestValidationError)
    async def _validacion(request: Request, e: RequestValidationError):
        detalle = "; ".join(f"{'.'.join(map(str, x['loc']))}: {x['msg']}" for x in e.errors())
        return problema(request, 422, "Datos no válidos", detalle, "/problems/validation")

    app.include_router(router)
    app.include_router(router_historico)

    @app.middleware("http")
    async def _sin_cache_panel(request: Request, call_next):
        respuesta = await call_next(request)
        if request.url.path.startswith("/panel"):
            respuesta.headers["Cache-Control"] = "no-cache"  # módulos ES: evita servir versiones viejas
        return respuesta

    @app.get("/", include_in_schema=False)
    def inicio():
        return RedirectResponse("/panel/")

    if PANEL.exists():
        app.mount("/panel", StaticFiles(directory=PANEL, html=True), name="panel")
    return app


def main() -> None:
    import argparse

    import uvicorn

    ap = argparse.ArgumentParser(description="Servicio REST del gemelo digital de la EPCC")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8800)
    args = ap.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    logging.getLogger("httpx").setLevel(logging.WARNING)  # una línea por consulta a SmartPoliTech es demasiado
    uvicorn.run(crear_app(), host=args.host, port=args.port, proxy_headers=True)
