// Opciones gráficas del gemelo (Ajustes ▸ Gráficos): antialiasing, sombras del sol, oclusión ambiental, escala de
// render con FSR, distancia de detalle y de la hierba, con preajustes Bajo/Medio/Alto/Ultra. Se aplican al asset de URP
// en uso, a la cámara principal, a QualitySettings y al Terrain, al arrancar y cada vez que cambian los ajustes.
// En el editor el asset de URP y el renderer son archivos del proyecto: se guardan sus valores al empezar y se
// restauran al salir de Play Mode para no dejar el proyecto modificado.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gemelo
{
    public static class Graficos
    {
        public enum Preajuste { Bajo, Medio, Alto, Ultra }

        public static readonly string[] NombresAntialias = { "No", "FXAA", "SMAA", "MSAA 2×", "MSAA 4×" };
        public static readonly string[] NombresSombras = { "No", "Bajas", "Medias", "Altas", "Ultra" };
        public static readonly string[] NombresOclusion = { "No", "Media resolución", "Completa" };

        // Sombras del sol por nivel: distancia (m), cascadas, resolución del mapa, calidad del suavizado (1–3)
        static readonly (float dist, int cascadas, int res, int suave)[] NivelSombras =
        {
            (0f, 1, 1024, 1), (60f, 2, 2048, 1), (100f, 3, 2048, 2), (150f, 4, 2048, 3), (150f, 4, 4096, 3),
        };

        /// <summary>Valores de cada preajuste (antialias, sombras, oclusión, escala, detalle, hierba).</summary>
        public static void Poner(Ajustes a, Preajuste p)
        {
            switch (p)
            {
                case Preajuste.Bajo:  Fijar(a, 1, 1, 0, 0.8f, 1f, 40f); break;
                case Preajuste.Medio: Fijar(a, 2, 2, 1, 1f, 1.5f, 70f); break;
                case Preajuste.Alto:  Fijar(a, 3, 3, 1, 1f, 2f, 90f); break;
                default:              Fijar(a, 4, 4, 2, 1f, 2f, 90f); break;
            }
        }

        static void Fijar(Ajustes a, int aa, int so, int oc, float esc, float det, float hi)
        {
            a.antialias = aa; a.sombras = so; a.oclusion = oc; a.escalaRender = esc; a.detalle = det; a.distanciaHierba = hi;
        }

        /// <summary>Preajuste al que corresponden los valores actuales, o −1 si están cambiados a mano.</summary>
        public static int PreajusteDe(Ajustes a)
        {
            var b = new Ajustes();
            foreach (Preajuste p in Enum.GetValues(typeof(Preajuste)))
            {
                Poner(b, p);
                if (b.antialias == a.antialias && b.sombras == a.sombras && b.oclusion == a.oclusion &&
                    Mathf.Approximately(b.escalaRender, a.escalaRender) && Mathf.Approximately(b.detalle, a.detalle) &&
                    Mathf.Approximately(b.distanciaHierba, a.distanciaHierba)) return (int)p;
            }
            return -1;
        }

        // ───────────────────────── Aplicar ─────────────────────────

        // GemeloApp vacía Arrancado en SubsystemRegistration; los módulos se suscriben después, en BeforeSceneLoad
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Registrar() => GemeloApp.Arrancado += Arrancado;

        static void Arrancado(GemeloApp app)
        {
            Ajustes.Cambiados -= Aplicar;
            Ajustes.Cambiados += Aplicar;
            app.ModoCambiado += _ => Camara(Ajustes.Actual);
            Aplicar(Ajustes.Actual);
        }

        const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        public static void Aplicar(Ajustes a)
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return;
            Guardar(urp);

            urp.msaaSampleCount = a.antialias == 4 ? 4 : a.antialias == 3 ? 2 : 1;

            var s = NivelSombras[Mathf.Clamp(a.sombras, 0, NivelSombras.Length - 1)];
            urp.shadowDistance = s.dist;
            urp.shadowCascadeCount = s.cascadas;
            if (s.cascadas == 2) urp.cascade2Split = 0.3f;
            else if (s.cascadas == 3) urp.cascade3Split = new Vector2(0.12f, 0.35f);
            else if (s.cascadas == 4) urp.cascade4Split = new Vector3(0.05f, 0.16f, 0.45f);
            urp.mainLightShadowmapResolution = s.res;
            CampoAsset(urp, "m_SoftShadowQuality", s.suave);

            float esc = Mathf.Clamp(a.escalaRender, 0.5f, 1f);
            urp.renderScale = esc;
            urp.upscalingFilter = esc < 0.999f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;

            Oclusion(urp, a.oclusion);

            QualitySettings.lodBias = Mathf.Clamp(a.detalle, 0.5f, 3f);
            foreach (var t in Terrain.activeTerrains) t.detailObjectDistance = Mathf.Clamp(a.distanciaHierba, 0f, 150f);
            Camara(a);
        }

        static void Camara(Ajustes a)
        {
            var cam = Camera.main;
            if (cam == null || !cam.TryGetComponent<UniversalAdditionalCameraData>(out var d)) return;
            d.antialiasing = a.antialias == 1 ? AntialiasingMode.FastApproximateAntialiasing
                : a.antialias == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            d.antialiasingQuality = AntialiasingQuality.High;
            cam.allowMSAA = a.antialias >= 3;
        }

        // Oclusión ambiental (renderer feature SSAO del renderer por defecto): apagada, a media resolución o completa
        static void Oclusion(UniversalRenderPipelineAsset urp, int nivel)
        {
            foreach (var f in Caracteristicas(urp))
            {
                if (f == null || !f.GetType().Name.Contains("AmbientOcclusion")) continue;
                f.SetActive(nivel > 0);
                var set = f.GetType().GetField("m_Settings", Priv)?.GetValue(f);
                set?.GetType().GetField("Downsample", Priv)?.SetValue(set, nivel == 1);
            }
        }

        static List<ScriptableRendererFeature> Caracteristicas(UniversalRenderPipelineAsset urp)
        {
            var datos = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Priv)?.GetValue(urp) as ScriptableRendererData[];
            var lista = new List<ScriptableRendererFeature>();
            if (datos != null) foreach (var d in datos) if (d != null) lista.AddRange(d.rendererFeatures);
            return lista;
        }

        static void CampoAsset(UniversalRenderPipelineAsset urp, string campo, int valor)
        {
            var f = typeof(UniversalRenderPipelineAsset).GetField(campo, Priv);
            if (f != null) f.SetValue(urp, Enum.ToObject(f.FieldType, valor));
        }

        // ───────────────────────── Restaurar en el editor ─────────────────────────

#if UNITY_EDITOR
        static Action restaurar;

        static void Guardar(UniversalRenderPipelineAsset urp)
        {
            if (restaurar != null) return;
            int msaa = urp.msaaSampleCount, casc = urp.shadowCascadeCount, res = urp.mainLightShadowmapResolution;
            float dist = urp.shadowDistance, esc = urp.renderScale;
            var c2 = urp.cascade2Split; var c3 = urp.cascade3Split; var c4 = urp.cascade4Split;
            var up = urp.upscalingFilter;
            var fs = typeof(UniversalRenderPipelineAsset).GetField("m_SoftShadowQuality", Priv);
            object suave = fs?.GetValue(urp);
            var ssao = new List<(ScriptableRendererFeature f, bool activa, object set, bool media)>();
            foreach (var f in Caracteristicas(urp))
            {
                if (f == null || !f.GetType().Name.Contains("AmbientOcclusion")) continue;
                var set = f.GetType().GetField("m_Settings", Priv)?.GetValue(f);
                var ds = set?.GetType().GetField("Downsample", Priv);
                ssao.Add((f, f.isActive, set, ds != null && (bool)ds.GetValue(set)));
            }
            restaurar = () =>
            {
                urp.msaaSampleCount = msaa; urp.shadowCascadeCount = casc; urp.mainLightShadowmapResolution = res;
                urp.shadowDistance = dist; urp.renderScale = esc; urp.upscalingFilter = up;
                urp.cascade2Split = c2; urp.cascade3Split = c3; urp.cascade4Split = c4;
                if (fs != null) fs.SetValue(urp, suave);
                foreach (var x in ssao)
                {
                    x.f.SetActive(x.activa);
                    x.set?.GetType().GetField("Downsample", Priv)?.SetValue(x.set, x.media);
                }
            };
            UnityEditor.EditorApplication.playModeStateChanged += AlCambiarModo;
        }

        static void AlCambiarModo(UnityEditor.PlayModeStateChange e)
        {
            if (e != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
            UnityEditor.EditorApplication.playModeStateChanged -= AlCambiarModo;
            restaurar?.Invoke();
            restaurar = null;
        }
#else
        static void Guardar(UniversalRenderPipelineAsset urp) { }
#endif
    }
}
