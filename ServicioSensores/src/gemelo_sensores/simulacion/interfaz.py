"""Interfaz que el resto del servicio espera del motor de simulación.

El servicio crea un `MotorSimulacion` con el catálogo y lo hace avanzar en un bucle propio
(`avanzar` cada `settings.simulation.tick_s` segundos de reloj real). Después lee los valores
simulados de cualquier sensor con `lectura`, tanto si ese sensor se está sirviendo como simulado
como si no (así cambiar de real a simulado es instantáneo y sin saltos de arranque).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import Protocol

from ..catalogo import Catalogo


@dataclass
class Exterior:
    """Condiciones exteriores reales, si hay datos frescos de SmartPoliTech (si no, None)."""

    air_temperature: float | None = None
    relative_humidity: float | None = None


@dataclass
class Escenario:
    id: str
    name: str
    description: str


@dataclass
class Evento:
    id: str
    type: str
    target: str  # id de sensor, estancia, edificio o "campus"
    start: datetime  # hora simulada
    duration_s: float | None
    params: dict = field(default_factory=dict)
    active: bool = True


class MotorSimulacion(Protocol):
    escenario: str

    def __init__(self, catalogo: Catalogo, semilla: int = 42) -> None: ...

    def escenarios(self) -> list[Escenario]: ...

    def poner_escenario(self, escenario_id: str) -> None:
        """Lanza KeyError si no existe."""

    def avanzar(self, ahora: datetime, dt_s: float, exterior: Exterior | None) -> None:
        """Integra el modelo hasta `ahora` (hora simulada, UTC). `dt_s` son segundos simulados."""

    def lectura(self, sensor_id: str) -> dict[str, float] | None:
        """Valores actuales del sensor por propiedad (booleanos como 0.0/1.0), o None si no se simula."""

    def crear_evento(self, tipo: str, objetivo: str, duracion_s: float | None, params: dict, ahora: datetime) -> Evento:
        """Lanza ValueError si el tipo o el objetivo no son válidos."""

    def eventos(self) -> list[Evento]:
        """Activos primero, luego los últimos terminados."""

    def terminar_evento(self, evento_id: str) -> None:
        """Lanza KeyError si no existe."""
