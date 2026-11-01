# Contrato de la API del gemelo digital (v1)

Servicio REST que hace de puente entre los sensores de SmartPoliTech (EPCC, Cáceres) y el gemelo
digital en Unity. Cada sensor puede servir datos **reales** (SmartPoliTech), **simulados** (motor
propio) o **auto** (real si hay un dato reciente; si no, simulado).

La especificación ejecutable es el OpenAPI que genera el servicio (`/api/v1/openapi.json`, con
documentación en `/api/v1/docs`). Este documento fija las decisiones y es la referencia para el
panel, el simulador y el cliente de Unity.

## Convenciones

- Base: `/api/v1`. La versión mayor va en la ruta; los cambios compatibles no la cambian.
- Recursos en plural y en inglés, `kebab-case` en rutas y `snake_case` en JSON.
- JSON en UTF-8. Fechas en ISO 8601 con zona (`2026-10-30T11:42:07Z`), siempre en UTC.
- Unidades en el código UCUM (`Cel`, `%`, `ppm`, `W`, `kW.h`, `L`, `L/min`, `V`, `dB`, `lx`, `ug/m3`, `{people}`).
- Errores en `application/problem+json` (RFC 9457): `type`, `title`, `status`, `detail`, `instance`.
- Colecciones paginadas: `?limit=` (por defecto 100, máx. 1000) y `?offset=`. La respuesta es
  `{"items": [...], "total": N, "limit": L, "offset": O}` y lleva una cabecera `Link` (RFC 8288) con
  `rel="next"` y `rel="prev"` cuando aplica.
- Filtros como parámetros de consulta con el nombre del campo (`?building=INF&kind=radiator_valve`).
- Las lecturas de estado llevan `ETag` y aceptan `If-None-Match` (responden 304), para que Unity
  sondee barato. También llevan `Cache-Control: no-cache`.
- Modificaciones con `PATCH` en JSON Merge Patch (RFC 7396, `application/merge-patch+json`; también
  se acepta `application/json`). `PUT` reemplaza el recurso entero.
- Acciones que no son CRUD (inyectar un evento en la simulación) se modelan como recursos creados
  con `POST` (`/simulation/events`), que responden `201` con `Location`.
- Las estructuras se pueden leer con `JsonUtility` de Unity: nada de diccionarios con claves
  variables en las respuestas de estado; los valores van en listas de objetos
  `{"property", "value", "unit"}`.

## Modelo

Se inspira en OGC SensorThings API (Thing → Datastream → Observation) y en SOSA/SSN, simplificado:

| Recurso | Qué es | Id |
|---|---|---|
| `Building` | Pabellón de la EPCC | código SmartPoliTech: `INF`, `ATE`, `TEL`, `OPU`, `SCO`, `INV`, `ACU`, `EXT` |
| `Space` | Estancia (aula, laboratorio, despacho, pasillo…) o zona exterior | `INF-P01-AUL024` |
| `Sensor` | Dispositivo que mide una o varias propiedades | id de SmartPoliTech (`UEXCC_INF_P01_AUL024_RAD_PE_001`) o `SIM_…` si solo existe simulado |
| `Observation` | Lectura de un sensor en un instante | — |
| `ObservedProperty` | Magnitud medida (`air_temperature`…) | nombre de la propiedad |

### Building

```json
{
  "id": "INF",
  "name": "Pabellón de Informática",
  "unity_id": "1903",
  "floors": ["PS1", "P00", "P01"],
  "space_count": 61,
  "sensor_count": 312,
  "links": {"self": "/api/v1/buildings/INF", "spaces": "/api/v1/spaces?building=INF", "state": "/api/v1/buildings/INF/state"}
}
```

### Space

```json
{
  "id": "INF-P01-AUL024",
  "building_id": "INF",
  "floor": "P01",
  "type": "computer_lab",
  "code": "AUL024",
  "name": "NORBA (Sala NORBA I y II)",
  "unity_room": "1903_P1_24",
  "sensor_count": 21,
  "links": {"self": "...", "sensors": "/api/v1/sensors?space=INF-P01-AUL024", "state": "/api/v1/spaces/INF-P01-AUL024/state"}
}
```

`type`: `classroom`, `computer_lab`, `lab`, `office`, `corridor`, `hall`, `technical_room`,
`restroom`, `cafeteria`, `meeting_room`, `outdoor`, `building` (sensores de todo el edificio) u `other`.

`floor` es `null` en las zonas de edificio entero y en el exterior. `unity_room` es la clave de `Referencias/Politecnica/datos/interiores.json` (`<edificio>_<planta>_<num>`),
o `null` si la estancia no está en el modelo.

### Sensor

```json
{
  "id": "UEXCC_INF_P01_AUL024_RAD_PE_001",
  "name": "Válvula de radiador 1 (pared este)",
  "kind": "radiator_valve",
  "space_id": "INF-P01-AUL024",
  "building_id": "INF",
  "properties": [
    {"name": "valve_position", "unit": "%", "type": "number"},
    {"name": "target_temperature", "unit": "Cel", "type": "number"},
    {"name": "air_temperature", "unit": "Cel", "type": "number"}
  ],
  "real_available": true,
  "mode": "auto",
  "effective_source": "simulated",
  "last_real_time": "2025-04-02T08:11:00Z",
  "links": {"self": "...", "observations": ".../observations", "latest": ".../observations/latest"}
}
```

- `kind`: tipo de dispositivo; ver la tabla de tipos.
- `real_available`: existe en SmartPoliTech. Los sensores `SIM_…` tienen `false`: solo pueden
  servir datos simulados y su `mode` efectivo es siempre `simulated`.
- `mode`: modo pedido, `real` | `simulated` | `auto`, resuelto con la política de fuentes.
- `effective_source`: de dónde sale ahora mismo el dato: `real` | `simulated` | `none`.
  - En modo `real` se sirve el último dato real dentro de `settings.upstream.lookback_s` (7 días por
    defecto), aunque sea viejo; su `time` dice de cuándo es. Si no hay ninguno, es `none` y la lectura
    no se inventa.
  - En modo `auto`, el dato real solo gana si es más reciente que `stale_after_s` (2 h por defecto).

### Observation

```json
{
  "sensor_id": "UEXCC_INF_P01_AUL024_RAD_PE_001",
  "time": "2026-10-30T11:42:07Z",
  "source": "simulated",
  "values": [
    {"property": "valve_position", "value": 35.0, "unit": "%"},
    {"property": "target_temperature", "value": 21.0, "unit": "Cel"}
  ]
}
```

Los booleanos (`window_open`, `door_open`, `power_on`…) van como `1.0`/`0.0` con `unit` `"1"`
y `type` `"boolean"` en la definición de la propiedad.

### Propiedades observadas

| property | unit | type |
|---|---|---|
| `air_temperature` | `Cel` | number |
| `relative_humidity` | `%` | number |
| `co2_concentration` | `ppm` | number |
| `window_open` | `1` | boolean |
| `door_open` | `1` | boolean |
| `valve_position` | `%` | number |
| `target_temperature` | `Cel` | number |
| `battery_voltage` | `V` | number |
| `active_power` | `W` | number |
| `active_energy` | `kW.h` | number (contador monótono) |
| `water_volume` | `L` | number (litros desde las 00:00, hora de Madrid) |
| `water_flow` | `L/min` | number |
| `occupancy` | `{people}` | number |
| `devices_on` | `{devices}` | number |
| `power_on` | `1` | boolean |
| `blind_position` | `%` | number (0 = subida, 100 = bajada) |
| `sound_level` | `dB` | number (LAeq) |
| `illuminance` | `lx` | number |
| `pm25` | `ug/m3` | number |
| `water_temperature` | `Cel` | number |
| `wind_speed` | `m/s` | number |
| `wind_direction` | `deg` | number |
| `atmospheric_pressure` | `hPa` | number |
| `precipitation` | `mm` | number |
| `solar_power` | `W` | number |
| `lights_on` | `1` | boolean |
| `alarm_active` | `1` | boolean |
| `wifi_clients` | `{devices}` | number |

### Tipos de sensor (`kind`)

| kind | Origen en SmartPoliTech | Propiedades |
|---|---|---|
| `radiator_valve` | `*_RAD_*` (válvula termostática LoRa) | valve_position, target_temperature, air_temperature, battery_voltage |
| `window_contact` | `*_SAV_*` (apertura de ventana con T/H) | window_open, air_temperature, relative_humidity |
| `blind` | `*_PER_*` (actuador de persiana) | blind_position |
| `climate` | `*_THR`, `*_THV`, `*_THM`, `*_THC` | air_temperature, relative_humidity (+ window_open, co2_concentration según el modelo) |
| `climate_co2` | `*_C` | air_temperature, relative_humidity, co2_concentration |
| `pc_usage` | `*_IPS` (PC encendidos de un aula) | devices_on |
| `power_meter` | `*_ELE` (analizador de red) | active_power, active_energy |
| `water_meter` | `*_AGU` | water_volume, water_flow |
| `heating_plant` | `*_CGT`, `UEXCC_INF_CAL_*` (calderas e impulsiones) | water_temperature |
| `solar_inverter` | `*_INV` (inversor fotovoltaico) | solar_power, active_energy |
| `occupancy_camera` | `AULCAM*`, `C1`, `I-1`, `O6`, `T1`… y `*_PEOPLE` | occupancy |
| `wifi_occupancy` | `*_APC`, `*_APS` (clientes wifi por edificio y por punto de acceso) | wifi_clients |
| `sound_meter` | `*_SON` | sound_level |
| `weather_station` | `*_MET`, `SENAEMET`, `SENOPENWEATHER`, `EXT_*` | air_temperature, relative_humidity, wind_speed, wind_direction, atmospheric_pressure, precipitation |
| `signage_screen` | `*_STV` (pantalla de cartelería) | power_on |
| `light_sensor` | solo simulado | illuminance, lights_on |
| `air_quality` | solo simulado | pm25, co2_concentration |
| `fire_alarm` | solo simulado | alarm_active |

## Recursos y rutas

### Catálogo (solo lectura)

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1` | Raíz con enlaces a las colecciones y la versión |
| `GET /api/v1/buildings` | Colección paginada de `Building` |
| `GET /api/v1/buildings/{building_id}` | `Building` |
| `GET /api/v1/spaces?building=&floor=&type=&has_unity_room=` | Colección de `Space` |
| `GET /api/v1/spaces/{space_id}` | `Space` |
| `GET /api/v1/sensors?building=&space=&kind=&mode=&source=&real_available=&q=` | Colección de `Sensor` (`q` busca en id y nombre) |
| `GET /api/v1/sensors/{sensor_id}` | `Sensor` |
| `GET /api/v1/observed-properties` | Lista de propiedades con unidad y tipo |

### Observaciones

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1/sensors/{id}/observations/latest` | `Observation` (404 si aún no hay ninguna) |
| `GET /api/v1/sensors/{id}/observations?from=&to=&limit=` | Colección de `Observation`, más reciente primero. Sin `from` se usa la última hora. Los datos reales salen del histórico de SmartPoliTech; los simulados, del búfer del servicio (24 h) |
| `GET /api/v1/observations/latest?building=&space=&kind=` | Última `Observation` de cada sensor filtrado |
| `GET /api/v1/observations/stream?building=&space=&kind=` | Server-Sent Events. Evento `observation` con un `Observation` en `data`; `: ping` cada 15 s |

### Estado agregado (lo que consume Unity)

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1/spaces/{id}/state` | `SpaceState` |
| `GET /api/v1/buildings/{id}/state` | `BuildingState` |
| `GET /api/v1/state` | `CampusState`: todo el campus en un documento, con `ETag` |

`SpaceState`:

```json
{
  "space_id": "INF-P01-AUL024",
  "unity_room": "1903_P1_24",
  "time": "2026-10-30T11:42:07Z",
  "metrics": [
    {"property": "air_temperature", "value": 21.4, "unit": "Cel", "source": "real", "sensor_count": 2},
    {"property": "co2_concentration", "value": 812, "unit": "ppm", "source": "simulated", "sensor_count": 1},
    {"property": "occupancy", "value": 23, "unit": "{people}", "source": "simulated", "sensor_count": 1}
  ],
  "windows_open": 1,
  "windows_total": 6,
  "radiators_active": 3,
  "radiators_total": 7,
  "comfort": "ok",
  "alerts": [{"code": "co2_high", "severity": "warning", "message": "CO₂ por encima de 1000 ppm"}]
}
```

- Agregación: media para temperatura, humedad, CO₂, ruido, luz y posición de válvulas y
  persianas; suma para ocupación, potencia, consumo y equipos; máximo para alarmas.
- `comfort`: `ok` | `cold` | `hot` | `stuffy` (CO₂ > 1000 ppm) | `humid` | `dry` | `unknown`.
- `alerts`: `co2_high` (> 1000), `co2_critical` (> 1500), `window_open_heating` (ventana abierta con
  radiadores activos), `temperature_low` (< 17 °C con ocupación), `temperature_high` (> 27 °C),
  `fire_alarm`, `sensor_battery_low`, `sensor_stale`.

`BuildingState`: `building_id`, `unity_id`, `time`, `metrics` (agregado del edificio: temperatura
media, ocupación total, potencia, agua de hoy…), `spaces` (lista de `SpaceState` resumidos) y `alerts`.

`CampusState`: `time`, `simulation` (estado del reloj y del escenario), `weather` y `buildings`
(lista de `BuildingState`). `weather` lleva `metrics` (las del exterior, reales o simuladas) y el
estado del cielo del modelo, pensado para la iluminación de Unity: `sun_elevation` (grados),
`cloud_cover` (0–1), `solar_irradiance` (W/m²) y `precipitation_rate` (mm/h).

### Política de fuentes y ajustes

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1/source-policy` | `SourcePolicy` |
| `PUT /api/v1/source-policy` | Reemplaza la política |
| `PATCH /api/v1/source-policy` | Merge patch (poner una clave a `null` la borra) |
| `PATCH /api/v1/sensors/{id}` | Solo admite `{"mode": "real"\|"simulated"\|"auto"\|null}`; escribe en `source-policy.sensors` (`null` quita la excepción) |
| `GET /api/v1/settings` | `Settings` |
| `PATCH /api/v1/settings` | Merge patch |

`SourcePolicy` (gana la regla más específica: sensor → tipo → estancia → edificio → `default`):

```json
{
  "default": "auto",
  "buildings": {"TEL": "simulated"},
  "spaces": {},
  "kinds": {"occupancy_camera": "simulated"},
  "sensors": {"UEXCC_INF_P01_AUL024_RAD_PE_001": "real"}
}
```

`Settings`:

```json
{
  "upstream": {
    "enabled": true,
    "base_url": "http://smartpolitech.unex.es",
    "datasource_id": 1,
    "database": "sensors",
    "poll_interval_s": 60,
    "timeout_s": 20,
    "stale_after_s": 7200,
    "lookback_s": 604800
  },
  "simulation": {"tick_s": 5, "seed": 42},
  "stream": {"heartbeat_s": 15},
  "cors_origins": ["*"]
}
```

### Simulación

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1/simulation` | `SimulationState` |
| `PATCH /api/v1/simulation` | Merge patch sobre `SimulationState` (`running`, `time_scale`, `scenario`, `clock`) |
| `GET /api/v1/simulation/scenarios` | Lista de escenarios con `id`, `name` y `description` |
| `GET /api/v1/simulation/events` | Eventos activos e historial reciente |
| `POST /api/v1/simulation/events` | Crea un evento (`201` + `Location`) |
| `DELETE /api/v1/simulation/events/{event_id}` | Termina un evento antes de tiempo (`204`) |

`SimulationState`:

```json
{
  "running": true,
  "time_scale": 1.0,
  "clock": "live",
  "sim_time": "2026-10-30T11:42:07Z",
  "model_time": "2026-10-30T11:42:07Z",
  "scenario": "normal_day",
  "weather_source": "real",
  "weather_in_use": "simulated"
}
```

- `clock`: `live` (la hora real) u otra hora ISO 8601 desde la que arranca el reloj simulado
  (para enseñar, por ejemplo, un lunes de enero a las 9:00 en plena clase).
- `time_scale`: 1 = tiempo real; hasta 3600.
- `weather_source`: `real` usa la temperatura exterior real si está disponible; `simulated`, un clima
  sintético de Cáceres. `weather_in_use` dice cuál se está usando de verdad (solo lectura).
- `model_time` (solo lectura): hora que usa el modelo. Coincide con `sim_time` salvo en los escenarios
  anclados a una fecha (`winter_morning`, `summer_afternoon`). Unity debe usarla para el sol.

Escenarios: `normal_day`, `winter_morning`, `summer_afternoon`, `exam_period`, `weekend`,
`holiday_closure`, `heating_failure`, `fire_drill`.

`SimulationEvent` (cuerpo del `POST`):

```json
{"type": "open_windows", "target": "INF-P01-AUL024", "duration_s": 900, "params": {}}
```

`type`: `open_windows`, `close_windows`, `class_start` (`params.people`), `class_end`,
`heating_off`, `heating_on`, `fire_alarm`, `power_outage`, `water_leak`, `sensor_failure`
(`target` = id de sensor). `target` puede ser un sensor, una estancia, un edificio o `campus`.

### Sistema

| Método y ruta | Respuesta |
|---|---|
| `GET /api/v1/health` | `{"status": "ok"\|"degraded", "checks": {...}}` (200 o 503) |
| `GET /api/v1/upstream` | Estado del enlace con SmartPoliTech (ver abajo) |
| `POST /api/v1/upstream/refresh` | Fuerza un sondeo (`202`) |

`GET /api/v1/upstream`:

```json
{
  "base_url": "http://smartpolitech.unex.es", "enabled": true, "reachable": true,
  "last_poll": "...", "last_success": "...", "next_poll": "...", "latency_ms": 61.0, "poll_duration_ms": 4200.0,
  "poll_interval_s": 60, "stale_after_s": 7200,
  "real_sensors": 594, "polled_sensors": 594, "sensors_with_data": 187, "fresh_sensors": 0,
  "quarantined_measurements": [],
  "errors": [{"time": "...", "message": "..."}]
}
```

`sensors_with_data` cuenta los sensores con algún dato real en memoria y `fresh_sensors`, los que
además son más recientes que `stale_after_s` (los que el modo `auto` sirve como reales).

## Panel de control

Se sirve en `/panel/`, estático y sin paso de compilación, y solo habla con la API pública de arriba.
`/` redirige a `/panel/`.
