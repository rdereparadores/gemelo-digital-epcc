"""Catálogo estático del gemelo: edificios, estancias, sensores y propiedades observadas.

Se carga de `datos/catalogo.json`, que genera `herramientas/generar_catalogo.py` a partir del
inventario de SmartPoliTech y de `Referencias/Politecnica/datos/interiores.json`.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

RUTA_POR_DEFECTO = Path(__file__).resolve().parents[2] / "datos" / "catalogo.json"


@dataclass(frozen=True)
class Propiedad:
    name: str
    unit: str
    type: str  # "number" | "boolean"
    description: str = ""


# Vocabulario de propiedades (ver docs/CONTRATO_API.md).
PROPIEDADES: dict[str, Propiedad] = {
    p.name: p
    for p in [
        Propiedad("air_temperature", "Cel", "number", "Temperatura del aire"),
        Propiedad("relative_humidity", "%", "number", "Humedad relativa"),
        Propiedad("co2_concentration", "ppm", "number", "Concentración de CO₂"),
        Propiedad("window_open", "1", "boolean", "Ventana abierta"),
        Propiedad("door_open", "1", "boolean", "Puerta abierta"),
        Propiedad("valve_position", "%", "number", "Apertura de la válvula del radiador"),
        Propiedad("target_temperature", "Cel", "number", "Consigna del radiador"),
        Propiedad("battery_voltage", "V", "number", "Tensión de la batería"),
        Propiedad("active_power", "W", "number", "Potencia activa"),
        Propiedad("active_energy", "kW.h", "number", "Energía activa acumulada"),
        Propiedad("water_volume", "L", "number", "Volumen de agua acumulado"),
        Propiedad("water_flow", "L/min", "number", "Caudal de agua"),
        Propiedad("occupancy", "{people}", "number", "Personas presentes"),
        Propiedad("devices_on", "{devices}", "number", "Equipos encendidos"),
        Propiedad("power_on", "1", "boolean", "Equipo encendido"),
        Propiedad("blind_position", "%", "number", "Persiana (0 subida, 100 bajada)"),
        Propiedad("sound_level", "dB", "number", "Nivel sonoro continuo equivalente (LAeq)"),
        Propiedad("illuminance", "lx", "number", "Iluminancia"),
        Propiedad("pm25", "ug/m3", "number", "Partículas PM2,5"),
        Propiedad("water_temperature", "Cel", "number", "Temperatura del agua de calefacción"),
        Propiedad("wind_speed", "m/s", "number", "Velocidad del viento"),
        Propiedad("wind_direction", "deg", "number", "Dirección del viento"),
        Propiedad("atmospheric_pressure", "hPa", "number", "Presión atmosférica"),
        Propiedad("precipitation", "mm", "number", "Precipitación"),
        Propiedad("solar_power", "W", "number", "Potencia fotovoltaica"),
        Propiedad("lights_on", "1", "boolean", "Luces encendidas"),
        Propiedad("alarm_active", "1", "boolean", "Alarma activa"),
        Propiedad("wifi_clients", "{devices}", "number", "Clientes wifi conectados"),
    ]
}


@dataclass
class Edificio:
    id: str
    name: str
    unity_id: str | None
    floors: list[str]


@dataclass
class Espacio:
    id: str
    building_id: str
    floor: str | None
    type: str
    code: str
    name: str | None
    unity_room: str | None
    area_m2: float | None = None
    height_m: float | None = None
    capacity: int | None = None


@dataclass
class Sensor:
    id: str
    name: str
    kind: str
    space_id: str
    building_id: str
    properties: list[str]
    real_available: bool
    # Cómo leerlo en SmartPoliTech: medición de InfluxDB y campo de origen de cada propiedad.
    # Las transformaciones se indican con "campo|transformacion" (p. ej. "door|bool").
    upstream: dict | None = None
    last_real_time: str | None = None  # último dato real conocido al generar el catálogo


@dataclass
class Catalogo:
    edificios: dict[str, Edificio] = field(default_factory=dict)
    espacios: dict[str, Espacio] = field(default_factory=dict)
    sensores: dict[str, Sensor] = field(default_factory=dict)

    @classmethod
    def cargar(cls, ruta: Path = RUTA_POR_DEFECTO) -> Catalogo:
        datos = json.loads(Path(ruta).read_text(encoding="utf-8"))
        return cls(
            edificios={e["id"]: Edificio(**e) for e in datos["buildings"]},
            espacios={e["id"]: Espacio(**e) for e in datos["spaces"]},
            sensores={s["id"]: Sensor(**s) for s in datos["sensors"]},
        )

    def sensores_de_espacio(self, espacio_id: str) -> list[Sensor]:
        return [s for s in self.sensores.values() if s.space_id == espacio_id]
