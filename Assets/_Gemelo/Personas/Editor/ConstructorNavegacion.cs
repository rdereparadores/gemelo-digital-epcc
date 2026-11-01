// Genera la malla de navegación del campus (interiores de los siete edificios con todas sus plantas y escaleras,
// pasarelas, aceras, aparcamientos y el terreno de alrededor) y la guarda como asset. En ejecución la carga el
// módulo Personas con NavMesh.AddNavMeshData, sin componentes en la escena.
//
// Solo se camina sobre lo que es suelo: suelos y peldaños de los edificios, aceras, calzadas, losas, escaleras y el
// terreno. Lo demás (muros, mobiliario, falsos techos, cubiertas…) se rasteriza como «no caminable»: estorba, pero
// encima no se genera malla (así no salen islas sobre las mesas). Las hojas de puerta y los triggers de los locales
// no cuentan.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace Gemelo
{
    public static class ConstructorNavegacion
    {
        public const string RutaAsset = "Assets/_Gemelo/Personas/Navegacion/Campus.asset";

        // Áreas de la malla (índices de NavMeshAreas; las de 3 y 4 no tienen nombre en el proyecto: el coste lo pone
        // el filtro de las consultas en ejecución, ver Navegacion.cs)
        public const int AreaSuelo = 0, AreaNoCaminable = 1, AreaTierra = 3, AreaCalzada = 4;

        // Zona de la Politécnica, sus aparcamientos, la parada del autobús y los accesos peatonales
        public static readonly Bounds Zona = new(new Vector3(400f, 22f, 562f), new Vector3(300f, 70f, 290f));

        // Lo que nunca es suelo aunque tenga caras horizontales accesibles. El resto de la geometría fija sí lo es
        // (los rellanos de las entradas, p. ej., van en las mallas «Cubierta_Granito…» del edificio); las islas que
        // salen en lo alto de muros y cubiertas quedan sueltas y no se usan (ver Navegacion.PuntoEn).
        static readonly string[] obstaculos =
        {
            "Falso_Techo", "Barandilla", "Vidrio", "Persiana", "Carpinteria", "Lucernario", "Placa_FV", "Marco_FV",
            "Equipo", "Farola", "Pergola", "Malla", "Senal", "Mupi", "Cartel", "Reja", "Lama", "Bajante", "Albardilla",
        };

        [MenuItem("Tools/Gemelo/Malla de navegación")]
        public static void ConstruirDesdeMenu()
        {
            var r = Construir();
            EditorUtility.DisplayDialog("Malla de navegación", r, "Vale");
        }

        public static string Construir(float radio = 0.2f, float altura = 1.8f, float escalon = 0.36f, float pendiente = 40f, float voxel = 0.08f, int tesela = 512, bool mallasRender = false)
        {
            var reloj = Stopwatch.StartNew();
            var fuentes = new List<NavMeshBuildSource>();
            int capas = ~(1 << 2);   // Ignore Raycast: los triggers de los locales
            NavMeshBuilder.CollectSources(Zona, capas, mallasRender ? NavMeshCollectGeometry.RenderMeshes : NavMeshCollectGeometry.PhysicsColliders, AreaNoCaminable,
                new List<NavMeshBuildMarkup>(), fuentes);

            var buenas = new List<NavMeshBuildSource>(fuentes.Count);
            int nSuelo = 0, nTierra = 0, nCalzada = 0, nObst = 0, fuera = 0;
            foreach (var f in fuentes)
            {
                var comp = f.component as Component;
                if (comp == null) { buenas.Add(f); continue; }
                if (comp is Collider c && (c.isTrigger || c is CharacterController)) { fuera++; continue; }
                if (comp.GetComponentInParent<Puerta>() != null) { fuera++; continue; }
                if (comp.GetComponentInParent<Local>() != null) { fuera++; continue; }
                if (comp is MeshFilter mf && (mf.GetComponent<Collider>() == null && !(comp.GetComponentInParent<Terrain>() != null))) { fuera++; continue; }
                var s = f;
                s.area = Area(comp);
                switch (s.area)
                {
                    case AreaSuelo: nSuelo++; break;
                    case AreaTierra: nTierra++; break;
                    case AreaCalzada: nCalzada++; break;
                    default: nObst++; break;
                }
                buenas.Add(s);
            }

            var ajustes = NavMesh.GetSettingsByID(0);
            ajustes.agentRadius = radio;
            ajustes.agentHeight = altura;
            ajustes.agentSlope = pendiente;
            ajustes.agentClimb = escalon;
            ajustes.overrideVoxelSize = true;
            ajustes.voxelSize = voxel;
            ajustes.overrideTileSize = true;
            ajustes.tileSize = tesela;
            ajustes.minRegionArea = 1.5f;
            ajustes.buildHeightMesh = true;

            var datos = NavMeshBuilder.BuildNavMeshData(ajustes, buenas, Zona, Vector3.zero, Quaternion.identity);
            if (datos == null) return "No se pudo construir la malla";
            datos.name = "Campus";

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RutaAsset)!);
            var previo = AssetDatabase.LoadAssetAtPath<NavMeshData>(RutaAsset);
            if (previo != null)
            {
                EditorUtility.CopySerialized(datos, previo);
                EditorUtility.SetDirty(previo);
                AssetDatabase.SaveAssets();
                Object.DestroyImmediate(datos);
            }
            else AssetDatabase.CreateAsset(datos, RutaAsset);
            AssetDatabase.ImportAsset(RutaAsset);
            var malla = AssetDatabase.LoadAssetAtPath<NavMeshData>(RutaAsset);

            var rec = Recursos();
            rec.malla = malla;
            int enlaces = Coser(malla, rec, out int sueltos);
            EditorUtility.SetDirty(rec);
            AssetDatabase.SaveAssets();

            var msg = $"Malla de navegación en {reloj.Elapsed.TotalSeconds:F0} s: {buenas.Count} fuentes " +
                      $"({nSuelo} suelo, {nTierra} terreno, {nCalzada} calzada, {nObst} obstáculos; {fuera} descartadas); " +
                      $"{enlaces} enlaces de costura, {sueltos} trozos sueltos de más de 4 m²";
            Debug.Log("[Personas] " + msg);
            return msg;
        }

        public const string RutaRecursos = "Assets/_Gemelo/Personas/Resources/RecursosPersonas.asset";

        public static RecursosPersonas Recursos()
        {
            var r = AssetDatabase.LoadAssetAtPath<RecursosPersonas>(RutaRecursos);
            if (r != null) return r;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RutaRecursos)!);
            r = ScriptableObject.CreateInstance<RecursosPersonas>();
            AssetDatabase.CreateAsset(r, RutaRecursos);
            return r;
        }

        // ───────────── Costuras ─────────────
        // El voxelizado deja a veces dos trozos de malla separados por unos centímetros donde se puede pasar (el
        // arranque de un tramo de escalera desde el rellano, p. ej.). Se buscan los trozos que no están unidos al
        // principal y se cosen con enlaces cortos donde hay un vértice de cada lado a menos de 0,9 m, casi a la misma
        // altura y sin nada en medio (un muro).
        const float DistCostura = 0.9f, DyCostura = 0.45f, AreaMinima = 4f;

        static int Coser(NavMeshData malla, RecursosPersonas rec, out int sueltos)
        {
            NavMesh.RemoveAllNavMeshData();
            var inst = NavMesh.AddNavMeshData(malla);
            var tri = NavMesh.CalculateTriangulation();
            inst.Remove();

            // Vértices soldados y componentes conexas (unión-búsqueda)
            var v = tri.vertices;
            var idx = tri.indices;
            var claveA = new Dictionary<Vector3Int, int>();
            var vid = new int[v.Length];
            var pos = new List<Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 50), Mathf.RoundToInt(v[i].y * 50), Mathf.RoundToInt(v[i].z * 50));
                if (!claveA.TryGetValue(k, out int id)) { id = pos.Count; claveA[k] = id; pos.Add(v[i]); }
                vid[i] = id;
            }
            var padre = new int[pos.Count];
            for (int i = 0; i < padre.Length; i++) padre[i] = i;
            int Raiz(int x) { while (padre[x] != x) { padre[x] = padre[padre[x]]; x = padre[x]; } return x; }
            for (int t = 0; t < idx.Length; t += 3)
            {
                int a = Raiz(vid[idx[t]]), b = Raiz(vid[idx[t + 1]]), c = Raiz(vid[idx[t + 2]]);
                padre[b] = a; padre[Raiz(c)] = a;
            }
            var area = new Dictionary<int, float>();
            for (int t = 0; t < idx.Length; t += 3)
            {
                int r = Raiz(vid[idx[t]]);
                var A = v[idx[t]]; var B = v[idx[t + 1]]; var C = v[idx[t + 2]];
                area[r] = area.GetValueOrDefault(r) + Vector3.Cross(B - A, C - A).magnitude * 0.5f;
            }
            int principal = -1; float maxA = 0;
            foreach (var kv in area) if (kv.Value > maxA) { maxA = kv.Value; principal = kv.Key; }
            var verticesDe = new Dictionary<int, List<int>>();
            for (int i = 0; i < pos.Count; i++)
            {
                int r = Raiz(i);
                if (!verticesDe.TryGetValue(r, out var l)) verticesDe[r] = l = new List<int>();
                l.Add(i);
            }

            // Rejilla (1 m) con los vértices de lo ya conectado
            var rejilla = new Dictionary<Vector2Int, List<int>>();
            void Meter(int comp)
            {
                foreach (int i in verticesDe[comp])
                {
                    var c = new Vector2Int(Mathf.FloorToInt(pos[i].x), Mathf.FloorToInt(pos[i].z));
                    if (!rejilla.TryGetValue(c, out var l)) rejilla[c] = l = new List<int>();
                    l.Add(i);
                }
            }
            Meter(principal);
            var conectadas = new HashSet<int> { principal };
            var pendientes = new List<int>();
            foreach (var kv in area) if (kv.Key != principal && kv.Value >= AreaMinima) pendientes.Add(kv.Key);
            pendientes.Sort((a, b) => area[b].CompareTo(area[a]));

            var ea = new List<Vector3>();
            var eb = new List<Vector3>();
            var golpes = new RaycastHit[16];
            bool Libre(Vector3 a, Vector3 b)
            {
                var o = a + Vector3.up * 1.2f;
                var d = b - a;
                float l = d.magnitude;
                if (l < 1e-3f) return true;
                int n = Physics.RaycastNonAlloc(o, d / l, golpes, l, ~(1 << 2), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (golpes[i].collider.GetComponentInParent<Puerta>() == null) return false;
                n = Physics.RaycastNonAlloc(b + Vector3.up * 1.2f, -d / l, golpes, l, ~(1 << 2), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (golpes[i].collider.GetComponentInParent<Puerta>() == null) return false;
                return true;
            }

            // Primera pasada: solo costuras sin nada en medio. Segunda: también a través de un tabique delgado, para los
            // trozos grandes (> 20 m²) que siguen sueltos (algún tramo de escalera del modelo queda tapiado por el muro
            // de la caja; la gente lo atraviesa por la costura, que es corta).
            // Tercera: plantas enteras sin escalera en la malla (el modelo de alguna escalera no se puede andar): se
            // unen con la planta de abajo por un «ascensor», un enlace vertical que las personas cruzan de golpe.
            bool forzar = false, ascensor = false;
            var locales = Object.FindObjectsByType<Local>();
            var interior = new Dictionary<int, bool>();
            bool Interior(int comp)
            {
                if (interior.TryGetValue(comp, out var si)) return si;
                var vs = verticesDe[comp];
                si = false;
                for (int k = 0; k < vs.Count && k < 400 && !si; k += Mathf.Max(1, vs.Count / 40))
                {
                    var q = pos[vs[k]];
                    foreach (var l in locales)
                        if (Mathf.Abs(q.y - l.cotaSuelo) < 0.4f && l.Contiene(q)) { si = true; break; }
                }
                return interior[comp] = si;
            }
            bool cambio = true;
            while (cambio || !forzar || !ascensor)
            {
                if (!cambio && forzar && !ascensor) { ascensor = true; cambio = true; }
                if (!cambio) { forzar = true; pendientes.RemoveAll(c => area[c] < 20f); }
                cambio = false;
                for (int p = pendientes.Count - 1; p >= 0; p--)
                {
                    int comp = pendientes[p];
                    if (ascensor && !Interior(comp)) continue;
                    var elegidos = new List<(Vector3 a, Vector3 b, float d)>();
                    foreach (int i in verticesDe[comp])
                    {
                        var q = pos[i];
                        int cx = Mathf.FloorToInt(q.x), cz = Mathf.FloorToInt(q.z);
                        float mejor = ascensor ? 1.5f : DistCostura; int mj = -1;
                        for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!rejilla.TryGetValue(new Vector2Int(cx + dx, cz + dz), out var l)) continue;
                            foreach (int j in l)
                            {
                                var w = pos[j];
                                float dyv = Mathf.Abs(w.y - q.y);
                                if (ascensor ? dyv < 2.5f || dyv > 6f : dyv > DyCostura) continue;
                                float h = new Vector2(w.x - q.x, w.z - q.z).magnitude;
                                if (h < mejor) { mejor = h; mj = j; }
                            }
                        }
                        if (mj >= 0) elegidos.Add((q, pos[mj], mejor));
                    }
                    if (elegidos.Count == 0) continue;
                    elegidos.Sort((x, y) => x.d.CompareTo(y.d));
                    var puestos = new List<Vector3>();
                    foreach (var e in elegidos)
                    {
                        if (puestos.Count >= (ascensor ? 1 : 4)) break;
                        bool lejos = true;
                        foreach (var u in puestos) if ((u - e.a).sqrMagnitude < 9f) { lejos = false; break; }
                        if (!lejos || !forzar && !Libre(e.a, e.b)) continue;
                        puestos.Add(e.a);
                        ea.Add(e.a);
                        eb.Add(e.b);
                    }
                    if (puestos.Count == 0) continue;
                    conectadas.Add(comp);
                    Meter(comp);
                    pendientes.RemoveAt(p);
                    cambio = true;
                }
            }
            rec.enlacesA = ea.ToArray();
            rec.enlacesB = eb.ToArray();
            sueltos = pendientes.Count;
            return ea.Count;
        }

        static int Area(Component comp)
        {
            if (comp is TerrainCollider || comp is Terrain) return AreaTierra;
            var n = comp.name;
            if (n.StartsWith("Calzada")) return AreaCalzada;
            if (EnMobiliario(comp.transform)) return AreaNoCaminable;
            foreach (var o in obstaculos)
                if (n.Contains(o)) return AreaNoCaminable;
            return AreaSuelo;
        }

        static bool EnMobiliario(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name == "Mobiliario") return true;
            return false;
        }
    }
}
