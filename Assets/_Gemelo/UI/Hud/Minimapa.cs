// Minimapa vectorial del HUD (Painter2D). El mapa del campus se dibuja una sola vez en metros (huella de los
// edificios a partir de los polígonos de sus locales) y la planta en la que está el jugador, coloreada por usos,
// al cambiar de planta; cada fotograma solo se mueve, gira y escala (sin volver a teselar). Gira con la cámara:
// arriba es hacia donde mira. La flecha amarilla es el jugador.
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class Minimapa : VisualElement
    {
        readonly VisualElement mundo, interior, flecha;
        readonly Label norte;
        Rect ext;                       // extensión XZ del campus (m)
        InfoPlanta plantaDibujada;
        InfoLocal localDibujado;
        float escala = 1.4f;            // px por metro (suavizada)
        bool construido;

        public Minimapa()
        {
            AddToClassList("minimapa");
            pickingMode = PickingMode.Ignore;
            mundo = El(this);
            mundo.style.position = Position.Absolute;
            mundo.generateVisualContent += DibujarMundo;
            interior = El(mundo);
            interior.style.position = Position.Absolute;
            interior.style.left = 0; interior.style.top = 0; interior.style.right = 0; interior.style.bottom = 0;
            interior.generateVisualContent += DibujarInterior;
            flecha = El(this);
            flecha.style.position = Position.Absolute;
            flecha.style.width = 26; flecha.style.height = 26;
            flecha.generateVisualContent += DibujarFlecha;
            norte = Txt(this, "N", "minimapa__norte");
            SinPicking(this);
        }

        void Construir()
        {
            if (construido || RegistroCampus.Edificios.Count == 0) return;
            construido = true;
            // Extensión: el terreno (945 × 888 m) o, si no, la de los edificios con margen
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var e in RegistroCampus.Edificios)
            {
                minX = Mathf.Min(minX, e.limites.min.x); minZ = Mathf.Min(minZ, e.limites.min.z);
                maxX = Mathf.Max(maxX, e.limites.max.x); maxZ = Mathf.Max(maxZ, e.limites.max.z);
            }
            ext = Rect.MinMaxRect(minX - 400, minZ - 400, maxX + 400, maxZ + 400);
            mundo.style.width = ext.width;
            mundo.style.height = ext.height;
            mundo.MarkDirtyRepaint();
        }

        Vector2 AMapa(Vector2 xz) => new(xz.x - ext.xMin, ext.yMax - xz.y);

        static InfoPlanta PlantaBaja(InfoEdificio e)
        {
            foreach (var p in e.plantas) if (p.nombre == "P0") return p;
            return e.plantas.Count > 0 ? e.plantas[0] : null;
        }

        void DibujarMundo(MeshGenerationContext ctx)
        {
            if (!construido) return;
            var p = ctx.painter2D;
            // Huella de cada edificio: los locales de su planta baja, del mismo color (se funden en una silueta)
            p.fillColor = new Color(0.78f, 0.80f, 0.84f, 0.30f);
            foreach (var e in RegistroCampus.Edificios)
            {
                var pb = PlantaBaja(e);
                if (pb == null) continue;
                foreach (var l in pb.locales) Poligono(p, l.local.poligono, true);
            }
            // Contorno ligero
            p.strokeColor = new Color(1, 1, 1, 0.18f);
            p.lineWidth = 0.5f;
            foreach (var e in RegistroCampus.Edificios)
            {
                var pb = PlantaBaja(e);
                if (pb == null) continue;
                foreach (var l in pb.locales) Poligono(p, l.local.poligono, false);
            }
        }

        void DibujarInterior(MeshGenerationContext ctx)
        {
            var pl = plantaDibujada;
            if (pl == null || !construido) return;
            var p = ctx.painter2D;
            // Fondo oscuro de la planta, para que los usos resalten sobre la huella
            p.fillColor = new Color(0.08f, 0.09f, 0.1f, 0.85f);
            foreach (var l in pl.locales) Poligono(p, l.local.poligono, true);
            foreach (var l in pl.locales)
            {
                var c = Formato.ColorGrupo(Formato.Grupo(l.local.uso));
                c.a = l == localDibujado ? 1f : 0.62f;
                p.fillColor = c;
                Poligono(p, l.local.poligono, true, 0.18f);
            }
            if (localDibujado != null && localDibujado.planta == pl)
            {
                p.strokeColor = Color.white;
                p.lineWidth = 0.45f;
                Poligono(p, localDibujado.local.poligono, false);
            }
        }

        void Poligono(Painter2D p, Vector2[] pol, bool relleno, float encoger = 0f)
        {
            if (pol == null || pol.Length < 3) return;
            Vector2 c = Vector2.zero;
            if (encoger > 0) { foreach (var v in pol) c += v; c /= pol.Length; }
            p.BeginPath();
            for (int i = 0; i < pol.Length; i++)
            {
                var v = pol[i];
                if (encoger > 0) v += (c - v).normalized * encoger;
                var q = AMapa(v);
                if (i == 0) p.MoveTo(q); else p.LineTo(q);
            }
            p.ClosePath();
            if (relleno) p.Fill(); else p.Stroke();
        }

        void DibujarFlecha(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var c = new Vector2(13, 13);
            p.fillColor = new Color(0, 0, 0, 0.35f);
            p.BeginPath(); p.Arc(c, 12, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = Escalas.Amarillo;
            p.strokeColor = new Color(0.13f, 0.13f, 0.13f, 1f);
            p.lineWidth = 1.5f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(c + new Vector2(0, -10));
            p.LineTo(c + new Vector2(7, 8));
            p.LineTo(c + new Vector2(0, 4));
            p.LineTo(c + new Vector2(-7, 8));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        /// <summary>Coloca el mapa: jugador en el centro, mirando hacia arriba la cámara.</summary>
        public void Actualizar(Vector3 pos, float yawCamara, float yawJugador, InfoLocal local)
        {
            Construir();
            if (!construido) return;
            var planta = local?.planta;
            if (planta == null)
            {
                // Fuera de un local conocido: planta del edificio en el que está (si está dentro de su volumen)
                var e = RegistroCampus.EdificioEn(pos);
                if (e != null)
                    foreach (var pl in e.plantas)
                        if (Mathf.Abs(pos.y - pl.cota - 1.2f) < 1.9f) planta = pl;
            }
            if (planta != plantaDibujada || local != localDibujado)
            {
                plantaDibujada = planta;
                localDibujado = local;
                interior.MarkDirtyRepaint();
            }

            float objetivo = planta != null ? 3.6f : 1.5f;
            escala = Mathf.Lerp(escala, objetivo, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || w < 1) return;
            var centro = new Vector2(w / 2f, h / 2f);
            var u = AMapa(new Vector2(pos.x, pos.z));
            mundo.style.transformOrigin = new TransformOrigin(u.x, u.y);
            mundo.style.translate = new Translate(centro.x - u.x, centro.y - u.y);
            mundo.style.rotate = new Rotate(Angle.Degrees(-yawCamara));
            mundo.style.scale = new Scale(new Vector2(escala, escala));

            flecha.style.left = centro.x - 13;
            flecha.style.top = centro.y - 13;
            flecha.style.rotate = new Rotate(Angle.Degrees(yawJugador - yawCamara));

            // Norte en el borde, girando con el mapa
            float a = -yawCamara * Mathf.Deg2Rad;
            float r = w / 2f - 14;
            norte.style.left = centro.x + Mathf.Sin(a) * r;
            norte.style.top = centro.y - Mathf.Cos(a) * r;
        }
    }
}
