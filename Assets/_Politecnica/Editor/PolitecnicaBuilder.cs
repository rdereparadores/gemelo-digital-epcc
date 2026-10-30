using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Monta los edificios de la Escuela Politécnica a partir de lo que genera
// Herramientas/politecnica/generar_politecnica.py en Assets/_Politecnica/Edificios/<código>/:
// las mallas por planta y de cubierta (.mcam) y el manifiesto <código>.json con locales, puertas,
// luces y mobiliario. Cada edificio se guarda como prefab en Assets/_Politecnica/Prefabs/ y se
// instancia en la escena bajo Politecnica_Edificios. También rebaja el Terrain bajo cada huella para
// que no asome por los suelos (y vacía los semisótanos), partiendo del heightmap de la red viaria.
public static class PolitecnicaBuilder
{
    const string Edificios = MaterialesPolitecnica.Raiz + "/Edificios";
    const string Mobiliario = MaterialesPolitecnica.Raiz + "/Mobiliario";
    const string Prefabs = MaterialesPolitecnica.Raiz + "/Prefabs";
    const string Contenedor = "Politecnica_Edificios";
    const string HeightmapViales = "Datos/Viales/heightmap_viales.raw";

#pragma warning disable 0649
    [System.Serializable] class Planta { public string nombre, malla; public float y; }
    [System.Serializable] class LocalDef
    {
        public string planta, num, rotulo, codigo, uso;
        public float area; public int capacidad;
        public float[] centro, min, max, poligono;
    }
    [System.Serializable] class PuertaDef { public string planta, tipo; public float[] p; public float yaw, ancho, alto, apertura; }
    [System.Serializable] class LuzDef { public string planta, local; public float[] p; public float rango; }
    [System.Serializable] class RebajeDef { public float[] huella; public float y; }
    [System.Serializable] class PiezaDef { public string tipo, planta, local; public float[] p; public float yaw, ex = 1f, ey = 1f, ez = 1f; }
    [System.Serializable] class Manifiesto
    {
        public string codigo, nombre, cubierta;
        public string[] extras;
        public float[] origen, huella;
        public float cota_p0, suelo_min;
        public Planta[] plantas;
        public LocalDef[] locales;
        public PuertaDef[] puertas;
        public LuzDef[] luces;
        public PiezaDef[] mobiliario;
        public RebajeDef[] rebajes;
        public RebajeDef[] suelos;
    }
#pragma warning restore 0649

    [MenuItem("Tools/Politécnica/Construir edificios")]
    public static void ConstruirTodo()
    {
        // Primero los materiales: los .mcam que se importen después ya los encuentran
        AssetDatabase.Refresh();
        MaterialesPolitecnica.Build();
        AssetDatabase.Refresh();
        // El rebaje del Terrain parte siempre del heightmap de la red viaria, para que repetir la
        // construcción no vaya ahondando el terreno
        if (Terrain.activeTerrain != null && File.Exists(HeightmapViales))
            VialesBuilder.AplicarHeightmap(Terrain.activeTerrain.terrainData, HeightmapViales);
        foreach (var dir in Directory.GetDirectories(Edificios))
            Construir(Path.GetFileName(dir));
        // Fuera la hierba que asomaría por pavimentos, aceras y escalones
        VegetacionBuilder.QuitarHierbaBajoMallas();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    public static string Construir(string cod)
    {
        string ruta = $"{Edificios}/{cod}/{cod}.json";
        if (!File.Exists(ruta)) return $"{cod}: sin manifiesto";
        var m = JsonUtility.FromJson<Manifiesto>(File.ReadAllText(ruta));
        var origen = V(m.origen);

        var root = new GameObject($"{m.codigo} {m.nombre}");
        var ed = root.AddComponent<EdificioPolitecnica>();
        ed.codigo = m.codigo;
        ed.nombre = m.nombre;
        ed.cotaPlantaBaja = m.cota_p0;

        var plantas = new Dictionary<string, Transform>();
        var lista = new List<Transform>();
        foreach (var p in m.plantas)
        {
            var t = new GameObject(p.nombre).transform;
            t.SetParent(root.transform, false);
            Instanciar($"{Edificios}/{cod}/{p.malla}", t, Vector3.zero, Quaternion.identity, Vector3.one);
            plantas[p.nombre] = t;
            lista.Add(t);
        }
        ed.plantas = lista.ToArray();
        if (!string.IsNullOrEmpty(m.cubierta))
        {
            var cub = new GameObject("Cubierta").transform;
            cub.SetParent(root.transform, false);
            Instanciar($"{Edificios}/{cod}/{m.cubierta}", cub, Vector3.zero, Quaternion.identity, Vector3.one);
            ed.cubierta = cub;
        }

        foreach (var extra in m.extras ?? new string[0])
        {
            var t = new GameObject(Path.GetFileNameWithoutExtension(extra).Replace($"{cod}_", "")).transform;
            t.SetParent(root.transform, false);
            Instanciar($"{Edificios}/{cod}/{extra}", t, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        // Locales: volumen como trigger con sus datos
        foreach (var l in m.locales ?? new LocalDef[0])
        {
            var padre = Hijo(plantas, l.planta, "Locales");
            var go = new GameObject(string.IsNullOrEmpty(l.rotulo) ? $"{l.num} {l.uso}" : $"{l.num} {l.rotulo}");
            go.transform.SetParent(padre, false);
            var a = V(l.min); var b = V(l.max);
            go.transform.localPosition = (a + b) / 2f;   // las plantas están en el origen del edificio
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Max(0.5f, b.y - a.y), Mathf.Abs(b.z - a.z));
            go.layer = 2;   // Ignore Raycast
            var loc = go.AddComponent<Local>();
            loc.edificio = m.codigo; loc.planta = l.planta; loc.numero = l.num; loc.codigo = l.codigo;
            loc.rotulo = l.rotulo; loc.uso = l.uso; loc.superficie = l.area; loc.capacidad = l.capacidad;
        }

        // Hojas de puerta (pivote en las bisagras, giran solas al acercarse)
        int sinPrefab = 0;
        foreach (var d in m.puertas ?? new PuertaDef[0])
        {
            var padre = Hijo(plantas, d.planta, "Puertas");
            string tipo = d.tipo == "exterior" ? "Puerta_Exterior" : "Puerta_Interior";
            var go = Instanciar($"{Mobiliario}/{tipo}.mcam", padre, V(d.p), Quaternion.Euler(0, d.yaw, 0), new Vector3(d.ancho, d.alto, 1f));
            if (go == null) { sinPrefab++; continue; }
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            foreach (Transform h in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(h.gameObject, 0);
            var pu = go.AddComponent<Puerta>();
            pu.apertura = d.apertura;
        }

        // Mobiliario e instalaciones
        var faltan = new HashSet<string>();
        foreach (var p in m.mobiliario ?? new PiezaDef[0])
        {
            var padre = Hijo(plantas, p.planta, "Mobiliario");
            var esc = new Vector3(p.ex == 0 ? 1 : p.ex, p.ey == 0 ? 1 : p.ey, p.ez == 0 ? 1 : p.ez);
            if (Instanciar($"{Mobiliario}/{p.tipo}.mcam", padre, V(p.p), Quaternion.Euler(0, p.yaw, 0), esc) == null)
                faltan.Add(p.tipo);
        }

        // Una luz puntual por local; EdificioPolitecnica las enciende solo con la cámara cerca
        foreach (var l in m.luces ?? new LuzDef[0])
        {
            var padre = Hijo(plantas, l.planta, "Luces");
            var go = new GameObject($"Luz {l.local}");
            go.transform.SetParent(padre, false);
            go.transform.localPosition = V(l.p);
            var luz = go.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.range = l.rango;
            luz.intensity = 1.6f;
            luz.color = Mathf.CorrelatedColorTemperatureToRGB(4000f);
            luz.shadows = LightShadows.None;
            luz.lightmapBakeType = LightmapBakeType.Realtime;
        }

        // Prefab y escena
        if (!AssetDatabase.IsValidFolder(Prefabs)) AssetDatabase.CreateFolder(MaterialesPolitecnica.Raiz, "Prefabs");
        string prefab = $"{Prefabs}/{m.codigo}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefab);
        Object.DestroyImmediate(root);

        var cont = GameObject.Find(Contenedor) ?? new GameObject(Contenedor);
        for (int i = cont.transform.childCount - 1; i >= 0; i--)
        {
            var h = cont.transform.GetChild(i);
            var e = h.GetComponent<EdificioPolitecnica>();
            // Fuera el edificio anterior con este código y los marcadores de OSM sin modelo
            if ((e != null && e.codigo == m.codigo) || (e == null && h.childCount == 0)) Object.DestroyImmediate(h.gameObject);
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), cont.transform);
        inst.transform.position = origen;
        inst.name = $"{m.codigo} {m.nombre}";

        // Bajo cada planta, hasta su suelo (los manifiestos antiguos solo traen la huella y el suelo más bajo)
        if (m.suelos != null && m.suelos.Length > 0)
            foreach (var s in m.suelos) RebajarTerreno(origen, s.huella, origen.y + s.y - 0.12f);
        else
            RebajarTerreno(origen, m.huella, origen.y + m.suelo_min - 0.12f);
        // Delante de cada puerta exterior el terreno no puede quedar por encima del umbral (la hoja
        // chocaría al abrir): se rebaja una franja de 1,5 m a cada lado de la puerta
        foreach (var d in m.puertas ?? new PuertaDef[0])
        {
            if (d.tipo != "exterior") continue;
            var rot = Quaternion.Euler(0, d.yaw, 0);
            var bis = V(d.p) + origen;
            var x = rot * Vector3.right * d.ancho;
            var z = rot * Vector3.forward;
            var c = bis + x / 2;
            var w = x.normalized * (d.ancho / 2 + 0.3f);
            var poly = new[] { c - w - z * 1.5f, c + w - z * 1.5f, c + w + z * 1.5f, c - w + z * 1.5f };
            var huella = new float[8];
            for (int i = 0; i < 4; i++) { huella[2 * i] = poly[i].x - origen.x; huella[2 * i + 1] = poly[i].z - origen.z; }
            RebajarTerreno(origen, huella, bis.y - 0.03f, -0.2f);
        }
        // Bajo cada peldaño y rellano exterior, el terreno queda por debajo de su huella
        foreach (var r in m.rebajes ?? new RebajeDef[0])
            RebajarTerreno(origen, r.huella, origen.y + r.y, -0.2f);
        return $"{cod}: {m.plantas.Length} plantas, {m.locales?.Length ?? 0} locales, {m.puertas?.Length ?? 0} puertas" +
               (sinPrefab > 0 ? $" ({sinPrefab} sin prefab)" : "") +
               (faltan.Count > 0 ? $"; faltan piezas: {string.Join(", ", faltan)}" : "");
    }

    static Transform Hijo(Dictionary<string, Transform> plantas, string planta, string nombre)
    {
        if (!plantas.TryGetValue(planta ?? "", out var p)) foreach (var q in plantas.Values) { p = q; break; }
        var t = p.Find(nombre);
        if (t == null)
        {
            t = new GameObject(nombre).transform;
            t.SetParent(p, false);
        }
        return t;
    }

    static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    // Instancia el prefab importado de un .mcam en coordenadas locales del edificio
    static GameObject Instanciar(string ruta, Transform padre, Vector3 posEdificio, Quaternion rot, Vector3 escala)
    {
        if (!cache.TryGetValue(ruta, out var pref) || pref == null)
            cache[ruta] = pref = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (pref == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(pref, padre);
        // Las piezas cuelgan de Planta/Grupo; posEdificio está en coordenadas del edificio
        go.transform.position = padre.root.TransformPoint(posEdificio);
        go.transform.localRotation = rot;
        go.transform.localScale = escala;
        return go;
    }

    static Vector3 V(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);

    // Baja el heightmap dentro de la huella hasta la cota dada, si está por encima
    static void RebajarTerreno(Vector3 origen, float[] huella, float yMax, float margen = 0.2f)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || huella == null || huella.Length < 6) return;
        var td = terrain.terrainData;
        var poly = new List<Vector2>();
        for (int i = 0; i + 1 < huella.Length; i += 2) poly.Add(new Vector2(huella[i] + origen.x, huella[i + 1] + origen.z));
        Vector2 lo = poly[0], hi = poly[0];
        foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
        int res = td.heightmapResolution;
        Vector3 tp = terrain.transform.position, size = td.size;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - 1 - tp.x) / size.x * (res - 1)), 0, res - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x + 1 - tp.x) / size.x * (res - 1)), 0, res - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - 1 - tp.z) / size.z * (res - 1)), 0, res - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((hi.y + 1 - tp.z) / size.z * (res - 1)), 0, res - 1);
        var h = td.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        float hmax = (yMax - tp.y) / size.y;
        bool cambio = false;
        for (int j = 0; j <= z1 - z0; j++)
            for (int i = 0; i <= x1 - x0; i++)
            {
                var p = new Vector2(tp.x + (x0 + i) * size.x / (res - 1), tp.z + (z0 + j) * size.z / (res - 1));
                // Solo por dentro de la huella (a más de 0,2 m de la fachada): fuera, el terreno queda
                // como estaba y la fachada baja hasta él (zócalo), sin zanja alrededor del edificio
                if (h[j, i] > hmax && DistanciaDentro(poly, p) > margen) { h[j, i] = hmax; cambio = true; }
            }
        if (cambio)
        {
            td.SetHeights(x0, z0, h);
            EditorUtility.SetDirty(td);
        }
    }

    // >0 dentro del polígono (distancia al borde), <0 fuera
    static float DistanciaDentro(List<Vector2> poly, Vector2 p)
    {
        bool dentro = false;
        float dmin = float.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) dentro = !dentro;
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            dmin = Mathf.Min(dmin, Vector2.Distance(p, a + ab * t));
        }
        return dentro ? dmin : -dmin;
    }
}
