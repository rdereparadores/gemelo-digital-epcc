# Personas (`Assets/_Gemelo/Personas/`)

NPC del gemelo: tantas personas en cada estancia como dice el servicio (1:1 con `occupancy`), que se mueven por el
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
| `FabricaAvatares.cs` | Crea y tiñe avatares (NPC y jugador: `GemeloApp.FabricaAvatar` y `GemeloApp.FabricaJugador`) |
| `Reparto.cs` | Qué personaje de Mixamo lleva cada persona según el local (alumnado, profesorado, mantenimiento, vigilancia) |
| `Shaders/MultitudInstanciada.shader` | Maniquí rígido animado en el vértice (andar, correr, sentado, hablar, móvil) |
| `Editor/ConstructorNavegacion.cs` | Tools ▸ Gemelo ▸ Malla de navegación → `Navegacion/Campus.asset` |
| `Editor/ConstructorManiquies.cs` | Tools ▸ Gemelo ▸ Maniquíes → `Maniquies/` (mallas lejanas, maniquíes, clips, controlador) |
| `Editor/ImportadorMixamo.cs` | Ajustes de importación de `Mixamo/` (Humanoid, LOD de malla, clips in place, texturas) |
| `Editor/ConstructorMixamo.cs` | Tools ▸ Gemelo ▸ Personajes de Mixamo → materiales, controladores, prefabs y `RecursosPersonas` |
| `Editor/GaleriaPersonajes.cs` | Render de prueba de todos los avatares con un clip (sin Play Mode) |
| `Mixamo/` | Personajes (FBX sin texturas embebidas, texturas ≤ 1024 px, JSON con colores), animaciones, override y prefabs |
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
  y cose las mesetas a sus tramos; ver [Herramientas/politecnica](../../../Herramientas/politecnica/README.md)). No
  hay enlaces «ascensor» ni costuras a través de tabiques (`CoserForzado`, desactivado: unían azoteas sin puerta con
  los locales de al lado atravesando la fachada).
- **Costuras**: el voxelizado deja algunos trozos sueltos separados por centímetros. El builder busca los trozos que no
  conectan con el principal y los cose con enlaces cortos (≤ 0,9 m, casi a la misma altura y sin nada en medio) que se
  guardan en `RecursosPersonas` y se añaden en ejecución con `NavMesh.AddLink`.
- **Puntos de paso**: el builder guarda en `RecursosPersonas.escaleras` el arranque y la llegada de cada tramo de
  escalera (huellas de peldaño agrupadas, en la malla). El buscador de Unity tiene un tope de nodos y no completa las
  rutas largas por interiores llenos de muebles (del aparcamiento a la P2 del Común): entonces `Navegacion.Calcular` la
  parte por el punto de paso (escalera o puerta exterior del edificio de un extremo) que menos la alarga, hasta tres
  veces; solo si así tampoco, sigue desde donde se quedó.
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
- **Puertas**: este módulo registra `Puerta.Presencia` con una consulta a la rejilla (`Rejilla.EnPaso`) y al
  jugador: las puertas se abren para los NPC de pie y el jugador que estén en su rectángulo de paso (ver
  [Convenciones](../README.md#convenciones)).

## Dibujo

- Hasta `Ajustes.maxAnimadas` personas (40 por defecto, con un tope interno de 80, `DibujoMultitud.TopeAnimadas`) a
  menos de 40 m y en pantalla llevan un **personaje de Mixamo** con `Animator` humanoide (reserva reutilizada por
  personaje). Se crean como mucho 3 por selección (cada 0,2 s), porque crear uno cuesta 1-3 ms; mientras tanto sigue
  de maniquí. Solo proyectan sombra los que están a menos de 14 m, y nunca el pelo ni las pestañas (recorte alfa a
  dos caras: la pasada de sombra más cara). A más de 8 m la piel se calcula con 2 huesos (`SkinQuality.Bone2`). Las
  mallas llevan LOD de malla de Unity 6 (`generateMeshLods` al importar): de lejos se dibujan simplificadas. Con
  `maxAnimadas` = 0 no queda ninguno animado (la holgura de 4 para no parpadear solo vale con alguno).
- El resto, hasta `Ajustes.distanciaPersonas`, son maniquíes rígidos dibujados con `Graphics.RenderMeshIndirect` (un
  dibujo por variante, con sombra a menos de 35 m) y animados en el vértice por `MultitudInstanciada`. Llevan los
  **colores dominantes del personaje** que tendrían de cerca (piel, ropa de arriba, mangas, ropa de abajo, zapatos y
  pelo o casco/gorra, sacados de su textura por zonas del cuerpo) y su estatura, así que el salto de LOD apenas se nota.
- Con los maniquíes como avatares cercanos (Tools ▸ Gemelo ▸ Maniquíes) cada uno se tiñe con una paleta 8×1 propia
  (`colorearAvatares`).
- **Sentados**: el avatar sube o baja `alturaAsiento − alturaAsientoClip · escala` (la altura de la silla menos la
  del asiento de la postura sentada, que mide `ConstructorMixamo`: 0,443 m para 1,81 m de estatura). Con el gesto
  del móvil, el estado `SentadoMovil` **teclea** (`Sit_Typing`) si hay una pantalla de ordenador delante del puesto
  (`Plaza.ordenador`: mallas `*Pantalla_Monitor` a la altura de una mesa, a menos de 0,9 m del punto 0,5 m por
  delante del asiento) y **escribe** (`Sit_Writing`) si no (`ParamAvatar.Escribiendo` = 0/1). Donde no hay mesa
  (`Estancia.mesas` falso: cafetería, salón de actos, salas de descanso y de estudiantes, bancos de los halls) se
  queda sentado sin más; de pie sí saca el móvil (`MovilDePie`, en la mano derecha). Con los Mixamo el móvil no
  sale sentado (`RecursosPersonas.movilSentado`).
- Se ocultan los que quedan por encima del corte de su edificio (`GemeloApp.CortarEdificio`) y, vistos desde fuera y
  a más de 70 m, los que están dentro de un edificio.
- **Tras los muros**: con la cámara dentro de un local cerrado (aula, despacho, laboratorio…; no pasillos, vestíbulos,
  halls, escaleras, cafetería ni patios, y no a menos de 2,5 m de una de sus puertas), los que están bajo techo en otro
  local solo se dibujan si están en la misma planta y a menos de 10 m (25 m en pasillos y vestíbulos, que se ven por
  la puerta abierta); los de fuera, sí (se ven por las ventanas). `DibujoMultitud.OcultarTrasMuros = false` lo
  desactiva (pruebas).
- **Sombras**: quien está quieto bajo techo (en una estancia que no es patio ni terraza) no da sombra del sol: el techo
  ya la tapa y las luces interiores no tienen sombras. `DibujoMultitud.SombrasBajoTecho = true` lo deshace (pruebas).
- **Stencil**: en la vista cenital con una capa de color, los avatares llevan un material más
  (`Resources/GemeloPersonas/MarcaPersonas.shader`) que marca el bit 3 (valor 8) del stencil donde se ven, y
  `MultitudInstanciada` lo marca siempre en su pasada de color; la capa de color de Control no pinta ahí, así que no
  tiñe a la gente. Un material de más solo vuelve a dibujar la última submalla: vale para los renderers de una
  submalla (casi todas las piezas de Mixamo); los de varias (James, Lewis, Alex) llevan al lado, solo mientras hace
  falta, una copia del `SkinnedMeshRenderer` con los mismos huesos y la marca en todas sus submallas.

## Personajes de Mixamo

Los NPC cercanos y el jugador son personajes de Mixamo:

| Papel | Personajes | Dónde salen (`Reparto.cs`) |
|---|---|---|
| Alumnado | Remy, Bryce, James, Lewis, David (hombres); Kate, Sophie, Elizabeth, Jody, Louise (mujeres) | el resto |
| Profesorado | Josh, Joe (hombres); Martha, Suzie, Roth (mujeres) | 85 % en despachos, secretaría, conserjería, oficinas y salas de juntas; 40 % en laboratorios de investigación; 8 % en los demás laboratorios; 3 % en las aulas; 6 % en el resto |
| Mantenimiento | Pete (chaleco reflectante y casco) | 5 % en pasillos, distribuidores, almacenes e instalaciones; como mucho 2 a la vez |
| Vigilancia | Alex (uniforme negro y gorra) | 12 % en conserjería, vestíbulos y halls; como mucho 2 a la vez |
| Jugador | Brian, Megan (no salen como NPC) | `Ajustes.avatarJugador` (Ajustes ▸ Controles) |

El sexo (y con él el andar y la malla lejana) sale de la semilla de cada persona (58 % hombres); el personaje se
elige entre los de ese sexo y papel (de dos al azar, el menos repetido). El servicio no publica ocupación de los
despachos, así que casi todo el personal que se ve está en aulas, laboratorios y salas de juntas (con
~1.800 personas, ~90 son profesorado, 2 de mantenimiento y 2 de vigilancia). `Multitud.porPersonaje` cuenta cuántos
lleva cada personaje.

### Cómo se rehace

1. Descargar de Mixamo a `Datos/descargas/mixamo/` (caché ignorada por git): personajes «FBX for Unity», con piel y
   en T-pose, en `personajes/<Nombre>.fbx`, y animaciones «FBX for Unity» sin piel (esqueleto de Y Bot), las de
   locomoción *in place*, en `animaciones/<nombre>.fbx` (nombres en la tabla de clips).
2. `/usr/bin/python3.14 -I Herramientas/mixamo/preparar_mixamo.py [Nombre…]` (unos 2 min con todo). Sin Blender: lee
   el FBX binario (`fbx_binario.py`), extrae las texturas embebidas y lo vuelve a escribir sin ellas (mallas, pesos y
   esqueleto `mixamorig:*` idénticos, así que Unity lo importa como cualquier FBX de Mixamo, sin nodo raíz de más).
   Deja en `Mixamo/Personajes/<N>/` el FBX (~2,5 MB), el color a 1024 px (JPG; PNG con alfa si el pelo o las
   pestañas recortan con ella, con el color de los texeles transparentes rellenado desde los vecinos para que no
   salga el halo blanco al filtrar), la normal (JPG), una máscara de 512 px con el *gloss* en A (→ smoothness de
   URP/Lit) y `<N>.json` con los materiales, la altura y los colores dominantes por zonas (en T-pose: manos y cara,
   pecho, brazos, muslos, pies, pelo o casco/gorra). Copia también las animaciones a `Mixamo/Animaciones/`.
   `MIXAMO_DEPURAR=<carpeta>` guarda una vista de las muestras de color; `MIXAMO_DESTINO=<carpeta>` escribe en otro
   sitio (pruebas).
3. En Unity, al importar, `ImportadorMixamo` aplica: Humanoid con *Create From This Model* (también las animaciones,
   con el avatar de su esqueleto de Y Bot), escala del archivo (Remy viene a 3,8 m y se importa a 1,78), LOD de
   malla, tangentes, sin cámaras ni blendshapes; clips con giro, altura y posición horneados en la pose (referencia
   original) y en bucle; texturas a 1024 como mucho, normales como *Normal map*,
   máscaras lineales y el color con alfa conservando la cobertura en los mipmaps.
4. **Tools ▸ Gemelo ▸ Personajes de Mixamo** (`ConstructorMixamo.Construir()`, ~30 s): materiales URP/Lit en
   `Personajes/<N>/Materiales/` (pelo y pestañas con *Alpha Clipping* 0,45 y a dos caras; smoothness 0,5 × gloss),
   reimporta cada FBX con ellos (`AddRemap`), crea los dos `AnimatorOverrideController` (`Persona_Hombre`,
   `Persona_Mujer`) sobre `Maniquies/Persona.controller`, un prefab por personaje en `Mixamo/Prefabs/` (con el móvil
   en la mano derecha: dedos en +Y y palma en +Z del hueso) y rellena `RecursosPersonas` (avatares, perfiles,
   jugadores, sin teñir, sin móvil sentado y la altura del asiento de la postura sentada). **Tools ▸ Gemelo ▸
   Maniquíes** rehace el controlador (conservando su GUID) y vuelve a poner los maniquíes: después hay que volver a
   pasar este.
5. Revisar sin Play Mode: `Gemelo.GaleriaPersonajes.Render("/ruta.png", "Sentado", 1.5f)` (o `Reposo`, `Andar`,
   `SentadoMovil`, `SentadoEscribiendo`, `MovilDePie`, `HablarDePie`…).

Lo versionado de Mixamo ocupa ~107 MB (personajes 83 MB, animaciones 23 MB contando las copias recentradas, prefabs
1,6 MB), frente a ~900 MB de los FBX originales.

### Clips

`Persona.controller` (generado por `ConstructorManiquies`) tiene los estados `Locomocion` (árbol 1D por `Velocidad`:
`Reposo` 0, `Andar` 1,4, `Correr` 3,0 y `CorrerRapido` 4,6 m/s), `HablarDePie`, `MovilDePie`, `Sentado`,
`SentadoHablando`, `SentadoMovil` (árbol 1D por `Escribiendo`: `SentadoMovil` 0 y `SentadoEscribiendo` 1) y `Aire`,
con los parámetros de `ParamAvatar` más `Movil`. Los override sustituyen los clips:

| Clip del maniquí | Hombre | Mujer |
|---|---|---|
| Reposo | Idle_Standing (Standing Idle) | Idle_WeightShift (Weight Shift Idle) |
| Andar | Walk_MaleStandard (Male Standard Walk) | Walk_Female (Female Walk) |
| Correr / CorrerRapido | Run_Jogging (Jogging) / Run_Standard (Standard Running) | igual |
| HablarDePie | Talk_GeneralConversation (General Conversation) | Talk_AskingOneHand (Asking A Question With One Hand) |
| MovilDePie | Texting_Standing (Standing Texting On Phone) | igual |
| Sentado | Sit_Breathing (Sitting With Breathing Idle) | Sit_StillChair_Centrado (Sitting Still In A Chair) |
| SentadoHablando | Sit_Talking_Centrado (Sitting And Talking To Another Person) | igual |
| SentadoMovil | Sit_Typing (Sitting At A Computer And Typing) | igual |
| SentadoEscribiendo | Sit_Writing_Centrado (Male Writing While Seated) | igual |
| Aire | Falling_Idle (Mid-Air Falling Idle) | igual |

Mixamo no tiene «texting» sentado: sentado, el gesto del móvil teclea o escribe (ver Dibujo). Las posturas sentadas no
ponen la cadera en el mismo sitio: «Writing» se sienta 20 cm más adelante (las piernas atravesaban la mesa) y
«Still In A Chair», «Talking» y «Writing», 5-8 cm más altas. `ConstructorMixamo` guarda copias `*_Centrado.anim`
con el cuerpo llevado a la cadera de «Typing» en planta y a medio camino en altura (del todo hundía los pies), con
las claves reducidas. `Walk_Standard`, `StandToSit` y `SitToStand` se descargaron pero no se versionan: los NPC llegan a
la plaza y se sientan en el sitio (con la transición de 0,55 s del controlador), y las animaciones de sentarse
necesitan empezar delante de la silla y desplazar la raíz, lo que complicaría la máquina de estados y el paso.

## Maniquíes

`Tools ▸ Gemelo ▸ Maniquíes` genera cuatro cuerpos (hombre y mujer, con y sin mochila) con esqueleto humanoide de
nombres estándar (`Hips`, `Spine`, `Chest`, `Neck`, `Head`, `LeftShoulder`, `LeftUpperArm`…), su `Avatar`, once clips
humanoides (los de la tabla de arriba) y `Persona.controller`. Las poses se escriben como giros de huesos y se
convierten a músculos, así que los clips valen para cualquier humanoide. Las mallas lejanas de la multitud
(`Lejano_*`) también salen de aquí; si se quisiera otro cuerpo lejos, basta con cambiar **Mallas Lejanas** por mallas
con la misma convención de `uv2`–`uv4` (pieza y región, hombro/cadera/cuello y codo/rodilla). De cerca los maniquíes
solo se usan para comparar (`FabricaAvatares.PruebaAvatares`, ver Rendimiento).

## Rendimiento

Medido con `MedidorPersonas` en Play Mode en el editor (Radeon 780M; tandas de 3 s con y sin multitud,
intercaladas), con 1.190–1.570 personas simuladas:

| Vista | Animadas / instanciadas visibles | GPU | Hilo principal | Simulación | Dibujo (CPU) |
|---|---|---|---|---|---|
| Aérea del campus | 0 / 9 | ±0,4 ms (ruido) | +0,03 ms | 0,61–0,68 ms | 0,31 ms |
| Pasillo de P1 del Común | 40 / 362 | +2,6 ms | +1,2 ms | 0,40–0,79 ms | 0,39 ms |
| Aula de 112 alumnos | 40 / 434 | +2,2 ms | +1,5 ms | 0,59–0,64 ms | 0,43 ms |
| Pasillo, solo instanciadas | 0 / 471 | +1,1 ms | +1,3 ms | 0,56–0,68 ms | 0,39 ms |

- **Animados**: cada uno cuesta ~0,05 ms de GPU y ~0,07 ms de hilo principal. Con 120 se iban a +7 ms de GPU y +9 ms
  de CPU, de ahí el valor de fábrica de 40. En la build, bajar de 40 a 20 ahorra 1–1,5 ms de GPU.
- **Mixamo frente a maniquíes** (40 animados, tercera persona, `FabricaAvatares.PruebaAvatares`): GPU +1,8 ms en la
  plaza del Común y +3,1 ms en el aula NORBA (1903_P1_24) con clase; hilo principal +1,2 y +1,5 ms. Lo que queda es
  sobre todo el sombreado de más triángulos y renderers (~7 por personaje).
- **Ocultar tras los muros y sombras bajo techo**: en el aula NORBA se pasa de dibujar 36 animados y 177 maniquíes
  (casi todos de las aulas y plantas vecinas) a 7 y 6. Al atardecer, con sombras del sol, ahorra 2,8 ms de GPU
  (mediana) y 0,9 ms de hilo principal; de noche, 1,4 y 0,8 ms. En la calle y en la vista cenital no cambia nada.
- **Simulación** (reconciliar + rutas + paso): 0,4–0,9 ms. Las rutas se calculan con un presupuesto de 0,6 ms por
  fotograma; al empezar una evacuación grande hay picos de 1–5 ms durante unos segundos mientras se calculan las
  rutas de cada estancia al punto de reunión (luego quedan en caché).

## Limitaciones conocidas

- La malla deja 20 de 526 accesos de estancia sin ruta desde el aparcamiento. Quien va a uno de ellos aparece en su
  sitio en vez de andar (se cuenta en `Multitud.Fallos`). Son:
  - locales sin puerta en el modelo (1903 P1 22, 1904 P0 26, terrazas de 1904 P1 y de 1905 P0 13 y P1 T1-T3,
    altillo 01 del 1907);
  - puertas que no abren el muro en que van (instalaciones 01-04 del −1 de 1903, 1903 P1 28 y su distribuidor,
    1904 P1 31-32, terraza 57 de 1905);
  - el almacén del −1 de Teleco (su puerta da bajo el forjado de P0), el cuarto de instalaciones del −1 del 1906
    (puerta de 1,67 m bajo la escalera) y el altillo 02 del 1907, que no tiene escalera en el plano.
- Algunos locales sin puerta con malla (instalaciones, galerías, terrazas) no tienen acceso y no reciben gente.
- Las estancias del servicio cuyo `unity_room` no existe en el modelo (p. ej. `1901_P2_23`) no se representan.
- La última aproximación al asiento es en línea recta (entre filas de pupitres la malla está cortada por las sillas).
- Hora acelerada: probada a 60× y 600× (y vuelta a 1× con salto de reloj) sin excepciones. A 60× hay ráfagas de
  10–100 teletransportes en cada reconciliación; a 600× todo se coloca directamente. Los personajes de Mixamo aún no
  se han probado con la hora acelerada ni con saltos de reloj; las cuentas de `Multitud.porPersonaje` salen
  coherentes tras arrancar y tras una evacuación.
- Personajes de Mixamo: no se usan clips de sentarse y levantarse. El móvil de pie queda en la palma, casi tapado por
  la mano. En los bancos fijos con mesa corrida las manos pueden rozar el tablero.
