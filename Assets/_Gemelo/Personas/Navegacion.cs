// Malla de navegación del campus en ejecución: la carga del asset que genera Tools ▸ Gemelo ▸ Malla de navegación
// (NavMesh.AddNavMeshData, sin componentes en la escena), añade los enlaces de costura y calcula rutas.
// Las rutas se «densifican»: en escaleras y en tramos largos al aire libre se muestrea la altura de la malla para que
// los pies sigan el suelo con una simple interpolación lineal. Las rutas entre nodos (accesos de estancias, llegadas,
// puntos de reunión) se guardan en caché: casi todos los viajes reutilizan una ruta ya calculada.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Gemelo
{
    /// <summary>Polilínea con su longitud acumulada.</summary>
    public class Ruta
    {
        public Vector3[] p;
        public float[] acum;
        public float Largo => acum[acum.Length - 1];
        public bool completa;

        public Ruta(List<Vector3> puntos, bool completa)
        {
            p = puntos.ToArray();
            acum = new float[p.Length];
            for (int i = 1; i < p.Length; i++) acum[i] = acum[i - 1] + Vector3.Distance(p[i - 1], p[i]);
            this.completa = completa;
        }

        /// <summary>Punto a distancia s del principio (o del final si inversa) y dirección de avance.</summary>
        public Vector3 En(float s, bool inversa, out Vector3 dir)
        {
            float L = Largo;
            if (p.Length < 2) { dir = Vector3.forward; return p[0]; }
            if (inversa) s = L - s;
            s = Mathf.Clamp(s, 0, L);
            int lo = 0, hi = acum.Length - 1;
            while (hi - lo > 1)
            {
                int m = (lo + hi) >> 1;
                if (acum[m] <= s) lo = m; else hi = m;
            }
            float seg = acum[hi] - acum[lo];
            float t = seg > 1e-5f ? (s - acum[lo]) / seg : 0f;
            dir = p[hi] - p[lo];
            if (inversa) dir = -dir;
            return Vector3.LerpUnclamped(p[lo], p[hi], t);
        }

        /// <summary>Distancia (desde el principio del recorrido) a la que acaba el segmento en el que está s.</summary>
        public float FinSegmento(float s, bool inversa, out float dy, out float dh)
        {
            float L = Largo;
            float t = inversa ? L - s : s;
            int lo = 0, hi = acum.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (acum[m] <= t) lo = m; else hi = m; }
            var d = p[hi] - p[lo];
            dy = Mathf.Abs(d.y);
            dh = new Vector2(d.x, d.z).magnitude;
            return inversa ? L - acum[lo] : acum[hi];
        }

        public Vector3 Inicio(bool inversa) => inversa ? p[p.Length - 1] : p[0];
        public Vector3 Fin(bool inversa) => inversa ? p[0] : p[p.Length - 1];
    }

    public class Navegacion
    {
        public const int AreaTierra = 3, AreaCalzada = 4;
        public static Navegacion Instancia { get; private set; }

        NavMeshDataInstance datos;
        readonly List<NavMeshLinkInstance> enlaces = new();
        public NavMeshQueryFilter Filtro;
        public bool Lista { get; private set; }
        readonly NavMeshPath camino = new();
        readonly List<Vector3> tmp = new(256);
        readonly Dictionary<long, Ruta> cache = new();
        public int RutasCalculadas { get; private set; }
        public int RutasCache => cache.Count;

        public Navegacion(RecursosPersonas rec)
        {
            Instancia = this;
            Filtro = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            Filtro.SetAreaCost(0, 1f);
            Filtro.SetAreaCost(AreaTierra, 1.8f);
            Filtro.SetAreaCost(AreaCalzada, 2.5f);
            if (rec == null || rec.malla == null)
            {
                Debug.LogWarning("[Personas] Falta la malla de navegación: Tools ▸ Gemelo ▸ Malla de navegación");
                return;
            }
            datos = NavMesh.AddNavMeshData(rec.malla);
            int n = Mathf.Min(rec.enlacesA.Length, rec.enlacesB.Length);
            for (int i = 0; i < n; i++)
            {
                var l = NavMesh.AddLink(new NavMeshLinkData
                {
                    startPosition = rec.enlacesA[i], endPosition = rec.enlacesB[i], width = 0f, bidirectional = true,
                    costModifier = -1, area = 0, agentTypeID = 0,
                }, Vector3.zero, Quaternion.identity);
                if (!NavMesh.IsLinkValid(l)) continue;
                NavMesh.SetLinkActive(l, true);
                enlaces.Add(l);
            }
            Lista = datos.valid;
        }

        public void Liberar()
        {
            foreach (var e in enlaces) NavMesh.RemoveLink(e);
            enlaces.Clear();
            if (datos.valid) NavMesh.RemoveNavMeshData(datos);
            Lista = false;
            if (Instancia == this) Instancia = null;
        }

        /// <summary>Punto de la malla cerca de p (a menos de radio y, si se pide, casi a la misma altura).</summary>
        public bool Punto(Vector3 p, float radio, out Vector3 r, float maxDy = 100f, int areas = NavMesh.AllAreas)
        {
            if (NavMesh.SamplePosition(p, out var h, radio, areas) && Mathf.Abs(h.position.y - p.y) <= maxDy)
            {
                r = h.position;
                return true;
            }
            r = p;
            return false;
        }

        public Ruta Cache(int a, int b, out bool inversa)
        {
            inversa = false;
            if (cache.TryGetValue(((long)a << 20) | (uint)b, out var r)) return r;
            if (cache.TryGetValue(((long)b << 20) | (uint)a, out r)) { inversa = true; return r; }
            return null;
        }

        public void Guardar(int a, int b, Ruta r)
        {
            if (cache.Count > 30000) cache.Clear();
            cache[((long)a << 20) | (uint)b] = r;
        }

        /// <summary>Ruta de a a b (puntos ya en la malla o cerca). Si no se llega, termina en lo más cercano.
        /// Si el buscador se queda corto (rutas largas con muchos polígonos), lo reintenta al revés.</summary>
        public Ruta Calcular(Vector3 a, Vector3 b)
        {
            RutasCalculadas++;
            if (!NavMesh.CalculatePath(a, b, Filtro, camino) || camino.status == NavMeshPathStatus.PathInvalid || camino.corners.Length == 0)
                return null;
            var c = camino.corners;
            bool completa = camino.status == NavMeshPathStatus.PathComplete;
            if (!completa)
            {
                // A veces la búsqueda se agota en un sentido y no en el otro
                if (NavMesh.CalculatePath(b, a, Filtro, camino) && camino.status == NavMeshPathStatus.PathComplete)
                {
                    c = camino.corners;
                    System.Array.Reverse(c);
                    completa = true;
                }
                else
                {
                    // El buscador tiene un tope de nodos: en rutas largas por interiores llenos de muebles se queda corto.
                    // Se sigue desde donde llegó, mientras avance.
                    var todo = new List<Vector3>(c);
                    float antes = Vector3.Distance(todo[todo.Count - 1], b);
                    for (int k = 0; k < 3 && !completa; k++)
                    {
                        if (!NavMesh.CalculatePath(todo[todo.Count - 1], b, Filtro, camino) || camino.corners.Length < 2) break;
                        float ahora = Vector3.Distance(camino.corners[camino.corners.Length - 1], b);
                        if (ahora > antes - 1f && camino.status != NavMeshPathStatus.PathComplete) break;
                        for (int i = 1; i < camino.corners.Length; i++) todo.Add(camino.corners[i]);
                        completa = camino.status == NavMeshPathStatus.PathComplete;
                        antes = ahora;
                    }
                    c = todo.ToArray();
                }
            }
            tmp.Clear();
            tmp.Add(c[0]);
            for (int i = 1; i < c.Length; i++)
            {
                Vector3 p0 = c[i - 1], p1 = c[i];
                float largo = Vector3.Distance(p0, p1);
                float dy = Mathf.Abs(p1.y - p0.y);
                bool fuera = RegistroCampus.EdificioEn(p0) == null && RegistroCampus.EdificioEn(p1) == null;
                float paso = dy > 0.05f && !fuera ? 0.6f : fuera && largo > 4f ? 2.5f : 0f;
                if (paso > 0f && largo > paso * 1.5f)
                {
                    int n = Mathf.CeilToInt(largo / paso);
                    float yPrev = p0.y;
                    for (int k = 1; k < n; k++)
                    {
                        var q = Vector3.Lerp(p0, p1, k / (float)n);
                        q.y = Mathf.Lerp(yPrev, q.y, 0.5f);
                        if (NavMesh.SamplePosition(q, out var h, 0.8f, NavMesh.AllAreas))
                        {
                            q.y = h.position.y;
                            tmp.Add(new Vector3(q.x, q.y, q.z));
                            yPrev = q.y;
                        }
                    }
                }
                tmp.Add(p1);
            }
            return new Ruta(tmp, completa);
        }
    }
}
