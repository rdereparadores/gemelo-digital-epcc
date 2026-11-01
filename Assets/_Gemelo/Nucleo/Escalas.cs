// Cómo se lee y se colorea cada capa de datos. Lo comparten la capa de color de la escena (Control) y las
// leyendas y fichas de la UI, para que el mismo valor tenga el mismo color en todas partes.
using UnityEngine;

namespace Gemelo
{
    public static class Escalas
    {
        public static string Nombre(Capa c) => c switch
        {
            Capa.Ocupacion => "Ocupación",
            Capa.Temperatura => "Temperatura",
            Capa.CO2 => "CO₂",
            Capa.Confort => "Confort",
            Capa.Humedad => "Humedad",
            Capa.Consumo => "Consumo eléctrico",
            Capa.Ruido => "Ruido",
            Capa.Iluminacion => "Iluminación",
            _ => "Sin capa",
        };

        public static string Propiedad(Capa c) => c switch
        {
            Capa.Ocupacion => "occupancy",
            Capa.Temperatura => "air_temperature",
            Capa.CO2 => "co2_concentration",
            Capa.Humedad => "relative_humidity",
            Capa.Consumo => "active_power",
            Capa.Ruido => "sound_level",
            Capa.Iluminacion => "illuminance",
            _ => null,
        };

        public static string Unidad(Capa c) => c switch
        {
            Capa.Ocupacion => "pers.",
            Capa.Temperatura => "°C",
            Capa.CO2 => "ppm",
            Capa.Humedad => "%",
            Capa.Consumo => "W",
            Capa.Ruido => "dB",
            Capa.Iluminacion => "lx",
            _ => "",
        };

        /// <summary>Rango de la leyenda (mínimo, máximo).</summary>
        public static Vector2 Rango(Capa c) => c switch
        {
            Capa.Ocupacion => new Vector2(0, 1),        // fracción de la capacidad (o personas/10 m² sin capacidad)
            Capa.Temperatura => new Vector2(14, 30),
            Capa.CO2 => new Vector2(400, 1600),
            Capa.Humedad => new Vector2(20, 80),
            Capa.Consumo => new Vector2(0, 5000),
            Capa.Ruido => new Vector2(30, 80),
            Capa.Iluminacion => new Vector2(0, 800),
            _ => new Vector2(0, 1),
        };

        /// <summary>Valor de la capa en una estancia (NaN si no hay dato).</summary>
        public static float Valor(Capa c, EstadoEstancia e, InfoLocal l = null)
        {
            if (e == null) return float.NaN;
            if (c == Capa.Confort) return ConfortIndice(e.comfort);
            float v = e.Valor(Propiedad(c));
            if (c == Capa.Ocupacion && !float.IsNaN(v))
            {
                float cap = l != null && l.local.capacidad > 0 ? l.local.capacidad : l != null ? Mathf.Max(2f, l.area / 2.5f) : 30f;
                return v / cap;
            }
            return v;
        }

        /// <summary>Color de un valor (transparente si no hay dato).</summary>
        public static Color Color(Capa c, float v)
        {
            if (float.IsNaN(v) || c == Capa.Ninguna) return new Color(0, 0, 0, 0);
            if (c == Capa.Confort) return ColorConfort(v);
            var r = Rango(c);
            float t = Mathf.InverseLerp(r.x, r.y, v);
            return c switch
            {
                Capa.Temperatura => Gradiente(t, Frio, Templado, Calido, Caliente),
                Capa.Humedad => Gradiente(t, Caliente, Templado, Frio, new Color(0.25f, 0.2f, 0.75f)),
                Capa.Iluminacion => Color32(0x20, 0x24, 0x30).Lerp(new Color(1f, 0.93f, 0.6f), t),
                _ => Gradiente(t, Bien, Amarillo, Naranja, Mal),
            };
        }

        public static Color ColorEstancia(Capa c, EstadoEstancia e, InfoLocal l = null) => Color(c, Valor(c, e, l));

        // ─── Confort: ok | cold | hot | stuffy | humid | dry | unknown
        public static float ConfortIndice(string c) => c switch
        {
            "ok" => 0, "cold" => 1, "hot" => 2, "stuffy" => 3, "humid" => 4, "dry" => 5, _ => float.NaN,
        };

        public static string ConfortTexto(string c) => c switch
        {
            "ok" => "Confortable", "cold" => "Frío", "hot" => "Calor", "stuffy" => "Aire cargado",
            "humid" => "Húmedo", "dry" => "Seco", _ => "Sin datos",
        };

        public static Color ColorConfort(float i) => (int)i switch
        {
            0 => Bien, 1 => Frio, 2 => Caliente, 3 => Naranja, 4 => new Color(0.3f, 0.45f, 0.9f), 5 => Amarillo, _ => new Color(0, 0, 0, 0),
        };

        public static Color ColorSeveridad(string s) => s switch
        {
            "critical" => Mal, "warning" => Naranja, _ => new Color(0.35f, 0.62f, 0.95f),
        };

        // Paleta: verde de interfaz UEx, amarillo de marca y naranja de aviso (como el panel del servicio)
        public static readonly Color Bien = Color32(0x0B, 0x80, 0x68);
        public static readonly Color Amarillo = Color32(0xF4, 0xB2, 0x23);
        public static readonly Color Naranja = Color32(0xE8, 0x6A, 0x1C);
        public static readonly Color Mal = Color32(0xC6, 0x28, 0x28);
        public static readonly Color Frio = Color32(0x2F, 0x6F, 0xD6);
        public static readonly Color Templado = Color32(0x3F, 0xB5, 0x8F);
        public static readonly Color Calido = Color32(0xF4, 0xB2, 0x23);
        public static readonly Color Caliente = Color32(0xD8, 0x43, 0x1F);

        static Color Color32(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        static Color Gradiente(float t, Color a, Color b, Color c, Color d)
        {
            t = Mathf.Clamp01(t) * 3f;
            return t < 1 ? a.Lerp(b, t) : t < 2 ? b.Lerp(c, t - 1) : c.Lerp(d, t - 2);
        }

        static Color Lerp(this Color a, Color b, float t) => UnityEngine.Color.Lerp(a, b, t);
    }
}
