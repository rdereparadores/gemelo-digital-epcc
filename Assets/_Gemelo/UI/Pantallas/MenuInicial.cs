// Menú inicial, sobre la cámara cinematográfica: imagotipo de la UEx y denominación del centro, los dos modos
// (explorar en tercera persona o centro de control cenital), el bloque de simulación, ajustes y salir.
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class MenuInicial
    {
        public readonly VisualElement raiz;
        readonly VisualElement lateral, tarjetaSim;
        readonly PanelSimulacion sim;
        readonly Label conexion, url, personas;
        readonly VisualElement punto;

        public MenuInicial(GemeloUI ui, VisualElement padre)
        {
            raiz = El(padre, "capa");
            raiz.pickingMode = PickingMode.Ignore;

            // Velo suave para que el texto se lea sobre cualquier plano de la cámara
            var velo = El(raiz, "capa", "velo");
            velo.pickingMode = PickingMode.Ignore;

            lateral = El(raiz, "menu-lateral");
            Animable(lateral, "anim--oculto-izq");
            var brillo = El(lateral, "brillo");
            brillo.pickingMode = PickingMode.Ignore;

            var imagotipo = El(lateral, "imagotipo");
            imagotipo.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("GemeloUI/Marca/imagotipo"));
            El(lateral, "filete");
            Txt(lateral, "GEMELO DIGITAL", "t-s", "f-negrita", "mayus", "c-amarillo");
            var den = Txt(lateral, "Escuela Politécnica", "denominacion");
            den.style.marginTop = 6;
            Txt(lateral, "de Cáceres", "denominacion").style.marginTop = -8;
            Txt(lateral, "Campus de Cáceres · datos en vivo de los sensores de SmartPoliTech, con simulación donde faltan", "subtitulo");

            var botones = El(lateral, "columna");
            botones.style.marginTop = 36;
            BotonMenu(botones, "Explorar el campus", "Recorre los edificios en tercera persona", "explorar", true,
                () => GemeloApp.Instancia?.CambiarModo(Modo.TerceraPersona));
            BotonMenu(botones, "Centro de control", "Vista cenital con capas de datos, gráficas y eventos", "control", false,
                () => GemeloApp.Instancia?.CambiarModo(Modo.Cenital));
            var fila = El(botones, "fila");
            fila.style.marginTop = 4;
            var aj = Boton(fila, "Ajustes", () => ui.AbrirAjustes(), "ajustes");
            aj.style.flexGrow = 1;
            aj.style.marginRight = 10;
            var salir = Boton(fila, "Salir", GemeloUI.Salir, "salir", "boton--peligro");
            salir.style.flexGrow = 1;

            El(lateral, "espacio");

            // Pie: estado del servicio
            var pie = El(lateral, "fila");
            var est = El(pie, "estado-conexion");
            punto = El(est, "punto");
            conexion = Txt(est, "Conectando…", "t-s", "f-semi");
            personas = Txt(pie, "", "t-s", "tenue");
            personas.style.marginLeft = 12;
            url = Txt(lateral, "", "t-xs", "tenue");
            url.style.marginTop = 8;
            var creditos = El(lateral, "fila");
            creditos.style.marginTop = 14;
            var sello = El(creditos, "sello");
            sello.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("GemeloUI/Marca/sello"));
            sello.style.marginRight = 12;
            Txt(creditos, "Universidad de Extremadura\nEscuela Politécnica · SmartPoliTech", "t-xs", "tenue");

            // Simulación, a la derecha
            tarjetaSim = Panel(raiz);
            tarjetaSim.style.position = Position.Absolute;
            tarjetaSim.style.right = 48;
            tarjetaSim.style.bottom = 56;
            tarjetaSim.style.paddingTop = 18;
            tarjetaSim.style.paddingBottom = 18;
            tarjetaSim.style.paddingLeft = 20;
            tarjetaSim.style.paddingRight = 20;
            Animable(tarjetaSim, "anim--oculto-der");
            sim = new PanelSimulacion(tarjetaSim);
        }

        static void BotonMenu(VisualElement padre, string titulo, string sub, string icono, bool primario, System.Action alPulsar)
        {
            var b = El(padre, "boton-menu");
            if (primario) b.AddToClassList("boton-menu--primario");
            b.Add(new Icono(icono, 26));
            var col = El(b, "columna", "crece");
            Txt(col, titulo, "boton-menu__titulo");
            Txt(col, sub, "boton-menu__sub");
            var flecha = new Icono("flecha-der", 20);
            flecha.style.marginRight = 0;
            b.Add(flecha);
            Clic(b, alPulsar);
        }

        public void Activar(bool si)
        {
            if (si) { Mostrar(raiz, true); sim.AlMostrar(); }
            Ver(lateral, si);
            Ver(tarjetaSim, si);
            if (!si) raiz.schedule.Execute(() => { if (!Visible(lateral)) Mostrar(raiz, false); }).StartingIn(320);
        }

        public void Actualizar()
        {
            sim.Actualizar();
            var cli = ClienteGemelo.Instancia;
            bool ok = cli != null && cli.Conectado;
            Clase(punto, "punto--ok", ok);
            Clase(punto, "punto--mal", !ok);
            Texto(conexion, ok ? "Servicio conectado" : cli != null && cli.UltimoError != null ? "Sin conexión con el servicio" : "Conectando…");
            Texto(url, cli != null ? cli.UrlBase : Ajustes.Actual.urlServicio);
            if (ok && cli.Estado != null)
            {
                float total = 0;
                foreach (var b in cli.Estado.buildings) total += Valor(b, "occupancy");
                Texto(personas, $"{Formato.Num(total)} personas en el campus");
            }
            else Texto(personas, "");
        }

        static float Valor(EstadoEdificio b, string p)
        {
            var m = b.metrics?.Find(x => x.property == p);
            return m != null ? m.value : 0f;
        }
    }
}
