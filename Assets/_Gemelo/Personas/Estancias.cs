// Lo que el módulo Personas sabe de cada sitio: estancias (con sus plazas para sentarse, su acceso en la malla y los
// sitios donde se puede estar de pie), edificios (alarma y punto de reunión) y puntos de llegada al campus
// (aparcamientos, parada del autobús y accesos peatonales).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Gemelo
{
    public struct Plaza
    {
        public Vector3 pos;        // a ras de suelo, bajo la cadera de quien se sienta
        public float rumbo;        // rad (0 = +Z)
        public float altura;       // altura del asiento sobre el suelo
        public int ocupante;       // id de la persona o −1
        public bool ordenador;     // hay un ordenador delante (se teclea en vez de escribir)
    }

    public class Estancia
    {
        public InfoLocal info;
        public EdificioPersonas edificio;
        public int nodo;                 // índice en la caché de rutas
        public Vector3 acceso;           // punto de la malla dentro del local, junto a una puerta
        public bool accesible;
        public bool sentados;            // se está sentado (aulas, despachos, laboratorios, cafetería…)
        public bool cafeteria;
        public bool alAireLibre;         // patios y terrazas: sin techo (el sol da sombra a quien está ahí)
        public bool mesas;               // se sienta a una mesa (aulas, despachos, laboratorios…): puede escribir o teclear
        public Plaza[] plazas = new Plaza[0];
        public readonly List<Persona> miembros = new();
        List<Vector3> puntos;            // sitios para estar de pie (se buscan la primera vez)
        public float dato = float.NaN;   // ocupación según el servicio

        public string Clave => info.clave;

        static readonly HashSet<string> conAsientos = new()
        {
            "aula", "aula_informatica", "aula_dibujo", "aula_flexible", "seminario", "sala_estudio", "despacho",
            "despacho_direccion", "sala_juntas", "salon_actos", "salon_grados", "cafeteria", "comedor", "sala_estudiantes",
            "sala_descanso", "minisala", "secretaria", "conserjeria", "oficina", "biblioteca", "reservado",
        };

        public Estancia(InfoLocal il)
        {
            info = il;
            var uso = il.local.uso ?? "";
            sentados = conAsientos.Contains(uso) || uso.StartsWith("lab");
            cafeteria = uso == "cafeteria" || uso == "comedor";
            alAireLibre = uso == "patio" || uso == "terraza";
            mesas = sentados && !cafeteria && uso != "salon_actos" && uso != "sala_descanso" && uso != "sala_estudiantes";
        }

        public int PlazaLibre(System.Random r)
        {
            int n = plazas.Length;
            if (n == 0) return -1;
            int ini = r.Next(n);
            // La del jugador (si está sentado aquí) no se elige
            int jugador = GemeloApp.AsientoJugador is Vector3 aj && PlazaCerca(aj, out int ij) ? ij : -1;
            for (int k = 0; k < n; k++)
            {
                int i = (ini + k) % n;
                if (plazas[i].ocupante < 0 && i != jugador) return i;
            }
            return -1;
        }

        /// <summary>La plaza a menos de 0,35 m (en planta) de un asiento, si la hay.</summary>
        public bool PlazaCerca(Vector3 asiento, out int indice)
        {
            indice = -1;
            float md = 0.35f * 0.35f;
            for (int i = 0; i < plazas.Length; i++)
            {
                var d = plazas[i].pos - asiento;
                if (Mathf.Abs(d.y) > 1f) continue;
                float h = d.x * d.x + d.z * d.z;
                if (h < md) { md = h; indice = i; }
            }
            return indice >= 0;
        }

        /// <summary>Un sitio libre para estar de pie dentro del local (lejos de los que ya hay).</summary>
        public bool PuntoDePie(System.Random r, out Vector3 p)
        {
            if (puntos == null) BuscarPuntos(r);
            p = acceso;
            if (puntos.Count == 0) return Holgura(r, acceso, ref p) || accesible;
            for (int intento = 0; intento < 8; intento++)
            {
                var q = puntos[r.Next(puntos.Count)];
                p = q;
                if (Cercania(q) >= 0.8f) return true;
            }
            // Todos los probados están ocupados: el punto más despejado y, si aun así hay alguien encima, un hueco
            // alrededor (antes se devolvía el último probado y varios acababan de pie en el mismo sitio)
            float mejor = -1f;
            foreach (var q in puntos)
            {
                float c = Cercania(q);
                if (c > mejor) { mejor = c; p = q; }
            }
            if (mejor < 0.8f) Holgura(r, p, ref p);
            return true;
        }

        // Distancia² al objetivo más cercano de los miembros
        float Cercania(Vector3 q)
        {
            float d = float.MaxValue;
            foreach (var m in miembros) d = Mathf.Min(d, (m.objetivo - q).sqrMagnitude);
            return d;
        }

        // Un sitio libre (en la malla y dentro del local) a 0,6–1,2 m de centro, si lo hay
        bool Holgura(System.Random r, Vector3 centro, ref Vector3 p)
        {
            var nav = Navegacion.Instancia;
            if (nav == null) return false;
            for (int intento = 0; intento < 10; intento++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = 0.6f + 0.6f * (float)r.NextDouble();
                var q = centro + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * d;
                if (!nav.Punto(q, 0.3f, out var h, 0.4f) || !info.local.Contiene(h) || Cercania(h) < 0.5f) continue;
                p = h;
                return true;
            }
            return false;
        }

        void BuscarPuntos(System.Random r)
        {
            puntos = new List<Vector3>();
            var pol = info.local.poligono;
            if (pol == null || pol.Length < 3 || Navegacion.Instancia == null) return;
            Vector2 min = pol[0], max = pol[0];
            foreach (var v in pol) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            int objetivo = Mathf.Clamp(Mathf.RoundToInt(info.area / 2.5f), 4, 50);
            float y = info.local.cotaSuelo;
            var path = new NavMeshPath();
            for (int i = 0; i < objetivo * 4 && puntos.Count < objetivo; i++)
            {
                var q = new Vector3(Mathf.Lerp(min.x, max.x, (float)r.NextDouble()), y + 0.1f, Mathf.Lerp(min.y, max.y, (float)r.NextDouble()));
                if (!info.local.Contiene(q)) continue;
                if (!NavMesh.SamplePosition(q, out var h, 0.3f, NavMesh.AllAreas) || Mathf.Abs(h.position.y - y) > 0.4f) continue;
                if (!info.local.Contiene(h.position)) continue;
                // Que se llegue desde el acceso (no una isla entre muebles)
                if (accesible && (!NavMesh.CalculatePath(acceso, h.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)) continue;
                puntos.Add(h.position);
            }
        }
    }

    public class EdificioPersonas
    {
        public InfoEdificio info;
        public bool alarma;
        public int nodoReunion = -1;
        public Vector3 reunion;
        public bool reunionLista;
        public Vector3 puertas;          // centro de las puertas exteriores
        public readonly List<Estancia> estancias = new();
    }

    public class Llegada
    {
        public string nombre;
        public Vector3 pos;              // nodo (en la malla)
        public readonly List<Vector3> puntos = new();   // sitios concretos (plazas de aparcamiento…)
        public float peso;
        public int nodo;
    }
}
