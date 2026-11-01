// Simulación de la multitud: tantas personas en cada estancia como diga el servicio (1:1, con el tope de
// Ajustes.maxPersonas). Cada vez que llega un estado se reconcilia: lo que sobra en una estancia sale (y se va a otra
// con hueco, mejor del mismo edificio, o deja el campus) y lo que falta llega desde otra estancia o entra al campus por
// un aparcamiento, la parada o un acceso peatonal. Con la hora del modelo muy acelerada, los viajes que no darían
// tiempo se resuelven teletransportando. Con la alarma de incendio de un edificio, su gente corre al punto de reunión
// y espera allí hasta que acaba.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace Gemelo
{
    public class Multitud
    {
        public static readonly Bounds Zona = new(new Vector3(400f, 22f, 562f), new Vector3(300f, 70f, 290f));

        public readonly List<Persona> activas = new();
        readonly Stack<Persona> libresPool = new();
        public readonly List<Estancia> estancias = new();
        readonly Dictionary<string, Estancia> porClave = new();
        public readonly List<EdificioPersonas> edificios = new();
        readonly Dictionary<InfoEdificio, EdificioPersonas> porEdificio = new();
        public readonly List<Llegada> llegadas = new();
        readonly Navegacion nav;
        public readonly Rejilla rejilla = new(Zona);
        readonly System.Random rnd = new(20261103);
        readonly LinkedList<Persona> cola = new();
        int siguienteId;
        bool inicial = true;
        public int Teletransportes, Viajes, Fallos, SaltosReloj;
        System.DateTime horaAnterior;
        float realAnterior;
        public double MsRutas;
        readonly Persona[] vecinos = new Persona[24];
        readonly List<Persona> libres = new();
        readonly List<(Estancia e, int n)> deficits = new();
        readonly NavMeshPath caminoTmp = new();

        public Multitud(Navegacion nav)
        {
            this.nav = nav;
            var reloj = Stopwatch.StartNew();
            foreach (var ie in RegistroCampus.Edificios)
            {
                var ep = new EdificioPersonas { info = ie };
                var c = Vector3.zero;
                foreach (var p in ie.puertasExteriores) c += RegistroCampus.CentroPuerta(p);
                ep.puertas = ie.puertasExteriores.Count > 0 ? c / ie.puertasExteriores.Count : ie.limites.center;
                edificios.Add(ep);
                porEdificio[ie] = ep;
            }
            foreach (var il in RegistroCampus.Locales.Values)
            {
                var e = new Estancia(il) { edificio = porEdificio[il.edificio], nodo = estancias.Count };
                BuscarAcceso(e);
                Plazas(e);
                estancias.Add(e);
                porClave[il.clave] = e;
                e.edificio.estancias.Add(e);
            }
            BuscarLlegadas();
            for (int i = 0; i < edificios.Count; i++) edificios[i].nodoReunion = estancias.Count + llegadas.Count + i;
            int asientos = 0, accesibles = 0;
            foreach (var e in estancias) { asientos += e.plazas.Length; if (e.accesible) accesibles++; }
            Debug.Log($"[Personas] {estancias.Count} estancias ({accesibles} con acceso en la malla), {asientos} plazas, " +
                      $"{llegadas.Count} puntos de llegada; preparado en {reloj.ElapsedMilliseconds} ms");
        }

        public Estancia Estancia(string clave) => clave != null && porClave.TryGetValue(clave, out var e) ? e : null;

        // ───────────────────────── Preparación ─────────────────────────

        void BuscarAcceso(Estancia e)
        {
            var l = e.info.local;
            float y = l.cotaSuelo;
            foreach (var pu in e.info.puertas)
            {
                var c = RegistroCampus.CentroPuerta(pu);
                var f = pu.transform.forward;
                foreach (float s in new[] { 0.7f, -0.7f, 1.1f, -1.1f, 1.6f, -1.6f })
                {
                    var q = c + f * s;
                    q.y = y + 0.1f;
                    if (!l.Contiene(q)) continue;
                    if (nav.Punto(q, 0.4f, out var h, 0.5f) && l.Contiene(h))
                    {
                        e.acceso = h;
                        e.accesible = true;
                        return;
                    }
                }
            }
            // Sin puerta con malla: lo más cerca del centro
            var cen = e.info.centro + Vector3.up * 0.1f;
            for (int k = 0; k < 40; k++)
            {
                var q = cen + new Vector3(Mathf.Sin(k * 2.4f), 0, Mathf.Cos(k * 2.4f)) * (k * 0.3f);
                if (nav.Punto(q, 0.5f, out var h, 0.5f) && l.Contiene(h))
                {
                    e.acceso = h;
                    e.accesible = true;
                    return;
                }
            }
            e.acceso = e.info.centro;
        }

        // Altura del asiento según el modelo de la silla (de mirar las mallas)
        static readonly (string, float)[] alturas =
        {
            ("Taburete_Bar", 0.75f), ("Taburete", 0.62f), ("Silla_Oficina", 0.47f), ("Silla_Confidente", 0.46f), ("Sofa", 0.43f),
            ("Silla_Cafeteria", 0.45f), ("Banco_Corrido", 0.45f), ("Banco_Hall", 0.45f), ("Silla_ISO", 0.46f), ("Sillon", 0.48f),
            ("Silla_Pala", 0.46f), ("Silla_Alumno", 0.45f), ("Butaca", 0.44f), ("Banco_Tapizado", 0.47f),
        };

        void Plazas(Estancia e)
        {
            var l = e.info.local;
            if (l.asientos == null || l.asientos.Length == 0) return;
            var lista = new List<Plaza>();
            foreach (var a in l.asientos)
            {
                if (a == null) continue;
                var mf = a.GetComponentInChildren<MeshFilter>();
                string nombre = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : a.name;
                float alto = 0.46f;
                foreach (var (pre, h) in alturas) if (nombre.StartsWith(pre)) { alto = h; break; }
                // Las sillas con respaldo lo tienen en −Z: se mira a +Z. Taburetes y bancos corridos no tienen respaldo:
                // se mira hacia la mesa más cercana (delante o detrás).
                var fwd = a.forward;
                if (nombre.StartsWith("Taburete") || nombre.StartsWith("Banco_Corrido"))
                {
                    var o = a.position + Vector3.up * 0.72f;
                    float dMas = Physics.Raycast(o, fwd, out var h1, 1.2f, ~(1 << 2), QueryTriggerInteraction.Ignore) && !h1.transform.IsChildOf(a) ? h1.distance : 9f;
                    float dMenos = Physics.Raycast(o, -fwd, out var h2, 1.2f, ~(1 << 2), QueryTriggerInteraction.Ignore) && !h2.transform.IsChildOf(a) ? h2.distance : 9f;
                    if (dMenos < dMas) fwd = -fwd;
                }
                fwd.y = 0;
                fwd.Normalize();
                // Bancos y sofás: varias plazas a lo largo
                float ancho = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.x * a.lossyScale.x : 0.5f;
                int n = Mathf.Clamp(Mathf.FloorToInt(ancho / 0.62f), 1, 8);
                float suelo = Mathf.Abs(a.position.y - l.cotaSuelo) < 1f ? a.position.y : l.cotaSuelo;
                var der = Vector3.Cross(Vector3.up, fwd);
                for (int i = 0; i < n; i++)
                {
                    float off = n == 1 ? 0 : (i - (n - 1) / 2f) * ancho / n;
                    var pos = a.position + der * off + fwd * 0.03f;
                    pos.y = suelo;
                    lista.Add(new Plaza { pos = pos, rumbo = Mathf.Atan2(fwd.x, fwd.z), altura = alto, ocupante = -1 });
                }
            }
            e.plazas = lista.ToArray();
        }

        void BuscarLlegadas()
        {
            // Aparcamientos: cada plaza (marca vial) es un sitio donde se baja del coche
            void Aparcamiento(string nombre, string marcas, float peso)
            {
                var raiz = GameObject.Find(nombre);
                var t = raiz != null ? raiz.transform.Find(marcas) : null;
                if (t == null) return;
                var l = new Llegada { nombre = marcas, peso = peso };
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                    if (nav.Punto(r.bounds.center, 2f, out var p, 2f)) l.puntos.Add(p);
                if (l.puntos.Count == 0) return;
                var c = Vector3.zero;
                foreach (var p in l.puntos) c += p;
                c /= l.puntos.Count;
                if (!nav.Punto(c, 6f, out l.pos)) l.pos = l.puntos[0];
                llegadas.Add(l);
            }
            Aparcamiento("Parking_Norte", "Marcas_Viales", 0.4f);
            Aparcamiento("Parking_Norte", "Marcas_Viales_Este", 0.2f);

            // Paradas de autobús (marquesinas) dentro de la zona
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.name.StartsWith("Vidrio_Marquesina") || !Zona.Contains(r.bounds.center)) continue;
                var l = new Llegada { nombre = "Parada", peso = 0.2f };
                if (!nav.Punto(r.bounds.center, 6f, out l.pos)) continue;
                for (int k = 0; k < 8; k++)
                    if (nav.Punto(l.pos + new Vector3((float)rnd.NextDouble() * 6 - 3, 0, (float)rnd.NextDouble() * 4 - 2), 1.5f, out var q, 1f))
                        l.puntos.Add(q);
                if (l.puntos.Count == 0) l.puntos.Add(l.pos);
                llegadas.Add(l);
            }

            // Accesos peatonales: aceras que salen por el borde de la zona
            var terreno = Terrain.activeTerrain;
            var bordes = new List<Vector3>();
            var min = Zona.min + new Vector3(5, 0, 5);
            var max = Zona.max - new Vector3(5, 0, 5);
            for (float x = min.x; x <= max.x; x += 5) { bordes.Add(new Vector3(x, 0, min.z)); bordes.Add(new Vector3(x, 0, max.z)); }
            for (float z = min.z; z <= max.z; z += 5) { bordes.Add(new Vector3(min.x, 0, z)); bordes.Add(new Vector3(max.x, 0, z)); }
            var acc = new List<Vector3>();
            foreach (var b in bordes)
            {
                var q = b;
                q.y = terreno != null ? terreno.SampleHeight(q) + terreno.transform.position.y : 15f;
                if (!NavMesh.SamplePosition(q, out var h, 3f, 1 << 0)) continue;   // solo aceras y suelos
                bool cerca = false;
                foreach (var a in acc) if ((a - h.position).sqrMagnitude < 30f * 30f) { cerca = true; break; }
                if (!cerca) acc.Add(h.position);
            }
            // Que lleguen al campus (desde el aparcamiento norte o el centro)
            var centro = llegadas.Count > 0 ? llegadas[0].pos : Zona.center;
            int nAcc = 0;
            foreach (var a in acc)
            {
                if (!NavMesh.CalculatePath(a, centro, nav.Filtro, caminoTmp) || caminoTmp.status != NavMeshPathStatus.PathComplete) continue;
                var l = new Llegada { nombre = "Acceso", pos = a, peso = 0.25f };
                l.puntos.Add(a);
                llegadas.Add(l);
                nAcc++;
            }
            foreach (var l in llegadas) if (l.nombre == "Acceso") l.peso = 0.25f / Mathf.Max(1, nAcc);
            for (int i = 0; i < llegadas.Count; i++) llegadas[i].nodo = estancias.Count + i;
        }

        void PrepararReunion(EdificioPersonas e)
        {
            if (e.reunionLista) return;
            e.reunionLista = true;
            // Puntos al aire libre a 15–30 m de alguna puerta exterior, fuera de cualquier local y lejos de las calzadas;
            // gana el más cercano a una puerta con algo de espacio alrededor
            var terreno = Terrain.activeTerrain;
            int sinCalzada = NavMesh.AllAreas & ~(1 << Navegacion.AreaCalzada);
            float mejor = float.MaxValue;
            Vector3 elegido = e.puertas;
            bool hay = false;
            var b = e.info.limites;
            var p0 = e.info.plantas.Find(x => x.nombre == "P0");
            float cotaP0 = p0 != null ? p0.cota : float.NaN;
            foreach (var pu in e.info.puertasExteriores)
            {
                var c = RegistroCampus.CentroPuerta(pu);
                if (!float.IsNaN(cotaP0) && Mathf.Abs(c.y - cotaP0) > 1.5f) continue;   // puertas de sótano o de planta alta
                // Hacia fuera: el lado de la puerta que no cae en ningún local
                var f = pu.transform.forward;
                f.y = 0;
                f.Normalize();
                if (RegistroCampus.LocalEn(c + f * 1.2f + Vector3.up * 0.2f) != null) f = -f;
                foreach (float dist in new[] { 18f, 26f, 34f })
                foreach (float giro in new[] { 0f, -35f, 35f })
                {
                    var q = c + Quaternion.Euler(0, giro, 0) * f * dist;
                    q.y = terreno != null ? terreno.SampleHeight(q) + terreno.transform.position.y : c.y;
                    if (!NavMesh.SamplePosition(q, out var h, 4f, sinCalzada)) continue;
                    var p = h.position;
                    if (RegistroCampus.LocalEn(p + Vector3.up * 0.2f) != null) continue;
                    if (terreno != null && Mathf.Abs(terreno.SampleHeight(p) + terreno.transform.position.y - p.y) > 1.2f) continue;
                    bool dentro = false;
                    foreach (var o in edificios)
                    {
                        var lim = o.info.limites;
                        if (o == e) lim.Expand(4f);
                        if (lim.Contains(p) && RegistroCampus.LocalEn(p + Vector3.up * 0.2f) == null && o != e) continue;
                        if (o == e && lim.Contains(p)) { dentro = true; break; }
                    }
                    if (dentro) continue;
                    // Espacio: cuántos de 8 puntos a 5 m siguen en la malla
                    int libre = 0;
                    for (int k = 0; k < 8; k++)
                        if (NavMesh.SamplePosition(p + Quaternion.Euler(0, k * 45f, 0) * Vector3.forward * 5f, out _, 0.8f, sinCalzada)) libre++;
                    // Que se llegue de verdad desde esa puerta, y por poco camino
                    if (!nav.Punto(c + f * 1.5f, 1.5f, out var fuera, 1f)) continue;
                    if (!NavMesh.CalculatePath(fuera, p, nav.Filtro, caminoTmp) || caminoTmp.status != NavMeshPathStatus.PathComplete) continue;
                    float largo = 0f;
                    var cs = caminoTmp.corners;
                    for (int k = 1; k < cs.Length; k++) largo += Vector3.Distance(cs[k - 1], cs[k]);
                    if (largo > dist * 2.2f) continue;
                    float coste = largo + (8 - libre) * 6f;
                    if (coste < mejor) { mejor = coste; elegido = p; hay = true; }
                }
            }
            if (!hay && nav.Punto(b.center + new Vector3(b.extents.x + 20f, 0, 0), 15f, out var r)) elegido = r;
            e.reunion = elegido;
        }

        // ───────────────────────── Reconciliación con el servicio ─────────────────────────

        public void Reconciliar(ClienteGemelo cliente)
        {
            if (cliente == null || cliente.Estado == null) return;
            int max = Mathf.Max(0, Ajustes.Actual.maxPersonas);
            float ahora = Time.time;

            // Salto de reloj (alguien cambia la hora en el menú o el servicio la rehace, o un cambio grande de escala):
            // el estado nuevo no sigue al anterior, así que todo se coloca directamente, como al arrancar. Si no, tras
            // saltar de la noche a media mañana entraban 1.700 personas andando desde los aparcamientos a la vez (y
            // la cola de rutas se iba a decenas de milisegundos por fotograma)
            var hora = HoraCampus.Utc;
            float real = Time.realtimeSinceStartup;
            bool salto = !inicial && System.Math.Abs((hora - horaAnterior).TotalSeconds - (real - realAnterior) * HoraCampus.Escala) > 900.0;
            horaAnterior = hora;
            realAnterior = real;
            bool colocar = inicial || salto;
            if (salto)
            {
                SaltosReloj++;
                for (int i = activas.Count - 1; i >= 0; i--)
                    if (i < activas.Count && activas[i].Andando && activas[i].evacuando == null) Colocar(activas[i]);
            }

            // Alarmas
            foreach (var e in edificios)
            {
                bool a = Alarma(cliente, e);
                if (a == e.alarma) continue;
                e.alarma = a;
                if (a) EmpezarEvacuacion(e);
                else
                    foreach (var p in activas)
                        if (p.evacuando == e)
                        {
                            p.evacuando = null;
                            p.tDecision = ahora + 25f;   // espera un rato por si vuelve a su sitio
                        }
            }

            // Datos por estancia
            libres.Clear();
            deficits.Clear();
            foreach (var e in estancias)
            {
                float v = 0f;
                if (cliente.Estancia(e.Clave, out var st))
                {
                    v = st.Valor("occupancy", 0f);
                    if (float.IsNaN(v)) v = 0f;
                }
                e.dato = v;
                if (e.edificio.alarma) continue;
                int n = e.miembros.Count;
                if (v < n - 0.6f)
                {
                    int sobra = n - Mathf.RoundToInt(v);
                    for (int k = 0; k < sobra && e.miembros.Count > 0; k++)
                    {
                        // Sale antes quien ya está quieto (no el que acaba de llegar ni el que viene de camino)
                        int idx = rnd.Next(e.miembros.Count);
                        for (int t = 0; t < 4; t++)
                        {
                            var c = e.miembros[(idx + t) % e.miembros.Count];
                            if (c.estado == EstadoPersona.Quieta) { idx = (idx + t) % e.miembros.Count; break; }
                        }
                        var p = e.miembros[idx];
                        Soltar(p);
                        p.tDecision = ahora;
                        libres.Add(p);
                    }
                }
                else if (v > n + 0.6f) deficits.Add((e, Mathf.RoundToInt(v) - n));
            }
            // También los que esperan sin sitio (tras una alarma)
            foreach (var p in activas)
                if (p.destino == null && !p.saliendo && p.evacuando == null && !libres.Contains(p)) libres.Add(p);

            // Barajar los déficits para no llenar siempre en el mismo orden
            for (int i = deficits.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (deficits[i], deficits[j]) = (deficits[j], deficits[i]); }

            // Primero, los que se mueven de una estancia a otra (preferiblemente del mismo edificio)
            for (int d = 0; d < deficits.Count; d++)
            {
                var (e, n) = deficits[d];
                while (n > 0 && libres.Count > 0)
                {
                    int mejor = -1;
                    float md = float.MaxValue;
                    for (int i = 0; i < libres.Count; i++)
                    {
                        var p = libres[i];
                        float dist = Vector3.Distance(p.pos, e.acceso) + (p.edificio == e.edificio ? 0f : 150f);
                        if (dist < md) { md = dist; mejor = i; }
                    }
                    var q = libres[mejor];
                    libres.RemoveAt(mejor);
                    Asignar(q, e, colocar);
                    n--;
                }
                deficits[d] = (e, n);
            }
            // Lo que aún falta entra al campus
            foreach (var (e, n0) in deficits)
            {
                for (int k = 0; k < n0 && activas.Count < max; k++)
                {
                    if (!e.accesible) break;
                    var p = Nueva(e);
                    if (colocar || llegadas.Count == 0) Asignar(p, e, true);
                    else
                    {
                        var l = ElegirLlegada(e.acceso);
                        p.llegada = l;
                        p.edificio = null;
                        p.pos = l.puntos[rnd.Next(l.puntos.Count)];
                        p.rumbo = (float)rnd.NextDouble() * 6.28f;
                        Asignar(p, e, false);
                    }
                }
            }
            // Y lo que sobra se va (tras esperar un poco los que salen de una alarma)
            foreach (var p in libres)
                if (colocar && p.evacuando == null) Desactivar(p);
                else if (ahora >= p.tDecision) Salir(p);

            // Por encima del tope (si se baja en los ajustes)
            while (activas.Count > max) Desactivar(activas[activas.Count - 1]);
            inicial = false;
        }

        static bool Alarma(ClienteGemelo c, EdificioPersonas e)
        {
            if (!c.Edificio(e.info.codigo, out var b)) return false;
            if (b.alerts != null) foreach (var a in b.alerts) if (a.code == "fire_alarm") return true;
            if (b.spaces != null)
                foreach (var s in b.spaces)
                {
                    if (s.Valor("alarm_active", 0f) > 0.5f) return true;
                    if (s.alerts != null) foreach (var a in s.alerts) if (a.code == "fire_alarm") return true;
                }
            return false;
        }

        Llegada ElegirLlegada(Vector3 hacia)
        {
            float tot = 0;
            foreach (var l in llegadas) tot += l.peso / (25f + Vector3.Distance(l.pos, hacia));
            float x = (float)rnd.NextDouble() * tot;
            foreach (var l in llegadas)
            {
                x -= l.peso / (25f + Vector3.Distance(l.pos, hacia));
                if (x <= 0) return l;
            }
            return llegadas[0];
        }

        Persona Nueva(Estancia e)
        {
            var p = libresPool.Count > 0 ? libresPool.Pop() : new Persona();
            p.id = siguienteId++;
            p.semilla = p.id * 7 + 3;
            var uso = e.info.local.uso ?? "";
            p.ap = Apariencia.Generar(p.semilla, uso.StartsWith("aula") || uso.StartsWith("sala") || uso == "seminario" ? 0.55f : 0.15f);
            p.estado = EstadoPersona.Quieta;
            p.destino = null; p.donde = null; p.plaza = -1; p.grupo = -1; p.llegada = null; p.saliendo = false; p.evacuando = null;
            p.sentada = false; p.gesto = Gesto.Nada; p.vel = 0; p.nTramos = 0; p.pendienteRuta = false; p.avatar = -1;
            p.lateral = p.lateralObj = ((float)rnd.NextDouble() - 0.5f) * 0.4f;
            p.fase = (float)rnd.NextDouble();
            p.multVel = 1f;
            p.edificio = e.edificio;
            p.tEdificio = 0;
            activas.Add(p);
            return p;
        }

        void Desactivar(Persona p)
        {
            Soltar(p);
            p.estado = EstadoPersona.Inactiva;
            activas.Remove(p);
            cola.Remove(p);
            libresPool.Push(p);
        }

        /// <summary>Quita a la persona de su estancia (y su plaza) sin moverla todavía.</summary>
        void Soltar(Persona p)
        {
            if (p.destino != null)
            {
                p.destino.miembros.Remove(p);
                if (p.plaza >= 0 && p.plaza < p.destino.plazas.Length && p.destino.plazas[p.plaza].ocupante == p.id)
                    p.destino.plazas[p.plaza].ocupante = -1;
            }
            p.plaza = -1;
            p.destino = null;
            p.grupo = -1;
        }

        // ───────────────────────── Viajes ─────────────────────────

        bool Teletransportar(float distancia, float vel)
        {
            float escala = Mathf.Max(1f, HoraCampus.Escala);
            if (escala >= 300f) return true;
            float necesario = distancia * 1.4f / Mathf.Max(0.5f, vel);
            float permitido = 900f / escala;   // un cuarto de hora del modelo
            return necesario > permitido;
        }

        void Asignar(Persona p, Estancia e, bool directo)
        {
            Soltar(p);
            p.destino = e;
            p.saliendo = false;
            e.miembros.Add(p);
            int pl = e.sentados ? e.PlazaLibre(rnd) : -1;
            if (pl >= 0)
            {
                e.plazas[pl].ocupante = p.id;
                p.plaza = pl;
                p.objetivo = e.plazas[pl].pos;
                p.rumboFinal = e.plazas[pl].rumbo;
                p.alturaAsiento = e.plazas[pl].altura;
            }
            else if (!UnirseAGrupo(p, e))
            {
                e.PuntoDePie(rnd, out p.objetivo);
                p.rumboFinal = (float)rnd.NextDouble() * 6.28f;
            }
            float dist = Vector3.Distance(p.pos, p.objetivo);
            if (directo || p.donde == e && dist < 0.3f || Teletransportar(dist + Mathf.Abs(p.pos.y - p.objetivo.y) * 4f, p.ap.paso))
            {
                if (!directo) Teletransportes++;
                Colocar(p);
                return;
            }
            Viajes++;
            p.estado = EstadoPersona.Preparando;
            p.tArranque = Time.time + (p.sentada ? 0.8f + (float)rnd.NextDouble() * 1.5f : 0.1f + (float)rnd.NextDouble() * 0.8f);
            p.sentada = false;
            p.gesto = Gesto.Nada;
            p.velObjetivo = p.ap.paso;
            p.multVel = HoraCampus.Escala > 20f ? 1.6f : 1f;
            Encolar(p, false);
        }

        bool UnirseAGrupo(Persona p, Estancia e)
        {
            if (e.sentados && !e.cafeteria) return false;
            if (rnd.NextDouble() > 0.6) return false;
            foreach (var m in e.miembros)
            {
                if (m == p || m.plaza >= 0 || m.estado != EstadoPersona.Quieta && m.estado != EstadoPersona.Andando) continue;
                int lider = m.grupo >= 0 ? m.grupo : m.id;
                int tam = 0;
                foreach (var o in e.miembros) if (o.grupo == lider) tam++;
                if (tam >= 4) continue;
                if (m.grupo < 0)
                {
                    m.grupo = m.id;
                    m.centroGrupo = m.objetivo + new Vector3(Mathf.Sin(m.rumboFinal), 0, Mathf.Cos(m.rumboFinal)) * 0.45f;
                }
                var centro = m.centroGrupo;
                for (int intento = 0; intento < 6; intento++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    var q = centro + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.5f;
                    if (!nav.Punto(q, 0.25f, out var h, 0.3f) || !e.info.local.Contiene(h)) continue;
                    bool libre = true;
                    foreach (var o in e.miembros) if (o != p && (o.objetivo - h).sqrMagnitude < 0.35f) { libre = false; break; }
                    if (!libre) continue;
                    p.objetivo = h;
                    p.grupo = lider;
                    p.centroGrupo = centro;
                    var d = centro - h;
                    p.rumboFinal = Mathf.Atan2(d.x, d.z);
                    if (m.grupo == m.id && m.estado == EstadoPersona.Quieta)
                    {
                        var dm = centro - m.objetivo;
                        m.rumboFinal = Mathf.Atan2(dm.x, dm.z);
                    }
                    return true;
                }
            }
            return false;
        }

        void Colocar(Persona p)
        {
            p.pos = p.objetivo;
            p.rumbo = p.rumboFinal;
            p.nTramos = 0;
            Llegar(p);
        }

        void Salir(Persona p)
        {
            Soltar(p);
            p.saliendo = true;
            p.evacuando = null;
            if (llegadas.Count == 0) { Desactivar(p); return; }
            var l = ElegirLlegada(p.pos);
            p.llegada = l;
            p.objetivo = l.puntos[rnd.Next(l.puntos.Count)];
            if (Teletransportar(Vector3.Distance(p.pos, p.objetivo), p.ap.paso)) { Teletransportes++; Desactivar(p); return; }
            Viajes++;
            p.estado = EstadoPersona.Preparando;
            p.tArranque = Time.time + (p.sentada ? 0.8f + (float)rnd.NextDouble() * 1.5f : (float)rnd.NextDouble() * 0.6f);
            p.sentada = false;
            p.gesto = Gesto.Nada;
            p.velObjetivo = p.ap.paso;
            p.multVel = HoraCampus.Escala > 20f ? 1.6f : 1f;
            Encolar(p, false);
        }

        void EmpezarEvacuacion(EdificioPersonas e)
        {
            PrepararReunion(e);
            int n = 0;
            foreach (var p in activas.ToArray())
            {
                bool afectada = p.destino != null && p.destino.edificio == e || p.edificio == e && p.destino == null && !p.saliendo;
                if (!afectada) continue;
                n++;
                Soltar(p);
                p.evacuando = e;
                p.saliendo = false;
                // Un hueco en el punto de reunión (un corro amplio)
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 2f + (float)rnd.NextDouble() * 9f;
                var q = e.reunion + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                p.objetivo = nav.Punto(q, 3f, out var h, 3f) ? h : e.reunion;
                var hacia = e.info.limites.center - p.objetivo;
                p.rumboFinal = Mathf.Atan2(hacia.x, hacia.z) + ((float)rnd.NextDouble() - 0.5f) * 1.2f;
                if (Teletransportar(Vector3.Distance(p.pos, p.objetivo), 3f)) { Teletransportes++; Colocar(p); continue; }
                Viajes++;
                p.estado = EstadoPersona.Preparando;
                p.tArranque = Time.time + 0.3f + (float)rnd.NextDouble() * (p.sentada ? 3f : 2f);
                p.sentada = false;
                p.gesto = Gesto.Nada;
                p.velObjetivo = 2.4f + (float)rnd.NextDouble() * 1.1f;
                p.multVel = 1f;
                Encolar(p, true);
            }
            Debug.Log($"[Personas] Alarma en {e.info.codigo}: evacuan {n} personas hacia {e.reunion}");
        }

        void Encolar(Persona p, bool urgente)
        {
            if (p.pendienteRuta) cola.Remove(p);
            p.pendienteRuta = true;
            if (urgente) cola.AddFirst(p); else cola.AddLast(p);
        }

        /// <summary>Calcula rutas pendientes hasta agotar el presupuesto (ms).</summary>
        public void ProcesarRutas(float presupuestoMs)
        {
            var reloj = Stopwatch.StartNew();
            int hechas = 0;
            while (cola.Count > 0 && (hechas == 0 || reloj.Elapsed.TotalMilliseconds < presupuestoMs))
            {
                var p = cola.First.Value;
                cola.RemoveFirst();
                p.pendienteRuta = false;
                if (!p.Activa || p.estado != EstadoPersona.Preparando) continue;
                PlanificarRuta(p);
                hechas++;
            }
            MsRutas = reloj.Elapsed.TotalMilliseconds;
        }

        // Ruta entre dos puntos cualesquiera (fuera de la malla se sale y se entra en línea recta)
        Ruta Tramo(Vector3 a, Vector3 b)
        {
            if (!nav.Punto(a, 1.6f, out var a2, 1.2f) || !nav.Punto(b, 1.6f, out var b2, 1.2f)) return null;
            var r = nav.Calcular(a2, b2);
            if (r == null) return null;
            if ((a - a2).sqrMagnitude < 0.01f && (b - r.p[r.p.Length - 1]).sqrMagnitude < 0.01f) return r;
            var l = new List<Vector3>(r.p.Length + 2);
            if ((a - a2).sqrMagnitude >= 0.01f) l.Add(a);
            l.AddRange(r.p);
            if ((b - r.p[r.p.Length - 1]).sqrMagnitude >= 0.01f) l.Add(b);
            return new Ruta(l, r.completa);
        }

        Ruta EntreNodos(int na, Vector3 a, int nb, Vector3 b, out bool inversa)
        {
            var r = nav.Cache(na, nb, out inversa);
            if (r != null) return r;
            r = Tramo(a, b);
            if (r != null) nav.Guardar(na, nb, r);
            inversa = false;
            return r;
        }

        void PlanificarRuta(Persona p)
        {
            p.nTramos = 0;
            p.tramo = 0;
            p.s = 0;
            int nodoB;
            Vector3 posB;
            if (p.saliendo) { nodoB = p.llegada.nodo; posB = p.llegada.pos; }
            else if (p.evacuando != null) { nodoB = p.evacuando.nodoReunion; posB = p.evacuando.reunion; }
            else { nodoB = p.destino.nodo; posB = p.destino.acceso; }

            bool ok = true;
            if (p.donde != null && p.destino == p.donde)
            {
                // Dentro de la misma estancia
                var r = Tramo(p.pos, p.objetivo);
                ok = Anadir(p, r, false);
            }
            else
            {
                int nodoA = -1;
                Vector3 posA = p.pos;
                if (p.donde != null && p.donde.accesible) { nodoA = p.donde.nodo; posA = p.donde.acceso; }
                else if (p.llegada != null && !p.saliendo && (p.pos - p.llegada.pos).sqrMagnitude < 60f * 60f && p.edificio == null)
                { nodoA = p.llegada.nodo; posA = p.llegada.pos; }

                if (nodoA >= 0)
                {
                    if ((p.pos - posA).sqrMagnitude > 0.25f) ok &= Anadir(p, Tramo(p.pos, posA), false);
                    if (ok)
                    {
                        var r = EntreNodos(nodoA, posA, nodoB, posB, out bool inv);
                        ok &= Anadir(p, r, inv);
                    }
                }
                else ok &= Anadir(p, Tramo(p.pos, posB), false);
                if (ok && (posB - p.objetivo).sqrMagnitude > 0.16f) ok &= Anadir(p, Tramo(posB, p.objetivo), false);
            }
            // Ruta imposible o que se queda lejos: se resuelve sin andar
            if (!ok || p.nTramos == 0 || (p.tramos[p.nTramos - 1].Fin(p.inversos[p.nTramos - 1]) - p.objetivo).sqrMagnitude > 9f)
            {
                Fallos++;
                if (p.saliendo) Desactivar(p);
                else Colocar(p);
                return;
            }
            p.donde = null;
        }

        bool Anadir(Persona p, Ruta r, bool inv)
        {
            if (r == null || p.nTramos >= p.tramos.Length) return false;
            p.tramos[p.nTramos] = r;
            p.inversos[p.nTramos] = inv;
            p.nTramos++;
            return true;
        }

        void Llegar(Persona p)
        {
            p.nTramos = 0;
            p.vel = 0;
            p.pos = p.objetivo;
            if (p.saliendo) { Desactivar(p); return; }
            if (p.evacuando != null)
            {
                p.estado = EstadoPersona.Reunida;
                p.donde = null;
                p.sentada = false;
                p.tDecision = Time.time + 2f + (float)rnd.NextDouble() * 6f;
                return;
            }
            p.estado = EstadoPersona.Quieta;
            p.donde = p.destino;
            p.sentada = p.plaza >= 0;
            p.edificio = p.destino != null ? p.destino.edificio : RegistroEdificio(p.pos);
            p.tDecision = Time.time + 3f + (float)rnd.NextDouble() * 15f;
            p.gesto = Gesto.Nada;
        }

        EdificioPersonas RegistroEdificio(Vector3 pos)
        {
            var e = RegistroCampus.EdificioEn(pos);
            return e != null && porEdificio.TryGetValue(e, out var ep) ? ep : null;
        }

        // ───────────────────────── Paso de simulación ─────────────────────────

        public void Actualizar(float dt)
        {
            float ahora = Time.time;
            bool aTope = HoraCampus.Escala >= 300f;
            rejilla.Rehacer(activas);
            for (int i = activas.Count - 1; i >= 0; i--)
            {
                if (i >= activas.Count) continue;
                var p = activas[i];
                switch (p.estado)
                {
                    case EstadoPersona.Quieta:
                    case EstadoPersona.Reunida:
                        Quieta(p, dt, ahora);
                        break;
                    case EstadoPersona.Preparando:
                        if (!p.pendienteRuta && ahora >= p.tArranque)
                        {
                            if (p.nTramos == 0) { Colocar(p); break; }
                            p.estado = EstadoPersona.Andando;
                            p.donde = null;
                        }
                        else if (p.vel > 0) p.vel = 0;
                        break;
                    case EstadoPersona.Andando:
                        if (aTope) { Colocar(p); break; }
                        Andar(p, dt, ahora);
                        break;
                }
            }
        }

        void Quieta(Persona p, float dt, float ahora)
        {
            p.vel = 0;
            p.rumbo = Mathf.LerpAngle(p.rumbo * Mathf.Rad2Deg, p.rumboFinal * Mathf.Rad2Deg, 1f - Mathf.Exp(-6f * dt)) * Mathf.Deg2Rad;
            if (ahora < p.tDecision) return;
            // Gestos: hablar con el de al lado, mirar el móvil o nada
            var e = p.donde;
            float pHabla = p.estado == EstadoPersona.Reunida ? 0.45f : p.grupo >= 0 ? 0.45f : e != null && e.cafeteria ? 0.5f : p.sentada ? 0.15f : 0.1f;
            float pMovil = p.estado == EstadoPersona.Reunida ? 0.35f : p.grupo >= 0 ? 0.12f : p.sentada ? 0.2f : 0.35f;
            if (p.sentada && e != null && e.miembros.Count < 2) pHabla = 0;
            double x = rnd.NextDouble();
            p.gesto = x < pHabla ? Gesto.Habla : x < pHabla + pMovil ? Gesto.Movil : Gesto.Nada;
            p.tDecision = ahora + 4f + (float)rnd.NextDouble() * (p.gesto == Gesto.Habla ? 8f : 18f);
            // De pie y solo: a veces cambia de sitio dentro de la estancia (pasea)
            if (p.estado == EstadoPersona.Quieta && !p.sentada && p.grupo < 0 && e != null && rnd.NextDouble() < 0.35)
            {
                if (e.PuntoDePie(rnd, out var q) && (q - p.pos).sqrMagnitude > 4f && !Teletransportar(Vector3.Distance(q, p.pos), p.ap.paso))
                {
                    p.objetivo = q;
                    p.rumboFinal = (float)rnd.NextDouble() * 6.28f;
                    p.estado = EstadoPersona.Preparando;
                    p.tArranque = ahora + 0.2f;
                    p.gesto = Gesto.Nada;
                    p.velObjetivo = p.ap.paso * 0.8f;
                    p.multVel = 1f;
                    Encolar(p, false);
                }
            }
        }

        void Andar(Persona p, float dt, float ahora)
        {
            var r = p.tramos[p.tramo];
            bool inv = p.inversos[p.tramo];
            float vObj = p.velObjetivo * p.multVel;
            // Frenar al final del viaje
            bool ultimo = p.tramo == p.nTramos - 1;
            float resta = r.Largo - p.s;
            if (ultimo && resta < 1.2f) vObj *= Mathf.Lerp(0.35f, 1f, resta / 1.2f);
            r.En(p.s, inv, out var dir);
            dir.y = 0;
            float dl = dir.magnitude;
            if (dl > 1e-4f) dir /= dl; else dir = new Vector3(Mathf.Sin(p.rumbo), 0, Mathf.Cos(p.rumbo));
            var der = new Vector3(dir.z, 0, -dir.x);

            // Esquivar: mirar a los de delante
            int n = rejilla.Vecinos(p.pos, 1.3f, 1.0f, vecinos);
            float lat = p.lateralObj * 0.98f;
            for (int i = 0; i < n; i++)
            {
                var q = vecinos[i];
                if (q == p) continue;
                var d = q.pos - p.pos;
                d.y = 0;
                float delante = Vector3.Dot(d, dir), lado = Vector3.Dot(d, der);
                if (delante < 0f || delante > 1.3f || Mathf.Abs(lado) > 0.7f) continue;
                // De frente o parado delante: apartarse por la derecha (o por donde haya hueco)
                float sentido = q.Andando ? Vector3.Dot(new Vector3(Mathf.Sin(q.rumbo), 0, Mathf.Cos(q.rumbo)), dir) : 0f;
                if (sentido < 0.3f) lat = lado > 0.1f ? -0.5f : 0.5f;
                else if (delante < 0.7f && q.vel < p.vel) vObj = Mathf.Min(vObj, q.vel + 0.1f);
            }
            p.lateralObj = Mathf.Clamp(lat, -0.6f, 0.6f);
            p.vel = Mathf.MoveTowards(p.vel, vObj, 2.5f * dt);
            p.s += p.vel * dt;
            while (p.s >= r.Largo)
            {
                p.s -= r.Largo;
                p.tramo++;
                if (p.tramo >= p.nTramos) { Llegar(p); return; }
                r = p.tramos[p.tramo];
                inv = p.inversos[p.tramo];
            }
            var pos = r.En(p.s, inv, out dir);
            // Carril propio, menos dentro de los edificios y nada en los extremos de cada tramo
            float margen = Mathf.Clamp01(Mathf.Min(p.s, r.Largo - p.s) / 1.5f);
            float maxLat = (p.edificio != null ? 0.18f : 0.5f) * margen;
            p.lateral = Mathf.MoveTowards(p.lateral, Mathf.Clamp(p.lateralObj, -maxLat, maxLat), 0.8f * dt);
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f)
            {
                dir.Normalize();
                pos += new Vector3(dir.z, 0, -dir.x) * p.lateral;
                float objetivo = Mathf.Atan2(dir.x, dir.z);
                p.rumbo = Mathf.LerpAngle(p.rumbo * Mathf.Rad2Deg, objetivo * Mathf.Rad2Deg, 1f - Mathf.Exp(-8f * dt)) * Mathf.Deg2Rad;
            }
            p.pos = pos;
            float zancada = p.vel > 2f ? 2.5f : 1.4f;
            p.fase += p.vel / (zancada * p.ap.escala) * dt;
            if (ahora > p.tEdificio)
            {
                p.tEdificio = ahora + 0.5f;
                p.edificio = RegistroEdificio(p.pos);
            }
        }

        public int EnCola => cola.Count;
    }
}
