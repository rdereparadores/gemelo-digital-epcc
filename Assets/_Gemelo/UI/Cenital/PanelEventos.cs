// Eventos de simulación sobre la selección (local, edificio o todo el campus): abrir o cerrar ventanas, empezar o
// terminar una clase, calefacción, alarma de incendio, corte eléctrico y fuga de agua, con duración. Debajo, los
// eventos activos (ClienteGemelo.Eventos) con un botón para terminarlos.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class PanelEventos
    {
        readonly VisualElement panel, activos;
        readonly Label objetivoTxt, sinActivos;
        readonly Segmentado duracion;
        Seleccion sel;
        float proximo;
        string firma;

        static readonly string[] Tipos =
            { "open_windows", "close_windows", "class_start", "class_end", "heating_off", "heating_on", "fire_alarm", "power_outage", "water_leak" };
        static readonly float[] Duraciones = { 300, 900, 1800, 3600, 0 };

        public PanelEventos(VisualElement padre)
        {
            panel = Panel(padre, "panel--compacto");
            panel.style.marginTop = 12;
            panel.style.flexShrink = 0;

            var cab = El(panel, "fila");
            Txt(cab, "EVENTOS", "titulo-panel");
            El(cab, "espacio");
            objetivoTxt = Txt(cab, "", "t-xs", "texto-2");
            objetivoTxt.style.marginBottom = 8;

            var rejilla = El(panel, "fila");
            rejilla.style.flexWrap = Wrap.Wrap;
            foreach (var t in Tipos)
            {
                var tt = t;
                var b = El(rejilla, "evento-boton");
                if (t is "fire_alarm" or "power_outage" or "water_leak") b.AddToClassList("evento-boton--peligro");
                b.Add(new Icono(Formato.IconoEvento(t), 22));
                Txt(b, Formato.Evento(t));
                Clic(b, () => Lanzar(tt));
            }

            var fd = El(panel, "fila");
            fd.style.marginTop = 2;
            Txt(fd, "Duración", "t-xs", "tenue").style.marginRight = 10;
            duracion = new Segmentado(new[] { "5 min", "15 min", "30 min", "1 h", "∞" }, 1);
            fd.Add(duracion);

            Txt(panel, "ACTIVOS", "titulo-panel").style.marginTop = 12;
            sinActivos = Txt(panel, "Ningún evento en marcha", "t-s", "tenue");
            activos = El(panel, "columna");
            activos.style.maxHeight = 150;
            activos.style.overflow = Overflow.Hidden;
        }

        public void Objetivo(Seleccion s)
        {
            sel = s;
            Texto(objetivoTxt, s.local != null ? "sobre " + Formato.NombreLocal(s.local)
                : s.edificio != null ? $"sobre el edificio {s.edificio.codigo}" : "sobre todo el campus");
        }

        string IdObjetivo()
        {
            if (sel.local != null) return sel.local.idServicio;
            if (sel.edificio != null) return sel.edificio.idServicio;
            return "campus";
        }

        void Lanzar(string tipo)
        {
            var cli = ClienteGemelo.Instancia;
            var app = GemeloApp.Instancia;
            if (cli == null) return;
            string obj = IdObjetivo();
            if (string.IsNullOrEmpty(obj)) { app.Avisar("Este local no está en el servicio de sensores", NivelAviso.Aviso); return; }
            float dur = Duraciones[Mathf.Clamp(duracion.Activo, 0, Duraciones.Length - 1)];
            string par = null;
            if (tipo == "class_start" && sel.local != null)
                par = $"{{\"people\":{Mathf.RoundToInt(FichaLocal.Capacidad(sel.local) * 0.85f)}}}";
            string donde = objetivoTxt.text;
            cli.CrearEvento(tipo, obj, dur, par, ok =>
            {
                app.Avisar(ok ? $"{Formato.Evento(tipo)} {donde}" : "El servicio no ha aceptado el evento",
                    ok ? (tipo is "fire_alarm" or "power_outage" or "water_leak" ? NivelAviso.Aviso : NivelAviso.Exito) : NivelAviso.Aviso);
                proximo = 0;
            });
        }

        public void Actualizar()
        {
            if (Time.unscaledTime < proximo) return;
            proximo = Time.unscaledTime + 2f;
            ClienteGemelo.Instancia?.Eventos(Pintar);
        }

        void Pintar(List<EventoSimulacion> lista)
        {
            if (lista == null) return;
            var vivos = lista.FindAll(e => e.active);
            var f = new System.Text.StringBuilder();
            foreach (var e in vivos) f.Append(e.id).Append(Restante(e) / 60).Append('|');
            if (f.ToString() == firma) return;
            firma = f.ToString();
            activos.Clear();
            Mostrar(sinActivos, vivos.Count == 0);
            foreach (var e in vivos)
            {
                var ev = e;
                var fila = El(activos, "evento-activo");
                var ic = new Icono(Formato.IconoEvento(e.type), 18);
                ic.style.marginRight = 8;
                if (e.type is "fire_alarm" or "power_outage" or "water_leak") ic.Color = Escalas.Mal;
                fila.Add(ic);
                var col = El(fila, "columna", "crece");
                Txt(col, Formato.Evento(e.type), "t-s", "f-semi");
                float r = Restante(e);
                col.style.flexShrink = 1;
                col.style.marginRight = 8;
                var sub = Txt(col, $"{NombreObjetivo(e.target)} · {(e.duration_s > 0 ? "quedan " + Formato.Duracion(r) : "sin límite")}", "t-xs", "tenue");
                sub.style.whiteSpace = WhiteSpace.Normal;
                var b = Boton(fila, "Terminar", () => ClienteGemelo.Instancia?.TerminarEvento(ev.id, ok => proximo = 0), null, "boton--mini");
                b.tooltip = "Terminar el evento ya";
            }
        }

        static float Restante(EventoSimulacion e)
        {
            if (e.duration_s <= 0 || !Formato.LeerFecha(e.start, out var ini)) return 0;
            return Mathf.Max(0, e.duration_s - (float)(HoraCampus.Utc - ini).TotalSeconds);
        }

        static string NombreObjetivo(string t)
        {
            if (t == "campus") return "Todo el campus";
            var l = RegistroCampus.PorEspacio(t);
            if (l != null) return $"{Formato.NombreLocal(l)} ({l.edificio.codigo})";
            var e = RegistroCampus.Edificio(t);
            if (e != null) return $"Edificio {e.codigo}";
            return t;
        }
    }
}
