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
    // Temperatura de color de las luces interiores: los tubos y LED de 4000 K se ven blancos con el ojo adaptado,
    // pero con 4000 K en el render (balance de blancos de día) el aula salía anaranjada
    const float ColorLuzInterior = 6000f;
    const string HeightmapViales = "Datos/Viales/heightmap_viales.raw";

#pragma warning disable 0649
    [System.Serializable] class Planta { public string nombre, malla; public float y; }
    [System.Serializable] class LocalDef
    {
        public string planta, num, rotulo, codigo, uso;
        public float area; public int capacidad;
        public float[] centro, min, max, poligono;
    }
    [System.Serializable] class PuertaDef { public string planta, tipo, hoja; public float[] p; public float yaw, ancho, alto, apertura; }
    [System.Serializable] class LuzDef { public string planta, local; public float[] p; public float rango, i; }
    [System.Serializable] class RebajeDef { public float[] huella; public float y; }
    [System.Serializable] class PiezaDef { public string tipo, planta, local; public float[] p; public float yaw, ex = 1f, ey = 1f, ez = 1f; }
    [System.Serializable] class Manifiesto
    {
        public string codigo, nombre, cubierta;
        public string[] extras;
        public float[] origen, huella;
        public float cota_p0, suelo_min, giro;
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
        var locales = new Dictionary<(string, string), Local>();
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
            // Para el gemelo: planta exacta (las cajas son envolventes alineadas con el mundo y se solapan)
            loc.poligono = Contorno(l.poligono, origen);
            loc.cotaSuelo = origen.y + l.min[1];
            locales[(l.planta, l.num)] = loc;
        }

        // Hojas de puerta (pivote en las bisagras, giran solas al acercarse)
        int sinPrefab = 0;
        Puerta anterior = null;
        PuertaDef defAnterior = null;
        foreach (var d in m.puertas ?? new PuertaDef[0])
        {
            var padre = Hijo(plantas, d.planta, "Puertas");
            // hoja: la del manifiesto (vidrio con la carpintería del edificio, o la de madera)
            string tipo = !string.IsNullOrEmpty(d.hoja) ? d.hoja : d.tipo == "exterior" ? "Puerta_Exterior" : "Puerta_Interior";
            var go = Instanciar($"{Mobiliario}/{tipo}.mcam", padre, V(d.p), Quaternion.Euler(0, d.yaw, 0), new Vector3(d.ancho, d.alto, 1f));
            if (go == null) { sinPrefab++; anterior = null; continue; }
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            foreach (Transform h in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(h.gameObject, 0);
            var pu = go.AddComponent<Puerta>();
            pu.apertura = d.apertura;
            pu.exterior = d.tipo == "exterior";
            // Las dos hojas de un mismo hueco salen seguidas en el manifiesto: enfrentadas (media vuelta), del
            // mismo ancho y con los cantos juntos en el centro. Se mueven juntas (manda la primera)
            if (anterior != null && anterior.pareja == null && MismaPuerta(defAnterior, d))
            {
                anterior.pareja = pu; pu.pareja = anterior;
                anterior.principal = true; pu.principal = false;
            }
            anterior = pu; defAnterior = d;
        }

        // Mobiliario e instalaciones
        var faltan = new HashSet<string>();
        foreach (var p in m.mobiliario ?? new PiezaDef[0])
        {
            var padre = Hijo(plantas, p.planta, "Mobiliario");
            var esc = new Vector3(p.ex == 0 ? 1 : p.ex, p.ey == 0 ? 1 : p.ey, p.ez == 0 ? 1 : p.ez);
            var pieza = Instanciar($"{Mobiliario}/{p.tipo}.mcam", padre, V(p.p), Quaternion.Euler(0, p.yaw, 0), esc);
            if (pieza == null) faltan.Add(p.tipo);
            else if (EsAsiento(p.tipo) && p.local != null && locales.TryGetValue((p.planta, p.local), out var dueño))
                Anadir(ref dueño.asientos, pieza.transform);
        }

        // Luces puntuales de los locales (varias en los grandes, con la intensidad según la superficie que alumbran;
        // ver luces_local en el generador), de luz blanca neutra de fluorescente o LED. EdificioPolitecnica las
        // enciende solo con la cámara cerca y el gemelo (LucesInteriores) según los sensores.
        var colorLuz = Mathf.CorrelatedColorTemperatureToRGB(ColorLuzInterior);
        foreach (var l in m.luces ?? new LuzDef[0])
        {
            var padre = Hijo(plantas, l.planta, "Luces");
            var go = new GameObject($"Luz {l.local}");
            go.transform.SetParent(padre, false);
            go.transform.localPosition = V(l.p);
            var luz = go.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.range = l.rango;
            luz.intensity = l.i > 0 ? l.i : 1.6f;
            luz.color = colorLuz;
            luz.shadows = LightShadows.None;
            luz.lightmapBakeType = LightmapBakeType.Realtime;
            // Con sus datos de URP ya puestos: si no, URP los añade al abrir la escena y la deja modificada sin guardar
            if (!go.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(out _))
                go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            if (l.local != null && locales.TryGetValue((l.planta, l.local), out var dueño)) Anadir(ref dueño.luces, luz);
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
        var cortes = new List<(List<Vector2> poly, float y)>();
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
            cortes.Add((Poligono(huella, origen), bis.y - 0.03f));
        }
        // Bajo cada peldaño y rellano exterior, el terreno queda por debajo de su huella
        foreach (var r in m.rebajes ?? new RebajeDef[0])
        {
            RebajarTerreno(origen, r.huella, origen.y + r.y, -0.2f);
            cortes.Add((Poligono(r.huella, origen), origen.y + r.y));
        }
        RebajarSotanos(m, origen, inst.transform, cortes);
        return $"{cod}: {m.plantas.Length} plantas, {m.locales?.Length ?? 0} locales, {m.puertas?.Length ?? 0} puertas" +
               (sinPrefab > 0 ? $" ({sinPrefab} sin prefab)" : "") +
               (faltan.Count > 0 ? $"; faltan piezas: {string.Join(", ", faltan)}" : "");
    }

    // ¿Son las dos hojas de una puerta de dos hojas? (mismo hueco: misma planta y casi el mismo ancho, giradas
    // media vuelta y con los cantos a menos de 10 cm; también dos puertas de una hoja que se cierran una contra
    // otra, como la de los distribuidores de P0 de Civil)
    static bool MismaPuerta(PuertaDef a, PuertaDef b)
    {
        if (a == null || b == null || a.planta != b.planta || a.tipo != b.tipo) return false;
        if (Mathf.Abs(a.ancho - b.ancho) > 0.03f || Mathf.Abs(Mathf.DeltaAngle(a.yaw, b.yaw + 180f)) > 2f) return false;
        Vector3 Canto(PuertaDef p) => V(p.p) + Quaternion.Euler(0, p.yaw, 0) * Vector3.right * p.ancho;
        return Vector3.Distance(Canto(a), Canto(b)) < 0.10f;
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

    // Piezas en las que se sienta la gente (los NPCs del gemelo ocupan estos puestos)
    static readonly string[] Asientos = { "Silla_", "Taburete", "Butaca_", "Banco_Corrido", "Banco_Hall", "Banco_Tapizado", "Sofa", "Sillon_" };
    static bool EsAsiento(string tipo) => System.Array.Exists(Asientos, a => tipo.StartsWith(a));

    static void Anadir<T>(ref T[] lista, T x)
    {
        var l = new List<T>(lista ?? new T[0]) { x };
        lista = l.ToArray();
    }

    // Polígono del manifiesto (x, z relativos al origen del edificio) en XZ del mundo
    static Vector2[] Contorno(float[] xz, Vector3 origen)
    {
        if (xz == null) return new Vector2[0];
        var r = new Vector2[xz.Length / 2];
        for (int i = 0; i < r.Length; i++) r[i] = new Vector2(origen.x + xz[2 * i], origen.z + xz[2 * i + 1]);
        return r;
    }

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

    static List<Vector2> Poligono(float[] huella, Vector3 origen)
    {
        var poly = new List<Vector2>();
        for (int i = 0; i + 1 < huella.Length; i += 2) poly.Add(new Vector2(huella[i] + origen.x, huella[i + 1] + origen.z));
        return poly;
    }

    static float[,] baseViales;

    // Locales bajo rasante: el terreno baja hasta debajo de su suelo en todas las celdas del heightmap que
    // los tocan. Con el margen de la huella, la última fila de celdas quedaba alta y el terreno entraba en
    // cuña por sus muros (el heightmap tiene celdas de 0,46 m). Fuera del edificio, la franja que queda
    // rebajada se tapa con una acera a la cota que tenía el terreno (o la de las puertas y peldaños de
    // `cortes`), que sigue el talud y no deja zanja junto a la fachada
    static void RebajarSotanos(Manifiesto m, Vector3 origen, Transform padre, List<(List<Vector2> poly, float y)> cortes)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || m.locales == null) return;
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        Vector3 tp = terrain.transform.position, size = td.size;
        float dx = size.x / (res - 1), dz = size.z / (res - 1);
        if (baseViales == null && File.Exists(HeightmapViales))
        {
            var bytes = File.ReadAllBytes(HeightmapViales);
            if (bytes.Length == res * res * 2)
            {
                baseViales = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        int i = (z * res + x) * 2;
                        baseViales[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
                    }
            }
        }
        var edificio = new List<List<Vector2>>();
        foreach (var s in m.suelos ?? new RebajeDef[0]) edificio.Add(Poligono(s.huella, origen));
        var zona = new HashSet<long>();
        foreach (var l in m.locales)
        {
            if (l.min == null || l.min.Length < 3 || l.min[1] > -0.5f || l.poligono == null || l.poligono.Length < 6) continue;
            var poly = Poligono(l.poligono, origen);
            float hmax = (origen.y + l.min[1] - 0.12f - tp.y) / size.y;
            Vector2 lo = poly[0], hi = poly[0];
            foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - 1 - tp.x) / dx), 1, res - 2);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x + 1 - tp.x) / dx), 1, res - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - 1 - tp.z) / dz), 1, res - 2);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((hi.y + 1 - tp.z) / dz), 1, res - 2);
            var h = td.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            bool cambio = false;
            for (int j = 0; j <= z1 - z0; j++)
                for (int i = 0; i <= x1 - x0; i++)
                {
                    // vértices de las cuatro celdas que pueden tocar el local (media diagonal de celda y algo más)
                    if (DistanciaDentro(poly, new Vector2(tp.x + (x0 + i) * dx, tp.z + (z0 + j) * dz)) < -0.7f) continue;
                    float hb = baseViales != null ? baseViales[z0 + j, x0 + i] : h[j, i];
                    if (hb > hmax + 0.01f / size.y)
                        for (int a = -1; a <= 0; a++)
                            for (int b = -1; b <= 0; b++)
                                zona.Add(((long)(x0 + i + a) << 32) | (uint)(z0 + j + b));
                    if (h[j, i] > hmax) { h[j, i] = hmax; cambio = true; }
                }
            if (cambio) { td.SetHeights(x0, z0, h); EditorUtility.SetDirty(td); }
        }
        if (zona.Count == 0 || baseViales == null) return;

        // Cota de referencia en un punto: la del terreno de partida, sin pasar de las puertas y peldaños
        float Ref(float x, float z)
        {
            float fx = Mathf.Clamp((x - tp.x) / dx, 0, res - 1.001f), fz = Mathf.Clamp((z - tp.z) / dz, 0, res - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            float hb = Mathf.Lerp(Mathf.Lerp(baseViales[j, i], baseViales[j, i + 1], u),
                                  Mathf.Lerp(baseViales[j + 1, i], baseViales[j + 1, i + 1], u), v) * size.y + tp.y;
            var q = new Vector2(x, z);
            foreach (var c in cortes)
                if (c.y < hb && DistanciaDentro(c.poly, q) > -0.2f) hb = c.y;
            return hb;
        }
        const int n = 5;   // subceldas por lado de celda (~9 cm): el borde dentro del muro no se ve
        // La acera es una franja de ancho fijo junto a la fachada en los tramos rebajados, no la mancha de celdas
        // rebajadas: esa tenía el borde en escalera de 0,46 m contra el talud de las celdas vecinas (dientes de
        // sierra en las fachadas giradas de Teleco). A 1,4 m de la fachada ninguna celda queda rebajada, así que
        // su borde exterior queda a ras del terreno
        const float Ancho = 1.4f;
        bool RebajeCerca(Vector2 c)
        {
            int ic = Mathf.FloorToInt((c.x - tp.x) / dx), jc = Mathf.FloorToInt((c.y - tp.z) / dz);
            int r = Mathf.CeilToInt((Ancho + 0.3f) / Mathf.Min(dx, dz));
            for (int a = -r; a <= r; a++)
                for (int b = -r; b <= r; b++)
                {
                    if (!zona.Contains(((long)(ic + a) << 32) | (uint)(jc + b))) continue;
                    var centro = new Vector2(tp.x + (ic + a + 0.5f) * dx, tp.z + (jc + b + 0.5f) * dz);
                    if (Vector2.Distance(centro, c) < Ancho + 0.3f) return true;
                }
            return false;
        }
        // Tramos de fachada (lados de las huellas) con rebaje a menos de 1,7 m: la franja va entera a lo largo de ellos
        var tramos = new List<(Vector2 a, Vector2 b, bool rebaje)>();
        foreach (var e in edificio)
            for (int i = 0, j = e.Count - 1; i < e.Count; j = i++)
            {
                Vector2 a = e[j], b = e[i];
                bool r = false;
                float lg = Vector2.Distance(a, b);
                for (float t = 0; t <= lg && !r; t += 0.4f) r = RebajeCerca(Vector2.Lerp(a, b, lg > 0 ? t / lg : 0));
                tramos.Add((a, b, r));
            }
        bool EnFranja(Vector2 c)
        {
            foreach (var (a, b, reb) in tramos)
            {
                if (!reb) continue;
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if (Vector2.Distance(c, a + ab * t) <= Ancho) return true;
            }
            return false;
        }
        var cubrir = new HashSet<long>();
        foreach (var (a, b, reb) in tramos)
        {
            if (!reb) continue;
            float lg = Vector2.Distance(a, b);
            for (float t = 0; t <= lg + 0.01f; t += Mathf.Min(dx, dz) * 0.5f)
            {
                var q = Vector2.Lerp(a, b, lg > 0 ? Mathf.Min(1f, t / lg) : 0);
                int qi = Mathf.FloorToInt((q.x - tp.x) / dx), qj = Mathf.FloorToInt((q.y - tp.z) / dz);
                int rr = Mathf.CeilToInt((Ancho + 0.2f) / Mathf.Min(dx, dz));
                for (int a2 = -rr; a2 <= rr; a2++)
                    for (int b2 = -rr; b2 <= rr; b2++)
                        cubrir.Add(((long)(qi + a2) << 32) | (uint)(qj + b2));
            }
        }
        int rad = Mathf.CeilToInt((Ancho + 0.5f) / Mathf.Min(dx, dz));
        foreach (var k in zona)
        {
            int ci = (int)(k >> 32), cj = (int)(k & 0xffffffff);
            for (int a = -rad; a <= rad; a++)
                for (int b = -rad; b <= rad; b++)
                    cubrir.Add(((long)(ci + a) << 32) | (uint)(cj + b));
        }
        // Subceldas de la acera (índice global de subcelda): las de siempre (celdas rebajadas en las que el terreno
        // queda por debajo de la cota de referencia) y, para enderezar el borde, las de la franja donde el terreno sigue
        // a ras (no se alarga sobre terreno más bajo: quedaría volando)
        float dsx = dx / n, dsz = dz / n;
        float Terr(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + tp.y;
        var sub = new HashSet<long>();
        foreach (var k in cubrir)
        {
            int ci = (int)(k >> 32), cj = (int)(k & 0xffffffff);
            if (ci < 0 || cj < 0 || ci >= res - 1 || cj >= res - 1) continue;
            bool rebajada = zona.Contains(k);
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    var c = new Vector2(tp.x + (ci * n + a + 0.5f) * dsx, tp.z + (cj * n + b + 0.5f) * dsz);
                    bool dentro = false, cerca = false;
                    foreach (var e in edificio)
                    {
                        float dd = DistanciaDentro(e, c);
                        if (dd > 0) { dentro = true; break; }
                        if (dd > -Ancho) cerca = true;
                    }
                    if (dentro) continue;
                    float dif = Ref(c.x, c.y) - Terr(c.x, c.y);
                    bool vale = (rebajada && dif >= 0.02f) || (cerca && dif > -0.3f && dif < 0.06f && EnFranja(c));
                    if (vale) sub.Add(((long)(ci * n + a) << 32) | (uint)(cj * n + b));
                }
        }
        // Cota de la acera: la de referencia o, si el terreno queda más alto, la del terreno (en las pendientes la
        // bilineal de la referencia y los triángulos del Terrain difieren unos cm y el terreno asomaba por encima)
        float Cota(float x, float z) => Mathf.Max(Ref(x, z), Terr(x, z)) + 0.02f;
        // UV en los ejes del edificio (las baldosas, paralelas a las fachadas)
        float cg = Mathf.Cos(m.giro * Mathf.Deg2Rad), sg = Mathf.Sin(m.giro * Mathf.Deg2Rad);
        Vector2 UV(float x, float z) => new Vector2(x * cg + z * sg, -x * sg + z * cg) / 2f;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (var k in sub)
        {
            int si = (int)(k >> 32), sj = (int)(k & 0xffffffff);
            float xa = tp.x + si * dsx, xb = xa + dsx, za = tp.z + sj * dsz, zb = za + dsz;
            int v0 = verts.Count;
            foreach (var (x, z) in new[] { (xa, za), (xa, zb), (xb, zb), (xb, za) })
            {
                verts.Add(new Vector3(x, Cota(x, z), z) - padre.position);
                uvs.Add(UV(x, z));
            }
            tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
            // Canto: en los lados sin vecina, si la acera queda por encima del terreno, una cara vertical hasta él
            // (si no, se veía el hueco por debajo del borde)
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (sub.Contains(((long)(si + di) << 32) | (uint)(sj + dj))) continue;
                Vector2 p0, p1;
                if (di == 1) { p0 = new Vector2(xb, za); p1 = new Vector2(xb, zb); }
                else if (di == -1) { p0 = new Vector2(xa, zb); p1 = new Vector2(xa, za); }
                else if (dj == 1) { p0 = new Vector2(xb, zb); p1 = new Vector2(xa, zb); }
                else { p0 = new Vector2(xa, za); p1 = new Vector2(xb, za); }
                float y0 = Cota(p0.x, p0.y), y1 = Cota(p1.x, p1.y);
                float t0 = Terr(p0.x, p0.y), t1 = Terr(p1.x, p1.y);
                if (y0 - t0 < 0.03f && y1 - t1 < 0.03f) continue;
                int w0 = verts.Count;
                verts.Add(new Vector3(p0.x, y0, p0.y) - padre.position);
                verts.Add(new Vector3(p1.x, y1, p1.y) - padre.position);
                verts.Add(new Vector3(p1.x, Mathf.Min(t1, y1) - 0.1f, p1.y) - padre.position);
                verts.Add(new Vector3(p0.x, Mathf.Min(t0, y0) - 0.1f, p0.y) - padre.position);
                float l = Vector2.Distance(p0, p1) / 2f;
                uvs.Add(new Vector2(0, y0 / 2f)); uvs.Add(new Vector2(l, y1 / 2f));
                uvs.Add(new Vector2(l, (Mathf.Min(t1, y1) - 0.1f) / 2f)); uvs.Add(new Vector2(0, (Mathf.Min(t0, y0) - 0.1f) / 2f));
                // hacia fuera de la subcelda (p0 → p1 deja la subcelda a la izquierda vista desde arriba)
                tris.AddRange(new[] { w0, w0 + 1, w0 + 2, w0, w0 + 2, w0 + 3 });
            }
        }
        if (tris.Count == 0) return;
        var mesh = new Mesh { name = $"{m.codigo}_Acera_Sotanos", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject("Acera sótanos");
        go.transform.SetParent(padre, false);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialesPolitecnica.Carpeta}/Pavimento_Patio.mat");
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
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
