// Ficha compacta de un local con sus datos en vivo: ocupación frente a la capacidad, temperatura, CO₂, humedad,
// confort, ventanas y radiadores, fuente del dato (real o simulado) y alertas. La usan el HUD y el centro de control.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class FichaLocal
    {
        public readonly VisualElement raiz;
        readonly Label uso, nombre, ocupacion, capacidad, sinDatos;
        readonly VisualElement fuente, barra, relleno, metricas, alertas, extras;
        readonly Label fuenteTxt;
        readonly Dictionary<string, (Label valor, Icono icono)> valores = new();
        readonly Label ventanas, radiadores;
        readonly bool cabecera;

        public FichaLocal(VisualElement padre, bool conCabecera = true)
        {
            cabecera = conCabecera;
            raiz = El(padre, "columna");
            var cab = El(raiz, "fila");
            var col = El(cab, "columna", "crece");
            uso = Txt(col, "", "t-xs", "tenue", "f-semi", "mayus");
            nombre = Txt(col, "", "t-l", "f-semi");
            nombre.style.whiteSpace = WhiteSpace.Normal;
            fuente = El(cab, "insignia");
            fuente.Add(new Icono("sensor", 12) { style = { marginRight = 5 } });
            fuenteTxt = Txt(fuente, "");
            Mostrar(cab, conCabecera);

            // Ocupación
            var fo = El(raiz, "fila");
            fo.style.marginTop = conCabecera ? 12 : 0;
            Ico(fo, "personas", 18).style.marginRight = 8;
            ocupacion = Txt(fo, "—", "t-xl", "f-semi");
            capacidad = Txt(fo, "", "t-s", "tenue");
            capacidad.style.marginLeft = 6;
            capacidad.style.alignSelf = Align.FlexEnd;
            capacidad.style.marginBottom = 4;
            barra = El(raiz, "barra");
            barra.style.marginTop = 6;
            relleno = El(barra, "barra__relleno");
            relleno.style.width = Length.Percent(0);

            sinDatos = Txt(raiz, "Este local no tiene sensores en el servicio.", "t-s", "tenue");
            sinDatos.style.marginTop = 10;
            sinDatos.style.whiteSpace = WhiteSpace.Normal;

            metricas = El(raiz, "metricas");
            metricas.style.marginTop = 8;
            Metrica("air_temperature", "termometro", "Temperatura");
            Metrica("co2_concentration", "viento", "CO₂");
            Metrica("relative_humidity", "gota", "Humedad");
            Metrica("comfort", "confort", "Confort");

            extras = El(raiz, "fila");
            extras.style.marginTop = 2;
            var v = El(extras, "fila", "crece");
            Ico(v, "ventana", 16).style.marginRight = 6;
            ventanas = Txt(v, "", "t-s", "texto-2");
            var r = El(extras, "fila", "crece");
            Ico(r, "radiador", 16).style.marginRight = 6;
            radiadores = Txt(r, "", "t-s", "texto-2");

            alertas = El(raiz, "columna");
            alertas.style.marginTop = 4;
        }

        void Metrica(string clave, string icono, string titulo)
        {
            var m = El(metricas, "metrica");
            var ic = Ico(m, icono, 20);
            var col = El(m, "columna");
            var val = Txt(col, "—", "metrica__valor");
            Txt(col, titulo, "metrica__nombre");
            valores[clave] = (val, ic);
        }

        public static float Capacidad(InfoLocal l)
        {
            if (l == null) return 0;
            if (l.local.capacidad > 0) return l.local.capacidad;
            int sillas = l.local.asientos?.Length ?? 0;
            return sillas > 0 ? sillas : Mathf.Max(2f, Mathf.Round(l.area / 2.5f));
        }

        public void Poner(InfoLocal l, EstadoEstancia e)
        {
            if (l == null) return;
            Texto(uso, $"{Formato.Uso(l.local.uso).ToUpperInvariant()} · {Formato.PlantaCorta(l.planta?.nombre)}");
            Texto(nombre, Formato.NombreLocal(l));

            bool hay = e != null && e.metrics != null && e.metrics.Count > 0;
            Mostrar(sinDatos, !hay);
            Mostrar(metricas, hay);
            Mostrar(extras, hay && (e.windows_total > 0 || e.radiators_total > 0));
            Mostrar(fuente, hay && cabecera);
            float cap = Capacidad(l);
            float oc = hay ? e.Valor("occupancy") : float.NaN;
            Texto(ocupacion, float.IsNaN(oc) ? "—" : Formato.Num(oc));
            Texto(capacidad, $"/ {Formato.Num(cap)} plazas");
            float frac = float.IsNaN(oc) ? 0 : Mathf.Clamp01(oc / Mathf.Max(1, cap));
            relleno.style.width = Length.Percent(frac * 100f);
            relleno.style.backgroundColor = frac > 0.9f ? Escalas.Naranja : Escalas.Amarillo;
            if (!hay) { alertas.Clear(); return; }

            Valor("air_temperature", e, ColorTemp(e.Valor("air_temperature")));
            Valor("co2_concentration", e, ColorCO2(e.Valor("co2_concentration")));
            Valor("relative_humidity", e, null);
            var (lc, ic) = valores["comfort"];
            Texto(lc, Escalas.ConfortTexto(e.comfort));
            var cc = Escalas.ColorConfort(Escalas.ConfortIndice(e.comfort));
            ic.Color = cc.a > 0 ? cc : (Color?)null;

            Texto(ventanas, e.windows_total > 0 ? $"{e.windows_open} de {e.windows_total} ventanas abiertas" : "Sin sensores de ventana");
            Texto(radiadores, e.radiators_total > 0 ? $"{e.radiators_active} de {e.radiators_total} radiadores" : "Sin radiadores");

            // Fuente del dato: real (verde), simulado (violeta) o mixto
            bool real = false, sim = false;
            foreach (var m in e.metrics)
            {
                if (m.source == "real") real = true;
                else if (m.source == "simulated") sim = true;
                else if (m.source == "mixed") { real = sim = true; }
            }
            Clase(fuente, "insignia--real", real && !sim);
            Clase(fuente, "insignia--sim", sim && !real);
            Clase(fuente, "insignia--mixto", real && sim);
            Texto(fuenteTxt, real && sim ? "Mixto" : real ? "Real" : "Simulado");

            PintarAlertas(alertas, e.alerts, 3, null);
        }

        void Valor(string p, EstadoEstancia e, Color? color)
        {
            var (l, ic) = valores[p];
            Texto(l, Formato.Valor(p, e.Valor(p)));
            ic.Color = color;
        }

        static Color? ColorTemp(float t) => float.IsNaN(t) ? null : t < 19 ? Escalas.Frio : t > 26 ? Escalas.Caliente : (Color?)null;
        static Color? ColorCO2(float c) => float.IsNaN(c) ? null : c > 1500 ? Escalas.Mal : c > 1000 ? Escalas.Naranja : (Color?)null;

        /// <summary>Pinta una lista de alertas (las críticas primero), con clic opcional.</summary>
        public static void PintarAlertas(VisualElement cont, List<Alerta> lista, int max, System.Action<Alerta> alPulsar)
        {
            cont.Clear();
            if (lista == null) return;
            int n = 0;
            foreach (var sev in new[] { "critical", "warning", "info" })
                foreach (var a in lista)
                {
                    if (a.severity != sev || n >= max) continue;
                    if (a.code == "sensor_stale" || a.code == "sensor_battery_low") continue;
                    n++;
                    var el = El(cont, "alerta", sev == "critical" ? "alerta--critico" : sev == "warning" ? "alerta--aviso" : "alerta--info");
                    el.Add(new Icono(sev == "info" ? "info" : "alerta", 16));
                    Txt(el, a.message, "alerta__texto");
                    if (alPulsar != null)
                    {
                        el.AddToClassList("alerta--clic");
                        var aa = a;
                        Clic(el, () => alPulsar(aa));
                    }
                }
        }
    }
}
