// Ayudas para montar la interfaz por código (UI Toolkit) con las clases de Gemelo.uss: elementos, textos,
// botones, paneles de cristal con sombra suave y transiciones de aparecer y desaparecer.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gemelo
{
    public static class Elem
    {
        public static VisualElement El(params string[] clases)
        {
            var e = new VisualElement();
            foreach (var c in clases) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static VisualElement El(VisualElement padre, params string[] clases)
        {
            var e = El(clases);
            padre?.Add(e);
            return e;
        }

        public static Label Txt(VisualElement padre, string texto, params string[] clases)
        {
            var l = new Label(texto);
            foreach (var c in clases) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            padre?.Add(l);
            return l;
        }

        public static Icono Ico(VisualElement padre, string nombre, float tam = 18f)
        {
            var i = new Icono(nombre, tam);
            padre?.Add(i);
            return i;
        }

        /// <summary>Botón con icono opcional y texto (clic con cualquier puntero).</summary>
        public static VisualElement Boton(VisualElement padre, string texto, Action alPulsar, string icono = null, params string[] clases)
        {
            var b = El("boton");
            foreach (var c in clases) if (!string.IsNullOrEmpty(c)) b.AddToClassList(c);
            if (icono != null) b.Add(new Icono(icono, 18));
            if (!string.IsNullOrEmpty(texto)) Txt(b, texto);
            Clic(b, alPulsar);
            padre?.Add(b);
            return b;
        }

        public static void Clic(VisualElement e, Action alPulsar)
        {
            if (alPulsar == null) return;
            e.focusable = true;
            e.RegisterCallback<ClickEvent>(ev => { alPulsar(); ev.StopPropagation(); });
            e.RegisterCallback<NavigationSubmitEvent>(ev => { alPulsar(); ev.StopPropagation(); });
        }

        public static void Clase(VisualElement e, string clase, bool si)
        {
            if (si) e.AddToClassList(clase); else e.RemoveFromClassList(clase);
        }

        public static void Texto(Label l, string t)
        {
            if (l != null && l.text != t) l.text = t;
        }

        public static void Mostrar(VisualElement e, bool si) => e.style.display = si ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Panel de cristal: fondo translúcido, borde fino, brillo arriba y sombra suave.</summary>
        public static VisualElement Panel(VisualElement padre, params string[] clases)
        {
            var p = El("panel");
            foreach (var c in clases) if (!string.IsNullOrEmpty(c)) p.AddToClassList(c);
            Sombra(p);
            var brillo = El(p, "brillo");
            brillo.pickingMode = PickingMode.Ignore;
            brillo.style.backgroundImage = new StyleBackground(TexBrillo);
            padre?.Add(p);
            return p;
        }

        public static void Sombra(VisualElement p, float radio = 16f)
        {
            var s = El(p, "sombra");
            s.pickingMode = PickingMode.Ignore;
            const int pad = Pad;
            s.style.left = -pad + 1; s.style.right = -pad + 1;
            s.style.top = -pad + 5; s.style.bottom = -pad - 5;
            s.style.backgroundImage = new StyleBackground(TexSombra);
            int corte = pad + 16;
            s.style.unitySliceLeft = corte; s.style.unitySliceRight = corte;
            s.style.unitySliceTop = corte; s.style.unitySliceBottom = corte;
        }

        // ─── Aparecer y desaparecer con transición (clases .anim y .anim--oculto*)
        public static void Animable(VisualElement e, string claseOculto = "anim--oculto")
        {
            e.AddToClassList("anim");
            e.AddToClassList(claseOculto);
            e.userData = claseOculto;
            e.style.display = DisplayStyle.None;
        }

        public static void Ver(VisualElement e, bool si)
        {
            string oculto = e.userData as string ?? "anim--oculto";
            if (si)
            {
                if (e.style.display == DisplayStyle.Flex && !e.ClassListContains(oculto)) return;
                e.style.display = DisplayStyle.Flex;
                // Un fotograma después, para que la transición arranque desde el estado oculto
                e.schedule.Execute(() => e.RemoveFromClassList(oculto)).StartingIn(16);
            }
            else
            {
                if (e.style.display == DisplayStyle.None || e.ClassListContains(oculto)) return;
                e.AddToClassList(oculto);
                e.schedule.Execute(() => { if (e.ClassListContains(oculto)) e.style.display = DisplayStyle.None; }).StartingIn(300);
            }
        }

        public static bool Visible(VisualElement e) => e.style.display != DisplayStyle.None && !e.ClassListContains(e.userData as string ?? "anim--oculto");

        public static void SinPicking(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var h in e.Children()) SinPicking(h);
        }

        public static ScrollView Desplazable(VisualElement padre)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.AddToClassList("desplazable");
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.verticalScrollerVisibility = ScrollerVisibility.Auto;
            padre?.Add(sv);
            return sv;
        }

        // ─── Texturas generadas: sombra difusa (9-slice, hueca por dentro) y brillo del cristal
        const int Pad = 22;
        static Texture2D texSombra, texBrillo;

        static Texture2D TexSombra
        {
            get
            {
                if (texSombra != null) return texSombra;
                const int radio = 16, n = 2 * (Pad + radio) + 8;
                texSombra = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[n * n];
                // Rectángulo redondeado interior (el panel) desplazado de forma que la sombra cae hacia abajo
                float x0 = Pad, y0 = Pad, x1 = n - Pad, y1 = n - Pad;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, x0 + radio, x1 - radio), cy = Mathf.Clamp(y + 0.5f, y0 + radio, y1 - radio);
                    float dist = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude - radio; // >0 fuera del panel
                    float a = dist <= 0 ? 0f : Mathf.Pow(1f - Mathf.Clamp01(dist / Pad), 2.2f) * 0.55f;
                    px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
                texSombra.SetPixels32(px);
                texSombra.Apply();
                return texSombra;
            }
        }

        static Texture2D TexBrillo
        {
            get
            {
                if (texBrillo != null) return texBrillo;
                texBrillo = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                for (int y = 0; y < 32; y++)
                    texBrillo.SetPixel(0, y, new Color(1, 1, 1, 0.055f * Mathf.Pow(y / 31f, 2f)));
                texBrillo.Apply();
                return texBrillo;
            }
        }

        /// <summary>Textura horizontal con el gradiente de una capa (para la leyenda).</summary>
        public static Texture2D Gradiente(Capa c)
        {
            const int n = 128;
            var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var r = Escalas.Rango(c);
            for (int i = 0; i < n; i++)
            {
                var col = c == Capa.Confort ? Escalas.ColorConfort(Mathf.Min(5, i * 6 / n)) : Escalas.Color(c, Mathf.Lerp(r.x, r.y, i / (n - 1f)));
                col.a = 1;
                t.SetPixel(i, 0, col);
            }
            t.filterMode = c == Capa.Confort ? FilterMode.Point : FilterMode.Bilinear;
            t.Apply();
            return t;
        }

        public static Color ConAlfa(Color c, float a) { c.a = a; return c; }
    }
}
