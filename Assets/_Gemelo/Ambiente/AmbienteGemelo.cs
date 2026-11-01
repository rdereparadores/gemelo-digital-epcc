// Módulo Ambiente del gemelo: lo que dicen los sensores que no son personas, hecho visible en la escena.
// Sol, cielo, nubes y lluvia según la hora del modelo y el tiempo; luces interiores y ventanas iluminadas;
// ventanas y persianas de las estancias con sensor; indicadores flotantes de las alertas; emergencias
// (incendio, apagón, fuga de agua) y sonido ambiente.
//
// Este componente es la raíz del módulo: guarda el estado compartido (hora y tiempo efectivos, con el modo de
// prueba, y los eventos activos de la simulación) y crea el resto de componentes sobre el objeto «Gemelo».
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public class AmbienteGemelo : MonoBehaviour
    {
        public static AmbienteGemelo Instancia { get; private set; }

        /// <summary>Modo de prueba: fuerza la hora y el tiempo que ve este módulo (no toca el servicio).
        /// Desde el editor: <c>Gemelo.AmbienteGemelo.Prueba.hora = new System.DateTime(2026,11,3,23,0,0);</c></summary>
        public static class Prueba
        {
            public static DateTime? hora;      // hora local del campus
            public static float? nubes;        // 0–1
            public static float? lluvia;       // mm/h
            public static float? viento;       // m/s
            /// <summary>Persiana forzada (0 subida – 1 bajada) por clave de local.</summary>
            public static readonly Dictionary<string, float> persianas = new();
            /// <summary>Locales (por clave) con las luces encendidas a la fuerza, digan lo que digan los sensores.</summary>
            public static readonly HashSet<string> luces = new();

            /// <summary>Estroboscópicos y balizas siempre encendidos (para las capturas).</summary>
            public static bool destellos;
            /// <summary>Punto de referencia en vez de la cámara principal (qué estroboscópicos, farolas y
            /// señales se encienden cerca).</summary>
            public static Vector3? camara;

            public static void Quitar() { hora = null; nubes = null; lluvia = null; viento = null; persianas.Clear(); luces.Clear(); destellos = false; camara = null; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Registrar()
        {
            // (el proyecto entra en Play Mode sin recargar el dominio: nada de suscribirse dos veces ni de
            // arrastrar datos estáticos de la sesión anterior)
            GemeloApp.Arrancado -= Crear;
            GemeloApp.Arrancado += Crear;
            DatosVentanas.Olvidar();
            Prueba.Quitar();
        }

        static void Crear(GemeloApp app)
        {
            if (app.GetComponentInChildren<AmbienteGemelo>() != null) return;
            var go = new GameObject("Ambiente");
            go.transform.SetParent(app.transform, false);
            go.AddComponent<AmbienteGemelo>();
        }

        // ─── Hora y tiempo efectivos (con el modo de prueba)

        public DateTime Utc => Prueba.hora.HasValue ? HoraCampus.AUtc(Prueba.hora.Value) : HoraCampus.Utc;
        public DateTime Local => Prueba.hora ?? HoraCampus.Local;

        /// <summary>Elevación y acimut del sol ahora (grados).</summary>
        public float ElevacionSol { get; private set; } = 45f;
        public float AcimutSol { get; private set; } = 180f;

        /// <summary>0 de día, 1 de noche cerrada (con el crepúsculo civil en medio).</summary>
        public float Noche { get; private set; }

        /// <summary>Luz natural relativa (0–1): la que entra por las ventanas, con las nubes.</summary>
        public float LuzNatural { get; private set; } = 1f;

        public float Nubes { get; private set; }
        public float Lluvia { get; private set; }       // mm/h
        public float Viento { get; private set; }       // m/s
        public float DireccionViento { get; private set; } = 270f;   // de dónde viene, grados

        // ─── Eventos activos de la simulación (se sondean aparte del estado)

        public readonly HashSet<string> EdificiosEnAlarma = new();     // códigos 1901…
        public readonly HashSet<string> EdificiosSinLuz = new();
        public readonly HashSet<InfoLocal> LocalesSinLuz = new();
        public readonly HashSet<InfoLocal> LocalesConFuga = new();
        public readonly HashSet<InfoEdificio> EdificiosConFuga = new();
        public readonly HashSet<InfoLocal> LocalesConHumo = new();
        public bool SimulacroEnCurso { get; private set; }
        /// <summary>Cambia cuando cambian los eventos activos.</summary>
        public event Action EventosCambiados;

        float siguienteSondeo;
        string firmaEventos = "";
        bool pidiendo;

        void Awake()
        {
            Instancia = this;
            gameObject.AddComponent<CieloYSol>();
            gameObject.AddComponent<Lluvia>();
            gameObject.AddComponent<Farolas>();
            gameObject.AddComponent<LucesInteriores>();
            gameObject.AddComponent<Ventanas>();
            gameObject.AddComponent<Indicadores>();
            gameObject.AddComponent<Emergencias>();
            gameObject.AddComponent<AudioAmbiente>();
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
        }

        void Update()
        {
            Actualizar();
            var cli = ClienteGemelo.Instancia;
            if (cli != null && cli.Conectado && !pidiendo && Time.realtimeSinceStartup >= siguienteSondeo)
            {
                pidiendo = true;
                siguienteSondeo = Time.realtimeSinceStartup + 2f;
                cli.Eventos(LeerEventos);
            }
        }

        void Actualizar()
        {
            HoraCampus.Sol(Utc, out float elev, out float az);
            ElevacionSol = elev;
            AcimutSol = az;
            Noche = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, -0.5f, elev));

            var t = ClienteGemelo.Instancia?.Estado?.weather;
            Nubes = Prueba.nubes ?? (t != null ? Mathf.Clamp01(t.cloud_cover) : 0.1f);
            Lluvia = Prueba.lluvia ?? (t != null ? Mathf.Max(0f, t.precipitation_rate) : 0f);
            float v = float.NaN, dir = float.NaN;
            if (t?.metrics != null)
                foreach (var m in t.metrics)
                {
                    if (m.property == "wind_speed") v = m.value;
                    else if (m.property == "wind_direction") dir = m.value;
                }
            Viento = Prueba.viento ?? (float.IsNaN(v) ? 2f : v);
            if (!float.IsNaN(dir)) DireccionViento = dir;

            float sol = Mathf.Clamp01(Mathf.Sin(Mathf.Max(0f, elev) * Mathf.Deg2Rad) / 0.5f);
            LuzNatural = sol * (1f - 0.7f * Nubes) * (1f - Noche);
        }

        void LeerEventos(List<EventoSimulacion> lista)
        {
            pidiendo = false;
            if (lista == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var e in lista) if (e.active) sb.Append(e.id).Append(e.type).Append(e.target).Append('|');
            string firma = sb.ToString();
            if (firma == firmaEventos) return;
            firmaEventos = firma;

            EdificiosEnAlarma.Clear(); EdificiosSinLuz.Clear(); LocalesSinLuz.Clear();
            LocalesConFuga.Clear(); EdificiosConFuga.Clear(); LocalesConHumo.Clear();
            SimulacroEnCurso = false;
            foreach (var e in lista)
            {
                if (!e.active) continue;
                switch (e.type)
                {
                    case "fire_alarm":
                        foreach (var ed in Edificios(e.target, true)) EdificiosEnAlarma.Add(ed.codigo);
                        var lh = RegistroCampus.PorEspacio(e.target);
                        if (lh != null) LocalesConHumo.Add(lh);
                        if (e.target == "campus") SimulacroEnCurso = true;
                        break;
                    case "power_outage":
                        foreach (var ed in Edificios(e.target, false)) EdificiosSinLuz.Add(ed.codigo);
                        var lp = RegistroCampus.PorEspacio(e.target);
                        if (lp != null) LocalesSinLuz.Add(lp);
                        break;
                    case "water_leak":
                        var lf = RegistroCampus.PorEspacio(e.target);
                        if (lf != null) LocalesConFuga.Add(lf);
                        else foreach (var ed in Edificios(e.target, false)) EdificiosConFuga.Add(ed);
                        break;
                }
            }
            EventosCambiados?.Invoke();
        }

        /// <summary>Edificios a los que afecta un objetivo de evento: «campus», un edificio (INF o 1903) y, con
        /// deEstancia, el de la estancia (la alarma de incendio suena en todo el edificio).</summary>
        static IEnumerable<InfoEdificio> Edificios(string objetivo, bool deEstancia)
        {
            if (objetivo == "campus")
            {
                foreach (var e in RegistroCampus.Edificios) yield return e;
                yield break;
            }
            var ed = RegistroCampus.Edificio(objetivo);
            if (ed != null) { yield return ed; yield break; }
            var l = RegistroCampus.PorEspacio(objetivo);
            if (l != null && deEstancia) yield return l.edificio;
        }

        // ─── Consultas para los demás componentes

        /// <summary>Posición de la cámara principal (o la del modo de prueba).</summary>
        public static bool PosicionCamara(out Vector3 p)
        {
            var c = Camera.main;
            p = Prueba.camara ?? (c != null ? c.transform.position : Vector3.zero);
            return Prueba.camara.HasValue || c != null;
        }

        public bool SinLuz(InfoLocal l) => l != null && (EdificiosSinLuz.Contains(l.edificio.codigo) || LocalesSinLuz.Contains(l));
        public bool EnAlarma(InfoEdificio e) => e != null && EdificiosEnAlarma.Contains(e.codigo);

        /// <summary>Estado de la estancia del servicio, o null.</summary>
        public static EstadoEstancia Estado(InfoLocal l)
        {
            var cli = ClienteGemelo.Instancia;
            return cli != null && l != null && cli.Estancia(l.clave, out var e) ? e : null;
        }

        /// <summary>¿Hay alarma de incendio en el edificio, por evento o por las alertas del estado?</summary>
        public bool AlarmaEn(InfoEdificio e)
        {
            if (EnAlarma(e)) return true;
            var cli = ClienteGemelo.Instancia;
            if (cli == null || !cli.Edificio(e.codigo, out var est) || est.alerts == null) return false;
            foreach (var a in est.alerts) if (a.code == "fire_alarm") return true;
            return false;
        }
    }
}
