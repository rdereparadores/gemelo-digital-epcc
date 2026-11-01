// Gráfica de una serie temporal dibujada con Painter2D: área y línea, rejilla, ejes con la hora local del campus,
// umbrales (CO₂ 1000 ppm, banda de confort de temperatura) y un tooltip que sigue al puntero.
// Los datos salen de GET /api/v1/{spaces|buildings}/{id}/history (ver Historial).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class Grafica : VisualElement
    {
        const float MIzq = 40, MDer = 10, MArr = 10, MAbj = 20;

        readonly List<Vector2> pts = new();       // x = segundos desde «desde», y = valor
        readonly Label[] ejeY = new Label[3];
        readonly Label[] ejeX = new Label[4];
        readonly Label tip, vacio;
        DateTime desde, hasta;                      // UTC
        string prop;
        Color color = Escalas.Amarillo;
        float yMin, yMax;
        int sobre = -1;

        public Grafica()
        {
            AddToClassList("grafica");
            generateVisualContent += Dibujar;
            for (int i = 0; i < ejeY.Length; i++) { ejeY[i] = Txt(this, "", "grafica__eje"); ejeY[i].pickingMode = PickingMode.Ignore; }
            for (int i = 0; i < ejeX.Length; i++) { ejeX[i] = Txt(this, "", "grafica__eje"); ejeX[i].pickingMode = PickingMode.Ignore; }
            vacio = Txt(this, "Cargando…", "grafica__vacio");
            vacio.pickingMode = PickingMode.Ignore;
            tip = Txt(this, "", "grafica__tip");
            tip.pickingMode = PickingMode.Ignore;
            Mostrar(tip, false);
            RegisterCallback<GeometryChangedEvent>(_ => Colocar());
            RegisterCallback<PointerMoveEvent>(e => Sobre(e.localPosition));
            RegisterCallback<PointerLeaveEvent>(_ => { sobre = -1; Mostrar(tip, false); MarkDirtyRepaint(); });
        }

        public void Vacia(string mensaje)
        {
            pts.Clear();
            vacio.text = mensaje;
            Mostrar(vacio, true);
            foreach (var l in ejeY) l.text = "";
            foreach (var l in ejeX) l.text = "";
            MarkDirtyRepaint();
        }

        public void Poner(SerieHist serie, DateTime desdeUtc, DateTime hastaUtc, Color col)
        {
            prop = serie?.property;
            desde = desdeUtc;
            hasta = hastaUtc;
            color = col;
            pts.Clear();
            if (serie?.points != null)
                foreach (var p in serie.points)
                    if (Formato.LeerFecha(p.time, out var t)) pts.Add(new Vector2((float)(t - desde).TotalSeconds, p.value));
            if (pts.Count == 0) { Vacia("Sin datos en este intervalo"); return; }
            Mostrar(vacio, false);

            // Rango vertical con un mínimo según la magnitud, y con los umbrales a la vista si están cerca
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var p in pts) { mn = Mathf.Min(mn, p.y); mx = Mathf.Max(mx, p.y); }
            float minSpan = prop switch
            {
                "air_temperature" => 3f, "co2_concentration" => 200f, "occupancy" => 5f, "relative_humidity" => 10f,
                "active_power" => 1000f, "sound_level" => 10f, "illuminance" => 100f, _ => 1f,
            };
            if (prop == "co2_concentration" && mx > 800) mx = Mathf.Max(mx, 1050);
            float span = Mathf.Max(minSpan, mx - mn);
            float centro = (mn + mx) / 2f;
            yMin = centro - span * 0.6f;
            yMax = centro + span * 0.6f;
            if (prop is "occupancy" or "active_power" or "illuminance" or "relative_humidity" || mn >= 0 && yMin < 0) yMin = Mathf.Max(0, yMin);
            if (prop == "occupancy") yMax = Mathf.Max(yMax, mx * 1.15f + 1f);
            Colocar();
            MarkDirtyRepaint();
        }

        float Ancho => Mathf.Max(1, contentRect.width - MIzq - MDer);
        float Alto => Mathf.Max(1, contentRect.height - MArr - MAbj);
        float Total => Mathf.Max(1f, (float)(hasta - desde).TotalSeconds);

        Vector2 APantalla(Vector2 p) => new(
            MIzq + p.x / Total * Ancho,
            MArr + (1f - Mathf.InverseLerp(yMin, yMax, p.y)) * Alto);

        void Colocar()
        {
            if (pts.Count == 0) return;
            for (int i = 0; i < ejeY.Length; i++)
            {
                float v = Mathf.Lerp(yMin, yMax, i / (ejeY.Length - 1f));
                var l = ejeY[i];
                l.text = Corto(v);
                l.style.left = 0;
                l.style.width = MIzq - 6;
                l.style.unityTextAlign = TextAnchor.MiddleRight;
                l.style.top = MArr + (1f - i / (ejeY.Length - 1f)) * Alto - 7;
            }
            for (int i = 0; i < ejeX.Length; i++)
            {
                float f = i / (ejeX.Length - 1f);
                var t = HoraCampus.ALocal(desde.AddSeconds(Total * f));
                var l = ejeX[i];
                l.text = Formato.Hora(t);
                l.style.top = MArr + Alto + 4;
                l.style.left = MIzq + f * Ancho - (i == 0 ? 0 : i == ejeX.Length - 1 ? 30 : 15);
            }
        }

        string Corto(float v) => prop switch
        {
            "active_power" => Mathf.Abs(v) >= 1000 ? Formato.Num(v / 1000f, 0) + "k" : Formato.Num(v),
            "air_temperature" => Formato.Num(v, 0) + "°",
            _ => Formato.Num(v),
        };

        void Sobre(Vector2 local)
        {
            if (pts.Count == 0) return;
            float x = (local.x - MIzq) / Ancho * Total;
            int mejor = 0;
            float d = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float di = Mathf.Abs(pts[i].x - x);
                if (di < d) { d = di; mejor = i; }
            }
            if (mejor == sobre) return;
            sobre = mejor;
            var p = pts[mejor];
            var t = HoraCampus.ALocal(desde.AddSeconds(p.x));
            tip.text = $"{Formato.Hora(t)}  ·  {Formato.Valor(prop, p.y)}";
            Mostrar(tip, true);
            var s = APantalla(p);
            tip.style.left = Mathf.Clamp(s.x - 50, 0, contentRect.width - 110);
            tip.style.top = Mathf.Max(0, s.y - 34);
            MarkDirtyRepaint();
        }

        void Dibujar(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            if (r.width < 10) return;

            // Rejilla
            p.lineWidth = 1f;
            p.strokeColor = new Color(1, 1, 1, 0.07f);
            for (int i = 0; i < 3; i++)
            {
                float y = MArr + i / 2f * Alto;
                p.BeginPath(); p.MoveTo(new Vector2(MIzq, y)); p.LineTo(new Vector2(MIzq + Ancho, y)); p.Stroke();
            }
            if (pts.Count == 0) return;

            // Umbrales: banda de confort (temperatura) y límite de CO₂
            if (prop == "air_temperature")
                Banda(p, 19f, 26f, new Color(0.0f, 0.69f, 0.54f, 0.07f));
            if (prop == "co2_concentration")
                Discontinua(p, 1000f, new Color(0.95f, 0.57f, 0.29f, 0.8f));

            // Área bajo la curva
            float y0 = MArr + Alto;
            p.fillColor = ConAlfa(color, 0.16f);
            p.BeginPath();
            p.MoveTo(new Vector2(APantalla(pts[0]).x, y0));
            foreach (var q in pts) p.LineTo(APantalla(q));
            p.LineTo(new Vector2(APantalla(pts[pts.Count - 1]).x, y0));
            p.ClosePath();
            p.Fill();

            // Línea
            p.lineWidth = 2.2f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.strokeColor = color;
            p.BeginPath();
            p.MoveTo(APantalla(pts[0]));
            for (int i = 1; i < pts.Count; i++)
            {
                // Huecos (sin datos) mayores que tres pasos: se corta la línea
                if (pts[i].x - pts[i - 1].x > Total / Mathf.Max(8, pts.Count) * 3f) p.MoveTo(APantalla(pts[i]));
                else p.LineTo(APantalla(pts[i]));
            }
            p.Stroke();

            // Último punto y punto bajo el cursor
            Punto(p, APantalla(pts[pts.Count - 1]), 3.5f);
            if (sobre >= 0 && sobre < pts.Count)
            {
                var s = APantalla(pts[sobre]);
                p.lineWidth = 1f;
                p.strokeColor = new Color(1, 1, 1, 0.25f);
                p.BeginPath(); p.MoveTo(new Vector2(s.x, MArr)); p.LineTo(new Vector2(s.x, MArr + Alto)); p.Stroke();
                Punto(p, s, 4.5f);
            }
        }

        void Punto(Painter2D p, Vector2 c, float r)
        {
            p.fillColor = new Color(0.09f, 0.09f, 0.1f, 1f);
            p.BeginPath(); p.Arc(c, r + 2f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = color;
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
        }

        void Banda(Painter2D p, float a, float b, Color c)
        {
            float ya = APantalla(new Vector2(0, a)).y, yb = APantalla(new Vector2(0, b)).y;
            ya = Mathf.Clamp(ya, MArr, MArr + Alto);
            yb = Mathf.Clamp(yb, MArr, MArr + Alto);
            if (Mathf.Abs(ya - yb) < 1) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(MIzq, yb)); p.LineTo(new Vector2(MIzq + Ancho, yb));
            p.LineTo(new Vector2(MIzq + Ancho, ya)); p.LineTo(new Vector2(MIzq, ya));
            p.ClosePath();
            p.Fill();
        }

        void Discontinua(Painter2D p, float v, Color c)
        {
            if (v < yMin || v > yMax) return;
            float y = APantalla(new Vector2(0, v)).y;
            p.lineWidth = 1.2f;
            p.strokeColor = c;
            p.BeginPath();
            for (float x = MIzq; x < MIzq + Ancho; x += 8)
            {
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(Mathf.Min(x + 4, MIzq + Ancho), y));
            }
            p.Stroke();
        }
    }

    // ─────────────── Histórico agregado (GET .../history) ───────────────

    [Serializable] public class PuntoHist { public string time; public float value; }

    [Serializable]
    public class SerieHist
    {
        public string property, unit, aggregation, source;
        public List<PuntoHist> points;
    }

    [Serializable]
    public class RespuestaHist
    {
        public string space_id, building_id, from, to, source, reconstructed_until;
        public float step_s;
        public List<SerieHist> series;
        public SerieHist Serie(string p) => series?.Find(s => s.property == p);
    }

    public static class Historial
    {
        static readonly Dictionary<string, (float t, RespuestaHist r)> cache = new();

        /// <summary>Histórico de una estancia (spaceId) o de un edificio (buildingId, si edificio = true).</summary>
        public static void Pedir(string id, bool edificio, string propiedades, float horas, Action<RespuestaHist> listo)
        {
            var cli = ClienteGemelo.Instancia;
            if (cli == null || string.IsNullOrEmpty(id)) { listo?.Invoke(null); return; }
            int paso = horas <= 2 ? 120 : horas <= 6 ? 300 : 900;
            string ruta = $"/api/v1/{(edificio ? "buildings" : "spaces")}/{UnityEngine.Networking.UnityWebRequest.EscapeURL(id)}/history" +
                          $"?properties={propiedades}&hours={horas.ToString(System.Globalization.CultureInfo.InvariantCulture)}&step_s={paso}";
            // Con el tiempo acelerado los datos envejecen rápido: la caché dura menos
            float vida = HoraCampus.Escala > 30 ? 3f : 15f;
            if (cache.TryGetValue(ruta, out var c) && Time.realtimeSinceStartup - c.t < vida) { listo?.Invoke(c.r); return; }
            cli.Get(ruta, txt =>
            {
                RespuestaHist r = null;
                if (txt != null)
                {
                    try { r = JsonUtility.FromJson<RespuestaHist>(txt); }
                    catch (Exception e) { Debug.LogWarning($"[Gemelo/UI] histórico ilegible: {e.Message}"); }
                }
                if (r != null) cache[ruta] = (Time.realtimeSinceStartup, r);
                listo?.Invoke(r);
            });
        }
    }
}
