// Lo que se dibuja encima de la escena en la vista cenital:
// - Capa de color (GemeloApp.Capa): en la planta visible de cada edificio cortado, cada local lleva un plano
//   semitransparente a 1 m del suelo (por encima de mesas y sillas, por debajo de las cabezas) con el color de
//   Escalas.ColorEstancia, borde más opaco y costados que se desvanecen hacia el suelo. En los edificios sin
//   cortar, un aura en su contorno (anillo en el suelo y banda vertical) con el valor agregado del edificio.
// - Resaltado: aura del edificio seleccionado (y, más tenue, del de bajo el cursor) y contorno del local.
// Los colores cambian con una transición suave cuando llega un estado nuevo o cambia la capa.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public class CapaColor : MonoBehaviour
    {
        class Estado
        {
            public Color actual, objetivo;
            public readonly MaterialPropertyBlock mpb = new(), mpb2 = new();
        }

        GemeloApp app;
        readonly Dictionary<InfoLocal, Estado> locales = new();
        readonly Dictionary<InfoEdificio, Estado> edificios = new();
        MaterialPropertyBlock mpbContorno, mpbRelleno, mpbContornoSobre, mpbRellenoSobre;
        static readonly Color SinDatos = new(0.62f, 0.64f, 0.66f, 1f);
        static readonly Color ColorSeleccion = new(1f, 0.82f, 0.25f, 1f);   // amarillo UEx, un poco más claro
        bool precalculado;

        void Awake()
        {
            app = GemeloApp.Instancia;
            mpbContorno = new MaterialPropertyBlock();
            mpbRelleno = new MaterialPropertyBlock();
            mpbContornoSobre = new MaterialPropertyBlock();
            mpbRellenoSobre = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            if (app == null) app = GemeloApp.Instancia;
            app.CapaCambiada += AlCambiarCapa;
            app.Cliente.EstadoActualizado += AlActualizar;
            Recalcular(true);
            if (!precalculado) { precalculado = true; StartCoroutine(Precalcular()); }
        }

        void OnDisable()
        {
            if (app == null) return;
            app.CapaCambiada -= AlCambiarCapa;
            if (app.Cliente != null) app.Cliente.EstadoActualizado -= AlActualizar;
        }

        // Las huellas de los edificios cuestan unos milisegundos cada una: se preparan de una en una
        System.Collections.IEnumerator Precalcular()
        {
            // (sobre una copia: entre fotograma y fotograma el registro puede rehacerse)
            foreach (var e in RegistroCampus.Edificios.ToArray())
            {
                GeometriaControl.Aura(e);
                yield return null;
            }
        }

        void AlCambiarCapa(Capa c) => Recalcular(false);
        void AlActualizar(EstadoCampus e) => Recalcular(false);

        void Recalcular(bool deGolpe)
        {
            var capa = app.Capa;
            var cli = app.Cliente;
            foreach (var e in RegistroCampus.Edificios)
            {
                double suma = 0, peso = 0, ocup = 0, cap = 0;
                var confort = new float[6];
                foreach (var l in e.locales)
                {
                    EstadoEstancia est = null;
                    if (cli != null) cli.Estancia(l.clave, out est);
                    var col = Escalas.ColorEstancia(capa, est, l);
                    var s = EstadoDe(locales, l);
                    s.objetivo = col.a <= 0f ? SinDatos.Transparente(capa == Capa.Ninguna ? 0f : 0.55f) : col;
                    if (deGolpe) s.actual = s.objetivo;
                    if (est == null || capa == Capa.Ninguna) continue;
                    float v = Escalas.Valor(capa, est, l);
                    if (float.IsNaN(v)) continue;
                    if (capa == Capa.Ocupacion)
                    {
                        float c = l.local.capacidad > 0 ? l.local.capacidad : Mathf.Max(2f, l.area / 2.5f);
                        ocup += v * c; cap += c;
                    }
                    else if (capa == Capa.Confort) confort[Mathf.Clamp((int)v, 0, 5)] += Mathf.Max(1f, l.area);
                    else { suma += v * Mathf.Max(1f, l.area); peso += Mathf.Max(1f, l.area); }
                }
                float agregado = float.NaN;
                if (capa == Capa.Ocupacion && cap > 0) agregado = (float)(ocup / cap);
                else if (capa == Capa.Confort)
                {
                    int mejor = -1;
                    for (int i = 0; i < 6; i++) if (confort[i] > 0 && (mejor < 0 || confort[i] > confort[mejor])) mejor = i;
                    if (mejor >= 0) agregado = mejor;
                }
                else if (peso > 0) agregado = (float)(suma / peso);
                var se = EstadoDe(edificios, e);
                var ce = Escalas.Color(capa, agregado);
                se.objetivo = ce.a <= 0f ? SinDatos.Transparente(capa == Capa.Ninguna ? 0f : 0.5f) : ce;
                if (deGolpe) se.actual = se.objetivo;
            }
        }

        static Estado EstadoDe<T>(Dictionary<T, Estado> d, T k)
        {
            if (!d.TryGetValue(k, out var s)) d[k] = s = new Estado();
            return s;
        }

        void LateUpdate()
        {
            var control = ControlGemelo.Instancia;
            if (control == null) return;
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-3f * dt);
            var camPos = control.Camara.transform.position;
            var capa = app.Capa;
            var sel = app.Seleccion;
            var sobre = app.Sobre;
            float pulso = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 3.2f);
            var rp = GeometriaControl.Parametros(null);

            // ─── Auras de los edificios
            foreach (var e in RegistroCampus.Edificios)
            {
                var s = EstadoDe(edificios, e);
                s.actual = Color.Lerp(s.actual, s.objetivo, k);
                bool esSel = sel.edificio == e, esSobre = sobre.edificio == e && !esSel;
                bool cortado = app.Corte(e) >= 0;
                if (capa == Capa.Ninguna && !esSel && !esSobre) continue;
                var mallas = GeometriaControl.Aura(e);
                float d = Mathf.Sqrt(e.limites.SqrDistance(camPos));
                float ancho = Mathf.Clamp(d * 0.007f, 0.5f, 5f);

                Color c;
                float aAnillo, aBanda;
                if (capa != Capa.Ninguna)
                {
                    c = s.actual;
                    aAnillo = c.a * (esSel ? 1f : esSobre ? 0.95f : 0.85f);
                    aBanda = c.a * (cortado ? 0.18f : esSel ? 0.75f : esSobre ? 0.6f : 0.5f);
                }
                else
                {
                    c = esSel ? ColorSeleccion : Color.white;
                    aAnillo = esSel ? 0.95f : 0.6f;
                    aBanda = esSel ? (cortado ? 0.25f : 0.55f) : (cortado ? 0.15f : 0.3f);
                }
                if (esSel) { aAnillo *= pulso; aBanda *= pulso; }
                s.mpb.SetColor(GeometriaControl.IdColor, c.Transparente(aAnillo));
                s.mpb.SetFloat(GeometriaControl.IdAncho, ancho * (esSel ? 1.4f : 1f));
                rp.matProps = s.mpb;
                Graphics.RenderMesh(rp, mallas, 0, Matrix4x4.identity);
                s.mpb2.SetColor(GeometriaControl.IdColor, c.Transparente(aBanda));
                s.mpb2.SetFloat(GeometriaControl.IdAncho, 0f);
                rp.matProps = s.mpb2;
                Graphics.RenderMesh(rp, mallas, 1, Matrix4x4.identity);
            }

            // ─── Capa de color de las estancias de la planta visible de los edificios cortados
            if (capa != Capa.Ninguna)
            {
                foreach (var e in RegistroCampus.Edificios)
                {
                    int corte = app.Corte(e);
                    if (corte < 0) continue;
                    foreach (var p in e.plantas)
                    {
                        if (p.indice != corte) continue;
                        foreach (var l in p.locales)
                        {
                            var s = EstadoDe(locales, l);
                            s.actual = Color.Lerp(s.actual, s.objetivo, k);
                            if (s.actual.a < 0.01f) continue;
                            s.mpb.SetColor(GeometriaControl.IdColor, s.actual);
                            s.mpb.SetFloat(GeometriaControl.IdAncho, 0f);
                            rp.matProps = s.mpb;
                            Graphics.RenderMesh(rp, GeometriaControl.MallaLocal(l), 0, Matrix4x4.identity);
                        }
                    }
                }
            }

            // ─── Contorno del local seleccionado y del de bajo el cursor
            DibujarLocal(sel.local, true, capa, pulso, ref rp);
            if (sobre.local != null && sobre.local != sel.local) DibujarLocal(sobre.local, false, capa, 1f, ref rp);
        }

        void DibujarLocal(InfoLocal l, bool seleccionado, Capa capa, float pulso, ref RenderParams rp)
        {
            if (l == null || app.Corte(l.edificio) != l.planta.indice) return;
            var malla = GeometriaControl.MallaLocal(l);
            var mC = seleccionado ? mpbContorno : mpbContornoSobre;
            mC.SetColor(GeometriaControl.IdColor, new Color(1f, 1f, 1f, seleccionado ? pulso : 0.7f));
            mC.SetFloat(GeometriaControl.IdAncho, 0f);
            rp.matProps = mC;
            // El contorno, un poco por encima del plano de la capa para que no se mezcle con él
            var subir = Matrix4x4.Translate(Vector3.up * 0.02f);
            Graphics.RenderMesh(rp, malla, 1, subir);
            {
                // Sin capa, relleno amarillo; con capa, un velo blanco que lo aclara sin tapar su color
                var mR = seleccionado ? mpbRelleno : mpbRellenoSobre;
                var col = capa == Capa.Ninguna
                    ? (seleccionado ? ColorSeleccion.Transparente(0.55f) : new Color(1f, 1f, 1f, 0.3f))
                    : new Color(1f, 1f, 1f, seleccionado ? 0.3f * pulso : 0.18f);
                mR.SetColor(GeometriaControl.IdColor, col);
                mR.SetFloat(GeometriaControl.IdAncho, 0f);
                rp.matProps = mR;
                Graphics.RenderMesh(rp, malla, 0, Matrix4x4.identity);
            }
        }
    }

    static class ColorExt
    {
        public static Color Transparente(this Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
