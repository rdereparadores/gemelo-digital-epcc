// Ventanas y persianas de las estancias con sensor (window_open → SpaceState.windows_open/windows_total, y
// blind_position). Sus hojas, vidrios y persianas se dibujan aquí, con la misma geometría que el generador
// (arquitectura.ventana): las correderas abren deslizando la hoja exterior sobre la de al lado y las de una
// hoja bascular hacia dentro (oscilobatiente); las persianas suben y bajan. Una malla por planta y material,
// colgada de la planta (así se oculta con el corte por plantas), que solo se rehace cuando algo se mueve.
//
// Mientras los .mcam no se regeneren (DatoVentana.en_malla), la malla fundida aún trae esas piezas: entonces solo
// se dibuja lo que difiere de ella (hojas abiertas un poco por fuera, persianas por delante de la de la malla).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Ventanas : MonoBehaviour
    {
        const float VelocidadHoja = 0.6f;       // fracción de apertura por segundo
        const float VelocidadPersiana = 0.12f;  // fracción de persiana por segundo (unos 8 s de recorrido)

        class Ventana
        {
            public DatoVentana d;
            public float abierta, abiertaObj;     // 0–1
            public float persiana, persianaObj;   // fracción bajada
            public bool conPersiana;
        }

        class Planta
        {
            public Transform raiz;
            public readonly List<Ventana> ventanas = new();
            public Mesh malla;
            public MeshRenderer rend;
            public bool sucia = true;
        }

        readonly List<Planta> plantas = new();
        readonly Dictionary<InfoLocal, List<Ventana>> porLocal = new();
        readonly Dictionary<string, Material> materiales = new();
        float siguiente;

        // Búferes de la malla (se reutilizan)
        readonly List<Vector3> vs = new();
        readonly List<Vector3> ns = new();
        readonly List<Vector2> uvs = new();
        readonly List<int>[] tris = { new(), new(), new() };   // carpintería, vidrio, persiana

        /// <summary>Fracción abierta de las ventanas del local (0–1) y persiana media, para quien lo pida.</summary>
        public bool Estado(InfoLocal l, out float abiertas, out float persiana)
        {
            abiertas = persiana = 0;
            if (l == null || !porLocal.TryGetValue(l, out var lista) || lista.Count == 0) return false;
            foreach (var v in lista) { abiertas += v.abiertaObj; persiana += v.persianaObj; }
            abiertas /= lista.Count;
            persiana /= lista.Count;
            return true;
        }

        /// <summary>Rehace las mallas (p. ej. tras cambiar DatoVentana.en_malla en una prueba).</summary>
        public void Reconstruir()
        {
            foreach (var p in plantas) p.sucia = true;
        }

        void Start()
        {
            var porRaiz = new Dictionary<Transform, Planta>();
            foreach (var d in DatosVentanas.Todas)
            {
                if (!d.movil || d.info == null || d.lamas) continue;
                var raiz = d.info.planta.raiz;
                if (!porRaiz.TryGetValue(raiz, out var p))
                {
                    p = new Planta { raiz = raiz };
                    porRaiz[raiz] = p;
                    plantas.Add(p);
                }
                var v = new Ventana { d = d, conPersiana = d.persiana >= 0 };
                v.persiana = v.persianaObj = Mathf.Max(0, d.persiana);
                p.ventanas.Add(v);
                if (!porLocal.TryGetValue(d.info, out var lista)) porLocal[d.info] = lista = new List<Ventana>();
                lista.Add(v);
            }
            // Materiales de la escena por nombre (los de las mallas de los edificios)
            foreach (var e in RegistroCampus.Edificios)
                foreach (var r in e.edificio.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !materiales.ContainsKey(m.name)) materiales[m.name] = m;

            foreach (var p in plantas)
            {
                var go = new GameObject("Ventanas (gemelo)");
                go.transform.SetParent(p.raiz, false);
                p.malla = new Mesh { name = "Ventanas " + p.raiz.name };
                p.malla.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = p.malla;
                p.rend = go.AddComponent<MeshRenderer>();
                var v0 = p.ventanas[0].d;
                p.rend.sharedMaterials = new[]
                {
                    Mat(v0.carpinteria, "Carpinteria_Blanca"), Mat(v0.vidrio, "Vidrio_Ventana"), Mat(v0.material_persiana, "Persiana"),
                };
                p.rend.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        Material Mat(string nombre, string porDefecto)
        {
            if (nombre != null && materiales.TryGetValue(nombre, out var m)) return m;
            return materiales.TryGetValue(porDefecto, out m) ? m : null;
        }

        void OnDestroy()
        {
            foreach (var p in plantas)
            {
                if (p.malla != null) Destroy(p.malla);
                if (p.rend != null) Destroy(p.rend.gameObject);
            }
        }

        void Update()
        {
            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 0.5f;
                LeerSensores();
            }
            float dt = Time.deltaTime;
            foreach (var p in plantas)
            {
                foreach (var v in p.ventanas)
                {
                    if (v.abierta != v.abiertaObj)
                    {
                        v.abierta = Mathf.MoveTowards(v.abierta, v.abiertaObj, VelocidadHoja * dt);
                        p.sucia = true;
                    }
                    if (v.persiana != v.persianaObj)
                    {
                        v.persiana = Mathf.MoveTowards(v.persiana, v.persianaObj, VelocidadPersiana * dt);
                        p.sucia = true;
                    }
                }
                if (p.sucia && p.raiz.gameObject.activeInHierarchy) Construir(p);
            }
        }

        void LeerSensores()
        {
            foreach (var kv in porLocal)
            {
                var st = AmbienteGemelo.Estado(kv.Key);
                if (st == null) continue;
                var lista = kv.Value;
                int n = lista.Count;
                // Ventanas abiertas: la misma fracción que los sensores, empezando por la primera
                int abiertas = st.windows_total > 0 ? Mathf.Clamp(Mathf.RoundToInt((float)st.windows_open / st.windows_total * n), 0, n) : 0;
                if (st.windows_open > 0 && abiertas == 0) abiertas = 1;
                bool hayPersiana = st.Metrica("blind_position", out var mp);
                if (AmbienteGemelo.Prueba.persianas.TryGetValue(kv.Key.clave, out float forzada))
                {
                    hayPersiana = true;
                    mp = new Metrica { value = forzada * 100f };
                }
                for (int i = 0; i < n; i++)
                {
                    var v = lista[i];
                    v.abiertaObj = i < abiertas ? 1f : 0f;
                    if (hayPersiana && v.conPersiana)
                        // 0 = subida, 100 = bajada; nunca del todo: la última lama deja ver la ventana
                        v.persianaObj = Mathf.Clamp(mp.value / 100f, 0f, 0.97f);
                }
            }
        }

        // ───────────────────────── Geometría ─────────────────────────

        void Construir(Planta p)
        {
            p.sucia = false;
            vs.Clear(); ns.Clear(); uvs.Clear();
            foreach (var t in tris) t.Clear();
            var origen = p.raiz.position;
            foreach (var v in p.ventanas) Ventana1(v, origen);
            p.malla.Clear();
            p.malla.subMeshCount = 3;
            p.malla.SetVertices(vs);
            p.malla.SetNormals(ns);
            p.malla.SetUVs(0, uvs);
            for (int i = 0; i < 3; i++) p.malla.SetTriangles(tris[i], i, false);
            p.malla.RecalculateBounds();
        }

        void Ventana1(Ventana v, Vector3 origen)
        {
            var d = v.d;
            bool enMalla = d.en_malla;
            float ancho = d.ancho, alto = d.alto, perf = d.perfil;
            float ya = -alto / 2, yb = alto / 2;
            int hojas = Mathf.Max(1, d.hojas);
            float w = (ancho - 2 * perf) / hojas;
            Vector3 c = d.centro - origen, ex = d.dir, ez = d.normal;

            // Hojas
            if (!enMalla || v.abierta > 0.001f)
            {
                for (int k = 0; k < hojas; k++)
                {
                    float carril = k % 2 == 0 ? 0.018f : -0.018f;
                    float t0 = perf + k * w - (k == 0 ? 0.02f : 0f);
                    float t1 = perf + (k + 1) * w + (k == hojas - 1 ? 0.02f : 0f);
                    // Qué hoja se mueve: en las correderas, la primera (y la última si hay cuatro o más) se desliza
                    // sobre su vecina; las de una hoja basculan hacia dentro
                    bool mueve = hojas == 1 ? k == 0 : (k == 0 || (hojas >= 4 && k == hojas - 1));
                    float a = mueve ? v.abierta : 0f;
                    if (enMalla && a <= 0.001f) continue;
                    float desliz = 0f, giro = 0f;
                    if (hojas == 1) giro = 10f * a;
                    else desliz = (k == 0 ? 1f : -1f) * a * w * 0.92f;
                    float fuera = enMalla ? 0.045f : 0f;   // por delante de la hoja de la malla
                    var marco = new Marco(c, ex, ez, ya + perf, giro);
                    float x0 = t0 - ancho / 2 + desliz, x1 = t1 - ancho / 2 + desliz;
                    float z = carril + fuera;
                    // Perfiles de la hoja (45 mm en el plano, 40 mm de fondo) y vidrio
                    marco.Caja(this, 0, new Vector3((x0 + x1) / 2, ya + perf, z), new Vector3((x1 - x0) / 2, 0.0225f, 0.02f));
                    marco.Caja(this, 0, new Vector3((x0 + x1) / 2, yb - perf, z), new Vector3((x1 - x0) / 2, 0.0225f, 0.02f));
                    marco.Caja(this, 0, new Vector3(x0, 0, z), new Vector3(0.0225f, (yb - ya) / 2 - perf, 0.02f));
                    marco.Caja(this, 0, new Vector3(x1, 0, z), new Vector3(0.0225f, (yb - ya) / 2 - perf, 0.02f));
                    if (k + 1 < hojas && !enMalla)
                        marco.Caja(this, 0, new Vector3(t1 - ancho / 2 + desliz, 0, fuera), new Vector3(0.03f, (yb - ya) / 2 - perf / 2, 0.042f));
                    marco.Caja(this, 1, new Vector3((x0 + x1) / 2, 0, z), new Vector3((t1 - t0) / 2 - 0.012f, alto / 2 - perf - 0.017f, 0.004f));
                }
            }

            // Persiana enrollable por fuera del vidrio
            if (v.conPersiana)
            {
                float frac = v.persiana;
                bool dibujar = !enMalla || Mathf.Abs(frac - Mathf.Max(0, d.persiana)) > 0.02f;
                if (dibujar && frac > 0.01f)
                {
                    float altoP = alto * frac;
                    float z = 0.05f + (enMalla ? 0.012f : 0f);
                    var marco = new Marco(c, ex, ez, 0, 0);
                    marco.Caja(this, 2, new Vector3(0, yb - altoP / 2, z), new Vector3(ancho / 2 - 0.01f, altoP / 2, 0.008f));
                    // Lamas: unas rayas finas para que se lea como persiana (cada 5 cm)
                    int lamas = Mathf.Min(60, Mathf.FloorToInt(altoP / 0.05f));
                    for (int i = 1; i <= lamas; i++)
                        marco.Caja(this, 2, new Vector3(0, yb - i * 0.05f, z + 0.009f), new Vector3(ancho / 2 - 0.012f, 0.003f, 0.0015f));
                }
            }
        }

        // Sistema de la ventana: x a lo largo del hueco, y arriba (centro del hueco), z hacia la calle; con giro
        // (hoja oscilobatiente) las piezas basculan sobre el travesaño inferior, con la parte de arriba hacia dentro
        static readonly Vector3[] Ejes = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };

        readonly struct Marco
        {
            readonly Vector3 c, ex, ey, ez;
            readonly float yGiro;
            readonly Quaternion q;
            readonly bool gira;

            public Marco(Vector3 c, Vector3 ex, Vector3 ez, float yGiro, float giro)
            {
                this.c = c; this.ex = ex; this.ez = ez; ey = Vector3.up; this.yGiro = yGiro;
                gira = Mathf.Abs(giro) > 0.01f;
                float g = giro * Mathf.Deg2Rad;
                q = Quaternion.FromToRotation(Vector3.up, Vector3.up * Mathf.Cos(g) - ez * Mathf.Sin(g));
            }

            Vector3 P(Vector3 l)
            {
                var r = ex * l.x + ey * (l.y - yGiro) + ez * l.z;
                if (gira) r = q * r;
                return c + ey * yGiro + r;
            }

            Vector3 D(Vector3 dl)
            {
                var r = ex * dl.x + ey * dl.y + ez * dl.z;
                return gira ? q * r : r;
            }

            public void Caja(Ventanas o, int sub, Vector3 centro, Vector3 medio)
            {
                var ax = Ejes;
                for (int a = 0; a < 3; a++)
                {
                    int b = (a + 1) % 3, e = (a + 2) % 3;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var nl = ax[a] * s;
                        var fc = centro + Vector3.Scale(nl, medio);
                        var ub = ax[b] * medio[b];
                        var ue = ax[e] * medio[e];
                        var p0 = P(fc - ub - ue); var p1 = P(fc + ub - ue); var p2 = P(fc + ub + ue); var p3 = P(fc - ub + ue);
                        var n = D(nl).normalized;
                        int i0 = o.vs.Count;
                        o.vs.Add(p0); o.vs.Add(p1); o.vs.Add(p2); o.vs.Add(p3);
                        for (int k = 0; k < 4; k++) o.ns.Add(n);
                        float lu = medio[b] * 2, lv = medio[e] * 2;
                        o.uvs.Add(new Vector2(0, 0)); o.uvs.Add(new Vector2(lu, 0)); o.uvs.Add(new Vector2(lu, lv)); o.uvs.Add(new Vector2(0, lv));
                        var t = o.tris[sub];
                        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) > 0)
                        { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                        else
                        { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
                    }
                }
            }
        }
    }
}
