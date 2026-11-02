"""Pruebas del motor de simulación con un catálogo pequeño y, si existe, con el real."""

from __future__ import annotations

import math
import time
from datetime import UTC, datetime, timedelta

import pytest

from gemelo_sensores.catalogo import PROPIEDADES, RUTA_POR_DEFECTO, Catalogo, Edificio, Espacio, Sensor
from gemelo_sensores.simulacion import Exterior, Simulador

# Hora local de Madrid = UTC+1 en invierno
MARTES_NOV_10H = datetime(2026, 11, 10, 9, 0, tzinfo=UTC)  # martes lectivo, 10:00 en Cáceres
MARTES_FEB_6H = datetime(2027, 2, 9, 5, 0, tzinfo=UTC)  # martes lectivo de febrero, 6:00


def _sensor(sid: str, kind: str, espacio: str, edificio: str, props: list[str]) -> Sensor:
    return Sensor(sid, sid, kind, espacio, edificio, props, real_available=False)


def catalogo_pequeno() -> Catalogo:
    edificios = {"INF": Edificio("INF", "Informática", "1903", ["P00"]),
                 "EXT": Edificio("EXT", "Exterior", None, [])}
    espacios = [
        Espacio("INF-EDIFICIO", "INF", "", "building", "EDIFICIO", None, None),
        Espacio("INF-P00-AUL001", "INF", "P00", "classroom", "AUL001", "Aula 1", None, 60.0, 3.0, 50),
        Espacio("INF-P00-AUL002", "INF", "P00", "classroom", "AUL002", "Aula 2", None, 90.0),
        Espacio("INF-P00-INF003", "INF", "P00", "computer_lab", "INF003", None, None, 70.0),
        Espacio("INF-P00-DES004", "INF", "P00", "office", "DES004", None, None, 16.0),
        Espacio("INF-P00-DES005", "INF", "P00", "office", "DES005", None, None),
        Espacio("INF-P00-LAB006", "INF", "P00", "lab", "LAB006", None, None, 45.0),
        Espacio("INF-P00-COM007", "INF", "P00", "corridor", "COM007", None, None, 80.0),
        Espacio("INF-P00-COM008", "INF", "P00", "hall", "COM008", None, None, 150.0),
        Espacio("INF-P00-ASE009", "INF", "P00", "restroom", "ASE009", None, None, 20.0),
        Espacio("INF-P00-CUA010", "INF", "P00", "technical_room", "CUA010", None, None, 15.0),
        Espacio("INF-P00-CAF011", "INF", "P00", "cafeteria", "CAF011", None, None, 120.0),
        Espacio("INF-P00-REU012", "INF", "P00", "meeting_room", "REU012", None, None, 30.0),
        Espacio("EXT-CAMPUS", "EXT", "", "outdoor", "COM001", None, None),
    ]
    a1 = "INF-P00-AUL001"
    sensores = [
        _sensor("RAD1", "radiator_valve", a1, "INF", ["valve_position", "target_temperature", "air_temperature", "battery_voltage"]),
        _sensor("RAD2", "radiator_valve", a1, "INF", ["valve_position", "target_temperature", "air_temperature", "battery_voltage"]),
        _sensor("VEN1", "window_contact", a1, "INF", ["window_open", "air_temperature", "relative_humidity"]),
        _sensor("VEN2", "window_contact", a1, "INF", ["window_open", "air_temperature", "relative_humidity"]),
        _sensor("PER1", "blind", a1, "INF", ["blind_position"]),
        _sensor("CO2A1", "climate_co2", a1, "INF", ["air_temperature", "relative_humidity", "co2_concentration"]),
        _sensor("CAM1", "occupancy_camera", a1, "INF", ["occupancy"]),
        _sensor("SON1", "sound_meter", a1, "INF", ["sound_level"]),
        _sensor("LUZ1", "light_sensor", a1, "INF", ["illuminance", "lights_on"]),
        _sensor("AQ1", "air_quality", a1, "INF", ["pm25", "co2_concentration"]),
        _sensor("CO2A2", "climate_co2", "INF-P00-AUL002", "INF", ["air_temperature", "relative_humidity", "co2_concentration"]),
        _sensor("IPS3", "pc_usage", "INF-P00-INF003", "INF", ["devices_on"]),
        _sensor("TH4", "climate", "INF-P00-DES004", "INF", ["air_temperature", "relative_humidity"]),
        _sensor("TH5", "climate", "INF-P00-DES005", "INF", ["air_temperature", "relative_humidity", "window_open"]),
        _sensor("TH7", "climate", "INF-P00-COM007", "INF", ["air_temperature", "relative_humidity"]),
        _sensor("CAM8", "occupancy_camera", "INF-P00-COM008", "INF", ["occupancy"]),
        _sensor("ALA8", "fire_alarm", "INF-P00-COM008", "INF", ["alarm_active"]),
        _sensor("STV8", "signage_screen", "INF-P00-COM008", "INF", ["power_on"]),
        _sensor("AGU9", "water_meter", "INF-P00-ASE009", "INF", ["water_volume", "water_flow"]),
        _sensor("ELE10", "power_meter", "INF-P00-CUA010", "INF", ["active_power", "active_energy"]),
        _sensor("CAL10", "heating_plant", "INF-P00-CUA010", "INF", ["water_temperature"]),
        _sensor("FV10", "solar_inverter", "INF-P00-CUA010", "INF", ["solar_power", "active_energy"]),
        _sensor("ELE", "power_meter", "INF-EDIFICIO", "INF", ["active_power", "active_energy"]),
        _sensor("AGU", "water_meter", "INF-EDIFICIO", "INF", ["water_volume", "water_flow"]),
        _sensor("WIFI", "wifi_occupancy", "INF-EDIFICIO", "INF", ["wifi_clients"]),
        _sensor("ALA", "fire_alarm", "INF-EDIFICIO", "INF", ["alarm_active"]),
        _sensor("MET", "weather_station", "EXT-CAMPUS", "EXT", ["air_temperature", "relative_humidity", "wind_speed",
                                                               "wind_direction", "atmospheric_pressure", "precipitation"]),
        # Tipo desconocido que declara todo el vocabulario
        _sensor("RARO", "quantum_flux", "INF-P00-LAB006", "INF", list(PROPIEDADES)),
    ]
    return Catalogo(edificios, {e.id: e for e in espacios}, {s.id: s for s in sensores})


def correr(sim: Simulador, desde: datetime, segundos: float, paso: float = 60.0, exterior: Exterior | None = None) -> datetime:
    """Avanza `segundos` en pasos de `paso` y devuelve la nueva hora."""
    t = desde
    for _ in range(int(segundos // paso)):
        t += timedelta(seconds=paso)
        sim.avanzar(t, paso, exterior)
    return t


@pytest.fixture
def sim() -> Simulador:
    s = Simulador(catalogo_pequeno(), semilla=7)
    s.avanzar(MARTES_NOV_10H, 0.0, None)
    return s


# ---------------------------------------------------------------------------------------- básicos
def test_escenarios_del_contrato():
    s = Simulador(catalogo_pequeno())
    ids = [e.id for e in s.escenarios()]
    assert ids == ["normal_day", "winter_morning", "summer_afternoon", "exam_period", "weekend", "holiday_closure",
                   "heating_failure", "fire_drill"]
    assert all(e.name and e.description for e in s.escenarios())
    assert s.escenario == "normal_day"
    s.poner_escenario("weekend")
    assert s.escenario == "weekend"
    with pytest.raises(KeyError):
        s.poner_escenario("apocalipsis")


def test_sin_avanzar_no_hay_lecturas():
    s = Simulador(catalogo_pequeno())
    assert s.lectura("CO2A1") is None
    s.avanzar(MARTES_NOV_10H, 0.0, None)
    assert s.lectura("CO2A1") is not None
    assert s.lectura("NO_EXISTE") is None


def test_todas_las_propiedades_tienen_valor_plausible(sim):
    correr(sim, MARTES_NOV_10H, 600)
    for sid, sensor in sim.catalogo.sensores.items():
        v = sim.lectura(sid)
        assert v is not None and set(v) == set(sensor.properties), sid
        for p, x in v.items():
            assert math.isfinite(x), (sid, p)
            if PROPIEDADES[p].type == "boolean":
                assert x in (0.0, 1.0), (sid, p)
    raro = sim.lectura("RARO")
    assert 10 < raro["air_temperature"] < 35 and 350 <= raro["co2_concentration"] < 5000
    assert 0 <= raro["relative_humidity"] <= 100 and 0 <= raro["valve_position"] <= 100


def test_redondeos(sim):
    v = sim.lectura("CO2A1")
    assert v["co2_concentration"] == int(v["co2_concentration"])
    assert round(v["air_temperature"], 1) == v["air_temperature"]
    assert sim.lectura("RAD1")["target_temperature"] * 2 == int(sim.lectura("RAD1")["target_temperature"] * 2)


# ---------------------------------------------------------------------------------------- determinismo
def _serie(semilla: int) -> list:
    s = Simulador(catalogo_pequeno(), semilla=semilla)
    t = MARTES_NOV_10H
    s.avanzar(t, 0.0, None)
    salida = []
    for paso in (5, 5, 60, 600, 3600, 5, 18000):
        t += timedelta(seconds=paso)
        s.avanzar(t, paso, None)
        salida.append({sid: s.lectura(sid) for sid in s.catalogo.sensores})
    return salida


def test_determinista_con_la_semilla():
    assert _serie(3) == _serie(3)
    assert _serie(3) != _serie(4)


def test_sin_saltos_y_ruido_correlado(sim):
    t = MARTES_NOV_10H
    previa = sim.lectura("TH4")["air_temperature"]
    for _ in range(120):  # 10 minutos en ticks de 5 s
        t += timedelta(seconds=5)
        sim.avanzar(t, 5, None)
        v = sim.lectura("TH4")["air_temperature"]
        assert abs(v - previa) <= 0.21
        previa = v


# ---------------------------------------------------------------------------------------- física
def test_co2_sube_en_clase_llena_y_baja_al_abrir(sim):
    t = MARTES_NOV_10H
    sim.crear_evento("class_start", "INF-P00-AUL001", 7200, {"people": 45}, t)
    sim.crear_evento("close_windows", "INF-P00-AUL001", 7200, {}, t)
    co2_0 = sim.lectura("CO2A1")["co2_concentration"]
    t = correr(sim, t, 1800)
    co2_30 = sim.lectura("CO2A1")["co2_concentration"]
    t = correr(sim, t, 1200)
    co2_50 = sim.lectura("CO2A1")["co2_concentration"]
    assert sim.lectura("CAM1")["occupancy"] == 45
    assert co2_0 < co2_30 < co2_50 and co2_50 > 1500
    assert sim.lectura("VEN1")["window_open"] == 0.0

    sim.crear_evento("open_windows", "INF-P00-AUL001", 900, {}, t)
    assert sim.lectura("VEN1")["window_open"] == 1.0 and sim.lectura("VEN2")["window_open"] == 1.0
    t = correr(sim, t, 600)
    co2_abierto = sim.lectura("CO2A1")["co2_concentration"]
    assert co2_abierto < co2_50 - 500
    assert sim.lectura("AQ1")["co2_concentration"] < co2_50 - 400  # el otro sensor de la sala, coherente


def test_heating_failure_enfria_en_invierno():
    medias = {}
    for esc in ("normal_day", "heating_failure"):
        s = Simulador(catalogo_pequeno(), semilla=1)
        s.poner_escenario(esc)
        s.avanzar(MARTES_FEB_6H, 0.0, None)
        correr(s, MARTES_FEB_6H, 6 * 3600, paso=120)
        temps = [s.lectura(x)["air_temperature"] for x in ("CO2A1", "CO2A2", "TH4", "TH5", "TH7")]
        medias[esc] = sum(temps) / len(temps)
        if esc == "heating_failure":
            assert s.lectura("CAL10")["water_temperature"] < 30  # calderas paradas
            assert s.lectura("RAD1")["valve_position"] >= 90  # las válvulas piden calor en vano
        else:
            assert s.lectura("CAL10")["water_temperature"] > 45
    assert medias["heating_failure"] < medias["normal_day"] - 2.0


def test_ocupacion_segun_horario(sim):
    # A las 10:00 de un martes lectivo hay gente; un domingo a las 10:00, casi nadie
    t = correr(sim, MARTES_NOV_10H, 1800)
    assert sim._edificios["INF"].occ_total > 30
    domingo = Simulador(catalogo_pequeno(), semilla=7)
    t = datetime(2026, 11, 15, 9, 0, tzinfo=UTC)
    domingo.avanzar(t, 0.0, None)
    correr(domingo, t, 1800)
    assert domingo._edificios["INF"].occ_total < 3
    assert domingo.lectura("CAM1")["occupancy"] == 0


def test_escenarios_cambian_el_comportamiento(sim):
    correr(sim, MARTES_NOV_10H, 1200)
    lleno = sim._edificios["INF"].occ_total
    sim.poner_escenario("holiday_closure")
    t = correr(sim, MARTES_NOV_10H + timedelta(seconds=1200), 1800)
    assert lleno > 30 and sim._edificios["INF"].occ_total < 1
    sim.poner_escenario("winter_morning")
    sim.avanzar(t + timedelta(seconds=5), 5, None)
    ef = sim.tiempo_efectivo
    assert ef.month == 2 and ef.weekday() == 1 and sim.fuente_clima == "simulated"
    assert sim.lectura("MET")["air_temperature"] < 12


def test_clima_real_y_sintetico():
    s = Simulador(catalogo_pequeno())
    s.avanzar(MARTES_NOV_10H, 0.0, Exterior(30.0, 20.0))
    assert s.fuente_clima == "real" and abs(s.lectura("MET")["air_temperature"] - 30.0) < 0.5
    verano = Simulador(catalogo_pequeno())
    t = datetime(2027, 7, 14, 14, 0, tzinfo=UTC)  # 16:00 en julio
    verano.avanzar(t, 0.0, None)
    assert 28 < verano.lectura("MET")["air_temperature"] < 41
    invierno = Simulador(catalogo_pequeno())
    t = datetime(2027, 1, 20, 6, 0, tzinfo=UTC)
    invierno.avanzar(t, 0.0, None)
    assert -3 < invierno.lectura("MET")["air_temperature"] < 11
    assert invierno.lectura("FV10")["solar_power"] == 0  # de noche


def test_fire_drill_activa_alarmas_y_vacia(sim):
    t = correr(sim, MARTES_NOV_10H, 1200)
    assert sim._edificios["INF"].occ_total > 30
    assert sim.lectura("ALA")["alarm_active"] == 0.0
    sim.poner_escenario("fire_drill")
    t = correr(sim, t, 360, paso=30)
    assert sim.lectura("ALA")["alarm_active"] == 1.0 and sim.lectura("ALA8")["alarm_active"] == 1.0
    assert sim._edificios["INF"].occ_total < 1
    assert sim.lectura("CAM1")["occupancy"] == 0
    assert sim._edificios["EXT"].occ_ext > 20  # la gente espera fuera
    # el simulacro termina a los 10 minutos y la gente vuelve
    correr(sim, t, 1800, paso=30)
    assert sim.lectura("ALA")["alarm_active"] == 0.0
    assert sim._edificios["INF"].occ_total > 10


# ---------------------------------------------------------------------------------------- contadores
def test_contadores_monotonos():
    s = Simulador(catalogo_pequeno())
    t = MARTES_NOV_10H
    s.avanzar(t, 0.0, None)
    energias = {sid: s.lectura(sid)["active_energy"] for sid in ("ELE", "ELE10", "FV10")}
    agua_previa = s.lectura("AGU")["water_volume"]
    dia_previo = (t + timedelta(hours=1)).date()
    reinicios = 0
    for paso in [60] * 120 + [600] * 100 + [3600] * 30 + [18000] * 10:
        t += timedelta(seconds=paso)
        s.avanzar(t, paso, None)
        for sid, previa in energias.items():
            v = s.lectura(sid)["active_energy"]
            assert v >= previa, sid
            energias[sid] = v
        agua = s.lectura("AGU")["water_volume"]
        dia = (t + timedelta(hours=1)).date()  # día local de Madrid (UTC+1 en noviembre)
        if dia == dia_previo:
            assert agua >= agua_previa
        else:
            reinicios += 1
        agua_previa, dia_previo = agua, dia
    assert reinicios >= 3
    assert s.lectura("AGU")["water_volume"] < 50000


def test_agua_se_reinicia_a_medianoche_de_madrid():
    s = Simulador(catalogo_pequeno())
    t = datetime(2026, 11, 10, 22, 50, tzinfo=UTC)  # 23:50 en Madrid
    s.avanzar(t, 0.0, None)
    antes = s.lectura("AGU")["water_volume"]
    t = correr(s, t, 1200)  # 00:10 del día siguiente
    assert antes > 100 and s.lectura("AGU")["water_volume"] < antes


def test_salto_de_reloj_no_rompe_contadores(sim):
    e0 = sim.lectura("ELE")["active_energy"]
    t = MARTES_NOV_10H - timedelta(days=30)  # el reloj vuelve atrás
    sim.avanzar(t, 5, None)
    assert sim.lectura("ELE")["active_energy"] >= e0


# ---------------------------------------------------------------------------------------- eventos
def test_eventos_caducan_solos(sim):
    t = MARTES_NOV_10H
    ev = sim.crear_evento("open_windows", "INF-P00-AUL001", 600, {}, t)
    largo = sim.crear_evento("heating_off", "INF", None, {}, t)
    assert ev.active and ev.start == t and ev.id != largo.id
    assert sim.lectura("VEN2")["window_open"] == 1.0
    t = correr(sim, t, 540)
    assert ev.active
    t = correr(sim, t, 120)
    assert not ev.active and largo.active
    assert sim.lectura("VEN1")["window_open"] == 0.0 and sim.lectura("VEN2")["window_open"] == 0.0
    lista = sim.eventos()
    assert lista[0] is largo and lista[-1] is ev  # activos primero
    sim.terminar_evento(largo.id)
    assert not largo.active
    with pytest.raises(KeyError):
        sim.terminar_evento("evt-nope")


def test_eventos_validan_tipo_y_objetivo(sim):
    with pytest.raises(ValueError):
        sim.crear_evento("open_windows", "NOPE", None, {}, MARTES_NOV_10H)
    with pytest.raises(ValueError):
        sim.crear_evento("meteorito", "campus", None, {}, MARTES_NOV_10H)
    with pytest.raises(ValueError):
        sim.crear_evento("class_start", "INF-P00-AUL001", None, {"people": "muchos"}, MARTES_NOV_10H)
    for objetivo in ("campus", "INF", "INF-P00-AUL001", "VEN1"):
        sim.crear_evento("close_windows", objetivo, 60, {}, MARTES_NOV_10H)


def test_ventana_concreta(sim):
    sim.crear_evento("close_windows", "INF-P00-AUL001", None, {}, MARTES_NOV_10H)
    sim.crear_evento("open_windows", "VEN2", None, {}, MARTES_NOV_10H)
    assert sim.lectura("VEN1")["window_open"] == 0.0 and sim.lectura("VEN2")["window_open"] == 1.0


def test_sensor_failure(sim):
    t = MARTES_NOV_10H
    ev = sim.crear_evento("sensor_failure", "CO2A1", None, {}, t)
    assert sim.lectura("CO2A1") is None
    congelado = sim.lectura("CAM1")
    sim.crear_evento("sensor_failure", "CAM1", 3600, {"mode": "frozen"}, t)
    sim.crear_evento("class_start", "INF-P00-AUL001", None, {"people": 33}, t)
    t = correr(sim, t, 900)
    assert sim.lectura("CAM1") == congelado != {"occupancy": 33.0}
    assert sim.lectura("SON1") is not None
    sim.terminar_evento(ev.id)
    assert sim.lectura("CO2A1") is not None


def test_class_start_y_class_end(sim):
    t = MARTES_NOV_10H
    sim.crear_evento("class_start", "INF-P00-REU012", None, {"people": 12}, t)
    t = correr(sim, t, 600)
    assert round(sim.estado_espacio("INF-P00-REU012")["occupancy"]) == 12
    sim.crear_evento("class_end", "INF-P00-REU012", None, {}, t)
    assert all(not e.active for e in sim.eventos() if e.type == "class_start")
    correr(sim, t, 300)
    assert sim.estado_espacio("INF-P00-REU012")["occupancy"] < 0.5


def test_apagon_fuga_y_alarma(sim):
    t = correr(sim, MARTES_NOV_10H, 300)
    potencia = sim.lectura("ELE")["active_power"]
    caudal = sim.lectura("AGU")["water_flow"]
    sim.crear_evento("power_outage", "INF", 600, {}, t)
    sim.crear_evento("water_leak", "INF-P00-ASE009", 600, {"flow_l_min": 12}, t)
    sim.crear_evento("fire_alarm", "INF-P00-COM008", 600, {}, t)
    t = correr(sim, t, 120)
    assert potencia > 1000 and sim.lectura("ELE")["active_power"] == 0
    assert sim.lectura("LUZ1")["lights_on"] == 0.0 and sim.lectura("STV8")["power_on"] == 0.0
    assert caudal > 0 and sim.lectura("AGU9")["water_flow"] >= 12 and sim.lectura("AGU")["water_flow"] >= 12
    assert sim.lectura("ALA")["alarm_active"] == 1.0


# ---------------------------------------------------------------------------------------- rendimiento
def catalogo_sintetico(n_espacios: int = 700, n_sensores: int = 1000) -> Catalogo:
    tipos = ["classroom", "computer_lab", "lab", "office", "office", "office", "corridor", "hall", "restroom",
             "technical_room", "meeting_room", "other"]
    kinds = [("climate", ["air_temperature", "relative_humidity"]),
             ("climate_co2", ["air_temperature", "relative_humidity", "co2_concentration"]),
             ("radiator_valve", ["valve_position", "target_temperature", "air_temperature", "battery_voltage"]),
             ("window_contact", ["window_open", "air_temperature", "relative_humidity"]),
             ("occupancy_camera", ["occupancy"]), ("light_sensor", ["illuminance", "lights_on"]),
             ("blind", ["blind_position"]), ("sound_meter", ["sound_level"])]
    edificios = {b: Edificio(b, b, None, ["P00", "P01"]) for b in ("A", "B", "C", "D", "E", "F", "G")}
    espacios = {}
    for i in range(n_espacios):
        b = "ABCDEFG"[i % 7]
        sid = f"{b}-P0{i % 2}-X{i:04d}"
        espacios[sid] = Espacio(sid, b, f"P0{i % 2}", tipos[i % len(tipos)], f"X{i:04d}", None, None,
                                10.0 + (i * 37) % 120 if i % 5 else None)
    ids = list(espacios)
    sensores = {}
    for j in range(n_sensores):
        e = espacios[ids[(j * 7) % n_espacios]]
        kind, props = kinds[j % len(kinds)]
        sid = f"S{j:05d}"
        sensores[sid] = Sensor(sid, sid, kind, e.id, e.building_id, props, False)
    for b in edificios:
        sensores[f"ELE_{b}"] = Sensor(f"ELE_{b}", "", "power_meter", ids[0], b, ["active_power", "active_energy"], False)
    return Catalogo(edificios, espacios, sensores)


def _medir(cat: Catalogo) -> tuple[float, float, float]:
    sim = Simulador(cat)
    t = MARTES_NOV_10H
    sim.avanzar(t, 0.0, None)
    normal, grande = [], []
    for _ in range(40):
        t += timedelta(seconds=5)
        t0 = time.perf_counter()
        sim.avanzar(t, 5, None)
        normal.append(time.perf_counter() - t0)
    for _ in range(10):
        t += timedelta(seconds=18000)  # time_scale = 3600 con ticks de 5 s
        t0 = time.perf_counter()
        sim.avanzar(t, 18000, None)
        grande.append(time.perf_counter() - t0)
    # Una sola pasada es muy sensible a la carga de la máquina (Unity abierto): la mejor de tres
    lecturas = []
    for _ in range(3):
        t0 = time.perf_counter()
        for sid in cat.sensores:
            sim.lectura(sid)
        lecturas.append(time.perf_counter() - t0)
    lectura = min(lecturas)
    return sorted(normal)[len(normal) // 2], sorted(grande)[len(grande) // 2], lectura


def test_rendimiento_catalogo_sintetico():
    normal, grande, lectura = _medir(catalogo_sintetico())
    assert normal < 0.020, f"avanzar(5 s): {normal * 1000:.1f} ms"
    assert grande < 0.050, f"avanzar(5 h): {grande * 1000:.1f} ms"
    assert lectura < 0.100


@pytest.mark.skipif(not RUTA_POR_DEFECTO.exists(), reason="sin datos/catalogo.json")
def test_rendimiento_catalogo_real():
    cat = Catalogo.cargar()
    normal, grande, lectura = _medir(cat)
    print(f"\ncatálogo real ({len(cat.espacios)} estancias, {len(cat.sensores)} sensores): avanzar(5 s) "
          f"{normal * 1000:.1f} ms, avanzar(5 h) {grande * 1000:.1f} ms, lectura de todos {lectura * 1000:.1f} ms")
    assert normal < 0.020
    assert grande < 0.050


# ------------------------------------------------------------ regresiones de la revisión del 02-11
@pytest.mark.parametrize("params", [{"flow_l_min": "nan"}, {"flow_l_min": "inf"}, {"flow_l_min": -500}])
def test_fuga_con_caudal_no_valido_se_rechaza(sim, params):
    with pytest.raises(ValueError):
        sim.crear_evento("water_leak", "INF-P00-AUL001", None, params, MARTES_NOV_10H)


@pytest.mark.parametrize("valor", [1e400, -3, 2.5, "muchos"])
def test_clase_con_personas_no_validas_se_rechaza(sim, valor):
    with pytest.raises(ValueError):
        sim.crear_evento("class_start", "INF-P00-AUL001", None, {"people": valor}, MARTES_NOV_10H)


def test_duracion_de_evento_acotada(sim):
    with pytest.raises(ValueError):
        sim.crear_evento("open_windows", "INF-P00-AUL001", 1e308, {}, MARTES_NOV_10H)


def test_clase_no_pasa_del_aforo(sim):
    t = MARTES_NOV_10H
    sim.crear_evento("class_start", "INF-P00-AUL001", 3600, {"people": 10000}, t)
    t = correr(sim, t, 600)
    estado = sim.estado_espacio("INF-P00-AUL001")
    assert estado["occupancy"] <= estado["capacity"] + 0.5


# ---------------------------------------------------------------------------------- salón de grados
def test_actos_del_salon_de_grados():
    """Conferencias en periodo lectivo y defensas (con poco público) en exámenes; nada los domingos."""
    from gemelo_sensores.simulacion import calendario as cal
    from gemelo_sensores.simulacion.ocupacion import programar_dia

    cap = 60
    por_tipo: dict[str, list[list]] = {}
    d0 = datetime(2026, 9, 1).date()
    for i in range(365):
        d = d0 + timedelta(days=i)
        tipo_dia = cal.tipo_dia(d.toordinal())
        iv = programar_dia("auditorium", tipo_dia, cap, 1, 12345, d.toordinal(), d.weekday())
        por_tipo.setdefault(tipo_dia, []).append(iv)
        for a, b, n, act in iv:
            assert 0 <= a < b <= 24 * 60 and 1 <= n <= cap + 2 and act in ("acto", "trabajo")
    assert all(not iv for iv in por_tipo[cal.FIN_DE_SEMANA] + por_tipo.get(cal.CERRADO, []))
    con_acto = lambda dias: sum(any(x[3] == "acto" for x in iv) for iv in dias) / len(dias)  # noqa: E731
    assert 0.3 < con_acto(por_tipo[cal.LECTIVO]) < 0.7
    assert con_acto(por_tipo[cal.EXAMENES]) > 0.5
    publico = lambda dias: [x[2] for iv in dias for x in iv if x[3] == "acto"]  # noqa: E731
    assert max(publico(por_tipo[cal.EXAMENES])) < max(publico(por_tipo[cal.LECTIVO]))


def test_salon_de_grados_con_acto():
    edificios = {"TEL": Edificio("TEL", "Telecomunicación", "1905", ["P00"])}
    sg = "TEL-P00-LOC017"
    espacios = [Espacio(sg, "TEL", "P00", "auditorium", "LOC017", "Salón de Grados", "1905_P0_17", 100.9, 4.5)]
    props = {"climate_co2": ["air_temperature", "relative_humidity", "co2_concentration"],
             "occupancy_camera": ["occupancy"], "light_sensor": ["illuminance", "lights_on"],
             "sound_meter": ["sound_level"], "air_quality": ["pm25", "co2_concentration"],
             "power_meter": ["active_power", "active_energy"]}
    sensores = [_sensor(k, k, sg, "TEL", p) for k, p in props.items()]
    sim = Simulador(Catalogo(edificios, {e.id: e for e in espacios}, {s.id: s for s in sensores}), semilla=3)
    t = datetime(2026, 11, 10, 3, 0, tzinfo=UTC)  # martes lectivo, 4:00
    sim.avanzar(t, 0.0, None)
    t = correr(sim, t, 3600)
    vacio = {k: sim.lectura(k) for k in props}
    assert vacio["occupancy_camera"]["occupancy"] == 0
    sim.crear_evento("class_start", sg, 5400, {"people": 50}, t)
    t = correr(sim, t, 3600)
    lleno = {k: sim.lectura(k) for k in props}
    assert lleno["occupancy_camera"]["occupancy"] == 50
    assert sim._salas[sg].actividad == "acto"
    assert lleno["climate_co2"]["co2_concentration"] > vacio["climate_co2"]["co2_concentration"] + 300
    assert lleno["sound_meter"]["sound_level"] > vacio["sound_meter"]["sound_level"] + 15
    assert lleno["power_meter"]["active_power"] > vacio["power_meter"]["active_power"]
    assert lleno["light_sensor"]["lights_on"] == 1.0


def test_salon_de_grados_en_el_catalogo_real():
    cat = Catalogo.cargar()
    esp = cat.espacios.get("TEL-P00-LOC017")
    if esp is None:
        pytest.skip("sin catálogo real")
    assert esp.type == "auditorium" and esp.unity_room == "1905_P0_17"
    kinds = {s.kind for s in cat.sensores_de_espacio(esp.id)}
    assert {"climate_co2", "occupancy_camera", "light_sensor", "sound_meter", "air_quality", "power_meter"} <= kinds
