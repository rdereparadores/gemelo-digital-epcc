// Menú de pausa (GemeloApp.PausaCambiada): reanudar, cambiar al otro modo, simulación, ajustes, volver al menú
// principal y salir. El gemelo sigue vivo detrás (no se toca Time.timeScale).
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class MenuPausa
    {
        public readonly VisualElement raiz;
        readonly VisualElement dialogo;
        readonly PanelSimulacion sim;
        readonly Label otroModo, otroModoSub;
        readonly Icono otroModoIcono;

        public MenuPausa(GemeloUI ui, VisualElement padre)
        {
            raiz = El(padre, "capa", "velo-oscuro");
            Mostrar(raiz, false);

            dialogo = Panel(raiz, "dialogo--pausa", "panel--fuerte");
            Animable(dialogo, "anim--oculto-escala");

            // Columna izquierda: acciones
            var izq = El(dialogo, "columna");
            izq.style.width = 360;
            izq.style.paddingTop = 26; izq.style.paddingBottom = 26; izq.style.paddingLeft = 28; izq.style.paddingRight = 24;
            izq.style.borderRightWidth = 1;
            izq.style.borderRightColor = new Color(1, 1, 1, 0.08f);
            Txt(izq, "PAUSA", "t-s", "f-negrita", "mayus", "c-amarillo");
            Txt(izq, "El campus sigue vivo detrás", "t-xs", "tenue").style.marginBottom = 18;

            Accion(izq, "Reanudar", "Esc", "play", true, () => GemeloApp.Instancia?.Pausar(false));
            var b = Accion(izq, "", "", "control", false, () =>
            {
                var app = GemeloApp.Instancia;
                if (app == null) return;
                app.CambiarModo(app.Modo == Modo.Cenital ? Modo.TerceraPersona : Modo.Cenital);
            });
            otroModoIcono = b.Q<Icono>();
            otroModo = b.Q<Label>(className: "boton-menu__titulo");
            otroModoSub = b.Q<Label>(className: "boton-menu__sub");
            Accion(izq, "Ajustes", "Gráficos, personas, controles, audio", "ajustes", false, () => ui.AbrirAjustes());
            Accion(izq, "Menú principal", "Volver a la portada", "volver", false, () => GemeloApp.Instancia?.CambiarModo(Modo.Menu));
            El(izq, "espacio");
            var salir = Boton(izq, "Salir del gemelo", GemeloUI.Salir, "salir", "boton--peligro");
            salir.style.marginTop = 12;

            // Columna derecha: simulación
            var der = El(dialogo, "columna", "crece");
            der.style.paddingTop = 26; der.style.paddingBottom = 26; der.style.paddingLeft = 28; der.style.paddingRight = 28;
            sim = new PanelSimulacion(der);
            sim.raiz.style.width = StyleKeyword.Auto;
        }

        static VisualElement Accion(VisualElement padre, string titulo, string sub, string icono, bool primario, System.Action alPulsar)
        {
            var b = El(padre, "boton-menu");
            if (primario) b.AddToClassList("boton-menu--primario");
            b.style.paddingTop = 11; b.style.paddingBottom = 11;
            b.Add(new Icono(icono, 22));
            var col = El(b, "columna", "crece");
            Txt(col, titulo, "boton-menu__titulo").style.fontSize = 15;
            Txt(col, sub, "boton-menu__sub");
            Clic(b, alPulsar);
            return b;
        }

        public bool Abierto => raiz.style.display == DisplayStyle.Flex && Visible(dialogo);

        public void Activar(bool si)
        {
            if (si)
            {
                var app = GemeloApp.Instancia;
                bool cen = app != null && app.Modo == Modo.Cenital;
                otroModo.text = cen ? "Explorar el campus" : "Centro de control";
                otroModoSub.text = cen ? "Pasar a tercera persona" : "Pasar a la vista cenital";
                otroModoIcono.Nombre = cen ? "explorar" : "control";
                Mostrar(raiz, true);
                raiz.style.opacity = 1;
                sim.AlMostrar();
                Ver(dialogo, true);
            }
            else
            {
                Ver(dialogo, false);
                raiz.schedule.Execute(() => { if (!Visible(dialogo)) Mostrar(raiz, false); }).StartingIn(300);
            }
        }

        public void Actualizar()
        {
            if (raiz.style.display == DisplayStyle.Flex) sim.Actualizar();
        }
    }
}
