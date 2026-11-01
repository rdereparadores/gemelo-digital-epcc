// Índice en tiempo de ejecución de lo que hay en la escena: edificios, plantas y locales (con su planta exacta,
// asientos, luces y puertas), enlazado con los ids del servicio de sensores. Lo construye GemeloApp al arrancar.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public class InfoEdificio
    {
        public EdificioPolitecnica edificio;
        public string codigo;          // 1901…1907
        public string idServicio;      // SCO, ATE, INF, OPU, TEL, INV, ACU
        public string nombre;
        public Bounds limites;         // de todos sus renderers
        public readonly List<InfoPlanta> plantas = new();
        public readonly List<InfoLocal> locales = new();
        public readonly List<Puerta> puertasExteriores = new();
    }

    public class InfoPlanta
    {
        public InfoEdificio edificio;
        public int indice;             // en EdificioPolitecnica.plantas (de abajo arriba)
        public string nombre;          // P-1, P0, P1, P2
        public Transform raiz;
        public float cota;             // cota absoluta del suelo (mediana de sus locales)
        public readonly List<InfoLocal> locales = new();
        public readonly List<Puerta> puertas = new();
    }

    public class InfoLocal
    {
        public Local local;
        public InfoEdificio edificio;
        public InfoPlanta planta;
        public string clave;           // 1903_P1_24 = unity_room del servicio
        public string idServicio;      // INF-P01-AUL024 (cuando el servicio lo publica)
        public Vector3 centro;         // centroide de la planta, a la cota del suelo
        public float area;
        public readonly List<Puerta> puertas = new();

        public string Nombre => !string.IsNullOrEmpty(local.codigo) ? local.codigo
                              : !string.IsNullOrEmpty(local.rotulo) ? local.rotulo : local.uso;
        public bool Contiene(Vector3 p) => Mathf.Abs(p.y - local.cotaSuelo - 1.2f) < 1.9f && local.Contiene(p);
    }

    public static class RegistroCampus
    {
        public static readonly List<InfoEdificio> Edificios = new();
        public static readonly Dictionary<string, InfoLocal> Locales = new();      // por clave 1903_P1_24
        static readonly Dictionary<string, InfoEdificio> porCodigo = new();
        static readonly Dictionary<string, InfoLocal> porEspacio = new();
        public static bool Listo { get; private set; }

        // Código del plano ↔ id de SmartPoliTech (también llega en el estado como unity_id)
        public static readonly Dictionary<string, string> IdServicio = new()
        {
            ["1901"] = "SCO", ["1902"] = "ATE", ["1903"] = "INF", ["1904"] = "OPU",
            ["1905"] = "TEL", ["1906"] = "INV", ["1907"] = "ACU",
        };

        public static void Construir()
        {
            Edificios.Clear(); Locales.Clear(); porCodigo.Clear(); porEspacio.Clear();
            foreach (var e in Object.FindObjectsByType<EdificioPolitecnica>())
            {
                var ie = new InfoEdificio
                {
                    edificio = e, codigo = e.codigo, nombre = e.nombre,
                    idServicio = IdServicio.TryGetValue(e.codigo, out var id) ? id : e.codigo,
                };
                ie.limites = new Bounds(e.transform.position, Vector3.zero);
                foreach (var r in e.GetComponentsInChildren<Renderer>(true)) ie.limites.Encapsulate(r.bounds);
                for (int i = 0; i < e.plantas.Length; i++)
                {
                    var t = e.plantas[i];
                    if (t == null) continue;
                    var ip = new InfoPlanta { edificio = ie, indice = i, nombre = t.name, raiz = t };
                    ie.plantas.Add(ip);
                    var cotas = new List<float>();
                    foreach (var l in t.GetComponentsInChildren<Local>(true))
                    {
                        var il = new InfoLocal { local = l, edificio = ie, planta = ip, clave = l.Clave };
                        Geometria(il);
                        ip.locales.Add(il);
                        ie.locales.Add(il);
                        Locales[il.clave] = il;
                        cotas.Add(l.cotaSuelo);
                    }
                    cotas.Sort();
                    ip.cota = cotas.Count > 0 ? cotas[cotas.Count / 2] : e.cotaPlantaBaja;
                    foreach (var p in t.GetComponentsInChildren<Puerta>(true))
                    {
                        ip.puertas.Add(p);
                        var c = CentroPuerta(p);
                        foreach (var il in ip.locales)
                            if (Cerca(il, c, 0.6f)) il.puertas.Add(p);
                        if (p.exterior) ie.puertasExteriores.Add(p);
                    }
                }
                Edificios.Add(ie);
                porCodigo[ie.codigo] = ie;
                porCodigo[ie.idServicio] = ie;
            }
            Listo = true;
        }

        /// <summary>Enlaza los space_id del servicio con los locales (se llama con cada estado).</summary>
        public static void Enlazar(EstadoCampus estado)
        {
            if (estado?.buildings == null) return;
            foreach (var b in estado.buildings)
                foreach (var s in b.spaces)
                {
                    if (string.IsNullOrEmpty(s.unity_room) || porEspacio.ContainsKey(s.space_id)) continue;
                    var destinos = AliasEstancias.Destinos(s.unity_room);
                    if (destinos == null) Enlazar(s.space_id, s.unity_room, true);
                    else foreach (var d in destinos) Enlazar(s.space_id, d.clave, false);
                }
        }

        // El id del servicio de un local es el de su estancia homónima; si no la hay (división, sótano renombrado), el
        // del alias. Todas las estancias del servicio apuntan a su local (las fusionadas, al que las acoge).
        static void Enlazar(string spaceId, string clave, bool directo)
        {
            if (!Locales.TryGetValue(clave, out var il)) return;
            if (!porEspacio.ContainsKey(spaceId)) porEspacio[spaceId] = il;
            if (directo || il.idServicio == null) il.idServicio = spaceId;
        }

        public static InfoEdificio Edificio(string codigoOId) =>
            codigoOId != null && porCodigo.TryGetValue(codigoOId, out var e) ? e : null;

        public static InfoLocal Local(string clave) => clave != null && Locales.TryGetValue(clave, out var l) ? l : null;

        public static InfoLocal PorEspacio(string spaceId) => spaceId != null && porEspacio.TryGetValue(spaceId, out var l) ? l : null;

        /// <summary>Local que contiene el punto (el más pequeño si hay varios), o null.</summary>
        public static InfoLocal LocalEn(Vector3 p)
        {
            InfoLocal mejor = null;
            foreach (var e in Edificios)
            {
                if (!e.limites.Contains(p)) continue;
                foreach (var l in e.locales)
                    if (l.Contiene(p) && (mejor == null || l.area < mejor.area)) mejor = l;
            }
            return mejor;
        }

        /// <summary>Edificio cuyo volumen contiene el punto, o null.</summary>
        public static InfoEdificio EdificioEn(Vector3 p)
        {
            foreach (var e in Edificios) if (e.limites.Contains(p)) return e;
            return null;
        }

        public static Vector3 CentroPuerta(Puerta p) => p.transform.position + p.transform.right * (p.transform.lossyScale.x / 2f);

        static void Geometria(InfoLocal il)
        {
            var pol = il.local.poligono;
            if (pol == null || pol.Length < 3)
            {
                var b = il.local.GetComponent<BoxCollider>().bounds;
                il.centro = new Vector3(b.center.x, b.min.y, b.center.z);
                il.area = b.size.x * b.size.z;
                return;
            }
            // Centroide del polígono
            double a = 0, cx = 0, cz = 0;
            for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
            {
                double f = pol[j].x * pol[i].y - pol[i].x * pol[j].y;
                a += f; cx += (pol[j].x + pol[i].x) * f; cz += (pol[j].y + pol[i].y) * f;
            }
            a *= 0.5;
            il.area = Mathf.Abs((float)a);
            il.centro = System.Math.Abs(a) < 1e-6
                ? new Vector3(pol[0].x, il.local.cotaSuelo, pol[0].y)
                : new Vector3((float)(cx / (6 * a)), il.local.cotaSuelo, (float)(cz / (6 * a)));
        }

        static bool Cerca(InfoLocal il, Vector3 c, float r)
        {
            if (Mathf.Abs(c.y - il.local.cotaSuelo) > 1.5f) return false;
            if (il.local.Contiene(c)) return true;
            var pol = il.local.poligono;
            if (pol == null) return false;
            var q = new Vector2(c.x, c.z);
            for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
            {
                Vector2 a = pol[j], b = pol[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((a + ab * t - q).sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
