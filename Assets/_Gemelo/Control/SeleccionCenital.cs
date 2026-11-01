// Selección en la vista cenital: lo que hay bajo el cursor (GemeloApp.PonerSobre, para el tooltip y el
// resaltado), clic para seleccionar un edificio o, si está cortado, un local de su planta visible; clic en
// vacío para quitar la selección; F o doble clic para centrar la cámara; AvPág/RePág para cortar el edificio
// seleccionado por plantas. Lo dibuja CapaColor.
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gemelo
{
    public class SeleccionCenital : MonoBehaviour
    {
        GemeloApp app;
        Vector2 posPulsado;
        bool pulsado;
        float ultimoClic = -10f;
        Vector2 posUltimoClic;
        Seleccion seleccionUltimoClic;

        void Awake() => app = GemeloApp.Instancia;

        void OnEnable()
        {
            if (app == null) app = GemeloApp.Instancia;
            app.CorteCambiado += AlCortar;
            pulsado = false;
        }

        void OnDisable()
        {
            if (app == null) return;
            app.CorteCambiado -= AlCortar;
            app.PonerSobre(Seleccion.Ninguna);
        }

        // Si el local seleccionado deja de verse (se cortó por debajo o se volvió a poner la planta de encima),
        // queda seleccionado solo el edificio
        void AlCortar(InfoEdificio e, int indice)
        {
            var s = app.Seleccion;
            if (s.local != null && s.edificio == e && s.local.planta.indice != indice)
                app.Seleccionar(new Seleccion(e, null));
        }

        void Update()
        {
            var m = Mouse.current;
            var kb = Keyboard.current;
            if (m == null || kb == null) return;
            if (!ControlGemelo.Jugando) { pulsado = false; return; }

            var raton = m.position.ReadValue();
            bool sobreUI = ControlGemelo.PunteroSobreUI(raton);
            bool dentro = raton.x >= 0 && raton.y >= 0 && raton.x <= Screen.width && raton.y <= Screen.height;
            var bajo = sobreUI || !dentro ? Seleccion.Ninguna : Elegir(raton);
            app.PonerSobre(bajo);

            bool alt = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
            if (m.leftButton.wasPressedThisFrame)
            {
                pulsado = !sobreUI && !alt;
                posPulsado = raton;
            }
            if (m.leftButton.wasReleasedThisFrame && pulsado)
            {
                pulsado = false;
                if ((raton - posPulsado).sqrMagnitude < 36f)
                {
                    bool doble = Time.unscaledTime - ultimoClic < 0.35f && (raton - posUltimoClic).sqrMagnitude < 100f;
                    ultimoClic = Time.unscaledTime;
                    posUltimoClic = raton;
                    if (doble && !seleccionUltimoClic.Vacia)
                    {
                        app.Seleccionar(seleccionUltimoClic);
                        ControlGemelo.Instancia.Cenital.Centrar(seleccionUltimoClic);
                    }
                    else
                    {
                        app.Seleccionar(bajo);
                        seleccionUltimoClic = bajo;
                    }
                }
            }

            if (kb.fKey.wasPressedThisFrame) ControlGemelo.Instancia.Cenital.Centrar(app.Seleccion);

            var sel = app.Seleccion.edificio;
            if (sel != null)
            {
                int n = sel.edificio.plantas.Length;
                int c = app.Corte(sel);
                if (kb.pageDownKey.wasPressedThisFrame)
                    app.CortarEdificio(sel, c < 0 ? n - 1 : Mathf.Max(0, c - 1));
                else if (kb.pageUpKey.wasPressedThisFrame && c >= 0)
                    app.CortarEdificio(sel, c + 1 >= n ? -1 : c + 1);
            }
        }

        /// <summary>Edificio (y local, si está cortado) bajo un punto de la pantalla.</summary>
        public static Seleccion Elegir(Vector2 pantalla)
        {
            var control = ControlGemelo.Instancia;
            if (control == null) return Seleccion.Ninguna;
            var rayo = control.Camara.ScreenPointToRay(pantalla);
            if (!Physics.Raycast(rayo, out var hit, 3000f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return Seleccion.Ninguna;
            var ep = hit.collider.GetComponentInParent<EdificioPolitecnica>();
            if (ep == null) return Seleccion.Ninguna;
            var e = RegistroCampus.Edificio(ep.codigo);
            if (e == null) return Seleccion.Ninguna;
            int corte = GemeloApp.Instancia.Corte(e);
            if (corte < 0) return new Seleccion(e, null);
            InfoLocal mejor = null;
            foreach (var p in e.plantas)
            {
                if (p.indice != corte) continue;
                foreach (var l in p.locales)
                    if (hit.point.y > l.local.cotaSuelo - 0.6f && l.local.Contiene(hit.point) && (mejor == null || l.area < mejor.area)) mejor = l;
            }
            return new Seleccion(e, mejor);
        }
    }
}
