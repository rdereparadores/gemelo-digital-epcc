# Sensores públicos de SmartPoliTech: qué hay y en qué estado

Investigación del 30-10-2026. El inventario completo, regenerable, está en
`datos/inventario_smartpolitech.json` (`herramientas/inventariar.py`).

## Cómo se accede

- `http://smartpolitech.unex.es` es un **Grafana 9.4.3 con acceso anónimo de lectura** (por HTTPS no
  responde). Tiene 116 paneles en carpetas: Ambientales, Agua, Electricidad, Gas, Ocupación y
  equipos, Espacios (NORBA, NOVELL, aula I1, cafetería), baterías y otros.
- Los paneles leen un **InfluxDB 1.x** (fuente `smart-sensors`, id 1, base `sensors`) a través del
  proxy de Grafana: `GET /api/datasources/proxy/1/query?db=sensors&q=<InfluxQL>`. El servicio usa esa
  misma vía, solo con consultas de lectura y acotadas en el tiempo.
- Hay **608 mediciones**. Las del campus se llaman `UEXCC_<edificio>_<planta>_<estancia><nº>_<tipo>`.
  - Edificios: `SCO` y `COM` (Común), `ATE` (Edificación), `INF`, `OPU` (Civil), `TEL`, `INV`, `ACU` y `EXT`.
  - Plantas: `PS2`, `PS1`, `P00`, `P01` y `P02`.
  - El número de estancia es el de los planos de 2013 que usa el modelo de Unity: `INF_P01_AUL024` es
    `1903_P1_24` (sala NORBA) e `INF_P00_AUL030` es `1903_P0_30` (aula I-1).
- Trampas encontradas:
  - Pedir campos concretos (`SELECT "a","b"`) de algunas mediciones cuelga el InfluxDB; con `SELECT *` responde en ~0,1 s.
  - Los analizadores de red guardan tantos puntos que `ORDER BY time DESC LIMIT 1` sin acotar el tiempo agota el plazo.
  - Las válvulas con la sonda rota envían -40 °C y 0 % de humedad.

## Inventario por tipo (estado a 30-10-2026, 00:30)

| Tipo | Origen | Sensores | Último dato < 2 días | < 30 días | < 1 año | Más viejo o sin datos |
|---|---|---|---|---|---|---|
| Válvula termostática LoRa (MClimate Vicki): apertura, consigna, temperatura, batería | `*_RAD_*` | 178 | 124 | 7 | 23 | 24 |
| Contacto de ventana con temperatura y humedad | `*_SAV_*` | 95 | 21 | 4 | 22 | 48 |
| Persiana / actuador (`state`) | `*_PER_*` | 92 | 0 | 57 | 33 | 2 |
| Termohigrómetros (THR/THV/THM, con ventana en THV) y sondas de calibración de Informática | `*_TH?`, `UEXCC_INF_CAL_*` | 76 | 0 | 16 | 0 | 60 |
| CO₂ + temperatura + humedad (LoRa) | `*_C`, `*_THC` | 28 | 0 | 11 | 2 | 15 |
| PC encendidos por aula y por subred | `*_IPS` | 33 | 32 | 0 | 0 | 1 |
| Analizadores de red (potencia y energía por fase) | `*_ELE` | 29 | 0 | 0 | 1 | 28 |
| Cámaras de ocupación de aulas (C1…C9, I-1, I-5, O6, O7, T1, T3, AULCAM1–13) | varios | 28 | 0 | 0 | 0 | 28 (2020–2022) |
| Contadores de agua (pulsos de 10 L) | `*_AGU` | 14 | 1 | 0 | 0 | 13 |
| Ocupación wifi por pabellón y por punto de acceso | `*_APC` | 7 | 0 | 0 | 0 | 7 (2022) |
| Estación AEMET 3469A, OpenWeather, exterior | `*_MET`, `SENAEMET`, `SENOPENWEATHER` | 6 | 0 | 0 | 0 | 6 |
| Pantallas de cartelería | `*_STV` | 5 | 0 | 0 | 0 | 5 |
| Calderas (impulsión y retorno por pabellón) | `*_CGT` | 1 | 0 | 0 | 0 | 1 |
| Inversor fotovoltaico de Teleco | `*_INV` | 1 | 0 | 0 | 0 | 1 |
| Sonómetro (LAeq) del laboratorio de Acústica | `*_SON` | 1 | 0 | 0 | 0 | 1 |

Por edificio, casi todo lo vivo está en **Informática** (161 sensores con dato de las últimas 48 h;
el piloto de climatización con válvulas, ventanas y persianas en aulas y despachos). Del resto de
pabellones solo quedan vivos los contadores de PC y algún contador.

Además: un menú de la cafetería (`menu_cafeteria`, último en 2026), mediciones de pruebas y caudales
de pueblos de la Diputación (`CAUDAL_*`, fuera del campus). El catálogo los descarta.

Notas:
- Los datos «vivos» llegan por lotes: a la hora del inventario, los 177 más recientes tenían ~17 h.
  Con el umbral de frescura por defecto (2 h) el modo `auto` los sirve simulados. Se puede subir en
  Ajustes ▸ `stale_after_s`.
- Las válvulas dan la apertura como `motorPosition`/`motorRange`: apertura = (1 − pos/rango)·100.

## Qué falta y se simula

El catálogo añade **749 sensores virtuales** (`SIM_…`) para que cada estancia del modelo tenga datos:
- CO₂, ocupación, luz, ruido y PM2,5 en aulas, laboratorios, salas y cafetería.
- Temperatura en despachos y halls.
- Detectores de incendio en halls y edificios.
- Contadores de electricidad y agua en los edificios sin ellos.
- El Salón de Grados de Telecomunicación (`TEL-P00-LOC017`, tipo `auditorium`), que no tiene ningún
  sensor real: CO₂, ocupación, luz, ruido, PM2,5 y un analizador de red. Su ocupación simulada son
  actos (conferencias en periodo lectivo y sesiones de defensas de TFG/TFM en los de exámenes).
