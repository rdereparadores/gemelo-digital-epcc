# Gemelo digital en Unity (`Assets/_Gemelo/`)

Representa en la escena del campus los datos en vivo del servicio de sensores (`ServicioSensores/`, contrato en
`ServicioSensores/docs/CONTRATO_API.md`). La escena no se monta a mano: todo se crea en tiempo de ejecución a
partir de `GemeloApp`, salvo los assets que generan los builders de editor (malla de navegación, materiales…).

Para probarlo hay que arrancar el servicio (`cd ServicioSensores && uv run gemelo-sensores`) y entrar en Play
Mode en `MainScene`. Con `-sinGemelo` o `-benchmark` el gemelo no arranca.

## Módulos

| Carpeta | Qué hace | Depende de |
|---|---|---|
| `Nucleo/` | Cliente REST, reloj del modelo, registro de edificios y locales, estado de la app (modo, pausa, selección, capa, corte), ajustes y escalas de color | `_Politecnica/Runtime` |
| `Personas/` | NPCs: simulación de la multitud 1:1 con la ocupación del servicio, navegación, maniquíes y LOD | Núcleo |
| `Ambiente/` | Sol y cielo según la hora del modelo, nubes y lluvia, luces interiores, ventanas y persianas, indicadores flotantes y emergencias | Núcleo |
| `Control/` | Avatar en tercera persona, cámara orbital, cámara cenital, selección, corte por plantas, capa de color de las estancias e interacciones | Núcleo |
| `UI/` | Menú inicial y de pausa, ajustes, HUD de tercera persona, paneles de la vista cenital, gráficas y avisos (UI Toolkit) | Núcleo |

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
    Arranca en `Menu`.
  - **Pausa:** `Pausado`, `Pausar(bool)` y el evento `PausaCambiada`. En pausa no se toca `Time.timeScale`: el
    gemelo sigue vivo detrás del menú y los controles dejan de leer la entrada.
  - **Selección:** `Seleccion` (edificio y local opcional), `Seleccionar(s)` y el evento `SeleccionCambiada`.
  - **Capa:** `Capa` (enum `Capa`), `CambiarCapa(c)` y el evento `CapaCambiada`.
  - **Corte:** `CortarEdificio(e, indicePlanta)` (−1 = completo), `Corte(e)`, `QuitarCortes()` y el evento
    `CorteCambiado`. Usa `EdificioPolitecnica.MostrarHasta`.
  - **Interacciones:** `OfrecerInteracciones(lista)` y el evento `InteraccionesCambiadas`. Cada interacción lleva
    texto, icono y acción. Control las ofrece y lee las teclas: E ejecuta la primera y 1–9 las demás. La UI solo
    las muestra.
  - **Bajo el cursor:** `Sobre`, `PonerSobre(s)` y el evento `SobreCambiado` (vista cenital). Control lo pone y la
    UI saca el tooltip.
  - **Avatar:** `GemeloApp.FabricaAvatar` (estático, `Func<int, GameObject>`). Lo registra Personas y lo usa
    Control para el jugador. Devuelve un avatar con `Animator` y los parámetros de `ParamAvatar`: `Velocidad`,
    `Sentado`, `Hablando`, `EnSuelo` y `Saltar`.
  - `PersonasEnEscena`: NPCs simulados ahora mismo, para el HUD.
  - **Local actual:** `LocalActual`, `PonerLocalActual(l)` y el evento `LocalActualCambiado` (dónde está el jugador o
    la cámara).
  - `Jugador`: Transform del avatar en tercera persona.
  - **Avisos:** `Avisar(texto, NivelAviso)` y el evento `Aviso`.
- **`ClienteGemelo`** (`GemeloApp.Instancia.Cliente`, o `ClienteGemelo.Instancia`)
  - Sondea `/api/v1/state` cada segundo con ETag.
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
  - `Edificios` → `InfoEdificio`: codigo, idServicio, límites, plantas, locales y puertas exteriores.
  - `InfoPlanta`: índice, nombre, cota, locales y puertas.
  - `InfoLocal`: `Local`, clave, idServicio, centro, área, puertas y `Contiene(p)`.
  - `Local` (componente de la escena) trae el polígono exacto, `cotaSuelo`, `asientos` (sillas, taburetes,
    butacas y bancos del local) y `luces`.
  - Búsquedas: `Local(clave)`, `PorEspacio(spaceId)`, `Edificio(cod|id)`, `LocalEn(punto)` y `EdificioEn(punto)`.
- **`Ajustes`** (`Ajustes.Actual`, `Ajustes.Aplicar()` y el evento `Ajustes.Cambiados`): se guardan en JSON en
  `persistentDataPath`.
- **`Escalas`**: nombre, unidad, rango, valor y color de cada capa, más los colores del confort y de la severidad.
  La paleta es la de la UEx.

## Convenciones

- Código y comentarios en español, como el resto del proyecto. Entrada con el Input System (el proyecto no tiene
  el gestor antiguo), con `InputSystem.actions` (`Assets/InputSystem_Actions.inputactions`) o con
  `Keyboard.current` y `Mouse.current`.
- Nada de cambios a mano en `MainScene`. Lo persistente (NavMesh, materiales, prefabs) lo generan builders de
  editor en `<módulo>/Editor/`.
- Rendimiento: la escena ya va al límite de la GPU (~25 ms en la Radeon 780M; ver `Referencias/Rendimiento.md`).
  Hay que instanciar en vez de crear miles de objetos y no añadir luces con sombra.
