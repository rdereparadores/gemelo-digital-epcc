// Lluvia alrededor de la cámara según precipitation_rate: trazos procedurales en la GPU (Gemelo/Lluvia), con la
// deriva del viento. Dentro de un edificio no se dibuja.
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Lluvia : MonoBehaviour
    {
        const int MaxGotas = 14000;

        AmbienteGemelo amb;
        Material mat;
        Mesh trazo;
        float intensidad;   // 0–1, suavizada

        /// <summary>Intensidad visible de la lluvia ahora (0–1), para el sonido.</summary>
        public float Intensidad => intensidad;
        public bool Dentro { get; private set; }

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            var sh = Resources.Load<Shader>("Ambiente/GemeloLluvia");
            if (sh != null) mat = new Material(sh) { name = "Lluvia (gemelo)" };
            trazo = new Mesh { name = "Trazo de lluvia" };
            trazo.SetVertices(new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) });
            trazo.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            trazo.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (trazo != null) Destroy(trazo);
        }

        void Update()
        {
            if (amb == null || mat == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // 0,5 mm/h llovizna, 4 mm/h lluvia, 15 mm/h chaparrón
            float objetivo = amb.Lluvia < 0.05f ? 0f : Mathf.Clamp01(0.12f + Mathf.Log(1f + amb.Lluvia, 2f) / 4.2f);
            intensidad = Mathf.MoveTowards(intensidad, objetivo, Time.deltaTime * 0.25f);
            var app = GemeloApp.Instancia;
            Dentro = app != null && app.LocalActual != null && app.Modo == Modo.TerceraPersona
                     && RegistroCampus.EdificioEn(cam.transform.position) != null;
            if (intensidad < 0.01f || Dentro) return;

            int n = Mathf.RoundToInt(MaxGotas * intensidad * intensidad);
            float dir = (amb.DireccionViento + 180f) * Mathf.Deg2Rad;
            float v = Mathf.Min(amb.Viento, 12f) * 0.8f;
            mat.SetVector("_Viento", new Vector4(Mathf.Sin(dir) * v, 0, Mathf.Cos(dir) * v, 0));
            // Brillo según la luz del cielo (de noche apenas se ven, salvo junto a las farolas)
            float luz = Mathf.Lerp(0.08f, 0.55f, 1f - amb.Noche) * (0.6f + 0.4f * intensidad);
            mat.SetColor("_Color", new Color(0.75f, 0.8f, 0.86f, luz));
            var rp = new RenderParams(mat)
            {
                worldBounds = new Bounds(cam.transform.position, new Vector3(60, 40, 60)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderMeshPrimitives(rp, trazo, 0, n);
        }
    }
}
