"""Escenarios de simulación (los ids son parte del contrato de la API)."""

from __future__ import annotations

from dataclasses import dataclass

from .calendario import CERRADO, EXAMENES, FIN_DE_SEMANA, LECTIVO


@dataclass(frozen=True)
class ConfigEscenario:
    id: str
    nombre: str
    descripcion: str
    # Fuerza el tipo de día. Con `solo_laborables`, solo de lunes a viernes no festivos.
    tipo_dia: str | None = None
    solo_laborables: bool = True
    # Ancla el modelo a una hora concreta: (mes, día mínimo, día de la semana 0=lunes, minuto local).
    # El tiempo del modelo arranca ahí y avanza con el reloj de la simulación.
    ancla: tuple[int, int, int, float] | None = None
    anomalia: float = 0.0  # °C sobre el clima sintético
    nubosidad_max: float | None = None
    clima_real: bool = True  # si se puede usar la temperatura exterior real
    averia_calefaccion: bool = False
    simulacro_s: float | None = None  # duración de la alarma del simulacro


ESCENARIOS: dict[str, ConfigEscenario] = {e.id: e for e in [
    ConfigEscenario(
        "normal_day", "Día normal",
        "Calendario académico real de la UEx con la hora del reloj de la simulación: clases de 8:30 a 14:30 y de "
        "16:00 a 20:30, calefacción de mediados de octubre a abril y clima real de Cáceres si hay datos."),
    ConfigEscenario(
        "winter_morning", "Mañana de invierno",
        "Martes lectivo de principios de febrero a las 7:30 con helada: las calderas arrancan, las aulas se llenan "
        "a las 8:30, el CO₂ sube con las ventanas cerradas y las aulas tardan en coger temperatura. El modelo "
        "salta a esa fecha y avanza con el reloj; el clima es sintético.",
        tipo_dia=LECTIVO, ancla=(2, 3, 1, 450.0), anomalia=-2.0, clima_real=False),
    ConfigEscenario(
        "summer_afternoon", "Tarde de verano",
        "Martes de julio a las 15:00, durante la convocatoria extraordinaria, con 36–38 °C fuera y el cielo "
        "despejado: el sol pega en las fachadas oeste, las persianas bajan, se abren ventanas y funcionan los "
        "aires acondicionados. El modelo salta a esa fecha y avanza con el reloj; el clima es sintético.",
        tipo_dia=EXAMENES, ancla=(7, 7, 1, 900.0), anomalia=3.0, nubosidad_max=0.1, clima_real=False),
    ConfigEscenario(
        "exam_period", "Periodo de exámenes",
        "Los días laborables pasan a ser de exámenes: pocas sesiones pero con aulas llenas y en silencio, "
        "salas de ordenadores para estudiar y menos movimiento en los pasillos.",
        tipo_dia=EXAMENES),
    ConfigEscenario(
        "weekend", "Fin de semana",
        "Todos los días se comportan como sábado o domingo: edificios cerrados, calefacción apagada y solo "
        "alguna persona suelta en los despachos.",
        tipo_dia=FIN_DE_SEMANA, solo_laborables=False),
    ConfigEscenario(
        "holiday_closure", "Cierre por vacaciones",
        "Cierre total como en agosto o en Navidad: nadie en los edificios, calefacción apagada y solo los "
        "consumos de base (servidores, cámaras, equipos de red).",
        tipo_dia=CERRADO, solo_laborables=False),
    ConfigEscenario(
        "heating_failure", "Avería de la calefacción",
        "Las calderas de todos los edificios fallan: la temperatura de impulsión cae, las válvulas de los "
        "radiadores se abren del todo sin conseguir nada y en invierno las estancias se enfrían hora a hora.",
        averia_calefaccion=True),
    ConfigEscenario(
        "fire_drill", "Simulacro de incendio",
        "Suena la alarma de incendios en todos los edificios durante 10 minutos: se desalojan en pocos minutos, "
        "los pasillos se llenan un momento, la gente espera en el exterior y después vuelve poco a poco.",
        simulacro_s=600.0),
]}
