// Sol, luna, cielo, luz ambiente y niebla según la hora del modelo y el tiempo (nubes y lluvia).
// Parte del ambiente calibrado con fotos (AmbienteBuilder: sol de 2,3 a 5600 K, Trilight ×1,6, niebla de calima,
// cielo HDR) y lo respeta con el sol alto y despejado; con el sol bajo calienta y atenúa la luz, con nubes la
// apaga y agrisa el cielo, y de noche pasa a una luna tenue y azulada, con estrellas. Con
// Ajustes.sincronizarSol = false se deja todo como estaba en la escena.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gemelo
{
    public class CieloYSol : MonoBehaviour
    {
        // Elevación a partir de la cual la luz es la calibrada (mediodía de verano en las fotos)
        const float ElevacionPlena = 35f;
        // Sol del HDR (AmbienteBuilder): acimut 250°, elevación 43°, con el panorama girado 234°
        const float GiroCielo = 234f, AcimutHdr = 250f;

        AmbienteGemelo amb;
        Light luz;
        Material cielo, cieloOriginal;
        Volume volumenNoche;
        Bloom bloomNoche;
        ColorAdjustments ajusteNoche;
        bool aplicado;
        Vector2 desplazamientoNubes;

        // Lo que había en la escena
        Quaternion rotOriginal;
        float intensidadOriginal, temperaturaOriginal, sombraOriginal, exposicionOriginal, niebla, reflejos;
        Color colorLuzOriginal, cieloAmb, ecuadorAmb, sueloAmb, colorNiebla;
        bool usaTemperatura;
        AmbientMode modoAmbiente;

        static readonly int idRotacion = Shader.PropertyToID("_Rotation"), idExposicion = Shader.PropertyToID("_Exposure"),
            idSol = Shader.PropertyToID("_SolDir"), idColorSol = Shader.PropertyToID("_SolColor"), idLuna = Shader.PropertyToID("_LunaDir"),
            idNoche = Shader.PropertyToID("_Noche"), idNubes = Shader.PropertyToID("_Nubes"), idOcaso = Shader.PropertyToID("_Ocaso"),
            idViento = Shader.PropertyToID("_Viento"), idSolHdr = Shader.PropertyToID("_SolHdr"), idSolPropio = Shader.PropertyToID("_SolPropio");

        /// <summary>Dirección de la luna (hacia ella) que usan el cielo y la luz de noche.</summary>
        public Vector3 DireccionLuna { get; private set; }

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            luz = RenderSettings.sun;
            if (luz == null)
                foreach (var l in FindObjectsByType<Light>())
                    if (l.type == LightType.Directional) { luz = l; break; }
            cieloOriginal = RenderSettings.skybox;
            if (luz != null)
            {
                rotOriginal = luz.transform.rotation;
                intensidadOriginal = luz.intensity;
                temperaturaOriginal = luz.colorTemperature;
                usaTemperatura = luz.useColorTemperature;
                colorLuzOriginal = luz.color;
                sombraOriginal = luz.shadowStrength;
            }
            modoAmbiente = RenderSettings.ambientMode;
            cieloAmb = RenderSettings.ambientSkyColor;
            ecuadorAmb = RenderSettings.ambientEquatorColor;
            sueloAmb = RenderSettings.ambientGroundColor;
            colorNiebla = RenderSettings.fogColor;
            niebla = RenderSettings.fogDensity;
            reflejos = RenderSettings.reflectionIntensity;
            exposicionOriginal = cieloOriginal != null && cieloOriginal.HasProperty("_Exposure") ? cieloOriginal.GetFloat("_Exposure") : 1f;

            var sh = Resources.Load<Shader>("Ambiente/GemeloCielo");
            if (sh != null && cieloOriginal != null && cieloOriginal.HasProperty("_MainTex"))
            {
                cielo = new Material(sh) { name = "Cielo (gemelo)" };
                cielo.SetTexture("_MainTex", cieloOriginal.GetTexture("_MainTex"));
                if (cieloOriginal.HasProperty("_Tint")) cielo.SetColor("_Tint", cieloOriginal.GetColor("_Tint"));
                // Dirección del sol del HDR en el espacio de la textura (elevación 43°, u = 0,6006)
                float e = 43f * Mathf.Deg2Rad, a = -0.1006f * 2f * Mathf.PI;
                cielo.SetVector(idSolHdr, new Vector4(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e), 0));
            }

            // Volumen de noche: algo más de exposición (el ojo se adapta) y de bloom para las luces
            var go = new GameObject("Volumen noche");
            go.transform.SetParent(transform, false);
            volumenNoche = go.AddComponent<Volume>();
            volumenNoche.isGlobal = true;
            volumenNoche.priority = 50;
            volumenNoche.weight = 0;
            var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            bloomNoche = perfil.Add<Bloom>(true);
            bloomNoche.intensity.Override(0.55f);
            bloomNoche.threshold.Override(0.9f);
            bloomNoche.scatter.Override(0.65f);
            ajusteNoche = perfil.Add<ColorAdjustments>(true);
            ajusteNoche.postExposure.Override(0.55f);
            volumenNoche.sharedProfile = perfil;
        }

        void OnDisable() => Restaurar();

        void OnDestroy()
        {
            Restaurar();
            if (cielo != null) Destroy(cielo);
        }

        void Restaurar()
        {
            if (!aplicado) return;
            aplicado = false;
            if (luz != null)
            {
                luz.transform.rotation = rotOriginal;
                luz.intensity = intensidadOriginal;
                luz.colorTemperature = temperaturaOriginal;
                luz.useColorTemperature = usaTemperatura;
                luz.color = colorLuzOriginal;
                luz.shadowStrength = sombraOriginal;
            }
            RenderSettings.skybox = cieloOriginal;
            RenderSettings.ambientMode = modoAmbiente;
            RenderSettings.ambientSkyColor = cieloAmb;
            RenderSettings.ambientEquatorColor = ecuadorAmb;
            RenderSettings.ambientGroundColor = sueloAmb;
            RenderSettings.fogColor = colorNiebla;
            RenderSettings.fogDensity = niebla;
            RenderSettings.reflectionIntensity = reflejos;
            if (volumenNoche != null) volumenNoche.weight = 0;
        }

        void LateUpdate()
        {
            if (amb == null || luz == null) return;
            if (!Ajustes.Actual.sincronizarSol)
            {
                Restaurar();
                return;
            }
            aplicado = true;

            float e = amb.ElevacionSol, az = amb.AcimutSol;
            float noche = amb.Noche, nubes = amb.Nubes;
            float lluvia = Mathf.Clamp01(amb.Lluvia / 4f);
            float ocaso = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 3f, e)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, -1f, e));
            float plena = Mathf.Clamp01(Mathf.Sin(Mathf.Max(e, 0f) * Mathf.Deg2Rad) / Mathf.Sin(ElevacionPlena * Mathf.Deg2Rad));
            float cubierto = Mathf.Clamp01(nubes * nubes * (3f - 2f * nubes) + lluvia * 0.3f);

            // ─── Sol o luna
            float luzSol = intensidadOriginal * Mathf.Pow(plena, 0.55f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 4f, e))
                           * (1f - 0.82f * cubierto);
            float azLuna = (az + 180f) % 360f;
            DireccionLuna = Direccion(38f, azLuna);
            if (e > -2f)
            {
                luz.transform.rotation = Quaternion.Euler(Mathf.Max(e, 3f), az - 180f, 0f);
                luz.intensity = luzSol;
                luz.useColorTemperature = true;
                float temp = e >= 30f ? temperaturaOriginal
                           : e >= 10f ? Mathf.Lerp(4500f, temperaturaOriginal, (e - 10f) / 20f)
                           : Mathf.Lerp(2700f, 4500f, Mathf.Clamp01(e / 10f));
                luz.colorTemperature = Mathf.Lerp(temp, 6800f, cubierto * 0.7f);
                luz.color = colorLuzOriginal;
                luz.shadowStrength = sombraOriginal * (1f - 0.7f * cubierto);
            }
            else
            {
                float luna = 0.24f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, -9f, e)) * (1f - 0.75f * cubierto);
                luz.transform.rotation = Quaternion.Euler(38f, azLuna - 180f, 0f);
                luz.intensity = luna;
                luz.useColorTemperature = true;
                luz.colorTemperature = 9000f;
                luz.color = new Color(0.78f, 0.86f, 1f);
                luz.shadowStrength = 0.55f;
            }

            // ─── Luz ambiente (Trilight)
            float kDia = Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 22f, e))) * (1f - 0.18f * cubierto);
            Color cieloD = Gris(cieloAmb, cubierto * 0.75f) * kDia;
            Color ecuadorD = Gris(ecuadorAmb, cubierto * 0.5f) * kDia;
            Color sueloD = Gris(sueloAmb, cubierto * 0.5f) * kDia * (1f - 0.2f * cubierto);
            var calido = new Color(1.25f, 0.88f, 0.68f);
            ecuadorD = Color.Lerp(ecuadorD, ecuadorD * calido, ocaso * 0.8f);
            cieloD = Color.Lerp(cieloD, cieloD * new Color(0.85f, 0.85f, 1.05f), ocaso * 0.6f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(cieloD, new Color(0.075f, 0.090f, 0.150f), noche);
            RenderSettings.ambientEquatorColor = Color.Lerp(ecuadorD, new Color(0.062f, 0.066f, 0.082f), noche);
            RenderSettings.ambientGroundColor = Color.Lerp(sueloD, new Color(0.040f, 0.037f, 0.033f), noche);
            RenderSettings.reflectionIntensity = reflejos * Mathf.Lerp(1f, 0.06f, noche) * (1f - 0.3f * cubierto);

            // ─── Niebla: calima de día, gris con nubes, densa con lluvia; oscura de noche
            Color nieblaD = Gris(colorNiebla, cubierto * 0.8f) * kDia * (1f - 0.15f * cubierto);
            nieblaD = Color.Lerp(nieblaD, nieblaD * calido, ocaso * 0.7f);
            RenderSettings.fogColor = Color.Lerp(nieblaD, new Color(0.022f, 0.026f, 0.036f), noche);
            RenderSettings.fogDensity = niebla * (1f + 1.2f * cubierto * cubierto + 3.5f * lluvia);

            // ─── Cielo
            if (cielo != null)
            {
                RenderSettings.skybox = cielo;
                var ds = Direccion(e, az);
                cielo.SetFloat(idRotacion, Mathf.Repeat(GiroCielo + AcimutHdr - az, 360f));
                cielo.SetFloat(idExposicion, exposicionOriginal * Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 15f, e))));
                cielo.SetVector(idSol, ds);
                cielo.SetColor(idColorSol, Mathf.CorrelatedColorTemperatureToRGB(Mathf.Max(2000f, e >= 10f ? 5600f : Mathf.Lerp(2400f, 5600f, Mathf.Clamp01(e / 10f)))));
                cielo.SetVector(idLuna, DireccionLuna);
                cielo.SetFloat(idNoche, noche);
                cielo.SetFloat(idNubes, Mathf.Clamp01(nubes + lluvia * 0.2f));
                cielo.SetFloat(idOcaso, ocaso);
                cielo.SetFloat(idSolPropio, 1f);
                // Las nubes viajan con el viento (y con la escala de tiempo de la simulación)
                float dirViento = (amb.DireccionViento + 180f) * Mathf.Deg2Rad;
                float paso = amb.Viento * Mathf.Clamp(HoraCampus.Escala, 0f, 120f) * Time.deltaTime / 900f;
                desplazamientoNubes += new Vector2(Mathf.Sin(dirViento), Mathf.Cos(dirViento)) * paso;
                if (desplazamientoNubes.sqrMagnitude > 1e6f) desplazamientoNubes = Vector2.zero;
                cielo.SetVector(idViento, desplazamientoNubes);
            }

            if (volumenNoche != null) volumenNoche.weight = noche;
        }

        static Vector3 Direccion(float elev, float acimut)
        {
            float e = elev * Mathf.Deg2Rad, a = acimut * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        static Color Gris(Color c, float t)
        {
            float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            return Color.Lerp(c, new Color(l, l * 1.01f, l * 1.04f), Mathf.Clamp01(t));
        }
    }
}
