using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Genera los prefabs del parking norte de la Escuela Politécnica (pérgola grande junto a la
// Av. de las Letras) y los coloca sobre el Terrain, adaptando cada pieza a la pendiente.
//
// Medidas sacadas de la ortofoto PNOA (Referencias/06_IGN_datos_oficiales) y de OSM.
// Marco local del parking: X a lo largo de la pérgola (hacia el ESE), Z hacia el NNE (avenida),
// origen en el centro de la cubierta.
public static class ParkingNorteBuilder
{
    const string Root = "Assets/_Ambientacion/Parking";
    const string MatDir = Root + "/Materiales";
    const string MeshDir = Root + "/Mallas";
    const string PrefabDir = Root + "/Prefabs";

    // Centro de la cubierta en coordenadas del Terrain y giro del eje largo
    static readonly Vector2 Centro = new Vector2(368.15f, 631.05f);
    const float Rumbo = 15.7f;

    // Pérgola: 18 módulos de 5 m = 90 m, cubierta de 9.5 m de ancho
    const int Modulos = 18;
    const float LargoModulo = 5f;
    const float SemiAncho = 4.75f;
    const float AlturaValle = 2.55f;   // cubierta en mariposa: más baja en el centro
    const float SubidaBorde = 0.40f;

    const float AlturaBordillo = 0.15f;
    const float Zocalo = 0.4f;         // lo que se hunde cada pieza para no dejar huecos con el terreno

    static float RoofY(float z) => AlturaValle + SubidaBorde * (z / SemiAncho) * (z / SemiAncho);

    [MenuItem("Tools/Parking Norte/Generar prefabs y colocar")]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[ParkingNorte] No hay Terrain activo"); return; }

        EnsureFolder(Root); EnsureFolder(MatDir); EnsureFolder(MeshDir); EnsureFolder(PrefabDir);

        // Materiales
        var acero = Mat("Acero_Pergola", new Color(0.45f, 0.47f, 0.47f), 0.6f, 0.4f);
        var chapaSup = Mat("Chapa_Superior", new Color(0.88f, 0.88f, 0.86f), 0.2f, 0.3f);
        var chapaInf = Mat("Chapa_Inferior", new Color(0.62f, 0.60f, 0.56f), 0.3f, 0.3f);
        var hormigon = Mat("Hormigon", new Color(0.70f, 0.70f, 0.68f), 0f, 0.15f);
        // Pavimentos y pintura: los mismos materiales con textura que la red viaria
        MaterialesCampus.Build();
        var bordillo = MatCampus("Bordillo");
        var acera = MatCampus("Acera");
        var isleta = Mat("Baldosa_Isleta", new Color(0.76f, 0.72f, 0.69f), 0f, 0.15f);
        var mediana = Mat("Pavimento_Mediana", new Color(0.55f, 0.55f, 0.55f), 0f, 0.15f);
        var pintura = MatCampus("Pintura");
        var farolaMetal = Mat("Farola_Metal", new Color(0.55f, 0.57f, 0.58f), 0.6f, 0.5f);
        var luminaria = Mat("Farola_Luminaria", new Color(0.75f, 0.75f, 0.75f), 0.3f, 0.6f);

        // Prefabs base
        var pModulo = MakePrefab("Pergola_Modulo", SaveMesh(PergolaModuloMesh(), "Pergola_Modulo"),
            new[] { acero, chapaSup, chapaInf, hormigon }, true, go =>
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.radius = 0.25f; c.height = RoofY(0f); c.center = new Vector3(0, RoofY(0f) / 2f, 0);
            });

        var pAcera = MakePrefab("Acera_Tramo_2m", SaveMesh(TramoMesh("Acera_Tramo_2m", 3.3f, false), "Acera_Tramo_2m"),
            new[] { bordillo, acera }, true, go => AddBox(go, 3.3f));
        var pAceraEstrecha = MakePrefab("Acera_Tramo_2m_Estrecha", SaveMesh(TramoMesh("Acera_Tramo_2m_Estrecha", 1.5f, false), "Acera_Tramo_2m_Estrecha"),
            new[] { bordillo, acera }, true, go => AddBox(go, 1.5f));
        var pMediana = MakePrefab("Mediana_Tramo_2m", SaveMesh(TramoMesh("Mediana_Tramo_2m", 3.5f, true), "Mediana_Tramo_2m"),
            new[] { bordillo, mediana }, true, go => AddBox(go, 3.5f));

        GameObject Isleta(string name, float r, float l) =>
            MakePrefab(name, SaveMesh(IsletaMesh(name, r, l), name), new[] { bordillo, isleta }, true,
                go => go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh);
        var pIsletaPergO = Isleta("Isleta_Pergola_Oeste", 5f, 5f);
        var pIsletaPergE = Isleta("Isleta_Pergola_Este", 5f, 0f);
        var pIsletaMedO = Isleta("Isleta_Mediana_Oeste", 5.8f, 5.4f);
        var pIsletaMedE = Isleta("Isleta_Mediana_Este", 5.75f, 1.95f);

        var pLinea = MakePrefab("Linea_Plaza", SaveMesh(LineaMesh(), "Linea_Plaza"), new[] { pintura }, false, null);
        var pFlecha = MakePrefab("Flecha_Recta", SaveMesh(FlechaMesh(), "Flecha_Recta"), new[] { pintura }, false, null);
        var pPaso = MakePrefab("Paso_Peatones_6m", SaveMesh(PasoMesh(), "Paso_Peatones_6m"), new[] { pintura }, false, null);
        var pFarola = MakePrefab("Farola", SaveMesh(FarolaMesh(), "Farola"), new[] { farolaMetal, luminaria }, true, go =>
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = 0.15f; c.height = 9f; c.center = new Vector3(0, 4.5f, 0);
        });

        // Montaje en la escena
        var old = GameObject.Find("Parking_Norte");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("Parking_Norte");
        root.transform.SetPositionAndRotation(new Vector3(Centro.x, 0f, Centro.y), Quaternion.Euler(0f, Rumbo, 0f));
        var placer = new Placer(terrain, root.transform);

        var gPergola = placer.Group("Pergola");
        placer.Row(pModulo, gPergola, -Modulos * LargoModulo / 2f, Modulos * LargoModulo / 2f, Modulos, 0f, Vector3.one);

        var gIsletas = placer.Group("Isletas");
        placer.Tilted(pIsletaPergO, gIsletas, new Vector2(-45f - 5f, 0f), 0f, 5f, 5f);
        placer.Tilted(pIsletaPergE, gIsletas, new Vector2(45f + 2.5f, 0f), 180f, 2.5f, 5f);
        placer.Tilted(pIsletaMedO, gIsletas, new Vector2(-45f - 5.6f, 16.6f), 0f, 5.6f, 5.8f);
        placer.Tilted(pIsletaMedE, gIsletas, new Vector2(46f + 3.85f, 16.5f), 180f, 3.85f, 5.75f);

        var gMediana = placer.Group("Mediana");
        placer.Strip(pMediana, gMediana, -45f, 46f, 17.25f, 3.5f, 0f);

        var gAcera = placer.Group("Acera_Sur");
        // El bordillo del prefab queda en su +Z, que aquí mira al pasillo sur
        placer.Strip(pAcera, gAcera, -50f, 54f, -12.55f, 3.3f, 0f);

        var gMarcas = placer.Group("Marcas_Viales");
        int plazas = Mathf.RoundToInt(Modulos * LargoModulo / 2.5f);
        for (int j = 0; j <= plazas; j++)
        {
            float x = -45f + 2.5f * j;
            placer.Tilted(pLinea, gMarcas, new Vector2(x, 2.5f), 0f, 1f, 2.4f, 0.03f);
            placer.Tilted(pLinea, gMarcas, new Vector2(x, -2.5f), 0f, 1f, 2.4f, 0.03f);
        }
        // Sentido único: el pasillo sur va hacia el este y el norte hacia el oeste
        foreach (var x in new[] { -30f, 0f, 30f })
        {
            placer.Tilted(pFlecha, gMarcas, new Vector2(x, -7.8f), 0f, 2f, 1f, 0.03f);
            placer.Tilted(pFlecha, gMarcas, new Vector2(-x, 10.2f), 180f, 2f, 1f, 0.03f);
        }
        placer.Tilted(pPaso, gMarcas, new Vector2(-48f, 7.9f), 0f, 2f, 2.9f, 0.03f);
        placer.Tilted(pPaso, gMarcas, new Vector2(-48f, -7.95f), 0f, 2f, 2.95f, 0.03f);

        var gFarolas = placer.Group("Farolas");
        foreach (var x in new[] { -40.4f, -10.9f, 18.7f, 48.2f })
            placer.Upright(pFarola, gFarolas, new Vector2(x, -11.6f), -90f);   // brazo hacia el pasillo
        foreach (var x in new[] { -32.4f, -3.8f, 24.6f })
            placer.Upright(pFarola, gFarolas, new Vector2(x, 17.25f), 90f);   // brazo hacia el parking

        // Pérgola pequeña del este: mismo eje que la grande, sin isletas, 41.1 x 10.1 m
        var gPergolaEste = placer.Group("Pergola_Este");
        var centroEste = new Vector2(100.96f, -0.96f);
        const int modulosEste = 8;
        const float largoEste = 41.1f;
        var escalaEste = new Vector3(largoEste / (modulosEste * LargoModulo), 1f, 10.1f / (2f * SemiAncho));
        placer.Row(pModulo, gPergolaEste, centroEste.x - largoEste / 2f, centroEste.x + largoEste / 2f, modulosEste, centroEste.y, escalaEste);

        var gMarcasEste = placer.Group("Marcas_Viales_Este");
        const int plazasEste = 16;
        for (int j = 0; j <= plazasEste; j++)
        {
            float x = centroEste.x - largoEste / 2f + largoEste / plazasEste * j;
            placer.Tilted(pLinea, gMarcasEste, new Vector2(x, centroEste.y + 2.6f), 0f, 1f, 2.4f, 0.03f);
            placer.Tilted(pLinea, gMarcasEste, new Vector2(x, centroEste.y - 2.6f), 0f, 1f, 2.4f, 0.03f);
        }
        // Fila en batería contra el bordillo norte
        const int plazasFilaNorte = 18;
        for (int j = 0; j <= plazasFilaNorte; j++)
            placer.Tilted(pLinea, gMarcasEste, new Vector2(centroEste.x - 19.4f + 2.5f * j, centroEste.y + 14f), 0f, 1f, 2.4f, 0.03f);

        var gAceraEste = placer.Group("Acera_Norte_Este");
        // Girada 180° para que el bordillo mire al parking
        placer.Strip(pAceraEstrecha, gAceraEste, centroEste.x - 35f, centroEste.x + 38f, centroEste.y + 17.25f, 1.5f, 180f);

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabDir + "/Parking_Norte.prefab", InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ParkingNorte] Parking generado: {Modulos + modulosEste} módulos de pérgola, " +
                  $"{plazas * 2 + plazasEste * 2 + plazasFilaNorte} plazas");
    }

    // ---------- Colocación sobre el terreno ----------

    class Placer
    {
        readonly Terrain terrain;
        readonly Transform root;

        public Placer(Terrain terrain, Transform root) { this.terrain = terrain; this.root = root; }

        public Transform Group(string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root, false);
            return g;
        }

        // Altura del terreno en un punto del marco local del parking (raíz con y = 0 y solo giro en Y)
        // Si hay viales generados (Entorno/Viales), las piezas se apoyan en su superficie, que
        // queda por encima del Terrain rebajado; si no, en el propio Terrain.
        float H(Vector3 local)
        {
            var w = root.TransformPoint(new Vector3(local.x, 0f, local.z));
            float h = terrain.SampleHeight(w) + terrain.GetPosition().y;
            var viales = GameObject.Find("Entorno/Viales");
            if (viales == null) return h;
            foreach (var hit in Physics.RaycastAll(new Vector3(w.x, h + 20f, w.z), Vector3.down, 40f))
                if (hit.collider.transform.IsChildOf(viales.transform) && hit.collider.name.StartsWith("Calzada"))
                    h = Mathf.Max(h, hit.point.y);
            return h;
        }

        GameObject Spawn(GameObject prefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return go;
        }

        // Piezas verticales (columnas, farolas): solo se ajusta la altura
        public void Upright(GameObject prefab, Transform parent, Vector2 c, float yaw, float sink = 0f)
        {
            var center = new Vector3(c.x, 0f, c.y);
            var go = Spawn(prefab, parent);
            go.transform.localPosition = new Vector3(c.x, H(center) - sink, c.y);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Fila de módulos sobre la recta que mejor ajusta el terreno bajo sus pilares, con la
        // pendiente aplicada como cabeceo para que la cubierta quede continua y sin escalones
        public void Row(GameObject prefab, Transform parent, float x0, float x1, int count, float z, Vector3 scale)
        {
            var xs = new float[count];
            var hs = new float[count];
            float mx = 0f, mh = 0f;
            for (int i = 0; i < count; i++)
            {
                xs[i] = x0 + (x1 - x0) / count * (i + 0.5f);
                hs[i] = H(new Vector3(xs[i], 0f, z));
                mx += xs[i] / count; mh += hs[i] / count;
            }
            float sxx = 0f, sxh = 0f;
            for (int i = 0; i < count; i++) { sxx += (xs[i] - mx) * (xs[i] - mx); sxh += (xs[i] - mx) * (hs[i] - mh); }
            float slope = sxx > 0f ? sxh / sxx : 0f;

            for (int i = 0; i < count; i++)
            {
                var go = Spawn(prefab, parent);
                go.transform.localPosition = new Vector3(xs[i], mh + slope * (xs[i] - mx), z);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan(slope) * Mathf.Rad2Deg);
                go.transform.localScale = scale;
            }
        }

        // Piezas planas: se inclinan siguiendo el plano del terreno muestreado en sus extremos
        public GameObject Tilted(GameObject prefab, Transform parent, Vector2 c, float yaw, float halfLen, float halfWid,
            float yOffset = 0f, float scaleX = 1f)
        {
            var yawQ = Quaternion.Euler(0f, yaw, 0f);
            Vector3 xh = yawQ * Vector3.right, zh = yawQ * Vector3.forward;
            var center = new Vector3(c.x, 0f, c.y);

            float h0 = H(center - xh * halfLen), h1 = H(center + xh * halfLen);
            float g0 = H(center - zh * halfWid), g1 = H(center + zh * halfWid);
            float hc = (2f * H(center) + h0 + h1 + g0 + g1) / 6f;

            var xDir = (xh * 2f * halfLen + Vector3.up * (h1 - h0)).normalized;
            var zDir = (zh * 2f * halfWid + Vector3.up * (g1 - g0)).normalized;
            var up = Vector3.Cross(zDir, xDir).normalized;

            var go = Spawn(prefab, parent);
            go.transform.localPosition = new Vector3(c.x, hc + yOffset, c.y);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(xDir, up), up);
            go.transform.localScale = new Vector3(scaleX, 1f, 1f);
            return go;
        }

        // Franja de tramos de 2 m (escalados para cubrir el largo) entre x0 y x1
        public void Strip(GameObject prefab, Transform parent, float x0, float x1, float z, float width, float yaw)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 2f));
            float seg = (x1 - x0) / n;
            for (int i = 0; i < n; i++)
                Tilted(prefab, parent, new Vector2(x0 + seg * (i + 0.5f), z), yaw, seg / 2f, width / 2f, 0f, seg / 2f * 1.015f);   // leve solape para tapar juntas
        }
    }

    // ---------- Mallas ----------

    static Mesh PergolaModuloMesh()
    {
        var mb = new MeshBuilder(4);
        const int Acero = 0, ChapaSup = 1, ChapaInf = 2, Hormigon = 3;
        float hx = LargoModulo / 2f;

        // Cubierta de chapa grecada (ondas a lo largo de X, nervios hacia el borde)
        const int nx = 100, nz = 24;
        const float amp = 0.018f, period = 0.2f;
        var top = new int[nx + 1, nz + 1];
        var bot = new int[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
        for (int k = 0; k <= nz; k++)
        {
            float x = -hx + LargoModulo * i / nx;
            float z = -SemiAncho + 2f * SemiAncho * k / nz;
            float w = 2f * Mathf.PI / period;
            float y = RoofY(z) + amp * Mathf.Sin(w * x);
            float dydx = amp * w * Mathf.Cos(w * x);
            float dydz = 2f * SubidaBorde * z / (SemiAncho * SemiAncho);
            var n = new Vector3(-dydx, 1f, -dydz).normalized;
            var uv = new Vector2(x, z);
            top[i, k] = mb.Vert(new Vector3(x, y, z), n, uv);
            bot[i, k] = mb.Vert(new Vector3(x, y - 0.01f, z), -n, uv);
        }
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
        {
            mb.TriFacing(ChapaSup, top[i, k], top[i, k + 1], top[i + 1, k + 1], Vector3.up);
            mb.TriFacing(ChapaSup, top[i, k], top[i + 1, k + 1], top[i + 1, k], Vector3.up);
            mb.TriFacing(ChapaInf, bot[i, k], bot[i, k + 1], bot[i + 1, k + 1], Vector3.down);
            mb.TriFacing(ChapaInf, bot[i, k], bot[i + 1, k + 1], bot[i + 1, k], Vector3.down);
        }

        // Frentes de borde y canalón central
        foreach (var s in new[] { -1f, 1f })
            mb.Box(Acero, new Vector3(0f, RoofY(SemiAncho) - 0.03f, s * SemiAncho), new Vector3(LargoModulo, 0.16f, 0.04f), Quaternion.identity);
        mb.Box(Acero, new Vector3(0f, RoofY(0f) - 0.07f, 0f), new Vector3(LargoModulo, 0.12f, 0.25f), Quaternion.identity);

        // Correas longitudinales bajo la chapa
        foreach (var z in new[] { 0.95f, 1.9f, 2.85f, 3.8f })
        foreach (var s in new[] { -1f, 1f })
        {
            float y = RoofY(z) - 0.05f;
            mb.Tube(Acero, new[] { new Vector3(-hx, y, s * z), new Vector3(hx, y, s * z) }, 0.03f, 8);
        }

        // Pilar central con dado de hormigón
        float yTop = RoofY(0f) - 0.13f;
        mb.Tube(Acero, new[] { new Vector3(0f, -Zocalo, 0f), new Vector3(0f, yTop, 0f) }, 0.1f, 14);
        mb.Box(Hormigon, new Vector3(0f, (0.35f - Zocalo) / 2f, 0f), new Vector3(0.45f, 0.35f + Zocalo, 0.45f), Quaternion.identity);
        mb.Box(Acero, new Vector3(0f, 0.37f, 0f), new Vector3(0.35f, 0.04f, 0.35f), Quaternion.identity);

        // Brazos en árbol: cordón superior bajo la cubierta, cordón inferior que nace del pilar
        // y celosía entre ambos, a cada lado del pilar
        const float zTip = 4.55f, yArranque = 1.95f;
        float Upper(float z) => RoofY(z) - 0.09f;
        float Lower(float z) => yArranque + (Upper(zTip) - yArranque) * Mathf.Pow(z / zTip, 1.7f);
        foreach (var s in new[] { -1f, 1f })
        {
            var up = new List<Vector3>();
            var lo = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float z = zTip * i / 12f;
                up.Add(new Vector3(0f, Upper(z), s * z));
                lo.Add(new Vector3(0f, Lower(z), s * z));
            }
            mb.Tube(Acero, up, 0.045f, 10);
            mb.Tube(Acero, lo, 0.05f, 10);
            for (int i = 0; i < 7; i++)
            {
                float za = 0.65f * i, zb = za + 0.325f, zc = za + 0.65f;
                var a = new Vector3(0f, Lower(za), s * za);
                var b = new Vector3(0f, Upper(zb), s * zb);
                var c = new Vector3(0f, Lower(Mathf.Min(zc, zTip)), s * Mathf.Min(zc, zTip));
                mb.Tube(Acero, new[] { a, b }, 0.018f, 6);
                mb.Tube(Acero, new[] { b, c }, 0.018f, 6);
            }
        }

        return mb.ToMesh("Pergola_Modulo");
    }

    // Tramo de 2 m (eje X) de acera o mediana elevada. Bordillo en +Z (y en -Z si es mediana).
    static Mesh TramoMesh(string name, float width, bool dosBordillos)
    {
        var mb = new MeshBuilder(2);
        const float bw = 0.25f;
        float hw = width / 2f;
        float zIn0 = dosBordillos ? -hw + bw : -hw;
        float zIn1 = hw - bw;
        float h = AlturaBordillo + Zocalo;
        mb.Box(0, new Vector3(0f, AlturaBordillo - h / 2f, hw - bw / 2f), new Vector3(2f, h, bw), Quaternion.identity);
        if (dosBordillos)
            mb.Box(0, new Vector3(0f, AlturaBordillo - h / 2f, -hw + bw / 2f), new Vector3(2f, h, bw), Quaternion.identity);
        float top = AlturaBordillo - 0.01f;
        mb.Box(1, new Vector3(0f, top - (top + Zocalo) / 2f, (zIn0 + zIn1) / 2f), new Vector3(2f, top + Zocalo, zIn1 - zIn0), Quaternion.identity);
        return mb.ToMesh(name);
    }

    // Isleta en "D": lado recto en +X, tramo recto de largo l y semicírculo de radio r hacia -X.
    // El pivote queda en el centro de la caja envolvente.
    static Mesh IsletaMesh(string name, float r, float l)
    {
        var mb = new MeshBuilder(2);
        const float bw = 0.25f;
        const int segs = 32;
        var outer = DOutline(r, l, 0f, segs);
        var inner = DOutline(r, l, bw, segs);
        float yTop = AlturaBordillo;
        int n = outer.Count;

        // Corona del bordillo
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mb.Quad(0, At(outer[i], yTop), At(outer[j], yTop), At(inner[j], yTop), At(inner[i], yTop), Vector3.up);
        }
        // Cara exterior del bordillo, hasta el zócalo
        var centroid = Vector3.zero;
        foreach (var p in outer) centroid += At(p, 0f);
        centroid /= n;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a = At(outer[i], yTop), b = At(outer[j], yTop);
            var nrm = Vector3.Cross(Vector3.up, b - a).normalized;
            if (Vector3.Dot(nrm, (a + b) / 2f - centroid) < 0f) nrm = -nrm;
            mb.Quad(0, a, b, At(outer[j], -Zocalo), At(outer[i], -Zocalo), nrm);
        }
        // Pavimento interior
        var pav = new List<Vector3>();
        foreach (var p in inner) pav.Add(At(p, yTop - 0.01f));
        mb.Fan(1, pav, Vector3.up);
        return mb.ToMesh(name);
    }

    static Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);

    static List<Vector2> DOutline(float r, float l, float inset, int segs)
    {
        float total = l + r;
        float flatX = total / 2f - inset;
        float cx = total / 2f - l;
        float rr = r - inset;
        var pts = new List<Vector2>();
        for (int i = 0; i <= segs; i++)
        {
            float a = Mathf.PI / 2f + Mathf.PI * i / segs;
            pts.Add(new Vector2(Mathf.Min(cx + rr * Mathf.Cos(a), flatX), rr * Mathf.Sin(a)));
        }
        pts.Insert(0, new Vector2(flatX, rr));
        pts.Add(new Vector2(flatX, -rr));
        return pts;
    }

    static Mesh LineaMesh()
    {
        var mb = new MeshBuilder(1);
        mb.Quad(0, new Vector3(-0.06f, 0f, -2.4f), new Vector3(-0.06f, 0f, 2.4f), new Vector3(0.06f, 0f, 2.4f), new Vector3(0.06f, 0f, -2.4f), Vector3.up);
        return mb.ToMesh("Linea_Plaza");
    }

    // Flecha de sentido de 4.5 m apuntando a +X
    static Mesh FlechaMesh()
    {
        var mb = new MeshBuilder(1);
        mb.Quad(0, new Vector3(-2.5f, 0f, -0.08f), new Vector3(-2.5f, 0f, 0.08f), new Vector3(0.5f, 0f, 0.08f), new Vector3(0.5f, 0f, -0.08f), Vector3.up);
        mb.Fan(0, new List<Vector3> { new Vector3(0.5f, 0f, -0.3f), new Vector3(0.5f, 0f, 0.3f), new Vector3(2f, 0f, 0f) }, Vector3.up);
        return mb.ToMesh("Flecha_Recta");
    }

    // Paso de cebra: 6 bandas de 4 m x 0.5 m separadas 1 m a lo largo de Z
    static Mesh PasoMesh()
    {
        var mb = new MeshBuilder(1);
        for (int i = 0; i < 6; i++)
        {
            float z = -2.5f + i;
            mb.Quad(0, new Vector3(-2f, 0f, z - 0.25f), new Vector3(-2f, 0f, z + 0.25f), new Vector3(2f, 0f, z + 0.25f), new Vector3(2f, 0f, z - 0.25f), Vector3.up);
        }
        return mb.ToMesh("Paso_Peatones_6m");
    }

    // Farola de 9 m con brazo curvo hacia +X
    static Mesh FarolaMesh()
    {
        var mb = new MeshBuilder(2);
        mb.Tube(0, new[] { new Vector3(0f, -Zocalo, 0f), new Vector3(0f, 9f, 0f) }, 0.085f, 12, 0.06f);
        mb.Box(0, new Vector3(0f, 0.2f, 0f), new Vector3(0.28f, 0.4f, 0.28f), Quaternion.identity);
        var arm = new List<Vector3>();
        Vector3 p0 = new Vector3(0f, 8.6f, 0f), p1 = new Vector3(0.05f, 9.2f, 0f), p2 = new Vector3(1.6f, 9.25f, 0f);
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            arm.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2);
        }
        mb.Tube(0, arm, 0.04f, 8);
        mb.Box(1, new Vector3(1.85f, 9.2f, 0f), new Vector3(0.8f, 0.14f, 0.32f), Quaternion.identity);
        return mb.ToMesh("Farola");
    }

    // ---------- Assets ----------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    static Material MatCampus(string nombre) =>
        AssetDatabase.LoadAssetAtPath<Material>($"{McamImporter.MaterialesDir}/{nombre}.mat");

    static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Reutiliza el asset existente para que los prefabs no pierdan la referencia al regenerar
    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshDir}/{name}.asset";
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

    static GameObject MakePrefab(string name, Mesh mesh, Material[] mats, bool castShadows, System.Action<GameObject> extra)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        extra?.Invoke(go);
        go.isStatic = true;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void AddBox(GameObject go, float width)
    {
        var c = go.AddComponent<BoxCollider>();
        float h = AlturaBordillo + Zocalo;
        c.center = new Vector3(0f, AlturaBordillo - h / 2f, 0f);
        c.size = new Vector3(2f, h, width);
    }
}
