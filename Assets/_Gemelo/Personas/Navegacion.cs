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
        /// El buscador de Unity tiene un tope de nodos y en las rutas largas por interiores llenos de muebles se queda
        /// corto: entonces se pasa por las puertas exteriores de los edificios de los extremos (la que da la ruta más
        /// corta de las tres que mejor caen), y solo si así tampoco, se sigue desde donde se quedó.</summary>
        public Ruta Calcular(Vector3 a, Vector3 b)
        {
            RutasCalculadas++;
            esquinas.Clear();
            bool completa = Esquinas(a, b, esquinas);
            if (esquinas.Count == 0) return null;
            if (!completa && PorPuertas(a, b, via))
            {
                esquinas.Clear();
                esquinas.AddRange(via);
                completa = true;
            }
            if (!completa)
            {
                // Se sigue desde donde llegó, mientras avance
                float antes = Vector3.Distance(esquinas[esquinas.Count - 1], b);
                for (int k = 0; k < 3 && !completa; k++)
                {
                    if (!NavMesh.CalculatePath(esquinas[esquinas.Count - 1], b, Filtro, camino) || camino.corners.Length < 2) break;
                    float ahora = Vector3.Distance(camino.corners[camino.corners.Length - 1], b);
                    if (ahora > antes - 1f && camino.status != NavMeshPathStatus.PathComplete) break;
                    for (int i = 1; i < camino.corners.Length; i++) esquinas.Add(camino.corners[i]);
                    completa = camino.status == NavMeshPathStatus.PathComplete;
                    antes = ahora;
                }
            }
            var c = esquinas;
            tmp.Clear();
            tmp.Add(c[0]);
            for (int i = 1; i < c.Count; i++)
            {
                Vector3 p0 = c[i - 1], p1 = c[i];
                float largo = Vector3.Distance(p0, p1);
                float dy = Mathf.Abs(p1.y - p0.y);
                bool fuera = RegistroCampus.EdificioEn(p0) == null && RegistroCampus.EdificioEn(p1) == null;
                // Fuera, el terreno ondula entre esquina y esquina: un punto por metro (cada 2,5 m se quedaba hasta
                // 1,5 m en el aire en las cuestas de los pies de las escaleras exteriores)
                float paso = dy > 0.05f && !fuera ? 0.6f : fuera ? 1f : 0f;
                if (paso > 0f && largo > paso * 1.5f)
                {
                    int n = Mathf.CeilToInt(largo / paso);
                    float yPrev = p0.y;
                    for (int k = 1; k < n; k++)
                    {
                        var q = Vector3.Lerp(p0, p1, k / (float)n);
                        q.y = Mathf.Lerp(yPrev, q.y, 0.5f);
                        bool hay = NavMesh.SamplePosition(q, out var h, 0.8f, NavMesh.AllAreas);
                        // (con más radio, solo si el punto de la malla está en la misma vertical: no salta a otra planta)
                        if (!hay && fuera && NavMesh.SamplePosition(q, out h, 2f, NavMesh.AllAreas))
                            hay = new Vector2(h.position.x - q.x, h.position.z - q.z).sqrMagnitude < 0.09f;
                        if (hay)
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

        readonly List<Vector3> esquinas = new(256), via = new(256);

        // Esquinas de la ruta de a a b; true si llega. Si la búsqueda se agota en un sentido, se prueba en el otro.
        bool Esquinas(Vector3 a, Vector3 b, List<Vector3> r)
        {
            r.Clear();
            if (!NavMesh.CalculatePath(a, b, Filtro, camino) || camino.status == NavMeshPathStatus.PathInvalid || camino.corners.Length == 0)
                return false;
            if (camino.status == NavMeshPathStatus.PathComplete) { r.AddRange(camino.corners); return true; }
            var parcial = camino.corners;
            if (NavMesh.CalculatePath(b, a, Filtro, camino) && camino.status == NavMeshPathStatus.PathComplete)
            {
                for (int i = camino.corners.Length - 1; i >= 0; i--) r.Add(camino.corners[i]);
                return true;
            }
            r.AddRange(parcial);
            return false;
        }

        // ───── Rutas largas por puntos de paso ─────
        // Puntos de paso: las puertas exteriores de cada edificio y el arranque y la llegada de cada escalera (los saca
        // Tools ▸ Gemelo ▸ Malla de navegación). Cuando el buscador no completa una ruta, se parte en dos por el punto
        // de paso que menos la alarga (de los tres mejores del edificio de cada extremo), y así hasta tres veces.
        readonly List<Vector3> pasos = new();
        readonly List<InfoEdificio> pasosEdificio = new();
        bool pasosListos;
        int presupuesto;

        void PrepararPasos()
        {
            pasosListos = true;
            var rec = RecursosPersonas.Cargar();
            if (rec != null && rec.escaleras != null)
                foreach (var e in rec.escaleras) { pasos.Add(e); pasosEdificio.Add(RegistroCampus.EdificioEn(e)); }
            foreach (var e in RegistroCampus.Edificios)
                foreach (var pu in e.puertasExteriores)
                    if (Punto(RegistroCampus.CentroPuerta(pu) + Vector3.up * 0.2f, 0.8f, out var q, 1f))
                    {
                        pasos.Add(q);
                        pasosEdificio.Add(e);
                    }
        }

        bool PorPuertas(Vector3 a, Vector3 b, List<Vector3> r)
        {
            if (!pasosListos) PrepararPasos();
            if (pasos.Count == 0) return false;
            presupuesto = 24;
            var l = new List<Vector3> { a };
            if (!Unir(a, b, l, 0)) return false;
            r.Clear();
            r.AddRange(l);
            return true;
        }

        // Añade a salida la ruta de desde a hasta (sin el primer punto); false si no se llega
        bool Unir(Vector3 desde, Vector3 hasta, List<Vector3> salida, int prof)
        {
            if ((desde - hasta).sqrMagnitude < 0.01f) return true;
            if (--presupuesto < 0) return false;
            var t = new List<Vector3>(64);
            if (Esquinas(desde, hasta, t))
            {
                for (int i = 1; i < t.Count; i++) salida.Add(t[i]);
                return true;
            }
            if (prof >= 3) return false;
            var ea = RegistroCampus.EdificioEn(desde);
            var eb = RegistroCampus.EdificioEn(hasta);
            float recta = Vector3.Distance(desde, hasta);
            var cand = new List<(int i, float h)>();
            for (int i = 0; i < pasos.Count; i++)
            {
                var e = pasosEdificio[i];
                if (e == null || e != ea && e != eb) continue;
                var w = pasos[i];
                if ((w - desde).sqrMagnitude < 4f || (w - hasta).sqrMagnitude < 4f) continue;
                // (las escaleras cuentan con la diferencia de altura: un paso a otra planta alarga de verdad)
                float h = Vector3.Distance(desde, w) + Vector3.Distance(w, hasta) + 4f * (Mathf.Abs(w.y - desde.y) + Mathf.Abs(w.y - hasta.y) - Mathf.Abs(hasta.y - desde.y));
                if (h > recta * 2.5f + 40f) continue;
                cand.Add((i, h));
            }
            cand.Sort((x, y) => x.h.CompareTo(y.h));
            for (int k = 0; k < cand.Count && k < 3 && presupuesto > 0; k++)
            {
                var w = pasos[cand[k].i];
                int n = salida.Count;
                if (Unir(desde, w, salida, prof + 1) && Unir(w, hasta, salida, prof + 1)) return true;
                salida.RemoveRange(n, salida.Count - n);
            }
            return false;
        }
    }
}
