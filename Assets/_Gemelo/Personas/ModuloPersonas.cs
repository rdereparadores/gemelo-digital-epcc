// Módulo Personas del gemelo: NPCs 1:1 con la ocupación que publica el servicio, con navegación por la malla del
// campus, vida (cambios de clase, llegadas y salidas, corrillos, móviles), evacuación con la alarma de incendio y
// dibujo por niveles de detalle. Se engancha a GemeloApp.Arrancado y solo usa el núcleo.
using System.Diagnostics;
using UnityEngine;

namespace Gemelo
{
    public class ModuloPersonas : MonoBehaviour
    {
        public static ModuloPersonas Instancia { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Registrar()
        {
            // Sin recarga de dominio al entrar en Play Mode los estáticos sobreviven: nada de suscribirse dos veces
            GemeloApp.Arrancado -= Arrancar;
            GemeloApp.Arrancado += Arrancar;
            GemeloApp.FabricaAvatar = FabricaAvatares.Crear;
            GemeloApp.FabricaJugador = FabricaAvatares.CrearJugador;
            FabricaAvatares.PruebaAvatares = null;
            Puerta.Presencia = null;
            DibujoMultitud.CamaraForzada = null;
            DibujoMultitud.SombrasBajoTecho = false;
            DibujoMultitud.OcultarTrasMuros = true;
        }

        static void Arrancar(GemeloApp app)
        {
            if (app.GetComponent<ModuloPersonas>() == null) app.gameObject.AddComponent<ModuloPersonas>();
        }

        public Multitud Multitud { get; private set; }
        public DibujoMultitud Dibujo { get; private set; }
        Navegacion nav;
        GemeloApp app;
        bool estadoNuevo;
        float tReconciliar;

        // Medidas (ms por fotograma, media móvil): simulación (reconciliar + rutas + paso) y preparación del dibujo
        public float MsSimulacion { get; private set; }
        public float MsDibujo { get; private set; }
        public float MsReconciliar { get; private set; }
        public float MsSimulacionFrame { get; private set; }
        public float MsDibujoFrame { get; private set; }

        /// <summary>Si es false, la multitud se sigue simulando pero no se dibuja.</summary>
        public bool Visible
        {
            get => visible;
            set { visible = value; Dibujo?.Mostrar(value); }
        }
        bool visible = true;
        readonly Stopwatch reloj = new();

        void Start()
        {
            if (Instancia != null && Instancia != this && Instancia.isActiveAndEnabled) { Destroy(this); return; }
            Instancia = this;
            app = GetComponent<GemeloApp>();
            var rec = RecursosPersonas.Cargar();
            nav = new Navegacion(rec);
            if (!nav.Lista || !RegistroCampus.Listo)
            {
                UnityEngine.Debug.LogWarning("[Personas] Sin malla de navegación o sin registro: no hay NPCs");
                enabled = false;
                return;
            }
            Multitud = new Multitud(nav);
            Dibujo = new DibujoMultitud(rec, Multitud, transform);
            if (app != null && app.Cliente != null) app.Cliente.EstadoActualizado += _ => estadoNuevo = true;
            Puerta.Presencia = Presencia;
            GemeloApp.AsientoOcupado = AsientoOcupado;
        }

        void OnDestroy()
        {
            if (Puerta.Presencia == (Puerta.ConsultaPresencia)Presencia) Puerta.Presencia = null;
            if (GemeloApp.AsientoOcupado == (System.Func<Vector3, bool>)AsientoOcupado) GemeloApp.AsientoOcupado = null;
            Dibujo?.Liberar();
            nav?.Liberar();
            if (Instancia == this) Instancia = null;
        }

        void Update()
        {
            if (Multitud == null) return;
            reloj.Restart();
            if (estadoNuevo || Time.time >= tReconciliar)
            {
                estadoNuevo = false;
                tReconciliar = Time.time + 2f;
                var r = Stopwatch.StartNew();
                Multitud.Reconciliar(app != null ? app.Cliente : ClienteGemelo.Instancia);
                MsReconciliar = (float)r.Elapsed.TotalMilliseconds;
            }
            Multitud.ProcesarRutas(0.6f);
            Multitud.Actualizar(Mathf.Min(Time.deltaTime, 0.1f));
            if (app != null) app.PersonasEnEscena = Multitud.activas.Count;
            MsSimulacionFrame = (float)reloj.Elapsed.TotalMilliseconds;
            MsSimulacion = Mathf.Lerp(MsSimulacion, MsSimulacionFrame, 0.05f);
        }

        void LateUpdate()
        {
            if (Dibujo == null) return;
            reloj.Restart();
            if (visible) Dibujo.Dibujar();
            MsDibujoFrame = (float)reloj.Elapsed.TotalMilliseconds;
            MsDibujo = Mathf.Lerp(MsDibujo, MsDibujoFrame, 0.05f);
        }

        readonly Persona[] vecinos = new Persona[16];

        // Para el jugador: un NPC sentado (o yendo a sentarse) en ese asiento
        bool AsientoOcupado(Vector3 asiento)
        {
            if (Multitud == null) return false;
            int n = Multitud.rejilla.Vecinos(asiento, 0.4f, 0.8f, vecinos);
            for (int i = 0; i < n; i++)
                if (vecinos[i].sentada) return true;
            return false;
        }

        // Para las puertas: la persona (o el jugador) más cerca, a ras de la puerta
        bool Presencia(Vector3 centro, Vector3 normal, float semiAncho, float alcance, out Vector3 pos)
        {
            pos = default;
            bool hay = false;
            float mejor = float.MaxValue;
            var j = app != null ? app.Jugador : null;
            if (j != null && j.gameObject.activeInHierarchy)
            {
                var d = j.position - centro;
                float frente = Mathf.Abs(d.x * normal.x + d.z * normal.z), largo = Mathf.Abs(d.x * normal.z - d.z * normal.x);
                if (Mathf.Abs(d.y) < 1.2f && frente <= alcance && largo <= semiAncho) { mejor = d.x * d.x + d.z * d.z; pos = j.position; hay = true; }
            }
            var p = Multitud?.rejilla.EnPaso(centro, normal, semiAncho, alcance, 1.2f);
            if (p != null)
            {
                var d = p.pos - centro;
                if (d.x * d.x + d.z * d.z < mejor) { pos = p.pos; hay = true; }
            }
            return hay;
        }
    }
}
