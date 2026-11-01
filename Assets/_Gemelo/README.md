# App del gemelo digital (`Assets/_Gemelo/`)

Muestra en la escena del campus los datos en vivo del servicio de sensores ([`ServicioSensores/`](../../ServicioSensores/README.md);
contrato en [`CONTRATO_API.md`](../../ServicioSensores/docs/CONTRATO_API.md)). La escena no se monta a mano: todo se
crea en tiempo de ejecución a partir de `GemeloApp`, salvo los assets que generan los constructores de editor (malla
de navegación, materiales, prefabs…).

Para probarlo, arranca el servicio (`cd ServicioSensores && uv run gemelo-sensores`) y entra en Play Mode en
`Assets/Scenes/MainScene.unity`. Con `-sinGemelo` o `-benchmark` en la línea de órdenes el gemelo no arranca.

**Build de Linux:** **Tools ▸ Gemelo ▸ Build de Linux** (`Nucleo/Editor/ConstruirGemelo.cs`) deja
`Build/Linux/GemeloPolitecnica.x86_64` con un `LEEME.txt`. Permite HTTP (el servicio va en local por HTTP), la ventana
se puede redimensionar y la app sigue corriendo en segundo plano. El servicio tiene que estar arrancado, igual que en
el editor.

## Módulos

| Carpeta | Qué hace | Depende de |
|---|---|---|
| `Nucleo/` | Cliente REST, reloj del modelo, registro de edificios y locales, estado de la app (modo, pausa, selección, capa, corte), ajustes, opciones gráficas y escalas de color | `_Politecnica/Runtime` |
| `Personas/` | NPC: multitud 1:1 con la ocupación del servicio, navegación, personajes de Mixamo y maniquíes lejanos ([README](Personas/README.md)) | Núcleo |
| `Ambiente/` | Sol y cielo según la hora del modelo, nubes y lluvia, luces interiores, ventanas y persianas, indicadores flotantes y emergencias | Núcleo |
| `Control/` | Cámaras de cada modo, avatar en tercera persona, selección, corte por plantas, capa de color, interacciones y medidores de rendimiento | Núcleo |
| `UI/` | Menú inicial y de pausa, ajustes, HUD de tercera persona, centro de control, gráficas y avisos (UI Toolkit) | Núcleo |

Los módulos **no se referencian entre sí**: solo usan el núcleo. Si un módulo necesita algo de otro, se añade al
núcleo como evento o propiedad de `GemeloApp`.

## Núcleo (`namespace Gemelo`)

- **`GemeloApp`** (singleton en el objeto «Gemelo», que no se destruye al cambiar de escena)
  - Arranque: `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` si la escena tiene edificios. Aparca el
    `FirstPersonController` de la escena.
  - `static event Arrancado(GemeloApp)`: cada módulo se suscribe desde un
    `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` y en él añade sus componentes, sobre `app.gameObject` o
    sobre objetos hijos suyos.
  - **Modo:** `Modo` (`Menu`, `TerceraPersona`, `Cenital`), `CambiarModo(m)` y el evento `ModoCambiado`.
    Arranca en `Menu`. En la interfaz, `TerceraPersona` es «Explorar el campus» y `Cenital`, «Centro de control».
  - **Pausa:** `Pausado`, `Pausar(bool)` y el evento `PausaCambiada`. En pausa no se toca `Time.timeScale`: el
    gemelo sigue vivo detrás del menú y los controles dejan de leer la entrada.
  - **Selección:** `Seleccion` (edificio y local opcional), `Seleccionar(s)` y el evento `SeleccionCambiada`.
  - **Capa:** `Capa` (enum `Capa`), `CambiarCapa(c)` y el evento `CapaCambiada`.
  - **Corte:** `CortarEdificio(e, indicePlanta)` (−1 = completo), `Corte(e)`, `QuitarCortes()` y el evento
    `CorteCambiado`. Usa `EdificioPolitecnica.MostrarHasta`.
  - **Interacciones:** `OfrecerInteracciones(lista)` y el evento `InteraccionesCambiadas`. Cada interacción lleva
    texto, icono y acción. Control las ofrece y lee las teclas: E ejecuta la primera y cada cifra 1–9 la de su
    número (1 = la primera, como E). La UI solo las muestra.
  - **Bajo el cursor:** `Sobre`, `PonerSobre(s)` y el evento `SobreCambiado` (vista cenital). Control lo pone y la
    UI saca el tooltip.
  - **Avatares:** `FabricaAvatar` (estático, `Func<int, GameObject>`) da el cuerpo de un NPC y `FabricaJugador`, el
    del jugador según `Ajustes.avatarJugador` (0 = Brian, 1 = Megan). Los registra Personas; Control usa
    `FabricaJugador` si existe y, si no, `FabricaAvatar(7)`. Devuelven un avatar con `Animator` y los parámetros de
    `ParamAvatar`: `Velocidad`, `Sentado`, `Hablando`, `EnSuelo`, `Saltar` y `Escribiendo` (en el estado
    `SentadoMovil`, 0 = teclea en un ordenador y 1 = escribe en la mesa; solo lo usan los NPC). El controlador de
    Personas trae además `Movil` (bool; el móvil va en la mano derecha, en un hijo «Movil» apagado). Con los
    maniquíes, la escala del objeto es la estatura (1 ≈ 1,81 m); los personajes de Mixamo traen su altura.
  - `Jugador`: Transform del avatar en tercera persona. `PersonasEnEscena`: NPC simulados ahora mismo, para el HUD.
  - **Local actual:** `LocalActual`, `PonerLocalActual(l)` y el evento `LocalActualCambiado` (dónde está el jugador o
    la cámara).
  - **`PunteroSobreUI`** (estático, `Func<Vector2, bool>`): la UI lo rellena y dice si un punto de la pantalla (en
    píxeles, como `Mouse.current.position`, con el origen abajo a la izquierda) cae sobre un panel. Control no hace
    clic, arrastre, zoom ni pan por el borde ahí.
  - **`EscapeConsumido`** (estático, `int`): la UI pone `Time.frameCount` cuando usa Esc para algo suyo (cerrar un
    desplegable o un popover). Control lee Esc en `LateUpdate` y no hace nada con el Esc de ese frame.
  - **Avisos:** `Avisar(texto, NivelAviso)` y el evento `Aviso`.
- **`ClienteGemelo`** (`GemeloApp.Instancia.Cliente`, o `ClienteGemelo.Instancia`)
  - Sondea `/api/v1/state` cada segundo con ETag. La URL sale de `Ajustes.urlServicio`.
  - Consultas: `Estancia(clave)`, `Espacio(spaceId)`, `Edificio(codigo|id)`, `Estado` y los eventos
    `EstadoActualizado` y `ConexionCambiada`.
  - Órdenes: `CambiarReloj("live" | DateTime local)`, `CambiarEscalaTiempo`, `CambiarEscenario`,
    `PausarSimulacion`, `CrearEvento(tipo, objetivo, duración, paramsJson)`, `TerminarEvento`, `Escenarios`,
    `Eventos` y `Get(ruta, callback)` para cualquier GET.
- **`HoraCampus`** (estática)
  - `Utc` y `Local`: hora del modelo, interpolada entre sondeos.
  - `Escala`: segundos de modelo por segundo real.
  - `Escenario` y `EnDirecto`.
  - `Sol(utc, out elevación, out acimut)` para Cáceres.
- **`RegistroCampus`** (estático)
  - `Edificios` → `InfoEdificio`: código, idServicio, límites, plantas, locales y puertas exteriores.
  - `InfoPlanta`: índice, nombre, cota, locales y puertas.
  - `InfoLocal`: `Local`, clave, idServicio, centro, área, puertas y `Contiene(p)`.
  - `Local` (componente de la escena) trae el polígono exacto, `cotaSuelo`, `asientos` (sillas, taburetes,
    butacas y bancos del local) y `luces`.
  - Búsquedas: `Local(clave)`, `PorEspacio(spaceId)`, `Edificio(cod|id)`, `LocalEn(punto)` y `EdificioEn(punto)`.
- **`AliasEstancias`**: claves del servicio que no casan tal cual con un local (sótanos `Pm1` → `P-1`, estancias
  fusionadas con la vecina, divisiones como C-10 → 23A/23B/23C). La tabla `Nucleo/Resources/AliasEstancias.json` la
  escribe `Herramientas/politecnica/alias_gemelo.py`. `ClienteGemelo.Estancia(clave)` ya devuelve el estado por local
  del modelo: en las fusiones suma ocupación, ventanas y radiadores; en las divisiones reparte la ocupación por
  superficie. Solo se quedan sin local los tres patios descubiertos de 1905.
- **`Ajustes`** (`Ajustes.Actual`, `Ajustes.Aplicar()` y el evento `Ajustes.Cambiados`): se guardan en JSON en
  `persistentDataPath` y se aplican en caliente (también el avatar del jugador). `Aplicar(false)` avisa sin guardar
  (pruebas). `Ajustes.Restablecer()` vuelve a los valores de fábrica.
- **`Graficos`**: opciones de Ajustes ▸ Gráficos. Las aplica al asset de URP en uso, a la cámara principal, a
  `QualitySettings.lodBias` y al Terrain; en el editor restaura el asset de URP al salir de Play Mode. Preajustes
  (`Graficos.Poner`; `PreajusteDe` dice cuál coincide con los ajustes actuales, o −1 si se han tocado a mano):

  | Preajuste | Antialiasing | Sombras del sol | Oclusión (SSAO) | Resolución 3D | Detalle (`lodBias`) | Hierba |
  |---|---|---|---|---|---|---|
  | Bajo | FXAA | 2 cascadas, 60 m, 2048 px | No | 80 % (FSR) | 1 | 40 m |
  | Medio | SMAA | 3 cascadas, 100 m, 2048 px | Media resolución | 100 % | 1,5 | 70 m |
  | **Alto** (de fábrica) | MSAA 2× | 4 cascadas, 150 m, 2048 px | Media resolución | 100 % | 2 | 90 m |
  | Ultra | MSAA 4× | 4 cascadas, 150 m, 4096 px | Completa | 100 % | 2 | 90 m |

  Ultra es la configuración que tenía la escena antes de existir los preajustes; Alto cuesta un 22–25 % menos de GPU
  sin diferencia visible (ver [Rendimiento](../../Referencias/Rendimiento.md#app-del-gemelo)).
- **`Escalas`**: nombre, unidad, rango, valor y color de cada capa, más los colores del confort y de la severidad.
  La paleta es la de la UEx.

## Control (`Control/`)

`ControlGemelo` (en el objeto Gemelo) es el dueño de la cámara: suelta la `Main Camera` de la escena del
`FirstPersonController` aparcado (conserva su posprocesado) y, según `GemeloApp.Modo`, activa un controlador que
le propone la pose de cada frame; al cambiar de modo hay un fundido en arco de 1,6 s. También lleva el cursor
(bloqueado solo en tercera persona jugando) y Esc: en tercera persona pausa; en la vista cenital quita la
selección y, si no hay, pausa. Esc se lee en `LateUpdate` y se ignora si la UI lo marcó en
`GemeloApp.EscapeConsumido` ese frame. **En pausa Control no lee Esc** (la UI lo usa para continuar).

- **Menú** (`CamaraMenu`): sobrevuelo lento por una Catmull-Rom cerrada que pasa por los siete edificios.
- **Tercera persona** (`JugadorTerceraPersona`): avatar de `FabricaJugador` (se cambia en caliente al recibir
  `Ajustes.Cambiados`), o de `FabricaAvatar`, o un maniquí de cápsulas, con CharacterController de 0,22 m de radio,
  0,02 de piel y 1,8 de alto, en la capa 2 (Ignore Raycast). WASD o flechas, Mayús corre, Espacio salta, ratón para
  mirar (sensibilidad, invertir y FOV de `Ajustes`), rueda para la distancia (0,9–8 m). La cámara va por detrás del
  hombro y se acerca con un spherecast si algo se interpone; pegada a la cabeza, el cuerpo solo da sombra. Aparece
  en la plaza de la cafetería del Común y, al volver al modo, sigue donde estaba. **C** sienta al jugador en el
  asiento más cercano del local (a menos de 1,3 m, mirando como los NPC; altura del asiento por raycast a la silla)
  y cualquier tecla de marcha, C o Espacio lo levanta. Pone `GemeloApp.Jugador` y `LocalActual`. Para pruebas y
  recorridos: `Ruta` (lista de puntos que recorre solo), `MarchaForzada`, `CorrerForzado`, `SaltoForzado`,
  `Sentarse()`/`Levantarse()`, `Rumbo`, `Inclinacion`, `Distancia` y `Colocar(punto, mirarA)`.
- **Interacciones** (`InteraccionesJugador`): según el local y su estado ofrece clase (empezar con el 85 % de la
  capacidad o terminar, en aulas, laboratorios y salas de reuniones), ventanas (abrir o cerrar, 30 min),
  calefacción (encender o apagar, 1 h) y, en el edificio, alarma de incendio (15 min) y apagón (10 min), o
  desactivarlos si están en marcha. Antes de crear un evento termina los activos del mismo tipo o del contrario
  sobre la misma estancia.
- **Vista cenital** (`CamaraCenital`): pan con WASD/flechas, botón central o borde de la pantalla; rueda hacia el
  cursor (15–600 m); giro con Q/E o Alt + arrastre; inclinación (30°–89°) y giro con el botón derecho; Mayús
  acelera; Inicio vuelve a la vista general; F o doble clic centra lo seleccionado. `Centrar(seleccion)` y
  `EntrarGeneral()` son públicos (para botones de la UI).
- **Selección** (`SeleccionCenital`): clic en un edificio lo selecciona; si está cortado, el clic en su planta
  visible selecciona el local. Clic en vacío quita la selección. AvPág baja el corte del edificio seleccionado y
  RePág lo sube (por encima de la última planta vuelve a ponerse la cubierta). `SeleccionCenital.Elegir(punto)`
  da lo que hay bajo un punto de la pantalla.
- **Capa de color y resaltado** (`CapaColor`, sombreador `Resources/GemeloControl/Superposicion.shader`): en la
  planta visible de los edificios cortados, un plano por local a 1 m del suelo (por encima de mesas y sillas,
  por debajo de las cabezas), con borde opaco y costados que se desvanecen; gris si no hay dato. En los demás
  edificios, un aura en su contorno (anillo en el suelo de ancho constante en pantalla y banda vertical) con el
  valor agregado del edificio: ocupación total entre capacidad total, el confort más extendido o la media por
  superficie. El edificio seleccionado lleva el aura en amarillo (sin capa) y pulsa; el de bajo el cursor, en
  blanco. Los colores pasan de uno a otro en ~1 s. Las mallas (`GeometriaControl`: ear clipping de
  `Local.poligono`, huella de cada edificio rasterizando sus locales) se calculan una vez. La superposición no
  pinta donde el stencil trae el bit 3 (ver [Convenciones](#convenciones)): así no tiñe a la gente de pie de cintura
  para abajo.

## Ambiente (`Ambiente/`)

`AmbienteGemelo` (hijo «Ambiente» del objeto Gemelo) guarda la hora y el tiempo efectivos y los eventos activos de la
simulación (los sondea cada 2 s en `/api/v1/simulation/events`: incendio, apagón y fuga por edificio o estancia), y
crea el resto:

- `CieloYSol`: sol o luna en la `Directional Light`, cielo `Gemelo/Cielo` (el HDR de `Cielo.mat` con sol propio,
  ocaso, nubes, estrellas y luna), Trilight y niebla. Con el sol alto y despejado deja lo calibrado de
  `AmbienteBuilder`; con `Ajustes.sincronizarSol = false` lo devuelve todo como estaba.
- `Lluvia`: trazos en la GPU alrededor de la cámara. No llueve bajo techo: la primera vez que llueve hornea un mapa
  de altura del techo visto desde arriba (0,2 m por texel, ~135 ms, con `GemeloAlturaTecho.shader`) y el sombreador
  descarta las gotas que caen por debajo. `Cubierto` y `Dentro` dicen si la cámara está a cubierto, para el sonido.
- `Farolas`: destellos en las luminarias y luces en las más cercanas.
- `LucesInteriores`: intensidad de `Local.luces` (`EdificioPolitecnica` sigue decidiendo `enabled` por distancia),
  ventanas y muros cortina iluminados de noche, apagón y señales verdes de salida. Los locales grandes traen varias
  luces (una cada ~20 m², con la intensidad según la superficie que alumbran; las pone el generador): solo quedan
  activas las de los locales encendidos más cercanos a la cámara, hasta `LucesInteriores.MaxLuces` (64; las otras
  plantas cuentan como más lejos), y de día bajan con la luz natural (`FactorDia`), porque la luz ambiente ya alumbra
  los interiores.
- `Ventanas`: hojas y persianas de las estancias con sensor, a partir de `Resources/Ambiente/Ventanas/<código>.json`
  (lo escribe el generador de edificios; ver [Ventanas del gemelo](../../Herramientas/politecnica/README.md#ventanas-del-gemelo)).
  Los paños de muro cortina van en el mismo JSON con `cortina: true` y solo sirven para iluminarlos de noche.
- `Indicadores`: insignias de las alertas sobre cada estancia.
- `Emergencias`: sirena, estroboscópicos, balizas y charcos con el sombreador `Gemelo/Agua` (suelo visto a través
  del agua, reflejo en espacio de pantalla de lo que hay alrededor o, si no, del techo con la luz del local, brillos
  de las luminarias y anillos del goteo; sin sondas de reflexión). El agua necesita la textura opaca de la cámara,
  que cuesta una copia de la imagen por frame: el asset de URP no la pide y la cámara solo la pide mientras hay
  algún charco.
- `AudioAmbiente`: viento, pájaros, lluvia y rumor de gente, con sonidos sintetizados en `SintesisAudio`.

## UI (`UI/`)

UI Toolkit montada por código (sin UXML) en un solo `UIDocument` hijo del objeto Gemelo (`GemeloUI`). El
`PanelSettings` se crea en ejecución con el tema `Resources/GemeloUI/Tema.tss` (el de serie de Unity) y la hoja
`Resources/GemeloUI/Gemelo.uss`. La estética es la del panel web del servicio: amarillo UEx `#F4B223` para la acción
principal y la selección, con texto negro encima; avisos en naranja; verde = dato real y violeta = simulado; paneles
translúcidos con sombra suave; Montserrat y Charis SIL (TTF con licencia OFL en `Resources/GemeloUI/Fuentes/`), y el
imagotipo y el sello de la UEx en negativo (`Resources/GemeloUI/Marca/`). Escala con la resolución (referencia
1920 × 1080 dividida por `Ajustes.escalaUI`).

- **Capas** (según `GemeloApp.Modo` y `Pausado`): `MenuInicial`, `HudTercera` (con `FichaLocal` y `Minimapa`),
  `CentroControl` (con `FichaSeleccion`, `PanelEventos` y la `Grafica`), `MenuPausa`, `PanelAjustes`, avisos
  (`GemeloApp.Aviso`) y el tooltip de `GemeloApp.Sobre`. `PanelSimulacion` es el bloque de reloj, escenario y
  velocidad que comparten el menú inicial y el de pausa (Unity manda en el reloj del servicio).
- **Entrada**: la UI solo atiende Esc con un menú abierto (cierra los ajustes o el desplegable; en pausa reanuda
  al final del fotograma, salvo en el mismo frame en que se pausó). Rellena `GemeloApp.PunteroSobreUI` con
  `panel.Pick` (los contenedores de maquetación no recogen el puntero). Crea un `EventSystem` con
  `InputSystemUIInputModule` si la escena no tiene ninguno. Con un menú delante deja el cursor libre y visible.
- **Ajustes globales** que aplica la UI al recibir `Ajustes.Cambiados`: pantalla completa (solo fuera del
  editor), FPS objetivo y sincronía vertical, `AudioListener.volume` y la escala de la interfaz. Las opciones
  gráficas las aplica `Graficos`.
- **Gráficas**: `Historial.Pedir` lee `GET /api/v1/{spaces|buildings}/{id}/history` (series agregadas como el
  estado; ver el contrato) y `Grafica` las dibuja con Painter2D (área, rejilla, ejes en hora local, banda de
  confort o umbral de CO₂ y tooltip).
- **Iconos**: `Icono` dibuja trayectos SVG de 24 × 24 con Painter2D (catálogo en `Icono.Trazos`).

## Pruebas y medida

Herramientas solo para pruebas; no cambian nada si no se llaman.

- **Capturas**: `Gemelo.CapturaPrueba.Tomar(pos, mira, ruta)` guarda un PNG en Play Mode con el posprocesado de la
  cámara principal; `Gemelo.CapturaUI.Tomar(ruta, 1920, 1080)` lo hace con la UI encima (cámara y panel en dos
  RenderTexture, compuestas).
- **Modo de prueba del ambiente** (no toca el servicio): `Gemelo.AmbienteGemelo.Prueba.hora`, `nubes`, `lluvia`,
  `viento`, `persianas[clave]`, `luces` (claves de locales encendidos a la fuerza), `destellos` y `camara`;
  `Prueba.Quitar()` lo deshace.
- **`MedidorRendimiento`** (`Control/`), desde código en Play Mode:
  - `Medir(s, nombre)`: tiempos de frame, GPU e hilos y contadores de dibujo.
  - `AB(nombre, on => …, tandas, s)`: alterna tandas con algo encendido y apagado (A B A B) y da la media de cada
    estado y la mediana de las diferencias por parejas. Conviene repetirlo con una prueba A/A para ver el ruido.
  - `Marcadores(s)`: desglosa el frame por marcadores del profiler (editor o build de desarrollo).

  El resultado queda en `MedidorRendimiento.Resultado`. Para el coste de la multitud está `MedidorPersonas` (ver
  [Personas](Personas/README.md#cómo-se-prueba)).
- **`RecorridoRendimiento`** (`Control/`): con `-recorrido <carpeta>` la build recorre vistas fijas de los tres modos y
  deja `recorrido.json` y una captura por vista. Opciones y ejemplo en
  [Rendimiento](../../Referencias/Rendimiento.md#la-app-recorridorendimiento).

## Convenciones

- Código y comentarios en español, como el resto del proyecto. Entrada con el Input System (el proyecto no tiene
  el gestor antiguo), con `InputSystem.actions` (`Assets/InputSystem_Actions.inputactions`) o con
  `Keyboard.current` y `Mouse.current`.
- Nada de cambios a mano en `MainScene`. Lo persistente (NavMesh, materiales, prefabs) lo generan constructores de
  editor en `<módulo>/Editor/`.
- **Rendimiento**: la escena va limitada por la GPU (ver [Rendimiento](../../Referencias/Rendimiento.md)). Hay que
  instanciar en vez de crear miles de objetos y no añadir luces con sombra.
- **Stencil**: el bit 3 (valor 8) significa «aquí se ve una persona». Lo escriben los sombreadores de Personas
  (`MultitudInstanciada` y el material `MarcaPersonas` que llevan los avatares en la vista cenital con capa) y lo
  respeta la superposición de Control (`Comp NotEqual`, propiedad `_StencilComp`). Los bits 4–7 son de URP.
- **Puertas**: las hojas de puerta (`Assets/_Politecnica/Runtime/Puerta.cs`) se abren solas. Personas registra
  `Puerta.Presencia`, una consulta «¿va alguien a pasar por este hueco?»: cuentan los NPC de pie y el jugador que
  estén en el rectángulo de paso, delante o detrás de la puerta cerrada y poco más ancho que ella. Así no se abre
  para quien va por el pasillo pegado al muro, para quien está al otro lado de un tabique ni para los sentados. Con
  la consulta registrada, la cámara ya no abre puertas.
