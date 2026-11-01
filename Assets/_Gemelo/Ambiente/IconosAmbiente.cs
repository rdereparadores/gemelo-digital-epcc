// Atlas de glifos de los indicadores, dibujado por código con funciones de distancia (bordes suaves, sin
// texturas en el proyecto): 8 × 4 celdas de 128 px, glifo blanco en el canal alfa.
using UnityEngine;

namespace Gemelo
{
    public static class IconosAmbiente
    {
        public const int Aviso = 0, CO2 = 1, Frio = 2, Calor = 3, Ventana = 4, Fuego = 5, Bateria = 6, Reloj = 7,
                         Rayo = 8, Gota = 9, Digito0 = 16, Mas = 26;

        const int Celda = 128, Cols = 8, Filas = 4;
        static Texture2D atlas;

        /// <summary>Glifo de un código de alerta del servicio (o de los eventos).</summary>
        public static int Glifo(string codigo) => codigo switch
        {
            "co2_high" or "co2_critical" => CO2,
            "temperature_low" => Frio,
            "temperature_high" => Calor,
            "window_open_heating" => Ventana,
            "fire_alarm" => Fuego,
            "sensor_battery_low" => Bateria,
            "sensor_stale" => Reloj,
            "power_outage" => Rayo,
            "water_leak" => Gota,
            _ => Aviso,
        };

        public static Texture2D Atlas
        {
            get
            {
                if (atlas != null) return atlas;
                atlas = new Texture2D(Celda * Cols, Celda * Filas, TextureFormat.RGBA32, true)
                {
                    name = "Glifos (gemelo)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2,
                };
                var px = new Color32[atlas.width * atlas.height];
                for (int g = 0; g < Cols * Filas; g++)
                {
                    int cx = g % Cols, cy = g / Cols;
                    for (int y = 0; y < Celda; y++)
                        for (int x = 0; x < Celda; x++)
                        {
                            var p = new Vector2((x + 0.5f) / Celda * 2f - 1f, (y + 0.5f) / Celda * 2f - 1f);
                            float d = Sdf(g, p);
                            byte a = (byte)(Mathf.Clamp01(0.5f - d * Celda * 0.5f) * 255f);
                            px[(cy * Celda + y) * atlas.width + cx * Celda + x] = new Color32(255, 255, 255, a);
                        }
                }
                atlas.SetPixels32(px);
                atlas.Apply(true, true);
                return atlas;
            }
        }

        // ─── Glifos (p en [-1, 1], y hacia arriba; distancia en las mismas unidades, negativa dentro)

        static float Sdf(int g, Vector2 p)
        {
            switch (g)
            {
                case Aviso:
                    return Mathf.Min(Seg(p, new(0, 0.62f), new(0, -0.12f), 0.13f), Circ(p, new(0, -0.55f), 0.15f));
                case CO2:
                {
                    float c = Mathf.Max(Anillo(p, new(-0.42f, 0.08f), 0.32f, 0.09f), -Caja(p, new(-0.08f, 0.08f), new(0.22f, 0.15f)));
                    float o = Anillo(p, new(0.22f, 0.08f), 0.30f, 0.09f);
                    float dos = Mathf.Min(Mathf.Max(Anillo(p, new(0.68f, -0.32f), 0.12f, 0.05f), -(p.y + 0.36f)),
                        Mathf.Min(Seg(p, new(0.79f, -0.36f), new(0.57f, -0.66f), 0.05f), Seg(p, new(0.57f, -0.66f), new(0.84f, -0.66f), 0.05f)));
                    return Mathf.Min(Mathf.Min(c, o), dos);
                }
                case Frio:
                case Calor:
                {
                    float tubo = Mathf.Min(Seg(p, new(0, 0.62f), new(0, -0.35f), 0.2f), Circ(p, new(0, -0.5f), 0.32f));
                    float contorno = Mathf.Abs(tubo) - 0.06f;
                    float columna = Mathf.Min(Circ(p, new(0, -0.5f), 0.18f), Seg(p, new(0, -0.4f), new(0, g == Calor ? 0.55f : -0.05f), 0.08f));
                    float marcas = Mathf.Min(Seg(p, new(0.3f, 0.45f), new(0.45f, 0.45f), 0.035f), Seg(p, new(0.3f, 0.2f), new(0.45f, 0.2f), 0.035f));
                    return Mathf.Min(Mathf.Min(contorno, columna), marcas);
                }
                case Ventana:
                {
                    float marco = Mathf.Abs(Caja(p, new(-0.18f, 0.0f), new(0.52f, 0.62f))) - 0.07f;
                    float parteluz = Seg(p, new(-0.18f, 0.6f), new(-0.18f, -0.6f), 0.05f);
                    // Hoja abierta hacia fuera, en perspectiva
                    float hoja = Mathf.Min(Seg(p, new(0.34f, 0.62f), new(0.78f, 0.42f), 0.06f),
                        Mathf.Min(Seg(p, new(0.78f, 0.42f), new(0.78f, -0.42f), 0.06f), Seg(p, new(0.78f, -0.42f), new(0.34f, -0.62f), 0.06f)));
                    return Mathf.Min(Mathf.Min(marco, parteluz), hoja);
                }
                case Fuego:
                {
                    float fuera = MinSuave(Circ(p, new(0, -0.32f), 0.45f), Tri(p, new(-0.42f, -0.2f), new(0.42f, -0.2f), new(0.08f, 0.88f)), 0.12f);
                    float dentro = MinSuave(Circ(p, new(0.02f, -0.45f), 0.2f), Tri(p, new(-0.18f, -0.4f), new(0.22f, -0.4f), new(0.06f, 0.12f)), 0.06f);
                    return Mathf.Max(fuera, -dentro);
                }
                case Bateria:
                {
                    float cuerpo = Mathf.Abs(Caja(p, new(-0.08f, 0), new(0.62f, 0.36f), 0.08f)) - 0.07f;
                    float borne = Caja(p, new(0.62f, 0), new(0.08f, 0.16f), 0.03f);
                    float carga = Caja(p, new(-0.52f, 0), new(0.1f, 0.2f), 0.02f);
                    return Mathf.Min(Mathf.Min(cuerpo, borne), carga);
                }
                case Reloj:
                    return Mathf.Min(Anillo(p, Vector2.zero, 0.66f, 0.08f),
                        Mathf.Min(Seg(p, Vector2.zero, new(0, 0.42f), 0.07f), Seg(p, Vector2.zero, new(0.32f, -0.12f), 0.07f)));
                case Rayo:
                    return Poligono(p, new Vector2[] { new(0.18f, 0.88f), new(-0.45f, -0.08f), new(-0.04f, -0.08f), new(-0.2f, -0.88f), new(0.46f, 0.12f), new(0.05f, 0.12f) });
                case Gota:
                    return MinSuave(Circ(p, new(0, -0.25f), 0.47f), Tri(p, new(-0.4f, -0.05f), new(0.4f, -0.05f), new(0, 0.88f)), 0.1f);
                case Mas:
                    return Mathf.Min(Seg(p, new(-0.45f, 0), new(0.45f, 0), 0.12f), Seg(p, new(0, -0.45f), new(0, 0.45f), 0.12f));
            }
            if (g >= Digito0 && g < Digito0 + 10) return Digito(p, g - Digito0);
            return 10f;
        }

        // Siete segmentos redondeados: a arriba, b arriba dcha., c abajo dcha., d abajo, e abajo izq., f arriba izq., g medio
        static readonly string[] Segmentos = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

        static float Digito(Vector2 p, int n)
        {
            const float x = 0.34f, y = 0.66f, w = 0.12f;
            float d = 10f;
            foreach (char s in Segmentos[n])
            {
                float t = s switch
                {
                    'a' => Seg(p, new(-x, y), new(x, y), w),
                    'b' => Seg(p, new(x, y), new(x, 0), w),
                    'c' => Seg(p, new(x, 0), new(x, -y), w),
                    'd' => Seg(p, new(-x, -y), new(x, -y), w),
                    'e' => Seg(p, new(-x, 0), new(-x, -y), w),
                    'f' => Seg(p, new(-x, y), new(-x, 0), w),
                    _ => Seg(p, new(-x, 0), new(x, 0), w),
                };
                d = Mathf.Min(d, t);
            }
            return d;
        }

        static float Circ(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Anillo(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;

        static float Seg(Vector2 p, Vector2 a, Vector2 b, float w)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return (pa - ba * h).magnitude - w;
        }

        static float Caja(Vector2 p, Vector2 c, Vector2 medio, float r = 0f)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - medio + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        static float Tri(Vector2 p, Vector2 a, Vector2 b, Vector2 c) => Poligono(p, new[] { a, b, c });

        static float Poligono(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        static float MinSuave(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1 - h);
        }
    }
}
