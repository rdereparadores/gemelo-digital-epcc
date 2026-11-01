using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Build de Linux para medir el rendimiento fuera del editor (Build/Benchmark/Campus.x86_64, ignorado por git).
// Se lanza con -benchmark (ver BenchmarkRendimiento).
public static class ConstruirBenchmark
{
    [MenuItem("Tools/Rendimiento/Build de benchmark (Linux)")]
    public static string Construir()
    {
        PlayerSettings.enableFrameTimingStats = true;
        var opciones = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MainScene.unity" },
            locationPathName = "Build/Benchmark/Campus.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None,
        };
        var r = BuildPipeline.BuildPlayer(opciones);
        var s = r.summary;
        string res = $"{s.result} {s.totalTime.TotalMinutes:F1} min, {s.totalSize / 1048576.0:F0} MB, errores {s.totalErrors}";
        Debug.Log("[Benchmark] build: " + res);
        return res;
    }
}
