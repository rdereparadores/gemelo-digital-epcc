// Iconos vectoriales de trazo (rejilla de 24×24, como los del panel web) dibujados con Painter2D a partir de
// trayectos SVG. Toman el color del texto (style.color heredado); si cambia, hay que llamar a MarkDirtyRepaint.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gemelo
{
    public class Icono : VisualElement
    {
        string nombre;
        Color? color;

        public Icono(string nombre, float tamaño = 18f)
        {
            this.nombre = nombre;
            AddToClassList("icono");
            pickingMode = PickingMode.Ignore;
            style.width = tamaño;
            style.height = tamaño;
            style.flexShrink = 0;
            generateVisualContent += Dibujar;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        public string Nombre
        {
            get => nombre;
            set { if (value != nombre) { nombre = value; MarkDirtyRepaint(); } }
        }

        /// <summary>Color fijo (si no, el del texto heredado).</summary>
        public Color? Color
        {
            get => color;
            set { color = value; MarkDirtyRepaint(); }
        }

        void Dibujar(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 1 || r.height < 1 || nombre == null) return;
            if (!Trazos.TryGetValue(nombre, out var def)) return;
            var ops = Compilado(nombre, def);
            float esc = Mathf.Min(r.width, r.height) / 24f;
            var off = new Vector2(r.x + (r.width - 24f * esc) / 2f, r.y + (r.height - 24f * esc) / 2f);
            var p = ctx.painter2D;
            var c = color ?? resolvedStyle.color;
            bool relleno = def.StartsWith("F:");
            p.lineWidth = Mathf.Max(1.1f, 1.9f * esc);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = c;
            p.fillColor = c;
            Pintar(p, ops, esc, off);
            if (relleno) p.Fill(); else p.Stroke();
        }

        // ───────────────────────── Trayectos ─────────────────────────

        enum Op : byte { M, L, C, Q, Arco, Z }

        struct Paso
        {
            public Op op;
            public Vector2 a, b, c;   // puntos (o centro en Arco)
            public float r, a0, a1;   // radio y ángulos del arco (radianes)
        }

        static readonly Dictionary<string, Paso[]> cache = new();

        static Paso[] Compilado(string n, string def)
        {
            if (cache.TryGetValue(n, out var ops)) return ops;
            ops = Parsear(def.StartsWith("F:") ? def.Substring(2) : def).ToArray();
            cache[n] = ops;
            return ops;
        }

        static void Pintar(Painter2D p, Paso[] ops, float esc, Vector2 off)
        {
            p.BeginPath();
            foreach (var s in ops)
            {
                switch (s.op)
                {
                    case Op.M: p.MoveTo(off + s.a * esc); break;
                    case Op.L: p.LineTo(off + s.a * esc); break;
                    case Op.C: p.BezierCurveTo(off + s.a * esc, off + s.b * esc, off + s.c * esc); break;
                    case Op.Q: p.QuadraticCurveTo(off + s.a * esc, off + s.b * esc); break;
                    case Op.Arco:
                        p.Arc(off + s.a * esc, s.r * esc, Angle.Radians(s.a0), Angle.Radians(s.a1),
                            s.a1 >= s.a0 ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                        break;
                    case Op.Z: p.ClosePath(); break;
                }
            }
        }

        static List<Paso> Parsear(string d)
        {
            var salida = new List<Paso>();
            int i = 0;
            char cmd = ' ';
            Vector2 cur = default, ini = default, ultCtrl = default;
            char ultCmd = ' ';
            while (true)
            {
                Saltar(d, ref i);
                if (i >= d.Length) break;
                char ch = d[i];
                if (char.IsLetter(ch) && ch != 'e' && ch != 'E') { cmd = ch; i++; }
                else if (cmd == ' ') break;
                bool rel = char.IsLower(cmd);
                Vector2 b = rel ? cur : Vector2.zero;
                switch (char.ToUpper(cmd))
                {
                    case 'M':
                        cur = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        ini = cur;
                        salida.Add(new Paso { op = Op.M, a = cur });
                        cmd = rel ? 'l' : 'L';
                        break;
                    case 'L':
                        cur = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        salida.Add(new Paso { op = Op.L, a = cur });
                        break;
                    case 'H':
                        cur = new Vector2((rel ? cur.x : 0) + Num(d, ref i), cur.y);
                        salida.Add(new Paso { op = Op.L, a = cur });
                        break;
                    case 'V':
                        cur = new Vector2(cur.x, (rel ? cur.y : 0) + Num(d, ref i));
                        salida.Add(new Paso { op = Op.L, a = cur });
                        break;
                    case 'C':
                    {
                        var c1 = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        var c2 = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        var e = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        salida.Add(new Paso { op = Op.C, a = c1, b = c2, c = e });
                        ultCtrl = c2; cur = e;
                        break;
                    }
                    case 'S':
                    {
                        var c1 = "CcSs".IndexOf(ultCmd) >= 0 ? 2 * cur - ultCtrl : cur;
                        var c2 = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        var e = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        salida.Add(new Paso { op = Op.C, a = c1, b = c2, c = e });
                        ultCtrl = c2; cur = e;
                        break;
                    }
                    case 'Q':
                    {
                        var c1 = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        var e = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        salida.Add(new Paso { op = Op.Q, a = c1, b = e });
                        cur = e;
                        break;
                    }
                    case 'A':
                    {
                        float rx = Num(d, ref i), ry = Num(d, ref i);
                        Num(d, ref i); // rotación (los iconos usan círculos)
                        bool grande = Bandera(d, ref i), barrido = Bandera(d, ref i);
                        var e = b + new Vector2(Num(d, ref i), Num(d, ref i));
                        Arco(salida, cur, e, (rx + ry) / 2f, grande, barrido);
                        cur = e;
                        break;
                    }
                    case 'O': // círculo: O cx cy r (extensión propia)
                    {
                        var c = new Vector2(Num(d, ref i), Num(d, ref i));
                        float r = Num(d, ref i);
                        salida.Add(new Paso { op = Op.M, a = c + new Vector2(r, 0) });
                        salida.Add(new Paso { op = Op.Arco, a = c, r = r, a0 = 0, a1 = Mathf.PI * 2f });
                        salida.Add(new Paso { op = Op.Z });
                        cur = c + new Vector2(r, 0);
                        break;
                    }
                    case 'Z':
                        salida.Add(new Paso { op = Op.Z });
                        cur = ini;
                        break;
                    default:
                        i++;
                        break;
                }
                ultCmd = cmd;
            }
            return salida;
        }

        static void Arco(List<Paso> salida, Vector2 p1, Vector2 p2, float r, bool grande, bool barrido)
        {
            if (r <= 0 || (p1 - p2).sqrMagnitude < 1e-8f) { salida.Add(new Paso { op = Op.L, a = p2 }); return; }
            var m = (p1 - p2) / 2f;
            float lam = m.sqrMagnitude / (r * r);
            if (lam > 1) r *= Mathf.Sqrt(lam);
            float num = r * r * r * r - r * r * m.y * m.y - r * r * m.x * m.x;
            float den = r * r * m.y * m.y + r * r * m.x * m.x;
            float coef = Mathf.Sqrt(Mathf.Max(0, num / Mathf.Max(den, 1e-9f))) * (grande == barrido ? -1 : 1);
            var cp = new Vector2(coef * m.y, -coef * m.x);
            var centro = cp + (p1 + p2) / 2f;
            float t1 = Mathf.Atan2((m.y - cp.y) / r, (m.x - cp.x) / r);
            float t2 = Mathf.Atan2((-m.y - cp.y) / r, (-m.x - cp.x) / r);
            float dt = t2 - t1;
            if (!barrido && dt > 0) dt -= 2 * Mathf.PI;
            else if (barrido && dt < 0) dt += 2 * Mathf.PI;
            salida.Add(new Paso { op = Op.Arco, a = centro, r = r, a0 = t1, a1 = t1 + dt });
        }

        static void Saltar(string d, ref int i)
        {
            while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t')) i++;
        }

        static bool Bandera(string d, ref int i)
        {
            Saltar(d, ref i);
            bool v = i < d.Length && d[i] == '1';
            i++;
            return v;
        }

        static float Num(string d, ref int i)
        {
            Saltar(d, ref i);
            int ini = i;
            if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            bool punto = false;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsDigit(c)) i++;
                else if (c == '.' && !punto) { punto = true; i++; }
                else if ((c == 'e' || c == 'E') && i + 1 < d.Length && (char.IsDigit(d[i + 1]) || d[i + 1] == '-')) { i += 2; }
                else break;
            }
            return i > ini ? float.Parse(d.Substring(ini, i - ini), CultureInfo.InvariantCulture) : 0f;
        }

        // ───────────────────────── Catálogo ─────────────────────────
        // «F:» delante = relleno en vez de trazo. «O cx cy r» = círculo.

        public static readonly Dictionary<string, string> Trazos = new()
        {
            ["personas"] = "O 9 7 4 M2 21v-2a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v2 M16 3.13a4 4 0 0 1 0 7.75 M22 21v-2a4 4 0 0 0-3-3.87",
            ["persona"] = "O 12 7 4 M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2",
            ["termometro"] = "M14 14.76V3.5a2.5 2.5 0 0 0-5 0v11.26a4.5 4.5 0 1 0 5 0z",
            ["gota"] = "M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z",
            ["viento"] = "M9.59 4.59A2 2 0 1 1 11 8H2 M12.59 19.41A2 2 0 1 0 14 16H2 M17.73 7.73A2.5 2.5 0 1 1 19.5 12H2",
            ["sol"] = "O 12 12 4 M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41",
            ["luna"] = "M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z",
            ["nube"] = "M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z",
            ["lluvia"] = "M16 13v8 M8 13v8 M12 15v8 M20 16.58A5 5 0 0 0 18 7h-1.26A8 8 0 1 0 4 15.25",
            ["sol-nube"] = "M12 2v2 M4.93 4.93l1.41 1.41 M20 12h2 M19.07 4.93l-1.41 1.41 M15.95 12.66A4 4 0 0 0 8.4 9.1 M13 22H7a5 5 0 1 1 4.9-6H13a3 3 0 0 1 0 6z",
            ["luna-nube"] = "M13 22H7a5 5 0 1 1 4.9-6H13a3 3 0 0 1 0 6z M10.08 9.03A6 6 0 0 0 18.2 3.6a6 6 0 0 1 3.15 7.45",
            ["reloj"] = "O 12 12 10 M12 6v6l4 2",
            ["play"] = "F:M7 4.5l13 7.5-13 7.5z",
            ["pausa"] = "F:M6 4h4v16H6z M14 4h4v16h-4z",
            ["rayo"] = "M13 2L3 14h9l-1 8 10-12h-9l1-8z",
            ["edificio"] = "M4 21V5a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v16 M2 21h20 M9 7h1 M14 7h1 M9 11h1 M14 11h1 M9 15h1 M14 15h1",
            ["capas"] = "M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5",
            ["alerta"] = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z M12 9v4 M12 17h.01",
            ["info"] = "O 12 12 10 M12 16v-4 M12 8h.01",
            ["ok"] = "O 12 12 10 M8 12l3 3 5-6",
            ["ventana"] = "M4 3h16v18H4z M12 3v18 M4 12h16",
            ["ventana-cerrada"] = "M4 3h16v18H4z M4 8h16 M4 13h16 M4 18h16",
            ["radiador"] = "M5 5v14 M9 5v14 M13 5v14 M17 5v14 M3 8h18 M3 16h18",
            ["radiador-off"] = "M5 5v14 M9 5v14 M13 5v14 M17 5v14 M3 8h18 M3 16h18 M2 2l20 20",
            ["clase"] = "M2 3h20 M3 3v11h18V3 M12 14v4 M8 21l4-3 4 3",
            ["salir"] = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9",
            ["fuego"] = "M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.072-2.143-.224-4.054 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.153.433-2.294 1-3a2.5 2.5 0 0 0 2.5 2.5z",
            ["evento"] = "M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18c0 0-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0",
            ["ajustes"] = "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6",
            ["mapa"] = "M1 6v16l7-4 8 4 7-4V2l-7 4-8-4-7 4z M8 2v16 M16 6v16",
            ["explorar"] = "O 12 12 10 M16.24 7.76l-2.12 6.36-6.36 2.12 2.12-6.36 6.36-2.12z",
            ["control"] = "M3 3h7v7H3z M14 3h7v7h-7z M14 14h7v7h-7z M3 14h7v7H3z",
            ["cerrar"] = "M18 6L6 18 M6 6l12 12",
            ["flecha-izq"] = "M15 18l-6-6 6-6",
            ["flecha-der"] = "M9 18l6-6-6-6",
            ["flecha-abajo"] = "M6 9l6 6 6-6",
            ["conexion"] = "M5 12.55a11 11 0 0 1 14.08 0 M1.42 9a16 16 0 0 1 21.16 0 M8.53 16.11a6 6 0 0 1 6.95 0 M12 20h.01",
            ["sensor"] = "M22 12h-4l-3 9L9 3l-3 9H2",
            ["volver"] = "M19 12H5 M12 19l-7-7 7-7",
            ["ruido"] = "M11 5L6 9H2v6h4l5 4V5z M15.54 8.46a5 5 0 0 1 0 7.07 M19.07 4.93a10 10 0 0 1 0 14.14",
            ["luz"] = "M9 18h6 M10 22h4 M12 2a7 7 0 0 0-4 12.74V17h8v-2.26A7 7 0 0 0 12 2z",
            ["confort"] = "O 12 12 10 M8 14c1 1.33 2.33 2 4 2 1.67 0 3-.67 4-2 M9 9h.01 M15 9h.01",
            ["ninguna"] = "O 12 12 10 M4.93 4.93l14.14 14.14",
            ["mas"] = "M12 5v14 M5 12h14",
            ["menos"] = "M5 12h14",
            ["calendario"] = "M3 4h18v18H3z M16 2v4 M8 2v4 M3 10h18",
            ["velocidad"] = "M13 19l9-7-9-7v14z M2 19l9-7-9-7v14z",
            ["directo"] = "O 12 12 2 M16.24 7.76a6 6 0 0 1 0 8.49 M7.76 16.24a6 6 0 0 1 0-8.49 M19.07 4.93a10 10 0 0 1 0 14.14 M4.93 19.07a10 10 0 0 1 0-14.14",
            ["escenario"] = "M3 3h18v18H3z M3 9h18 M9 21V9",
            ["pantalla"] = "M2 3h20v14H2z M8 21h8 M12 17v4",
            ["mando"] = "M6 12h4 M8 10v4 M15 13h.01 M18 11h.01 M17.32 5H6.68a4 4 0 0 0-3.978 3.59C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258A4 4 0 0 0 17.32 5z",
            ["altavoz"] = "M11 5L6 9H2v6h4l5 4V5z M19.07 4.93a10 10 0 0 1 0 14.14 M15.54 8.46a5 5 0 0 1 0 7.07",
            ["gemelo"] = "M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5",
            ["grafica"] = "M3 3v18h18 M7 15l4-4 3 3 6-6",
            ["planta"] = "M12 2L2 7l10 5 10-5-10-5z",
            ["puerta"] = "M13 4h3a2 2 0 0 1 2 2v14 M2 20h3 M13 20h9 M10 12v.01 M13 4.56v16.32a.6.6 0 0 1-.78.57l-6-1.76A1 1 0 0 1 5.5 18.7V5.3a1 1 0 0 1 .72-.96l6-1.76a.6.6 0 0 1 .78.57z",
            ["herramienta"] = "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
        };
    }
}
