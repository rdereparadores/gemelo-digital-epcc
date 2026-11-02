// HUD de tercera persona: reloj y fecha del modelo con la velocidad, tiempo exterior, ubicación (edificio ·
// planta · local), ficha del local con sus datos en vivo, interacciones disponibles (las teclas las lee Control),
// minimapa y estado de la conexión con el servicio.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class HudTercera
    {
        public readonly VisualElement raiz;
        readonly VisualElement izq, tarjetaReloj, tarjetaUbicacion, tarjetaFicha, interacciones, der;
        readonly Label hora, fecha, velocidad, temp, cielo, edificio, local;
        readonly Icono iconoCielo;
        readonly FichaLocal ficha;
        readonly Minimapa mapa;
        readonly Label conexion, personas;
        readonly VisualElement punto;
        float proximo;
        bool dirtyFicha = true;

        public HudTercera(VisualElement padre)
        {
            raiz = El(padre, "capa");
            raiz.pickingMode = PickingMode.Ignore;
            Mostrar(raiz, false);

            // ─── Columna izquierda
            izq = El(raiz, "columna");
            izq.pickingMode = PickingMode.Ignore;
            izq.style.position = Position.Absolute;
            izq.style.left = 24; izq.style.top = 22;
            izq.style.width = 330;

            tarjetaReloj = Panel(izq, "panel--compacto");
            Animable(tarjetaReloj, "anim--oculto-izq");
            var fr = El(tarjetaReloj, "hud-reloj");
            hora = Txt(fr, "--:--", "hud-hora");
            velocidad = Txt(fr, "", "velocidad");
            El(fr, "espacio");
            iconoCielo = Ico(fr, "sol", 26);
            iconoCielo.style.marginRight = 8;
            iconoCielo.style.color = new Color(1, 1, 1, 0.85f);
            var ct = El(fr, "columna");
            temp = Txt(ct, "—", "t-l", "f-semi");
            temp.style.unityTextAlign = TextAnchor.MiddleRight;
            cielo = Txt(ct, "", "t-xs", "tenue");
            cielo.style.unityTextAlign = TextAnchor.MiddleRight;
            fecha = Txt(tarjetaReloj, "", "t-s", "texto-2");

            tarjetaUbicacion = Panel(izq, "panel--compacto");
            tarjetaUbicacion.style.marginTop = 12;
            Animable(tarjetaUbicacion, "anim--oculto-izq");
            edificio = Txt(tarjetaUbicacion, "", "ubicacion__edificio");
            edificio.style.whiteSpace = WhiteSpace.Normal;
            local = Txt(tarjetaUbicacion, "", "ubicacion__local");
            local.style.whiteSpace = WhiteSpace.Normal;

            tarjetaFicha = Panel(izq, "panel--compacto");
            tarjetaFicha.style.marginTop = 12;
            Animable(tarjetaFicha, "anim--oculto-izq");
            ficha = new FichaLocal(tarjetaFicha, false);

            // ─── Derecha: minimapa y estado
            der = El(raiz, "columna");
            der.pickingMode = PickingMode.Ignore;
            der.style.position = Position.Absolute;
            der.style.right = 24; der.style.top = 22;
            der.style.alignItems = Align.FlexEnd;
            mapa = new Minimapa();
            der.Add(mapa);
            var pie = Panel(der, "panel--compacto");
            pie.style.marginTop = 10;
            pie.style.width = 220;
            pie.style.paddingTop = 8; pie.style.paddingBottom = 8;
            var leyenda = El(pie, "fila");
            leyenda.style.flexWrap = Wrap.Wrap;
            foreach (var g in new[] { Formato.GrupoUso.Docencia, Formato.GrupoUso.Laboratorio, Formato.GrupoUso.Despachos, Formato.GrupoUso.Estudiantes })
            {
                var it = El(leyenda, "fila");
                it.style.width = Length.Percent(50);
                it.style.marginBottom = 3;
                var m = El(it);
                m.style.width = 8; m.style.height = 8; m.style.marginRight = 4;
                m.style.backgroundColor = Formato.ColorGrupo(g);
                m.style.borderTopLeftRadius = 2; m.style.borderTopRightRadius = 2; m.style.borderBottomLeftRadius = 2; m.style.borderBottomRightRadius = 2;
                Txt(it, Formato.NombreGrupo(g), "t-xs", "texto-2");
            }
            var est = El(pie, "fila");
            est.style.marginTop = 6;
            punto = El(est, "punto");
            conexion = Txt(est, "", "t-xs", "f-semi");
            personas = Txt(est, "", "t-xs", "tenue");
            personas.style.marginLeft = 8;
            SinPicking(der);

            // ─── Interacciones, abajo en el centro
            // (columna centrada en pantalla cuyas filas se estiran a la más ancha: quedan alineadas y del mismo ancho)
            var zonaAcciones = El(raiz, "columna");
            zonaAcciones.pickingMode = PickingMode.Ignore;
            zonaAcciones.style.position = Position.Absolute;
            zonaAcciones.style.left = 0; zonaAcciones.style.right = 0; zonaAcciones.style.bottom = 36;
            zonaAcciones.style.alignItems = Align.Center;
            interacciones = El(zonaAcciones, "columna");
            interacciones.pickingMode = PickingMode.Ignore;
            interacciones.style.alignItems = Align.Stretch;

            var app = GemeloApp.Instancia;
            app.InteraccionesCambiadas += PintarInteracciones;
            app.PausaCambiada += pausa => Mostrar(interacciones, !pausa);   // no asoman bajo el menú de pausa
            app.LocalActualCambiado += _ => dirtyFicha = true;
            app.Cliente.EstadoActualizado += _ => dirtyFicha = true;
        }

        public void Activar(bool si)
        {
            if (si)
            {
                Mostrar(raiz, true);
                Ver(tarjetaReloj, true);
                Ver(tarjetaUbicacion, true);
                dirtyFicha = true;
                PintarInteracciones(GemeloApp.Instancia.Interacciones);
            }
            else
            {
                Ver(tarjetaReloj, false);
                Ver(tarjetaUbicacion, false);
                Ver(tarjetaFicha, false);
                raiz.schedule.Execute(() => { if (!Visible(tarjetaReloj)) Mostrar(raiz, false); }).StartingIn(320);
            }
        }

        void PintarInteracciones(IReadOnlyList<Interaccion> lista)
        {
            interacciones.Clear();
            if (lista == null) return;
            for (int i = 0; i < lista.Count && i < 9; i++)
            {
                var it = lista[i];
                var fila = El(interacciones, "interaccion");
                var tecla = Txt(fila, i == 0 ? "E" : (i + 1).ToString(), "tecla");
                if (i == 0) tecla.AddToClassList("tecla--principal");
                if (!string.IsNullOrEmpty(it.icono) && Icono.Trazos.ContainsKey(IconoDe(it.icono))) fila.Add(new Icono(IconoDe(it.icono), 18));
                Txt(fila, it.texto, "interaccion__texto");
                SinPicking(fila);
                fila.style.opacity = 0;
                fila.style.translate = new Translate(0, 10);
                int k = i;
                fila.schedule.Execute(() => { fila.style.opacity = 1; fila.style.translate = new Translate(0, 0); }).StartingIn(20 + 40 * k);
                fila.style.transitionProperty = new List<StylePropertyName> { "opacity", "translate" };
                fila.style.transitionDuration = new List<TimeValue> { new(0.25f), new(0.25f) };
            }
        }

        static string IconoDe(string i) => i switch
        {
            "calefaccion" => "radiador",
            "alarma" => "fuego",
            "luz" or "luces" => "luz",
            "sentarse" => "persona",
            _ => i,
        };

        public void Actualizar()
        {
            var app = GemeloApp.Instancia;
            // Minimapa: cada fotograma (solo transformaciones)
            var cam = Camera.main;
            var jug = app.Jugador;
            Vector3 pos = jug != null ? jug.position : cam != null ? cam.transform.position : Vector3.zero;
            float yawCam = cam != null ? cam.transform.eulerAngles.y : 0;
            float yawJug = jug != null ? jug.eulerAngles.y : yawCam;
            mapa.Actualizar(pos, yawCam, yawJug, app.LocalActual);

            if (Time.unscaledTime < proximo && !dirtyFicha) return;
            proximo = Time.unscaledTime + 0.25f;

            var t = HoraCampus.Local;
            Texto(hora, Formato.Hora(t));
            Texto(fecha, Formato.Mayus(Formato.Fecha(t)));
            float esc = HoraCampus.Escala;
            Texto(velocidad, HoraCampus.EnDirecto ? "EN DIRECTO" : esc <= 0 ? "PAUSA" : Formato.Escala(esc));
            Tiempo(app.Cliente.Estado, iconoCielo, temp, cielo);

            var cli = app.Cliente;
            Clase(punto, "punto--ok", cli.Conectado);
            Clase(punto, "punto--mal", !cli.Conectado);
            Texto(conexion, cli.Conectado ? "En línea" : "Sin servicio");
            Texto(personas, app.PersonasEnEscena > 0 ? $"{Formato.Num(app.PersonasEnEscena)} personas a la vista" : "");

            // Ubicación
            var l = app.LocalActual;
            if (l != null)
            {
                Texto(edificio, $"{l.edificio.codigo} · {Formato.NombreEdificio(l.edificio).ToUpperInvariant()} · {Formato.Planta(l.planta?.nombre).ToUpperInvariant()}");
                Texto(local, Formato.NombreLocal(l));
            }
            else
            {
                var e = RegistroCampus.EdificioDentro(pos);
                Texto(edificio, e != null ? $"{e.codigo} · {Formato.NombreEdificio(e).ToUpperInvariant()}" : "CAMPUS DE CÁCERES");
                Texto(local, e != null ? "Zonas comunes" : "Exteriores de la Escuela Politécnica");
            }

            if (dirtyFicha)
            {
                dirtyFicha = false;
                bool ver = l != null;
                if (ver)
                {
                    cli.Estancia(l.clave, out var est);
                    ficha.Poner(l, est);
                }
                Ver(tarjetaFicha, ver);
            }
        }

        /// <summary>Tiempo exterior: temperatura e icono según la nubosidad, la lluvia y si es de día.</summary>
        public static void Tiempo(EstadoCampus e, Icono icono, Label temp, Label cielo)
        {
            var w = e?.weather;
            if (w == null) { Texto(temp, "—"); Texto(cielo, ""); return; }
            float t = float.NaN;
            var m = w.metrics?.Find(x => x.property == "air_temperature");
            if (m != null) t = m.value;
            Texto(temp, float.IsNaN(t) ? "—" : Formato.Num(t, 1) + " °C");
            bool dia = w.sun_elevation > -2f;
            string ic, txt;
            if (w.precipitation_rate > 0.1f) { ic = "lluvia"; txt = w.precipitation_rate > 2f ? "Lluvia fuerte" : "Lluvia"; }
            else if (w.cloud_cover > 0.75f) { ic = "nube"; txt = "Nublado"; }
            else if (w.cloud_cover > 0.3f) { ic = dia ? "sol-nube" : "luna-nube"; txt = "Poco nuboso"; }
            else { ic = dia ? "sol" : "luna"; txt = dia ? "Despejado" : "Noche despejada"; }
            if (icono != null) icono.Nombre = ic;
            Texto(cielo, txt);
        }
    }
}
