// Ficha de la selección en el centro de control. Sin selección: resumen del campus por edificios. Edificio:
// métricas agregadas, ocupación por planta, corte por plantas, gráfica histórica y alertas. Local: datos en vivo,
// métricas con su fuente (real o simulada) y gráficas históricas de las últimas horas.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class FichaSeleccion
    {
        readonly VisualElement panel, cuerpo;
        readonly Label titulo, subtitulo, codigo;
        readonly VisualElement botonVolver, botonCerrar;

        // Vistas
        readonly VisualElement vistaCampus, vistaEdificio, vistaLocal, edificioAbajo, localAbajo;
        readonly VisualElement barrasCampus;
        // Edificio
        readonly Label edPersonas, edTemp, edCO2, edPotencia;
        readonly VisualElement plantas, alertasEd;
        readonly VisualElement corteCont;
        readonly List<InfoPlanta> plantasCorte = new();
        // Local
        readonly FichaLocal fichaLocal;
        readonly VisualElement sensores, fuenteLocal;
        readonly Label fuenteLocalTxt;
        // Gráfica (compartida)
        readonly VisualElement chipsProp, chipsHoras;
        readonly Grafica grafica;
        readonly Label graficaInfo;
        readonly VisualElement bloqueGrafica;

        Seleccion sel;
        string propiedad = "occupancy";
        float horas = 6;
        float proximaGrafica;

        static readonly (string p, string nombre)[] PropsLocal =
            { ("air_temperature", "Temperatura"), ("co2_concentration", "CO₂"), ("occupancy", "Ocupación"), ("relative_humidity", "Humedad"),
              ("sound_level", "Ruido"), ("illuminance", "Luz"), ("active_power", "Potencia") };
        static readonly (string p, string nombre)[] PropsEdificio =
            { ("occupancy", "Ocupación"), ("air_temperature", "Temperatura"), ("co2_concentration", "CO₂"), ("active_power", "Potencia") };

        public static Color ColorPropiedad(string p) => p switch
        {
            "occupancy" => Escalas.Amarillo,
            "air_temperature" => new Color32(0xF2, 0x92, 0x4A, 255),
            "co2_concentration" => new Color32(0x00, 0xB1, 0x89, 255),
            "relative_humidity" => new Color32(0x6F, 0xA8, 0xF5, 255),
            "active_power" => new Color32(0x4F, 0xC3, 0xD9, 255),
            "sound_level" => new Color32(0xD9, 0x8C, 0xB3, 255),
            "illuminance" => new Color32(0xFF, 0xE0, 0x8A, 255),
            _ => Color.white,
        };

        public FichaSeleccion(VisualElement padre)
        {
            panel = Panel(padre, "panel--compacto");
            panel.style.flexGrow = 1;
            panel.style.flexShrink = 1;
            panel.style.paddingLeft = 16; panel.style.paddingRight = 16;

            var cab = El(panel, "fila");
            cab.style.alignItems = Align.FlexStart;
            botonVolver = Boton(cab, null, Volver, "flecha-izq", "boton--icono", "boton--fantasma");
            botonVolver.style.marginRight = 6;
            botonVolver.tooltip = "Volver al edificio";
            codigo = Txt(cab, "", "edificio-item__codigo");
            var col = El(cab, "columna", "crece");
            subtitulo = Txt(col, "", "t-xs", "tenue", "f-semi", "mayus");
            titulo = Txt(col, "", "t-l", "f-semi");
            titulo.style.whiteSpace = WhiteSpace.Normal;
            botonCerrar = Boton(cab, null, () => GemeloApp.Instancia?.Seleccionar(Seleccion.Ninguna), "cerrar", "boton--icono", "boton--fantasma");
            botonCerrar.style.marginLeft = 4;

            var sv = Desplazable(panel);
            sv.style.marginTop = 10;
            cuerpo = sv.contentContainer;

            // ─── Campus
            vistaCampus = El(cuerpo, "columna");
            var hint = Txt(vistaCampus, "Haz clic en un edificio o en una estancia del mapa para ver sus datos, sus gráficas y lanzar eventos.", "t-s", "texto-2");
            hint.style.whiteSpace = WhiteSpace.Normal;
            Txt(vistaCampus, "OCUPACIÓN POR EDIFICIO", "titulo-panel").style.marginTop = 14;
            barrasCampus = El(vistaCampus, "columna");

            // ─── Edificio
            vistaEdificio = El(cuerpo, "columna");
            var kp = El(vistaEdificio, "metricas");
            edPersonas = MiniKpi(kp, "personas", "Personas");
            edTemp = MiniKpi(kp, "termometro", "Temperatura media");
            edCO2 = MiniKpi(kp, "viento", "CO₂ medio");
            edPotencia = MiniKpi(kp, "rayo", "Potencia");

            // ─── Local
            vistaLocal = El(cuerpo, "columna");
            var filaFuente = El(vistaLocal, "fila");
            filaFuente.style.marginBottom = 8;
            fuenteLocal = El(filaFuente, "insignia");
            fuenteLocal.Add(new Icono("sensor", 12) { style = { marginRight = 5 } });
            fuenteLocalTxt = Txt(fuenteLocal, "");
            fichaLocal = new FichaLocal(vistaLocal, false);

            // ─── Gráfica histórica (edificio o local)
            bloqueGrafica = El(cuerpo, "columna");
            var cg = El(bloqueGrafica, "fila");
            cg.style.marginTop = 14;
            Txt(cg, "HISTÓRICO", "titulo-panel");
            El(cg, "espacio");
            chipsHoras = El(cg, "fila");
            chipsHoras.style.marginBottom = 6;
            foreach (var h in new[] { 2f, 6f, 24f })
            {
                float hh = h;
                var c = Txt(chipsHoras, $"{h:0} h", "segmento");
                c.userData = h;
                Clic(c, () => { horas = hh; PintarChips(); PedirGrafica(); });
            }
            chipsProp = El(bloqueGrafica, "fila");
            chipsProp.style.flexWrap = Wrap.Wrap;
            grafica = new Grafica();
            bloqueGrafica.Add(grafica);
            graficaInfo = Txt(bloqueGrafica, "", "t-xs", "tenue");
            graficaInfo.style.marginTop = 4;
            graficaInfo.style.whiteSpace = WhiteSpace.Normal;

            // Debajo de la gráfica: plantas y corte (edificio) o sensores (local); al final, alertas del edificio
            edificioAbajo = El(cuerpo, "columna");
            Txt(edificioAbajo, "OCUPACIÓN POR PLANTA", "titulo-panel").style.marginTop = 14;
            plantas = El(edificioAbajo, "columna");
            Txt(edificioAbajo, "CORTE POR PLANTAS", "titulo-panel").style.marginTop = 14;
            corteCont = El(edificioAbajo, "fila");
            localAbajo = El(cuerpo, "columna");
            Txt(localAbajo, "SENSORES Y FUENTES", "titulo-panel").style.marginTop = 14;
            sensores = El(localAbajo, "columna");
            alertasEd = El(cuerpo, "columna");
            cuerpo.style.paddingRight = 10;

            Poner(Seleccion.Ninguna);
        }

        static Label MiniKpi(VisualElement padre, string icono, string nombre)
        {
            var m = El(padre, "metrica");
            m.Add(new Icono(icono, 20));
            var col = El(m, "columna");
            var v = Txt(col, "—", "metrica__valor");
            Txt(col, nombre, "metrica__nombre");
            return v;
        }

        void Volver()
        {
            if (sel.local != null) GemeloApp.Instancia?.Seleccionar(new Seleccion(sel.edificio, null));
        }

        public void Poner(Seleccion s)
        {
            sel = s;
            bool campus = s.Vacia, ed = !s.Vacia && s.local == null, lo = s.local != null;
            Mostrar(vistaCampus, campus);
            Mostrar(vistaEdificio, ed);
            Mostrar(edificioAbajo, ed);
            Mostrar(vistaLocal, lo);
            Mostrar(localAbajo, lo);
            Mostrar(bloqueGrafica, !campus);
            Mostrar(botonVolver, lo);
            Mostrar(botonCerrar, !campus);
            Mostrar(codigo, !campus);
            alertasEd.Clear();

            if (campus)
            {
                Texto(subtitulo, "CAMPUS DE CÁCERES");
                Texto(titulo, "Escuela Politécnica");
            }
            else if (ed)
            {
                var e = s.edificio;
                Texto(codigo, e.codigo);
                Texto(subtitulo, $"EDIFICIO · {e.plantas.Count} PLANTAS · {e.locales.Count} LOCALES");
                Texto(titulo, Formato.NombreEdificio(e));
                // Corte por plantas: Todo + cada planta
                plantasCorte.Clear();
                var opciones = new List<string> { "Todo" };
                foreach (var p in e.plantas) { plantasCorte.Add(p); opciones.Add(Formato.PlantaCorta(p.nombre)); }
                var nuevo = new Segmentado(opciones);
                corteCont.Clear();
                corteCont.Add(nuevo);
                CambiarCorte(nuevo, e);
                if (Array.FindIndex(PropsEdificio, x => x.p == propiedad) < 0) propiedad = "occupancy";
            }
            else
            {
                var l = s.local;
                Texto(codigo, l.edificio.codigo);
                Texto(subtitulo, $"{Formato.Uso(l.local.uso).ToUpperInvariant()} · {Formato.Planta(l.planta?.nombre).ToUpperInvariant()}");
                Texto(titulo, Formato.NombreLocal(l));
                if (Array.FindIndex(PropsLocal, x => x.p == propiedad) < 0) propiedad = "air_temperature";
            }
            PintarChipsPropiedades();
            PintarChips();
            AlEstado();
            if (!campus) { grafica.Vacia("Cargando…"); PedirGrafica(); }
        }

        Segmentado corteActual;

        void CambiarCorte(Segmentado s, InfoEdificio e)
        {
            corteActual = s;
            s.Elegido += i => GemeloApp.Instancia?.CortarEdificio(e, i == 0 ? -1 : plantasCorte[i - 1].indice);
            int c = GemeloApp.Instancia.Corte(e);
            s.Activo = c < 0 ? 0 : plantasCorte.FindIndex(p => p.indice == c) + 1;
        }

        void PintarChipsPropiedades()
        {
            chipsProp.Clear();
            var lista = sel.local != null ? PropsLocal : PropsEdificio;
            EstadoEstancia est = null;
            if (sel.local != null) ClienteGemelo.Instancia.Estancia(sel.local.clave, out est);
            foreach (var (p, nombre) in lista)
            {
                // En los locales solo las propiedades que miden sus sensores
                if (est != null && p != "occupancy" && !est.Metrica(p, out _)) continue;
                var pp = p;
                var c = El(chipsProp, "chip");
                var punto = El(c);
                punto.style.width = 8; punto.style.height = 8; punto.style.marginRight = 6;
                punto.style.backgroundColor = ColorPropiedad(p);
                punto.style.borderTopLeftRadius = 4; punto.style.borderTopRightRadius = 4; punto.style.borderBottomLeftRadius = 4; punto.style.borderBottomRightRadius = 4;
                Txt(c, nombre);
                Clase(c, "chip--activo", p == propiedad);
                c.userData = p;
                Clic(c, () => { propiedad = pp; PintarChips(); PedirGrafica(); });
            }
        }

        void PintarChips()
        {
            foreach (var c in chipsProp.Children()) Clase(c, "chip--activo", (string)c.userData == propiedad);
            foreach (var c in chipsHoras.Children()) Clase(c, "segmento--activo", Mathf.Approximately((float)c.userData, horas));
        }

        void PedirGrafica()
        {
            proximaGrafica = Time.unscaledTime + (HoraCampus.Escala > 30 ? 4f : 20f);
            if (sel.Vacia) return;
            bool ed = sel.local == null;
            string id = ed ? sel.edificio.idServicio : sel.local.idServicio;
            if (string.IsNullOrEmpty(id)) { grafica.Vacia("Este local no está en el servicio de sensores"); graficaInfo.text = ""; return; }
            var pedida = sel;
            string prop = propiedad;
            Historial.Pedir(id, ed, prop, horas, r =>
            {
                if (pedida.edificio != sel.edificio || pedida.local != sel.local || prop != propiedad) return;
                if (r == null) { grafica.Vacia("No se ha podido leer el histórico"); return; }
                Formato.LeerFecha(r.from, out var desde);
                Formato.LeerFecha(r.to, out var hasta);
                var serie = r.Serie(prop);
                grafica.Poner(serie, desde, hasta, ColorPropiedad(prop));
                string fuente = serie == null ? "" : serie.source switch
                {
                    "real" => "Datos reales de SmartPoliTech", "simulated" => "Datos simulados", "mixed" => "Datos reales y simulados", _ => "",
                };
                string agg = ed
                    ? serie?.aggregation switch { "sum" => "suma de las estancias", "max" => "máximo de las estancias", _ => "media de las estancias" }
                    : prop == "occupancy" ? "personas en la estancia"
                    : serie?.aggregation switch { "sum" => "suma de sus sensores", "max" => "máximo de sus sensores", _ => "media de sus sensores" };
                bool rec = !string.IsNullOrEmpty(r.reconstructed_until);
                graficaInfo.text = serie == null ? "" : $"{fuente} · {agg} · cada {Formato.Num(r.step_s / 60f)} min" + (rec ? " · tramo inicial reconstruido" : "");
            });
        }

        /// <summary>Cada 0,2 s desde CentroControl: refresco periódico de la gráfica.</summary>
        public void Actualizar()
        {
            if (!sel.Vacia && Time.unscaledTime > proximaGrafica) PedirGrafica();
            if (corteActual != null && sel.edificio != null && sel.local == null)
            {
                int c = GemeloApp.Instancia.Corte(sel.edificio);
                int k = c < 0 ? 0 : plantasCorte.FindIndex(p => p.indice == c) + 1;
                if (corteActual.Activo != k) corteActual.Activo = k;
            }
        }

        /// <summary>Con cada estado nuevo: valores en vivo.</summary>
        public void AlEstado()
        {
            var cli = ClienteGemelo.Instancia;
            if (cli == null) return;
            if (sel.Vacia) { Campus(cli); return; }
            if (sel.local == null) { Edificio(cli, sel.edificio); return; }
            cli.Estancia(sel.local.clave, out var est);
            fichaLocal.Poner(sel.local, est);
            Sensores(est);
        }

        void Campus(ClienteGemelo cli)
        {
            barrasCampus.Clear();
            if (cli.Estado == null) return;
            var ordenados = new List<InfoEdificio>(RegistroCampus.Edificios);
            ordenados.Sort((x, y) => string.CompareOrdinal(x.codigo, y.codigo));
            foreach (var e in ordenados)
            {
                if (!cli.Edificio(e.codigo, out var b)) continue;
                float oc = 0, cap = 0;
                var m = b.metrics?.Find(x => x.property == "occupancy");
                if (m != null) oc = m.value;
                foreach (var l in e.locales) cap += Formato.Grupo(l.local.uso) is Formato.GrupoUso.Docencia or Formato.GrupoUso.Laboratorio ? FichaLocal.Capacidad(l) : 0;
                Barra(barrasCampus, e.codigo, oc, Mathf.Max(1, cap), () => GemeloApp.Instancia?.Seleccionar(new Seleccion(e, null)));
            }
        }

        void Edificio(ClienteGemelo cli, InfoEdificio e)
        {
            cli.Edificio(e.codigo, out var b);
            float V(string p) { var m = b?.metrics?.Find(x => x.property == p); return m != null ? m.value : float.NaN; }
            Texto(edPersonas, Formato.Num(V("occupancy")));
            Texto(edTemp, Formato.Valor("air_temperature", V("air_temperature")));
            Texto(edCO2, Formato.Valor("co2_concentration", V("co2_concentration")));
            Texto(edPotencia, Formato.Potencia(V("active_power")));

            plantas.Clear();
            for (int i = e.plantas.Count - 1; i >= 0; i--)
            {
                var p = e.plantas[i];
                float oc = 0, cap = 0;
                foreach (var l in p.locales)
                {
                    if (cli.Estancia(l.clave, out var est)) { float v = est.Valor("occupancy"); if (!float.IsNaN(v)) oc += v; }
                    var g = Formato.Grupo(l.local.uso);
                    if (g is Formato.GrupoUso.Docencia or Formato.GrupoUso.Laboratorio or Formato.GrupoUso.Despachos or Formato.GrupoUso.Estudiantes)
                        cap += FichaLocal.Capacidad(l);
                }
                var pp = p;
                Barra(plantas, Formato.PlantaCorta(p.nombre), oc, Mathf.Max(1, cap), () => GemeloApp.Instancia?.CortarEdificio(e, pp.indice));
            }

            alertasEd.Clear();
            if (b?.alerts != null && b.alerts.Count > 0)
            {
                Txt(alertasEd, "ALERTAS", "titulo-panel").style.marginTop = 14;
                FichaLocal.PintarAlertas(alertasEd, b.alerts, 8, a =>
                {
                    var il = RegistroCampus.PorEspacio(a.space_id);
                    if (il != null) GemeloApp.Instancia?.Seleccionar(new Seleccion(il.edificio, il));
                });
            }
        }

        static void Barra(VisualElement padre, string nombre, float valor, float total, Action alPulsar)
        {
            var f = El(padre, "planta-barra");
            Txt(f, nombre, "planta-barra__nombre");
            var b = El(f, "barra", "crece");
            var r = El(b, "barra__relleno");
            float frac = Mathf.Clamp01(valor / total);
            r.style.width = Length.Percent(frac * 100);
            r.style.backgroundColor = frac > 0.85f ? Escalas.Naranja : Escalas.Amarillo;
            Txt(f, $"{Formato.Num(valor)} / {Formato.Num(total)}", "planta-barra__valor");
            if (alPulsar != null) { Clic(f, alPulsar); f.tooltip = "Clic para ver solo hasta esta planta"; }
        }

        void Sensores(EstadoEstancia est)
        {
            sensores.Clear();
            bool real = false, sim = false;
            if (est?.metrics == null || est.metrics.Count == 0)
            {
                Txt(sensores, "Sin sensores en el servicio", "t-s", "tenue");
            }
            else
            {
                foreach (var m in est.metrics)
                {
                    if (m.property is "window_open" or "door_open" or "valve_position" or "target_temperature" or "lights_on" or "blind_position") continue;
                    var f = El(sensores, "fila");
                    f.style.paddingTop = 3; f.style.paddingBottom = 3;
                    f.style.borderBottomWidth = 1;
                    f.style.borderBottomColor = new Color(1, 1, 1, 0.05f);
                    var n = Txt(f, Formato.Propiedad(m.property), "t-s", "crece");
                    n.style.color = new Color(0.8f, 0.8f, 0.8f);
                    Txt(f, Formato.Valor(m.property, m.value), "t-s", "f-semi").style.marginRight = 10;
                    var ins = El(f, "insignia", m.source == "real" ? "insignia--real" : m.source == "simulated" ? "insignia--sim" : "insignia--mixto");
                    Txt(ins, $"{Formato.Fuente(m.source)} · {m.sensor_count}");
                    ins.tooltip = $"{m.sensor_count} sensor(es)";
                    if (m.source == "real" || m.source == "mixed") real = true;
                    if (m.source == "simulated" || m.source == "mixed") sim = true;
                }
            }
            Clase(fuenteLocal, "insignia--real", real && !sim);
            Clase(fuenteLocal, "insignia--sim", sim && !real);
            Clase(fuenteLocal, "insignia--mixto", real && sim);
            Mostrar(fuenteLocal, real || sim);
            Texto(fuenteLocalTxt, real && sim ? "Datos reales y simulados" : real ? "Datos reales de SmartPoliTech" : "Datos simulados");
        }
    }
}
