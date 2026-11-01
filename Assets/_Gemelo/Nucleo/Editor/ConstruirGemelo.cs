// Build de Linux del gemelo (Build/Linux/, ignorado por git). La app necesita el servicio de sensores en marcha
// (por defecto http://127.0.0.1:8800; la URL se cambia en los ajustes): `cd ServicioSensores && uv run gemelo-sensores`.
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gemelo
{
    public static class ConstruirGemelo
    {
        public const string Carpeta = "Build/Linux";
        public const string Ejecutable = "GemeloPolitecnica.x86_64";

        [MenuItem("Tools/Gemelo/Build de Linux")]
        static void Menu() => Debug.Log("[Gemelo] build: " + Construir());

        public static string Construir()
        {
            // El servicio va por HTTP en local: sin esto UnityWebRequest lo rechaza en el reproductor
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            var opciones = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MainScene.unity" },
                locationPathName = $"{Carpeta}/{Ejecutable}",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            };
            var r = BuildPipeline.BuildPlayer(opciones);
            var s = r.summary;
            if (s.result == BuildResult.Succeeded)
                File.WriteAllText($"{Carpeta}/LEEME.txt",
                    "Gemelo digital de la Escuela Politécnica de Cáceres (UEx)\n\n" +
                    "1. Arranca el servicio de sensores desde el repositorio:\n" +
                    "       cd ServicioSensores && uv run gemelo-sensores\n" +
                    "   (escucha en http://127.0.0.1:8800; otra URL se pone en Ajustes ▸ Gemelo ▸ URL del servicio)\n" +
                    $"2. Lanza ./{Ejecutable}\n" +
                    "   Ventana: ./" + Ejecutable + " -screen-fullscreen 0 -screen-width 1600 -screen-height 900\n\n" +
                    "Ajustes guardados en ~/.config/unity3d/" + PlayerSettings.companyName + "/" + PlayerSettings.productName + "/\n" +
                    "Registro del reproductor en ~/.config/unity3d/" + PlayerSettings.companyName + "/" + PlayerSettings.productName + "/Player.log\n");
            return $"{s.result} {s.totalTime.TotalMinutes:F1} min, {s.totalSize / 1048576.0:F0} MB, errores {s.totalErrors}, avisos {s.totalWarnings}";
        }
    }
}
