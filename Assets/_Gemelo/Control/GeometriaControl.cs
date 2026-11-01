// Geometría de las superposiciones del módulo de control: triangulación de las plantas de los locales
// (ear clipping), polígonos desplazados hacia dentro, huella exterior de cada edificio (rasterizando sus
// locales) y las mallas que dibujan la capa de color, los contornos y las auras. Todo se calcula una vez
// por local o edificio y se guarda en caché.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public static class GeometriaControl
    {
        /// <summary>Altura sobre el suelo del plano de color de las estancias: por encima de mesas, sillas y
        /// bancos de laboratorio y por debajo de la cabeza de quien está de pie o sentado.</summary>
        public const float AlturaCapa = 1.0f;

        static Material material;
        static readonly int idColor = Shader.PropertyToID("_Color");
        static readonly int idAncho = Shader.PropertyToID("_Ancho");
        public static int IdColor => idColor;
        public static int IdAncho => idAncho;

        public static Material Material
        {
            get
            {
                if (material != null) return material;
                var sh = Resources.Load<Shader>("GemeloControl/Superposicion");
                if (sh == null) sh = Shader.Find("Gemelo/Control/Superposicion");
                material = new Material(sh) { name = "Superposicion (Control)", enableInstancing = true };
                return material;
            }
        }

        // Play Mode sin recarga de dominio: las cachés de una sesión no valen para la siguiente
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar()
        {
            mallasLocal.Clear();
            huellas.Clear();
            auras.Clear();
        }

        public static RenderParams Parametros(MaterialPropertyBlock mpb) => new RenderParams(Material)
        {
            matProps = mpb,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            layer = 0,
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f),
        };

        // ───────────────────────── Polígonos ─────────────────────────

        public static float AreaFirmada(IList<Vector2> p)
        {
            double a = 0;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += (double)p[j].x * p[i].y - (double)p[i].x * p[j].y;
            return (float)(a * 0.5);
        }

        static float Cruz(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Quita vértices repetidos y alineados y lo deja en sentido antihorario (en X-Z).</summary>
        public static List<Vector2> Limpiar(IList<Vector2> entrada)
        {
            var p = new List<Vector2>(entrada.Count);
            foreach (var v in entrada)
                if (p.Count == 0 || (p[p.Count - 1] - v).sqrMagnitude > 1e-6f) p.Add(v);
            if (p.Count > 1 && (p[0] - p[p.Count - 1]).sqrMagnitude <= 1e-6f) p.RemoveAt(p.Count - 1);
            bool cambio = true;
            while (cambio && p.Count > 3)
            {
                cambio = false;
                for (int i = 0; i < p.Count && p.Count > 3; i++)
                {
                    Vector2 a = p[(i + p.Count - 1) % p.Count], b = p[i], c = p[(i + 1) % p.Count];
                    Vector2 ab = b - a, bc = c - b;
                    float l = ab.magnitude * bc.magnitude;
                    if (l < 1e-8f || Mathf.Abs(Cruz(ab, bc)) / l < 1e-3f && Vector2.Dot(ab, bc) > 0)
                    {
                        p.RemoveAt(i); cambio = true; i--;
                    }
                }
            }
            if (AreaFirmada(p) < 0) p.Reverse();
            return p;
        }

        static bool EnTriangulo(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cruz(b - a, q - a), d2 = Cruz(c - b, q - b), d3 = Cruz(a - c, q - c);
            return d1 >= -1e-7f && d2 >= -1e-7f && d3 >= -1e-7f;
        }

        /// <summary>Triangula un polígono simple antihorario por recorte de orejas. Añade índices a «tris».</summary>
        public static void Triangular(IList<Vector2> p, List<int> tris, int desfase = 0)
        {
            int n = p.Count;
            if (n < 3) return;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int vueltas = 0;
            while (idx.Count > 3 && vueltas++ < 4 * n * n)
            {
                bool cortada = false;
                int m = idx.Count;
                for (int i = 0; i < m; i++)
                {
                    int ia = idx[(i + m - 1) % m], ib = idx[i], ic = idx[(i + 1) % m];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if (Cruz(b - a, c - b) <= 1e-7f) continue;   // vértice reflejo o alineado
                    bool tapa = false;
                    for (int j = 0; j < m && !tapa; j++)
                    {
                        int q = idx[j];
                        if (q == ia || q == ib || q == ic) continue;
                        var v = p[q];
                        if ((v - a).sqrMagnitude < 1e-8f || (v - b).sqrMagnitude < 1e-8f || (v - c).sqrMagnitude < 1e-8f) continue;
                        tapa = EnTriangulo(v, a, b, c);
                    }
                    if (tapa) continue;
                    tris.Add(desfase + ia); tris.Add(desfase + ib); tris.Add(desfase + ic);
                    idx.RemoveAt(i);
                    cortada = true;
                    break;
                }
                if (!cortada)
                {
                    // Polígono degenerado (se toca a sí mismo): se quita el vértice más plano y se sigue
                    int peor = 0; float min = float.MaxValue;
                    for (int i = 0; i < m; i++)
                    {
                        Vector2 a = p[idx[(i + m - 1) % m]], b = p[idx[i]], c = p[idx[(i + 1) % m]];
                        float k = Mathf.Abs(Cruz(b - a, c - b));
                        if (k < min) { min = k; peor = i; }
                    }
                    idx.RemoveAt(peor);
                }
            }
            if (idx.Count == 3) { tris.Add(desfase + idx[0]); tris.Add(desfase + idx[1]); tris.Add(desfase + idx[2]); }
        }

        /// <summary>Desplaza un polígono antihorario hacia dentro (d &gt; 0) o hacia fuera (d &lt; 0), con inglete limitado.</summary>
        public static Vector2[] Desplazar(IList<Vector2> p, float d)
        {
            int n = p.Count;
            var r = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                Vector2 d1 = (b - a).normalized, d2 = (c - b).normalized;
                Vector2 n1 = new(-d1.y, d1.x), n2 = new(-d2.y, d2.x);   // hacia dentro (izquierda)
                Vector2 m = (n1 + n2);
                if (m.sqrMagnitude < 1e-6f) m = n1; else m.Normalize();
                float cos = Mathf.Max(0.35f, Vector2.Dot(m, n1));
                r[i] = b + m * (d / cos);
            }
            return r;
        }

        // ───────────────────────── Mallas de los locales ─────────────────────────

        static readonly Dictionary<InfoLocal, Mesh> mallasLocal = new();

        /// <summary>Malla del local para la capa de color y el resaltado. Submalla 0: plano a AlturaCapa con un
        /// borde más opaco y los costados (alfa de 0 en el suelo a 0,35 arriba); submalla 1: solo el contorno.
        /// Los vértices llevan color blanco con el alfa de cada parte: el color lo pone _Color.</summary>
        public static Mesh MallaLocal(InfoLocal il)
        {
            if (mallasLocal.TryGetValue(il, out var malla)) return malla;
            var pol = il.local.poligono;
            List<Vector2> p;
            if (pol == null || pol.Length < 3)
            {
                var b = il.local.GetComponent<BoxCollider>().bounds;
                p = new List<Vector2> { new(b.min.x, b.min.z), new(b.max.x, b.min.z), new(b.max.x, b.max.z), new(b.min.x, b.max.z) };
            }
            else p = Limpiar(pol);

            float y0 = il.local.cotaSuelo + 0.03f, y1 = il.local.cotaSuelo + AlturaCapa;
            float ancho = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, il.area)) * 0.06f, 0.12f, 0.3f);
            var dentro = Desplazar(p, ancho);
            var v = new List<Vector3>();
            var col = new List<Color32>();
            var t0 = new List<int>();
            var t1 = new List<int>();
            int n = p.Count;

            // Plano (alfa 0,45)
            int f = v.Count;
            for (int i = 0; i < n; i++) { v.Add(new Vector3(p[i].x, y1, p[i].y)); col.Add(Alfa(0.45f)); }
            Triangular(p, t0, f);

            // Borde del plano (alfa 0,9): del contorno al polígono interior
            int bo = v.Count;
            for (int i = 0; i < n; i++)
            {
                v.Add(new Vector3(p[i].x, y1 + 0.004f, p[i].y)); col.Add(Alfa(0.9f));
                v.Add(new Vector3(dentro[i].x, y1 + 0.004f, dentro[i].y)); col.Add(Alfa(0.9f));
            }
            Cinta(t0, bo, n);
            Cinta(t1, bo, n);

            // Costados (alfa de 0 abajo a 0,35 arriba)
            int co = v.Count;
            for (int i = 0; i < n; i++)
            {
                v.Add(new Vector3(p[i].x, y1, p[i].y)); col.Add(Alfa(0.35f));
                v.Add(new Vector3(p[i].x, y0, p[i].y)); col.Add(Alfa(0f));
            }
            Cinta(t0, co, n);

            malla = new Mesh { name = "Capa_" + il.clave };
            malla.SetVertices(v);
            malla.SetColors(col);
            malla.subMeshCount = 2;
            malla.SetTriangles(t0, 0);
            malla.SetTriangles(t1, 1);
            malla.RecalculateBounds();
            mallasLocal[il] = malla;
            return malla;
        }

        static Color32 Alfa(float a) => new(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));

        // Cinta cerrada entre pares de vértices (2i, 2i+1) desde «o»
        static void Cinta(List<int> t, int o, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a = o + 2 * i, b = o + 2 * i + 1, c = o + 2 * j, d = o + 2 * j + 1;
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(b); t.Add(c); t.Add(d);
            }
        }

        // ───────────────────────── Huella y aura de los edificios ─────────────────────────

        static readonly Dictionary<InfoEdificio, List<Vector2[]>> huellas = new();
        static readonly Dictionary<InfoEdificio, Mesh> auras = new();

        /// <summary>Contornos exteriores (y patios) de la planta del edificio, en XZ del mundo, con el macizo a la izquierda.</summary>
        public static List<Vector2[]> Huella(InfoEdificio e)
        {
            if (huellas.TryGetValue(e, out var h)) return h;
            h = CalcularHuella(e);
            huellas[e] = h;
            return h;
        }

        static List<Vector2[]> CalcularHuella(InfoEdificio e)
        {
            var pols = new List<Vector2[]>();
            foreach (var l in e.locales)
                if (l.local.poligono != null && l.local.poligono.Length >= 3) pols.Add(l.local.poligono);
            var res = new List<Vector2[]>();
            if (pols.Count == 0) return res;

            // Orientación dominante de los muros (los edificios van girados respecto a los ejes del mundo)
            var hist = new float[90];
            foreach (var pol in pols)
                for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
                {
                    var d = pol[i] - pol[j];
                    float l = d.magnitude;
                    if (l < 0.5f) continue;
                    float a = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 90f);
                    hist[Mathf.Clamp((int)a, 0, 89)] += l;
                }
            int mejor = 0;
            for (int i = 1; i < 90; i++) if (hist[i] > hist[mejor]) mejor = i;
            double sx = 0, sy = 0;   // media circular (periodo 90°) de las aristas cercanas al máximo
            foreach (var pol in pols)
                for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
                {
                    var d = pol[i] - pol[j];
                    float l = d.magnitude;
                    if (l < 0.5f) continue;
                    float a = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 90f);
                    if (Mathf.Abs(Mathf.DeltaAngle(a * 4f, (mejor + 0.5f) * 4f)) > 12f) continue;
                    sx += l * System.Math.Cos(a * 4 * Mathf.Deg2Rad); sy += l * System.Math.Sin(a * 4 * Mathf.Deg2Rad);
                }
            float ang = (float)(System.Math.Atan2(sy, sx) / 4.0);
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            Vector2 AMarco(Vector2 p) => new(p.x * cs + p.y * sn, -p.x * sn + p.y * cs);
            Vector2 AMundo(Vector2 q) => new(q.x * cs - q.y * sn, q.x * sn + q.y * cs);

            // Rejilla de 0,5 m en el marco del edificio
            const float celda = 0.5f;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            var marco = new List<Vector2[]>(pols.Count);
            foreach (var pol in pols)
            {
                var q = new Vector2[pol.Length];
                for (int i = 0; i < pol.Length; i++) { q[i] = AMarco(pol[i]); min = Vector2.Min(min, q[i]); max = Vector2.Max(max, q[i]); }
                marco.Add(q);
            }
            min -= Vector2.one * 4f; max += Vector2.one * 4f;
            int W = Mathf.CeilToInt((max.x - min.x) / celda), H = Mathf.CeilToInt((max.y - min.y) / celda);
            var g = new bool[W * H];
            foreach (var q in marco)
            {
                Vector2 a = q[0], b = q[0];
                foreach (var v in q) { a = Vector2.Min(a, v); b = Vector2.Max(b, v); }
                int x0 = Mathf.Max(0, (int)((a.x - min.x) / celda)), x1 = Mathf.Min(W - 1, (int)((b.x - min.x) / celda) + 1);
                int z0 = Mathf.Max(0, (int)((a.y - min.y) / celda)), z1 = Mathf.Min(H - 1, (int)((b.y - min.y) / celda) + 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (g[z * W + x]) continue;
                        var c = new Vector2(min.x + (x + 0.5f) * celda, min.y + (z + 0.5f) * celda);
                        if (Dentro(q, c)) g[z * W + x] = true;
                    }
            }
            // Cierre morfológico: dilata 1,5 m (tapa muros y juntas) y erosiona 1 m (queda 0,5 m por fuera)
            g = Dilatar(g, W, H, 3, true);
            g = Dilatar(g, W, H, 2, false);

            // Aristas entre celdas llenas y vacías, con lo lleno a la izquierda
            var sig = new Dictionary<int, List<int>>();
            void Arista(int ax, int az, int bx, int bz)
            {
                int ka = az * (W + 1) + ax, kb = bz * (W + 1) + bx;
                if (!sig.TryGetValue(ka, out var l)) sig[ka] = l = new List<int>(1);
                l.Add(kb);
            }
            bool Lleno(int x, int z) => x >= 0 && z >= 0 && x < W && z < H && g[z * W + x];
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    if (!g[z * W + x]) continue;
                    if (!Lleno(x, z - 1)) Arista(x, z, x + 1, z);
                    if (!Lleno(x + 1, z)) Arista(x + 1, z, x + 1, z + 1);
                    if (!Lleno(x, z + 1)) Arista(x + 1, z + 1, x, z + 1);
                    if (!Lleno(x - 1, z)) Arista(x, z + 1, x, z);
                }
            while (sig.Count > 0)
            {
                int inicio = -1;
                foreach (var k in sig.Keys) { inicio = k; break; }
                var lazo = new List<Vector2>();
                int v = inicio, guarda = 0;
                do
                {
                    lazo.Add(new Vector2(min.x + (v % (W + 1)) * celda, min.y + (v / (W + 1)) * celda));
                    var l = sig[v];
                    int s = l[l.Count - 1];
                    l.RemoveAt(l.Count - 1);
                    if (l.Count == 0) sig.Remove(v);
                    v = s;
                } while (v != inicio && sig.ContainsKey(v) && guarda++ < 1_000_000);
                if (lazo.Count < 4) continue;
                // Simplifica: solo los cambios de dirección
                var simple = new List<Vector2>();
                for (int i = 0; i < lazo.Count; i++)
                {
                    Vector2 a = lazo[(i + lazo.Count - 1) % lazo.Count], b = lazo[i], c = lazo[(i + 1) % lazo.Count];
                    if (Mathf.Abs(Cruz(b - a, c - b)) > 1e-4f) simple.Add(b);
                }
                float per = 0;
                for (int i = 0; i < simple.Count; i++) per += (simple[(i + 1) % simple.Count] - simple[i]).magnitude;
                if (simple.Count < 4 || per < 14f) continue;
                var mundo = new Vector2[simple.Count];
                for (int i = 0; i < simple.Count; i++) mundo[i] = AMundo(simple[i]);
                res.Add(mundo);
            }
            return res;
        }

        static bool Dentro(Vector2[] p, Vector2 q)
        {
            bool d = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) d = !d;
            return d;
        }

        // Dilatación (o erosión) con un cuadrado de lado 2r+1, separable
        static bool[] Dilatar(bool[] g, int W, int H, int r, bool dilatar)
        {
            var t = new bool[g.Length];
            var o = new bool[g.Length];
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    bool acc = !dilatar;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = x + k;
                        bool c = xx >= 0 && xx < W && g[z * W + xx];
                        if (dilatar ? c : !c) { acc = dilatar; break; }
                    }
                    t[z * W + x] = acc;
                }
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    bool acc = !dilatar;
                    for (int k = -r; k <= r; k++)
                    {
                        int zz = z + k;
                        bool c = zz >= 0 && zz < H && t[zz * W + x];
                        if (dilatar ? c : !c) { acc = dilatar; break; }
                    }
                    o[z * W + x] = acc;
                }
            return o;
        }

        /// <summary>Aura del edificio: submalla 0, anillo sobre el suelo (se ensancha hacia fuera con _Ancho);
        /// submalla 1, banda vertical desde el suelo hasta la cubierta que se desvanece hacia arriba.</summary>
        public static Mesh Aura(InfoEdificio e)
        {
            if (auras.TryGetValue(e, out var malla)) return malla;
            var v = new List<Vector3>();
            var nor = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color32>();
            var t0 = new List<int>();
            var t1 = new List<int>();
            float techo = e.limites.max.y + 0.5f;
            foreach (var lazo0 in Huella(e))
            {
                // Subdivide en tramos de 3 m para seguir el terreno
                var lazo = new List<Vector2>();
                var normal = new List<Vector2>();
                int n0 = lazo0.Length;
                for (int i = 0; i < n0; i++)
                {
                    Vector2 a = lazo0[i], b = lazo0[(i + 1) % n0];
                    int pasos = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 3f));
                    for (int k = 0; k < pasos; k++) lazo.Add(Vector2.Lerp(a, b, k / (float)pasos));
                }
                int n = lazo.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = lazo[(i + n - 1) % n], b = lazo[i], c = lazo[(i + 1) % n];
                    Vector2 d1 = (b - a).normalized, d2 = (c - b).normalized;
                    Vector2 n1 = new(d1.y, -d1.x), n2 = new(d2.y, -d2.x);   // hacia fuera (derecha)
                    Vector2 m = n1 + n2;
                    m = m.sqrMagnitude < 1e-6f ? n1 : m.normalized;
                    normal.Add(m / Mathf.Max(0.5f, Vector2.Dot(m, n1)));
                }
                int o = v.Count;
                for (int i = 0; i < n; i++)
                {
                    float y = Suelo(e, lazo[i] + normal[i] * 0.6f);
                    var nm = new Vector3(normal[i].x, 0, normal[i].y);
                    var p = new Vector3(lazo[i].x, y, lazo[i].y);
                    // anillo: interior y exterior (desplazado por el sombreador)
                    v.Add(p + Vector3.up * 0.12f); nor.Add(nm); uv.Add(new Vector2(0, 0)); col.Add(Alfa(1f));
                    v.Add(p + Vector3.up * 0.12f); nor.Add(nm); uv.Add(new Vector2(1, 0)); col.Add(Alfa(0.15f));
                    // banda
                    v.Add(p + Vector3.up * 0.05f); nor.Add(nm); uv.Add(Vector2.zero); col.Add(Alfa(0.75f));
                    v.Add(new Vector3(p.x, Mathf.Max(techo, y + 3f), p.z)); nor.Add(nm); uv.Add(Vector2.zero); col.Add(Alfa(0f));
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int a = o + 4 * i, b = o + 4 * j;
                    t0.Add(a); t0.Add(b); t0.Add(a + 1);
                    t0.Add(a + 1); t0.Add(b); t0.Add(b + 1);
                    t1.Add(a + 2); t1.Add(b + 2); t1.Add(a + 3);
                    t1.Add(a + 3); t1.Add(b + 2); t1.Add(b + 3);
                }
            }
            malla = new Mesh { name = "Aura_" + e.codigo, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            malla.SetVertices(v);
            malla.SetNormals(nor);
            malla.SetUVs(0, uv);
            malla.SetColors(col);
            malla.subMeshCount = 2;
            malla.SetTriangles(t0, 0);
            malla.SetTriangles(t1, 1);
            malla.bounds = new Bounds(e.limites.center, e.limites.size + Vector3.one * 200f);
            auras[e] = malla;
            return malla;
        }

        /// <summary>Altura del suelo exterior junto al edificio (acera o terreno), sin contar sus cubiertas.</summary>
        static float Suelo(InfoEdificio e, Vector2 p)
        {
            var t = Terrain.activeTerrain;
            float yt = t != null ? t.SampleHeight(new Vector3(p.x, 0, p.y)) + t.transform.position.y : e.edificio.transform.position.y;
            if (Physics.Raycast(new Vector3(p.x, e.limites.max.y + 5f, p.y), Vector3.down, out var hit, 200f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                && hit.point.y < e.edificio.transform.position.y + 1.5f && hit.point.y > yt - 3f)
                return Mathf.Max(hit.point.y, yt);
            return yt;
        }
    }
}
