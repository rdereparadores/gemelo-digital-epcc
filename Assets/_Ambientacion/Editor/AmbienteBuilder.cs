using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Iluminación y atmósfera del campus: cielo HDRI (CC0, Poly Haven), sol de tarde de verano en
// Cáceres, niebla, postprocesado, oclusión ambiental y ajustes de sombras y distancias.
public static class AmbienteBuilder
{
    const string Cielo = "Assets/_Ambientacion/Ambiente/cielo.hdr";
    const string MatCielo = "Assets/_Ambientacion/Ambiente/Cielo.mat";
    const string Perfil = "Assets/_Ambientacion/Ambiente/Campus_Volume.asset";

    // Sol de una tarde de julio hacia las 17:00 en Cáceres: azimut ~250° (OSO), elevación ~43°,
    // que es la del sol del HDRI, así que el cielo y la luz cuadran
    const float ElevacionSol = 43f, AzimutSol = 250f;
    // Giro del panorama para llevar el sol del HDRI (u = 0.6006) al azimut del sol
    const float GiroCielo = 234f;    // ajustado con un render mirando al sol

    [MenuItem("Tools/Ambiente/Aplicar cielo, luz y postprocesado")]
    public static void Build()
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(Cielo);
        if (imp.textureShape != TextureImporterShape.Texture2D || imp.maxTextureSize != 2048)
        {
            imp.textureShape = TextureImporterShape.Texture2D;
            imp.maxTextureSize = 2048;
            imp.mipmapEnabled = false;
            imp.wrapModeU = TextureWrapMode.Repeat;
            imp.wrapModeV = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        var cielo = AssetDatabase.LoadAssetAtPath<Material>(MatCielo);
        if (cielo == null)
        {
            cielo = new Material(Shader.Find("Skybox/Panoramic"));
            AssetDatabase.CreateAsset(cielo, MatCielo);
        }
        cielo.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Cielo));
        cielo.SetFloat("_Mapping", 1f);           // latitud-longitud
        cielo.SetFloat("_ImageType", 0f);         // 360°
        cielo.SetFloat("_Rotation", GiroCielo);
        cielo.SetFloat("_Exposure", 1.0f);
        EditorUtility.SetDirty(cielo);

        // Sol
        var luz = Object.FindObjectsByType<Light>().First(l => l.type == LightType.Directional);
        luz.transform.rotation = Quaternion.Euler(ElevacionSol, AzimutSol - 180f, 0f);
        luz.useColorTemperature = true;
        luz.colorTemperature = 5600f;
        luz.color = Color.white;
        luz.intensity = 2.3f;
        luz.shadows = LightShadows.Soft;
        luz.shadowStrength = 0.92f;
        luz.shadowBias = 0.05f;
        luz.shadowNormalBias = 0.4f;

        // Cielo, luz ambiente y niebla de calima
        RenderSettings.skybox = cielo;
        RenderSettings.sun = luz;
        // Luz ambiente de tres tonos en vez de la del HDR: el cielo HDR, con el sol de tarde, dejaba las
        // caras este y la luz de suelo casi a cero (fachadas en sombra azul marino); así hay cielo,
        // horizonte y rebote cálido del suelo, como en las fotos a pleno sol
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.61f, 0.58f);
        RenderSettings.ambientGroundColor = new Color(0.46f, 0.41f, 0.34f);
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0009f;
        RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.84f);
        DynamicGI.UpdateEnvironment();

        AjustarPipeline();
        AjustarVolumen();
        AjustarCamara();
        AjustarTerreno();
        MaterialesCampus.Build();
        Entorno.Colocar("Assets/_Ambientacion/Ambiente/horizonte.mcam", "Horizonte");

        Entorno.Guardar();
        Debug.Log("[Ambiente] Cielo, sol, niebla y postprocesado aplicados");
    }

    static void AjustarPipeline()
    {
        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp == null) return;
        rp.shadowDistance = 150f;
        rp.shadowCascadeCount = 4;
        rp.cascade4Split = new Vector3(0.05f, 0.16f, 0.45f);
        rp.mainLightShadowmapResolution = 4096;
        rp.supportsHDR = true;
        rp.msaaSampleCount = 4;
        EditorUtility.SetDirty(rp);

        // Oclusión ambiental en el renderer
        var datos = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        if (datos != null && !datos.rendererFeatures.Any(f => f is ScreenSpaceAmbientOcclusion))
        {
            var ao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ao, datos);
            datos.rendererFeatures.Add(ao);
            datos.SetDirty();
            EditorUtility.SetDirty(datos);
        }
    }

    static void AjustarVolumen()
    {
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Perfil);
        if (perfil == null)
        {
            perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(perfil, Perfil);
        }
        T Comp<T>() where T : VolumeComponent
        {
            if (!perfil.TryGet(out T c))
            {
                c = perfil.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, perfil);
            }
            return c;
        }
        var tono = Comp<Tonemapping>();
        tono.mode.Override(TonemappingMode.ACES);
        var color = Comp<ColorAdjustments>();
        color.postExposure.Override(-0.1f);
        color.contrast.Override(8f);
        color.saturation.Override(-6f);
        var balance = Comp<WhiteBalance>();
        balance.temperature.Override(6f);       // luz cálida y seca de verano
        var bloom = Comp<Bloom>();
        bloom.intensity.Override(0.15f);
        bloom.threshold.Override(1.1f);
        var viñeta = Comp<Vignette>();
        viñeta.intensity.Override(0.15f);
        EditorUtility.SetDirty(perfil);

        var vol = Object.FindObjectsByType<Volume>().FirstOrDefault(v => v.isGlobal);
        if (vol == null) vol = new GameObject("Global Volume").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = perfil;
    }

    static void AjustarCamara()
    {
        foreach (var cam in Object.FindObjectsByType<Camera>())
        {
            cam.farClipPlane = 2000f;
            cam.nearClipPlane = 0.1f;
            var datos = cam.GetUniversalAdditionalCameraData();
            datos.renderPostProcessing = true;
            datos.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            datos.antialiasingQuality = AntialiasingQuality.High;
            EditorUtility.SetDirty(cam);
        }
    }

    static void AjustarTerreno()
    {
        var t = Terrain.activeTerrain;
        if (t == null) return;
        t.heightmapPixelError = 2f;
        t.basemapDistance = 400f;
        t.treeDistance = 2000f;
        t.detailObjectDistance = 90f;
        t.detailObjectDensity = 1f;
        t.shadowCastingMode = ShadowCastingMode.TwoSided;
        EditorUtility.SetDirty(t);
    }
}
