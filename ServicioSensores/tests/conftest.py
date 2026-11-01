"""Fixtures comunes: un gemelo con el catálogo real, un simulador falso y sin red."""

from __future__ import annotations

import itertools
from datetime import datetime

import pytest
from fastapi.testclient import TestClient

from gemelo_sensores.catalogo import Catalogo
from gemelo_sensores.simulacion.interfaz import Escenario, Evento


class SimuladorFalso:
    """Valores fijos por propiedad: basta para probar la API sin depender del motor."""

    VALORES = {"air_temperature": 21.0, "relative_humidity": 45.0, "co2_concentration": 1200.0, "window_open": 1.0,
               "valve_position": 40.0, "occupancy": 20.0, "alarm_active": 0.0}

    def __init__(self, catalogo: Catalogo, semilla: int = 42) -> None:
        self.catalogo = catalogo
        self.escenario = "normal_day"
        self._eventos: list[Evento] = []
        self._ids = itertools.count(1)

    def escenarios(self):
        return [Escenario("normal_day", "Día normal", ""), Escenario("fire_drill", "Simulacro", "")]

    def poner_escenario(self, escenario_id: str) -> None:
        if escenario_id not in {e.id for e in self.escenarios()}:
            raise KeyError(escenario_id)
        self.escenario = escenario_id

    def avanzar(self, ahora: datetime, dt_s: float, exterior) -> None:
        pass

    def lectura(self, sensor_id: str):
        s = self.catalogo.sensores[sensor_id]
        return {p: self.VALORES.get(p, 1.0) for p in s.properties}

    def crear_evento(self, tipo, objetivo, duracion_s, params, ahora):
        if objetivo != "campus" and objetivo not in self.catalogo.espacios and objetivo not in self.catalogo.sensores \
                and objetivo not in self.catalogo.edificios:
            raise ValueError(f"objetivo desconocido: {objetivo}")
        e = Evento(f"ev{next(self._ids)}", tipo, objetivo, ahora, duracion_s, params)
        self._eventos.append(e)
        return e

    def eventos(self):
        return list(self._eventos)

    def terminar_evento(self, evento_id: str) -> None:
        for e in self._eventos:
            if e.id == evento_id:
                e.active = False
                return
        raise KeyError(evento_id)


@pytest.fixture(scope="session")
def catalogo() -> Catalogo:
    return Catalogo.cargar()


@pytest.fixture
def cliente(catalogo, tmp_path, monkeypatch):
    monkeypatch.setattr("gemelo_sensores.ajustes.DIR_ESTADO", tmp_path)
    from gemelo_sensores.gemelo import Gemelo
    from gemelo_sensores.main import crear_app

    g = Gemelo(catalogo, SimuladorFalso(catalogo), sondear_al_arrancar=False)
    g.ajustes.upstream.enabled = False
    with TestClient(crear_app(g, tareas=False)) as c:
        c.gemelo = g
        yield c
