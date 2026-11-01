using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Benchmark de rendimiento reproducible. Solo se activa si el ejecutable se lanza con -benchmark
// (o desde el editor con BenchmarkRendimiento.Lanzar()): aparca al jugador, pone la cámara principal en una
// serie de vistas fijas (exteriores, aulas, pasillos, halls) y en cada una mide tiempos de CPU/GPU, llamadas de
// dibujo y memoria. Escribe un JSON y una captura por vista para comparar la calidad visual antes y después
// de cada optimización.
//   ./Campus.x86_64 -benchmark -benchmarkOut /ruta/salida -screen-width 1920 -screen-height 1080 -screen-fullscreen 0
public class BenchmarkRendimiento : MonoBehaviour
{
    struct Vista { public string nombre; public Vector3 pos, objetivo; }

    // Mundo (m). Interiores: desde una esquina del local mirando a su centro, a 1,6 m del suelo.
    static readonly Vista[] Vistas =
    {
        V("aereo", 300, 95, 420, 400, 18, 530),
        V("fachada_sur_comun", 395, 21.7f, 458, 360, 24, 495),
        V("plaza_este", 440, 18.0f, 515, 470, 22, 560),
        V("oeste_largo", 250, 14.6f, 500, 420, 20, 520),
        V("cafeteria_1901", 405.5f, 18.2f, 491, 419, 17.6f, 478),
        V("hall_1903", 405, 18.3f, 530, 425, 18, 565),
        V("aula_1901_P1_08", 388.04f, 22.2f, 484.94f, 382.45f, 21.8f, 492.44f),
        V("aula_pc_1901_P0_22", 336.15f, 18.2f, 511.78f, 331.82f, 17.8f, 506.5f),
        V("aula_pc_1903_P1_24", 426.25f, 22.2f, 577.91f, 432.93f, 21.8f, 580.02f),
        V("pasillo_1901_P2_22", 377.0f, 26.15f, 502.26f, 378.44f, 25.75f, 501.74f),
    };

    static Vista V(string n, float x, float y, float z, float tx, float ty, float tz) =>
        new Vista { nombre = n, pos = new Vector3(x, y, z), objetivo = new Vector3(tx, ty, tz) };

    public int framesCalentar = 120;
    public int framesMedir = 300;
    public string salida;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Arrancar()
    {
        var args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-benchmark") < 0) return;
        string outDir = Path.Combine(Application.persistentDataPath, "benchmark");
        int i = System.Array.IndexOf(args, "-benchmarkOut");
        if (i >= 0 && i + 1 < args.Length) outDir = args[i + 1];
        Lanzar(outDir);
    }

    // Lo que tiene que estar puesto antes de cargar la escena (el streaming decide al cargar qué mips sube)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AntesDeCargar()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") < 0) return;
        string a;
        if ((a = Arg("-streaming")) != null) QualitySettings.streamingMipmapsActive = a == "1";
        if ((a = Arg("-presupuesto")) != null) QualitySettings.streamingMipmapsMemoryBudget = float.Parse(a, System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Arg(string nombre)
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, nombre);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Ajustes que se pueden cambiar sin recompilar, para medir cuánto cuesta cada cosa:
    //   -grd 0|1 (GPU Resident Drawer), -gpuocc 0|1 (oclusión por GPU), -msaa 1|2|4|8, -ssao 0|1,
    //   -sombras <distancia>, -cascadas 1..4, -sombraRes <px>, -escala <renderScale>, -streaming 0|1
    static string AplicarAjustes()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var hecho = new StringBuilder();
        if (urp == null) return "";
        string a;
        if ((a = Arg("-grd")) != null) urp.gpuResidentDrawerMode = a == "1" ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
        if ((a = Arg("-gpuocc")) != null) urp.gpuResidentDrawerEnableOcclusionCullingInCameras = a == "1";
        if ((a = Arg("-msaa")) != null) urp.msaaSampleCount = int.Parse(a);
        if ((a = Arg("-sombras")) != null) urp.shadowDistance = float.Parse(a, ci);
        if ((a = Arg("-cascadas")) != null) urp.shadowCascadeCount = int.Parse(a);
        if ((a = Arg("-sombraRes")) != null) urp.mainLightShadowmapResolution = int.Parse(a);
        if ((a = Arg("-escala")) != null) urp.renderScale = float.Parse(a, ci);
        if ((a = Arg("-streaming")) != null) QualitySettings.streamingMipmapsActive = a == "1";
        if ((a = Arg("-ssao")) != null)
        {
            var renderer = urp.GetRenderer(0);
            var prop = typeof(ScriptableRenderer).GetProperty("rendererFeatures",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop?.GetValue(renderer) is List<ScriptableRendererFeature> fs)
                foreach (var f in fs) if (f.name.Contains("Ambient")) f.SetActive(a == "1");
        }
        const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if ((a = Arg("-ssaoDepth")) == "1")
        {
            var prop = typeof(ScriptableRenderer).GetProperty("rendererFeatures", priv);
            if (prop?.GetValue(urp.GetRenderer(0)) is List<ScriptableRendererFeature> fs)
                foreach (var f in fs)
                {
                    var set = f.GetType().GetField("m_Settings", priv)?.GetValue(f);
                    var src = set?.GetType().GetField("Source", priv | System.Reflection.BindingFlags.Public);
                    if (src != null) { src.SetValue(set, System.Enum.ToObject(src.FieldType, 0)); hecho.Append("ssaoSource=Depth "); }
                }
        }
        if ((a = Arg("-depthPriming")) != null)
        {
            var fld = typeof(UniversalRenderer).GetField("m_DepthPrimingMode", priv);
            if (fld != null) { fld.SetValue(urp.GetRenderer(0), System.Enum.ToObject(fld.FieldType, int.Parse(a))); hecho.Append("depthPriming=" + a + " "); }
            else hecho.Append("depthPriming=NO ");
        }
        if ((a = Arg("-sombrasMuebles")) == "0")
        {
            int n = 0;
            foreach (var t in FindObjectsByType<Transform>())
                if (t.name == "Mobiliario")
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; n++; }
            hecho.Append($"sombrasMuebles=0({n}) ");
        }
        if ((a = Arg("-luces")) == "0")
            foreach (var l in FindObjectsByType<Light>()) if (l.type != LightType.Directional) l.gameObject.SetActive(false);
        if ((a = Arg("-hierba")) != null)
            foreach (var t in Terrain.activeTerrains) t.detailObjectDistance = float.Parse(a, ci);
        if (Arg("-luces") != null || Arg("-hierba") != null) hecho.AppendFormat("luces={0} hierba={1} ", Arg("-luces") ?? "def", Arg("-hierba") ?? "def");
        hecho.AppendFormat(ci, "grd={0} gpuocc={1} msaa={2} sombras={3} cascadas={4} sombraRes={5} escala={6} streaming={7} ssao={8} presupuesto={9}",
            urp.gpuResidentDrawerMode, urp.gpuResidentDrawerEnableOcclusionCullingInCameras, urp.msaaSampleCount,
            urp.shadowDistance, urp.shadowCascadeCount, urp.mainLightShadowmapResolution, urp.renderScale,
            QualitySettings.streamingMipmapsActive, Arg("-ssao") ?? "def", QualitySettings.streamingMipmapsMemoryBudget);
        return hecho.ToString();
    }

    public static void Lanzar(string outDir)
    {
        var go = new GameObject("BenchmarkRendimiento");
        var b = go.AddComponent<BenchmarkRendimiento>();
        b.salida = outDir;
    }

    static readonly string[] ContadoresRender =
    {
        "SetPass Calls Count", "Draw Calls Count", "Batches Count", "Triangles Count", "Vertices Count",
        "Shadow Casters Count", "Visible Skinned Meshes Count",
    };
    static readonly string[] ContadoresMemoria =
    {
        "Total Used Memory", "Total Reserved Memory", "GC Used Memory", "Gfx Used Memory", "Gfx Reserved Memory",
        "Texture Memory", "Mesh Memory", "System Used Memory", "Texture Count", "Mesh Count", "Material Count",
    };

    IEnumerator Start()
    {
        Directory.CreateDirectory(salida);
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        float tCarga = Time.realtimeSinceStartup;
        string ajustes = AplicarAjustes();

        var cam = Camera.main;
        // El jugador se queda quieto: sin controlador ni gravedad que muevan la cámara
        foreach (var fp in FindObjectsByType<FirstPersonController>()) fp.enabled = false;
        foreach (var cc in FindObjectsByType<CharacterController>()) cc.enabled = false;
        Cursor.lockState = CursorLockMode.None;

        var rec = new Dictionary<string, ProfilerRecorder>();
        foreach (var n in ContadoresRender) rec[n] = ProfilerRecorder.StartNew(ProfilerCategory.Render, n);
        foreach (var n in ContadoresMemoria) rec[n] = ProfilerRecorder.StartNew(ProfilerCategory.Memory, n);

        var timings = new FrameTiming[1];
        var json = new StringBuilder();
        json.Append("{\n");
        json.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
            "  \"dispositivo\": \"{0}\", \"api\": \"{1}\", \"resolucion\": \"{2}x{3}\", \"calidad\": \"{4}\", \"carga_s\": {5:F2}, \"ajustes\": \"{6}\",\n",
            SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceType, Screen.width, Screen.height,
            QualitySettings.names[QualitySettings.GetQualityLevel()], tCarga, ajustes);
        json.Append("  \"vistas\": [\n");

        for (int v = 0; v < Vistas.Length; v++)
        {
            var vista = Vistas[v];
            cam.transform.SetPositionAndRotation(vista.pos, Quaternion.LookRotation(vista.objetivo - vista.pos));
            for (int f = 0; f < framesCalentar; f++) yield return null;

            var frame = new List<double>(); var cpu = new List<double>(); var main = new List<double>();
            var render = new List<double>(); var gpu = new List<double>();
            var sumas = new Dictionary<string, double>();
            for (int f = 0; f < framesMedir; f++)
            {
                yield return null;
                frame.Add(Time.unscaledDeltaTime * 1000.0);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    cpu.Add(timings[0].cpuFrameTime); main.Add(timings[0].cpuMainThreadFrameTime);
                    render.Add(timings[0].cpuRenderThreadFrameTime); gpu.Add(timings[0].gpuFrameTime);
                }
                foreach (var n in ContadoresRender)
                    if (rec[n].Valid) sumas[n] = (sumas.TryGetValue(n, out var s) ? s : 0) + rec[n].LastValue;
            }

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(salida, $"{v:D2}_{vista.nombre}.png"));
            yield return null;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            // Comprobación de colisiones (mallas no legibles con collider precocinado): suelo bajo la cámara
            string suelo = Physics.Raycast(vista.pos, Vector3.down, out var hit, 200f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                ? $"{hit.collider.GetType().Name}:{hit.collider.name}@{hit.distance:F2}" : "nada";
            json.AppendFormat(ci, "    {{\"vista\": \"{0}\", \"fps_medio\": {1:F1}, \"frame_ms\": {2}, \"cpu_ms\": {3}, \"main_ms\": {4}, \"render_ms\": {5}, \"gpu_ms\": {6}",
                vista.nombre, 1000.0 / Media(frame), Est(frame), Est(cpu), Est(main), Est(render), Est(gpu));
            json.AppendFormat(", \"suelo\": \"{0}\"", suelo.Replace("\"", "'"));
            foreach (var n in ContadoresRender)
                if (sumas.ContainsKey(n)) json.AppendFormat(ci, ", \"{0}\": {1:F0}", n, sumas[n] / framesMedir);
            foreach (var n in ContadoresMemoria)
                if (rec[n].Valid) json.AppendFormat(ci, ", \"{0}\": {1:F1}", n,
                    n.EndsWith("Count") ? rec[n].LastValue : rec[n].LastValue / 1048576.0);
            // Memoria de texturas (disponible también en builds sin desarrollo)
            json.AppendFormat(ci, ", \"tex_actual_MB\": {0:F0}, \"tex_deseada_MB\": {1:F0}, \"tex_total_MB\": {2:F0}",
                Texture.currentTextureMemory / 1048576.0, Texture.desiredTextureMemory / 1048576.0, Texture.totalTextureMemory / 1048576.0);
            json.Append(v < Vistas.Length - 1 ? "},\n" : "}\n");
            Debug.Log($"[Benchmark] {vista.nombre}: {1000.0 / Media(frame):F1} fps");
        }
        json.Append("  ]\n}\n");
        File.WriteAllText(Path.Combine(salida, "benchmark.json"), json.ToString());
        foreach (var r in rec.Values) r.Dispose();
        Debug.Log("[Benchmark] terminado: " + salida);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static double Media(List<double> l) { if (l.Count == 0) return 0; double s = 0; foreach (var x in l) s += x; return s / l.Count; }

    // {mediana, p95}
    static string Est(List<double> l)
    {
        if (l.Count == 0) return "null";
        var c = new List<double>(l); c.Sort();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:F2}, {1:F2}]",
            c[c.Count / 2], c[Mathf.Min(c.Count - 1, (int)(c.Count * 0.95))]);
    }
}
