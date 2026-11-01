// Cliente del servicio REST del gemelo (ServicioSensores/docs/CONTRATO_API.md).
// Sondea GET /api/v1/state con ETag (un 304 no cuesta nada) y publica el estado por estancia del modelo
// (clave unity_room = Local.Clave, p. ej. "1903_P1_24") y por edificio (código del plano, "1903").
// También manda las órdenes de Unity al servicio: reloj y escenario de la simulación y eventos.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace Gemelo
{
    public class ClienteGemelo : MonoBehaviour
    {
        public static ClienteGemelo Instancia { get; private set; }

        [Tooltip("Segundos entre sondeos del estado")]
        [Min(0.25f)] public float intervalo = 1f;

        public string UrlBase => Ajustes.Actual.urlServicio.TrimEnd('/');
        public EstadoCampus Estado { get; private set; }
        public bool Conectado { get; private set; }
        /// <summary>Hora real (Time.realtimeSinceStartup) del último estado recibido.</summary>
        public float UltimaRecepcion { get; private set; } = -1f;
        public string UltimoError { get; private set; }

        /// <summary>Cada vez que llega un estado nuevo (no en los 304).</summary>
        public event Action<EstadoCampus> EstadoActualizado;
        /// <summary>Al conectar o perder la conexión con el servicio.</summary>
        public event Action<bool> ConexionCambiada;

        readonly Dictionary<string, EstadoEstancia> porSala = new();
        readonly Dictionary<string, EstadoEstancia> porEspacio = new();
        readonly Dictionary<string, EstadoEdificio> porEdificio = new();
        readonly Dictionary<string, EstadoEdificio> porIdServicio = new();
        string etag;
        bool forzar;

        void Awake()
        {
            if (Instancia != null && Instancia != this) { Destroy(this); return; }
            Instancia = this;
        }

        void OnEnable() => StartCoroutine(Sondear());

        // ───────────────────────── Consultas ─────────────────────────

        /// <summary>Estado de una estancia por su clave del modelo ("1903_P1_24").</summary>
        public bool Estancia(string unityRoom, out EstadoEstancia estado)
        {
            estado = null;
            return unityRoom != null && porSala.TryGetValue(unityRoom, out estado);
        }

        /// <summary>Estado de una estancia por su id del servicio ("INF-P01-AUL024").</summary>
        public bool Espacio(string spaceId, out EstadoEstancia estado)
        {
            estado = null;
            return spaceId != null && porEspacio.TryGetValue(spaceId, out estado);
        }

        /// <summary>Estado de un edificio por su código del modelo ("1903") o del servicio ("INF").</summary>
        public bool Edificio(string codigo, out EstadoEdificio estado)
        {
            estado = null;
            return codigo != null && (porEdificio.TryGetValue(codigo, out estado) || porIdServicio.TryGetValue(codigo, out estado));
        }

        /// <summary>Todas las estancias con estado (las que tienen sala en el modelo).</summary>
        public IEnumerable<EstadoEstancia> Estancias => porSala.Values;

        /// <summary>Pide el estado ya, sin esperar al siguiente sondeo (tras mandar una orden).</summary>
        public void Refrescar() => forzar = true;

        // ───────────────────────── Órdenes ─────────────────────────

        /// <summary>Cambia el reloj de la simulación: "live" o una hora ISO 8601 (se manda en UTC).</summary>
        public void CambiarReloj(string reloj, Action<bool> hecho = null) =>
            Parchear("/api/v1/simulation", $"{{\"clock\":{Json(reloj)}}}", hecho);

        public void CambiarReloj(DateTime horaLocalCampus, Action<bool> hecho = null) =>
            CambiarReloj(HoraCampus.AUtc(horaLocalCampus).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), hecho);

        public void CambiarEscalaTiempo(float escala, Action<bool> hecho = null) =>
            Parchear("/api/v1/simulation", $"{{\"time_scale\":{F(Mathf.Clamp(escala, 0.1f, 3600f))}}}", hecho);

        public void CambiarEscenario(string id, Action<bool> hecho = null) =>
            Parchear("/api/v1/simulation", $"{{\"scenario\":{Json(id)}}}", hecho);

        public void PausarSimulacion(bool pausar, Action<bool> hecho = null) =>
            Parchear("/api/v1/simulation", $"{{\"running\":{(pausar ? "false" : "true")}}}", hecho);

        /// <summary>Inyecta un evento en la simulación. Objetivo: space_id, building_id (INF…), sensor o "campus".
        /// paramsJson: objeto JSON con los parámetros (p. ej. {"people":30}) o null.</summary>
        public void CrearEvento(string tipo, string objetivo, float duracionS = 600, string paramsJson = null, Action<bool> hecho = null)
        {
            string dur = duracionS > 0 ? $",\"duration_s\":{F(duracionS)}" : "";
            string par = string.IsNullOrEmpty(paramsJson) ? "" : $",\"params\":{paramsJson}";
            Enviar("POST", "/api/v1/simulation/events", $"{{\"type\":{Json(tipo)},\"target\":{Json(objetivo)}{dur}{par}}}", hecho);
        }

        public void TerminarEvento(string id, Action<bool> hecho = null) =>
            Enviar("DELETE", $"/api/v1/simulation/events/{id}", null, hecho);

        public void Escenarios(Action<List<Escenario>> listo) =>
            Get("/api/v1/simulation/scenarios", t => listo(t == null ? null : JsonUtility.FromJson<ListaEscenarios>(t).items));

        public void Eventos(Action<List<EventoSimulacion>> listo) =>
            Get("/api/v1/simulation/events", t => listo(t == null ? null : JsonUtility.FromJson<ListaEventos>(t).items));

        /// <summary>GET de una ruta de la API (p. ej. "/api/v1/spaces/INF-P01-AUL024/history?hours=6");
        /// devuelve el cuerpo o null si falla.</summary>
        public void Get(string ruta, Action<string> listo) => StartCoroutine(GetCo(ruta, listo));

        // ───────────────────────── Implementación ─────────────────────────

        IEnumerator Sondear()
        {
            while (enabled)
            {
                using (var req = UnityWebRequest.Get($"{UrlBase}/api/v1/state"))
                {
                    if (etag != null) req.SetRequestHeader("If-None-Match", etag);
                    req.timeout = 10;
                    yield return req.SendWebRequest();

                    bool ok = req.result == UnityWebRequest.Result.Success || req.responseCode == 304;
                    if (ok != Conectado)
                    {
                        Conectado = ok;
                        ConexionCambiada?.Invoke(ok);
                    }
                    if (req.responseCode == 200)
                    {
                        etag = req.GetResponseHeader("ETag");
                        try { Aplicar(JsonUtility.FromJson<EstadoCampus>(req.downloadHandler.text)); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                    else if (!ok)
                    {
                        etag = null;
                        if (UltimoError != req.error) Debug.LogWarning($"[Gemelo] {req.error} ({UrlBase})");
                        UltimoError = req.error;
                    }
                }
                float hasta = Time.realtimeSinceStartup + (Conectado ? intervalo : 3f);
                while (Time.realtimeSinceStartup < hasta && !forzar) yield return null;
                forzar = false;
            }
        }

        void Aplicar(EstadoCampus estado)
        {
            Estado = estado;
            UltimaRecepcion = Time.realtimeSinceStartup;
            UltimoError = null;
            porSala.Clear();
            porEspacio.Clear();
            porEdificio.Clear();
            porIdServicio.Clear();
            foreach (var b in estado.buildings)
            {
                if (!string.IsNullOrEmpty(b.unity_id)) porEdificio[b.unity_id] = b;
                if (!string.IsNullOrEmpty(b.building_id)) porIdServicio[b.building_id] = b;
                foreach (var e in b.spaces)
                {
                    if (!string.IsNullOrEmpty(e.space_id)) porEspacio[e.space_id] = e;
                    if (!string.IsNullOrEmpty(e.unity_room)) porSala[e.unity_room] = e;
                }
            }
            EstadoActualizado?.Invoke(estado);
        }

        void Parchear(string ruta, string json, Action<bool> hecho) => Enviar("PATCH", ruta, json, hecho);

        void Enviar(string metodo, string ruta, string json, Action<bool> hecho) => StartCoroutine(EnviarCo(metodo, ruta, json, hecho));

        IEnumerator EnviarCo(string metodo, string ruta, string json, Action<bool> hecho)
        {
            using var req = new UnityWebRequest(UrlBase + ruta, metodo) { downloadHandler = new DownloadHandlerBuffer() };
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.timeout = 10;
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success;
            if (!ok) Debug.LogWarning($"[Gemelo] {metodo} {ruta}: {req.responseCode} {req.downloadHandler?.text}");
            forzar = true;
            hecho?.Invoke(ok);
        }

        IEnumerator GetCo(string ruta, Action<string> listo)
        {
            using var req = UnityWebRequest.Get(UrlBase + ruta);
            req.timeout = 20;
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success;
            if (!ok) Debug.LogWarning($"[Gemelo] GET {ruta}: {req.responseCode} {req.error}");
            listo?.Invoke(ok ? req.downloadHandler.text : null);
        }

        static string F(float v) => v.ToString(CultureInfo.InvariantCulture);
        static string Json(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
