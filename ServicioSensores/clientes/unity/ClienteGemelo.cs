// Cliente del servicio REST del gemelo: sondea GET /api/v1/state con ETag (un 304 no cuesta nada)
// y publica el estado por estancia del modelo (clave unity_room, p. ej. "1903_P1_24").
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Gemelo
{
    public class ClienteGemelo : MonoBehaviour
    {
        public static ClienteGemelo Instancia { get; private set; }

        [Tooltip("URL base del servicio, sin barra final")]
        public string urlBase = "http://127.0.0.1:8800";
        [Tooltip("Segundos entre sondeos")]
        [Min(0.5f)] public float intervalo = 2f;

        public EstadoCampus Estado { get; private set; }
        public bool Conectado { get; private set; }

        /// <summary>Se lanza cada vez que llega un estado nuevo (no en los 304).</summary>
        public event Action<EstadoCampus> EstadoActualizado;

        readonly Dictionary<string, EstadoEstancia> porSala = new();
        readonly Dictionary<string, EstadoEdificio> porEdificio = new();
        string etag;

        void Awake()
        {
            if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
            Instancia = this;
        }

        void OnEnable() => StartCoroutine(Sondear());

        /// <summary>Estado de una estancia por su clave del modelo ("1903_P1_24").</summary>
        public bool Estancia(string unityRoom, out EstadoEstancia estado) => porSala.TryGetValue(unityRoom, out estado);

        /// <summary>Estado de un edificio por su id del modelo ("1903").</summary>
        public bool Edificio(string unityId, out EstadoEdificio estado) => porEdificio.TryGetValue(unityId, out estado);

        IEnumerator Sondear()
        {
            var espera = new WaitForSecondsRealtime(intervalo);
            while (enabled)
            {
                using (var req = UnityWebRequest.Get($"{urlBase}/api/v1/state"))
                {
                    if (etag != null) req.SetRequestHeader("If-None-Match", etag);
                    req.timeout = 10;
                    yield return req.SendWebRequest();

                    Conectado = req.result == UnityWebRequest.Result.Success || req.responseCode == 304;
                    if (req.responseCode == 200)
                    {
                        etag = req.GetResponseHeader("ETag");
                        Aplicar(JsonUtility.FromJson<EstadoCampus>(req.downloadHandler.text));
                    }
                    else if (!Conectado)
                    {
                        Debug.LogWarning($"[Gemelo] {req.error} ({urlBase})");
                    }
                }
                yield return espera;
            }
        }

        void Aplicar(EstadoCampus estado)
        {
            Estado = estado;
            porSala.Clear();
            porEdificio.Clear();
            foreach (var b in estado.buildings)
            {
                if (!string.IsNullOrEmpty(b.unity_id)) porEdificio[b.unity_id] = b;
                foreach (var e in b.spaces)
                    if (!string.IsNullOrEmpty(e.unity_room)) porSala[e.unity_room] = e;
            }
            EstadoActualizado?.Invoke(estado);
        }

        /// <summary>Inyecta un evento en la simulación (p. ej. abrir las ventanas de una sala).</summary>
        public void CrearEvento(string tipo, string objetivo, float duracionS = 600) =>
            StartCoroutine(Post("/api/v1/simulation/events",
                $"{{\"type\":\"{tipo}\",\"target\":\"{objetivo}\",\"duration_s\":{duracionS.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"));

        IEnumerator Post(string ruta, string json)
        {
            using var req = new UnityWebRequest(urlBase + ruta, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[Gemelo] POST {ruta}: {req.responseCode} {req.downloadHandler.text}");
        }
    }
}
