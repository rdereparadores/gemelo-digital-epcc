// Interacciones del jugador en tercera persona: según el local en el que está y su estado en el servicio,
// ofrece (GemeloApp.OfrecerInteracciones) abrir o cerrar las ventanas, encender o apagar la calefacción,
// empezar o terminar una clase y, en el edificio, la alarma de incendio y el apagón. Cada una crea un evento
// en la simulación (ClienteGemelo.CrearEvento) y avisa del resultado. E ejecuta la primera; 1–9, la de ese
// número (1 = la primera).
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gemelo
{
    public class InteraccionesJugador : MonoBehaviour
    {
        GemeloApp app;
        readonly Dictionary<string, string> tipos = new();   // space_id → type del servicio (classroom, lab…)
        bool pidiendoTipos;
        float siguiente;
        InfoLocal ultimoLocal;
        string firma = "";
        List<Interaccion> lista = new();
        bool ocupado;

        // Tipos de estancia en los que tiene sentido empezar o terminar una clase (o una reunión)
        static readonly HashSet<string> docentes = new() { "classroom", "computer_lab", "lab", "meeting_room" };

        static readonly Key[] teclasNumero =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };
        static readonly Key[] teclasNumpad =
        {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9,
        };

        void Awake()
        {
            app = GemeloApp.Instancia;
            enabled = false;
        }

        void OnEnable()
        {
            if (app == null) app = GemeloApp.Instancia;
            app.Cliente.EstadoActualizado += AlActualizar;
            firma = "";
            siguiente = 0;
            if (tipos.Count == 0 && !pidiendoTipos) PedirTipos();
        }

        void OnDisable()
        {
            if (app == null) return;
            if (app.Cliente != null) app.Cliente.EstadoActualizado -= AlActualizar;
            app.OfrecerInteracciones(null);
            firma = "";
        }

        void AlActualizar(EstadoCampus e) => siguiente = 0;

        void PedirTipos()
        {
            pidiendoTipos = true;
            app.Cliente.Get("/api/v1/spaces?limit=1000", texto =>
            {
                pidiendoTipos = false;
                if (texto == null) return;
                var l = JsonUtility.FromJson<ListaEspacios>(texto);
                if (l?.items == null) return;
                foreach (var s in l.items) if (!string.IsNullOrEmpty(s.id)) tipos[s.id] = s.type;
                siguiente = 0;
            });
        }

        [System.Serializable] class Espacio { public string id; public string type; }
        [System.Serializable] class ListaEspacios { public List<Espacio> items; }

        void Update()
        {
            if (app.LocalActual != ultimoLocal) { ultimoLocal = app.LocalActual; siguiente = 0; }
            if (Time.unscaledTime >= siguiente)
            {
                siguiente = Time.unscaledTime + 1f;
                Recalcular();
            }
            if (!ControlGemelo.Jugando || ocupado) return;
            var kb = Keyboard.current;
            if (kb == null || lista.Count == 0) return;
            int i = -1;
            if (kb.eKey.wasPressedThisFrame) i = 0;
            for (int k = 0; k < 9 && i < 0; k++)
                if (kb[teclasNumero[k]].wasPressedThisFrame || kb[teclasNumpad[k]].wasPressedThisFrame) i = k;
            if (i >= 0 && i < lista.Count)
            {
                try { lista[i].ejecutar?.Invoke(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        void Recalcular()
        {
            var nueva = new List<Interaccion>();
            var l = app.LocalActual;
            var cli = app.Cliente;
            if (l != null && cli != null)
            {
                string nombre = NombreLocal(l);
                if (l.idServicio != null && cli.Espacio(l.idServicio, out var est))
                {
                    string id = l.idServicio;
                    // Clase
                    if (tipos.TryGetValue(id, out var tipo) && docentes.Contains(tipo))
                    {
                        float ocup = est.Valor("occupancy", 0f);
                        bool reunion = tipo == "meeting_room";
                        if (ocup >= 1f)
                            nueva.Add(Nueva(reunion ? "Terminar la reunión" : "Terminar la clase", "clase",
                                () => Evento("class_end", id, 0, null, $"Se acabó la {(reunion ? "reunión" : "clase")} en {nombre}: la sala se vacía")));
                        else
                        {
                            int n = Personas(l);
                            nueva.Add(Nueva($"{(reunion ? "Empezar una reunión" : "Empezar una clase")} ({n} personas)", "clase",
                                () => Evento("class_start", id, 0, $"{{\"people\":{n}}}", $"Empieza una {(reunion ? "reunión" : "clase")} de {n} personas en {nombre}")));
                        }
                    }
                    // Ventanas
                    if (est.windows_total > 0)
                    {
                        if (est.windows_open > 0)
                            nueva.Add(Nueva(est.windows_open == 1 ? "Cerrar la ventana" : $"Cerrar las ventanas ({est.windows_open} abiertas)", "ventana",
                                () => Evento("close_windows", id, 1800, null, $"Ventanas cerradas en {nombre}")));
                        if (est.windows_open < est.windows_total)
                            nueva.Add(Nueva(est.windows_total == 1 ? "Abrir la ventana" : "Abrir las ventanas", "ventana",
                                () => Evento("open_windows", id, 1800, null, $"Ventanas abiertas en {nombre} (media hora)")));
                    }
                    // Calefacción
                    if (est.radiators_total > 0)
                    {
                        if (est.radiators_active > 0)
                            nueva.Add(Nueva("Apagar la calefacción", "calefaccion",
                                () => Evento("heating_off", id, 3600, null, $"Calefacción apagada en {nombre} (una hora)")));
                        else
                            nueva.Add(Nueva("Encender la calefacción", "calefaccion",
                                () => Evento("heating_on", id, 3600, null, $"Calefacción encendida en {nombre} (una hora)")));
                    }
                }
                // Edificio
                var e = l.edificio;
                if (e != null && cli.Edificio(e.codigo, out var eb))
                {
                    string ide = e.idServicio;
                    string ne = string.IsNullOrEmpty(e.nombre) ? ide : e.nombre;
                    if (MetricaEdificio(eb, "alarm_active", 0f) > 0.5f)
                        nueva.Add(Nueva("Desactivar la alarma de incendio", "alarma", () => Terminar("fire_alarm", ide, $"Alarma de incendio desactivada en {ne}")));
                    else
                        nueva.Add(Nueva("Activar la alarma de incendio", "alarma",
                            () => Evento("fire_alarm", ide, 900, null, $"¡Alarma de incendio en {ne}! Hay que desalojar", NivelAviso.Critico)));
                    if (MetricaEdificio(eb, "power_on", 1f) < 0.5f)
                        nueva.Add(Nueva("Restablecer la luz", "apagon", () => Terminar("power_outage", ide, $"Vuelve la luz en {ne}")));
                    else
                        nueva.Add(Nueva("Provocar un apagón", "apagon",
                            () => Evento("power_outage", ide, 600, null, $"Apagón en {ne} (diez minutos)", NivelAviso.Aviso)));
                }
            }

            var sb = new StringBuilder();
            foreach (var i in nueva) sb.Append(i.texto).Append('|');
            string f = sb.ToString();
            if (f == firma) return;
            firma = f;
            lista = nueva;
            app.OfrecerInteracciones(lista);
        }

        static Interaccion Nueva(string texto, string icono, System.Action accion) => new() { texto = texto, icono = icono, ejecutar = accion };

        static float MetricaEdificio(EstadoEdificio e, string prop, float porDefecto)
        {
            if (e?.metrics == null) return porDefecto;
            foreach (var m in e.metrics) if (m.property == prop) return m.value;
            return porDefecto;
        }

        static int Personas(InfoLocal l)
        {
            float cap = l.local.capacidad > 0 ? l.local.capacidad : Mathf.Max(4f, l.area / 2.2f);
            return Mathf.Max(2, Mathf.RoundToInt(cap * 0.85f));
        }

        static string NombreLocal(InfoLocal l) => string.IsNullOrEmpty(l.Nombre) ? l.clave : l.Nombre;

        static string Opuesto(string tipo) => tipo switch
        {
            "open_windows" => "close_windows", "close_windows" => "open_windows",
            "heating_on" => "heating_off", "heating_off" => "heating_on",
            "class_start" => "class_end", "class_end" => "class_start",
            _ => null,
        };

        // Termina los eventos activos del mismo tipo o del contrario sobre el objetivo (para no apilarlos) y crea el nuevo
        void Evento(string tipo, string objetivo, float duracion, string parametros, string textoOk, NivelAviso nivel = NivelAviso.Exito)
        {
            ocupado = true;
            var cli = app.Cliente;
            string opuesto = Opuesto(tipo);
            cli.Eventos(evs =>
            {
                if (evs != null)
                    foreach (var ev in evs)
                        if (ev.active && ev.target == objetivo && (ev.type == tipo || ev.type == opuesto) && tipo != "class_end")
                            cli.TerminarEvento(ev.id);
                cli.CrearEvento(tipo, objetivo, duracion, parametros, ok =>
                {
                    ocupado = false;
                    siguiente = Time.unscaledTime + 0.3f;
                    app.Avisar(ok ? textoOk : "El servicio de sensores no ha aceptado la orden", ok ? nivel : NivelAviso.Aviso);
                });
            });
        }

        // Termina los eventos activos de un tipo sobre el edificio
        void Terminar(string tipo, string objetivo, string textoOk)
        {
            ocupado = true;
            var cli = app.Cliente;
            cli.Eventos(evs =>
            {
                int n = 0;
                if (evs != null)
                    foreach (var ev in evs)
                        if (ev.active && ev.type == tipo && (ev.target == objetivo || ev.target == "campus"))
                        {
                            cli.TerminarEvento(ev.id);
                            n++;
                        }
                ocupado = false;
                siguiente = Time.unscaledTime + 0.5f;
                if (n > 0) app.Avisar(textoOk, NivelAviso.Exito);
                else app.Avisar("No hay ningún evento que terminar: lo provoca el escenario de la simulación", NivelAviso.Aviso);
            });
        }
    }
}
