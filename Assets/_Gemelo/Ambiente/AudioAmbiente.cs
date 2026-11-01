// Sonido ambiente: viento y pájaros de día fuera, lluvia (más apagada dentro) y el rumor de la gente del local
// donde está la cámara (GemeloApp.LocalActual), según su ocupación. Volúmenes de Ajustes (general × ambiente).
// Los sonidos se sintetizan al arrancar (SintesisAudio).
using UnityEngine;

namespace Gemelo
{
    public class AudioAmbiente : MonoBehaviour
    {
        AmbienteGemelo amb;
        Lluvia lluvia;
        AudioSource viento, pajaros, agua, rumor;
        AudioLowPassFilter filtroAgua;
        float ocupacion;   // suavizada
        float siguiente;
        float rumorObj;

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            lluvia = GetComponent<Lluvia>();
            viento = Fuente("Viento", SintesisAudio.Viento());
            pajaros = Fuente("Pájaros", SintesisAudio.Pajaros());
            agua = Fuente("Lluvia", SintesisAudio.Lluvia());
            filtroAgua = agua.gameObject.AddComponent<AudioLowPassFilter>();
            rumor = Fuente("Rumor", SintesisAudio.Rumor());
            // Un AudioListener si la escena no tiene (la cámara principal suele traerlo)
            if (FindAnyObjectByType<AudioListener>() == null && Camera.main != null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        AudioSource Fuente(string nombre, AudioClip clip)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.spatialBlend = 0;
            s.volume = 0;
            s.playOnAwake = false;
            s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            return s;
        }

        void Update()
        {
            if (amb == null) return;
            var app = GemeloApp.Instancia;
            var aj = Ajustes.Actual;
            float vol = aj.volumenGeneral * aj.volumenAmbiente;
            bool dentro = app != null && app.LocalActual != null;
            bool cenital = app != null && app.Modo == Modo.Cenital;
            float dt = Time.deltaTime;

            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 0.5f;
                rumorObj = 0f;
                var l = app?.LocalActual;
                var st = AmbienteGemelo.Estado(l);
                if (st != null && !cenital)
                {
                    float occ = st.Valor("occupancy", 0f);
                    // Saturación hacia 1 con unas 40 personas, más deprisa en locales pequeños
                    float dens = occ / Mathf.Max(8f, l.area * 0.35f);
                    rumorObj = Mathf.Clamp01(Mathf.Sqrt(occ / 40f) * 0.7f + dens * 0.4f);
                    // En clase o examen hay poca charla
                    if (l.local.uso == "aula" && occ > 10) rumorObj *= 0.55f;
                }
            }
            ocupacion = Mathf.MoveTowards(ocupacion, rumorObj, dt * 0.5f);

            float dia = 1f - amb.Noche;
            float exterior = dentro ? 0.18f : 1f;
            float alto = cenital ? 0.5f : 1f;
            float vientoV = Mathf.Clamp01(0.25f + amb.Viento / 10f);
            Fijar(viento, vol * 0.35f * vientoV * exterior * alto, dt);
            viento.pitch = 0.8f + 0.04f * amb.Viento;
            Fijar(pajaros, vol * 0.25f * dia * (1f - amb.Nubes * 0.6f) * (amb.Lluvia > 0.1f ? 0.1f : 1f) * exterior * alto, dt);
            float ll = lluvia != null ? lluvia.Intensidad : Mathf.Clamp01(amb.Lluvia / 4f);
            Fijar(agua, vol * 0.55f * ll * (dentro ? 0.45f : 1f), dt);
            if (filtroAgua != null) filtroAgua.cutoffFrequency = dentro ? 1200f : 16000f;
            Fijar(rumor, vol * 0.5f * ocupacion, dt);
        }

        static void Fijar(AudioSource s, float v, float dt)
        {
            if (s == null) return;
            s.volume = Mathf.MoveTowards(s.volume, Mathf.Clamp01(v), dt * 0.6f);
        }
    }
}
