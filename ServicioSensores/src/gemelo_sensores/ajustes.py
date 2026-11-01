"""Ajustes del servicio y política de fuentes, persistidos en datos/estado/ (fuera de git)."""

from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field

RAIZ = Path(__file__).resolve().parents[2]
DIR_ESTADO = Path(os.environ.get("GEMELO_DIR_ESTADO", RAIZ / "datos" / "estado"))

Modo = Literal["real", "simulated", "auto"]


class AjustesUpstream(BaseModel):
    enabled: bool = True
    base_url: str = Field("http://smartpolitech.unex.es", pattern=r"^https?://[^\s/]+")
    datasource_id: int = 1
    database: str = "sensors"
    poll_interval_s: float = Field(60, ge=10, le=3600)
    timeout_s: float = Field(20, ge=1, le=120)
    stale_after_s: float = Field(7200, ge=60, le=30 * 86400, description="Un dato real más viejo que esto no cuenta en modo auto")
    lookback_s: float = Field(7 * 86400, ge=3600, le=90 * 86400,
                              description="Hasta dónde se busca el último dato real (el modo real lo sirve aunque sea viejo)")


class AjustesSimulacion(BaseModel):
    tick_s: float = Field(5, ge=0.5, le=60, description="Cada cuánto avanza el motor (segundos de reloj real)")
    seed: int = 42
    history_interval_s: float = Field(300, ge=60, le=3600, description="Resolución del búfer de histórico")


class AjustesStream(BaseModel):
    heartbeat_s: float = Field(15, ge=1, le=120)


class Ajustes(BaseModel):
    upstream: AjustesUpstream = AjustesUpstream()
    simulation: AjustesSimulacion = AjustesSimulacion()
    stream: AjustesStream = AjustesStream()
    cors_origins: list[str] = ["*"]


class PoliticaFuentes(BaseModel):
    """Gana la regla más específica: sensor, tipo, estancia, edificio y, por último, default."""

    default: Modo = "auto"
    buildings: dict[str, Modo] = {}
    spaces: dict[str, Modo] = {}
    kinds: dict[str, Modo] = {}
    sensors: dict[str, Modo] = {}

    def resolver(self, sensor_id: str, kind: str, space_id: str, building_id: str) -> Modo:
        for regla, clave in ((self.sensors, sensor_id), (self.kinds, kind), (self.spaces, space_id), (self.buildings, building_id)):
            if clave in regla:
                return regla[clave]
        return self.default


def merge_patch(destino: dict, parche: dict) -> dict:
    """JSON Merge Patch (RFC 7396)."""
    resultado = dict(destino)
    for k, v in parche.items():
        if v is None:
            resultado.pop(k, None)
        elif isinstance(v, dict) and isinstance(resultado.get(k), dict):
            resultado[k] = merge_patch(resultado[k], v)
        else:
            resultado[k] = v
    return resultado


def _ruta(nombre: str) -> Path:
    return DIR_ESTADO / f"{nombre}.json"


def cargar[T: BaseModel](nombre: str, tipo: type[T]) -> T:
    ruta = _ruta(nombre)
    if ruta.exists():
        return tipo.model_validate_json(ruta.read_text(encoding="utf-8"))
    return tipo()


def guardar(nombre: str, modelo: BaseModel) -> None:
    DIR_ESTADO.mkdir(parents=True, exist_ok=True)
    tmp = _ruta(nombre).with_suffix(".tmp")
    tmp.write_text(json.dumps(modelo.model_dump(mode="json"), ensure_ascii=False, indent=1), encoding="utf-8")
    tmp.replace(_ruta(nombre))
