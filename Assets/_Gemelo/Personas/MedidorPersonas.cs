// Medidor del coste de la multitud (solo para pruebas; se añade a mano desde código o consola):
// alterna tandas con la multitud dibujada y sin dibujar (A B A B, porque la GPU integrada deriva) y apunta la media de
// GPU (FrameTimingManager), del hilo principal y de la simulación y el dibujo de Personas.
using System.Text;
using UnityEngine;

namespace Gemelo
{
    public class MedidorPersonas : MonoBehaviour
    {
        public static string Resultado = "";
        public int tandas = 4;
        public float segundosTanda = 3f, descarte = 0.7f;

        readonly FrameTiming[] ft = new FrameTiming[1];
        double[] gpu = new double[2], cpu = new double[2], sim = new double[2], dib = new double[2];
        int[] n = new int[2];
        int tanda;
        float tInicio;

        void Start()
        {
            Resultado = "midiendo…";
            tInicio = Time.realtimeSinceStartup;
            Aplicar();
        }

        void Aplicar()
        {
            var mp = ModuloPersonas.Instancia;
            if (mp != null) mp.Visible = tanda % 2 == 0;
        }

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            float t = Time.realtimeSinceStartup - tInicio;
            if (t > descarte && FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                int k = tanda % 2;
                gpu[k] += ft[0].gpuFrameTime;
                cpu[k] += ft[0].cpuMainThreadFrameTime;
                var mp = ModuloPersonas.Instancia;
                if (mp != null) { sim[k] += mp.MsSimulacionFrame; dib[k] += mp.MsDibujoFrame; }
                n[k]++;
            }
            if (t < segundosTanda) return;
            tanda++;
            tInicio = Time.realtimeSinceStartup;
            if (tanda >= tandas * 2)
            {
                var sb = new StringBuilder();
                string[] nom = { "con personas", "sin personas" };
                for (int k = 0; k < 2; k++)
                    sb.Append($"{nom[k]}: GPU {gpu[k] / n[k]:F2} ms, hilo principal {cpu[k] / n[k]:F2} ms, sim {sim[k] / n[k]:F3} ms, dibujo(CPU) {dib[k] / n[k]:F3} ms ({n[k]} frames)\n");
                sb.Append($"diferencia GPU: {(gpu[0] / n[0] - gpu[1] / n[1]):F2} ms; CPU principal: {(cpu[0] / n[0] - cpu[1] / n[1]):F2} ms");
                var m = ModuloPersonas.Instancia;
                if (m != null) sb.Append($"; personas {m.Multitud.activas.Count}, animadas {m.Dibujo.Animadas}, instanciadas {m.Dibujo.Instanciadas}");
                Resultado = sb.ToString();
                if (m != null) m.Visible = true;
                Destroy(this);
                return;
            }
            Aplicar();
        }
    }
}
