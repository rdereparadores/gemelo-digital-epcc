// Medidor de rendimiento del gemelo (solo para pruebas; se lanza desde código o desde `unity command eval`):
// - Medir(segundos): media de Time.smoothDeltaTime y de deltaTime, GPU, hilo principal y de render
//   (FrameTimingManager) y contadores de dibujo (draw calls, SetPass, triángulos, sombras…).
// - AB(nombre, poner): alterna tandas con algo encendido y apagado (A B A B, porque la GPU integrada deriva ±3 ms
//   entre pasadas) y da la diferencia: la media de cada estado y la mediana de las diferencias de cada pareja de
//   tandas seguidas (resiste mejor la deriva y los picos). Conviene repetirlo con poner = nada (prueba A/A) para ver
//   el ruido de ese momento.
// El resultado queda en MedidorRendimiento.Resultado («midiendo…» mientras tanto).
using System;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Gemelo
{
    public class MedidorRendimiento : MonoBehaviour
    {
        public static string Resultado = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Limpiar() => Resultado = "";

        static readonly string[] Contadores =
        {
            "Draw Calls Count", "SetPass Calls Count", "Batches Count", "Triangles Count", "Shadow Casters Count",
            "Visible Skinned Meshes Count",
        };

        class Acum
        {
            public double suave, dt, gpu, cpu, render;
            public readonly double[] cont = new double[Contadores.Length];
            public int n;
        }

        Action<bool> poner;
        string nombre;
        int tandas = 1, tanda;
        float segundos = 4f, descarte = 0.6f, t0;
        readonly Acum[] acum = { new(), new() };
        readonly System.Collections.Generic.List<double> tandaGpu = new(), tandaCpu = new(), tandaSuave = new();
        double tg, tc, ts;
        int tn;
        readonly FrameTiming[] ft = new FrameTiming[1];
        ProfilerRecorder[] rec;

        /// <summary>Mide durante unos segundos lo que hay ahora.</summary>
        public static void Medir(float segundos = 5f, string nombre = "") => Lanzar(nombre, null, 1, segundos);

        /// <summary>Alterna `tandas` veces con poner(true) y poner(false) y compara.</summary>
        public static void AB(string nombre, Action<bool> poner, int tandas = 3, float segundos = 3f) =>
            Lanzar(nombre, poner, tandas * 2, segundos);

        static void Lanzar(string nombre, Action<bool> poner, int tandas, float segundos)
        {
            var app = GemeloApp.Instancia;
            if (app == null) { Resultado = "sin GemeloApp"; return; }
            foreach (var m in app.GetComponents<MedidorRendimiento>()) Destroy(m);
            var x = app.gameObject.AddComponent<MedidorRendimiento>();
            x.nombre = nombre;
            x.poner = poner;
            x.tandas = tandas;
            x.segundos = segundos;
            Resultado = "midiendo…";
        }

        void Start()
        {
            rec = new ProfilerRecorder[Contadores.Length];
            for (int i = 0; i < Contadores.Length; i++) rec[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, Contadores[i]);
            t0 = Time.realtimeSinceStartup;
            poner?.Invoke(true);
        }

        // Mediana de (con − sin) de cada pareja de tandas seguidas, en los dos órdenes (con→sin y sin→con)
        static double Mediana(System.Collections.Generic.List<double> v)
        {
            var d = new System.Collections.Generic.List<double>();
            for (int i = 0; i + 1 < v.Count; i++)
            {
                double x = i % 2 == 0 ? v[i] - v[i + 1] : v[i + 1] - v[i];
                if (!double.IsNaN(x)) d.Add(x);
            }
            if (d.Count == 0) return double.NaN;
            d.Sort();
            return d.Count % 2 == 1 ? d[d.Count / 2] : (d[d.Count / 2 - 1] + d[d.Count / 2]) / 2;
        }

        void OnDestroy()
        {
            if (rec != null) foreach (var r in rec) r.Dispose();
        }

        void Update()
        {
            if (rec == null) return;
            FrameTimingManager.CaptureFrameTimings();
            float t = Time.realtimeSinceStartup - t0;
            if (t > descarte)
            {
                var a = acum[tanda % 2];
                a.suave += Time.smoothDeltaTime * 1000.0;
                a.dt += Time.unscaledDeltaTime * 1000.0;
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                {
                    a.gpu += ft[0].gpuFrameTime;
                    a.cpu += ft[0].cpuMainThreadFrameTime;
                    a.render += ft[0].cpuRenderThreadFrameTime;
                }
                for (int i = 0; i < rec.Length; i++) a.cont[i] += rec[i].Valid ? rec[i].LastValue : 0;
                a.n++;
                tg += ft[0].gpuFrameTime; tc += ft[0].cpuMainThreadFrameTime; ts += Time.smoothDeltaTime * 1000.0; tn++;
            }
            if (t < segundos) return;
            if (tn > 0) { tandaGpu.Add(tg / tn); tandaCpu.Add(tc / tn); tandaSuave.Add(ts / tn); }
            else { tandaGpu.Add(double.NaN); tandaCpu.Add(double.NaN); tandaSuave.Add(double.NaN); }
            tg = tc = ts = 0; tn = 0;
            tanda++;
            t0 = Time.realtimeSinceStartup;
            if (tanda < tandas) { poner?.Invoke(tanda % 2 == 0); return; }
            poner?.Invoke(true);
            var sb = new StringBuilder(nombre);
            for (int k = 0; k < (poner != null ? 2 : 1); k++)
            {
                var a = acum[k];
                if (a.n == 0) continue;
                sb.Append(poner != null ? (k == 0 ? "\n  con: " : "\n  sin: ") : "\n  ");
                sb.Append($"suave {a.suave / a.n:F1} ms, frame {a.dt / a.n:F1} ms, GPU {a.gpu / a.n:F1} ms, principal {a.cpu / a.n:F1} ms, render {a.render / a.n:F1} ms");
                sb.Append($" | dibujos {a.cont[0] / a.n:F0}, setpass {a.cont[1] / a.n:F0}, lotes {a.cont[2] / a.n:F0}, tri {a.cont[3] / a.n / 1e6:F2} M, sombras {a.cont[4] / a.n:F0}, skinned {a.cont[5] / a.n:F0} ({a.n} frames)");
            }
            if (poner != null && acum[0].n > 0 && acum[1].n > 0)
            {
                sb.Append($"\n  diferencia de medias: suave {acum[0].suave / acum[0].n - acum[1].suave / acum[1].n:F2} ms, GPU {acum[0].gpu / acum[0].n - acum[1].gpu / acum[1].n:F2} ms, principal {acum[0].cpu / acum[0].n - acum[1].cpu / acum[1].n:F2} ms");
                sb.Append($"\n  mediana por parejas: suave {Mediana(tandaSuave):F2} ms, GPU {Mediana(tandaGpu):F2} ms, principal {Mediana(tandaCpu):F2} ms ({tandaGpu.Count / 2} parejas)");
            }
            Resultado = sb.ToString();
            Destroy(this);
        }
    }
}
