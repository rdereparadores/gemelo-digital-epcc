"""Hora de Madrid y calendario académico aproximado de la Universidad de Extremadura (Cáceres).

Se trabaja con instantes como segundos desde la época (UTC) y con días como ordinales de
`date.toordinal()`, que es lo más barato de calcular dentro del bucle del motor.
"""

from __future__ import annotations

import math
import time
from datetime import UTC, date, datetime, timedelta
from functools import lru_cache

ORDINAL_EPOCA = date(1970, 1, 1).toordinal()

# Tipos de día
LECTIVO = "lectivo"
EXAMENES = "examenes"
NO_LECTIVO = "no_lectivo"  # sin clases pero con el personal (julio, primeros de septiembre)
VACACIONES = "vacaciones"  # Navidad y Semana Santa: edificio abierto por la mañana, casi vacío
FIN_DE_SEMANA = "fin_de_semana"
FESTIVO = "festivo"
CERRADO = "cerrado"  # agosto: la universidad cierra

DIAS_LABORABLES = (LECTIVO, EXAMENES, NO_LECTIVO, VACACIONES)
DIAS_DOCENTES = (LECTIVO, EXAMENES)

# Festivos fijos (nacionales, de Extremadura, de Cáceres y el patrón de la universidad)
_FESTIVOS_FIJOS = {
    (1, 1), (1, 6), (1, 28),  # Año Nuevo, Reyes, Santo Tomás de Aquino (patrón universitario)
    (4, 23),  # San Jorge, fiesta local de Cáceres
    (5, 1), (8, 15), (9, 8),  # Trabajo, Asunción, Día de Extremadura
    (10, 12), (11, 1), (12, 6), (12, 8), (12, 25),
}


@lru_cache(maxsize=64)
def _limites_verano(anio: int) -> tuple[float, float]:
    """Instantes (UTC) de inicio y fin del horario de verano europeo."""

    def ultimo_domingo(mes: int) -> date:
        d = date(anio, mes, 31)
        return d - timedelta(days=(d.weekday() - 6) % 7)

    ini = ultimo_domingo(3)
    fin = ultimo_domingo(10)
    return (datetime(ini.year, ini.month, ini.day, 1, tzinfo=UTC).timestamp(),
            datetime(fin.year, fin.month, fin.day, 1, tzinfo=UTC).timestamp())


def desfase_madrid(t: float) -> int:
    """Segundos que hay que sumar a UTC para tener la hora peninsular española."""
    ini, fin = _limites_verano(time.gmtime(t).tm_year)
    return 7200 if ini <= t < fin else 3600


def local(t: float) -> tuple[int, float, int]:
    """(ordinal del día local, minuto del día local, día de la semana 0=lunes)."""
    lt = t + desfase_madrid(t)
    dias = math.floor(lt / 86400.0)
    ordinal = ORDINAL_EPOCA + dias
    return ordinal, (lt - dias * 86400.0) / 60.0, (ordinal - 1) % 7


def utc_desde_local(ordinal: int, minuto: float) -> float:
    """Instante UTC de una hora local de Madrid."""
    lt = (ordinal - ORDINAL_EPOCA) * 86400.0 + minuto * 60.0
    return lt - desfase_madrid(lt - 3600)


def medianoche_local(t: float) -> float:
    """Instante UTC de las 00:00 (hora de Madrid) del día local de `t`."""
    ordinal, _, _ = local(t)
    return utc_desde_local(ordinal, 0.0)


def a_datetime(t: float) -> datetime:
    return datetime.fromtimestamp(t, UTC)


def a_epoca(d: datetime) -> float:
    if d.tzinfo is None:
        d = d.replace(tzinfo=UTC)
    return d.timestamp()


@lru_cache(maxsize=16)
def pascua(anio: int) -> date:
    """Domingo de Pascua (algoritmo anónimo gregoriano)."""
    a = anio % 19
    b, c = divmod(anio, 100)
    d, e = divmod(b, 4)
    f = (b + 8) // 25
    g = (b - f + 1) // 3
    h = (19 * a + b - d - g + 15) % 30
    i, k = divmod(c, 4)
    l_ = (32 + 2 * e + 2 * i - h - k) % 7
    m = (a + 11 * h + 22 * l_) // 451
    mes = (h + l_ - 7 * m + 114) // 31
    dia = (h + l_ - 7 * m + 114) % 31 + 1
    return date(anio, mes, dia)


@lru_cache(maxsize=1024)
def tipo_dia(ordinal: int) -> str:
    """Tipo de día según el calendario académico aproximado de la UEx en Cáceres."""
    d = date.fromordinal(ordinal)
    m, dia = d.month, d.day
    if m == 8:
        return CERRADO
    p = pascua(d.year)
    if (m, dia) in _FESTIVOS_FIJOS or d in (p - timedelta(days=3), p - timedelta(days=2)):
        return FESTIVO
    if d.weekday() >= 5:
        return FIN_DE_SEMANA
    if (m == 12 and dia >= 23) or (m == 1 and dia <= 7):
        return VACACIONES
    if p - timedelta(days=6) <= d <= p + timedelta(days=1):  # Semana Santa hasta el lunes de Pascua
        return VACACIONES
    if m == 1:
        return EXAMENES  # convocatoria de enero
    if (m == 5 and dia >= 25) or m == 6 or (m == 7 and dia <= 15):
        return EXAMENES  # convocatorias ordinaria (junio) y extraordinaria (julio)
    if m == 7 or (m == 9 and dia < 10):
        return NO_LECTIVO
    return LECTIVO


def temporada_calefaccion(ordinal: int) -> bool:
    """De mediados de octubre a finales de abril."""
    d = date.fromordinal(ordinal)
    return d.month in (11, 12, 1, 2, 3, 4) or (d.month == 10 and d.day >= 15)


def temporada_refrigeracion(ordinal: int) -> bool:
    """De mediados de mayo a finales de septiembre."""
    d = date.fromordinal(ordinal)
    return d.month in (6, 7, 8, 9) or (d.month == 5 and d.day >= 15)


def horario_edificio(tipo: str) -> tuple[float, float] | None:
    """Apertura del edificio (minutos locales) según el tipo de día, o None si está cerrado."""
    if tipo in DIAS_DOCENTES:
        return 450.0, 1290.0  # 7:30–21:30
    if tipo in (NO_LECTIVO, VACACIONES):
        return 480.0, 900.0  # 8:00–15:00
    return None


def horario_calderas(tipo: str, dia_semana: int) -> tuple[float, float] | None:
    """Horario de las calderas: arrancan antes de abrir para precalentar (más el lunes)."""
    if tipo in DIAS_DOCENTES:
        return (330.0 if dia_semana == 0 else 390.0), 1260.0  # 5:30 (lunes) o 6:30 hasta 21:00
    if tipo in (NO_LECTIVO, VACACIONES):
        return 420.0, 870.0
    return None
