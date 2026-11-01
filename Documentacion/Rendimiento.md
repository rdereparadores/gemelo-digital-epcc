# Rendimiento

Equipo de medida: AMD Radeon 780M integrada (Mesa RADV), 16 hilos, 30 GB, a 1920 × 1080. Mediciones de noviembre
de 2026.

**La GPU integrada varía ±3 ms entre pasadas idénticas** por temperatura y reloj. Las comparaciones solo sirven
**intercaladas** (A B A B) y dentro del mismo proceso, no en tandas seguidas.

## Cómo se mide

### La escena (`BenchmarkRendimiento`)

`Assets/Scripts/BenchmarkRendimiento.cs` mide la escena sin el gemelo. Con `-benchmark -benchmarkOut <carpeta>` el
ejecutable recorre 10 vistas fijas: aérea, tres exteriores a pie de calle, cafetería, hall de Informática, aula de
teoría, dos aulas de ordenadores y pasillo de P2 del Común. En cada una espera 120 frames y mide 300. Saca mediana y
p95 de frame, CPU, hilo principal, hilo de render y GPU, más triángulos, memoria y texturas, y una captura por vista
para comparar la imagen.

- La build se hace con **Tools ▸ Rendimiento ▸ Build de benchmark (Linux)** y queda en `Build/Benchmark/Campus.x86_64`.
  En el editor se lanza con `BenchmarkRendimiento.Lanzar(carpeta)` en Play Mode.
- Interruptores para medir sin recompilar: `-grd`, `-gpuocc`, `-msaa`, `-sombras` (distancia), `-cascadas`,
  `-sombraRes`, `-suaves`, `-escala`, `-ssao`, `-ssaoMedia`, `-ssaoDepth`, `-depthPriming`, `-opaca`, `-lodBias`,
  `-streaming`, `-presupuesto`, `-sombrasMuebles 0`, `-luces 0` y `-hierba`.

### La app (`RecorridoRendimiento`)

`Assets/_Gemelo/Control/RecorridoRendimiento.cs` mide la app del gemelo con la multitud cargada. Se lanza en la build de
**Tools ▸ Gemelo ▸ Build de Linux** con `-recorrido <carpeta>` (en el editor, `RecorridoRendimiento.Lanzar(carpeta)` en
Play Mode). Espera a que haya gente en escena, recorre las vistas y cierra la app. Necesita el servicio de sensores en
marcha y su reloj en horario lectivo; si no, no hay gente que medir.

Vistas: `menu`, `cenital`, `cenital_capa_1905` (capa de ocupación con Teleco cortado por P1) y, en tercera persona,
`tp_fachada_sur_comun`, `tp_plaza_este`, `tp_cafeteria_1901`, `tp_hall_1903`, `tp_aula_1901_P1_08`,
`tp_aula_pc_1903_P1_24` y `tp_pasillo_1901_P2`.

| Opción | Efecto |
|---|---|
| `-preajuste N` | Parte del preajuste N: 0 = Bajo, 1 = Medio, 2 = Alto, 3 = Ultra |
| `-ajustes '{json}'` | Cambia campos de `Ajustes` solo para esa sesión (no se guardan) |
| `-vistas a,b` | Mide solo esas vistas |
| `-ab '{json}'` | En cada vista alterna 6 tandas con los ajustes de partida (A) y 6 con esos cambios (B) |
| Interruptores de `BenchmarkRendimiento` | Van por encima de los ajustes (`-msaa`, `-cascadas`, `-opaca`…) |

Sin `-ab`, `recorrido.json` trae por vista los FPS, la mediana y el p95 de frame, hilo principal, hilo de render y
GPU, y los contadores de dibujo, más una captura. Con `-ab` trae la mediana de GPU y de frame de A y de B y la mediana
de las diferencias de cada pareja de tandas seguidas, más una captura de cada estado (`<vista>_A.png` y
`<vista>_B.png`) para compararlas.

Por ejemplo, para comparar el preajuste Alto con Ultra:

```bash
./GemeloPolitecnica.x86_64 -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 \
    -recorrido ab_alto -preajuste 3 -ab '{"antialias":3,"sombras":3,"oclusion":1}'
```

Para cambios de código hay además `MedidorRendimiento` (tandas A/B y desglose por marcadores del profiler desde Play
Mode) y `MedidorPersonas` (coste de la multitud); ver [Pruebas y medida](../Assets/_Gemelo/README.md#pruebas-y-medida).

## La escena

### Diagnóstico

- **Escena:** 32.800 renderers, 1.457 mallas, 3,0 M de vértices, 516 luces puntuales, 10.861 LODGroups de mobiliario
  y 153 texturas (531 MB en el build).
- **Por frame:** se dibujan 5–12 M de triángulos contando las cascadas de sombra y el prepass de profundidad y normales
  del SSAO.
- **Cuello de botella:** la GPU, a unos 24–27 ms por frame en build y editor con MSAA 4×, sombras de 4 cascadas a
  150 m y 4096 px y SSAO completo (hoy, el preajuste Ultra). MSAA 4× costaba ~7 ms; las sombras del sol, ~7 ms; el
  SSAO, ~3 ms en exteriores.
- **Lo que no pesa:** las sombras del mobiliario (quitarlas todas no cambia nada) y las luces interiores (ya se apagan
  lejos de la cámara).
- **Build original en OpenGL:** el hilo de render costaba 8–20 ms por la sobrecarga del driver.
- **Static batching:** duplicaba en `level0.resS` (203 MB) la geometría de cada mueble instanciado.
- **Mallas de los `.mcam`:** se importaban legibles, con copia en CPU además de en GPU.

### Cambios aplicados sin cambio visible

Comprobados comparando las capturas píxel a píxel.

| Cambio | Dónde | Efecto medido |
|---|---|---|
| Vulkan en Linux (antes OpenGL) | Player ▸ Graphics APIs | Hilo de render: de 13,7 a 1,7 ms. Mismo tiempo de GPU |
| Sin static batching en Standalone | Player ▸ Static Batching | Build −220 MB, memoria −200 MB. Con el SRP Batcher no cuesta CPU |
| Mallas `.mcam` no legibles y reordenadas para la caché de vértices | `McamImporter` (versión 8) | Sin copia en CPU en el build |
| Colisiones precocinadas | Player ▸ Prebake Collision Meshes | Las mallas no legibles conservan su MeshCollider; carga más rápida |
| Normales y tangentes a 16 bits; las UV, no (van en metros y perderían precisión) | Player ▸ Vertex Compression | Menos memoria de vértices |
| Mip streaming en texturas de mallas, descartando los mips que no se ven, con presupuesto de 1 GB | `TexturasStreaming` y `ConfiguracionRendimiento`; Quality ▸ PC | Texturas: de 531 a 190–340 MB según la vista, a la misma calidad |
| GPU Resident Drawer en el editor; en el build solo con `-grd 1` | Asset de URP y `ConfiguracionRendimiento` | Scene view: render de 14,4 a 10,7 ms. En el build con la 780M costaba +1,2 ms de GPU |
| Textura opaca de la cámara solo mientras hay charcos de una fuga de agua | Asset de URP y `Emergencias` | Una copia de la imagen menos por frame |

Total en el build: memoria usada de 1.344 a 1.155 MB, memoria de sistema de 991 a 792 MB, build de 997 a 803 MB y p95
de 29,5 a 28,1 ms. Los FPS medios no cambiaron (36–38), porque el límite es la GPU.

**Descartado:**
- **Oclusión por GPU:** quita un 30 % de triángulos pero cuesta +2,9 ms de GPU en la 780M.
- **Apagar en modo edición las luces de los edificios lejanos:** solo ahorra 0,4 ms con GRD, y Unity lo apunta como
  cambio de las instancias de prefab.

## App del gemelo

Build de Linux con Vulkan, ~1.900 personas en escena, tandas A/B intercaladas en el mismo proceso con
`RecorridoRendimiento -ab`. La app también va limitada por la GPU: el hilo principal se queda en 12–16 ms.

Ahorro de GPU de cada opción frente a Ultra: mediana de las diferencias entre tandas seguidas, como rango entre las
vistas medidas.

| Cambio frente a Ultra | GPU |
|---|---|
| Sombras a 2048 px en vez de 4096 | −2 a −4,5 ms |
| MSAA 2× en vez de 4× | −2 a −3,5 ms |
| SSAO a media resolución | −0,5 a −1,7 ms |
| SMAA en vez de MSAA 4× | −4 a −6 ms |
| Sombras «Medias» (3 cascadas, 100 m) | −4 a −6 ms |
| 20 personas animadas en vez de 40 | −1 a −1,5 ms |
| **Preajuste Alto** (MSAA 2×, sombras a 2048 px, SSAO a media resolución) | **−5,4 a −7,9 ms (22–25 %)** |

Alto no se distingue de Ultra en las capturas comparadas, así que es el preajuste de fábrica. Algunos ejemplos
(mediana de GPU por frame de cada configuración):

| Vista | Ultra | Alto |
|---|---|---|
| Menú | 24,4 ms | 19,2 ms |
| Aula 1901_P1_08 | 34,2 ms | 26,4 ms |
| Hall de 1903 | 28,7 ms | 22,1 ms |

SMAA y las sombras «Medias» ahorran más, pero se notan: SMAA deja más dientes de sierra en barandillas, cables y
aristas finas, y las sombras «Medias» solo llegan a 100 m. Por eso van en el preajuste Medio.

La hierba del Terrain ocupa ~290 MB de memoria. Bajar la distancia de la hierba (Ajustes ▸ Gráficos) la reduce, a
costa de que deje de verse antes.

Lo que cuestan las personas (animadas, maniquíes, simulación) está en
[Personas ▸ Rendimiento](../Assets/_Gemelo/Personas/README.md#rendimiento).
