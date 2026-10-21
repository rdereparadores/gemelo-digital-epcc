using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Genera árboles, arbustos y hierba para la Av. de las Letras y los coloca en el Terrain como
// árboles y detalles (los mismos que usan las herramientas Paint Trees y Paint Details).
//
// Las posiciones y tamaños de copa salen de Assets/Vegetacion/Datos/vegetacion_avenida.csv, que
// genera Herramientas/detectar_vegetacion.py a partir de la ortofoto PNOA.
public static class VegetacionAvenidaBuilder
{
    const string Root = "Assets/Vegetacion";
    const string TexDir = Root + "/Texturas";
    const string MatDir = Root + "/Materiales";
    const string MeshDir = Root + "/Mallas";
    const string PrefabDir = Root + "/Prefabs";
    const string Datos = Root + "/Datos/vegetacion_avenida.csv";

    // Mediana ajardinada de la avenida (polígonos de OSM, en metros del Terrain)
    static readonly Vector2[][] Medianas =
    {
        new[] { V(226, 709), V(224, 700), V(525, 615), V(528, 616), V(528, 619), V(527, 622) },
        new[] { V(545, 617), V(542, 616), V(541, 613), V(543, 610), V(633, 584), V(635, 585), V(636, 588), V(634, 592) },
    };

    static Vector2 V(float x, float z) => new Vector2(x, z);

    class Especie
    {
        public string Nombre;
        public int Semilla;
        public bool Arbusto;
        public float Fuste;           // altura del tronco limpio hasta la cruz
        public float RadioTronco;
        public float RadioCopa;       // horizontal
        public float SemiAltoCopa;    // vertical
        public int Ramas;
        public int Tarjetas;
        public float TamTarjeta;
        public Material Corteza, Hojas, HojasInterior;
    }

    [MenuItem("Tools/Vegetación Avenida/Generar árboles y colocar en el Terrain")]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[Vegetacion] No hay Terrain activo"); return; }
        if (!File.Exists(Datos)) { Debug.LogError($"[Vegetacion] Falta {Datos}: ejecuta Herramientas/detectar_vegetacion.py"); return; }

        foreach (var d in new[] { Root, TexDir, MatDir, MeshDir, PrefabDir }) EnsureFolder(d);

        // Texturas
        var texCaduca = GuardarTextura("Hojas_Caduca", PintarHojas(512, 11, new Color(0.47f, 0.62f, 0.22f), 0.035f, 300, 34f, 13f, null, 0, true), true);
        var texEncina = GuardarTextura("Hojas_Encina", PintarHojas(512, 23, new Color(0.25f, 0.31f, 0.17f), 0.03f, 650, 18f, 9f, null, 0, true), true);
        var texArbusto = GuardarTextura("Hojas_Arbusto", PintarHojas(512, 37, new Color(0.30f, 0.39f, 0.19f), 0.04f, 600, 16f, 7f, null, 0, false), true);
        var texAdelfa = GuardarTextura("Hojas_Adelfa", PintarHojas(512, 41, new Color(0.27f, 0.38f, 0.20f), 0.03f, 380, 32f, 7f, new Color(0.93f, 0.55f, 0.70f), 70, false), true);
        var texCorteza = GuardarTextura("Corteza", PintarCorteza(256, 5), false);
        var texHierbaSeca = GuardarTextura("Hierba_Seca", PintarHierba(256, 51, new Color(0.82f, 0.78f, 0.64f)), true);
        var texHierbaVerde = GuardarTextura("Hierba_Verde", PintarHierba(256, 52, new Color(0.50f, 0.60f, 0.32f)), true);

        // Materiales
        var cortezaClara = MatCorteza("Corteza_Clara", texCorteza, new Color(0.55f, 0.50f, 0.44f));
        var cortezaOscura = MatCorteza("Corteza_Oscura", texCorteza, new Color(0.36f, 0.32f, 0.28f));
        (Material, Material) Follaje(string n, Texture2D t) =>
            (MatHojas($"Follaje_{n}", t, Color.white), MatHojas($"Follaje_{n}_Interior", t, new Color(0.62f, 0.64f, 0.6f)));
        var (caduca, caducaIn) = Follaje("Caduca", texCaduca);
        var (encina, encinaIn) = Follaje("Encina", texEncina);
        var (arbusto, arbustoIn) = Follaje("Arbusto", texArbusto);
        var (adelfa, adelfaIn) = Follaje("Adelfa", texAdelfa);

        // Especies. Medidas de referencia; cada instancia se escala según su copa en la ortofoto.
        var especies = new[]
        {
            new Especie { Nombre = "Arbol_Alineacion_A", Semilla = 101, Fuste = 2.6f, RadioTronco = 0.16f, RadioCopa = 3.0f, SemiAltoCopa = 2.7f,
                Ramas = 34, Tarjetas = 260, TamTarjeta = 1.4f, Corteza = cortezaClara, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Arbol_Alineacion_B", Semilla = 202, Fuste = 2.3f, RadioTronco = 0.15f, RadioCopa = 2.7f, SemiAltoCopa = 3.1f,
                Ramas = 30, Tarjetas = 240, TamTarjeta = 1.35f, Corteza = cortezaClara, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Arbol_Joven", Semilla = 303, Fuste = 2.0f, RadioTronco = 0.07f, RadioCopa = 1.4f, SemiAltoCopa = 1.7f,
                Ramas = 14, Tarjetas = 90, TamTarjeta = 0.95f, Corteza = cortezaClara, Hojas = caduca, HojasInterior = caducaIn },
            new Especie { Nombre = "Encina", Semilla = 404, Fuste = 1.5f, RadioTronco = 0.2f, RadioCopa = 2.6f, SemiAltoCopa = 2.1f,
                Ramas = 30, Tarjetas = 300, TamTarjeta = 1.1f, Corteza = cortezaOscura, Hojas = encina, HojasInterior = encinaIn },
            new Especie { Nombre = "Arbusto", Semilla = 505, Arbusto = true, RadioTronco = 0.03f, RadioCopa = 0.9f, SemiAltoCopa = 0.7f,
                Ramas = 6, Tarjetas = 70, TamTarjeta = 0.7f, Corteza = cortezaOscura, Hojas = arbusto, HojasInterior = arbustoIn },
            new Especie { Nombre = "Adelfa", Semilla = 606, Arbusto = true, RadioTronco = 0.03f, RadioCopa = 0.9f, SemiAltoCopa = 0.9f,
                Ramas = 7, Tarjetas = 75, TamTarjeta = 0.75f, Corteza = cortezaOscura, Hojas = adelfa, HojasInterior = adelfaIn },
        };
        var prefabs = especies.Select(CrearPrefab).ToArray();

        ColocarArboles(terrain, especies, prefabs);
        ColocarHierba(terrain, texHierbaSeca, texHierbaVerde);

        AssetDatabase.SaveAssets();
    }

    // ---------- Colocación ----------

    static void ColocarArboles(Terrain terrain, Especie[] especies, GameObject[] prefabs)
    {
        var data = terrain.terrainData;
        var size = data.size;

        // Conserva los prototipos e instancias ajenos; los nuestros siempre van al final
        var propios = new HashSet<GameObject>(prefabs);
        var ajenos = data.treePrototypes.Where(p => !propios.Contains(p.prefab)).ToList();
        var instancias = data.treeInstances.Where(t => t.prototypeIndex < data.treePrototypes.Length &&
                                                     !propios.Contains(data.treePrototypes[t.prototypeIndex].prefab)).ToList();
        int primero = ajenos.Count;
        data.treePrototypes = ajenos.Concat(prefabs.Select(p => new TreePrototype { prefab = p })).ToArray();

        int Indice(string nombre) => primero + System.Array.FindIndex(especies, e => e.Nombre == nombre);

        var rng = new System.Random(7);
        float R() => (float)rng.NextDouble();
        int n = 0;
        foreach (var linea in File.ReadAllLines(Datos).Skip(1))
        {
            var c = linea.Split(',');
            float x = float.Parse(c[0], CultureInfo.InvariantCulture);
            float z = float.Parse(c[1], CultureInfo.InvariantCulture);
            float radio = float.Parse(c[2], CultureInfo.InvariantCulture);
            bool claro = c[3] == "claro";

            // La detección mide el radio inscrito en la mancha de copa, algo menor que el real
            string especie;
            float escala;
            if (claro && radio >= 2.2f)
            {
                especie = R() < 0.5f ? "Arbol_Alineacion_A" : "Arbol_Alineacion_B";
                escala = Mathf.Clamp(radio * 1.1f / 2.9f, 0.65f, 1.3f);
            }
            else if (claro)
            {
                especie = "Arbol_Joven";
                escala = Mathf.Clamp(radio / 1.4f, 0.7f, 1.5f);
            }
            else if (radio >= 1.4f)
            {
                especie = "Encina";
                escala = Mathf.Clamp(radio * 1.1f / 2.6f, 0.45f, 1.25f);
            }
            else
            {
                especie = R() < 0.3f ? "Adelfa" : "Arbusto";
                escala = Mathf.Clamp(radio / 0.9f, 0.6f, 1.6f);
            }

            instancias.Add(new TreeInstance
            {
                position = new Vector3(x / size.x, 0f, z / size.z),
                widthScale = escala * (0.92f + 0.16f * R()),
                heightScale = escala * (0.9f + 0.22f * R()),
                rotation = R() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
                prototypeIndex = Indice(especie),
            });
            n++;
        }

        data.SetTreeInstances(instancias.ToArray(), true);
        terrain.Flush();
        // Recalcula los colliders de los árboles
        var col = terrain.GetComponent<TerrainCollider>();
        if (col != null) { col.enabled = false; col.enabled = true; }
        EditorUtility.SetDirty(data);
        Debug.Log($"[Vegetacion] {n} árboles y arbustos colocados en el Terrain");
    }

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

        // Bajo las copas grandes la mediana está regada y verde; al sol, agostada
        var sombras = data.treeInstances
            .Where(t => data.treePrototypes[t.prototypeIndex].prefab.name.StartsWith("Arbol_Alineacion"))
            .Select(t => new Vector2(t.position.x * data.size.x, t.position.z * data.size.z))
            .ToList();

        // El Terrain usa dispersión por cobertura: cada celda va de 0 a 255
        bool cobertura = data.detailScatterMode == DetailScatterMode.CoverageMode;
        int res = data.detailResolution;
        var mSeca = new int[res, res];
        var mVerde = new int[res, res];
        var rng = new System.Random(9);
        int celdas = 0;
        for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++)
        {
            var p = new Vector2((i + 0.5f) * data.size.x / res, (j + 0.5f) * data.size.z / res);
            if (!Medianas.Any(poly => Dentro(poly, p))) continue;
            celdas++;
            bool sombra = sombras.Any(s => (s - p).sqrMagnitude < 3.5f * 3.5f);
            if (sombra) mVerde[j, i] = cobertura ? 170 + rng.Next(85) : 5 + rng.Next(5);
            else mSeca[j, i] = cobertura ? 50 + rng.Next(100) : 2 + rng.Next(4);
        }
        // Viento suave y sin el tinte anaranjado que el Terrain aplica por defecto a la hierba
        data.wavingGrassTint = new Color(0.9f, 0.9f, 0.88f);
        data.wavingGrassAmount = 0.15f;
        data.wavingGrassStrength = 0.3f;
        data.wavingGrassSpeed = 0.4f;
        data.SetDetailLayer(0, 0, iSeca, mSeca);
        data.SetDetailLayer(0, 0, iVerde, mVerde);
        EditorUtility.SetDirty(data);
        Debug.Log($"[Vegetacion] Hierba pintada en {celdas} celdas de la mediana");
    }

    static bool Dentro(Vector2[] poly, Vector2 p)
    {
        bool dentro = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                dentro = !dentro;
        return dentro;
    }

    // ---------- Modelos ----------

    // El LODGroup hace que el Terrain lo trate como árbol de malla (sin él lo toma por un árbol
    // antiguo de Soft Occlusion). LOD1 lleva menos ramas y tarjetas más grandes.
    static GameObject CrearPrefab(Especie e)
    {
        var go = new GameObject(e.Nombre);
        var lods = new LOD[2];
        float[] corte = { 0.12f, e.Arbusto ? 0.03f : 0.015f };
        for (int l = 0; l < 2; l++)
        {
            var hijo = new GameObject($"LOD{l}");
            hijo.transform.SetParent(go.transform, false);
            hijo.AddComponent<MeshFilter>().sharedMesh = SaveMesh(GenerarArbol(e, l == 1), $"{e.Nombre}_LOD{l}");
            var mr = hijo.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { e.Corteza, e.Hojas, e.HojasInterior };
            lods[l] = new LOD(corte[l], new Renderer[] { mr });
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

    // Árbol procedural: tronco, ramas que crecen desde la estructura más cercana hacia puntos de
    // la copa (colonización del espacio simplificada) y tarjetas de follaje repartidas por la
    // capa exterior de una copa elipsoidal con lóbulos.
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

        var mb = new MeshBuilder(3);
        const int Corteza = 0, Hojas = 1, HojasInterior = 2;

        var lobulos = Enumerable.Range(0, 6).Select(_ => (dir: Dir(), amp: 0.10f + 0.18f * R())).ToList();
        float Forma(Vector3 d) => 0.88f + lobulos.Sum(l => l.amp * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, l.dir)), 3f));
        var centro = new Vector3(0f, e.Arbusto ? e.SemiAltoCopa * 0.85f : e.Fuste + e.SemiAltoCopa * 0.8f, 0f);
        Vector3 EnCopa(float u)
        {
            var d = Dir();
            float s = Forma(d) * u;
            return centro + new Vector3(d.x * e.RadioCopa * s, d.y * e.SemiAltoCopa * s, d.z * e.RadioCopa * s);
        }

        var nodos = new List<(Vector3 p, float r)>();
        if (!e.Arbusto)
        {
            var inclinacion = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.35f;
            float alto = e.Fuste + e.SemiAltoCopa * 0.6f;
            var tronco = new List<Vector3>();
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                var p = new Vector3(0f, -0.3f + (alto + 0.3f) * t, 0f) + inclinacion * t * t;
                if (i > 0 && i < 6) p += new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.06f;
                tronco.Add(p);
                if (p.y > e.Fuste * 0.8f) nodos.Add((p, Mathf.Lerp(e.RadioTronco, e.RadioTronco * 0.45f, t)));
            }
            mb.Tube(Corteza, tronco, e.RadioTronco * 1.25f, 10, e.RadioTronco * 0.45f);
        }
        else
        {
            // Matas: varios tallos desde el suelo
            for (int s = 0; s < 4; s++)
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
            var a = Vector3.Cross(frente, Dir()).normalized;
            if (a.sqrMagnitude < 0.5f) a = Vector3.Cross(frente, Vector3.up).normalized;
            var b = Vector3.Cross(frente, a);
            float s = tam * (0.75f + 0.5f * R()) * 0.5f;
            mb.Card(u < 0.72f ? HojasInterior : Hojas, p, a * s, b * s, normal, rng.Next(4));
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
        var px = new Color32[size * size];
        var fondo = (Color32)(baseCol * 0.7f);
        fondo.a = 0;
        for (int i = 0; i < px.Length; i++) px[i] = fondo;

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
        int pasos = (int)(Vector2.Distance(a, b) * 2f);
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

    // Corteza gris neutra con surcos verticales; el color lo pone el material
    static Color32[] PintarCorteza(int size, int semilla)
    {
        var px = new Color32[size * size];
        float off = semilla * 13.7f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Ruido periódico en X para que no se note la costura del tronco
            float ang = x / (float)size * Mathf.PI * 2f;
            float cx = Mathf.Cos(ang) * 3f + off, cy = Mathf.Sin(ang) * 3f + off;
            float surcos = Mathf.PerlinNoise(cx + y * 0.004f, cy + y * 0.015f);
            float fino = Mathf.PerlinNoise(cx * 4f + y * 0.05f, cy * 4f + y * 0.08f);
            float v = Mathf.Clamp01(0.55f + 0.5f * (surcos - 0.5f) * 1.6f + 0.25f * (fino - 0.5f));
            v = Mathf.Lerp(0.45f, 1f, v);
            px[y * size + x] = new Color(v, v, v, 1f);
        }
        return px;
    }

    // Mata de hierba: hojas finas que salen de la base
    static Color32[] PintarHierba(int size, int semilla, Color baseCol)
    {
        var rng = new System.Random(semilla);
        float R() => (float)rng.NextDouble();
        var px = new Color32[size * size];
        var fondo = (Color32)baseCol;
        fondo.a = 0;
        for (int i = 0; i < px.Length; i++) px[i] = fondo;
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

    static Material MatCorteza(string nombre, Texture2D tex, Color color)
    {
        var mat = Mat(nombre);
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.1f);
        mat.SetTextureScale("_BaseMap", new Vector2(1f, 0.8f));
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
