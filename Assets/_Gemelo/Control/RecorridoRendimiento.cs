// Recorrido de rendimiento del gemelo (solo para pruebas). Con `-recorrido <carpeta>` el ejecutable, ya arrancado y con
// la multitud cargada, pasa por vistas fijas de los tres modos (menú, cenital con y sin capa, tercera persona en la
// calle, en halls, aulas y pasillos), mide en cada una tiempos de frame, GPU e hilos y contadores de dibujo, y deja un
// JSON y una captura por vista. Opciones:
//   -preajuste 0..3 (Bajo, Medio, Alto, Ultra) y -ajustes '{"antialias":4,"maxAnimadas":20}' (campos de Ajustes, solo
//   para esa sesión: no se guardan); -vistas menu,tp_hall_1903 (solo esas); los interruptores de BenchmarkRendimiento
//   (-msaa, -cascadas, -opaca…), que van por encima de los ajustes; -ab '{json}': en cada vista alterna 6 tandas con los
//   ajustes de partida (A) y con esos cambios (B) y da la mediana de las diferencias de GPU y de frame de cada pareja
//   de tandas seguidas (la GPU integrada deriva varios ms entre pasadas: así la comparación es dentro del mismo rato).
// En el editor: RecorridoRendimiento.Lanzar(carpeta) en Play Mode.
// Para que haya gente, el reloj del servicio tiene que estar en horario lectivo.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Gemelo
{
    public class RecorridoRendimiento : MonoBehaviour
    {
        public string salida;
        public int framesCalentar = 100, framesMedir = 240;

        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        static readonly string[] Contadores =
        {
            "SetPass Calls Count", "Draw Calls Count", "Batches Count", "Triangles Count", "Shadow Casters Count",
            "Visible Skinned Meshes Count",
        };

        // Tercera persona: el jugador se pone sobre el suelo bajo el punto (ojos) mirando al objetivo
        struct VistaTP { public string nombre; public Vector3 ojos, objetivo; }
        static readonly VistaTP[] VistasTP =
        {
            T("tp_fachada_sur_comun", 395, 21.7f, 458, 360, 24, 495),
            T("tp_plaza_este", 440, 18.0f, 515, 470, 22, 560),
            T("tp_cafeteria_1901", 405.5f, 18.2f, 491, 419, 17.6f, 478),
            T("tp_hall_1903", 405, 18.3f, 530, 425, 18, 565),
            T("tp_aula_1901_P1_08", 388.04f, 22.2f, 484.94f, 382.45f, 21.8f, 492.44f),
            T("tp_aula_pc_1903_P1_24", 426.25f, 22.2f, 577.91f, 432.93f, 21.8f, 580.02f),
            T("tp_pasillo_1901_P2", 377.0f, 26.15f, 502.26f, 378.44f, 25.75f, 501.74f),
        };
        static VistaTP T(string n, float x, float y, float z, float tx, float ty, float tz) =>
            new VistaTP { nombre = n, ojos = new Vector3(x, y, z), objetivo = new Vector3(tx, ty, tz) };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arrancar()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-recorrido");
            if (i < 0) return;
            Lanzar(i + 1 < args.Length && !args[i + 1].StartsWith("-")
                ? args[i + 1] : Path.Combine(Application.persistentDataPath, "recorrido"));
        }

        public static void Lanzar(string carpeta)
        {
            var go = new GameObject("RecorridoRendimiento");
            go.AddComponent<RecorridoRendimiento>().salida = carpeta;
        }

        static string Arg(string nombre)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, nombre);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(salida);
            // Espera a la app y a que la multitud esté en escena (o 40 s como mucho)
            float t0 = Time.realtimeSinceStartup;
            while (GemeloApp.Instancia == null) yield return null;
            while (GemeloApp.Instancia.PersonasEnEscena == 0 && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            for (int f = 0; f < 120; f++) yield return null;

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            string a;
            if ((a = Arg("-preajuste")) != null) Graficos.Poner(Ajustes.Actual, (Graficos.Preajuste)int.Parse(a));
            if ((a = Arg("-ajustes")) != null) JsonUtility.FromJsonOverwrite(a, Ajustes.Actual);
            Ajustes.Aplicar(false);
            for (int f = 0; f < 10; f++) yield return null;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            var A = Ajustes.Actual;
            string ajustes = BenchmarkRendimiento.AplicarAjustes() + string.Format(CI,
                " | aa={0} sombras={1} oclusion={2} escala={3} detalle={4} hierba={5} animadas={6}",
                A.antialias, A.sombras, A.oclusion, A.escalaRender, A.detalle, A.distanciaHierba, A.maxAnimadas);
            string filtro = Arg("-vistas");
            bool Toca(string n) => filtro == null || System.Array.IndexOf(filtro.Split(','), n) >= 0;

            var rec = new Dictionary<string, ProfilerRecorder>();
            foreach (var n in Contadores) rec[n] = ProfilerRecorder.StartNew(ProfilerCategory.Render, n);
            var json = new StringBuilder();
            json.AppendFormat(CI, "{{\n  \"dispositivo\": \"{0}\", \"resolucion\": \"{1}x{2}\", \"calidad\": \"{3}\", \"personas\": {4}, \"hora\": \"{5}\", \"ajustes\": \"{6}\",\n  \"vistas\": [\n",
                SystemInfo.graphicsDeviceName, Screen.width, Screen.height, QualitySettings.names[QualitySettings.GetQualityLevel()],
                GemeloApp.Instancia.PersonasEnEscena, HoraCampus.Local.ToString("yyyy-MM-dd HH:mm", CI), ajustes);
            var app = GemeloApp.Instancia;
            bool primera = true;

            string jsonB = Arg("-ab"), jsonA = JsonUtility.ToJson(Ajustes.Actual);

            IEnumerator Tanda(List<double> gpuT, List<double> frameT, int frames)
            {
                var g = new List<double>(); var fr = new List<double>(); var tm = new FrameTiming[1];
                for (int f = 0; f < frames; f++)
                {
                    yield return null;
                    fr.Add(Time.unscaledDeltaTime * 1000.0);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, tm) > 0) g.Add(tm[0].gpuFrameTime);
                }
                g.Sort(); fr.Sort();
                gpuT.Add(g.Count > 0 ? g[g.Count / 2] : 0); frameT.Add(fr[fr.Count / 2]);
            }

            IEnumerator MedirAB(string nombre)
            {
                for (int f = 0; f < framesCalentar; f++) yield return null;
                var gpu = new List<double>(); var frame = new List<double>();
                for (int k = 0; k < 12; k++)
                {
                    JsonUtility.FromJsonOverwrite(k % 2 == 0 ? jsonA : jsonB, Ajustes.Actual);
                    Ajustes.Aplicar(false);
                    Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
                    for (int f = 0; f < 20; f++) yield return null;
                    if (k == 1) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(salida, nombre + "_B.png")); }
                    if (k == 2) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(salida, nombre + "_A.png")); }
                    yield return Tanda(gpu, frame, 60);
                }
                JsonUtility.FromJsonOverwrite(jsonA, Ajustes.Actual);
                Ajustes.Aplicar(false);
                Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
                if (!primera) json.Append(",\n");
                primera = false;
                json.AppendFormat(CI, "    {{\"vista\": \"{0}\", \"gpu_A\": {1:F2}, \"gpu_B\": {2:F2}, \"gpu_dif\": {3:F2}, \"frame_A\": {4:F2}, \"frame_B\": {5:F2}, \"frame_dif\": {6:F2}",
                    nombre, MedianaPar(gpu, 0), MedianaPar(gpu, 1), MedianaDif(gpu), MedianaPar(frame, 0), MedianaPar(frame, 1), MedianaDif(frame));
                json.Append("}");
            }

            IEnumerator Medir(string nombre)
            {
                if (!Toca(nombre)) yield break;
                if (jsonB != null) { yield return MedirAB(nombre); yield break; }
                for (int f = 0; f < framesCalentar; f++) yield return null;
                var frame = new List<double>(); var main = new List<double>(); var render = new List<double>(); var gpu = new List<double>();
                var sumas = new double[Contadores.Length];
                var tim = new FrameTiming[1];
                for (int f = 0; f < framesMedir; f++)
                {
                    yield return null;
                    frame.Add(Time.unscaledDeltaTime * 1000.0);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, tim) > 0)
                    {
                        main.Add(tim[0].cpuMainThreadFrameTime); render.Add(tim[0].cpuRenderThreadFrameTime); gpu.Add(tim[0].gpuFrameTime);
                    }
                    for (int k = 0; k < Contadores.Length; k++) if (rec[Contadores[k]].Valid) sumas[k] += rec[Contadores[k]].LastValue;
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(salida, nombre + ".png"));
                yield return null;
                if (!primera) json.Append(",\n");
                primera = false;
                json.AppendFormat(CI, "    {{\"vista\": \"{0}\", \"fps\": {1:F1}, \"frame_ms\": {2}, \"main_ms\": {3}, \"render_ms\": {4}, \"gpu_ms\": {5}, \"personas\": {6}",
                    nombre, 1000.0 / Media(frame), Est(frame), Est(main), Est(render), Est(gpu), app.PersonasEnEscena);
                for (int k = 0; k < Contadores.Length; k++) json.AppendFormat(CI, ", \"{0}\": {1:F0}", Contadores[k], sumas[k] / framesMedir);
                json.Append("}");
                Debug.Log($"[Recorrido] {nombre}: {1000.0 / Media(frame):F1} fps");
            }

            // Menú
            yield return Medir("menu");
            // Cenital general y con capa de ocupación y corte de Teleco P1
            app.CambiarModo(Modo.Cenital);
            yield return Medir("cenital");
            var e = RegistroCampus.Edificio("1905");
            app.CambiarCapa(Capa.Ocupacion);
            app.Seleccionar(new Seleccion(e, null));
            app.CortarEdificio(e, e.plantas.FindIndex(p => p.nombre == "P1"));
            FindAnyObjectByType<CamaraCenital>()?.Centrar(app.Seleccion);
            yield return Medir("cenital_capa_1905");
            app.QuitarCortes();
            app.CambiarCapa(Capa.Ninguna);
            app.Seleccionar(default);
            // Tercera persona
            app.CambiarModo(Modo.TerceraPersona);
            yield return null;
            var j = FindAnyObjectByType<JugadorTerceraPersona>();
            foreach (var v in VistasTP)
            {
                var pie = Physics.Raycast(v.ojos + Vector3.up * 0.3f, Vector3.down, out var hit, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    ? hit.point + Vector3.up * 0.05f : v.ojos - Vector3.up * 1.6f;
                j.Colocar(pie, v.objetivo);
                yield return Medir(v.nombre);
            }

            json.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(salida, "recorrido.json"), json.ToString());
            foreach (var r in rec.Values) r.Dispose();
            Debug.Log("[Recorrido] terminado: " + salida);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Mediana de las tandas pares (A) o impares (B)
        static double MedianaPar(List<double> v, int resto)
        {
            var l = new List<double>();
            for (int i = resto; i < v.Count; i += 2) l.Add(v[i]);
            l.Sort();
            return l.Count > 0 ? l[l.Count / 2] : 0;
        }

        // Mediana de B − A de cada pareja de tandas seguidas (A→B y B→A)
        static double MedianaDif(List<double> v)
        {
            var d = new List<double>();
            for (int i = 0; i + 1 < v.Count; i++) d.Add(i % 2 == 0 ? v[i + 1] - v[i] : v[i] - v[i + 1]);
            d.Sort();
            return d.Count > 0 ? (d[(d.Count - 1) / 2] + d[d.Count / 2]) / 2 : 0;
        }

        static double Media(List<double> l) { if (l.Count == 0) return 0; double s = 0; foreach (var x in l) s += x; return s / l.Count; }

        // [mediana, p95]
        static string Est(List<double> l)
        {
            if (l.Count == 0) return "null";
            l.Sort();
            return string.Format(CI, "[{0:F2}, {1:F2}]", l[l.Count / 2], l[Mathf.Min(l.Count - 1, (int)(l.Count * 0.95))]);
        }
    }
}
