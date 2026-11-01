"""Genera datos/catalogo.json (edificios, estancias y sensores del gemelo).

Entradas:
- datos/inventario_smartpolitech.json (herramientas/inventariar.py): sensores reales.
- ../Referencias/Politecnica/datos/interiores.json: las 504 estancias del modelo de Unity.

Cada sensor de SmartPoliTech se nombra UEXCC_<edificio>_<planta>_<estancia>_<tipo>; el número de la
estancia coincide con el de los planos de 2013 que usa el modelo de Unity (INF_P01_AUL024 es
1903_P1_24, la sala NORBA), así que el enlace con Unity sale directo. A las estancias del modelo se
les añaden sensores virtuales (SIM_...), que solo existen simulados.

Uso:  uv run python herramientas/generar_catalogo.py
"""

from __future__ import annotations

import json
import re
import unicodedata
from datetime import UTC, datetime
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[1]
INVENTARIO = RAIZ / "datos" / "inventario_smartpolitech.json"
INTERIORES = RAIZ.parent / "Referencias" / "Politecnica" / "datos" / "interiores.json"
SALIDA = RAIZ / "datos" / "catalogo.json"

EDIFICIOS = {
    # código SmartPoliTech: (nombre, id del modelo de Unity)
    "SCO": ("Edificio Común / Servicios Centrales", "1901"),
    "ATE": ("Pabellón de Edificación (Arquitectura Técnica)", "1902"),
    "INF": ("Pabellón de Informática", "1903"),
    "OPU": ("Pabellón de Ingeniería Civil (Obras Públicas)", "1904"),
    "TEL": ("Pabellón de Telecomunicación", "1905"),
    "INV": ("Edificio de Investigación", "1906"),
    "ACU": ("Laboratorio de Acústica", "1907"),
    "EXT": ("Exterior del campus", None),
}
UNITY_A_CODIGO = {u: c for c, (_, u) in EDIFICIOS.items() if u}
ALIAS_EDIFICIO = {"COM": "SCO"}  # UEXCC_COM_P00_CAF014 es la cafetería del Edificio Común
PLANTA_A_UNITY = {"PS2": "Pm2", "PS1": "Pm1", "P00": "P0", "P01": "P1", "P02": "P2"}
UNITY_A_PLANTA = {v: k for k, v in PLANTA_A_UNITY.items()}

# Prefijo de estancia en SmartPoliTech -> tipo de estancia del contrato
TIPO_POR_PREFIJO = {
    "AUL": "classroom", "LAB": "lab", "DES": "office", "COM": "corridor", "CUA": "technical_room",
    "ASE": "restroom", "SAL": "meeting_room", "CAF": "cafeteria",
}
# Uso del modelo de Unity -> (tipo de estancia, prefijo para estancias sin código SmartPoliTech)
TIPO_POR_USO = {
    "aula": ("classroom", "AUL"), "aula_dibujo": ("classroom", "AUL"), "seminario": ("classroom", "AUL"),
    "aula_informatica": ("computer_lab", "AUL"),
    "despacho": ("office", "DES"), "despacho_direccion": ("office", "DES"), "oficina": ("office", "DES"),
    "secretaria": ("office", "DES"), "conserjeria": ("office", "DES"),
    "distribuidor": ("corridor", "COM"), "pasillo": ("corridor", "COM"), "galeria": ("corridor", "COM"),
    "vestibulo": ("hall", "COM"), "hall": ("hall", "COM"),
    "aseo": ("restroom", "ASE"), "aseo_adaptado": ("restroom", "ASE"),
    "instalaciones": ("technical_room", "CUA"), "sala_servidores": ("technical_room", "CUA"),
    "almacen": ("technical_room", "CUA"), "cuarto_limpieza": ("technical_room", "CUA"),
    "minisala": ("meeting_room", "SAL"), "sala_descanso": ("meeting_room", "SAL"),
    "cafeteria": ("cafeteria", "CAF"), "cocina": ("cafeteria", "CAF"),
    "patio": ("outdoor", "EXT"),
}

# Mediciones que no son del campus o son pruebas
DESCARTAR = re.compile(r"^(CAUDAL_|SMARTOPENPARK|Prueba|prueba|LoraSDL|menu_cafeteria)")

NOMBRE_KIND = {
    "radiator_valve": "Válvula de radiador", "window_contact": "Sensor de ventana", "blind": "Persiana",
    "climate": "Termohigrómetro", "climate_co2": "Sensor de CO₂", "pc_usage": "Equipos encendidos",
    "power_meter": "Analizador de red", "water_meter": "Contador de agua",
    "heating_plant": "Calefacción", "solar_inverter": "Inversor fotovoltaico",
    "occupancy_camera": "Cámara de ocupación", "wifi_occupancy": "Ocupación wifi",
    "sound_meter": "Sonómetro", "weather_station": "Estación meteorológica",
    "signage_screen": "Pantalla de cartelería", "light_sensor": "Sensor de luz",
    "air_quality": "Calidad del aire", "fire_alarm": "Detector de incendios",
}
ORIENTACION = {"N": "norte", "S": "sur", "E": "este", "O": "oeste"}


def _norm(s: str) -> str:
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()
    return re.sub(r"[^A-Z0-9]", "", s.upper())


def _iso(ts: float | None) -> str | None:
    return datetime.fromtimestamp(ts, UTC).isoformat().replace("+00:00", "Z") if ts else None


def mapeo_campos(kind: str, campos: dict[str, str]) -> dict[str, str]:
    """Propiedad del contrato -> campo de InfluxDB (con transformación tras '|')."""
    tiene = lambda *ks: next((k for k in ks if k in campos), None)  # noqa: E731
    m: dict[str, str | None] = {}
    if kind == "radiator_valve":
        m = {
            "valve_position": "motorPosition|valvula",
            "target_temperature": "targetTemperature",
            "air_temperature": "sensorTemperature",
            "battery_voltage": "batteryVoltage",
        }
    elif kind == "window_contact":
        m = {"window_open": "door|bool", "air_temperature": "temperature", "relative_humidity": "humidity"}
    elif kind == "blind":
        m = {"blind_position": "state|persiana"}
    elif kind in ("climate", "climate_co2", "weather_station"):
        m = {
            "air_temperature": tiene("temperature", "temp", "ta"),
            "relative_humidity": tiene("humidity", "hum", "hr"),
            "co2_concentration": tiene("sensorCO2", "CO2", "co2"),
            "window_open": (w + "|bool") if (w := tiene("window")) else None,
            "battery_voltage": tiene("batteryVoltage", "vbat"),
            "wind_speed": tiene("vv", "windspeed"),
            "wind_direction": tiene("dv", "winddirection"),
            "atmospheric_pressure": tiene("pres", "pressure"),
            "precipitation": tiene("prec"),
        }
    elif kind == "pc_usage":
        m = {"devices_on": "totalOn"}
    elif kind == "power_meter":
        if "pt" in campos:
            m = {"active_power": "pt", "active_energy": "et|kwh_desde_wh"}  # analizadores WTD: e* en Wh
        elif "Potencia_activa_Trifásica" in campos:
            m = {"active_power": "Potencia_activa_Trifásica", "active_energy": "Energía_activa_Trifásica|kwh"}
        elif "Power1" in campos:
            m = {"active_power": "Power1+Power2+Power3+Power4"}
        elif "potencia" in campos:
            m = {"active_power": "potencia"}
    elif kind == "water_meter":
        m = {"water_volume": "dailyliters" if "dailyliters" in campos else ("counter1+counter2|agua_hoy" if "counter1" in campos else "counter|agua_hoy")}
    elif kind == "heating_plant":
        m = {"water_temperature": tiene("Imp_Caldera1", "temperature", "Temp1")}
    elif kind == "solar_inverter":
        m = {"solar_power": "PAC_W", "active_energy": "TOTAL_ENERGY_Wh|kwh_desde_wh"}
    elif kind == "occupancy_camera":
        m = {"occupancy": tiene("people", "ocupacion") + "|num"}
    elif kind == "wifi_occupancy":
        m = {"wifi_clients": tiene("Actual")}
    elif kind == "sound_meter":
        m = {"sound_level": tiene("LAeq", "laeq")}
    elif kind == "signage_screen":
        m = {"power_on": "cpu|presencia"}
    if kind == "weather_station":
        m.pop("window_open", None)
    return {k: v for k, v in m.items() if v and v.split("|")[0].split("+")[0] in campos}


def clasificar(m: str) -> tuple[str, str, str | None, str, str | None] | None:
    """(kind, edificio, planta, código de estancia, sufijo) de una medición, o None si no es del campus."""
    if r := re.match(r"^UEXCC_([A-Z]+)_(P[S0-9]\d)_([A-Z]+)(\d+)_(.+)$", m):
        edif, planta, pref, num, resto = r.groups()
        edif = ALIAS_EDIFICIO.get(edif, edif)
        codigo = f"{pref}{int(num):03d}"
        tipo = resto.split("_")[0] if not resto.startswith("SEN") else resto.split("_")[1]
        kind = {
            "RAD": "radiator_valve", "SAV": "window_contact", "PER": "blind", "THR": "climate",
            "THV": "climate", "THM": "climate", "THC": "climate_co2", "C": "climate_co2",
            "IPS": "pc_usage", "ELE": "power_meter", "AGU": "water_meter", "CGT": "heating_plant",
            "INV": "solar_inverter", "APC": "wifi_occupancy", "SON": "sound_meter", "MET": "weather_station",
            "STV": "signage_screen",
        }.get(tipo)
        if edif == "EXT" and kind in ("climate", "climate_co2"):
            kind = "weather_station"
        return (kind, edif, planta, codigo, resto) if kind else None
    if r := re.match(r"^UEXCC_INF_CAL_(\d+)$", m):
        return ("climate", "INF", None, "EDIFICIO", r.group(1))  # sondas de calibración de modelos térmicos
    if m == "UEXCC_INF_EXT_SEN001_C":
        return ("weather_station", "EXT", None, "INF", None)
    if r := re.match(r"^UEXCC_SEN(AEMET|OPENWEATHER)\d_TH$", m):
        return ("weather_station", "EXT", None, r.group(1), None)
    if r := re.match(r"^UEXCC_([A-Z]+)_PEOPLE$", m):
        return ("occupancy_camera", r.group(1), None, "EDIFICIO", None)
    return None


def nombre_sensor(kind: str, resto: str | None, codigo: str) -> str:
    base = NOMBRE_KIND[kind]
    if resto and (r := re.match(r"^(RAD|SAV|PER)_([PV])([NSEO]?)_(\d+)$", resto)):
        _, pv, ori, n = r.groups()
        donde = "ventana" if pv == "V" else "pared"
        return f"{base} {int(n)} ({donde}{' ' + ORIENTACION[ori] if ori else ''})"
    if resto and resto.isdigit():
        return f"Sonda de temperatura {int(resto)}"
    if resto and (r := re.match(r"^SEN(\d+)", resto)):
        return f"{base} {int(r.group(1))}"
    return f"{base} {codigo}".strip()


def main() -> None:
    inventario = json.loads(INVENTARIO.read_text(encoding="utf-8"))["measurements"]
    locales = json.loads(INTERIORES.read_text(encoding="utf-8"))["locales"]
    por_nombre = {_norm(v["nombre_actual"].split("(")[0]): k for k, v in locales.items() if v.get("nombre_actual")}

    espacios: dict[str, dict] = {}
    por_numero: dict[tuple[str, str, int], str] = {}
    sensores: list[dict] = []

    def prefijo_unity(loc: dict) -> str:
        return "LAB" if loc["uso"].startswith("lab_") else TIPO_POR_USO.get(loc["uso"], ("other", "LOC"))[1]

    def espacio(edif: str, planta: str | None, codigo: str, unity_room: str | None = None) -> str:
        if edif == "EXT":
            planta = None
        if planta is None:  # sensores de todo el edificio o del exterior
            eid = f"{edif}-{codigo}" if edif != "EXT" else "EXT-CAMPUS"
            if eid not in espacios:
                espacios[eid] = dict(id=eid, building_id=edif, floor=None, type="outdoor" if edif == "EXT" else "building",
                                     code=codigo, name="Exterior del campus" if edif == "EXT" else f"{EDIFICIOS[edif][0]} (todo el edificio)",
                                     unity_room=None)
            return eid
        # SmartPoliTech usa a veces prefijos distintos para la misma estancia (AUL024 y LAB024 son la sala
        # NORBA): la estancia se identifica por edificio, planta y número, y el prefijo sale del uso real.
        num = int(re.sub(r"\D", "", codigo) or 0)
        if (edif, planta, num) in por_numero:
            return por_numero[(edif, planta, num)]
        uni = EDIFICIOS.get(edif, (None, None))[1]
        clave = unity_room or (f"{uni}_{PLANTA_A_UNITY.get(planta, planta)}_{num:02d}" if uni else None)
        loc = locales.get(clave) if clave else None
        tipo = TIPO_POR_PREFIJO.get(re.sub(r"\d", "", codigo), "other")
        if loc:
            codigo = f"{prefijo_unity(loc)}{num:03d}"
            tipo = "lab" if loc["uso"].startswith("lab_") else TIPO_POR_USO.get(loc["uso"], (tipo,))[0]
        eid = f"{edif}-{planta}-{codigo}"
        dims = (loc or {}).get("dimensiones_m")
        espacios[eid] = dict(
            id=eid, building_id=edif, floor=planta, type=tipo, code=codigo,
            name=(loc or {}).get("nombre_actual") or (loc or {}).get("rotulo_2013"),
            unity_room=clave if loc else None,
            area_m2=(loc or {}).get("superficie_m2_aprox") or (round(dims[0] * dims[1], 1) if dims else None),
            height_m=(loc or {}).get("altura_libre_m"),
        )
        por_numero[(edif, planta, num)] = eid
        return eid

    # 1. Sensores reales
    for m, info in sorted(inventario.items()):
        if DESCARTAR.match(m):
            continue
        campos = info["fields"]
        ultimo = info["last"] if isinstance(info["last"], dict) else None
        cl = clasificar(m)
        if cl is None:
            # Cámaras de aula: C1, I-1, O6, T3, C9-bis... se enlazan por el nombre del aula en el modelo
            if "ocupacion" in campos or "people" in campos:
                clave = por_nombre.get(_norm(m.replace("-bis", "")))
                if clave:
                    loc = locales[clave]
                    edif, planta = UNITY_A_CODIGO[loc["edificio"]], UNITY_A_PLANTA[loc["planta"]]
                    eid = espacio(edif, planta, f"AUL{int(loc['num']):03d}", clave)
                else:  # AULCAMn: cámaras sin ubicación publicada
                    eid = espacio("SCO", None, "AULCAM")
                cl = ("occupancy_camera", espacios[eid]["building_id"], None, None, None)
            else:
                continue
        else:
            kind, edif, planta, codigo, resto = cl
            eid = espacio(edif, planta, codigo)
        kind = cl[0]
        if kind == "wifi_occupancy" and "Actual_Informatica" in campos:
            # Un campo por edificio: un sensor por edificio
            for campo, edif in [("Actual_Central", "SCO"), ("Actual_Edificacion", "ATE"), ("Actual_Informatica", "INF"),
                                ("Actual_Construccion", "OPU"), ("Actual_Telecomunicacion", "TEL"), ("Actual_Investigacion", "INV")]:
                sensores.append(dict(
                    id=f"{m}_{edif}", name=f"Ocupación wifi ({EDIFICIOS[edif][0]})", kind=kind,
                    space_id=espacio(edif, None, "EDIFICIO"), building_id=edif, properties=["wifi_clients"],
                    real_available=True, upstream={"measurement": m, "fields": {"wifi_clients": campo}},
                    last_real_time=_iso(ultimo and ultimo.get("time"))))
            continue
        if kind == "wifi_occupancy" and "Actual" not in campos:
            continue
        mapa = mapeo_campos(kind, campos)
        if not mapa:
            continue
        esp = espacios[eid]
        sensores.append(dict(
            id=m, name=nombre_sensor(kind, cl[4], m if cl[3] is None else ""), kind=kind, space_id=eid,
            building_id=esp["building_id"], properties=list(mapa), real_available=True,
            upstream={"measurement": m, "fields": mapa}, last_real_time=_iso(ultimo and ultimo.get("time")),
        ))

    # 2. Estancias del modelo de Unity sin sensores reales
    con_unity = {e["unity_room"] for e in espacios.values() if e["unity_room"]}
    for clave, loc in locales.items():
        if clave in con_unity or loc["edificio"] not in UNITY_A_CODIGO:
            continue
        edif = UNITY_A_CODIGO[loc["edificio"]]
        planta = UNITY_A_PLANTA.get(loc["planta"], loc["planta"])
        num = re.sub(r"\W", "", loc["num"])
        if num.isdigit():
            espacio(edif, planta, f"{prefijo_unity(loc)}{int(num):03d}", clave)
            continue
        tipo = "lab" if loc["uso"].startswith("lab_") else TIPO_POR_USO.get(loc["uso"], ("other",))[0]
        eid = f"{edif}-{planta}-{prefijo_unity(loc)}{num.upper()}"
        dims = loc.get("dimensiones_m")
        espacios[eid] = dict(
            id=eid, building_id=edif, floor=planta, type=tipo, code=eid.split("-")[-1],
            name=loc.get("nombre_actual") or loc.get("rotulo_2013"), unity_room=clave,
            area_m2=loc.get("superficie_m2_aprox") or (round(dims[0] * dims[1], 1) if dims else None),
            height_m=loc.get("altura_libre_m"),
        )

    # 3. Sensores virtuales: lo que tendría un gemelo completo y SmartPoliTech no mide (o ya no mide)
    reales_por_espacio: dict[str, set[str]] = {}
    for s in sensores:
        reales_por_espacio.setdefault(s["space_id"], set()).update(s["properties"])
    virtuales = {
        "classroom": ["climate_co2", "occupancy_camera", "light_sensor", "sound_meter", "air_quality"],
        "computer_lab": ["climate_co2", "occupancy_camera", "light_sensor", "pc_usage", "sound_meter"],
        "lab": ["climate_co2", "occupancy_camera", "light_sensor"],
        "meeting_room": ["climate_co2", "occupancy_camera", "light_sensor"],
        "cafeteria": ["climate_co2", "occupancy_camera", "sound_meter", "air_quality"],
        "hall": ["climate", "occupancy_camera", "light_sensor", "fire_alarm"],
        "office": ["climate"],
    }
    props_kind = {
        "climate": ["air_temperature", "relative_humidity"],
        "climate_co2": ["air_temperature", "relative_humidity", "co2_concentration"],
        "occupancy_camera": ["occupancy"], "light_sensor": ["illuminance", "lights_on"],
        "sound_meter": ["sound_level"], "air_quality": ["pm25", "co2_concentration"],
        "pc_usage": ["devices_on"], "fire_alarm": ["alarm_active"],
        "power_meter": ["active_power", "active_energy"], "water_meter": ["water_volume", "water_flow"],
    }
    for e in list(espacios.values()):
        tiene = reales_por_espacio.get(e["id"], set())
        for kind in virtuales.get(e["type"], []):
            if set(props_kind[kind]) <= tiene:
                continue
            sensores.append(dict(
                id=f"SIM_{e['id']}_{kind.upper()}", name=f"{NOMBRE_KIND[kind]} (virtual)", kind=kind,
                space_id=e["id"], building_id=e["building_id"], properties=props_kind[kind],
                real_available=False, upstream=None, last_real_time=None))
    for edif in EDIFICIOS:
        if edif == "EXT":
            continue
        eid = espacio(edif, None, "EDIFICIO")
        kinds_reales = {s["kind"] for s in sensores if s["building_id"] == edif and s["real_available"]}
        for kind in ("power_meter", "water_meter", "fire_alarm"):
            if kind == "fire_alarm" or kind not in kinds_reales:
                sensores.append(dict(
                    id=f"SIM_{eid}_{kind.upper()}", name=f"{NOMBRE_KIND[kind]} del edificio (virtual)", kind=kind,
                    space_id=eid, building_id=edif, properties=props_kind[kind],
                    real_available=False, upstream=None, last_real_time=None))

    # 4. Edificios
    edificios = []
    for c, (nombre, uni) in EDIFICIOS.items():
        plantas = sorted({e["floor"] for e in espacios.values() if e["building_id"] == c and e["floor"]},
                         key=lambda p: list(PLANTA_A_UNITY).index(p) if p in PLANTA_A_UNITY else 9)
        edificios.append(dict(id=c, name=nombre, unity_id=uni, floors=plantas))

    for e in espacios.values():
        e.setdefault("area_m2", None)
        e.setdefault("height_m", None)
        e["capacity"] = None
    catalogo = {
        "_meta": {"generado_por": "herramientas/generar_catalogo.py", "generado": datetime.now(UTC).isoformat(timespec="seconds"),
                  "fuentes": ["datos/inventario_smartpolitech.json", "Referencias/Politecnica/datos/interiores.json"]},
        "buildings": edificios,
        "spaces": sorted(espacios.values(), key=lambda e: e["id"]),
        "sensors": sorted(sensores, key=lambda s: s["id"]),
    }
    SALIDA.write_text(json.dumps(catalogo, ensure_ascii=False, indent=1), encoding="utf-8")
    reales = sum(s["real_available"] for s in sensores)
    print(f"{len(edificios)} edificios, {len(espacios)} estancias "
          f"({sum(1 for e in espacios.values() if e['unity_room'])} enlazadas con Unity), "
          f"{len(sensores)} sensores ({reales} reales, {len(sensores) - reales} virtuales)")


if __name__ == "__main__":
    main()
