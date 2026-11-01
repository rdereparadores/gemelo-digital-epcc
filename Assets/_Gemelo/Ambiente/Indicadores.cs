// Indicadores flotantes sobre las estancias con alertas (las del estado del servicio más las de los eventos:
// fuga de agua, apagón, incendio), con el color de Escalas.ColorSeveridad y el glifo de la alerta más grave.
// - Tercera persona: los de las estancias cercanas (si la cámara está dentro de un edificio, solo los de su
//   planta) y uno por edificio para los edificios lejanos.
// - Vista cenital y menú: uno por edificio (con el número de alertas); si el edificio está cortado
//   (GemeloApp.CorteCambiado), uno por estancia de la planta visible.
// Todo se dibuja instanciado (Gemelo/Icono). Se ocultan con Ajustes.indicadoresFlotantes.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Indicadores : MonoBehaviour
    {
        const float RadioCerca = 42f;

        struct Marca
        {
            public int glifo, cuenta;
            public string severidad;
            public int rango;   // 2 crítica, 1 aviso, 0 info
        }

        AmbienteGemelo amb;
        Material mat;
        Mesh cuadro;
        MaterialPropertyBlock mpb;
        readonly Matrix4x4[] matrices = new Matrix4x4[1023];
        readonly Vector4[] colores = new Vector4[1023];
        readonly Vector4[] datos = new Vector4[1023];
        readonly Vector4[] extra = new Vector4[1023];
        int n;

        readonly Dictionary<InfoLocal, Marca> porLocal = new();
        readonly Dictionary<InfoEdificio, Marca> porEdificio = new();
        float siguiente;

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            var sh = Resources.Load<Shader>("Ambiente/GemeloIcono");
            if (sh != null)
            {
                mat = new Material(sh) { name = "Indicadores (gemelo)", enableInstancing = true };
                mat.SetTexture("_MainTex", IconosAmbiente.Atlas);
            }
            cuadro = LucesInteriores.CrearCuadro();
            mpb = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (cuadro != null) Destroy(cuadro);
        }

        void Update()
        {
            if (mat == null || amb == null) return;
            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 0.5f;
                Recoger();
            }
            if (!Ajustes.Actual.indicadoresFlotantes) return;
            Componer();
            if (n == 0) return;
            mpb.SetVectorArray("_Color", colores);
            mpb.SetVectorArray("_Datos", datos);
            mpb.SetVectorArray("_Extra", extra);
            var rp = new RenderParams(mat)
            {
                matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                worldBounds = new Bounds(new Vector3(400, 20, 540), new Vector3(3000, 1000, 3000)),
            };
            Graphics.RenderMeshInstanced(rp, cuadro, 0, matrices, n);
        }

        // ─── Alertas por estancia y por edificio

        static int Rango(string s) => s == "critical" ? 2 : s == "warning" ? 1 : 0;

        static void Sumar(Dictionary<InfoLocal, Marca> d, InfoLocal l, string codigo, string sev)
        {
            d.TryGetValue(l, out var m);
            int r = Rango(sev);
            if (m.cuenta == 0 || r > m.rango) { m.glifo = IconosAmbiente.Glifo(codigo); m.severidad = sev; m.rango = r; }
            m.cuenta++;
            d[l] = m;
        }

        void Recoger()
        {
            porLocal.Clear();
            porEdificio.Clear();
            var cli = ClienteGemelo.Instancia;
            if (cli != null && cli.Estado != null)
                foreach (var st in cli.Estancias)
                {
                    if (st.alerts == null || st.alerts.Count == 0) continue;
                    var l = RegistroCampus.Local(st.unity_room);
                    if (l == null) continue;
                    foreach (var a in st.alerts) Sumar(porLocal, l, a.code, a.severity);
                }
            foreach (var l in amb.LocalesConFuga) Sumar(porLocal, l, "water_leak", "critical");
            foreach (var l in amb.LocalesSinLuz) Sumar(porLocal, l, "power_outage", "warning");
            foreach (var l in amb.LocalesConHumo) Sumar(porLocal, l, "fire_alarm", "critical");

            foreach (var kv in porLocal)
            {
                var e = kv.Key.edificio;
                porEdificio.TryGetValue(e, out var m);
                var x = kv.Value;
                if (m.cuenta == 0 || x.rango > m.rango) { m.glifo = x.glifo; m.severidad = x.severidad; m.rango = x.rango; }
                m.cuenta += x.cuenta;
                porEdificio[e] = m;
            }
            foreach (var e in RegistroCampus.Edificios)
            {
                bool alarma = amb.AlarmaEn(e), apagon = amb.EdificiosSinLuz.Contains(e.codigo), fuga = amb.EdificiosConFuga.Contains(e);
                if (!alarma && !apagon && !fuga) continue;
                porEdificio.TryGetValue(e, out var m);
                // El suceso del edificio manda sobre las alertas de sus estancias
                if (alarma) { m.glifo = IconosAmbiente.Fuego; m.severidad = "critical"; m.rango = 3; }
                else if (fuga && m.rango < 3) { m.glifo = IconosAmbiente.Gota; m.severidad = "critical"; m.rango = 2; }
                else if (apagon && m.rango < 2) { m.glifo = IconosAmbiente.Rayo; m.severidad = "warning"; m.rango = 2; }
                m.cuenta = Mathf.Max(1, m.cuenta);
                porEdificio[e] = m;
            }
        }

        // ─── Qué se ve ahora

        void Componer()
        {
            n = 0;
            var app = GemeloApp.Instancia;
            var cam = Camera.main;
            if (app == null || cam == null) return;
            var pc = cam.transform.position;

            if (app.Modo == Modo.TerceraPersona)
            {
                var actual = app.LocalActual;
                foreach (var kv in porLocal)
                {
                    var l = kv.Key;
                    if (!l.planta.raiz.gameObject.activeInHierarchy) continue;
                    if (actual != null && (l.edificio != actual.edificio || l.planta != actual.planta)) continue;
                    var p = l.centro + Vector3.up * 2.1f;
                    // Desde fuera, solo las cercanas y que no sean meramente informativas
                    float radio = actual != null ? RadioCerca : 28f;
                    if ((p - pc).sqrMagnitude > radio * radio || actual == null && kv.Value.rango < 1) continue;
                    Poner(p, kv.Value, 0.32f, 0.032f, false);
                }
                if (actual == null)
                    foreach (var kv in porEdificio)
                    {
                        var b = kv.Key.limites;
                        if (b.SqrDistance(pc) < 50f * 50f) continue;
                        Poner(new Vector3(b.center.x, b.max.y + 3f, b.center.z), kv.Value, 2f, 0.03f, true);
                    }
                return;
            }

            // Cenital y menú
            foreach (var kv in porEdificio)
            {
                var e = kv.Key;
                int corte = app.Corte(e);
                if (corte >= 0) continue;
                var b = e.limites;
                Poner(new Vector3(b.center.x, b.max.y + 4f, b.center.z), kv.Value, 3f, 0.04f, true);
            }
            foreach (var kv in porLocal)
            {
                var l = kv.Key;
                int corte = app.Corte(l.edificio);
                if (corte < 0 || l.planta.indice != corte) continue;
                Poner(l.centro + Vector3.up * 1.6f, kv.Value, 0.8f, 0.025f, false);
            }
        }

        void Poner(Vector3 p, Marca m, float minimo, float porMetro, bool conCuenta)
        {
            if (n >= matrices.Length - 3) return;
            var c = Escalas.ColorSeveridad(m.severidad);
            bool critica = m.rango >= 2 && m.severidad == "critical";
            matrices[n] = Matrix4x4.Translate(p);
            colores[n] = new Vector4(c.r, c.g, c.b, 1);
            datos[n] = new Vector4(m.glifo, critica ? 1 : 0, minimo, porMetro);
            extra[n] = Vector4.zero;
            n++;
            if (!conCuenta || m.cuenta <= 1) return;
            // Número de alertas: una o dos cifras (o «9+» abreviado a dos burbujas)
            int cuenta = Mathf.Min(m.cuenta, 99);
            if (cuenta >= 10)
            {
                Burbuja(p, IconosAmbiente.Digito0 + cuenta / 10, 0.48f, minimo, porMetro);
                Burbuja(p, IconosAmbiente.Digito0 + cuenta % 10, 0.78f, minimo, porMetro);
            }
            else Burbuja(p, IconosAmbiente.Digito0 + cuenta, 0.62f, minimo, porMetro);
        }

        void Burbuja(Vector3 p, int glifo, float dx, float minimo, float porMetro)
        {
            matrices[n] = Matrix4x4.Translate(p);
            colores[n] = new Vector4(1, 1, 1, 1);
            datos[n] = new Vector4(glifo, 0, minimo, porMetro);
            extra[n] = new Vector4(1, dx, 0.6f, 0);
            n++;
        }
    }
}
