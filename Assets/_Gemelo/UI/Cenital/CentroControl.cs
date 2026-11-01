// Centro de control (vista cenital): barra superior con el reloj, los controles del tiempo, el escenario y los
// KPIs del campus; a la izquierda, capas con su leyenda, edificios y el feed de alertas; a la derecha, la ficha de
// la selección (FichaSeleccion) y los eventos (PanelEventos).
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class CentroControl
    {
        public readonly VisualElement raiz;
        readonly VisualElement barra, izq, der;
        readonly Label hora, fecha, escenario, kpiPersonas, kpiTemp, kpiPotencia, kpiAlertas, kpiEscena, conexion;
        readonly VisualElement kpiAlertasCaja, puntoConexion, botonPausa, popover;
        readonly Icono iconoPausa, iconoTemp;
        readonly Segmentado velocidad;
        readonly Dictionary<Capa, VisualElement> botonesCapa = new();
        readonly VisualElement leyenda, listaEdificios, feed;
        readonly Label leyendaMin, leyendaMax, leyendaTitulo, feedCuenta;
        readonly VisualElement leyendaGrad, leyendaConfort;
        readonly Dictionary<string, (VisualElement item, Label datos, VisualElement alerta)> edificios = new();
        readonly FichaSeleccion ficha;
        readonly PanelEventos eventos;
        readonly Dictionary<Capa, Texture2D> gradientes = new();
        string firmaFeed;
        float proximo;

        static readonly float[] Velocidades = { 1, 60, 600, 3600 };
        static readonly Capa[] Capas = { Capa.Ninguna, Capa.Ocupacion, Capa.Temperatura, Capa.CO2, Capa.Confort, Capa.Humedad, Capa.Consumo, Capa.Ruido, Capa.Iluminacion };

        public CentroControl(VisualElement padre)
        {
            raiz = El(padre, "capa");
            raiz.pickingMode = PickingMode.Ignore;
            Mostrar(raiz, false);
            var app = GemeloApp.Instancia;

            // ───────── Barra superior
            barra = Panel(raiz, "barra-superior");
            Animable(barra, "anim--oculto-arriba");
            var sello = El(barra, "sello");
            sello.style.width = 38; sello.style.height = 38; sello.style.marginRight = 12;
            sello.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("GemeloUI/Marca/sello"));
            var tit = El(barra, "columna");
            Txt(tit, "CENTRO DE CONTROL", "t-xs", "f-negrita", "c-amarillo", "mayus");
            Txt(tit, "Escuela Politécnica", "t-m", "f-serif-negrita");
            El(barra, "sep-v");

            var reloj = El(barra, "columna");
            reloj.style.minWidth = 200;
            hora = Txt(reloj, "--:--", "t-xl", "f-semi");
            fecha = Txt(reloj, "", "t-xs", "tenue");
            botonPausa = Boton(barra, null, AlternarPausa, null, "boton--icono");
            iconoPausa = new Icono("pausa", 16);
            botonPausa.Add(iconoPausa);
            botonPausa.style.marginRight = 8;
            velocidad = new Segmentado(new[] { "1×", "60×", "600×", "3600×" });
            velocidad.Elegido += i => ClienteGemelo.Instancia?.CambiarEscalaTiempo(Velocidades[i]);
            barra.Add(velocidad);
            var bEsc = El(barra, "boton", "boton--mini");
            bEsc.style.marginLeft = 10;
            bEsc.style.height = 34;
            bEsc.Add(new Icono("escenario", 15) { style = { marginRight = 6 } });
            escenario = Txt(bEsc, "Escenario");
            bEsc.Add(new Icono("flecha-abajo", 14) { style = { marginLeft = 6, marginRight = 0 } });
            Clic(bEsc, () => AbrirEscenarios(bEsc));

            El(barra, "espacio");
            kpiPersonas = Kpi(barra, "personas", "PERSONAS", out _);
            kpiTemp = Kpi(barra, "termometro", "EXTERIOR", out var ktCaja);
            iconoTemp = ktCaja.Q<Icono>();
            kpiPotencia = Kpi(barra, "rayo", "POTENCIA", out _);
            kpiAlertas = Kpi(barra, "alerta", "ALERTAS", out kpiAlertasCaja);
            kpiEscena = Kpi(barra, "persona", "EN ESCENA", out _);
            var con = El(barra, "fila");
            con.style.marginLeft = 14;
            puntoConexion = El(con, "punto");
            conexion = Txt(con, "", "t-xs", "tenue");

            // ───────── Lateral izquierdo
            izq = El(raiz, "lateral-izq", "columna");
            izq.pickingMode = PickingMode.Ignore;
            Animable(izq, "anim--oculto-izq");

            var pCapas = Panel(izq, "panel--compacto");
            Txt(pCapas, "CAPA DE DATOS", "titulo-panel");
            var rejilla = El(pCapas, "fila");
            rejilla.style.flexWrap = Wrap.Wrap;
            foreach (var c in Capas)
            {
                var cc = c;
                var b = El(rejilla, "capa-boton");
                b.style.width = Length.Percent(50);
                b.Add(new Icono(IconoCapa(c), 18));
                Txt(b, c == Capa.Ninguna ? "Ninguna" : c == Capa.Consumo ? "Consumo" : Escalas.Nombre(c), "t-s");
                Clic(b, () => GemeloApp.Instancia?.CambiarCapa(cc));
                botonesCapa[c] = b;
            }
            leyenda = El(pCapas, "columna");
            leyenda.style.marginTop = 8;
            leyendaTitulo = Txt(leyenda, "", "t-xs", "tenue");
            leyendaGrad = El(leyenda, "leyenda__gradiente");
            leyendaConfort = El(leyenda, "fila");
            leyendaConfort.style.flexWrap = Wrap.Wrap;
            leyendaConfort.style.marginTop = 6;
            foreach (var k in new[] { "ok", "cold", "hot", "stuffy", "humid", "dry" })
            {
                var it = El(leyendaConfort, "fila");
                it.style.width = Length.Percent(33);
                it.style.marginBottom = 4;
                var m = El(it);
                m.style.width = 10; m.style.height = 10; m.style.marginRight = 5;
                m.style.backgroundColor = Escalas.ColorConfort(Escalas.ConfortIndice(k));
                m.style.borderTopLeftRadius = 3; m.style.borderTopRightRadius = 3; m.style.borderBottomLeftRadius = 3; m.style.borderBottomRightRadius = 3;
                Txt(it, Escalas.ConfortTexto(k), "t-xs", "texto-2");
            }
            var mm = El(leyenda, "fila");
            leyendaMin = Txt(mm, "", "t-xs", "texto-2");
            El(mm, "espacio");
            leyendaMax = Txt(mm, "", "t-xs", "texto-2");

            var pEd = Panel(izq, "panel--compacto");
            pEd.style.marginTop = 12;
            Txt(pEd, "EDIFICIOS", "titulo-panel");
            listaEdificios = El(pEd, "columna");
            var ordenados = new List<InfoEdificio>(RegistroCampus.Edificios);
            ordenados.Sort((a, b) => string.CompareOrdinal(a.codigo, b.codigo));
            foreach (var e in ordenados)
            {
                var ee = e;
                var it = El(listaEdificios, "edificio-item");
                Txt(it, e.codigo, "edificio-item__codigo");
                var col = El(it, "columna", "crece");
                var n = Txt(col, Formato.NombreEdificio(e), "t-s", "f-semi");
                n.style.overflow = Overflow.Hidden;
                n.style.textOverflow = TextOverflow.Ellipsis;
                var d = Txt(col, "", "t-xs", "tenue");
                var al = El(it, "punto");
                al.style.marginRight = 0;
                al.style.marginLeft = 8;
                al.style.flexShrink = 0;
                Clic(it, () => GemeloApp.Instancia?.Seleccionar(new Seleccion(ee, null)));
                edificios[e.codigo] = (it, d, al);
            }

            var pAl = Panel(izq, "panel--compacto");
            pAl.style.marginTop = 12;
            pAl.style.flexGrow = 1;
            pAl.style.flexShrink = 1;
            var cabAl = El(pAl, "fila");
            Txt(cabAl, "ALERTAS DEL CAMPUS", "titulo-panel");
            El(cabAl, "espacio");
            feedCuenta = Txt(cabAl, "", "t-xs", "tenue");
            feedCuenta.style.marginBottom = 8;
            var sv = Desplazable(pAl);
            feed = sv.contentContainer;

            // ───────── Lateral derecho
            der = El(raiz, "lateral-der", "columna");
            der.pickingMode = PickingMode.Ignore;
            Animable(der, "anim--oculto-der");
            ficha = new FichaSeleccion(der);
            eventos = new PanelEventos(der);

            // ───────── Popover de escenarios
            popover = El(raiz, "popover");
            Mostrar(popover, false);

            app.CapaCambiada += _ => PintarCapa();
            app.SeleccionCambiada += s => { ficha.Poner(s); eventos.Objetivo(s); PintarEdificios(); };
            app.Cliente.EstadoActualizado += _ => { if (raiz.style.display == DisplayStyle.Flex) AlEstado(); };
            PintarCapa();
        }

        static string IconoCapa(Capa c) => c switch
        {
            Capa.Ocupacion => "personas", Capa.Temperatura => "termometro", Capa.CO2 => "viento", Capa.Confort => "confort",
            Capa.Humedad => "gota", Capa.Consumo => "rayo", Capa.Ruido => "ruido", Capa.Iluminacion => "luz", _ => "ninguna",
        };

        static Label Kpi(VisualElement padre, string icono, string nombre, out VisualElement caja)
        {
            caja = El(padre, "kpi");
            caja.Add(new Icono(icono, 22));
            var col = El(caja, "columna");
            var v = Txt(col, "—", "kpi__valor");
            Txt(col, nombre, "kpi__nombre");
            return v;
        }

        public void Activar(bool si)
        {
            if (si)
            {
                Mostrar(raiz, true);
                Ver(barra, true); Ver(izq, true); Ver(der, true);
                var app = GemeloApp.Instancia;
                ficha.Poner(app.Seleccion);
                eventos.Objetivo(app.Seleccion);
                PintarCapa();
                AlEstado();
            }
            else
            {
                CerrarEmergente();
                Ver(barra, false); Ver(izq, false); Ver(der, false);
                raiz.schedule.Execute(() => { if (!Visible(barra)) Mostrar(raiz, false); }).StartingIn(320);
            }
        }

        public bool CerrarEmergente()
        {
            if (popover.style.display != DisplayStyle.Flex) return false;
            Mostrar(popover, false);
            return true;
        }

        void AlternarPausa()
        {
            var sim = ClienteGemelo.Instancia?.Estado?.simulation;
            if (sim == null) return;
            ClienteGemelo.Instancia.PausarSimulacion(sim.running);
        }

        void AbrirEscenarios(VisualElement ancla)
        {
            if (CerrarEmergente()) return;
            ClienteGemelo.Instancia?.Escenarios(lista =>
            {
                if (lista == null) return;
                popover.Clear();
                foreach (var e in lista)
                {
                    var id = e.id;
                    var it = Txt(popover, Formato.Escenario(e.id, e.name), "popover__item");
                    it.tooltip = e.description;
                    if (e.id == HoraCampus.Escenario) it.AddToClassList("popover__item--activo");
                    Clic(it, () =>
                    {
                        Mostrar(popover, false);
                        ClienteGemelo.Instancia?.CambiarEscenario(id, ok =>
                            GemeloApp.Instancia?.Avisar(ok ? "Escenario: " + Formato.Escenario(id) : "El servicio no ha aceptado el escenario",
                                ok ? NivelAviso.Exito : NivelAviso.Aviso));
                    });
                }
                var r = ancla.worldBound;
                var origen = raiz.worldBound;
                popover.style.left = r.xMin - origen.xMin;
                popover.style.top = r.yMax - origen.yMin + 8;
                Mostrar(popover, true);
            });
        }

        void PintarCapa()
        {
            var c = GemeloApp.Instancia.Capa;
            foreach (var kv in botonesCapa)
            {
                Clase(kv.Value, "capa-boton--activa", kv.Key == c);
                kv.Value.Q<Icono>()?.MarkDirtyRepaint();
            }
            bool hay = c != Capa.Ninguna;
            Mostrar(leyendaGrad, hay && c != Capa.Confort);
            Mostrar(leyendaMin.parent, hay && c != Capa.Confort);
            Mostrar(leyendaConfort, c == Capa.Confort);
            if (!hay) { leyendaTitulo.text = "Elige una capa para colorear las estancias"; return; }
            leyendaTitulo.text = c == Capa.Ocupacion ? "Ocupación respecto al aforo" : $"{Escalas.Nombre(c)} ({Escalas.Unidad(c)})";
            if (c == Capa.Confort) return;
            if (!gradientes.TryGetValue(c, out var tex)) gradientes[c] = tex = Gradiente(c);
            leyendaGrad.style.backgroundImage = new StyleBackground(tex);
            var r = Escalas.Rango(c);
            leyendaMin.text = c == Capa.Ocupacion ? "Vacía" : c == Capa.Consumo ? Formato.Potencia(r.x) : Formato.Num(r.x) + " " + Escalas.Unidad(c);
            leyendaMax.text = c == Capa.Ocupacion ? "Llena" : c == Capa.Consumo ? Formato.Potencia(r.y) : Formato.Num(r.y) + " " + Escalas.Unidad(c);
        }

        void PintarEdificios()
        {
            var sel = GemeloApp.Instancia.Seleccion.edificio;
            foreach (var kv in edificios) Clase(kv.Value.item, "edificio-item--sel", sel != null && kv.Key == sel.codigo);
        }

        public void Actualizar()
        {
            if (Time.unscaledTime < proximo) return;
            proximo = Time.unscaledTime + 0.2f;
            var t = HoraCampus.Local;
            Texto(hora, Formato.HoraSeg(t));
            Texto(fecha, Formato.Mayus(Formato.Fecha(t)));
            var cli = ClienteGemelo.Instancia;
            var sim = cli?.Estado?.simulation;
            bool enMarcha = sim == null || sim.running;
            if (iconoPausa.Nombre != (enMarcha ? "pausa" : "play")) iconoPausa.Nombre = enMarcha ? "pausa" : "play";
            botonPausa.tooltip = enMarcha ? "Pausar la simulación" : "Reanudar la simulación";
            int k = -1;
            float esc = HoraCampus.Escala;
            for (int i = 0; i < Velocidades.Length; i++) if (Mathf.Abs(Velocidades[i] - esc) < 0.01f * Velocidades[i]) k = i;
            if (velocidad.Activo != k) velocidad.Activo = k;
            Texto(escenario, Formato.Escenario(HoraCampus.Escenario));
            Texto(kpiEscena, Formato.Num(GemeloApp.Instancia.PersonasEnEscena));
            bool ok = cli != null && cli.Conectado;
            Clase(puntoConexion, "punto--ok", ok);
            Clase(puntoConexion, "punto--mal", !ok);
            Texto(conexion, ok ? (HoraCampus.EnDirecto ? "En directo" : "Simulación") : "Sin servicio");
            ficha.Actualizar();
            eventos.Actualizar();
        }

        void AlEstado()
        {
            var est = ClienteGemelo.Instancia?.Estado;
            if (est == null) return;
            float personas = 0, potencia = 0;
            int alertas = 0;
            var lista = new List<(Alerta a, EstadoEdificio b)>();
            foreach (var b in est.buildings)
            {
                float oc = Metrica(b, "occupancy"), pw = Metrica(b, "active_power"), tm = Metrica(b, "air_temperature");
                if (!float.IsNaN(oc)) personas += oc;
                if (!float.IsNaN(pw)) potencia += pw;
                int crit = 0, av = 0;
                if (b.alerts != null)
                    foreach (var a in b.alerts)
                    {
                        if (a.severity == "critical") crit++;
                        else if (a.severity == "warning") av++;
                        if (a.severity != "info") lista.Add((a, b));
                    }
                alertas += crit + av;
                if (edificios.TryGetValue(b.unity_id ?? "", out var it))
                {
                    Texto(it.datos, $"{Formato.Num(oc)} pers. · {Formato.Num(tm, 1)} °C · {Formato.Potencia(pw)}");
                    it.alerta.style.backgroundColor = crit > 0 ? Escalas.Mal : av > 0 ? Escalas.Naranja : new Color(1, 1, 1, 0.12f);
                    it.alerta.tooltip = crit + av > 0 ? $"{crit} críticas, {av} avisos" : "Sin alertas";
                }
            }
            Texto(kpiPersonas, Formato.Num(personas));
            Texto(kpiPotencia, Formato.Potencia(potencia));
            Texto(kpiAlertas, Formato.Num(alertas));
            Clase(kpiAlertasCaja, "kpi--alerta", alertas > 0);
            HudTercera.Tiempo(est, iconoTemp, kpiTemp, null);
            ficha.AlEstado();
            Feed(lista);
        }

        static float Metrica(EstadoEdificio b, string p)
        {
            var m = b.metrics?.Find(x => x.property == p);
            return m != null ? m.value : float.NaN;
        }

        void Feed(List<(Alerta a, EstadoEdificio b)> lista)
        {
            lista.Sort((x, y) => Peso(y.a).CompareTo(Peso(x.a)));
            var firma = new StringBuilder();
            int n = Mathf.Min(lista.Count, 40);
            for (int i = 0; i < n; i++) firma.Append(lista[i].a.code).Append(lista[i].a.space_id).Append(lista[i].a.message).Append('|');
            Texto(feedCuenta, lista.Count == 0 ? "" : $"{lista.Count}");
            if (firma.ToString() == firmaFeed) return;
            firmaFeed = firma.ToString();
            feed.Clear();
            if (lista.Count == 0)
            {
                var ok = El(feed, "fila");
                ok.style.marginTop = 4;
                ok.Add(new Icono("ok", 18) { Color = Escalas.Bien });
                Txt(ok, "Todo en orden en el campus", "t-s", "texto-2").style.marginLeft = 8;
                return;
            }
            for (int i = 0; i < n; i++)
            {
                var (a, b) = lista[i];
                var il = RegistroCampus.PorEspacio(a.space_id);
                var el = El(feed, "alerta", "alerta--clic", a.severity == "critical" ? "alerta--critico" : "alerta--aviso");
                el.Add(new Icono("alerta", 16));
                var col = El(el, "columna", "crece");
                Txt(col, a.message, "alerta__texto");
                var donde = Txt(col, il != null ? $"{il.edificio.codigo} {Formato.PlantaCorta(il.planta?.nombre)} · {Formato.NombreLocal(il)}" : b.name, "t-xs", "tenue");
                donde.style.overflow = Overflow.Hidden;
                donde.style.textOverflow = TextOverflow.Ellipsis;
                donde.style.whiteSpace = WhiteSpace.NoWrap;
                var eb = RegistroCampus.Edificio(b.unity_id) ?? RegistroCampus.Edificio(b.building_id);
                Clic(el, () => GemeloApp.Instancia?.Seleccionar(il != null ? new Seleccion(il.edificio, il) : new Seleccion(eb, null)));
            }
        }

        static int Peso(Alerta a) => a.severity == "critical" ? 2 : a.severity == "warning" ? 1 : 0;
    }
}
