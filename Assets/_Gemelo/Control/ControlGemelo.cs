// Módulo de control del gemelo: dueño de la cámara y de la entrada de juego. Según GemeloApp.Modo activa
// la cámara cinematográfica del menú, el avatar en tercera persona (con sus interacciones) o la vista
// cenital (con selección, corte por plantas y capa de color). Cada controlador propone una pose de cámara
// y aquí se aplica, con un fundido al cambiar de modo. También lleva el cursor y la tecla Esc (pausa).
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gemelo
{
    [DefaultExecutionOrder(200)]
    public class ControlGemelo : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Registrar() => GemeloApp.Arrancado += app => app.gameObject.AddComponent<ControlGemelo>();

        public static ControlGemelo Instancia { get; private set; }
        public Camera Camara { get; private set; }
        public CamaraCenital Cenital { get; private set; }
        public JugadorTerceraPersona Jugador { get; private set; }

        GemeloApp app;
        CamaraMenu menu;
        SeleccionCenital seleccion;
        CapaColor capa;
        InteraccionesJugador interacciones;

        // Pose que pide el controlador activo este frame
        Vector3 posDeseada;
        Quaternion rotDeseada = Quaternion.identity;
        float fovDeseado = 60f;
        bool hayPose;

        // Fundido entre modos
        Vector3 posIni;
        Quaternion rotIni;
        float fovIni;
        float fundido = 1f;
        const float DuracionFundido = 1.6f;

        void Awake()
        {
            Instancia = this;
            app = GetComponent<GemeloApp>();
            Camara = PrepararCamara();

            var raiz = new GameObject("Control");
            raiz.transform.SetParent(transform, false);
            menu = raiz.AddComponent<CamaraMenu>();
            Cenital = raiz.AddComponent<CamaraCenital>();
            seleccion = raiz.AddComponent<SeleccionCenital>();
            capa = raiz.AddComponent<CapaColor>();
            Jugador = raiz.AddComponent<JugadorTerceraPersona>();
            interacciones = raiz.AddComponent<InteraccionesJugador>();
        }

        void Start()
        {
            app.ModoCambiado += AlCambiarModo;
            app.PausaCambiada += _ => ActualizarCursor(true);
            Aplicar(app.Modo, false);
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
            if (app != null) app.ModoCambiado -= AlCambiarModo;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // La cámara de la escena colgaba del FirstPersonController, que el núcleo aparca: se suelta y se reutiliza
        // (conserva el posprocesado, el antialiasing y el AudioListener de la escena).
        Camera PrepararCamara()
        {
            Camera cam = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (c.CompareTag("MainCamera")) { cam = c; break; }
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                go.tag = "MainCamera";
                cam = go.GetComponent<Camera>();
                cam.farClipPlane = 2000f;
            }
            cam.transform.SetParent(transform, true);
            cam.gameObject.name = "Camara Gemelo";
            cam.gameObject.SetActive(true);
            cam.enabled = true;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
            return cam;
        }

        void AlCambiarModo(Modo m) => Aplicar(m, true);

        void Aplicar(Modo m, bool fundir)
        {
            if (fundir)
            {
                posIni = Camara.transform.position;
                rotIni = Camara.transform.rotation;
                fovIni = Camara.fieldOfView;
                fundido = 0f;
            }
            if (m != Modo.Cenital)
            {
                app.QuitarCortes();
                app.Seleccionar(Seleccion.Ninguna);
                app.PonerSobre(Seleccion.Ninguna);
            }
            menu.enabled = m == Modo.Menu;
            seleccion.enabled = m == Modo.Cenital;
            capa.enabled = m == Modo.Cenital;
            if (m == Modo.Cenital)
            {
                // Desde el jugador, se sube sobre él; desde el menú, vista general del campus
                if (Jugador.Raiz != null && Jugador.Raiz.gameObject.activeSelf)
                    Cenital.Entrar(Jugador.Raiz.position, 70f, Camara.transform.eulerAngles.y);
                else Cenital.EntrarGeneral();
            }
            Cenital.enabled = m == Modo.Cenital;
            Jugador.Activar(m == Modo.TerceraPersona);
            interacciones.enabled = m == Modo.TerceraPersona;
            if (m != Modo.TerceraPersona) app.OfrecerInteracciones(null);
            ActualizarCursor(true);
        }

        /// <summary>El controlador activo propone la pose de la cámara de este frame (se aplica en LateUpdate).</summary>
        public void Pose(Vector3 pos, Quaternion rot, float fov)
        {
            posDeseada = pos;
            rotDeseada = rot;
            fovDeseado = fov;
            hayPose = true;
        }

        /// <summary>Pose a la que va la cámara (sin el fundido entre modos).</summary>
        public Vector3 PosicionDeseada => posDeseada;

        void Update() => ActualizarCursor(false);

        // Esc se lee en LateUpdate, cuando la UI ya ha tenido ocasión de usarlo (GemeloApp.EscapeConsumido)
        void Esc()
        {
            var kb = Keyboard.current;
            if (kb == null || app.Pausado || !kb.escapeKey.wasPressedThisFrame) return;
            if (GemeloApp.EscapeConsumido == Time.frameCount) return;
            switch (app.Modo)
            {
                case Modo.TerceraPersona:
                    app.Pausar(true);
                    break;
                case Modo.Cenital:
                    if (!app.Seleccion.Vacia) app.Seleccionar(Seleccion.Ninguna);
                    else app.Pausar(true);
                    break;
            }
        }

        void LateUpdate()
        {
            Esc();
            if (!hayPose) return;
            var t = Camara.transform;
            if (fundido < 1f)
            {
                fundido = Mathf.Min(1f, fundido + Time.unscaledDeltaTime / DuracionFundido);
                float s = Mathf.SmoothStep(0f, 1f, fundido);
                var p = Vector3.Lerp(posIni, posDeseada, s);
                // Arco: entre poses lejanas la cámara sube un poco a mitad de camino
                p.y += Mathf.Sin(s * Mathf.PI) * Mathf.Min(40f, Vector3.Distance(posIni, posDeseada) * 0.2f);
                t.SetPositionAndRotation(p, Quaternion.Slerp(rotIni, rotDeseada, s));
                Camara.fieldOfView = Mathf.Lerp(fovIni, fovDeseado, s);
            }
            else
            {
                t.SetPositionAndRotation(posDeseada, rotDeseada);
                Camara.fieldOfView = fovDeseado;
            }
        }

        public bool EnFundido => fundido < 1f;

        // Bloqueado solo en tercera persona jugando; libre en el menú, la vista cenital y la pausa
        void ActualizarCursor(bool forzar)
        {
            bool bloquear = app.Modo == Modo.TerceraPersona && !app.Pausado && Application.isFocused;
            var estado = bloquear ? CursorLockMode.Locked : CursorLockMode.None;
            if (!forzar && Cursor.lockState == estado) return;
            Cursor.lockState = estado;
            Cursor.visible = !bloquear;
            if (bloquear) Jugador.IgnorarRatonUnFrame();
        }

        // ─── Ayudas de entrada compartidas por los controladores

        /// <summary>¿Hay que leer la entrada de juego? (no en pausa).</summary>
        public static bool Jugando => GemeloApp.Instancia != null && !GemeloApp.Instancia.Pausado;

        public static bool PunteroSobreUI(Vector2 p)
        {
            var f = GemeloApp.PunteroSobreUI;
            if (f == null) return false;
            try { return f(p); }
            catch { return false; }
        }

        /// <summary>Muescas de la rueda del ratón este frame (positivas hacia delante).</summary>
        public static float RuedaRaton()
        {
            var m = Mouse.current;
            if (m == null) return 0f;
            float v = m.scroll.ReadValue().y;
            if (Mathf.Abs(v) < 0.01f) return 0f;
            // Según la plataforma llega en «líneas» (±1) o en unidades de 120 por muesca
            return Mathf.Abs(v) >= 30f ? v / 120f : Mathf.Clamp(v, -3f, 3f);
        }

        /// <summary>Altura de lo más alto que hay bajo un punto (terreno, edificios, árboles con collider).</summary>
        public static float Superficie(Vector3 p, float desde = 400f)
        {
            var t = Terrain.activeTerrain;
            float y = t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
            if (Physics.Raycast(new Vector3(p.x, desde, p.z), Vector3.down, out var hit, desde + 50f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                y = Mathf.Max(y, hit.point.y);
            return y;
        }
    }
}
