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
`NavMeshBuilder.BuildNavMeshData` (agente de 0,2 m de radio y 1,8 m de alto, escalón 0,36 m, vóxel 8 cm, con *height
mesh* para que la altura de los pies sea exacta en las escaleras).

- Se camina sobre suelos, peldaños, aceras, calzadas y el terreno. Muros, falsos techos, carpinterías, barandillas y
  **todo el mobiliario** se rasterizan como «no caminable»: estorban, pero encima no sale malla (ni islas sobre mesas).
- Las hojas de puerta (`Puerta`) y los triggers de los locales no cuentan.
- Áreas: 0 suelo/acera (coste 1), 3 terreno (1,8), 4 calzada (2,5): se prefiere la acera y se cruza por donde toca.
- **Costuras**: el voxelizado deja algunos trozos sueltos (sobre todo el arranque de tramos de escalera desde los
  rellanos, que en el modelo quedan tapiados por el muro de la caja). El builder busca los trozos que no conectan con
  el principal y los cose con enlaces cortos (≤ 0,9 m, casi a la misma altura) que se guardan en `RecursosPersonas` y
  se añaden en ejecución con `NavMesh.AddLink`.
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
  teñida con una paleta 8×1 propia por `MaterialPropertyBlock`). Solo proyectan sombra los que están a menos de 14 m.
- El resto, hasta `Ajustes.distanciaPersonas`, son maniquíes rígidos dibujados con `Graphics.RenderMeshIndirect` (un
  dibujo por variante, con sombra a menos de 35 m) y animados en el vértice por `MultitudInstanciada`.
- Se ocultan los que quedan por encima del corte de su edificio (`GemeloApp.CortarEdificio`) y, vistos desde fuera y
  a más de 70 m, los que están dentro de un edificio.

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
- La simulación (reconciliar + rutas + paso) se queda en 0,4–0,9 ms. Las rutas se calculan con un presupuesto de
  0,6 ms por fotograma; en el arranque de una evacuación grande hay picos de 1–5 ms durante unos segundos mientras se
  calculan las rutas de cada estancia al punto de reunión (luego quedan en caché).

## Limitaciones conocidas

- La malla deja 79 de 509 accesos de estancia sin ruta desde el aparcamiento (sobre todo la P1 de Edificación, cuya
  escalera no se puede andar en el modelo, y algunos despachos de la P1 de Telecomunicación). Quien va a una estancia
  sin ruta aparece en su sitio en vez de andar (se cuenta en `Multitud.Fallos`). Algunas plantas se unen por enlaces
  «ascensor» que se cruzan de golpe (1906 P1/P2, 1907 P1).
- El buscador de rutas de Unity tiene un tope de nodos: las rutas largas por interiores se calculan por tramos
  (se sigue desde donde se quedó), y alguna sale con rodeos.
- Con la hora del modelo acelerada no se ha podido probar en vivo (no se toca la escala global del servicio); la
  lógica de teletransporte depende solo de `HoraCampus.Escala`.
- Las estancias del servicio cuyo `unity_room` no existe en el modelo (p. ej. `1901_P2_23`) no se representan.
- Algunos locales sin puerta con malla (instalaciones, galerías, terrazas) no tienen acceso y no reciben gente.
- La última aproximación al asiento es en línea recta (entre filas de pupitres la malla está cortada por las sillas).
