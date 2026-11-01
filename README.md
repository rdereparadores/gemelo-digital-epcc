# Gemelo digital de la Escuela Politécnica de Cáceres

Recreación en Unity del campus de la Escuela Politécnica de Cáceres (Universidad de Extremadura): los siete
edificios de la escuela (1901–1907) con sus interiores, y el entorno de unos 945 × 888 m con el parque del Arroyo
del Cuartillo y los aparcamientos. Sobre la escena funciona un gemelo digital: cada estancia muestra los datos de
los sensores de SmartPoliTech, o datos simulados donde no hay sensor, y la gente de cada aula aparece en la escena
según su ocupación.

## Qué incluye

- **Escena del campus generada desde datos.** El terreno sale del LiDAR del IGN (MDT de 0,5 m). Calles, aceras,
  parque y vegetación salen de OpenStreetMap y de la ortofoto PNOA. Los edificios se construyen a partir de los
  planos oficiales de 2013, el Catastro y el LiDAR: muros, huecos, escaleras, unos 500 locales amueblados según su
  uso, fachadas y cubiertas. No hay modelado a mano: la geometría la generan scripts de Python y constructores de
  editor.
- **App del gemelo.** Un menú inicial da paso a dos modos:
  - *Explorar el campus*: un avatar en tercera persona que entra en los edificios, se sienta y lanza acciones
    sobre la estancia (empezar una clase, abrir ventanas, activar una alarma…).
  - *Centro de control*: vista cenital con capas de color por estancia (ocupación, temperatura, CO₂, confort…),
    corte por plantas, fichas, gráficas históricas y eventos de simulación.
- **Personas 1:1.** Hay tantos NPC en cada estancia como dice la ocupación del servicio (unas 1.900 en hora
  lectiva). Se mueven por una malla de navegación, se sientan, charlan y evacúan los edificios con una alarma de
  incendio. De cerca son personajes de Mixamo animados y de lejos, maniquíes instanciados en la GPU.
- **Ambiente.** Sol y cielo según la hora del modelo, nubes y lluvia, luces interiores, ventanas y persianas que
  siguen a los sensores, y emergencias (incendio, apagón, fuga de agua).
- **Servicio de sensores.** API REST en FastAPI que hace de puente con SmartPoliTech, simula lo que falta y trae
  un panel web para elegir la fuente de cada sensor y manejar la simulación.

## Requisitos

- **Unity 6000.6.3f1** (URP 17, Input System y AI Navigation, que se instalan solos desde `Packages/manifest.json`).
  El proyecto se desarrolla y se mide en Linux con Vulkan.
- **Python ≥ 3.12 y [uv](https://docs.astral.sh/uv/)** para el servicio de sensores.
- Solo para regenerar la escena: Python 3.14 con numpy, shapely y Pillow (ver [Herramientas](Herramientas/README.md)).

## Cómo arrancar

1. Arranca el servicio de sensores (escucha en `http://127.0.0.1:8800`; el panel web está en `/panel/`):

   ```bash
   cd ServicioSensores
   uv sync
   uv run gemelo-sensores
   ```

2. Abre el proyecto en Unity, carga `Assets/Scenes/MainScene.unity` y pulsa Play. Si el servicio está en otra
   dirección, se cambia en Ajustes ▸ Gemelo ▸ URL del servicio.
3. Para una build de Linux: **Tools ▸ Gemelo ▸ Build de Linux**. Deja `Build/Linux/GemeloPolitecnica.x86_64` con un
   `LEEME.txt`. La build también necesita el servicio en marcha.

Para que haya gente en las aulas, el reloj del modelo tiene que estar en horario lectivo. Se elige, con el escenario
y la velocidad del tiempo, en el bloque Simulación del menú inicial o del de pausa.

## Controles

| Modo | Controles |
|---|---|
| Explorar el campus | WASD o flechas para andar, ratón para mirar, Mayús para correr, Espacio para saltar, rueda para acercar la cámara, C para sentarse o levantarse, E (o 1–9) para las acciones de la estancia, Esc para la pausa |
| Centro de control | WASD, flechas, botón central o borde de la pantalla para desplazar; rueda para el zoom; Q/E o Alt + arrastre para girar; botón derecho para inclinar; clic para seleccionar; F o doble clic para centrar; Inicio para la vista general; AvPág/RePág para cortar el edificio por plantas; Esc quita la selección o pausa |

La pestaña Ajustes ▸ Controles permite cambiar la sensibilidad, el campo de visión y el avatar (Brian o Megan).

## Rendimiento

La escena va limitada por la GPU. Ajustes ▸ Gráficos tiene los preajustes Bajo, Medio, Alto y Ultra; el de
fábrica es Alto, que en una Radeon 780M integrada gasta un 22–25 % menos de GPU que Ultra sin diferencia visible.
Medidas y herramientas de medida en [Referencias/Rendimiento.md](Referencias/Rendimiento.md).

## Estructura

| Carpeta | Contenido |
|---|---|
| `Assets/_Politecnica/` | Edificios de la escuela: mallas generadas, mobiliario, materiales, prefabs y componentes (`EdificioPolitecnica`, `Local`, `Puerta`). Se generan con [Herramientas/politecnica](Herramientas/politecnica/README.md) |
| `Assets/_Ambientacion/` | Entorno: terreno, suelo, viales, parque, vegetación, equipamiento urbano y ambiente, con sus constructores de editor. Créditos de texturas en [CREDITOS.md](Assets/_Ambientacion/Suelo/Texturas/CREDITOS.md) |
| `Assets/_Gemelo/` | App del gemelo: núcleo, personas, ambiente, control e interfaz ([README](Assets/_Gemelo/README.md); personas en [su README](Assets/_Gemelo/Personas/README.md)) |
| `Assets/Scripts/` | Controlador en primera persona de la escena de trabajo, benchmark y configuración de rendimiento |
| `Herramientas/` | Generadores en Python del entorno y de los personajes ([README](Herramientas/README.md)) |
| `Referencias/` | Material de partida: MDT, ortofotos, OSM, capturas y el dosier de la Politécnica ([README](Referencias/README.md)) |
| `ServicioSensores/` | Servicio de sensores, panel web, contrato de la API y cliente de Unity de ejemplo ([README](ServicioSensores/README.md)) |
| `Datos/` | Datos intermedios de los generadores (splatmaps, accesos de los edificios). Las descargas, los rásteres de viales y las salidas de auditoría no se versionan: se regeneran con los scripts |

## Créditos y licencias de terceros

- **Personajes y animaciones** de [Mixamo](https://www.mixamo.com) (Adobe), en `Assets/_Gemelo/Personas/Mixamo/`,
  sujetos a las condiciones de uso de Mixamo.
- **Texturas** CC0 de [Poly Haven](https://polyhaven.com), con el color corregido. Autoría en
  [CREDITOS.md](Assets/_Ambientacion/Suelo/Texturas/CREDITOS.md). Dos fotos CC0 de Wikimedia Commons en
  `Assets/_Politecnica/Texturas/foto_bn_LICENCIA.txt`.
- **Tipografías** Montserrat y Charis SIL, con licencia SIL OFL (textos de la licencia junto a las fuentes, en
  `Assets/_Gemelo/UI/Resources/GemeloUI/Fuentes/` y `ServicioSensores/panel/fuentes/`).
- **Marca de la UEx** (imagotipo y sello, en la interfaz y el panel web) y logotipos de la UEx y de la Escuela en
  los rótulos de fachada: Universidad de Extremadura.
- **Datos geográficos:**
  - MDT, LiDAR y ortofoto PNOA: IGN/CNIG, CC BY 4.0 (scne.es).
  - Catastro: Dirección General del Catastro.
  - OpenStreetMap: ODbL, © colaboradores de OpenStreetMap.
  - Las capturas de Google Maps y Street View de `Referencias/` son solo referencia visual (imágenes © Google,
    Airbus y Maxar).

  El detalle está en [Referencias/README.md](Referencias/README.md) y en
  [Referencias/Politecnica/03_geodatos.md](Referencias/Politecnica/03_geodatos.md).
- **Planos de planta** de 2013 y documentación de los edificios: Universidad de Extremadura (ver el
  [dosier de la Politécnica](Referencias/Politecnica/00_INDICE.md)).
- **Datos de sensores**: [SmartPoliTech](http://smartpolitech.unex.es), Escuela Politécnica de Cáceres.

Licencia del proyecto: pendiente de definir.
