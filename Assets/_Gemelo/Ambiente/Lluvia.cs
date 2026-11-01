// Lluvia alrededor de la cámara según precipitation_rate: trazos procedurales en la GPU (Gemelo/Lluvia), con la
// deriva del viento.
//
// No llueve bajo techo: la primera vez que hace falta se hornea un mapa de altura del techo (la Y de la superficie
// más alta de cada punto, vista desde arriba, a 0,2 m por texel) con toda la geometría del campus alrededor de los
// edificios; el sombreador de las gotas descarta las que quedan por debajo. Así no llueve dentro de los edificios
// ni bajo porches, pasarelas, voladizos o pérgolas, pero desde dentro se ve llover por las ventanas y sí llueve en
// los patios descubiertos. La copia en la CPU del mapa sirve para saber si la cámara está a cubierto (sonido).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Lluvia : MonoBehaviour
    {
        const int MaxGotas = 14000;
        const float Texel = 0.2f;          // m por texel del mapa de techo
        const float Margen = 70f;          // m alrededor de los edificios
        const float SinTecho = -1000f;

        AmbienteGemelo amb;
        Material mat;
        Mesh trazo;
        float intensidad;   // 0–1, suavizada

        // Mapa de altura del techo
        enum EstadoMapa { Sin, Horneando, Listo, Fallo }
        EstadoMapa estadoMapa;
        Texture2D mapaTex;
        ushort[] mapa;          // half, fila 0 = zmin
        int mapaAncho, mapaFondo;
        Vector4 mapaRect;       // xmin, zmin, 1/ancho, 1/fondo (m)
        RenderTexture mapaRT;
        float siguienteCubierto;

        /// <summary>Intensidad visible de la lluvia ahora (0–1), para el sonido.</summary>
        public float Intensidad => intensidad;
        /// <summary>La cámara está bajo techo (dentro de un edificio, en un porche, bajo una pasarela…).</summary>
        public bool Cubierto { get; private set; }
        /// <summary>La cámara está bajo techo y dentro de un local de un edificio.</summary>
        public bool Dentro { get; private set; }
        /// <summary>Tiempo que costó hornear el mapa de techo (ms, CPU), para las pruebas.</summary>
        public float MsHorneado { get; private set; }
        public bool MapaListo => estadoMapa == EstadoMapa.Listo;

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
            if (mapaTex != null) Destroy(mapaTex);
            if (mapaRT != null) { mapaRT.Release(); Destroy(mapaRT); }
        }

        void Update()
        {
            if (amb == null || mat == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // 0,5 mm/h llovizna, 4 mm/h lluvia, 15 mm/h chaparrón
            float objetivo = amb.Lluvia < 0.05f ? 0f : Mathf.Clamp01(0.12f + Mathf.Log(1f + amb.Lluvia, 2f) / 4.2f);
            if (objetivo > 0f && estadoMapa == EstadoMapa.Sin) HornearMapa();
            // Sin mapa todavía (unos pocos frames tras hornearlo) no se dibuja: no entra lluvia en los edificios
            if (estadoMapa == EstadoMapa.Horneando) objetivo = 0f;
            intensidad = Mathf.MoveTowards(intensidad, objetivo, Time.deltaTime * 0.25f);

            if (Time.unscaledTime >= siguienteCubierto)
            {
                siguienteCubierto = Time.unscaledTime + 0.2f;
                var p = cam.transform.position;
                Cubierto = CamaraCubierta(p);
                // (por los locales, no por la caja del edificio, que abarca también pasarelas y porches)
                Dentro = Cubierto && RegistroCampus.LocalEn(p) != null;
            }
            if (intensidad < 0.01f) return;
            // Sin mapa (no se pudo hacer): como antes, nada de lluvia con la cámara en un edificio
            if (estadoMapa == EstadoMapa.Fallo && RegistroCampus.EdificioEn(cam.transform.position) != null) return;

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

        // ─── Mapa de altura del techo

        /// <summary>Altura (Y del mundo) de la superficie más alta sobre el punto, o -1000 si no hay nada o
        /// fuera del mapa (o si aún no está hecho).</summary>
        public float AlturaTecho(float x, float z)
        {
            if (estadoMapa != EstadoMapa.Listo) return SinTecho;
            int ix = Mathf.FloorToInt((x - mapaRect.x) * mapaRect.z * mapaAncho);
            int iz = Mathf.FloorToInt((z - mapaRect.y) * mapaRect.w * mapaFondo);
            if (ix < 0 || iz < 0 || ix >= mapaAncho || iz >= mapaFondo) return SinTecho;
            return Mathf.HalfToFloat(mapa[iz * mapaAncho + ix]);
        }

        /// <summary>¿Hay techo (algo por encima) sobre el punto?</summary>
        public bool BajoTecho(Vector3 p) => p.y < AlturaTecho(p.x, p.z) - 0.05f;

        // Para el sonido: a cubierto si el punto y sus vecinos a 0,6 m lo están (una farola no es un techo)
        bool CamaraCubierta(Vector3 p)
        {
            if (!BajoTecho(p)) return false;
            const float d = 0.6f;
            return BajoTecho(p + new Vector3(d, 0, 0)) && BajoTecho(p - new Vector3(d, 0, 0))
                && BajoTecho(p + new Vector3(0, 0, d)) && BajoTecho(p - new Vector3(0, 0, d));
        }

        void HornearMapa()
        {
            estadoMapa = EstadoMapa.Fallo;
            var sh = Resources.Load<Shader>("Ambiente/GemeloAlturaTecho");
            if (sh == null || RegistroCampus.Edificios == null || RegistroCampus.Edificios.Count == 0
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Blend))
            {
                Debug.LogWarning("[Lluvia] sin mapa de techo: la lluvia no se dibuja");
                return;
            }
            var t0 = Time.realtimeSinceStartup;

            // Rectángulo: los edificios y un margen (porches, pasarelas, pérgolas del aparcamiento, paradas…)
            Bounds b = default;
            bool primero = true;
            foreach (var e in RegistroCampus.Edificios)
            {
                if (primero) { b = e.limites; primero = false; }
                else b.Encapsulate(e.limites);
            }
            float x0 = b.min.x - Margen, z0 = b.min.z - Margen;
            mapaAncho = Mathf.Min(4096, Mathf.CeilToInt((b.size.x + 2 * Margen) / Texel));
            mapaFondo = Mathf.Min(4096, Mathf.CeilToInt((b.size.z + 2 * Margen) / Texel));
            mapaRect = new Vector4(x0, z0, 1f / (mapaAncho * Texel), 1f / (mapaFondo * Texel));
            var rect = new Rect(x0, z0, mapaAncho * Texel, mapaFondo * Texel);

            // Solo el LOD 0 de cada grupo
            var lodsBajos = new HashSet<Renderer>();
            foreach (var g in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) lodsBajos.Add(r);
            }
            var app = GemeloApp.Instancia;
            Transform excluir1 = app != null ? app.transform : null;
            Transform excluir2 = app != null && app.Jugador != null ? app.Jugador.root : null;

            mapaRT = new RenderTexture(mapaAncho, mapaFondo, GraphicsFormat.R16_SFloat, GraphicsFormat.None)
            { name = "Mapa de techo (lluvia)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mapaRT.Create();
            var m = new Material(sh);
            m.SetVector("_MapaRect", mapaRect);
            m.SetFloat("_Volteo", SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            var cmd = new CommandBuffer { name = "Mapa de techo (lluvia)" };
            cmd.SetRenderTarget(mapaRT);
            cmd.ClearRenderTarget(false, true, new Color(SinTecho, 0, 0, 0));
            int dibujos = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!r.enabled || lodsBajos.Contains(r)) continue;
                var tr = r.transform;
                if (excluir1 != null && tr.IsChildOf(excluir1)) continue;
                if (excluir2 != null && tr.IsChildOf(excluir2)) continue;
                // Las plantas ocultas por el corte de la vista cenital también cuentan
                if (!r.gameObject.activeInHierarchy && r.GetComponentInParent<EdificioPolitecnica>(true) == null) continue;
                var rb = r.bounds;
                if (rb.size.x > 500f || rb.size.z > 500f) continue;   // horizonte
                if (!rect.Overlaps(new Rect(rb.min.x, rb.min.z, rb.size.x, rb.size.z))) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                int subs = Mathf.Min(mesh.subMeshCount, Mathf.Max(1, r.sharedMaterials.Length));
                if (r.isPartOfStaticBatch)   // (el proyecto no usa lotes estáticos; por si acaso)
                    for (int s = 0; s < subs; s++) cmd.DrawRenderer(r, m, s, 0);
                else
                    for (int s = 0; s < subs; s++) cmd.DrawMesh(mesh, r.localToWorldMatrix, m, s, 0);
                dibujos++;
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            Destroy(m);
            estadoMapa = EstadoMapa.Horneando;
            AsyncGPUReadback.Request(mapaRT, 0, TextureFormat.RHalf, Leido);
            MsHorneado = (Time.realtimeSinceStartup - t0) * 1000f;
            Debug.Log($"[Lluvia] mapa de techo {mapaAncho}×{mapaFondo} ({dibujos} mallas) en {MsHorneado:0} ms");
        }

        void Leido(AsyncGPUReadbackRequest req)
        {
            if (this == null) return;
            if (mapaRT != null) { mapaRT.Release(); Destroy(mapaRT); mapaRT = null; }
            if (req.hasError || mat == null)
            {
                estadoMapa = EstadoMapa.Fallo;
                Debug.LogWarning("[Lluvia] no se pudo leer el mapa de techo: la lluvia no se dibuja");
                return;
            }
            mapa = req.GetData<ushort>().ToArray();
            mapaTex = new Texture2D(mapaAncho, mapaFondo, TextureFormat.RHalf, false, true)
            { name = "Mapa de techo (lluvia)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mapaTex.SetPixelData(mapa, 0);
            mapaTex.Apply(false, true);
            mat.SetTexture("_MapaTecho", mapaTex);
            mat.SetVector("_MapaRect", mapaRect);
            estadoMapa = EstadoMapa.Listo;
        }
    }
}
