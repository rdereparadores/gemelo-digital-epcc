// Interfaz del gemelo (UI Toolkit, montada por código). Un solo UIDocument con capas: menú inicial, HUD de
// tercera persona, centro de control (vista cenital), menú de pausa, ajustes, avisos y tooltip.
// Estética «UEx moderna»: Resources/GemeloUI/Gemelo.uss. Se engancha a GemeloApp.Arrancado.
//
// Entrada: el módulo de control lee las teclas de juego (y Esc para pausar); aquí solo se atiende Esc cuando hay
// un panel o menú abierto, para cerrarlo o volver. GemeloApp.PunteroSobreUI dice a Control si el puntero está
// sobre un panel (para no seleccionar cosas de la escena a través de la UI).
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class GemeloUI : MonoBehaviour
    {
        public static GemeloUI Instancia { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Registrar() => GemeloApp.Arrancado += app =>
        {
            var go = new GameObject("UI");
            go.transform.SetParent(app.transform, false);
            go.AddComponent<GemeloUI>();
        };

        UIDocument doc;
        PanelSettings ajustesPanel;
        public PanelSettings AjustesPanel => ajustesPanel;
        VisualElement raiz;
        GemeloApp app;

        MenuInicial menu;
        MenuPausa pausa;
        PanelAjustes ajustes;
        HudTercera hud;
        CentroControl cenital;
        VisualElement avisos, tooltip;
        Label tipTitulo, tipSub, tipValor, fps;
        VisualElement tipMuestra;

        int framePausa = -10;
        bool reanudarPendiente;
        float fpsSuave, fpsProximo;

        void Awake()
        {
            Instancia = this;
            app = GemeloApp.Instancia;

            ajustesPanel = ScriptableObject.CreateInstance<PanelSettings>();
            ajustesPanel.name = "PanelGemelo";
            ajustesPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("GemeloUI/Tema");
            ajustesPanel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ajustesPanel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ajustesPanel.match = 0.5f;
            ajustesPanel.sortingOrder = 100;
            ajustesPanel.clearColor = false;
            Escala(Ajustes.Actual.escalaUI);

            gameObject.SetActive(false);
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = ajustesPanel;
            gameObject.SetActive(true);

            GemeloApp.PunteroSobreUI = PunteroSobre;
            AsegurarEventSystem();
        }

        void Start()
        {
            var r = doc.rootVisualElement;
            r.pickingMode = PickingMode.Ignore;
            var hoja = Resources.Load<StyleSheet>("GemeloUI/Gemelo");
            if (hoja != null) r.styleSheets.Add(hoja);
            else Debug.LogWarning("[Gemelo/UI] Falta Resources/GemeloUI/Gemelo.uss");

            raiz = El(r, "raiz");
            raiz.pickingMode = PickingMode.Ignore;

            hud = new HudTercera(this, raiz);
            cenital = new CentroControl(this, raiz);
            menu = new MenuInicial(this, raiz);
            pausa = new MenuPausa(this, raiz);
            ajustes = new PanelAjustes(raiz);

            avisos = El(raiz, "avisos");
            avisos.pickingMode = PickingMode.Ignore;

            tooltip = El(raiz, "tooltip");
            var filaTip = El(tooltip, "fila");
            tipMuestra = El(filaTip);
            tipMuestra.style.width = 10; tipMuestra.style.height = 10; tipMuestra.style.marginRight = 8;
            SetRadio(tipMuestra, 3);
            tipTitulo = Txt(filaTip, "", "tooltip__titulo");
            tipSub = Txt(tooltip, "", "tooltip__sub");
            tipValor = Txt(tooltip, "", "tooltip__valor");
            SinPicking(tooltip);
            tooltip.style.opacity = 0;

            fps = Txt(raiz, "", "fps");
            fps.style.position = Position.Absolute;
            fps.style.left = Length.Percent(50);
            fps.style.translate = new Translate(Length.Percent(-50), 0);
            fps.style.bottom = 10;
            fps.pickingMode = PickingMode.Ignore;

            app.ModoCambiado += AlCambiarModo;
            app.PausaCambiada += AlPausar;
            app.Aviso += Avisar;
            app.SobreCambiado += _ => ActualizarTooltip();
            app.CapaCambiada += _ => ActualizarTooltip();
            app.Cliente.EstadoActualizado += _ => { if (!app.Sobre.Vacia) ActualizarTooltip(); };
            Ajustes.Cambiados += AplicarGlobales;
            AplicarGlobales(Ajustes.Actual);
            AlCambiarModo(app.Modo);
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
            if (GemeloApp.PunteroSobreUI == (Func<Vector2, bool>)PunteroSobre) GemeloApp.PunteroSobreUI = null;
            Ajustes.Cambiados -= AplicarGlobales;
            if (ajustesPanel != null) Destroy(ajustesPanel);
        }

        static void SetRadio(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        // ───────────────────────── Modos ─────────────────────────

        void AlCambiarModo(Modo m)
        {
            if (ajustes.Abierto) ajustes.Cerrar();
            menu.Activar(m == Modo.Menu);
            hud.Activar(m == Modo.TerceraPersona);
            cenital.Activar(m == Modo.Cenital);
            if (pausa.Abierto && !app.Pausado) pausa.Activar(false);
        }

        void AlPausar(bool si)
        {
            framePausa = Time.frameCount;
            reanudarPendiente = false;
            if (!si && ajustes.Abierto) ajustes.Cerrar();
            pausa.Activar(si);
        }

        /// <summary>Abre los ajustes; al cerrarlos se vuelve a lo que hubiera debajo (menú o pausa).</summary>
        public void AbrirAjustes() => ajustes.Abrir();

        public static void Salir()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ───────────────────────── Bucle ─────────────────────────

        void Update()
        {
            if (raiz == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Escape();

            switch (app.Modo)
            {
                case Modo.Menu: menu.Actualizar(); break;
                case Modo.TerceraPersona: hud.Actualizar(); break;
                case Modo.Cenital: cenital.Actualizar(); break;
            }
            pausa.Actualizar();
            MoverTooltip();
            Fps();
        }

        void LateUpdate()
        {
            if (raiz == null || ajustes == null) return;
            // Esc en el menú de pausa: se reanuda al final del fotograma, por si Control ya ha conmutado la pausa
            if (reanudarPendiente)
            {
                reanudarPendiente = false;
                if (app.Pausado && Time.frameCount != framePausa) app.Pausar(false);
            }
            // Con un menú delante, el cursor se ve y está libre
            if (app.Modo == Modo.Menu || app.Pausado || ajustes.Abierto)
            {
                if (UnityEngine.Cursor.lockState != CursorLockMode.None) UnityEngine.Cursor.lockState = CursorLockMode.None;
                if (!UnityEngine.Cursor.visible) UnityEngine.Cursor.visible = true;
            }
        }

        void Escape()
        {
            if (ajustes.Abierto) { ajustes.Cerrar(); GemeloApp.EscapeConsumido = Time.frameCount; return; }
            if (cenital.CerrarEmergente()) { GemeloApp.EscapeConsumido = Time.frameCount; return; }
            if (app.Pausado && pausa.Abierto && Time.frameCount != framePausa) reanudarPendiente = true;
        }

        void Fps()
        {
            bool ver = Ajustes.Actual.mostrarFps;
            Mostrar(fps, ver);
            if (!ver) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            fpsSuave = fpsSuave <= 0 ? 1f / dt : Mathf.Lerp(fpsSuave, 1f / dt, 0.05f);
            if (Time.unscaledTime < fpsProximo) return;
            fpsProximo = Time.unscaledTime + 0.25f;
            fps.text = $"{Mathf.RoundToInt(fpsSuave)} FPS · {Formato.Num(1000f / fpsSuave, 1)} ms";
        }

        // ───────────────────────── Ajustes globales ─────────────────────────

        void AplicarGlobales(Ajustes a)
        {
            if (a.calidad >= 0 && a.calidad < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != a.calidad)
                QualitySettings.SetQualityLevel(a.calidad, true);
            if (!Application.isEditor && Screen.fullScreen != a.pantallaCompleta) Screen.fullScreen = a.pantallaCompleta;
            Application.targetFrameRate = a.fpsMaximo > 0 ? a.fpsMaximo : -1;
            QualitySettings.vSyncCount = a.fpsMaximo > 0 ? 0 : 1;
            AudioListener.volume = Mathf.Clamp01(a.volumenGeneral);
            Escala(a.escalaUI);
        }

        void Escala(float e)
        {
            e = Mathf.Clamp(e, 0.5f, 2f);
            ajustesPanel.referenceResolution = new Vector2Int(Mathf.RoundToInt(1920 / e), Mathf.RoundToInt(1080 / e));
        }

        // ───────────────────────── Puntero sobre la UI ─────────────────────────

        bool PunteroSobre(Vector2 pantalla)
        {
            var panel = doc != null ? doc.rootVisualElement?.panel : null;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(pantalla.x, Screen.height - pantalla.y));
            var e = panel.Pick(p);
            return e != null;
        }

        /// <summary>Posición del ratón en coordenadas del panel (o null si no hay ratón).</summary>
        public Vector2? RatonEnPanel()
        {
            var m = Mouse.current;
            var panel = doc.rootVisualElement?.panel;
            if (m == null || panel == null) return null;
            var s = m.position.ReadValue();
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(s.x, Screen.height - s.y));
        }

        /// <summary>Que la UI reciba los eventos del Input System aunque la escena no tenga EventSystem.</summary>
        static void AsegurarEventSystem()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem (UI del gemelo)");
            DontDestroyOnLoad(go);
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        /// <summary>Para pruebas por consola: pulsa el elemento cuyo texto es este (el botón que contiene la etiqueta).</summary>
        public bool PulsarTexto(string texto)
        {
            Label l = null;
            raiz.Query<Label>().ForEach(x => { if (l == null && x.text == texto && SeVe(x)) l = x; });
            if (l == null) return false;
            VisualElement b = l;
            while (b != null && !b.focusable) b = b.parent;
            b ??= l;
            using (var e = ClickEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
            return true;
        }

        static bool SeVe(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.opacity < 0.01f) return false;
            return true;
        }

        public bool AjustesAbiertos => ajustes != null && ajustes.Abierto;

        // ───────────────────────── Avisos ─────────────────────────

        void Avisar(string texto, NivelAviso nivel)
        {
            if (avisos == null || string.IsNullOrEmpty(texto)) return;
            while (avisos.childCount >= 4) avisos.RemoveAt(0);
            string clase = nivel switch
            {
                NivelAviso.Exito => "toast--exito", NivelAviso.Aviso => "toast--aviso", NivelAviso.Critico => "toast--critico", _ => "toast--info",
            };
            string icono = nivel switch { NivelAviso.Exito => "ok", NivelAviso.Aviso => "alerta", NivelAviso.Critico => "alerta", _ => "info" };
            var t = El(avisos, "toast", clase);
            t.Add(new Icono(icono, 18));
            Txt(t, texto, "toast__texto");
            SinPicking(t);
            t.style.opacity = 0;
            t.style.translate = new Translate(0, -14);
            t.schedule.Execute(() => { t.style.opacity = 1; t.style.translate = new Translate(0, 0); }).StartingIn(20);
            long vida = nivel == NivelAviso.Critico ? 7000 : nivel == NivelAviso.Aviso ? 5500 : 4000;
            t.schedule.Execute(() => { t.style.opacity = 0; t.style.translate = new Translate(0, -10); }).StartingIn(vida);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(vida + 400);
        }

        // ───────────────────────── Tooltip (vista cenital) ─────────────────────────

        void ActualizarTooltip()
        {
            if (tooltip == null) return;
            var s = app.Sobre;
            bool ver = app.Modo == Modo.Cenital && !app.Pausado && !s.Vacia;
            if (!ver) { tooltip.style.opacity = 0; return; }
            var capa = app.Capa;
            if (s.local != null)
            {
                tipTitulo.text = Formato.NombreLocal(s.local);
                tipSub.text = $"{s.edificio?.codigo} · {Formato.Planta(s.local.planta?.nombre)} · {Formato.Uso(s.local.local.uso)}";
                ClienteGemelo.Instancia.Estancia(s.local.clave, out var est);
                if (capa == Capa.Ninguna)
                {
                    float oc = est != null ? est.Valor("occupancy") : float.NaN;
                    tipValor.text = float.IsNaN(oc) ? "Sin sensores" : $"{Formato.Num(oc)} personas";
                    tipMuestra.style.display = DisplayStyle.None;
                }
                else
                {
                    float v = Escalas.Valor(capa, est, s.local);
                    tipValor.text = capa == Capa.Confort ? Escalas.ConfortTexto(est?.comfort) : $"{Escalas.Nombre(capa)}: {Formato.ValorCapa(capa, v)}";
                    tipMuestra.style.display = float.IsNaN(v) ? DisplayStyle.None : DisplayStyle.Flex;
                    tipMuestra.style.backgroundColor = Escalas.Color(capa, v);
                }
            }
            else
            {
                tipTitulo.text = Formato.NombreEdificio(s.edificio);
                tipSub.text = $"Edificio {s.edificio.codigo} · {s.edificio.plantas.Count} plantas";
                ClienteGemelo.Instancia.Edificio(s.edificio.codigo, out var eb);
                var m = eb?.metrics?.Find(x => x.property == "occupancy");
                tipValor.text = m != null ? $"{Formato.Num(m.value)} personas" : "";
                tipMuestra.style.display = DisplayStyle.None;
            }
            tooltip.style.opacity = 1;
        }

        void MoverTooltip()
        {
            if (tooltip.resolvedStyle.opacity <= 0.01f) return;
            var p = RatonEnPanel();
            if (p == null) return;
            var r = raiz.layout;
            float x = p.Value.x + 18, y = p.Value.y + 18;
            float w = tooltip.layout.width, h = tooltip.layout.height;
            if (x + w > r.width - 8) x = p.Value.x - w - 12;
            if (y + h > r.height - 8) y = p.Value.y - h - 12;
            tooltip.style.left = x;
            tooltip.style.top = y;
        }
    }
}
