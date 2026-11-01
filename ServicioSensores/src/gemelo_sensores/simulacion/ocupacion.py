"""Horarios de ocupación de cada estancia para un día.

Cada estancia "programada" (aulas, laboratorios, despachos, salas de reuniones…) recibe cada día una
lista de intervalos `(inicio, fin, personas, actividad)` en minutos locales. Las clases siguen el
horario de la EPCC (mañanas de 8:30 a 14:30 y tardes de 16:00 a 20:30, en franjas de una hora, con
bloques de una o dos horas y unos minutos de cambio entre clases). Las estancias de paso (pasillos,
halls, aseos) no tienen horario propio: el motor las deriva del movimiento del edificio.
"""

from __future__ import annotations

import math

from .azar import u01
from .calendario import CERRADO, EXAMENES, FESTIVO, FIN_DE_SEMANA, LECTIVO, NO_LECTIVO, VACACIONES

Intervalo = tuple[float, float, int, str]

FRANJAS_MANANA = ((510, 570), (570, 630), (630, 690), (690, 750), (750, 810), (810, 870))
FRANJAS_TARDE = ((960, 1020), (1020, 1080), (1080, 1140), (1140, 1230))
FRANJAS_EXAMEN = ((540, 720), (750, 870), (960, 1140))

# Prioridad de la actividad cuando se solapan intervalos (para el ruido y las puertas)
PRIORIDAD = {"examen": 5, "clase": 4, "practica": 4, "reunion": 3, "libre": 2, "trabajo": 1, "": 0}

# Probabilidad de clase por franja (mañana, tarde) y asistencia (mín, máx) sobre la capacidad
_DOCENCIA = {
    "classroom": (0.72, 0.45, 0.35, 0.85),
    "computer_lab": (0.60, 0.42, 0.45, 0.95),
    "lab": (0.22, 0.25, 0.40, 0.90),
}

_PRESENCIA = {LECTIVO: (0.85, 0.45), EXAMENES: (0.80, 0.35), NO_LECTIVO: (0.60, 0.10), VACACIONES: (0.12, 0.03),
              FIN_DE_SEMANA: (0.04, 0.0), FESTIVO: (0.02, 0.0), CERRADO: (0.0, 0.0)}


def _clases(iv: list[Intervalo], tipo: str, cap: int, c: int, ordinal: int, dia_semana: int) -> None:
    p_m, p_t, a_min, a_max = _DOCENCIA[tipo]
    if dia_semana == 4:
        p_t *= 0.4  # los viernes por la tarde hay poca docencia
    for n_franja, (franjas, p) in enumerate(((FRANJAS_MANANA, p_m), (FRANJAS_TARDE, p_t))):
        i = 0
        while i < len(franjas):
            k = n_franja * 100 + i
            if u01(c, ordinal * 1000 + k, 1) < p:
                dur = 2 if (u01(c, ordinal * 1000 + k, 2) < 0.55 and i + 1 < len(franjas)) else 1
                ini = franjas[i][0] + 6.0 * u01(c, ordinal * 1000 + k, 3)  # llegan hasta 6 min tarde
                fin = franjas[i + dur - 1][1] - 4.0 - 9.0 * u01(c, ordinal * 1000 + k, 4)
                asist = a_min + (a_max - a_min) * u01(c, ordinal * 1000 + k, 5)
                if n_franja == 0 and i == 0:
                    asist *= 0.8  # a las 8:30 siempre falta gente
                n = min(cap, max(3, round(cap * asist)))   # (el mínimo de 3 no puede pasar del aforo)
                iv.append((ini, fin, n + 1, "practica" if tipo == "lab" else "clase"))
                i += dur
            else:
                if tipo == "computer_lab" and u01(c, ordinal * 1000 + k, 6) < 0.5:
                    # uso libre del aula de informática entre clases
                    n = max(1, round(cap * 0.12 * u01(c, ordinal * 1000 + k, 7)))
                    iv.append((franjas[i][0] + 8.0, franjas[i][1] - 3.0, n, "libre"))
                i += 1


def _examenes(iv: list[Intervalo], tipo: str, cap: int, c: int, ordinal: int) -> None:
    probs = (0.35, 0.25, 0.22) if tipo == "classroom" else (0.25, 0.18, 0.15)
    for j, ((a, b), p) in enumerate(zip(FRANJAS_EXAMEN, probs)):
        k = ordinal * 1000 + 500 + j
        if u01(c, k, 1) < p:
            n = min(cap, max(4, round(cap * (0.3 + 0.4 * u01(c, k, 2)))))
            ini = a - 15.0 + 10.0 * u01(c, k, 3)
            fin = b - 40.0 * u01(c, k, 4)  # la gente va acabando antes
            iv.append((ini, fin, n + 2, "examen"))
        elif u01(c, k, 5) < 0.25:
            iv.append((a + 30.0, b - 20.0, max(1, round(cap * 0.08 * u01(c, k, 6))), "libre"))  # estudio


def _personal(iv: list[Intervalo], puestos: int, tipo_dia: str, c: int, ordinal: int, factor: float = 1.0) -> None:
    p_man, p_tar = _PRESENCIA.get(tipo_dia, (0.0, 0.0))
    p_man *= factor
    p_tar *= factor
    for p in range(puestos):
        k = ordinal * 1000 + 700 + p
        if u01(c, k, 1) < p_man:
            if tipo_dia in (FIN_DE_SEMANA, FESTIVO):
                ini = 600.0 + 60.0 * u01(c, k, 2)
                iv.append((ini, ini + 90.0 + 120.0 * u01(c, k, 3), 1, "trabajo"))
                continue
            lleg = 480.0 + 120.0 * u01(c, k, 2) ** 1.5  # 8:00–10:00
            sal = 825.0 + 75.0 * u01(c, k, 3)  # 13:45–15:00
            if u01(c, k, 4) < 0.6:  # pausa para el café
                c0 = 630.0 + 60.0 * u01(c, k, 5)
                c1 = c0 + 15.0 + 20.0 * u01(c, k, 6)
                iv.append((lleg, c0, 1, "trabajo"))
                iv.append((c1, sal, 1, "trabajo"))
            else:
                iv.append((lleg, sal, 1, "trabajo"))
        if u01(c, k, 7) < p_tar:
            t0 = 960.0 + 45.0 * u01(c, k, 8)
            iv.append((t0, 1110.0 + 120.0 * u01(c, k, 9), 1, "trabajo"))


def _reuniones(iv: list[Intervalo], cap: int, c: int, ordinal: int) -> None:
    libre_desde = 0.0
    for j, h in enumerate((540, 600, 660, 720, 780, 960, 1020, 1080)):
        k = ordinal * 1000 + 800 + j
        if h >= libre_desde and u01(c, k, 1) < 0.13:
            dur = 50.0 + 70.0 * u01(c, k, 2)
            n = 3 + round((min(cap, 14) - 3) * u01(c, k, 3))
            ini = h + 5.0 * u01(c, k, 4)
            iv.append((ini, ini + dur, max(2, n), "reunion"))
            libre_desde = ini + dur


def programar_dia(tipo: str, tipo_dia: str, cap: int, puestos: int, c: int, ordinal: int, dia_semana: int) -> list[Intervalo]:
    """Intervalos de ocupación de una estancia programada en un día."""
    iv: list[Intervalo] = []
    if tipo_dia == CERRADO:
        return iv
    if tipo in _DOCENCIA:
        if tipo_dia == LECTIVO:
            _clases(iv, tipo, cap, c, ordinal, dia_semana)
        elif tipo_dia == EXAMENES and tipo != "lab":
            _examenes(iv, tipo, cap, c, ordinal)
        elif tipo_dia == NO_LECTIVO and u01(c, ordinal, 900) < 0.06:
            iv.append((540.0, 780.0, max(3, round(cap * 0.4)), "clase"))  # curso o jornada
        if tipo == "lab":
            _personal(iv, puestos, tipo_dia, c, ordinal, 0.8)  # personal investigador
    elif tipo == "office":
        _personal(iv, puestos, tipo_dia, c, ordinal)
    elif tipo == "meeting_room":
        if tipo_dia in (LECTIVO, EXAMENES, NO_LECTIVO):
            _reuniones(iv, cap, c, ordinal)
    elif tipo == "technical_room":
        if tipo_dia in (LECTIVO, EXAMENES, NO_LECTIVO) and u01(c, ordinal, 901) < 0.08:
            ini = 540.0 + 240.0 * u01(c, ordinal, 902)
            iv.append((ini, ini + 30.0 + 60.0 * u01(c, ordinal, 903), 1, "trabajo"))  # mantenimiento
    elif tipo == "other":
        _personal(iv, puestos, tipo_dia, c, ordinal, 0.4)
    return iv


def ocupacion_cafeteria(minuto: float, tipo_dia: str, cap: int, factor_dia: float) -> float:
    """Personas en la cafetería: desayunos a media mañana y comidas."""
    if tipo_dia in (LECTIVO, EXAMENES):
        escala = 1.0 if tipo_dia == LECTIVO else 0.6
        ini, fin = 480.0, 1140.0
    elif tipo_dia == NO_LECTIVO:
        escala, ini, fin = 0.25, 510.0, 900.0
    else:
        return 0.0
    if not ini <= minuto < fin:
        return 0.0
    f = (0.08 + 0.55 * math.exp(-((minuto - 650.0) / 40.0) ** 2) + 0.75 * math.exp(-((minuto - 845.0) / 45.0) ** 2)
         + 0.15 * math.exp(-((minuto - 1020.0) / 40.0) ** 2))
    borde = min(1.0, (minuto - ini) / 20.0, (fin - minuto) / 20.0)
    return cap * f * escala * factor_dia * borde
