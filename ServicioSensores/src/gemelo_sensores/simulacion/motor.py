"""Motor de simulación del gemelo: un modelo físico sencillo por estancia del que salen las lecturas.

Cada estancia (`Espacio`) tiene estado propio: personas, temperatura del aire y de la masa del
edificio, CO₂, humedad, partículas, ventanas, persianas, válvulas, luces y equipos. Todos los
sensores de una estancia leen de ese estado (con su sesgo de calibración y un ruido pequeño con
correlación temporal), así que son coherentes entre sí. Los edificios suman las cargas de sus
estancias (potencia, agua, demanda de calefacción) y tienen su sala de calderas; el campus tiene el
clima, el sol y la fotovoltaica.

Modelo, en resumen:

- Ocupación: horario diario por estancia (clases, exámenes, personal, reuniones) según el calendario
  académico; los pasillos y halls se derivan del movimiento del edificio (picos en los cambios de
  clase) y los aseos de la gente presente. Las personas tienden a su objetivo con una constante de
  tiempo de minuto y medio.
- Térmico: dos nodos (aire casi estacionario y masa del edificio) con solución exponencial exacta en
  cada subpaso, así que es estable aunque el paso sea de horas. Pérdidas por la envolvente y por la
  ventilación, ganancias por personas, equipos, luces y sol (según la orientación y la persiana),
  radiadores con válvula termostática proporcional y aire acondicionado donde lo hay.
- CO₂, humedad (razón de humedad con inercia higroscópica) y PM2,5 por balance de masas con la
  renovación de aire, que sube mucho con las ventanas abiertas.

Las decisiones aleatorias (clases, ventanas, persianas…) salen de funciones hash de la semilla, así
que el resultado es determinista para la misma semilla y la misma secuencia de llamadas.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import date, datetime

from ..catalogo import Catalogo, Sensor
from . import calendario as cal
from .azar import RuidoSensor, clave, u01
from .clima import AjusteClima, ClimaCaceres, EstadoExterior, humedad_relativa, razon_humedad
from .escenarios import ESCENARIOS, ConfigEscenario
from .interfaz import Escenario, Evento, Exterior
from .ocupacion import PRIORIDAD, ocupacion_cafeteria, programar_dia

DURACION_MAX_S = 30 * 86400.0  # tope de duration_s de un evento (sin tope, 1e308 rompía la tabla del panel)
TIPOS_EVENTO = ("open_windows", "close_windows", "class_start", "class_end", "heating_off", "heating_on",
                "fire_alarm", "power_outage", "water_leak", "sensor_failure")

_PASO_MAX = 300.0  # s: tamaño máximo de subpaso deseable
_MAX_SUBPASOS = 6  # tope de subpasos por llamada (con pasos enormes la integración exacta sigue siendo estable)
_ARRANQUE_H = 36.0  # horas que se simulan de golpe al arrancar o al saltar el reloj
_ARRANQUE_PASOS = 48
_HISTORIAL_EVENTOS = 50
_P_ATM = 1.2 * 1005.0 / 3600.0  # W/K por (m³/h) de aire renovado

# Modos de estancia
_PROGRAMADA, _CAFETERIA, _CIRCULACION, _ASEO, _EXTERIOR, _AGREGADA = range(6)


@dataclass(frozen=True)
class _ParamTipo:
    ua: float  # W/(m²·K) de envolvente por m² de planta
    ach: float  # renovaciones por hora por infiltración
    rad: float  # W/m² de radiadores
    vidrio: float  # m² de vidrio efectivo (con factor solar) por m² de planta
    fl: float  # factor de luz diurna
    luz: float  # W/m² de alumbrado
    lux: float  # iluminancia con las luces encendidas
    consigna: float  # consigna de confort de los radiadores
    ac: float  # probabilidad de tener aire acondicionado
    equipo: float  # W/m² de equipos siempre encendidos
    m2_plaza: float  # para estimar la capacidad
    area: float  # área por defecto
    w_persona: float  # W eléctricos por persona presente (portátiles, monitores…)


_TIPOS: dict[str, _ParamTipo] = {
    "classroom": _ParamTipo(0.9, 0.7, 75, 0.06, 0.030, 10, 500, 21.0, 0.10, 0.5, 1.6, 90, 5),
    "computer_lab": _ParamTipo(0.9, 0.8, 60, 0.05, 0.025, 10, 500, 21.0, 0.90, 1.0, 2.4, 70, 0),
    "lab": _ParamTipo(1.0, 1.0, 75, 0.05, 0.030, 11, 500, 21.0, 0.50, 5.0, 5.0, 50, 25),
    "office": _ParamTipo(1.2, 0.6, 90, 0.07, 0.035, 9, 400, 21.5, 0.60, 2.0, 8.0, 18, 30),
    "corridor": _ParamTipo(0.4, 1.5, 30, 0.015, 0.008, 6, 150, 19.0, 0.0, 0.3, 2.0, 40, 0),
    "hall": _ParamTipo(1.3, 2.0, 45, 0.08, 0.050, 7, 200, 19.0, 0.0, 0.5, 2.0, 80, 0),
    "technical_room": _ParamTipo(0.6, 0.5, 0, 0.0, 0.0, 8, 300, 0.0, 1.0, 6.0, 50.0, 20, 0),
    "restroom": _ParamTipo(0.7, 3.0, 50, 0.02, 0.010, 8, 200, 20.0, 0.0, 0.5, 5.0, 15, 0),
    "cafeteria": _ParamTipo(1.1, 1.5, 70, 0.07, 0.040, 10, 300, 21.0, 0.8, 15.0, 1.4, 120, 0),
    "meeting_room": _ParamTipo(1.0, 0.7, 80, 0.06, 0.030, 10, 500, 21.0, 0.7, 1.0, 2.5, 30, 10),
    # Salón de grados: techo alto, split mural (aire acondicionado seguro) y unas 60 plazas en 100 m²
    "auditorium": _ParamTipo(0.9, 0.7, 75, 0.05, 0.025, 11, 500, 21.0, 1.0, 1.0, 1.7, 100, 3),
    "other": _ParamTipo(0.8, 0.8, 60, 0.04, 0.020, 8, 300, 20.0, 0.2, 2.0, 6.0, 20, 10),
}
_TIPOS["outdoor"] = _TIPOS["other"]
_TIPOS["building"] = _TIPOS["other"]

_TIPOS_DOCENTES = ("classroom", "computer_lab", "lab")
_MEDIDOR_EDIFICIO = ("building", "technical_room", "hall", "corridor", "other", "outdoor")

# Ruido y redondeo por propiedad: (ruido absoluto, ruido relativo, periodo s, sesgo máx., paso, mín., máx.)
_POST: dict[str, tuple[float, float, float, float, float | None, float | None, float | None]] = {
    "air_temperature": (0.07, 0.0, 900.0, 0.25, 0.1, None, None),
    "relative_humidity": (0.8, 0.0, 900.0, 2.0, 1.0, 0.0, 100.0),
    "co2_concentration": (8.0, 0.015, 600.0, 20.0, 1.0, 350.0, 10000.0),
    "valve_position": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 100.0),
    "target_temperature": (0.0, 0.0, 0.0, 0.0, 0.5, None, None),
    "battery_voltage": (0.004, 0.0, 3600.0, 0.0, 0.01, 2.0, 3.7),
    "active_power": (0.0, 0.02, 120.0, 0.0, 1.0, 0.0, None),
    "active_energy": (0.0, 0.0, 0.0, 0.0, 0.01, 0.0, None),
    "water_volume": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, None),
    "water_flow": (0.0, 0.08, 60.0, 0.0, 0.1, 0.0, None),
    "occupancy": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, None),
    "devices_on": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, None),
    "blind_position": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 100.0),
    "sound_level": (1.2, 0.0, 20.0, 0.5, 0.1, 25.0, 110.0),
    "illuminance": (0.0, 0.03, 300.0, 0.0, 1.0, 0.0, None),
    "pm25": (0.4, 0.06, 1200.0, 0.8, 0.1, 0.0, None),
    "water_temperature": (0.15, 0.0, 300.0, 0.4, 0.1, None, None),
    "wind_speed": (0.0, 0.0, 0.0, 0.0, 0.1, 0.0, None),
    "wind_direction": (0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 359.0),
    "atmospheric_pressure": (0.0, 0.0, 0.0, 0.0, 0.1, None, None),
    "precipitation": (0.0, 0.0, 0.0, 0.0, 0.2, 0.0, None),
    "solar_power": (0.0, 0.01, 60.0, 0.0, 1.0, 0.0, None),
    "wifi_clients": (0.0, 0.05, 300.0, 0.0, 1.0, 0.0, None),
}


def _limitar(x: float, a: float, b: float) -> float:
    return a if x < a else min(x, b)


def _decimales(paso: float) -> int:
    return max(0, -math.floor(math.log10(paso) + 1e-9)) if paso < 1 else 0


# ---------------------------------------------------------------------------------------- estado
class _Sala:
    __slots__ = (
        "id", "esp", "idx", "tipo", "modo", "p", "edif", "c", "planta", "area", "vol", "cap", "puestos", "orient",
        "ua", "hm", "cm", "ach_inf", "ach_mec", "pmax", "frio", "a_vid", "fl", "w_luces", "lux_luces", "equipo_w",
        "w_persona", "w_pc", "n_pc", "consigna", "peso", "conserje", "persianas_auto",
        # estado
        "t", "tm", "w", "co2", "pm", "occ", "actividad", "ventanas", "n_abiertas", "persianas", "valvula", "phi",
        "sp", "q_rad", "q_frio", "luces", "e_dia", "pcs", "carga", "caudal", "energia", "agua_dia", "dia_agua",
        "iv", "dia", "luz_olvidada", "pc_resto", "pc_noche", "factor_dia",
        # efectos de eventos
        "forz_ocup", "vent_forz", "calef", "apagon", "fuga", "humo",
    )

    def __init__(self) -> None:
        self.esp = None
        self.t = self.tm = 20.0
        self.w = 0.007
        self.co2 = 420.0
        self.pm = 5.0
        self.occ = 0.0
        self.actividad = ""
        self.ventanas: list[bool] = []
        self.n_abiertas = 0
        self.persianas: list[float] = []
        self.valvula = self.phi = self.q_rad = self.q_frio = 0.0
        self.sp = 20.0
        self.luces = False
        self.e_dia = 0.0
        self.pcs = 0
        self.carga = self.caudal = self.energia = self.agua_dia = 0.0
        self.dia_agua = -1
        self.iv: list = []
        self.dia = None
        self.luz_olvidada = False
        self.pc_resto = self.factor_dia = 0.0
        self.pc_noche = 0
        self.peso = 0.0
        self.conserje = False
        self.persianas_auto = False
        self.forz_ocup = None
        self.vent_forz = None
        self.calef = None
        self.apagon = False
        self.fuga = 0.0
        self.humo = False


class _Edificio:
    __slots__ = (
        "abiertas",
        "agregada",
        "agua_dia",
        "alarma",
        "apagon",
        "area",
        "averia",
        "base_w",
        "c",
        "calef",
        "carga",
        "caudal",
        "co2_media",
        "demanda",
        "demanda_frac",
        "dia_agua",
        "energia",
        "es_exterior",
        "evacuados",
        "fin_alarma",
        "flujo",
        "fuga",
        "id",
        "k_ocup",
        "luces",
        "mov",
        "occ_circ",
        "occ_ext",
        "occ_prim",
        "occ_total",
        "ocup_planta",
        "pcs",
        "planta_on",
        "pm_media",
        "pmax_tot",
        "potencia",
        "rh_media",
        "salas",
        "t_imp",
        "t_media",
        "t_ret",
    )

    def __init__(self, id_: str) -> None:
        self.id = id_
        self.salas: list[_Sala] = []
        self.agregada: _Sala | None = None
        self.area = 0.0
        self.base_w = 0.0
        self.t_imp = self.t_ret = 25.0
        self.planta_on = False
        self.demanda = self.pmax_tot = self.demanda_frac = 0.0
        self.k_ocup = 0.0
        self.mov = self.occ_prim = self.occ_circ = self.occ_ext = self.occ_total = self.flujo = 0.0
        self.carga = self.potencia = self.energia = self.caudal = self.agua_dia = 0.0
        self.dia_agua = -1
        self.evacuados = 0.0
        self.alarma = False
        self.fin_alarma = -1e18
        self.apagon = False
        self.calef = None
        self.averia = False
        self.fuga = 0.0
        self.t_media, self.rh_media, self.co2_media, self.pm_media = 20.0, 45.0, 450.0, 5.0
        self.pcs = 0
        self.luces = False
        self.abiertas = 0
        self.ocup_planta: dict[str, float] = {}
        self.es_exterior = False


class _SensorSim:
    __slots__ = (
        "acum",
        "ap",
        "bat0",
        "c",
        "cuota",
        "desvio_v",
        "e0",
        "edif",
        "id",
        "ip",
        "iv",
        "kind",
        "kwp",
        "lectores",
        "medidor",
        "props",
        "rol",
        "s",
        "sala",
    )

    def __init__(self, s: Sensor, sala: _Sala, edif: _Edificio, c: int) -> None:
        self.s = s
        self.id = s.id
        self.kind = s.kind
        self.props = list(s.properties)
        self.sala = sala
        self.edif = edif
        self.c = c
        self.lectores: list[tuple] | None = None
        self.iv: int | None = None
        self.ip: int | None = None
        self.medidor = "sala"
        self.cuota = 1.0
        self.e0 = 0.0
        self.kwp = 0.0
        self.rol = "impulsion"
        self.ap = "sala"
        self.bat0 = u01(c, 77)
        self.desvio_v = (u01(c, 78) - 0.5) * 0.16
        self.acum = 0.0


@dataclass
class _EventoInt:
    ev: Evento
    salas: list[_Sala]
    edificios: list[_Edificio]
    sensores: list[str]
    ventanas: list[tuple[_Sala, int]]
    fin: float | None  # instante (UTC, reloj del servicio) en que caduca
    seq: int


class _Ctx:
    """Contexto del campus en un subpaso."""

    __slots__ = (
        "abierto",
        "calderas",
        "calefaccion",
        "cierre",
        "confort",
        "dia_semana",
        "dt_e",
        "estoc",
        "ext",
        "h",
        "k_aire",
        "minuto",
        "ordinal",
        "refrigeracion",
        "t",
        "tipo_dia",
    )


# ---------------------------------------------------------------------------------------- motor
class Simulador:
    """Motor de simulación del gemelo (cumple `interfaz.MotorSimulacion`).

    Además del protocolo expone `tiempo_efectivo` (la hora que usa el modelo, que solo difiere de la
    del reloj en los escenarios anclados a una fecha), `fuente_clima`, `exterior_actual()` y
    `estado_espacio()`, útiles para el estado del campus y para depurar.
    """

    def __init__(self, catalogo: Catalogo, semilla: int = 42) -> None:
        self.catalogo = catalogo
        self.semilla = semilla
        self.clima = ClimaCaceres(semilla)
        self._cfg: ConfigEscenario = ESCENARIOS["normal_day"]
        self._ajuste = AjusteClima()
        self._ancla_pendiente = False
        self._ref_ancla: tuple[float, float] | None = None
        self._simulacro_pendiente = False
        self._simulacro_id: str | None = None

        self._iniciado = False
        self._t = 0.0  # tiempo efectivo del modelo (época UTC)
        self._t_estoc = -1e18
        self._ext = EstadoExterior()
        self._exterior_real: Exterior | None = None
        self._dte = 0.0
        self._drh = 0.0
        self._dte_inicial = True
        self._usa_real = False
        self._ctx = _Ctx()
        self._ctx.estoc = False
        self._ctx.dt_e = 60.0

        self._eventos: list[_EventoInt] = []
        self._n_eventos = 0
        self._fallos: dict[str, tuple[str, dict | None]] = {}
        self._cache: dict[str, dict[str, float] | None] = {}
        self._precip: tuple[float, float] | None = None
        self._evacuados = 0.0
        self._occ_campus = 0.0
        self._flujo_campus = 0.0

        self._construir()

    # ------------------------------------------------------------------ construcción
    def _construir(self) -> None:
        cat = self.catalogo
        sem = self.semilla
        self._edificios: dict[str, _Edificio] = {}

        def edificio(bid: str) -> _Edificio:
            e = self._edificios.get(bid)
            if e is None:
                e = self._edificios[bid] = _Edificio(bid)
                e.c = clave("edificio:" + bid, sem)
            return e

        for bid in cat.edificios:
            edificio(bid)
        self._salas: dict[str, _Sala] = {}
        for i, esp in enumerate(cat.espacios.values()):
            s = self._nueva_sala(esp.id, esp.type, edificio(esp.building_id), i)
            s.esp = esp
            s.planta = esp.floor
            self._configurar_sala(s, esp.area_m2, esp.height_m, esp.capacity)
            self._salas[esp.id] = s
        # Cada edificio tiene una estancia agregada (la del catálogo de tipo `building` o una implícita)
        for e in self._edificios.values():
            agregadas = [s for s in e.salas if s.modo == _AGREGADA]
            if agregadas:
                e.agregada = agregadas[0]
            else:
                e.agregada = self._nueva_sala(f"{e.id}-*", "building", e, len(self._salas) + 1)
                self._configurar_sala(e.agregada, None, None, None)
            e.es_exterior = all(s.modo in (_EXTERIOR, _AGREGADA) for s in e.salas)

        # Sensores
        self._sensores: dict[str, _SensorSim] = {}
        por_sala: dict[str, list[Sensor]] = {}
        for sen in cat.sensores.values():
            por_sala.setdefault(sen.space_id, []).append(sen)
        for sen in cat.sensores.values():
            e = edificio(sen.building_id or "campus")
            sala = self._salas.get(sen.space_id)
            if sala is None:
                if e.agregada is None:
                    e.agregada = self._nueva_sala(f"{e.id}-*", "building", e, len(self._salas) + 1)
                    self._configurar_sala(e.agregada, None, None, None)
                sala = e.agregada
            else:
                e = sala.edif
            self._sensores[sen.id] = _SensorSim(sen, sala, e, clave("sensor:" + sen.id, sem))

        # Ventanas y persianas: una por sensor; si la estancia no tiene, unas virtuales
        for s in self._salas.values():
            if s.modo in (_EXTERIOR, _AGREGADA):
                continue
            sens = sorted(por_sala.get(s.id, []), key=lambda x: x.id)
            contactos = [x for x in sens if x.kind == "window_contact"]
            persianas = [x for x in sens if x.kind == "blind"]
            for i, x in enumerate(contactos):
                self._sensores[x.id].iv = i
            for i, x in enumerate(persianas):
                self._sensores[x.id].ip = i
            n_v = len(contactos) or (0 if s.p.vidrio <= 0 else max(1, min(4, round(s.area / 30.0))))
            s.ventanas = [False] * n_v
            n_p = len(persianas) or (1 if s.p.vidrio > 0.03 else 0)
            s.persianas_auto = bool(persianas)
            s.persianas = [round(30.0 * u01(s.c, 31, i) / 5.0) * 5.0 for i in range(n_p)]
            if any(x.kind == "pc_usage" for x in sens) and s.n_pc == 0:
                s.n_pc = 1

        # Pesos de las estancias de paso y del exterior
        for e in self._edificios.values():
            for modo in (_CIRCULACION, _ASEO):
                grupo = [s for s in e.salas if s.modo == modo]
                total = sum(s.area for s in grupo) or 1.0
                for s in grupo:
                    s.peso = s.area / total
            halls = [s for s in e.salas if s.tipo == "hall"]
            if halls:
                max(halls, key=lambda s: s.area).conserje = True
            e.area = sum(s.area for s in e.salas if s.modo not in (_EXTERIOR, _AGREGADA))
            e.base_w = 2.0 * e.area + (1000.0 if e.area > 0 else 300.0)
        exteriores = [s for s in self._salas.values() if s.modo == _EXTERIOR]
        total = sum(s.area for s in exteriores) or 1.0
        for s in exteriores:
            s.peso = s.area / total

        # Medidores, cámaras y wifi de edificio: se reparten el total con cuotas que suman 1
        grupos: dict[tuple[str, str], list[_SensorSim]] = {}
        for ss in self._sensores.values():
            k = ss.kind
            tipo = ss.sala.tipo
            if k == "power_meter" or (k != "solar_inverter" and "active_power" in ss.props and tipo in _MEDIDOR_EDIFICIO):
                if tipo in _MEDIDOR_EDIFICIO:
                    ss.medidor = "edificio"
                    grupos.setdefault((ss.edif.id, "ele"), []).append(ss)
                ss.e0 = 2.0e4 + 4.0e5 * u01(ss.c, 1)
            es_agua = k == "water_meter" or "water_volume" in ss.props or "water_flow" in ss.props
            if es_agua and tipo in _MEDIDOR_EDIFICIO:
                ss.medidor = "edificio"
                grupos.setdefault((ss.edif.id, "agua"), []).append(ss)
            if k == "solar_inverter" or "solar_power" in ss.props:
                ss.kwp = round(15.0 + 45.0 * u01(ss.c, 2))
                ss.e0 = ss.kwp * (1500.0 + 4000.0 * u01(ss.c, 3))
            if "occupancy" in ss.props and ss.sala.modo == _AGREGADA:
                grupos.setdefault((ss.edif.id + "|" + ss.sala.id, "cam"), []).append(ss)
            if k == "wifi_occupancy" or "wifi_clients" in ss.props:
                if ss.sala.modo == _AGREGADA or tipo == "technical_room":
                    ss.ap = "edificio"
                    grupos.setdefault((ss.edif.id, "ap"), []).append(ss)
                elif ss.sala.modo in (_CIRCULACION, _ASEO) or tipo == "other":
                    ss.ap = "planta"
                    grupos.setdefault((ss.edif.id + "|" + (ss.sala.planta or ""), "apl"), []).append(ss)
            if k == "heating_plant" or "water_temperature" in ss.props:
                texto = (ss.s.name + " " + ss.id).lower()
                ss.rol = "retorno" if ("retorno" in texto or "_ret" in texto) else "acs" if "acs" in texto else "impulsion"
        for (_, tipo_g), grupo in grupos.items():
            pesos = [0.6 + 0.8 * u01(ss.c, 4) for ss in grupo] if tipo_g in ("ele", "agua", "cam") else [1.0] * len(grupo)
            total = sum(pesos)
            for ss, p in zip(grupo, pesos):
                ss.cuota = p / total

        self._programadas = [s for s in self._salas.values() if s.modo in (_PROGRAMADA, _CAFETERIA)]
        self._derivadas = [s for s in self._salas.values() if s.modo in (_CIRCULACION, _ASEO)]
        self._exteriores = exteriores
        self._lista_edificios = list(self._edificios.values())
        self._inversores = [ss for ss in self._sensores.values() if ss.kwp > 0]

    def _nueva_sala(self, sid: str, tipo: str, e: _Edificio, idx: int) -> _Sala:
        s = _Sala()
        s.id = sid
        s.idx = idx
        s.tipo = tipo
        s.p = _TIPOS.get(tipo, _TIPOS["other"])
        s.edif = e
        s.c = clave("sala:" + sid, self.semilla)
        s.planta = ""
        s.modo = {"corridor": _CIRCULACION, "hall": _CIRCULACION, "restroom": _ASEO, "outdoor": _EXTERIOR,
                  "building": _AGREGADA, "cafeteria": _CAFETERIA}.get(tipo, _PROGRAMADA)
        e.salas.append(s)
        return s

    def _configurar_sala(self, s: _Sala, area: float | None, altura: float | None, capacidad: int | None) -> None:
        p = s.p
        c = s.c
        s.area = area if area and area > 0 else p.area
        s.vol = s.area * (altura if altura and altura > 0 else 3.0)
        cap = capacidad if capacidad and capacidad > 0 else max(1, round(s.area / p.m2_plaza))
        if s.tipo == "office":
            s.puestos = min(cap, 6) if capacidad else max(1, min(4, round(s.area / 10.0)))
            cap = max(cap, s.puestos)
        elif s.tipo == "lab":
            s.puestos = max(1, min(8, round(s.area / 15.0)))
        else:
            s.puestos = 1
        s.cap = cap
        s.orient = int(u01(c, 1) * 4)
        r = lambda k, a, b: a + (b - a) * u01(c, k)
        s.ua = p.ua * s.area * r(2, 0.8, 1.2)
        s.hm = 8.0 * s.area
        s.cm = 230e3 * s.area * r(3, 0.85, 1.15)
        s.ach_inf = p.ach * r(4, 0.7, 1.4)
        s.ach_mec = 2.0 if (s.tipo in ("computer_lab", "meeting_room", "auditorium", "cafeteria", "restroom")
                            and u01(c, 5) < 0.5) else 0.0
        s.pmax = p.rad * s.area * r(6, 0.85, 1.2)
        s.frio = (60.0 if s.tipo == "technical_room" else 90.0) * s.area if u01(c, 7) < p.ac else 0.0
        s.a_vid = p.vidrio * s.area * r(8, 0.6, 1.4)
        s.fl = p.fl * r(9, 0.6, 1.4)
        s.w_luces = p.luz * s.area
        s.lux_luces = p.lux * r(10, 0.8, 1.1)
        s.equipo_w = p.equipo * s.area
        s.w_persona = p.w_persona
        s.consigna = p.consigna + round(r(11, -1.5, 1.0) * 2.0) / 2.0
        if s.tipo == "computer_lab":
            s.n_pc, s.w_pc = cap, 90.0
        elif s.tipo == "technical_room":
            s.n_pc, s.w_pc = int(4 * u01(c, 12)), 120.0  # servidores o equipos de red
        elif s.tipo in ("office", "lab"):
            s.n_pc, s.w_pc = s.puestos, 110.0
        elif s.tipo in ("classroom", "auditorium"):
            s.n_pc, s.w_pc = 1, 120.0  # el ordenador del atril
        else:
            s.n_pc, s.w_pc = 0, 90.0

    # ------------------------------------------------------------------ escenarios
    @property
    def escenario(self) -> str:
        return self._cfg.id

    @escenario.setter
    def escenario(self, escenario_id: str) -> None:
        self.poner_escenario(escenario_id)

    def escenarios(self) -> list[Escenario]:
        return [Escenario(e.id, e.nombre, e.descripcion) for e in ESCENARIOS.values()]

    def poner_escenario(self, escenario_id: str) -> None:
        cfg = ESCENARIOS[escenario_id]  # KeyError si no existe
        anterior = self._cfg
        self._cfg = cfg
        self._ajuste = AjusteClima(cfg.anomalia, cfg.nubosidad_max)
        self._ancla_pendiente = cfg.ancla is not None
        if cfg.ancla is None:
            self._ref_ancla = None
        if anterior.simulacro_s is not None and self._simulacro_id is not None:
            for ei in self._eventos:
                if ei.ev.id == self._simulacro_id and ei.ev.active:
                    self._terminar(ei)
            self._simulacro_id = None
            self._aplicar_eventos()
        self._simulacro_pendiente = cfg.simulacro_s is not None
        for e in self._edificios.values():
            e.averia = cfg.averia_calefaccion
        self._cache.clear()

    def _tiempo_modelo(self, ta: float) -> float:
        if self._ref_ancla is None:
            return ta
        ref_reloj, ref_modelo = self._ref_ancla
        return ref_modelo + (ta - ref_reloj)

    def _fijar_ancla(self, ta: float) -> None:
        mes, dia_min, dia_semana, minuto = self._cfg.ancla
        anio = date.fromordinal(cal.local(ta)[0]).year
        d = date(anio, mes, dia_min)
        while d.weekday() != dia_semana or cal.tipo_dia(d.toordinal()) in (cal.FESTIVO, cal.VACACIONES):
            d = date.fromordinal(d.toordinal() + 1)
        self._ref_ancla = (ta, cal.utc_desde_local(d.toordinal(), minuto))

    @property
    def tiempo_efectivo(self) -> datetime | None:
        """Hora (UTC) que usa el modelo; distinta del reloj solo en escenarios anclados a una fecha."""
        return cal.a_datetime(self._t) if self._iniciado else None

    @property
    def fuente_clima(self) -> str:
        """'real' si la temperatura exterior viene de SmartPoliTech; 'simulated' si es sintética."""
        return "real" if self._usa_real else "simulated"

    # ------------------------------------------------------------------ avance
    def avanzar(self, ahora: datetime, dt_s: float, exterior: Exterior | None) -> None:
        ta = cal.a_epoca(ahora)
        self._exterior_real = exterior
        self._expirar(ta)
        if self._ancla_pendiente:
            self._fijar_ancla(ta)
            self._ancla_pendiente = False
        if self._simulacro_pendiente:
            self._simulacro_pendiente = False
            ei = self._nuevo_evento("fire_alarm", "campus", self._cfg.simulacro_s, {"drill": True}, ahora)
            self._simulacro_id = ei.ev.id
        t = self._tiempo_modelo(ta)
        dt_s = max(0.0, float(dt_s or 0.0))
        d = t - self._t
        if not self._iniciado or d < -1.0 or d > dt_s + 900.0:
            self._arrancar(t)
        elif d > 0.0:
            n = min(_MAX_SUBPASOS, max(1, math.ceil(d / _PASO_MAX)))
            h = d / n
            t0 = self._t
            for k in range(1, n + 1):
                self._paso(t0 + k * h, h)
        self._t = t
        self._agregar()
        self._cache.clear()
        self._precip = None

    def _arrancar(self, t: float) -> None:
        """Arranque en caliente: simula las horas previas de golpe para partir de un estado plausible."""
        t0 = t - _ARRANQUE_H * 3600.0
        if not self._iniciado:
            ext = self.clima.calcular(t0, self._ajuste)
            ordinal = cal.local(t0)[0]
            t_ini = 20.5 if cal.temporada_calefaccion(ordinal) else max(22.0, ext.te + 3.0)
            for s in self._salas.values():
                s.t = s.tm = t_ini
                s.w = ext.w
                s.co2 = 420.0
                s.pm = ext.pm25 * 0.6
            for e in self._edificios.values():
                e.t_imp = e.t_ret = 25.0
                e.t_media = t_ini
        self._t_estoc = -1e18
        h = (t - t0) / _ARRANQUE_PASOS
        self._dte_inicial = True
        for k in range(1, _ARRANQUE_PASOS + 1):
            self._paso(t0 + k * h, h)
        self._iniciado = True

    def _actualizar_exterior(self, c: _Ctx, t: float, h: float) -> None:
        ext = self.clima.calcular(t, self._ajuste)
        real = self._exterior_real if self._cfg.clima_real else None
        obj_t = obj_h = 0.0
        self._usa_real = False
        if real is not None and real.air_temperature is not None:
            obj_t = real.air_temperature - ext.te
            self._usa_real = True
            if real.relative_humidity is not None:
                obj_h = real.relative_humidity - ext.rh
        if self._dte_inicial:
            self._dte, self._drh = obj_t, obj_h
            self._dte_inicial = False
        else:
            k = math.exp(-h / 1800.0)
            self._dte = obj_t + (self._dte - obj_t) * k
            self._drh = obj_h + (self._drh - obj_h) * k
        if self._dte or self._drh:
            ext.te += self._dte
            ext.rh = _limitar(ext.rh + self._drh, 5.0, 100.0)
            ext.w = razon_humedad(ext.te, ext.rh)
        self._ext = ext
        c.ext = ext

    def _paso(self, t: float, h: float) -> None:
        c = self._ctx
        c.t = t
        c.h = h
        ordinal, minuto, dsem = cal.local(t)
        c.ordinal, c.minuto, c.dia_semana = ordinal, minuto, dsem
        td = cal.tipo_dia(ordinal)
        cfg = self._cfg
        if cfg.tipo_dia is not None and (not cfg.solo_laborables or td in cal.DIAS_LABORABLES):
            td = cfg.tipo_dia
        c.tipo_dia = td
        ap = cal.horario_edificio(td)
        c.abierto = ap is not None and ap[0] <= minuto < ap[1]
        c.cierre = ap is not None and ap[1] <= minuto < ap[1] + 60.0
        c.calefaccion = cal.temporada_calefaccion(ordinal)
        c.refrigeracion = cal.temporada_refrigeracion(ordinal)
        hc = cal.horario_calderas(td, dsem)
        c.calderas = c.calefaccion and hc is not None and hc[0] <= minuto < hc[1]
        c.confort = hc is not None and hc[0] + 30.0 <= minuto < hc[1]
        c.k_aire = math.exp(-h / 600.0)
        self._actualizar_exterior(c, t, h)
        c.estoc = t - self._t_estoc >= 60.0
        if c.estoc:
            c.dt_e = min(t - self._t_estoc, 6 * 3600.0)
            self._t_estoc = t

        edificios = self._lista_edificios
        for e in edificios:
            self._pre_edificio(e, c, h)
        paso_sala = self._paso_sala
        for s in self._programadas:
            paso_sala(s, c, h)
        occ = flujo = 0.0
        for e in edificios:
            e.flujo = e.mov / (2.0 * h)
            occ += e.occ_prim
            flujo += e.flujo
        for s in self._derivadas:
            paso_sala(s, c, h)
        evac = 0.0
        for e in edificios:
            occ += e.occ_circ
            evac += e.evacuados
        self._evacuados, self._occ_campus, self._flujo_campus = evac, occ, flujo
        for s in self._exteriores:
            paso_sala(s, c, h)
        for e in edificios:
            self._post_edificio(e, c, h)
        poa = c.ext.poa_fv
        for ss in self._inversores:
            if not (ss.edif.apagon or ss.sala.apagon):
                ss.acum += poa * 0.82 * h / 3.6e6

    def _pre_edificio(self, e: _Edificio, c: _Ctx, h: float) -> None:
        e.mov = e.occ_prim = e.occ_circ = e.occ_ext = e.carga = e.caudal = 0.0
        e.demanda_frac = e.demanda / e.pmax_tot if e.pmax_tot > 0 else 0.0
        e.demanda = e.pmax_tot = 0.0
        if e.alarma:
            e.k_ocup = math.exp(-h / 50.0)
        elif c.t - e.fin_alarma < 1800.0:
            e.k_ocup = math.exp(-h / 400.0)
            e.evacuados *= math.exp(-h / 600.0)
        else:
            e.k_ocup = math.exp(-h / 90.0)
            e.evacuados *= math.exp(-h / 600.0)
        on = not e.apagon and (e.calef == "on" or (e.calef != "off" and not e.averia and c.calderas))
        e.planta_on = on
        if on:
            obj, tau = _limitar(45.0 + 1.7 * (15.0 - c.ext.te), 45.0, 75.0), 1500.0
        else:
            obj, tau = max(c.ext.te, e.t_media - 3.0), 5400.0
        e.t_imp = obj + (e.t_imp - obj) * math.exp(-h / tau)
        e.t_ret = e.t_imp - (2.0 + 14.0 * e.demanda_frac if on else 0.8)

    def _post_edificio(self, e: _Edificio, c: _Ctx, h: float) -> None:
        if e.apagon:
            base = 0.0
        elif e.es_exterior:
            base = e.base_w + (8000.0 if c.ext.alt_sol < -0.05 else 0.0)  # alumbrado exterior
        else:
            base = e.base_w + (1800.0 if e.planta_on else 0.0)
        e.potencia = e.carga + base
        e.energia += e.potencia * h / 3.6e6
        if not e.es_exterior:
            e.caudal += 0.15 + e.fuga
        else:
            e.caudal += e.fuga
        if e.dia_agua != c.ordinal:
            e.dia_agua = c.ordinal
            e.agua_dia = 0.0
        e.agua_dia += e.caudal * h / 60.0

    # ------------------------------------------------------------------ estancia
    def _objetivo(self, s: _Sala, c: _Ctx) -> tuple[float, str]:
        e = s.edif
        modo = s.modo
        if modo == _PROGRAMADA:
            if e.alarma:
                return 0.0, ""
            if s.forz_ocup is not None:
                return s.forz_ocup, (("acto" if s.tipo == "auditorium" else "clase") if s.forz_ocup > 0 else "")
            dia = (c.ordinal, c.tipo_dia)
            if s.dia != dia:
                self._nuevo_dia(s, c)
            m = c.minuto
            tot = 0
            act = ""
            pr = 0
            for a, b, n, ac in s.iv:
                if a <= m < b:
                    tot += n
                    p = PRIORIDAD[ac]
                    if p > pr:
                        pr, act = p, ac
            # los intervalos que se solapan (personal y clase) se suman: como mucho el aforo y quien da la clase
            return float(min(tot, s.cap + 2)), act
        if modo == _CAFETERIA:
            if e.alarma:
                return 0.0, ""
            if s.forz_ocup is not None:
                return s.forz_ocup, ""
            dia = (c.ordinal, c.tipo_dia)
            if s.dia != dia:
                self._nuevo_dia(s, c)
            return ocupacion_cafeteria(c.minuto, c.tipo_dia, s.cap, s.factor_dia), "comida"
        if modo == _CIRCULACION:
            transito = e.flujo * 60.0 * s.peso
            if e.alarma:
                return transito, ""
            base = e.occ_prim * 0.035 * s.peso
            if s.conserje and c.abierto:
                base += 1.0
            return base + transito, ""
        if modo == _ASEO:
            return (0.0 if e.alarma else e.occ_prim * 0.012 * s.peso), ""
        # exterior: gente de paso y los desalojados en el punto de reunión
        return (self._evacuados + self._occ_campus * 0.01 + self._flujo_campus * 20.0) * s.peso, ""

    def _nuevo_dia(self, s: _Sala, c: _Ctx) -> None:
        s.dia = (c.ordinal, c.tipo_dia)
        k = c.ordinal
        s.iv = programar_dia(s.tipo, c.tipo_dia, s.cap, s.puestos, s.c, c.ordinal, c.dia_semana)
        s.luz_olvidada = u01(s.c, k, 21) < 0.08
        s.pc_resto = 0.08 * u01(s.c, k, 22)
        s.pc_noche = 1 if u01(s.c, k, 23) < 0.2 else 0
        s.factor_dia = 0.7 + 0.6 * u01(s.c, k, 24)

    def _paso_sala(self, s: _Sala, c: _Ctx, h: float) -> None:
        e = s.edif
        obj, act = self._objetivo(s, c)
        ant = s.occ
        occ = obj + (ant - obj) * e.k_ocup
        if occ < 0.02:
            occ = 0.0
        s.occ = occ
        s.actividad = act if occ >= 0.5 else ""
        d = occ - ant
        modo = s.modo
        if modo == _EXTERIOR:
            e.occ_ext += occ
            return
        if modo == _CIRCULACION or modo == _ASEO:
            e.occ_circ += occ
        else:
            e.occ_prim += occ
            e.mov += d if d > 0 else -d
        if e.alarma and d < 0:
            e.evacuados -= d

        ext = c.ext
        if c.estoc:
            self._estocastico(s, c)
        apagado = s.apagon or e.apagon
        nv = len(s.ventanas)
        f_ab = s.n_abiertas / nv if nv else 0.0
        b = sum(s.persianas) / (100.0 * len(s.persianas)) if s.persianas else 0.0

        # Luz natural, luces y equipos
        s.e_dia = ext.ghi * 115.0 * s.fl * (1.0 - 0.85 * b)
        tipo = s.tipo
        if apagado:
            luces = False
        elif occ >= 0.5:
            luces = s.e_dia < 0.6 * s.lux_luces
        elif modo == _CIRCULACION:
            luces = c.abierto and s.e_dia < 200.0
        else:
            luces = s.luz_olvidada and c.abierto and s.e_dia < 300.0
        s.luces = luces
        if apagado:
            pcs = 0
        elif tipo == "computer_lab":
            pcs = min(s.n_pc, int(occ * 0.93 + 0.5) + int(s.n_pc * (s.pc_resto if c.abierto else 0.02)))
        elif tipo == "technical_room":
            pcs = s.n_pc
        elif tipo in ("office", "lab", "other", "meeting_room"):
            pcs = min(max(s.n_pc, 1), int(occ + 0.5) + s.pc_noche)
        elif tipo in ("classroom", "auditorium"):
            pcs = 1 if act in ("clase", "acto") else 0
        else:
            pcs = s.n_pc if c.abierto else 0
        s.pcs = pcs
        proyector = 250.0 if act in ("clase", "practica", "acto") else 0.0
        q_int = (75.0 * occ + 0.9 * pcs * s.w_pc + (s.w_luces if luces else 0.0) + s.equipo_w + proyector) if not apagado \
            else 75.0 * occ + 0.3 * s.equipo_w
        sol = s.a_vid * ext.fachadas[s.orient] * (1.0 - 0.75 * b)

        # Térmico: aire casi estacionario acoplado a la masa, solución exacta en el paso
        mec = s.ach_mec if (c.abierto and not apagado) else 0.0
        ach = s.ach_inf + mec + f_ab * 6.0 * (1.0 + 0.08 * ext.viento)
        H = s.ua + _P_ATM * s.vol * (ach - 0.6 * mec)  # la ventilación mecánica recupera el 60 % del calor
        Hm = s.hm
        A = H * ext.te + q_int + 0.4 * sol
        D = H + Hm
        tm = s.tm
        v = 0.0
        pe = 0.0
        lineal = False
        if s.pmax > 0.0:
            if s.calef == "off":
                sp = 5.0
            elif s.calef == "on" or c.confort:
                sp = s.consigna
            else:
                sp = 15.5
            s.sp = sp
            dif = e.t_imp - s.t
            s.phi = phi = (dif / 50.0) ** 1.3 if dif > 0.0 else 0.0
            if phi > 1.3:
                s.phi = phi = 1.3
            pe = s.pmax * phi
            if s.calef == "off":
                v = 0.0
            elif pe < 1.0:
                tq = (A + Hm * tm) / D
                v = _limitar((sp + 0.5 - tq) / 1.5, 0.0, 1.0)
                pe = 0.0
            else:
                if (A + pe + Hm * tm) / D <= sp - 1.0:
                    v = 1.0
                    A += pe
                elif (A + Hm * tm) / D >= sp + 0.5:
                    v = 0.0
                else:
                    g = pe / 1.5
                    A += g * (sp + 0.5)
                    D += g
                    lineal = True  # la válvula se calcula con la temperatura final
        # Aire acondicionado
        frio = 0.0
        spc = 24.0 if tipo == "technical_room" else 25.0
        if s.frio > 0.0 and not apagado and v <= 0.0 and not lineal and (
                tipo == "technical_room" or (occ >= 0.5 and (c.refrigeracion or s.t > 26.0))):
            if (A - s.frio + Hm * tm) / D >= spc + 0.5:
                A -= s.frio
                frio = -1.0
            elif (A + Hm * tm) / D > spc - 0.5:
                g = s.frio
                A += g * (spc - 0.5)
                D += g
                frio = -2.0
        k = Hm * (D - Hm) / D
        tm_eq = (Hm * A / D + 0.6 * sol) / k
        tm = tm_eq + (tm - tm_eq) * math.exp(-k * h / s.cm)
        s.tm = tm
        tq = (A + Hm * tm) / D
        if lineal:
            v = _limitar((s.sp + 0.5 - tq) / 1.5, 0.0, 1.0)
        s.valvula = v
        s.q_rad = q_rad = pe * v
        if frio == -1.0:
            q_frio = s.frio
        elif frio == -2.0:
            q_frio = _limitar(tq - (spc - 0.5), 0.0, 1.0) * s.frio
        else:
            q_frio = 0.0
        s.q_frio = q_frio
        s.t = tq + (s.t - tq) * c.k_aire
        e.demanda += q_rad
        e.pmax_tot += s.pmax

        # CO₂, humedad y partículas
        a = ach / 3600.0
        co2_eq = ext.co2 + occ * 18000.0 / (s.vol * ach)
        s.co2 = co2_eq + (s.co2 - co2_eq) * math.exp(-a * h)
        vapor = occ * 0.055 + (0.5 if s.fuga > 0.0 else 0.0) + (0.4 if (modo == _CAFETERIA and occ > 0.5) else 0.0)
        w_eq = ext.w + vapor / (1.2 * s.vol * ach)
        s.w = w_eq + (s.w - w_eq) * math.exp(-a * h / 3.0)
        r = ach + 0.4
        fuente = occ * 50.0 + (40000.0 if s.humo else 0.0) + (3000.0 if (modo == _CAFETERIA and occ > 0.5) else 0.0)
        pm_eq = (ach * 0.8 * ext.pm25 + fuente / s.vol) / r
        s.pm = pm_eq + (s.pm - pm_eq) * math.exp(-r * h / 3600.0)

        # Consumos
        if apagado:
            carga = 0.0
        else:
            carga = ((s.w_luces if luces else 0.0) + pcs * s.w_pc + s.equipo_w + occ * s.w_persona + proyector
                     + q_frio / 3.0)
        s.carga = carga
        e.carga += carga
        s.energia += carga * h / 3.6e6
        if tipo == "restroom":
            caudal = occ * 1.6
        elif modo == _CAFETERIA:
            caudal = occ * 0.06 + (1.2 if occ > 0.5 else 0.0)
        elif tipo == "lab":
            caudal = occ * 0.12
        else:
            caudal = 0.0
        caudal += s.fuga
        s.caudal = caudal
        e.caudal += caudal
        if s.dia_agua != c.ordinal:
            s.dia_agua = c.ordinal
            s.agua_dia = 0.0
        s.agua_dia += caudal * h / 60.0

    def _estocastico(self, s: _Sala, c: _Ctx) -> None:
        """Ventanas y persianas: decisiones de la gente, como mucho una vez por minuto simulado."""
        m = c.dt_e / 60.0
        vs = s.ventanas
        n = len(vs)
        ext = c.ext
        if n:
            forz = s.vent_forz
            if forz:
                for i, val in forz.items():
                    vs[i] = val
            if not forz or len(forz) < n:
                ti, te, occ = s.t, ext.te, s.occ
                if occ >= 0.5:
                    if te < 6.0:
                        pa = 0.0003
                    elif te < 12.0:
                        pa = 0.001
                    elif te < 18.0:
                        pa = 0.004
                    elif te <= ti + 1.0:
                        pa = 0.008
                    else:
                        pa = 0.0008
                    if s.co2 > 1000.0:
                        pa += 0.004 if te > 10.0 else 0.002
                    if s.co2 > 1500.0:
                        pa += 0.008
                    if ti > 25.5 and te < ti:
                        pa += 0.01
                    pc = 0.002
                    if te < 12.0:
                        pc += 0.02
                    if ti < 19.5:
                        pc += 0.03
                    if s.co2 < 700.0 and te < 15.0:
                        pc += 0.015
                    if te > ti + 2.0:
                        pc += 0.02
                else:
                    pa = 0.00003
                    pc = 0.06 if c.cierre else 0.001
                if ext.lluvia > 0.5:
                    pa *= 0.2
                    pc += 0.03
                libres = [i for i in range(n) if not forz or i not in forz]
                cerradas = [i for i in libres if not vs[i]]
                abiertas = [i for i in libres if vs[i]]
                u = u01(s.c, int(c.t), 41)
                if cerradas and u < 1.0 - (1.0 - pa) ** (m * len(cerradas)):
                    vs[cerradas[int(u01(s.c, int(c.t), 42) * len(cerradas))]] = True
                u = u01(s.c, int(c.t), 43)
                if abiertas and u < 1.0 - (1.0 - pc) ** (m * len(abiertas)):
                    vs[abiertas[int(u01(s.c, int(c.t), 44) * len(abiertas))]] = False
            s.n_abiertas = sum(vs)
        ps = s.persianas
        if ps:
            directa = ext.fachadas_dir[s.orient]
            auto = s.persianas_auto
            obj = None
            if s.occ >= 0.5 or auto:
                if directa > 150.0:
                    obj = 85.0 if c.refrigeracion else 65.0
                elif ext.alt_sol > 0.05 and directa < 60.0 and not c.refrigeracion and s.occ >= 0.5:
                    obj = 0.0
                elif auto and not c.abierto and c.calefaccion and ext.alt_sol < 0.0:
                    obj = 100.0  # de noche en invierno, bajadas para aislar
            if obj is not None:
                p_mov = 1.0 - (1.0 - (0.5 if auto else 0.1)) ** m
                for i, pos in enumerate(ps):
                    if abs(pos - obj) > 25.0 and u01(s.c, int(c.t), 50 + i) < p_mov:
                        ps[i] = _limitar(obj + round((u01(s.c, c.ordinal, 60 + i) - 0.5) * 4.0) * 5.0, 0.0, 100.0)

    def _agregar(self) -> None:
        """Medias y totales por edificio para los sensores de edificio."""
        for e in self._edificios.values():
            st = srh = sco = spm = sa = 0.0
            pcs = 0
            luces = False
            abiertas = 0
            plantas: dict[str, float] = {}
            for s in e.salas:
                if s.modo in (_EXTERIOR, _AGREGADA):
                    continue
                a = s.area
                st += s.t * a
                srh += humedad_relativa(s.t, s.w) * a
                sco += s.co2 * a
                spm += s.pm * a
                sa += a
                pcs += s.pcs
                luces = luces or s.luces
                abiertas += s.n_abiertas
                plantas[s.planta] = plantas.get(s.planta, 0.0) + s.occ
            if sa > 0:
                e.t_media, e.rh_media, e.co2_media, e.pm_media = st / sa, srh / sa, sco / sa, spm / sa
            else:
                ext = self._ext
                e.t_media, e.rh_media, e.co2_media, e.pm_media = ext.te, ext.rh, ext.co2, ext.pm25
            e.pcs = pcs
            e.luces = luces
            e.abiertas = abiertas
            e.ocup_planta = plantas
            e.occ_total = e.occ_prim + e.occ_circ + e.occ_ext

    # ------------------------------------------------------------------ eventos
    def _resolver(self, objetivo: str) -> tuple[list[_Sala], list[_Edificio], list[str], list[tuple[_Sala, int]]]:
        if objetivo == "campus":
            return ([s for s in self._salas.values()], list(self._edificios.values()), list(self._sensores), [])
        if objetivo in self.catalogo.edificios or (objetivo in self._edificios and objetivo != "campus"):
            e = self._edificios[objetivo]
            sens = [ss.id for ss in self._sensores.values() if ss.edif is e]
            return list(e.salas), [e], sens, []
        if objetivo in self._salas:
            s = self._salas[objetivo]
            return [s], [s.edif], [ss.id for ss in self._sensores.values() if ss.sala is s], []
        if objetivo in self._sensores:
            ss = self._sensores[objetivo]
            ventanas = [(ss.sala, ss.iv)] if ss.iv is not None else []
            return [ss.sala], [ss.edif], [ss.id], ventanas
        raise ValueError(f"objetivo desconocido: '{objetivo}' (debe ser un sensor, una estancia, un edificio o 'campus')")

    def crear_evento(self, tipo: str, objetivo: str, duracion_s: float | None, params: dict, ahora: datetime) -> Evento:
        if tipo not in TIPOS_EVENTO:
            raise ValueError(f"tipo de evento desconocido: '{tipo}' (válidos: {', '.join(TIPOS_EVENTO)})")
        if duracion_s is not None and not (0 <= duracion_s <= DURACION_MAX_S):
            raise ValueError(f"duration_s debe estar entre 0 y {DURACION_MAX_S:.0f} s (30 días)")
        params = dict(params or {})
        if tipo == "class_start" and "people" in params:
            try:
                n = float(params["people"])
                if not (math.isfinite(n) and n == int(n) and 0 <= n <= 10000):
                    raise ValueError
            except (TypeError, ValueError, OverflowError):
                raise ValueError("params.people debe ser un entero entre 0 y 10000") from None
        if "flow_l_min" in params:
            # (NaN o infinito pasaban la validación y rompían el tick en cada ciclo)
            try:
                q = float(params["flow_l_min"])
                if not (math.isfinite(q) and 0 < q <= 600):
                    raise ValueError
            except (TypeError, ValueError, OverflowError):
                raise ValueError("params.flow_l_min debe ser un caudal entre 0 y 600 L/min") from None
        if tipo == "sensor_failure" and params.get("mode", "none") not in ("none", "frozen"):
            raise ValueError("params.mode debe ser 'none' o 'frozen'")
        return self._nuevo_evento(tipo, objetivo, duracion_s, params, ahora).ev

    def _nuevo_evento(self, tipo: str, objetivo: str, duracion_s: float | None, params: dict,
                      ahora: datetime) -> _EventoInt:
        salas, edificios, sensores, ventanas = self._resolver(objetivo)
        ta = cal.a_epoca(ahora)
        if tipo == "class_start":
            if duracion_s is None:
                duracion_s = 3000.0  # una clase de 50 minutos si no se dice otra cosa
            if objetivo in self._salas or objetivo in self._sensores:
                salas = [s for s in salas if s.modo in (_PROGRAMADA, _CAFETERIA)]
            else:
                salas = [s for s in salas if s.tipo in _TIPOS_DOCENTES]
        if tipo == "class_end":
            if duracion_s is None:
                duracion_s = float(round(self._resto_de_clase(salas)))
            for ei in self._eventos:
                if ei.ev.active and ei.ev.type == "class_start" and set(map(id, ei.salas)) <= set(map(id, salas)):
                    self._terminar(ei)
        self._n_eventos += 1
        ev = Evento(f"evt-{self._n_eventos:05d}", tipo, objetivo, ahora, duracion_s, params)
        fin = ta + duracion_s if duracion_s is not None else None
        ei = _EventoInt(ev, salas, edificios, sensores, ventanas, fin, self._n_eventos)
        if tipo == "sensor_failure":
            modo = params.get("mode", "none")
            for sid in sensores:
                congelado = self._leer_sin_fallo(sid) if modo == "frozen" else None
                self._fallos[sid] = (modo, congelado)
        self._eventos.append(ei)
        self._aplicar_eventos()
        return ei

    def _resto_de_clase(self, salas: list[_Sala]) -> float:
        """Lo que queda de la clase programada en curso (para `class_end` sin duración)."""
        if not self._iniciado:
            return 600.0
        m = cal.local(self._t)[1]
        resto = 0.0
        for s in salas:
            for a, b, _n, _ac in s.iv:
                if a <= m < b:
                    resto = max(resto, (b - m) * 60.0)
        return resto if resto > 0 else 600.0

    def _expirar(self, ta: float) -> None:
        cambio = False
        for ei in self._eventos:
            if ei.ev.active and ei.fin is not None and ta >= ei.fin:
                self._terminar(ei, aplicar=False)
                cambio = True
        if cambio:
            self._aplicar_eventos()

    def _terminar(self, ei: _EventoInt, aplicar: bool = True) -> None:
        ei.ev.active = False
        if ei.ev.type == "open_windows":
            # al acabar, la gente las cierra
            for s, i in (ei.ventanas or [(s, None) for s in ei.salas]):
                if i is None:
                    for j in range(len(s.ventanas)):
                        s.ventanas[j] = False
                else:
                    s.ventanas[i] = False
                s.n_abiertas = sum(s.ventanas)
        if aplicar:
            self._aplicar_eventos()

    def eventos(self) -> list[Evento]:
        activos = [ei.ev for ei in self._eventos if ei.ev.active]
        terminados = [ei.ev for ei in reversed(self._eventos) if not ei.ev.active]
        return activos + terminados[:_HISTORIAL_EVENTOS]

    def terminar_evento(self, evento_id: str) -> None:
        for ei in self._eventos:
            if ei.ev.id == evento_id:
                if ei.ev.active:
                    self._terminar(ei)
                return
        raise KeyError(evento_id)

    def _aplicar_eventos(self) -> None:
        """Recalcula los efectos de los eventos activos (los posteriores mandan sobre los anteriores)."""
        alarmas_antes = {e.id for e in self._edificios.values() if e.alarma}
        for s in self._salas.values():
            s.forz_ocup = None
            s.vent_forz = None
            s.calef = None
            s.apagon = False
            s.fuga = 0.0
            s.humo = False
        for e in self._edificios.values():
            e.alarma = False
            e.apagon = False
            e.calef = None
            e.fuga = 0.0
        fallos: dict[str, tuple[str, dict | None]] = {}
        # Purga el historial antiguo
        activos = [ei for ei in self._eventos if ei.ev.active]
        terminados = [ei for ei in self._eventos if not ei.ev.active][-_HISTORIAL_EVENTOS:]
        self._eventos = sorted(activos + terminados, key=lambda x: x.seq)
        todo_campus = len(self._edificios)
        for ei in activos:
            ev = ei.ev
            tipo = ev.type
            p = ev.params
            edificio_entero = ev.target in self._edificios or ev.target == "campus"
            if tipo in ("open_windows", "close_windows"):
                val = tipo == "open_windows"
                if ei.ventanas:
                    for s, i in ei.ventanas:
                        s.vent_forz = dict(s.vent_forz or {})
                        s.vent_forz[i] = val
                else:
                    for s in ei.salas:
                        if s.ventanas:
                            s.vent_forz = {i: val for i in range(len(s.ventanas))}
            elif tipo == "class_start":
                for s in ei.salas:
                    n = p.get("people")
                    # (como mucho el aforo de la sala: con people=100000 un aula de 54 plazas llegaba a 2498 personas)
                    s.forz_ocup = float(min(int(float(n)), s.cap)) if n is not None else float(round(0.75 * s.cap))
            elif tipo == "class_end":
                for s in ei.salas:
                    if s.modo in (_PROGRAMADA, _CAFETERIA):
                        s.forz_ocup = 0.0
            elif tipo in ("heating_off", "heating_on"):
                val = "off" if tipo == "heating_off" else "on"
                if edificio_entero:
                    for e in ei.edificios:
                        e.calef = val
                    for s in ei.salas:
                        s.calef = val
                else:
                    for s in ei.salas:
                        s.calef = val
                    if val == "on":
                        for e in ei.edificios:
                            e.calef = "on"
            elif tipo == "fire_alarm":
                for e in ei.edificios:
                    e.alarma = True
                if p.get("smoke"):
                    for s in (ei.salas if not edificio_entero else []):
                        s.humo = True
            elif tipo == "power_outage":
                if edificio_entero:
                    for e in ei.edificios:
                        e.apagon = True
                for s in ei.salas:
                    s.apagon = True
            elif tipo == "water_leak":
                caudal = float(p.get("flow_l_min", 6.0))
                if edificio_entero:
                    for e in ei.edificios:
                        e.fuga += caudal / (1 if ev.target != "campus" else todo_campus)
                else:
                    for s in ei.salas:
                        s.fuga += caudal
            elif tipo == "sensor_failure":
                modo = p.get("mode", "none")
                for sid in ei.sensores:
                    previo = self._fallos.get(sid)
                    fallos[sid] = previo if previo and previo[0] == modo else (modo, None)
        # Ventanas forzadas: el estado cambia en el acto
        for s in self._salas.values():
            if s.vent_forz:
                for i, val in s.vent_forz.items():
                    s.ventanas[i] = val
                s.n_abiertas = sum(s.ventanas)
        for e in self._edificios.values():
            if e.id in alarmas_antes and not e.alarma:
                e.fin_alarma = self._t
        self._fallos = fallos
        self._cache.clear()

    # ------------------------------------------------------------------ lecturas
    def lectura(self, sensor_id: str) -> dict[str, float] | None:
        f = self._fallos.get(sensor_id)
        if f is not None:
            return dict(f[1]) if (f[0] == "frozen" and f[1] is not None) else None
        return self._leer_sin_fallo(sensor_id)

    def _leer_sin_fallo(self, sensor_id: str) -> dict[str, float] | None:
        if not self._iniciado:
            return None
        r = self._cache.get(sensor_id)
        if r is not None or sensor_id in self._cache:
            return dict(r) if r is not None else None
        ss = self._sensores.get(sensor_id)
        if ss is None:
            return None
        if ss.lectores is None:
            ss.lectores = self._lectores(ss)
        t = self._t
        out: dict[str, float] = {}
        for prop, f, post, ruido, sesgo, nd in ss.lectores:
            v = float(f(ss))
            if post is None:  # booleanos
                out[prop] = 1.0 if v >= 0.5 else 0.0
                continue
            amp, rel, _periodo, _s, paso, lo, hi = post
            if ruido is not None:
                n = ruido.valor(t)
                v += amp * n + rel * v * n
            v += sesgo
            if lo is not None and v < lo:
                v = lo
            if hi is not None and v > hi:
                v = hi
            if paso:
                v = round(round(v / paso) * paso, nd)
            out[prop] = float(v)
        self._cache[sensor_id] = out
        return dict(out)

    def _lectores(self, ss: _SensorSim) -> list[tuple]:
        """Para cada propiedad: función, post-proceso, ruido con su caché, sesgo de calibración y decimales."""
        lectores = []
        for prop in ss.props:
            f = getattr(self, "_p_" + prop, None)
            if f is None:
                continue  # propiedad fuera del vocabulario
            post = _POST.get(prop)
            ruido = sesgo = None
            nd = 0
            if post is not None:
                amp, rel, periodo, sesgo_max, paso, _lo, _hi = post
                cp = ss.c ^ clave(prop, 0)
                ruido = RuidoSensor(cp, periodo) if (amp or rel) else None
                sesgo = sesgo_max * (2.0 * u01(cp, 99) - 1.0)
                nd = _decimales(paso) if paso else 0
            lectores.append((prop, f, post, ruido, sesgo or 0.0, nd))
        return lectores

    def _exterior_de(self, ss: _SensorSim) -> bool:
        return ss.kind == "weather_station" or ss.sala.modo == _EXTERIOR or (
            ss.sala.modo == _AGREGADA and ss.edif.es_exterior)

    # Una función por propiedad del vocabulario (`_p_<propiedad>`)
    def _p_air_temperature(self, ss: _SensorSim) -> float:
        s = ss.sala
        if self._exterior_de(ss):
            return self._ext.te
        if s.modo == _AGREGADA:
            return ss.edif.t_media
        v = s.t
        if ss.kind == "radiator_valve":
            v += 0.8 * s.valvula * min(1.0, s.phi)  # la válvula está pegada al radiador
        elif ss.kind == "window_contact" and ss.iv is not None and s.ventanas[ss.iv]:
            v += 0.3 * (self._ext.te - s.t)
        return v

    def _p_relative_humidity(self, ss: _SensorSim) -> float:
        s = ss.sala
        if self._exterior_de(ss):
            return self._ext.rh
        if s.modo == _AGREGADA:
            return ss.edif.rh_media
        t = self._p_air_temperature(ss)
        return humedad_relativa(t, s.w)

    def _p_co2_concentration(self, ss: _SensorSim) -> float:
        s = ss.sala
        if self._exterior_de(ss):
            return self._ext.co2
        if s.modo == _AGREGADA:
            return ss.edif.co2_media
        return s.co2

    def _p_window_open(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.modo == _EXTERIOR:
            return 0.0
        if s.modo == _AGREGADA:
            return 1.0 if ss.edif.abiertas > 0 else 0.0
        if ss.iv is not None and ss.iv < len(s.ventanas):
            return 1.0 if s.ventanas[ss.iv] else 0.0
        return 1.0 if s.n_abiertas > 0 else 0.0

    def _p_door_open(self, ss: _SensorSim) -> float:
        s = ss.sala
        c = self._ctx
        if s.modo in (_CIRCULACION, _AGREGADA, _EXTERIOR):
            p = 0.15 + min(0.7, ss.edif.flujo * 0.5) if c.abierto else 0.0
        elif s.occ < 0.5:
            p = 0.03
        elif s.actividad in ("clase", "examen", "practica", "reunion", "acto"):
            p = 0.1
        elif s.tipo == "office":
            p = 0.6
        else:
            p = 0.7  # cambio de clase o uso libre
        return 1.0 if u01(ss.c, int(self._t // 120.0), 5) < p else 0.0

    def _p_valve_position(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.pmax <= 0.0 or s.calef == "off":
            return 0.0
        v = s.valvula
        if 0.0 < v < 1.0:
            v = _limitar(v + ss.desvio_v, 0.0, 1.0)
        return 100.0 * v

    def _p_target_temperature(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.pmax <= 0.0:
            return 15.5
        return s.sp

    def _p_battery_voltage(self, ss: _SensorSim) -> float:
        # Descarga lenta (unos 2,5 años por pila) y cambio de pila al agotarse
        dias = self._t / 86400.0
        frac = (ss.bat0 - dias / 900.0) % 1.0
        return 2.55 + 1.05 * (1.0 - (1.0 - frac) ** 3)

    def _p_active_power(self, ss: _SensorSim) -> float:
        if ss.kind == "solar_inverter":
            return self._p_solar_power(ss)
        if ss.medidor == "edificio":
            return ss.cuota * ss.edif.potencia
        s = ss.sala
        if s.modo == _AGREGADA:
            return ss.edif.potencia
        return 0.0 if (s.apagon or ss.edif.apagon) else s.carga

    def _p_active_energy(self, ss: _SensorSim) -> float:
        if ss.kwp > 0:
            return ss.e0 + ss.kwp * ss.acum
        if ss.medidor == "edificio" or ss.sala.modo == _AGREGADA:
            return ss.e0 + ss.cuota * ss.edif.energia
        return ss.e0 + ss.sala.energia

    def _p_water_volume(self, ss: _SensorSim) -> float:
        """Litros desde las 00:00 (hora de Madrid)."""
        if ss.medidor == "edificio" or ss.sala.modo == _AGREGADA:
            return ss.cuota * ss.edif.agua_dia
        return ss.sala.agua_dia

    def _p_water_flow(self, ss: _SensorSim) -> float:
        if ss.medidor == "edificio" or ss.sala.modo == _AGREGADA:
            return ss.cuota * ss.edif.caudal
        return ss.sala.caudal

    def _p_occupancy(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.modo == _AGREGADA:
            return ss.cuota * (self._occ_exterior() if ss.edif.es_exterior else ss.edif.occ_total)
        return s.occ

    def _occ_exterior(self) -> float:
        return sum(s.occ for s in self._exteriores)

    def _p_devices_on(self, ss: _SensorSim) -> float:
        s = ss.sala
        return ss.edif.pcs if s.modo == _AGREGADA else s.pcs

    def _p_power_on(self, ss: _SensorSim) -> float:
        if ss.sala.apagon or ss.edif.apagon:
            return 0.0
        if self._ctx.abierto:
            return 1.0
        return 1.0 if u01(ss.c, self._ctx.ordinal, 6) < 0.05 else 0.0

    def _p_blind_position(self, ss: _SensorSim) -> float:
        ps = ss.sala.persianas
        if not ps:
            return 0.0
        if ss.ip is not None and ss.ip < len(ps):
            return ps[ss.ip]
        return sum(ps) / len(ps)

    def _p_sound_level(self, ss: _SensorSim) -> float:
        s = ss.sala
        c = self._ctx
        if s.modo == _EXTERIOR or self._exterior_de(ss):
            trafico = 52.0 if c.abierto else 42.0
            n = self._occ_exterior() if s.modo == _AGREGADA else s.occ
            return 10.0 * math.log10(10 ** (trafico / 10.0) + n * 0.5 * 10 ** 5.2 + 10 ** (self._ext.viento * 0.8 + 3.5))
        if s.modo == _AGREGADA:
            n, por_persona, extra = ss.edif.occ_circ, 50.0, 0.0
        else:
            n = s.occ
            act = s.actividad
            extra = 0.0
            if s.modo == _CIRCULACION:
                por_persona, n = 52.0, n * 0.5
            elif act == "examen":
                por_persona = 24.0
            elif act in ("clase", "practica"):
                por_persona, extra = 34.0, 10 ** (5.6 + 0.8 * u01(s.c, 71))  # el profesor
            elif act == "acto":
                por_persona, extra = 30.0, 10 ** (6.2 + 0.5 * u01(s.c, 71))  # el ponente, con micrófono
            elif s.modo == _CAFETERIA:
                por_persona, n = 52.0, n * 0.6
            else:
                por_persona, n = 47.0, n * 0.5
        fondo = 31.0 + (12.0 if s.n_abiertas else 0.0) + (4.0 if s.pcs else 0.0)
        return 10.0 * math.log10(10 ** (fondo / 10.0) + n * 10 ** (por_persona / 10.0) + extra)

    def _p_illuminance(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.modo == _EXTERIOR or self._exterior_de(ss):
            return self._ext.lux + (15.0 if self._ext.alt_sol < -0.05 else 0.0)
        if s.modo == _AGREGADA:
            return 250.0 if ss.edif.luces else 0.02 * self._ext.lux
        return s.e_dia + (s.lux_luces if s.luces else 0.0)

    def _p_pm25(self, ss: _SensorSim) -> float:
        s = ss.sala
        if self._exterior_de(ss):
            return self._ext.pm25
        if s.modo == _AGREGADA:
            return ss.edif.pm_media
        return s.pm

    def _p_water_temperature(self, ss: _SensorSim) -> float:
        e = ss.edif
        if ss.rol == "acs":
            return 56.0
        return e.t_ret if ss.rol == "retorno" else e.t_imp

    def _p_wind_speed(self, ss: _SensorSim) -> float:
        return self._ext.viento

    def _p_wind_direction(self, ss: _SensorSim) -> float:
        return self._ext.direccion

    def _p_atmospheric_pressure(self, ss: _SensorSim) -> float:
        return self._ext.presion

    def _p_precipitation(self, ss: _SensorSim) -> float:
        """mm acumulados desde las 00:00 (hora de Madrid)."""
        if self._precip is None or self._precip[0] != self._t:
            v = self.clima.precipitacion_dia(self._t, cal.medianoche_local(self._t), self._ajuste)
            self._precip = (self._t, v)
        return self._precip[1]

    def _p_solar_power(self, ss: _SensorSim) -> float:
        if ss.sala.apagon or ss.edif.apagon:
            return 0.0
        return (ss.kwp or 20.0) * self._ext.poa_fv * 0.82

    def _p_lights_on(self, ss: _SensorSim) -> float:
        s = ss.sala
        if s.modo == _EXTERIOR:
            return 1.0 if self._ext.alt_sol < -0.05 else 0.0
        return (1.0 if ss.edif.luces else 0.0) if s.modo == _AGREGADA else (1.0 if s.luces else 0.0)

    def _p_alarm_active(self, ss: _SensorSim) -> float:
        return 1.0 if (ss.edif.alarma or ss.sala.humo) else 0.0

    def _p_wifi_clients(self, ss: _SensorSim) -> float:
        e = ss.edif
        if e.apagon or ss.sala.apagon:
            return 0.0
        por_persona = 1.3 + 0.4 * u01(ss.c, self._ctx.ordinal, 7)
        if ss.ap == "edificio":
            gente = (self._occ_exterior() if e.es_exterior else e.occ_total) * ss.cuota
            base = 12.0 * ss.cuota
        elif ss.ap == "planta":
            gente = e.ocup_planta.get(ss.sala.planta, 0.0) * ss.cuota
            base = 3.0
        else:
            gente = ss.sala.occ
            base = 2.0
        return base + gente * por_persona

    # ------------------------------------------------------------------ extras
    def exterior_actual(self) -> dict[str, float]:
        """Condiciones exteriores que usa el modelo (para el `weather` del estado del campus)."""
        e = self._ext
        return {"air_temperature": round(e.te, 1), "relative_humidity": round(e.rh), "wind_speed": round(e.viento, 1),
                "wind_direction": round(e.direccion), "atmospheric_pressure": round(e.presion, 1),
                "precipitation_rate": round(e.lluvia, 1), "cloud_cover": round(e.nubosidad, 2),
                "solar_irradiance": round(e.ghi), "sun_elevation": round(math.degrees(e.alt_sol), 1),
                "co2_concentration": round(e.co2), "pm25": round(e.pm25, 1)}

    def calefaccion_en_marcha(self, edificio_id: str) -> bool | None:
        """Si la caldera del edificio da agua caliente ahora mismo (None si no se simula ese edificio)."""
        e = self._edificios.get(edificio_id)
        return None if e is None or not self._iniciado else bool(getattr(e, "planta_on", False))

    def estado_espacio(self, espacio_id: str) -> dict | None:
        """Estado interno de una estancia (sin ruido de sensor), para depurar y para pruebas."""
        s = self._salas.get(espacio_id)
        if s is None or not self._iniciado:
            return None
        return {"occupancy": s.occ, "activity": s.actividad, "air_temperature": s.t, "mass_temperature": s.tm,
                "relative_humidity": humedad_relativa(s.t, s.w), "co2_concentration": s.co2, "pm25": s.pm,
                "windows_open": s.n_abiertas, "windows_total": len(s.ventanas), "valve": s.valvula,
                "setpoint": s.sp, "heating_w": s.q_rad, "cooling_w": s.q_frio, "lights_on": s.luces,
                "illuminance_daylight": s.e_dia, "devices_on": s.pcs, "power_w": s.carga, "capacity": s.cap,
                "blinds": list(s.persianas), "supply_temperature": s.edif.t_imp, "alarm": s.edif.alarma}
