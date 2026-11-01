// Punto de entrada del gemelo digital en tiempo de ejecución. No hay nada montado a mano en la escena:
// al cargar una escena con edificios de la Politécnica se crea el objeto «Gemelo» con el cliente del servicio
// y el estado de la aplicación (modo, pausa, selección, capa de datos…). Cada módulo (Personas, Ambiente,
// Control, UI) se engancha a GemeloApp.Arrancado desde su propio [RuntimeInitializeOnLoadMethod] y añade
// sus componentes; así los módulos no dependen unos de otros, solo de este núcleo.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public enum Modo { Menu, TerceraPersona, Cenital }

    /// <summary>Magnitud con la que se colorean las estancias en la vista cenital.</summary>
    public enum Capa { Ninguna, Ocupacion, Temperatura, CO2, Confort, Humedad, Consumo, Ruido, Iluminacion }

    public enum NivelAviso { Info, Exito, Aviso, Critico }

    /// <summary>Lo seleccionado en la vista cenital (o lo que mira el jugador): un edificio y, si acaso, un local.</summary>
    public readonly struct Seleccion
    {
        public readonly InfoEdificio edificio;
        public readonly InfoLocal local;
        public Seleccion(InfoEdificio e, InfoLocal l) { edificio = e ?? l?.edificio; local = l; }
        public bool Vacia => edificio == null;
        public static readonly Seleccion Ninguna = default;
    }

    /// <summary>Acción contextual que ofrece el entorno al jugador (E la primera; 1–9 las demás).</summary>
    public class Interaccion
    {
        public string texto;           // «Abrir las ventanas del aula»
        public string icono;           // nombre corto opcional para la UI (ventana, calefaccion, clase, alarma…)
        public Action ejecutar;
    }

    /// <summary>Parámetros del Animator de los avatares (NPCs y jugador). Los clips de los maniquíes y,
    /// más adelante, los de Mixamo cuelgan del mismo controlador con estos nombres.</summary>
    public static class ParamAvatar
    {
        public const string Velocidad = "Velocidad";   // float, m/s sobre el suelo
        public const string Sentado = "Sentado";       // bool
        public const string Hablando = "Hablando";     // bool (gesticula de pie o sentado)
        public const string EnSuelo = "EnSuelo";       // bool
        public const string Saltar = "Saltar";         // trigger
    }

    public class GemeloApp : MonoBehaviour
    {
        public static GemeloApp Instancia { get; private set; }

        /// <summary>Se lanza una vez, tras crear el objeto Gemelo y el registro. Los módulos añaden aquí sus componentes.</summary>
        public static event Action<GemeloApp> Arrancado;

        public ClienteGemelo Cliente { get; private set; }

        // ─── Modo y pausa
        public Modo Modo { get; private set; } = Modo.Menu;
        public bool Pausado { get; private set; }
        public event Action<Modo> ModoCambiado;
        public event Action<bool> PausaCambiada;

        // ─── Selección y capa (vista cenital)
        public Seleccion Seleccion { get; private set; }
        public Capa Capa { get; private set; } = Capa.Ninguna;
        public event Action<Seleccion> SeleccionCambiada;
        public event Action<Capa> CapaCambiada;

        // ─── Corte por plantas: índice de la planta más alta visible por edificio (−1 = todo)
        readonly Dictionary<string, int> corte = new();
        public event Action<InfoEdificio, int> CorteCambiado;

        // ─── Lo que hay bajo el cursor en la vista cenital (para resaltarlo y dar un tooltip)
        public Seleccion Sobre { get; private set; }
        public event Action<Seleccion> SobreCambiado;

        // ─── Interacciones contextuales y avisos
        static readonly List<Interaccion> sinInteracciones = new();
        public IReadOnlyList<Interaccion> Interacciones { get; private set; } = sinInteracciones;
        public event Action<IReadOnlyList<Interaccion>> InteraccionesCambiadas;
        public event Action<string, NivelAviso> Aviso;

        // ─── Avatares: el módulo Personas registra aquí la fábrica (semilla → avatar con Animator de ParamAvatar)
        // para que el jugador en tercera persona use el mismo cuerpo que los NPCs. Puede ser null.
        public static Func<int, GameObject> FabricaAvatar;

        /// <summary>NPCs que hay ahora en la escena (lo pone el módulo Personas, para el HUD).</summary>
        public int PersonasEnEscena { get; set; }

        /// <summary>Local en el que está el jugador o la cámara (lo actualiza el módulo de control).</summary>
        public InfoLocal LocalActual { get; private set; }
        public event Action<InfoLocal> LocalActualCambiado;

        /// <summary>Transform del avatar del jugador (null fuera de la tercera persona). Lo pone el módulo de control.</summary>
        public Transform Jugador { get; set; }

        /// <summary>¿Está el puntero sobre un panel de la UI? Recibe la posición en píxeles de pantalla como la da
        /// Mouse.current.position (origen abajo a la izquierda). La rellena la UI; Control no hace clic, arrastre
        /// ni zoom donde devuelva true. Null = no hay UI.</summary>
        public static Func<Vector2, bool> PunteroSobreUI;

        /// <summary>Frame (Time.frameCount) en que la UI usó la tecla Esc para algo suyo (cerrar un desplegable, un
        /// popover…). Control lee Esc en LateUpdate y no hace nada con el Esc de ese frame.</summary>
        public static int EscapeConsumido = -1;

        // El proyecto entra en Play Mode sin recargar el dominio: los estáticos sobreviven entre sesiones. Se limpian
        // aquí, antes de que los módulos se suscriban en BeforeSceneLoad, para no acumular suscripciones.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar()
        {
            Arrancado = null;
            FabricaAvatar = null;
            PunteroSobreUI = null;
            EscapeConsumido = -1;
            Instancia = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arrancar()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") >= 0 || Array.IndexOf(args, "-sinGemelo") >= 0) return;
            if (FindAnyObjectByType<EdificioPolitecnica>() == null) return;
            if (Instancia != null) return;

            var go = new GameObject("Gemelo");
            DontDestroyOnLoad(go);
            go.AddComponent<GemeloApp>();
        }

        void Awake()
        {
            Instancia = this;
            RegistroCampus.Construir();
            Cliente = gameObject.AddComponent<ClienteGemelo>();
            Cliente.EstadoActualizado += e =>
            {
                RegistroCampus.Enlazar(e);
                HoraCampus.Sincronizar(e.simulation);
            };
            // El jugador en primera persona de la escena de trabajo se aparca: el gemelo trae sus propios modos
            var fp = FindAnyObjectByType<FirstPersonController>();
            if (fp != null) fp.gameObject.SetActive(false);
            Application.runInBackground = true;
        }

        void Start()
        {
            try { Arrancado?.Invoke(this); }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"[Gemelo] arrancado: {RegistroCampus.Edificios.Count} edificios, {RegistroCampus.Locales.Count} locales");
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
        }

        // ─────────────── API para los módulos ───────────────

        public void CambiarModo(Modo m)
        {
            if (Pausado) Pausar(false);
            if (m == Modo) return;
            Modo = m;
            ModoCambiado?.Invoke(m);
        }

        public void Pausar(bool si)
        {
            if (si == Pausado || Modo == Modo.Menu && si) return;
            Pausado = si;
            PausaCambiada?.Invoke(si);
        }

        public void Seleccionar(Seleccion s)
        {
            if (s.edificio == Seleccion.edificio && s.local == Seleccion.local) return;
            Seleccion = s;
            SeleccionCambiada?.Invoke(s);
        }

        public void CambiarCapa(Capa c)
        {
            if (c == Capa) return;
            Capa = c;
            CapaCambiada?.Invoke(c);
        }

        public int Corte(InfoEdificio e) => e != null && corte.TryGetValue(e.codigo, out var i) ? i : -1;

        /// <summary>Muestra el edificio hasta la planta indicada (índice en plantas; −1 = completo, con cubierta).</summary>
        public void CortarEdificio(InfoEdificio e, int indice)
        {
            if (e == null) return;
            if (Corte(e) == indice) return;
            corte[e.codigo] = indice;
            e.edificio.MostrarHasta(indice);
            CorteCambiado?.Invoke(e, indice);
        }

        public void QuitarCortes()
        {
            foreach (var e in RegistroCampus.Edificios) CortarEdificio(e, -1);
        }

        public void OfrecerInteracciones(IReadOnlyList<Interaccion> lista)
        {
            lista ??= sinInteracciones;
            if (ReferenceEquals(lista, Interacciones)) return;
            Interacciones = lista;
            InteraccionesCambiadas?.Invoke(lista);
        }

        public void PonerSobre(Seleccion s)
        {
            if (s.edificio == Sobre.edificio && s.local == Sobre.local) return;
            Sobre = s;
            SobreCambiado?.Invoke(s);
        }

        public void PonerLocalActual(InfoLocal l)
        {
            if (l == LocalActual) return;
            LocalActual = l;
            LocalActualCambiado?.Invoke(l);
        }

        public void Avisar(string texto, NivelAviso nivel = NivelAviso.Info) => Aviso?.Invoke(texto, nivel);
    }
}
