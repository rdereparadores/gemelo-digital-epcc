# Personas (`Assets/_Gemelo/Personas/`)

NPCs del gemelo: tantas personas en cada estancia como dice el servicio (1:1 con `occupancy`), que se mueven por el
campus cuando los datos cambian, se sientan, charlan, miran el móvil y evacúan los edificios con alarma de incendio.
Solo usa el núcleo (`Assets/_Gemelo/Nucleo/`) y `Assets/_Politecnica/Runtime/`.

## Cómo se prueba

1. Con el servicio en marcha, Play Mode en `MainScene`. El módulo se engancha solo a `GemeloApp.Arrancado`.
2. `ModuloPersonas.Instancia` expone la simulación (`Multitud`), el dibujo (`Dibujo`), los tiempos (`MsSimulacion`,
   `MsDibujo`) y `Visible` para ocultar la multitud sin pararla. `GemeloApp.Instancia.PersonasEnEscena` lleva la cuenta.
3. Para forzar situaciones, eventos acotados en el servicio (y borrarlos después):
   - cambio de clase: `POST /api/v1/simulation/events {"type":"class_end","target":"SCO-P00-AUL007"}` (o `class_start`);
   - evacuación: `{"type":"fire_alarm","target":"INF"}`.
4. Medir el coste: añadir `MedidorPersonas` al objeto Gemelo en Play Mode; alterna tandas con y sin multitud (A B A B) y
   deja el resultado en `MedidorPersonas.Resultado`.

## Piezas

| Archivo | Qué hace |
|---|---|
| `ModuloPersonas.cs` | Registro en `GemeloApp`, bucle (reconciliar, rutas, paso, dibujo), `GemeloApp.FabricaAvatar` y `Puerta.Presencia` |
| `Multitud.cs` | Simulación: reconciliación con el servicio, viajes, teletransporte con la hora acelerada, corrillos, gestos, evacuación |
| `Estancias.cs` | Estancias (acceso en la malla, plazas, sitios de pie), edificios (alarma, punto de reunión) y llegadas al campus |
| `Persona.cs` | Estado de cada persona y su aspecto (`Apariencia`: cuerpo, estatura, ropa y paso a partir de una semilla) |
| `Navegacion.cs` | Carga la malla (`NavMesh.AddNavMeshData`) y sus enlaces, calcula y guarda en caché las rutas |
| `Rejilla.cs` | Rejilla espacial (2 m) para esquivarse y para las puertas |
| `DibujoMultitud.cs` | LOD: avatares con Animator cerca, maniquíes instanciados en la GPU lejos, nada más allá |
| `FabricaAvatares.cs` | Crea y tiñe avatares (lo usa también el jugador) |
| `Shaders/MultitudInstanciada.shader` | Maniquí rígido animado en el vértice (andar, correr, sentado, hablar, móvil) |
| `Editor/ConstructorNavegacion.cs` | Tools ▸ Gemelo ▸ Malla de navegación → `Navegacion/Campus.asset` |
| `Editor/ConstructorManiquies.cs` | Tools ▸ Gemelo ▸ Maniquíes → `Maniquies/` (mallas, avatares, clips, controlador) |
| `Resources/RecursosPersonas.asset` | Lo que se carga en ejecución: malla y enlaces, prefabs de avatar, controlador, mallas lejanas |

## Malla de navegación

`Tools ▸ Gemelo ▸ Malla de navegación` recoge los colliders de la zona de la Politécnica (300 × 290 m: los siete
edificios con todas sus plantas y escaleras, pasarelas, aparcamientos, parada y accesos) y construye con
`NavMeshBuilder.BuildNavMeshData` (agente de 0,18 m de radio y 1,7 m de alto, escalón 0,36 m, vóxel 6 cm, con
*height mesh* para que la altura de los pies sea exacta en las escaleras). Con 0,2 m y 8 cm las puertas de 0,65-0,68 m
(despachos de Teleco) quedaban sin paso; con 1,8 m, los cuartos bajo las escaleras (puerta rebajada a 1,79 m).
`ConstructorNavegacion.ConstruirDatos(zona, …)` construye solo una zona, sin guardar, para probar cambios.

- Se camina sobre suelos, peldaños, aceras, calzadas y el terreno. Muros, falsos techos, carpinterías, barandillas y
  **todo el mobiliario** se rasterizan como «no caminable»: estorban, pero encima no sale malla (ni islas sobre mesas).
- Las hojas de puerta (`Puerta`) y los triggers de los locales no cuentan.
- Áreas: 0 suelo/acera (coste 1), 3 terreno (1,8), 4 calzada (2,5): se prefiere la acera y se cruza por donde toca.
- **Escaleras**: todas las de los siete edificios se andan por la malla (el generador corta los muros que las tapiaban
  y cose las mesetas a sus tramos; ver `Herramientas/politecnica/README.md`). No hay enlaces «ascensor»
  (`CoserAscensor`, desactivado) ni costuras a través de tabiques (`CoserForzado`, desactivado: unían azoteas sin
  puerta con los locales de al lado atravesando la fachada).
- **Costuras**: el voxelizado deja algunos trozos sueltos separados por centímetros. El builder busca los trozos que no
  conectan con el principal y los cose con enlaces cortos (≤ 0,9 m, casi a la misma altura y sin nada en medio) que se
  guardan en `RecursosPersonas` y se añaden en ejecución con `NavMesh.AddLink`.
- **Puntos de paso**: el builder guarda en `RecursosPersonas.escaleras` el arranque y la llegada de cada tramo de
  escalera (huellas de peldaño agrupadas, en la malla). El buscador de Unity tiene un tope de nodos y no completa las
  rutas largas por interiores llenos de muebles (del aparcamiento a la P2 del Común): entonces `Navegacion.Calcular` la
  parte por el punto de paso (escalera o puerta exterior del edificio de un extremo) que menos la alarga, hasta tres
  veces; solo si así tampoco, sigue desde donde se quedó (lo que antes daba los rodeos).
- En ejecución las rutas se densifican (altura de la malla cada 0,6 m en escaleras y cada 2,5 m fuera) y las de nodo a
  nodo (acceso de estancia, llegada, punto de reunión) se guardan en caché.

## Simulación

- **Reconciliación** (con cada estado nuevo del servicio): por estancia con estado (también pasillos, halls y
  cafetería; los `*-EDIFICIO` sin `unity_room` no se representan) se compara el dato con las personas asignadas, con
  una histéresis de ±0,6 para que los decimales no hagan entrar y salir a nadie. Lo que sobra queda libre; los libres
  van a las estancias con hueco (primero del mismo edificio) y si no queda hueco se van del campus; lo que aún falta
  entra por un **punto de llegada**: plazas del aparcamiento norte y del este (marcas viales de `Parking_Norte`), la
  marquesina de la parada y los accesos peatonales (aceras que cruzan el borde de la zona). Tope: `Ajustes.maxPersonas`.
- **Sitios**: en aulas, despachos, laboratorios, cafetería… se sientan en `Local.asientos` (bancos y sofás dan varias
  plazas). Orientación mirando las mallas: todas las sillas con respaldo lo tienen en −Z local, así que se mira a +Z;
  taburetes y bancos corridos no tienen respaldo y se mira hacia la mesa más cercana. Si no hay asiento libre, de pie.
  En halls, pasillos y vestíbulos se quedan de pie, pasean de un sitio a otro o forman corrillos de hasta 4.
- **Gestos**: hablar (más en la cafetería y en los corrillos), mirar el móvil o nada, con cambios cada pocos segundos.
- **Saltos de reloj**: si entre dos reconciliaciones la hora del modelo avanza más de un cuarto de hora de lo que
  tocaría por la escala (alguien cambia la hora en el menú, el servicio rehace el reloj o cambia mucho la escala), todo
  se coloca directamente, como al arrancar: los que andan llegan, los nuevos aparecen en su sitio y los que sobran se
  van (`Multitud.SaltosReloj` los cuenta).
- **Hora acelerada**: un viaje que a pie tardaría más de un cuarto de hora del modelo se resuelve teletransportando;
  por encima de 300× todo se coloca directamente. Con más de 20× se anda un 60 % más deprisa.
- **Evacuación**: un edificio está en alarma si su estado trae la alerta `fire_alarm` o alguna estancia tiene
  `alarm_active` > 0 (así lo publica el motor con un evento `fire_alarm`, con el objetivo en el edificio o el campus).
  Su gente se levanta (con un tiempo de reacción de 0,3–3 s) y corre (2,4–3,5 m/s) por la malla —sale por la puerta
  exterior que le cae más a mano— hasta un punto de reunión fuera (a ~16 m del edificio, lejos de las calzadas), y espera
  allí charlando o con el móvil. No entra nadie nuevo. Al acabar la alarma, vuelven a sus sitios según los datos o se van.
- **Puertas**: `Puerta.Presencia` (estático en `_Politecnica/Runtime/Puerta.cs`) lo registra este módulo con una
  consulta a la rejilla: las puertas se abren para los NPCs y para `GemeloApp.Jugador`, además de para la cámara.

## Dibujo

- Hasta `Ajustes.maxAnimadas` personas (con un tope interno de 40, `DibujoMultitud.TopeAnimadas`) a menos de 40 m y
  en pantalla llevan un avatar con `Animator` humanoide (reserva reutilizada por variante de cuerpo; una sola submalla
  teñida con una paleta 8×1 propia en un material propio de cada avatar, no con `MaterialPropertyBlock`, para que el
  SRP Batcher los agrupe). Solo proyectan sombra los que están a menos de 14 m.
- El resto, hasta `Ajustes.distanciaPersonas`, son maniquíes rígidos dibujados con `Graphics.RenderMeshIndirect` (un
  dibujo por variante, con sombra a menos de 35 m) y animados en el vértice por `MultitudInstanciada`.
- Se ocultan los que quedan por encima del corte de su edificio (`GemeloApp.CortarEdificio`) y, vistos desde fuera y
  a más de 70 m, los que están dentro de un edificio.
- **Tras los muros**: con la cámara dentro de un local cerrado (aula, despacho, laboratorio…; no pasillos, vestíbulos,
  halls, escaleras, cafetería ni patios, y no a menos de 2,5 m de una de sus puertas), los que están bajo techo en otro
  local solo se dibujan si están en la misma planta y a menos de 10 m (25 m en pasillos y vestíbulos, que se ven por
  la puerta abierta); los de
  fuera, sí (se ven por las ventanas). `DibujoMultitud.OcultarTrasMuros = false` lo desactiva (pruebas).
- **Sombras**: quien está quieto bajo techo (en una estancia que no es patio ni terraza) no da sombra del sol: el techo
  ya la tapa y las luces interiores no tienen sombras. `DibujoMultitud.SombrasBajoTecho = true` lo deshace (pruebas).
- **Stencil**: en la vista cenital con una capa de color, los avatares llevan un material más
  (`Resources/GemeloPersonas/MarcaPersonas.shader`) que marca el bit 3 (valor 8) del stencil donde se ven, y
  `MultitudInstanciada` lo marca siempre en su pasada de color; la capa de color de Control no pinta ahí, así que no
  tiñe a la gente (con personajes de varias submallas solo se marcaría la última).

## Maniquíes, clips y cómo cambiarlos por personajes de Mixamo

`Tools ▸ Gemelo ▸ Maniquíes` genera cuatro cuerpos (hombre y mujer, con y sin mochila) con esqueleto humanoide de
nombres estándar (`Hips`, `Spine`, `Chest`, `Neck`, `Head`, `LeftShoulder`, `LeftUpperArm`…), su `Avatar`, nueve clips
humanoides (`Reposo`, `Andar`, `Correr`, `Sentado`, `SentadoHablando`, `SentadoMovil`, `HablarDePie`, `MovilDePie`,
`Aire`) y `Persona.controller` con los parámetros de `ParamAvatar` (`Velocidad`, `Sentado`, `Hablando`, `EnSuelo`,
`Saltar`) más `Movil` (bool). Las poses se escriben como giros de huesos y se convierten a músculos, así que los clips
valen para cualquier humanoide.

Para usar personajes de Mixamo (FBX):

1. Importar cada personaje con *Rig ▸ Animation Type: Humanoid* (*Avatar Definition: Create From This Model*) y hacer
   un prefab con el modelo (el `Animator` puede quedarse sin controlador).
2. En `Resources/RecursosPersonas.asset`, poner esos prefabs en **Avatares** (el índice es la variante: 0 hombre,
   1 hombre con mochila, 2 mujer, 3 mujer con mochila; si hay otra cantidad, se reparten por módulo) y desmarcar
   **Colorear Avatares** (los de Mixamo traen su textura).
3. Para las animaciones, importar los clips de Mixamo también como *Humanoid* (*In Place*, *Loop Time*) y crear un
   `AnimatorOverrideController` sobre `Maniquies/Persona.controller` que sustituya cada clip (Reposo → Idle, Andar →
   Walking, Correr → Running, Sentado → Sitting Idle, SentadoHablando → Sitting Talking, SentadoMovil → Texting While
   Sitting, HablarDePie → Talking, MovilDePie → Texting, Aire → Falling Idle). Ponerlo en **Controlador**.
4. Nada más: el código solo usa los parámetros del controlador y `HumanBodyBones.RightHand` para el móvil (si el
   personaje no lo trae, se le pone uno). El jugador (módulo Control) recibe el mismo cuerpo por `GemeloApp.FabricaAvatar`.

Los maniquíes lejanos siguen siendo los del shader (la silueta y los colores sirven igual a distancia). Si se quisiera
otro cuerpo lejos, basta con cambiar **Mallas Lejanas** por mallas con la misma convención de `uv2`–`uv4` (pieza y
región, hombro/cadera/cuello y codo/rodilla) que genera el builder.

## Rendimiento

Medido en Play Mode en el editor (Radeon 780M, 01-11-2026) con `MedidorPersonas` (tandas de 3 s con y sin multitud,
intercaladas), con 1.190–1.570 personas simuladas:

| Vista | Animadas / instanciadas visibles | GPU | Hilo principal | Simulación | Dibujo (CPU) |
|---|---|---|---|---|---|
| Aérea del campus | 0 / 9 | ±0,4 ms (ruido) | +0,03 ms | 0,61–0,68 ms | 0,31 ms |
| Pasillo de P1 del Común | 40 / 362 | +2,6 ms | +1,2 ms | 0,40–0,79 ms | 0,39 ms |
| Aula de 112 alumnos | 40 / 434 | +2,2 ms | +1,5 ms | 0,59–0,64 ms | 0,43 ms |
| Pasillo, solo instanciadas | 0 / 471 | +1,1 ms | +1,3 ms | 0,56–0,68 ms | 0,39 ms |

- Cada avatar animado cuesta ~0,05 ms de GPU y ~0,07 ms de hilo principal: con 120 (el valor de fábrica de
  `Ajustes.maxAnimadas`) se iban a +7 ms de GPU y +9 ms de CPU, de ahí el tope de 40.
- **Pulido del 01-11-2026** (tercera persona en el aula NORBA 1903_P1_24, con `Gemelo.MedidorRendimiento.AB`, tandas
  de 2 s intercaladas y mediana por parejas; ruido de una prueba A/A en ese momento: ±1,5 ms). Antes se dibujaban 36
  avatares animados y 177 maniquíes, casi todos de las aulas, plantas y edificios de al lado; ahora, 7 y 6 (los del aula
  y el pasillo). Al atardecer (con sombras del sol): GPU −2,8 ms (mediana; −4,3 ms de media), hilo principal −0,9 ms,
  SetPass de 345 a 222, 0,5 M triángulos menos. De noche (sin sombras del sol; ruido ±0,5 ms): GPU −1,4 ms, hilo
  principal −0,8 ms, `Time.smoothDeltaTime` −0,8 ms, SetPass de 245 a 176. En la calle y en la vista cenital, igual
  (dentro del ruido).
- La simulación (reconciliar + rutas + paso) se queda en 0,4–0,9 ms. Las rutas se calculan con un presupuesto de
  0,6 ms por fotograma; en el arranque de una evacuación grande hay picos de 1–5 ms durante unos segundos mientras se
  calculan las rutas de cada estancia al punto de reunión (luego quedan en caché).

## Limitaciones conocidas

- La malla deja 20 de 526 accesos de estancia sin ruta desde el aparcamiento (01-11-2026; antes 79 de 509, y otros 10
  solo «llegaban» por costuras que atravesaban la fachada): locales sin puerta en el modelo (1903 P1 22, 1904 P0 26,
  terrazas de 1904 P1 y de 1905 P0 13 y P1 T1-T3, altillo 01 del 1907), puertas que no abren el muro en que van
  (instalaciones 01-04 del −1 de 1903, 1903 P1 28 y su distribuidor, 1904 P1 31-32, terraza 57 de 1905), el almacén
  del −1 de Teleco (su puerta da bajo el forjado de P0), el cuarto de instalaciones del −1 del 1906 (puerta de 1,67 m
  bajo la escalera) y el altillo 02 del 1907, que no tiene escalera en el plano. Quien va a uno de ellos aparece en su
  sitio en vez de andar (se cuenta en `Multitud.Fallos`).
- Hora acelerada probada en vivo el 01-11-2026 a 60× y 600× (y vuelta a 1× con salto de reloj): sin excepciones;
  a 60× hay ráfagas de 10–100 teletransportes en cada reconciliación (viajes de más de un cuarto de hora del modelo) y
  a 600× todo se coloca directamente (cientos de cambios de sitio de golpe cuando cambia la hora de clase, como debe ser).
- Las estancias del servicio cuyo `unity_room` no existe en el modelo (p. ej. `1901_P2_23`) no se representan.
- Algunos locales sin puerta con malla (instalaciones, galerías, terrazas) no tienen acceso y no reciben gente.
- La última aproximación al asiento es en línea recta (entre filas de pupitres la malla está cortada por las sillas).
