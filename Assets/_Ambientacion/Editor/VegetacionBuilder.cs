using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Genera las especies de árboles y matorral del campus y las coloca en el Terrain como árboles y
// detalles (los mismos que usan las herramientas Paint Trees y Paint Details, así que se pueden
// retocar a mano).
//
// Las posiciones, tamaños de copa y especies salen de Assets/_Ambientacion/Vegetacion/Datos/arboles.csv, que
// genera Herramientas/detectar_arboles.py a partir de la ortofoto PNOA y OSM. La hierba se reparte
// con el splatmap del suelo (Datos/Suelo).
public static class VegetacionBuilder
{
    const string Root = "Assets/_Ambientacion/Vegetacion";
    const string TexDir = Root + "/Texturas";
    const string MatDir = Root + "/Materiales";
    const string MeshDir = Root + "/Mallas";
    const string PrefabDir = Root + "/Prefabs";
    const string Datos = Root + "/Datos/arboles.csv";
    const string Splat03 = "Datos/Suelo/splat_0_3.png";
    const string Splat47 = "Datos/Suelo/splat_4_7.png";

    enum Forma { Redonda, Paraguas, Columnar, Llorona, Palmera, Mata }

    class Especie
    {
        public string Nombre;
        public int Semilla;
        public Forma Forma;
        public float Fuste;           // altura del tronco limpio hasta la cruz
        public float RadioTronco;
        public float RadioCopa;       // horizontal
        public float SemiAltoCopa;    // vertical
        public int Ramas;
        public int Tarjetas;
        public float TamTarjeta;
        public int Troncos = 1;       // olivos y matas
        public float Inclinacion = 0.35f;
        public Material Corteza, Hojas, HojasInterior;

        public bool Arbusto => Forma == Forma.Mata;
    }

    [MenuItem("Tools/Vegetación/Generar y colocar en el Terrain")]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[Vegetacion] No hay Terrain activo"); return; }
        if (!File.Exists(Datos)) { Debug.LogError($"[Vegetacion] Falta {Datos}: ejecuta Herramientas/detectar_arboles.py"); return; }

        foreach (var d in new[] { Root, TexDir, MatDir, MeshDir, PrefabDir }) EnsureFolder(d);

        // Texturas de follaje (procedurales, con transparencia)
        var tCaduca = GuardarTextura("Hojas_Caduca", PintarHojas(512, 11, new Color(0.47f, 0.62f, 0.22f), 0.035f, 300, 34f, 13f, null, 0, true), true);
        var tEncina = GuardarTextura("Hojas_Encina", PintarHojas(512, 23, new Color(0.25f, 0.31f, 0.17f), 0.03f, 650, 18f, 9f, null, 0, true), true);
        var tArbusto = GuardarTextura("Hojas_Arbusto", PintarHojas(512, 37, new Color(0.30f, 0.39f, 0.19f), 0.04f, 600, 16f, 7f, null, 0, false), true);
        var tAdelfa = GuardarTextura("Hojas_Adelfa", PintarHojas(512, 41, new Color(0.27f, 0.38f, 0.20f), 0.03f, 380, 32f, 7f, new Color(0.93f, 0.55f, 0.70f), 70, false), true);
        var tJara = GuardarTextura("Hojas_Jara", PintarHojas(512, 43, new Color(0.22f, 0.27f, 0.15f), 0.03f, 520, 26f, 8f, null, 0, false), true);
        var tOlivo = GuardarTextura("Hojas_Olivo", PintarHojas(512, 47, new Color(0.42f, 0.47f, 0.36f), 0.03f, 700, 22f, 4.5f, null, 0, true), true);
        var tSauce = GuardarTextura("Hojas_Sauce", PintarHojas(512, 53, new Color(0.52f, 0.62f, 0.30f), 0.03f, 650, 30f, 4f, null, 0, true), true);
        var tAlamo = GuardarTextura("Hojas_Alamo", PintarHojas(512, 59, new Color(0.40f, 0.55f, 0.22f), 0.04f, 260, 26f, 20f, null, 0, true), true);
        var tPino = GuardarTextura("Hojas_Pino", PintarAciculas(512, 61, new Color(0.23f, 0.33f, 0.16f)), true);
        var tRetama = GuardarTextura("Hojas_Retama", PintarVaras(512, 67, new Color(0.40f, 0.46f, 0.20f)), true);
        var tCortina = GuardarTextura("Cortina_Sauce", PintarCortina(512, 71, new Color(0.50f, 0.60f, 0.28f)), true);
        var tPalma = GuardarTextura("Hoja_Palmera", PintarAbanico(512, 73, new Color(0.36f, 0.45f, 0.20f)), true);
        var tHierbaSeca = GuardarTextura("Hierba_Seca", PintarHierba(256, 51, new Color(0.82f, 0.78f, 0.64f)), true);
        var tHierbaVerde = GuardarTextura("Hierba_Verde", PintarHierba(256, 52, new Color(0.50f, 0.60f, 0.32f)), true);

        // Cortezas: texturas CC0 de Poly Haven (Herramientas/preparar_texturas.py)
        var cAlineacion = MatCorteza("Corteza_Alineacion", "bark_platanus", 1.5f);
        var cEncina = MatCorteza("Corteza_Encina", "bark_brown_02", 1f);
        var cPino = MatCorteza("Corteza_Pino", "pine_bark", 2f);
        var cPalma = MatCorteza("Corteza_Palmera", "palm_bark", 1.86f);
        var cSauce = MatCorteza("Corteza_Sauce", "bark_willow", 1f);

        (Material, Material) Follaje(string n, Texture2D t) =>
            (MatHojas($"Follaje_{n}", t, Color.white), MatHojas($"Follaje_{n}_Interior", t, new Color(0.62f, 0.64f, 0.6f)));
        var (caduca, caducaIn) = Follaje("Caduca", tCaduca);
        var (encina, encinaIn) = Follaje("Encina", tEncina);
        var (arbusto, arbustoIn) = Follaje("Arbusto", tArbusto);
        var (adelfa, adelfaIn) = Follaje("Adelfa", tAdelfa);
        var (jara, jaraIn) = Follaje("Jara", tJara);
        var (olivo, olivoIn) = Follaje("Olivo", tOlivo);
        var (sauce, sauceIn) = Follaje("Sauce", tSauce);
        var (alamo, alamoIn) = Follaje("Alamo", tAlamo);
        var (pino, pinoIn) = Follaje("Pino", tPino);
        var (retama, retamaIn) = Follaje("Retama", tRetama);
        var cortina = MatHojas("Follaje_Cortina_Sauce", tCortina, Color.white);
        var palma = MatHojas("Follaje_Palmera", tPalma, Color.white);
        var palmaSeca = MatHojas("Follaje_Palmera_Seca", tPalma, new Color(1.6f, 1.1f, 0.75f));

        // Especies. Medidas de referencia; cada instancia se escala según su copa en la ortofoto.
        var especies = new[]
        {
            new Especie { Nombre = "Arbol_Alineacion_A", Semilla = 101, Forma = Forma.Redonda, Fuste = 2.6f, RadioTronco = 0.16f, RadioCopa = 3.0f, SemiAltoCopa = 2.7f,
                Ramas = 34, Tarjetas = 260, TamTarjeta = 1.4f, Corteza = cAlineacion, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Arbol_Alineacion_B", Semilla = 202, Forma = Forma.Redonda, Fuste = 2.3f, RadioTronco = 0.15f, RadioCopa = 2.7f, SemiAltoCopa = 3.1f,
                Ramas = 30, Tarjetas = 240, TamTarjeta = 1.35f, Corteza = cAlineacion, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Arbol_Joven", Semilla = 303, Forma = Forma.Redonda, Fuste = 2.0f, RadioTronco = 0.07f, RadioCopa = 1.4f, SemiAltoCopa = 1.7f,
                Ramas = 14, Tarjetas = 90, TamTarjeta = 0.95f, Corteza = cAlineacion, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Encina", Semilla = 404, Forma = Forma.Redonda, Fuste = 1.5f, RadioTronco = 0.2f, RadioCopa = 2.6f, SemiAltoCopa = 2.1f,
                Ramas = 30, Tarjetas = 300, TamTarjeta = 1.1f, Corteza = cEncina, Hojas = encina, HojasInterior = encinaIn },
            new Especie { Nombre = "Olivo", Semilla = 414, Forma = Forma.Redonda, Fuste = 1.1f, RadioTronco = 0.17f, RadioCopa = 2.1f, SemiAltoCopa = 1.6f,
                Ramas = 26, Tarjetas = 240, TamTarjeta = 1.0f, Troncos = 2, Inclinacion = 0.6f, Corteza = cEncina, Hojas = olivo, HojasInterior = olivoIn },
            new Especie { Nombre = "Pino_Pinonero", Semilla = 424, Forma = Forma.Paraguas, Fuste = 6.5f, RadioTronco = 0.24f, RadioCopa = 4.2f, SemiAltoCopa = 1.5f,
                Ramas = 30, Tarjetas = 950, TamTarjeta = 0.8f, Inclinacion = 0.9f, Corteza = cPino, Hojas = pino, HojasInterior = pinoIn },
            new Especie { Nombre = "Sauce", Semilla = 434, Forma = Forma.Llorona, Fuste = 2.2f, RadioTronco = 0.22f, RadioCopa = 3.2f, SemiAltoCopa = 2.6f,
                Ramas = 28, Tarjetas = 220, TamTarjeta = 1.2f, Corteza = cSauce, Hojas = sauce, HojasInterior = sauceIn },
            new Especie { Nombre = "Alamo", Semilla = 444, Forma = Forma.Columnar, Fuste = 1.6f, RadioTronco = 0.18f, RadioCopa = 1.9f, SemiAltoCopa = 5.0f,
                Ramas = 30, Tarjetas = 280, TamTarjeta = 1.2f, Inclinacion = 0.15f, Corteza = cSauce, Hojas = alamo, HojasInterior = alamoIn },
            new Especie { Nombre = "Palmera", Semilla = 454, Forma = Forma.Palmera, Fuste = 8f, RadioTronco = 0.28f, RadioCopa = 2.0f, SemiAltoCopa = 1.2f,
                Tarjetas = 30, TamTarjeta = 1.5f, Corteza = cPalma, Hojas = palma, HojasInterior = palmaSeca },
            new Especie { Nombre = "Arbusto", Semilla = 505, Forma = Forma.Mata, RadioTronco = 0.03f, RadioCopa = 0.9f, SemiAltoCopa = 0.7f,
                Ramas = 6, Tarjetas = 70, TamTarjeta = 0.7f, Troncos = 4, Corteza = cEncina, Hojas = arbusto, HojasInterior = arbustoIn },
            new Especie { Nombre = "Adelfa", Semilla = 606, Forma = Forma.Mata, RadioTronco = 0.03f, RadioCopa = 0.9f, SemiAltoCopa = 0.9f,
                Ramas = 7, Tarjetas = 75, TamTarjeta = 0.75f, Troncos = 4, Corteza = cEncina, Hojas = adelfa, HojasInterior = adelfaIn },
            new Especie { Nombre = "Jara", Semilla = 616, Forma = Forma.Mata, RadioTronco = 0.025f, RadioCopa = 0.8f, SemiAltoCopa = 0.65f,
                Ramas = 6, Tarjetas = 60, TamTarjeta = 0.6f, Troncos = 5, Corteza = cEncina, Hojas = jara, HojasInterior = jaraIn },
            new Especie { Nombre = "Retama", Semilla = 626, Forma = Forma.Mata, RadioTronco = 0.02f, RadioCopa = 0.85f, SemiAltoCopa = 0.8f,
                Ramas = 8, Tarjetas = 55, TamTarjeta = 0.9f, Troncos = 6, Corteza = cEncina, Hojas = retama, HojasInterior = retamaIn },
        };
        // El sauce lleva además las cortinas colgantes como cuarto material
        var prefabs = especies.Select(e => CrearPrefab(e, e.Forma == Forma.Llorona ? cortina : null)).ToArray();

        ColocarArboles(terrain, especies, prefabs);
        ColocarHierba(terrain, tHierbaSeca, tHierbaVerde);
        AssetDatabase.SaveAssets();
    }

    // ---------- Colocación ----------

    static void ColocarArboles(Terrain terrain, Especie[] especies, GameObject[] prefabs)
    {
        var data = terrain.terrainData;
        var size = data.size;

        // Conserva los prototipos e instancias ajenos; los nuestros siempre van al final
        var viejos = data.treePrototypes;
        var propios = new HashSet<GameObject>(prefabs);
        var ajenos = viejos.Where(p => !propios.Contains(p.prefab)).ToList();
        var instancias = data.treeInstances
            .Where(t => t.prototypeIndex < viejos.Length && !propios.Contains(viejos[t.prototypeIndex].prefab))
            .Select(t => { t.prototypeIndex = ajenos.IndexOf(viejos[t.prototypeIndex]); return t; })
            .Where(t => t.prototypeIndex >= 0).ToList();
        int primero = ajenos.Count;
        data.treePrototypes = ajenos.Concat(prefabs.Select(p => new TreePrototype { prefab = p })).ToArray();

        var porNombre = new Dictionary<string, int>();
        for (int i = 0; i < especies.Length; i++) porNombre[especies[i].Nombre] = i;

        var rng = new System.Random(7);
        float R() => (float)rng.NextDouble();
        var cuenta = new int[especies.Length];
        foreach (var linea in File.ReadAllLines(Datos).Skip(1))
        {
            var c = linea.Split(',');
            float x = float.Parse(c[0], CultureInfo.InvariantCulture);
            float z = float.Parse(c[1], CultureInfo.InvariantCulture);
            float radio = float.Parse(c[2], CultureInfo.InvariantCulture);
            if (!porNombre.TryGetValue(c[3], out int k)) { Debug.LogWarning($"[Vegetacion] Especie desconocida {c[3]}"); continue; }
            var e = especies[k];

            // La detección mide el radio inscrito en la mancha de copa, algo menor que el real
            float escala = e.Forma == Forma.Palmera ? 0.8f + 0.4f * R()
                : Mathf.Clamp(radio * 1.15f / e.RadioCopa, e.Arbusto ? 0.6f : 0.45f, e.Arbusto ? 1.7f : 1.35f);
            instancias.Add(new TreeInstance
            {
                position = new Vector3(x / size.x, 0f, z / size.z),
                widthScale = escala * (0.92f + 0.16f * R()),
                heightScale = escala * (0.9f + 0.22f * R()),
                rotation = R() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
                prototypeIndex = primero + k,
            });
            cuenta[k]++;
        }

        data.SetTreeInstances(instancias.ToArray(), true);
        terrain.Flush();
        terrain.treeDistance = 2000f;
        var col = terrain.GetComponent<TerrainCollider>();
        if (col != null) { col.enabled = false; col.enabled = true; }    // recalcula los colliders de los árboles
        EditorUtility.SetDirty(data);
        Debug.Log("[Vegetacion] " + string.Join(", ", especies.Select((e, i) => $"{e.Nombre} {cuenta[i]}")));
    }

    // Hierba en todo el suelo natural: seca donde el splatmap marca pasto o tierra, verde en el
    // césped regado y bajo el bosque de ribera
    static void ColocarHierba(Terrain terrain, Texture2D seca, Texture2D verde)
    {
        var data = terrain.terrainData;
        var ajenos = data.detailPrototypes.Where(p => p.prototypeTexture != seca && p.prototypeTexture != verde).ToList();
        int iSeca = ajenos.Count, iVerde = ajenos.Count + 1;

        DetailPrototype Hierba(Texture2D t, Color sana, Color agostada, float alto) => new DetailPrototype
        {
            prototypeTexture = t,
            renderMode = DetailRenderMode.Grass,
            usePrototypeMesh = false,
            healthyColor = sana,
            dryColor = agostada,
            minWidth = 0.3f, maxWidth = 0.5f,
            minHeight = alto * 0.6f, maxHeight = alto,
            noiseSpread = 0.4f,
            density = 2.5f,
        };
        data.detailPrototypes = ajenos.Concat(new[]
        {
            Hierba(seca, new Color(0.92f, 0.88f, 0.78f), new Color(0.85f, 0.78f, 0.62f), 0.55f),
            Hierba(verde, new Color(0.90f, 0.95f, 0.85f), new Color(0.85f, 0.82f, 0.65f), 0.4f),
        }).ToArray();

        var a = CargarSplat(Splat03);
        var b = CargarSplat(Splat47);
        int sres = (int)Mathf.Sqrt(a.Length);

        // Sombra de las copas de hoja caduca
        int res = data.detailResolution;
        var sombra = new bool[res, res];
        foreach (var t in data.treeInstances)
        {
            var nombre = data.treePrototypes[t.prototypeIndex].prefab.name;
            // Solo el bosque de ribera conserva el suelo verde en verano
            if (!(nombre == "Sauce" || nombre == "Alamo")) continue;
            int ci = (int)(t.position.x * res), cj = (int)(t.position.z * res);
            int r = Mathf.CeilToInt(3.2f * t.widthScale / data.size.x * res);
            for (int j = cj - r; j <= cj + r; j++)
            for (int i = ci - r; i <= ci + r; i++)
                if (i >= 0 && j >= 0 && i < res && j < res && (i - ci) * (i - ci) + (j - cj) * (j - cj) <= r * r)
                    sombra[j, i] = true;
        }

        bool cobertura = data.detailScatterMode == DetailScatterMode.CoverageMode;
        var mSeca = new int[res, res];
        var mVerde = new int[res, res];
        int celdas = 0;
        for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++)
        {
            int si = Mathf.Min((int)((i + 0.5f) / res * sres), sres - 1);
            int sj = Mathf.Min((int)((j + 0.5f) / res * sres), sres - 1);
            var p = a[sj * sres + si];
            var q = b[sj * sres + si];
            float pasto = p.r / 255f, tierra = p.g / 255f, cesped = q.r / 255f;
            float artificial = (p.b + q.g + q.b) / 255f;      // jabre, asfalto, pavimento
            if (artificial > 0.3f) continue;
            float verdeW = Mathf.Max(cesped, sombra[j, i] ? 0.6f : 0f);
            float secaW = (pasto + tierra * 0.35f) * (1f - verdeW);
            float ruido = Mathf.PerlinNoise(i * 0.07f, j * 0.07f);
            int Dens(float w, int max) => cobertura ? Mathf.Clamp((int)(w * max * (0.5f + ruido)), 0, 255) : Mathf.RoundToInt(w * 6 * ruido);
            mSeca[j, i] = Dens(secaW, 170);
            mVerde[j, i] = Dens(verdeW, 230);
            if (mSeca[j, i] + mVerde[j, i] > 0) celdas++;
        }
        // Viento suave y sin el tinte anaranjado que el Terrain aplica por defecto a la hierba
        data.wavingGrassTint = new Color(0.9f, 0.9f, 0.88f);
        data.wavingGrassAmount = 0.15f;
        data.wavingGrassStrength = 0.3f;
        data.wavingGrassSpeed = 0.4f;
        data.SetDetailLayer(0, 0, iSeca, mSeca);
        data.SetDetailLayer(0, 0, iVerde, mVerde);
        terrain.detailObjectDistance = 90f;
        EditorUtility.SetDirty(data);
        Debug.Log($"[Vegetacion] Hierba en {celdas} celdas, {QuitarHierbaBajoMallas()} quitadas bajo mallas");
    }

    // Vacía las celdas de hierba que quedan bajo una malla a ras de suelo (aceras, bordillos, pavimentos y
    // escalones de los edificios): el splatmap no siempre las recoge y la hierba atravesaría la malla.
    // Un rayo hacia abajo por celda con hierba; cuenta cualquier MeshCollider entre 0,3 m bajo el
    // terreno y 1 m sobre él.
    public static int QuitarHierbaBajoMallas()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return 0;
        Physics.SyncTransforms();
        var data = terrain.terrainData;
        int res = data.detailResolution, capas = data.detailPrototypes.Length;
        if (capas == 0) return 0;
        Vector3 tp = terrain.transform.position, size = data.size;
        var mapas = new int[capas][,];
        for (int c = 0; c < capas; c++) mapas[c] = data.GetDetailLayer(0, 0, res, res, c);
        int quitadas = 0;
        for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++)
        {
            bool hay = false;
            for (int c = 0; c < capas && !hay; c++) hay = mapas[c][j, i] > 0;
            if (!hay) continue;
            var p = new Vector3(tp.x + (i + 0.5f) * size.x / res, 0, tp.z + (j + 0.5f) * size.z / res);
            float ty = terrain.SampleHeight(p) + tp.y;
            bool tapada = false;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, ty + 2.5f, p.z), Vector3.down, 3.0f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (h.collider is MeshCollider && h.point.y > ty - 0.3f && h.point.y < ty + 1.0f) { tapada = true; break; }
            if (!tapada) continue;
            for (int c = 0; c < capas; c++) mapas[c][j, i] = 0;
            quitadas++;
        }
        if (quitadas > 0)
        {
            for (int c = 0; c < capas; c++) data.SetDetailLayer(0, 0, c, mapas[c]);
            EditorUtility.SetDirty(data);
        }
        return quitadas;
    }

    // PNG con el norte arriba; LoadImage deja la fila 0 al sur, que es el orden del Terrain
    static Color32[] CargarSplat(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return px;
    }

    // ---------- Modelos ----------

    // El LODGroup hace que el Terrain lo trate como árbol de malla (sin él lo toma por un árbol
    // antiguo de Soft Occlusion). LOD1 lleva menos ramas y tarjetas más grandes y se mantiene
    // hasta muy lejos para que el arbolado no desaparezca en las vistas del campus.
    static GameObject CrearPrefab(Especie e, Material extra)
    {
        var go = new GameObject(e.Nombre);
        var lods = new LOD[2];
        float[] corte = { 0.18f, e.Arbusto ? 0.015f : 0.003f };
        var mats = extra != null ? new[] { e.Corteza, e.Hojas, e.HojasInterior, extra } : new[] { e.Corteza, e.Hojas, e.HojasInterior };
        for (int l = 0; l < 2; l++)
        {
            var hijo = new GameObject($"LOD{l}");
            hijo.transform.SetParent(go.transform, false);
            var mesh = e.Forma == Forma.Palmera ? GenerarPalmera(e, l == 1) : GenerarArbol(e, l == 1);
            // Un renderer por material: con varias submallas en un mismo renderer, Unity dibujaba
            // parte de las tarjetas de follaje con el material de la corteza
            var renderers = new List<Renderer>();
            for (int k = 0; k < mesh.subMeshCount; k++)
            {
                var parte = Submalla(mesh, k);
                if (parte == null) continue;
                var pieza = new GameObject(mats[k].name);
                pieza.transform.SetParent(hijo.transform, false);
                pieza.AddComponent<MeshFilter>().sharedMesh = SaveMesh(parte, $"{e.Nombre}_LOD{l}_{k}");
                var mr = pieza.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[k];
                if (e.Arbusto && l == 1) mr.shadowCastingMode = ShadowCastingMode.Off;
                renderers.Add(mr);
            }
            Object.DestroyImmediate(mesh);
            lods[l] = new LOD(corte[l], renderers.ToArray());
        }
        var grupo = go.AddComponent<LODGroup>();
        grupo.SetLODs(lods);
        grupo.RecalculateBounds();
        if (!e.Arbusto)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = e.RadioTronco * 1.3f;
            c.height = e.Fuste + 1f;
            c.center = new Vector3(0f, c.height / 2f, 0f);
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{e.Nombre}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // Extrae una submalla como malla independiente, solo con los vértices que usa
    static Mesh Submalla(Mesh m, int k)
    {
        var tri = m.GetTriangles(k);
        if (tri.Length == 0) return null;
        var v = m.vertices; var n = m.normals; var uv = m.uv; var tg = m.tangents;
        var mapa = new Dictionary<int, int>();
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var ntg = new List<Vector4>();
        var nt = new int[tri.Length];
        for (int i = 0; i < tri.Length; i++)
        {
            if (!mapa.TryGetValue(tri[i], out int j))
            {
                j = nv.Count;
                mapa[tri[i]] = j;
                nv.Add(v[tri[i]]); nn.Add(n[tri[i]]); nuv.Add(uv[tri[i]]); ntg.Add(tg[tri[i]]);
            }
            nt[i] = j;
        }
        var r = new Mesh { name = $"{m.name}_{k}" };
        if (nv.Count > 65000) r.indexFormat = IndexFormat.UInt32;
        r.SetVertices(nv); r.SetNormals(nn); r.SetUVs(0, nuv); r.SetTangents(ntg);
        r.SetTriangles(nt, 0);
        r.RecalculateBounds();
        return r;
    }

    // Árbol procedural: tronco, ramas que crecen desde la estructura más cercana hacia puntos de
    // la copa (colonización del espacio simplificada) y tarjetas de follaje repartidas por la
    // capa exterior de una copa con lóbulos. La forma de la copa depende de la especie.
    static Mesh GenerarArbol(Especie e, bool lejano)
    {
        // Misma semilla en los dos LOD para que la silueta de la copa coincida
        var rng = new System.Random(e.Semilla);
        float R() => (float)rng.NextDouble();
        Vector3 Dir()
        {
            var v = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f);
            return v.sqrMagnitude < 1e-4f ? Vector3.up : v.normalized;
        }

        var mb = new MeshBuilder(e.Forma == Forma.Llorona ? 4 : 3);
        const int Corteza = 0, Hojas = 1, HojasInterior = 2, Cortina = 3;

        var lobulos = Enumerable.Range(0, 6).Select(_ => (dir: Dir(), amp: 0.10f + 0.18f * R())).ToList();
        float FormaCopa(Vector3 d) => 0.88f + lobulos.Sum(l => l.amp * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, l.dir)), 3f));
        var inclinacion = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * e.Inclinacion;
        var centro = new Vector3(0f, e.Arbusto ? e.SemiAltoCopa * 0.85f : e.Fuste + e.SemiAltoCopa * 0.8f, 0f) + inclinacion;
        Vector3 EnCopa(float u)
        {
            var d = Dir();
            // Pino: copa en paraguas, plana por debajo y algo abombada por arriba
            if (e.Forma == Forma.Paraguas) d.y = d.y < 0 ? d.y * 0.35f : d.y;
            float s = FormaCopa(d) * u;
            return centro + new Vector3(d.x * e.RadioCopa * s, d.y * e.SemiAltoCopa * s, d.z * e.RadioCopa * s);
        }

        var nodos = new List<(Vector3 p, float r)>();
        if (!e.Arbusto)
        {
            for (int k = 0; k < e.Troncos; k++)
            {
                // Varios troncos (olivo): salen juntos y se abren
                var abre = e.Troncos > 1 ? new Vector3(Mathf.Cos(k * 2.4f), 0f, Mathf.Sin(k * 2.4f)) * 0.5f : Vector3.zero;
                float alto = e.Forma == Forma.Paraguas ? e.Fuste + e.SemiAltoCopa * 0.3f : e.Fuste + e.SemiAltoCopa * 0.6f;
                var tronco = new List<Vector3>();
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    var p = new Vector3(0f, -0.3f + (alto + 0.3f) * t, 0f) + (inclinacion + abre) * t * t;
                    if (i > 0 && i < 8) p += new Vector3(R() - 0.5f, 0f, R() - 0.5f) * (e.Troncos > 1 ? 0.15f : 0.06f);
                    tronco.Add(p);
                    if (p.y > e.Fuste * 0.8f) nodos.Add((p, Mathf.Lerp(e.RadioTronco, e.RadioTronco * 0.45f, t)));
                }
                mb.Tube(Corteza, tronco, e.RadioTronco * 1.25f, lejano ? 6 : 10, e.RadioTronco * 0.45f);
            }
        }
        else
        {
            // Matas: varios tallos desde el suelo
            for (int s = 0; s < e.Troncos; s++)
            {
                var b = new Vector3(R() - 0.5f, -0.1f, R() - 0.5f) * 0.25f;
                var top = EnCopa(0.4f);
                mb.Tube(Corteza, new[] { b, Vector3.Lerp(b, top, 0.5f) + Dir() * 0.05f, top }, e.RadioTronco, 5, e.RadioTronco * 0.4f);
                nodos.Add((top, e.RadioTronco * 0.5f));
            }
        }

        // Ramas hacia puntos de la copa, de dentro afuera
        int ramas = lejano ? e.Ramas / 3 : e.Ramas;
        var objetivos = Enumerable.Range(0, ramas).Select(_ => EnCopa(0.45f + 0.35f * R())).ToList();
        objetivos.Sort((a, b) => (a - centro).sqrMagnitude.CompareTo((b - centro).sqrMagnitude));
        foreach (var obj in objetivos)
        {
            var origen = nodos.OrderBy(nd => (nd.p - obj).sqrMagnitude).First();
            float largo = Vector3.Distance(origen.p, obj);
            if (largo < 0.15f) continue;
            var medio = Vector3.Lerp(origen.p, obj, 0.5f) + Vector3.up * largo * 0.15f + Dir() * largo * 0.1f;
            var camino = new List<Vector3>();
            for (int i = 0; i <= 4; i++)
            {
                float t = i / 4f;
                camino.Add((1 - t) * (1 - t) * origen.p + 2 * (1 - t) * t * medio + t * t * obj);
            }
            float r0 = Mathf.Min(origen.r * 0.75f, 0.015f + largo * 0.022f);
            float r1 = Mathf.Max(0.008f, r0 * 0.25f);
            mb.Tube(Corteza, camino, r0, e.Arbusto ? 4 : 6, r1);
            for (int i = 1; i <= 4; i++) nodos.Add((camino[i], Mathf.Lerp(r0, r1, i / 4f)));

            if (lejano) continue;
            // Ramillas en la punta
            for (int k = 0; k < 2; k++)
            {
                var dir = ((obj - centro).normalized + Dir() * 0.7f).normalized;
                mb.Tube(Corteza, new[] { obj, obj + dir * (0.2f + 0.3f * R()) * (e.Arbusto ? 0.5f : 1f) }, r1, 4, 0.004f);
            }
        }

        // Follaje
        int tarjetas = lejano ? (int)(e.Tarjetas * 0.4f) : e.Tarjetas;
        float tam = lejano ? e.TamTarjeta * 1.5f : e.TamTarjeta;
        for (int i = 0; i < tarjetas; i++)
        {
            float u = 0.45f + 0.55f * Mathf.Sqrt(R());
            var p = EnCopa(u);
            if (!e.Arbusto && p.y < e.Fuste * 0.95f) p.y = e.Fuste * 0.95f + R() * 0.3f;
            if (e.Arbusto && p.y < 0.15f) p.y = 0.15f + R() * 0.2f;

            var radial = new Vector3((p.x - centro.x) / e.RadioCopa, (p.y - centro.y) / e.SemiAltoCopa, (p.z - centro.z) / e.RadioCopa).normalized;
            var normal = (radial + Vector3.up * 0.35f).normalized;
            var frente = (radial * 0.6f + Dir() * 0.8f).normalized;
            // Pino: los penachos de acículas miran al cielo
            if (e.Forma == Forma.Paraguas) frente = (Vector3.up * 0.7f + radial * 0.5f + Dir() * 0.7f).normalized;
            var a = Vector3.Cross(frente, Dir()).normalized;
            if (a.sqrMagnitude < 0.5f) a = Vector3.Cross(frente, Vector3.up).normalized;
            var b = Vector3.Cross(frente, a);
            float s = tam * (0.75f + 0.5f * R()) * 0.5f;
            mb.Card(u < 0.72f ? HojasInterior : Hojas, p, a * s, b * s, normal, rng.Next(4));
        }

        // Sauce llorón: cortinas de ramillas que cuelgan del borde de la copa
        if (e.Forma == Forma.Llorona)
        {
            int cortinas = lejano ? 14 : 36;
            for (int i = 0; i < cortinas; i++)
            {
                float ang = (i + R() * 0.6f) / cortinas * Mathf.PI * 2f;
                var dirH = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var arriba = centro + dirH * e.RadioCopa * (0.75f + 0.2f * R()) + Vector3.up * e.SemiAltoCopa * (0.1f + 0.3f * R());
                float largo = 2.2f + 1.6f * R();
                var abajo = arriba + Vector3.down * largo + dirH * 0.4f;
                var lado = Vector3.Cross(dirH, Vector3.up) * (0.55f + 0.3f * R());
                var c = (arriba + abajo) / 2f;
                mb.Card(Cortina, c, lado, (arriba - abajo) / 2f, (dirH + Vector3.up * 0.3f).normalized, 0);
            }
        }

        return mb.ToMesh($"{e.Nombre}_LOD{(lejano ? 1 : 0)}");
    }

    // Palmera de abanico (Washingtonia, Street View A1): estípite con la base ensanchada, falda de
    // hojas secas bajo la corona y hojas en abanico con su pecíolo
    static Mesh GenerarPalmera(Especie e, bool lejano)
    {
        var rng = new System.Random(e.Semilla);
        float R() => (float)rng.NextDouble();
        var mb = new MeshBuilder(3);
        const int Corteza = 0, Hojas = 1, HojasSecas = 2;

        var curva = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.6f;
        var tronco = new List<Vector3>();
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            tronco.Add(new Vector3(0f, -0.3f + (e.Fuste + 0.3f) * t, 0f) + curva * t * t);
        }
        mb.Tube(Corteza, tronco.Take(3).ToList(), e.RadioTronco * 1.6f, lejano ? 6 : 12, e.RadioTronco);
        mb.Tube(Corteza, tronco.Skip(2).ToList(), e.RadioTronco, lejano ? 6 : 12, e.RadioTronco * 0.85f);
        var copa = tronco[10];

        // Falda de hojas secas colgando bajo la corona (abanicos pardos)
        int falda = lejano ? 6 : 16;
        for (int i = 0; i < falda; i++)
        {
            float ang = (i + R() * 0.5f) / falda * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            var c = copa + Vector3.down * (0.9f + 0.4f * R()) + d * (e.RadioTronco + 0.12f);
            mb.Card(HojasSecas, c, Vector3.Cross(d, Vector3.up) * 0.5f, (Vector3.up - d * 0.25f).normalized * -0.8f, d, 0);
        }

        // Corona: pecíolos que salen en abanico y abanicos casi verticales mirando hacia fuera;
        // los de abajo, caídos
        int hojas = lejano ? 12 : e.Tarjetas;
        for (int i = 0; i < hojas; i++)
        {
            float ang = (i * 2.39996f) % (Mathf.PI * 2f);          // ángulo áureo
            float elev = Mathf.Lerp(70f, -25f, (i + 0.5f) / hojas + (R() - 0.5f) * 0.1f) * Mathf.Deg2Rad;
            var d = new Vector3(Mathf.Cos(ang) * Mathf.Cos(elev), Mathf.Sin(elev), Mathf.Sin(ang) * Mathf.Cos(elev));
            float pec = 0.9f + 0.5f * R();
            var fin = copa + d * pec;
            if (!lejano) mb.Tube(Corteza, new[] { copa, copa + d * pec * 0.5f + Vector3.down * 0.05f, fin }, 0.035f, 4, 0.02f);
            var horiz = new Vector3(d.x, 0f, d.z).normalized;
            var lado = Vector3.Cross(Vector3.up, horiz).normalized;
            // El abanico se abre a lo largo del pecíolo, inclinado hacia arriba
            var eje = (d + Vector3.up * 0.35f).normalized;
            var c = fin + eje * e.TamTarjeta * 0.45f;
            var normal = Vector3.Cross(lado, eje).normalized;
            mb.Card(Hojas, c, lado * e.TamTarjeta * 0.6f, eje * e.TamTarjeta * 0.5f, normal, 0);
        }
        return mb.ToMesh($"{e.Nombre}_LOD{(lejano ? 1 : 0)}");
    }

    // ---------- Texturas procedurales ----------

    // Racimo de hojas sobre fondo transparente; las del centro, más oscuras (oclusión)
    static Color32[] PintarHojas(int size, int semilla, Color baseCol, float varTono, int cuantas, float largo, float ancho,
        Color? flor, int flores, bool ramillas)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol * 0.7f);

        float c = size / 2f, radio = size * 0.44f;
        if (ramillas)
            for (int k = 0; k < 6; k++)
            {
                float ang = k * Mathf.PI / 3f + R() * 0.5f;
                Linea(px, size, new Vector2(c, c), new Vector2(c + Mathf.Cos(ang) * radio * 0.55f, c + Mathf.Sin(ang) * radio * 0.55f),
                    1.6f, new Color(0.30f, 0.24f, 0.17f));
            }

        Color.RGBToHSV(baseCol, out float h0, out float s0, out float v0);
        for (int n = 0; n < cuantas; n++)
        {
            float th = R() * Mathf.PI * 2f, rho = radio * Mathf.Sqrt(R());
            var pos = new Vector2(c + Mathf.Cos(th) * rho, c + Mathf.Sin(th) * rho);
            float ang = th + (R() - 0.5f) * 2.8f;
            float h = Mathf.Repeat(h0 + (R() - 0.5f) * varTono, 1f);
            float s = Mathf.Clamp01(s0 * (0.85f + 0.3f * R()));
            float v = Mathf.Clamp01(v0 * (0.75f + 0.45f * R()) * (0.6f + 0.4f * rho / radio));
            Hoja(px, size, pos, ang, largo * (0.75f + 0.5f * R()), ancho * (0.75f + 0.5f * R()), Color.HSVToRGB(h, s, v));
        }
        if (flor.HasValue)
            for (int n = 0; n < flores; n++)
            {
                float th = R() * Mathf.PI * 2f, rho = radio * (0.4f + 0.6f * Mathf.Sqrt(R()));
                var pos = new Vector2(c + Mathf.Cos(th) * rho, c + Mathf.Sin(th) * rho);
                for (int p = 0; p < 5; p++)
                {
                    float a = p * Mathf.PI * 0.4f + R();
                    Hoja(px, size, pos, a, 9f, 7f, flor.Value * (0.85f + 0.2f * R()));
                }
            }
        return px;
    }

    // Penachos de acículas de pino que salen de varios ramillos
    static Color32[] PintarAciculas(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol * 0.7f);
        float c = size / 2f;
        for (int k = 0; k < 16; k++)
        {
            var centro = new Vector2(c + (R() - 0.5f) * size * 0.6f, c + (R() - 0.5f) * size * 0.6f);
            for (int n = 0; n < 90; n++)
            {
                float ang = R() * Mathf.PI * 2f;
                float l = size * (0.12f + 0.1f * R());
                var col = baseCol * (0.7f + 0.5f * R());
                col.a = 1f;
                Linea(px, size, centro, centro + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * l, 1.1f, col);
            }
        }
        return px;
    }

    // Varas finas y verticales de retama
    static Color32[] PintarVaras(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol * 0.7f);
        for (int n = 0; n < 160; n++)
        {
            var a = new Vector2(size * (0.3f + 0.4f * R()), size * 0.05f);
            float ang = Mathf.PI / 2f + (R() - 0.5f) * 1.1f;
            var b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * size * (0.5f + 0.4f * R());
            var col = baseCol * (0.75f + 0.4f * R());
            col.a = 1f;
            Linea(px, size, a, b, 1.4f, col);
        }
        return px;
    }

    // Ramillas colgantes de sauce: tiras verticales de hojas estrechas
    static Color32[] PintarCortina(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol * 0.7f);
        Color.RGBToHSV(baseCol, out float h0, out float s0, out float v0);
        for (int k = 0; k < 18; k++)
        {
            float x = size * (0.05f + 0.9f * R());
            float largo = size * (0.6f + 0.4f * R());
            for (float y = size; y > size - largo; y -= 7f)
            {
                float yy = y - 1f;
                var col = Color.HSVToRGB(h0, s0 * (0.8f + 0.3f * R()), v0 * (0.7f + 0.5f * R()));
                float sway = Mathf.Sin(y * 0.02f + k) * 6f;
                Hoja(px, size, new Vector2(x + sway, yy), -Mathf.PI / 2f + (R() - 0.5f) * 0.9f, 22f, 4f, col);
            }
        }
        return px;
    }

    // Hoja de palmera en abanico: segmentos radiales desde el pecíolo (abajo, centro)
    static Color32[] PintarAbanico(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol * 0.7f);
        var origen = new Vector2(size / 2f, size * 0.08f);
        int segmentos = 38;
        for (int k = 0; k < segmentos; k++)
        {
            float ang = Mathf.Lerp(0.15f, Mathf.PI - 0.15f, k / (segmentos - 1f));
            float largo = size * (0.75f + 0.15f * R());
            var col = baseCol * (0.75f + 0.4f * R());
            col.a = 1f;
            Hoja(px, size, origen, ang, largo, 16f, col);
        }
        return px;
    }

    static Color32[] Vacia(int size, Color fondoCol)
    {
        var px = new Color32[size * size];
        var fondo = (Color32)fondoCol;
        fondo.a = 0;
        for (int i = 0; i < px.Length; i++) px[i] = fondo;
        return px;
    }

    // Hoja lanceolada que nace en "pos" y crece hacia "ang", con nervio central
    static void Hoja(Color32[] px, int size, Vector2 pos, float ang, float largo, float ancho, Color col)
    {
        var u = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        var v = new Vector2(-u.y, u.x);
        var fin = pos + u * largo;
        int x0 = Mathf.Max(0, (int)(Mathf.Min(pos.x, fin.x) - ancho)), x1 = Mathf.Min(size - 1, (int)(Mathf.Max(pos.x, fin.x) + ancho));
        int y0 = Mathf.Max(0, (int)(Mathf.Min(pos.y, fin.y) - ancho)), y1 = Mathf.Min(size - 1, (int)(Mathf.Max(pos.y, fin.y) + ancho));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var d = new Vector2(x + 0.5f, y + 0.5f) - pos;
            float a = Vector2.Dot(d, u), b = Vector2.Dot(d, v);
            if (a < 0f || a > largo) continue;
            float medio = ancho * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * a / largo), 0.7f);
            if (Mathf.Abs(b) > medio) continue;
            float sombra = (Mathf.Abs(b) < 0.7f && largo > 14f) ? 0.8f : 0.9f + 0.2f * (b / medio * 0.5f + 0.5f);
            px[y * size + x] = new Color(col.r * sombra, col.g * sombra, col.b * sombra, 1f);
        }
    }

    static void Linea(Color32[] px, int size, Vector2 a, Vector2 b, float grosor, Color col)
    {
        int pasos = Mathf.Max(1, (int)(Vector2.Distance(a, b) * 2f));
        for (int i = 0; i <= pasos; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)pasos);
            float g = grosor * (1f - 0.6f * i / pasos);
            for (int y = (int)(p.y - g); y <= (int)(p.y + g); y++)
            for (int x = (int)(p.x - g); x <= (int)(p.x + g); x++)
                if (x >= 0 && y >= 0 && x < size && y < size && (new Vector2(x, y) - p).sqrMagnitude <= g * g)
                    px[y * size + x] = col;
        }
    }

    // Mata de hierba: hojas finas que salen de la base
    static Color32[] PintarHierba(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = Vacia(size, baseCol);
        for (int n = 0; n < 70; n++)
        {
            float x0 = size * (0.25f + 0.5f * R());
            float alto = size * (0.4f + 0.58f * R());
            // Las hojas se abren hacia fuera de la mata
            float curva = ((x0 / size - 0.5f) * 0.5f + (R() - 0.5f) * 0.25f) * size;
            Color.RGBToHSV(baseCol, out float h, out float sat, out float val);
            var col = Color.HSVToRGB(Mathf.Repeat(h + (R() - 0.5f) * 0.06f, 1f), sat * (0.7f + 0.5f * R()), val * (0.7f + 0.4f * R()));
            col.a = 1f;
            int pasos = (int)alto;
            for (int i = 0; i < pasos; i++)
            {
                float t = i / (float)pasos;
                float x = x0 + curva * t * t;
                float g = 1.3f * (1f - t) + 0.35f;
                var sombra = col * (0.55f + 0.45f * Mathf.Sqrt(t));
                sombra.a = 1f;
                for (int dx = (int)(x - g); dx <= (int)(x + g); dx++)
                    if (dx >= 0 && dx < size && i < size)
                        px[i * size + dx] = sombra;
            }
        }
        return px;
    }

    // ---------- Assets ----------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    static Texture2D GuardarTextura(string nombre, Color32[] px, bool alfa)
    {
        int size = (int)Mathf.Sqrt(px.Length);
        string path = $"{TexDir}/{nombre}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = alfa;
        imp.mipmapEnabled = true;
        // Mantiene la densidad del follaje en los mipmaps lejanos
        imp.mipMapsPreserveCoverage = alfa;
        imp.alphaTestReferenceValue = 0.5f;
        imp.wrapMode = alfa ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string nombre)
    {
        string path = $"{MatDir}/{nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.enableInstancing = true;
        return mat;
    }

    static Texture2D Importar(string path, TextureImporterType tipo, bool srgb)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) { Debug.LogError($"[Vegetacion] No encuentro {path}"); return null; }
        if (imp.textureType != tipo || imp.sRGBTexture != srgb || imp.maxTextureSize != 1024)
        {
            imp.textureType = tipo;
            imp.sRGBTexture = srgb;
            imp.maxTextureSize = 1024;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Las UV del tronco van en vueltas (U) y metros (V); la textura cubre "metros" de corteza
    static Material MatCorteza(string nombre, string textura, float metros)
    {
        var mat = Mat(nombre);
        mat.SetTexture("_BaseMap", Importar($"{TexDir}/{textura}_diff.jpg", TextureImporterType.Default, true));
        mat.SetTexture("_BumpMap", Importar($"{TexDir}/{textura}_normal.jpg", TextureImporterType.NormalMap, false));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.08f);
        mat.SetTextureScale("_BaseMap", new Vector2(1f, 1f / metros));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Follaje recortado por alfa y visible por las dos caras
    static Material MatHojas(string nombre, Texture2D tex, Color color)
    {
        var mat = Mat(nombre);
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.12f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.doubleSidedGI = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh SaveMesh(Mesh mesh, string nombre)
    {
        string path = $"{MeshDir}/{nombre}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
