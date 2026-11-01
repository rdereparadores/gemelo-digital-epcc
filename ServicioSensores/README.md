# Servicio de sensores del gemelo digital (EPCC)

Puente REST entre los sensores de **SmartPoliTech** (Escuela Politécnica de Cáceres, UEx) y el gemelo
digital del campus en Unity. Cada sensor sirve datos reales, simulados o «auto» (real si hay dato
reciente; si no, simulado). Lleva un panel de control web para elegir la fuente, manejar la
simulación y ajustar el servicio.

```text
SmartPoliTech (Grafana + InfluxDB) ──sondeo──▶ ┌────────────────────────┐ ──REST/SSE──▶ Unity (ClienteGemelo)
                                                │ política de fuentes     │
Motor de simulación (por estancia) ──tick────▶ │ estado agregado, ETag   │ ──────────▶ Panel /panel/
                                                └────────────────────────┘
```

## Arrancar

```bash
cd ServicioSensores
uv sync
uv run gemelo-sensores            # http://127.0.0.1:8800  (--host 0.0.0.0 --port 8800)
```

- Panel de control: <http://127.0.0.1:8800/panel/>
- Documentación OpenAPI: <http://127.0.0.1:8800/api/v1/docs>
- Estado de todo el campus para Unity: <http://127.0.0.1:8800/api/v1/state>

Los ajustes y la política de fuentes se guardan en `datos/estado/`, que no se versiona.

## Estructura

| Ruta | Qué es |
|---|---|
| [`docs/CONTRATO_API.md`](docs/CONTRATO_API.md) | Contrato de la API v1: convenciones, recursos y modelo |
| [`docs/SENSORES_SMARTPOLITECH.md`](docs/SENSORES_SMARTPOLITECH.md) | Investigación: qué publica SmartPoliTech y en qué estado está |
| `herramientas/inventariar.py` | Inventario de SmartPoliTech → `datos/inventario_smartpolitech.json` |
| `herramientas/generar_catalogo.py` | Inventario + `interiores.json` del modelo de edificios → `datos/catalogo.json` (el catálogo ya va generado; el `interiores.json` sale de los generadores de la escena, que no se publican) |
| `src/gemelo_sensores/` | Servicio: `api.py` (rutas), `gemelo.py` (orquestación), `smartpolitech.py` (cliente), `simulacion/` (motor) |
| `panel/` | Panel de control estático (HTML, CSS y módulos ES; sin compilación) con la identidad corporativa de la UEx |
| `clientes/unity/` | Cliente C# mínimo para otros proyectos de Unity (ver abajo) |
| `tests/` | `uv run pytest` |

El panel sigue el Manual de Identidad Corporativa de la UEx: sello institucional en negativo sobre la
cabecera negra, denominación del centro en Charis SIL, textos en Montserrat, amarillo `#F4B223` para la
acción principal y la selección, y `#7A5A00` cuando el amarillo hace de texto en el tema claro. El verde
corporativo marca el dato real; el violeta, el simulado; los avisos van en naranja para no confundirse con
el amarillo. Los logotipos y favicons están en `panel/marca/` y las fuentes (subconjunto latino en WOFF2,
licencia SIL OFL) en `panel/fuentes/`.

Para regenerar el catálogo:

```bash
uv run python herramientas/inventariar.py
uv run python herramientas/generar_catalogo.py
```

## Unity

La app del gemelo de este repositorio ya trae su propio cliente, más completo, en `Assets/_Gemelo/Nucleo/`
(ver [su README](../Assets/_Gemelo/README.md)), y tiene permitido el HTTP en Player Settings.

`clientes/unity/` es un cliente mínimo de ejemplo para usar el servicio desde **otro** proyecto de Unity. No se copia
a este: sus clases (`Gemelo.ClienteGemelo`…) chocarían con las del gemelo.

1. Un GameObject con `ClienteGemelo`, con `urlBase` = `http://127.0.0.1:8800`. Sondea `/api/v1/state`
   cada `intervalo` segundos; con `If-None-Match`, si nada ha cambiado la respuesta es un 304 vacío.
2. `ClienteGemelo.Instancia.Estancia("1903_P1_24", out var sala)` devuelve el estado de la sala:
   métricas, ventanas abiertas, radiadores activos, confort y alertas. Las claves son las de
   `interiores.json`, las mismas que usan los prefabs de la Politécnica.
3. `IndicadorEstancia` es un ejemplo que tiñe un renderer por temperatura, CO₂, ocupación o confort.
4. `ClienteGemelo.Instancia.CrearEvento("open_windows", "INF-P01-AUL024")` dispara eventos de simulación desde la escena.

Unity 6 bloquea por defecto las descargas por HTTP: hay que permitirlas en Player Settings ▸ Allow downloads over
HTTP, o servir el servicio detrás de HTTPS.
