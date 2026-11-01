// Sonidos generados por código (sin archivos de audio en el proyecto): sirena de evacuación, viento, lluvia,
// pájaros y rumor de gente. Bucles de unos segundos que enlazan sin chasquido.
using UnityEngine;

namespace Gemelo
{
    public static class SintesisAudio
    {
        const int Hz = 44100;

        /// <summary>Sirena de evacuación: barrido 650→1100 Hz en 1,1 s y bajada rápida, con armónicos (2,8 s).</summary>
        public static AudioClip Sirena()
        {
            int n = Mathf.RoundToInt(Hz * 2.8f);
            var s = new float[n];
            double fase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Hz % 1.4f;
                float f = t < 1.1f ? Mathf.Lerp(650f, 1100f, Mathf.SmoothStep(0, 1, t / 1.1f)) : Mathf.Lerp(1100f, 650f, (t - 1.1f) / 0.3f);
                fase += 2 * Mathf.PI * f / Hz;
                float x = Mathf.Sin((float)fase) + 0.35f * Mathf.Sin((float)(3 * fase)) + 0.15f * Mathf.Sin((float)(5 * fase));
                s[i] = Mathf.Clamp(x * 0.55f, -1f, 1f);
            }
            return Clip("Sirena", s);
        }

        /// <summary>Viento: ruido marrón con ráfagas lentas.</summary>
        public static AudioClip Viento()
        {
            int n = Hz * 8;
            var s = new float[n];
            var r = new System.Random(3);
            float b = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                b = Mathf.Clamp(b + ((float)r.NextDouble() * 2 - 1) * 0.02f, -1f, 1f) * 0.998f;
                lp += (b - lp) * 0.08f;
                float t = (float)i / n * 2 * Mathf.PI;
                float rafaga = 0.55f + 0.3f * Mathf.Sin(t) + 0.15f * Mathf.Sin(3 * t + 1.3f);
                s[i] = lp * 3.5f * rafaga;
            }
            return Clip("Viento", Fundir(s));
        }

        /// <summary>Lluvia: ruido blanco filtrado con golpes de gota.</summary>
        public static AudioClip Lluvia()
        {
            int n = Hz * 6;
            var s = new float[n];
            var r = new System.Random(7);
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)r.NextDouble() * 2 - 1;
                lp += (w - lp) * 0.35f;
                hp = 0.95f * (hp + lp - prev);
                prev = lp;
                float gota = r.NextDouble() < 0.0016 ? ((float)r.NextDouble() * 0.6f + 0.2f) : 0f;
                s[i] = hp * 0.5f;
                if (gota > 0)
                    for (int k = 0; k < 220 && i + k < n; k++) s[i + k] += gota * Mathf.Exp(-k / 40f) * Mathf.Sin(k * 0.9f) * 0.5f;
            }
            return Clip("Lluvia", Fundir(s));
        }

        /// <summary>Pájaros: trinos sueltos (gorriones y algún mirlo) sobre silencio.</summary>
        public static AudioClip Pajaros()
        {
            int n = Hz * 10;
            var s = new float[n];
            var r = new System.Random(11);
            int pos = Hz / 3;
            while (pos < n - Hz)
            {
                int notas = r.Next(2, 7);
                float fBase = 2600f + (float)r.NextDouble() * 2400f;
                for (int k = 0; k < notas; k++)
                {
                    int dur = (int)(Hz * (0.04f + (float)r.NextDouble() * 0.07f));
                    float f0 = fBase * (0.85f + (float)r.NextDouble() * 0.3f), f1 = f0 * (0.7f + (float)r.NextDouble() * 0.6f);
                    double fase = 0;
                    for (int j = 0; j < dur && pos + j < n; j++)
                    {
                        float u = (float)j / dur;
                        fase += 2 * Mathf.PI * Mathf.Lerp(f0, f1, u) / Hz;
                        s[pos + j] += Mathf.Sin((float)fase) * Mathf.Sin(Mathf.PI * u) * 0.25f;
                    }
                    pos += dur + (int)(Hz * (0.02f + (float)r.NextDouble() * 0.06f));
                }
                pos += (int)(Hz * (0.4f + (float)r.NextDouble() * 1.6f));
            }
            return Clip("Pajaros", Fundir(s));
        }

        /// <summary>Rumor de gente: varias «voces» de ruido filtrado en la banda de la voz, moduladas a ritmo de sílaba.</summary>
        public static AudioClip Rumor()
        {
            int n = Hz * 8;
            var s = new float[n];
            var r = new System.Random(5);
            for (int v = 0; v < 9; v++)
            {
                float f = 160f + (float)r.NextDouble() * 140f;     // tono de la voz
                float ritmo = 3.2f + (float)r.NextDouble() * 2.2f;  // sílabas por segundo
                float desf = (float)r.NextDouble() * 10f;
                double fase = 0;
                float lp = 0, bp = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Hz;
                    float sil = Mathf.Max(0, Mathf.Sin(2 * Mathf.PI * ritmo * t + desf)) * (0.5f + 0.5f * Mathf.PerlinNoise(t * 0.7f, v * 3.1f));
                    float frase = Mathf.PerlinNoise(t * 0.35f + desf, v) > 0.42f ? 1f : 0.15f;
                    fase += 2 * Mathf.PI * f * (1f + 0.08f * Mathf.Sin(t * 2.3f + v)) / Hz;
                    float ruido = (float)r.NextDouble() * 2 - 1;
                    lp += (ruido - lp) * 0.12f;
                    bp += (lp - bp) * 0.5f;
                    float voz = (Mathf.Sin((float)fase) * 0.4f + Mathf.Sin((float)fase * 2.0f) * 0.25f + Mathf.Sin((float)fase * 3.0f) * 0.12f) * 0.5f + bp * 0.6f;
                    s[i] += voz * sil * frase * 0.18f;
                }
            }
            // Paso bajo suave (las voces lejanas pierden agudos)
            float y = 0;
            for (int i = 0; i < n; i++) { y += (s[i] - y) * 0.25f; s[i] = y; }
            return Clip("Rumor", Fundir(s));
        }

        // Funde el final con el principio para que el bucle no chasquee
        static float[] Fundir(float[] s)
        {
            int f = Mathf.Min(Hz / 2, s.Length / 4);
            int n = s.Length - f;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = s[i];
            for (int i = 0; i < f; i++)
            {
                float t = (float)i / f;
                o[i] = s[i] * t + s[n + i] * (1 - t);
            }
            // Normaliza a −6 dB
            float max = 1e-4f;
            foreach (var x in o) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < n; i++) o[i] *= 0.5f / max;
            return o;
        }

        static AudioClip Clip(string nombre, float[] s)
        {
            var c = AudioClip.Create(nombre + " (gemelo)", s.Length, 1, Hz, false);
            c.SetData(s, 0);
            return c;
        }
    }
}
