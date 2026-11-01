// Emergencias: alarma de incendio (sirena sintetizada, estroboscópicos rojos y blancos en los pasillos y
// vestíbulos del edificio más cercanos a la cámara y destellos en las puertas de fachada, que se ven desde fuera
// y desde la vista cenital) y fugas de agua (charco que crece en el suelo del local, con el sombreador Gemelo/Agua:
// suelo visto a través del agua, reflejos en espacio de pantalla o del techo, brillos de las luminarias y anillos del
// goteo, sin sondas de reflexión). El apagón lo llevan LucesInteriores (luces y señales de salida) y los Indicadores.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Emergencias : MonoBehaviour
    {
        const int MaxEstrobos = 8;

        AmbienteGemelo amb;
        AudioClip sirena;
        readonly Dictionary<InfoEdificio, AudioSource> sirenas = new();
        readonly List<Light> estrobos = new();
        readonly List<(Vector3 pos, int i)> puntos = new();
        readonly List<InfoLocal> circulaciones = new();
        float siguiente;

        // Destellos (billboards aditivos)
        Material matBrillo;
        Mesh cuadro;
        MaterialPropertyBlock mpb;
        readonly Matrix4x4[] matrices = new Matrix4x4[256];
        readonly Vector4[] colores = new Vector4[256];
        readonly Vector4[] formas = new Vector4[256];
        readonly List<(Vector3 pos, int i)> balizas = new();

        // Charcos
        class Charco { public InfoLocal l; public GameObject go; public MeshRenderer rend; public float radio, maximo; public bool vivo; }
        readonly Dictionary<InfoLocal, Charco> charcos = new();
        Material matAgua;
        MaterialPropertyBlock mpbAgua;
        LucesInteriores luces;
        static readonly int IdTecho = Shader.PropertyToID("_Techo");
        static readonly Color ColorTecho = new(0.62f, 0.60f, 0.56f);

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            amb.EventosCambiados += Revisar;
            sirena = SintesisAudio.Sirena();
            for (int i = 0; i < MaxEstrobos; i++)
            {
                var go = new GameObject("Estroboscópico");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 8f;
                l.shadows = LightShadows.None;
                l.intensity = 0;
                go.SetActive(false);
                estrobos.Add(l);
            }
            var sh = Resources.Load<Shader>("Ambiente/GemeloBrillo");
            if (sh != null) matBrillo = new Material(sh) { name = "Destellos (gemelo)", enableInstancing = true };
            cuadro = LucesInteriores.CrearCuadro();
            mpb = new MaterialPropertyBlock();
            var shAgua = Resources.Load<Shader>("Ambiente/GemeloAgua");
            if (shAgua != null) matAgua = new Material(shAgua) { name = "Agua (gemelo)" };
            mpbAgua = new MaterialPropertyBlock();
            luces = GetComponent<LucesInteriores>();
        }

        void OnDestroy()
        {
            if (amb != null) amb.EventosCambiados -= Revisar;
            if (matBrillo != null) Destroy(matBrillo);
            if (cuadro != null) Destroy(cuadro);
            if (matAgua != null) Destroy(matAgua);
            foreach (var c in charcos.Values) if (c.go != null) Destroy(c.go);
        }

        void Revisar()
        {
            // Charcos nuevos y los que se secan
            foreach (var l in amb.LocalesConFuga)
                if (!charcos.TryGetValue(l, out var c) || !c.vivo)
                {
                    if (c == null) charcos[l] = c = CrearCharco(l);
                    c.vivo = true;
                }
            foreach (var c in charcos.Values)
                if (!amb.LocalesConFuga.Contains(c.l)) c.vivo = false;
        }

        void Update()
        {
            if (amb == null) return;
            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 1f;
                Sirenas();
                ElegirEstrobos();
            }
            Destellos();
            Charcos();
        }

        // ─── Alarma de incendio

        void Sirenas()
        {
            float vol = Ajustes.Actual.volumenEfectos * Ajustes.Actual.volumenGeneral;
            var app = GemeloApp.Instancia;
            foreach (var e in RegistroCampus.Edificios)
            {
                bool alarma = amb.AlarmaEn(e);
                sirenas.TryGetValue(e, out var src);
                if (alarma && src == null)
                {
                    var go = new GameObject("Sirena " + e.codigo);
                    go.transform.SetParent(transform, false);
                    go.transform.position = e.limites.center;
                    src = go.AddComponent<AudioSource>();
                    src.clip = sirena;
                    src.loop = true;
                    src.spatialBlend = 1f;
                    src.rolloffMode = AudioRolloffMode.Logarithmic;
                    src.minDistance = Mathf.Max(e.limites.extents.x, e.limites.extents.z) * 0.8f;
                    src.maxDistance = 400f;
                    src.dopplerLevel = 0;
                    src.Play();
                    sirenas[e] = src;
                    app?.Avisar($"Alarma de incendio en {e.nombre}", NivelAviso.Critico);
                }
                else if (!alarma && src != null)
                {
                    Destroy(src.gameObject);
                    sirenas.Remove(e);
                }
                if (src != null)
                {
                    // Dentro del edificio suena por todas partes
                    bool dentro = app != null && app.LocalActual != null && app.LocalActual.edificio == e;
                    src.spatialBlend = dentro ? 0.2f : 1f;
                    src.volume = 0.8f * vol;
                }
            }
        }

        void ElegirEstrobos()
        {
            puntos.Clear();
            balizas.Clear();
            if (!AmbienteGemelo.PosicionCamara(out var pc) || amb.EdificiosEnAlarma.Count == 0 && sirenas.Count == 0)
            {
                foreach (var l in estrobos) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
                return;
            }
            circulaciones.Clear();
            foreach (var e in sirenas.Keys)
            {
                foreach (var l in e.locales)
                    if (l.local.uso is "pasillo" or "vestibulo" or "hall" or "distribuidor" && l.planta.raiz.gameObject.activeInHierarchy)
                        circulaciones.Add(l);
                // Balizas en las puertas de fachada (se ven desde fuera)
                foreach (var p in e.puertasExteriores)
                    balizas.Add((RegistroCampus.CentroPuerta(p) + Vector3.up * (p.transform.lossyScale.y + 0.45f), balizas.Count));
            }
            circulaciones.Sort((a, b) => (a.centro - pc).sqrMagnitude.CompareTo((b.centro - pc).sqrMagnitude));
            for (int i = 0; i < estrobos.Count; i++)
            {
                var l = estrobos[i];
                bool usar = i < circulaciones.Count && (circulaciones[i].centro - pc).sqrMagnitude < 70f * 70f;
                if (usar)
                {
                    var c = circulaciones[i];
                    var pos = c.centro + Vector3.up * 2.45f;
                    l.transform.position = pos;
                    puntos.Add((pos, i));
                }
                if (l.gameObject.activeSelf != usar) l.gameObject.SetActive(usar);
            }
        }

        void Destellos()
        {
            if (puntos.Count == 0 && balizas.Count == 0) return;
            float t = Time.time;
            int n = 0;
            foreach (var (pos, i) in puntos)
            {
                // Un destello de 70 ms por segundo, cada estroboscópico con su fase; rojos y blancos alternos
                float f = Destello(t, i);
                var col = i % 2 == 0 ? new Color(1f, 0.08f, 0.05f) : new Color(1f, 0.95f, 0.9f);
                estrobos[i].color = col;
                estrobos[i].intensity = f * 6f;
                if (n < matrices.Length && f > 0)
                {
                    matrices[n] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.9f);
                    colores[n] = new Vector4(col.r, col.g, col.b, 6f * f);
                    formas[n] = new Vector4(1, 0, 0, 0);
                    n++;
                }
            }
            foreach (var (pos, i) in balizas)
            {
                float f = Destello(t, i + 3);
                if (f <= 0 || n >= matrices.Length) continue;
                var col = i % 2 == 0 ? new Color(1f, 0.1f, 0.05f) : new Color(1f, 0.95f, 0.9f);
                matrices[n] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 3f);
                colores[n] = new Vector4(col.r, col.g, col.b, 14f * f);
                formas[n] = new Vector4(1, 0, 0, 0);
                n++;
            }
            if (n == 0 || matBrillo == null) return;
            mpb.SetVectorArray("_Color", colores);
            mpb.SetVectorArray("_Forma", formas);
            var rp = new RenderParams(matBrillo)
            {
                matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                worldBounds = new Bounds(new Vector3(400, 20, 540), new Vector3(3000, 1000, 3000)),
            };
            Graphics.RenderMeshInstanced(rp, cuadro, 0, matrices, n);
        }

        static float Destello(float t, int i)
        {
            if (AmbienteGemelo.Prueba.destellos) return 1f;
            float fase = Mathf.Repeat(t * 1.05f + i * 0.37f, 1f);
            return fase < 0.07f ? 1f - fase / 0.07f * 0.4f : 0f;
        }

        // ─── Fugas de agua

        Charco CrearCharco(InfoLocal l)
        {
            var c = new Charco { l = l };
            // Hasta dónde puede crecer: lo que deja la pared más cercana al centro (y no más de 2,2 m)
            var pol = l.local.poligono;
            float max = 2.2f;
            if (pol != null && pol.Length >= 3)
            {
                var q = new Vector2(l.centro.x, l.centro.z);
                for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
                {
                    Vector2 a = pol[j], b = pol[i], ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    max = Mathf.Min(max, (a + ab * t - q).magnitude * 0.85f);
                }
            }
            c.maximo = Mathf.Max(0.5f, max);
            c.go = new GameObject("Charco " + l.clave);
            c.go.transform.SetParent(l.planta.raiz, true);
            c.go.transform.position = l.centro + Vector3.up * 0.008f;
            c.go.transform.rotation = Quaternion.Euler(0, Mathf.Abs(l.centro.x * 37f) % 360f, 0);
            c.go.AddComponent<MeshFilter>().sharedMesh = MallaCharco(l.centro.x + l.centro.z);
            var r = c.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = matAgua;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            c.rend = r;
            c.go.transform.localScale = Vector3.one * 0.05f;
            return c;
        }

        void Charcos()
        {
            float dt = Time.deltaTime;
            List<InfoLocal> secos = null;
            foreach (var c in charcos.Values)
            {
                // Crece hasta su tamaño en un par de minutos y, al cerrar la fuga, se seca despacio
                float obj = c.vivo ? c.maximo : 0f;
                c.radio = Mathf.MoveTowards(c.radio, obj, dt * (c.vivo ? c.maximo / 90f : c.maximo / 40f));
                if (c.go != null)
                {
                    c.go.transform.localScale = new Vector3(c.radio, 1, c.radio);
                    // El techo que refleja: la luz del local (o la natural que entra de día), apagado en un apagón
                    float enc = luces != null ? luces.Encendido(c.l) : 1f;
                    float luz = Mathf.Max(enc, amb.SinLuz(c.l) ? 0f : amb.LuzNatural * 0.7f);
                    mpbAgua.SetColor(IdTecho, ColorTecho * (0.06f + 0.8f * luz));
                    c.rend.SetPropertyBlock(mpbAgua);
                }
                if (!c.vivo && c.radio <= 0.001f) (secos ??= new List<InfoLocal>()).Add(c.l);
            }
            if (secos != null)
                foreach (var l in secos)
                {
                    if (charcos[l].go != null)
                    {
                        Destroy(charcos[l].go.GetComponent<MeshFilter>().sharedMesh);
                        Destroy(charcos[l].go);
                    }
                    charcos.Remove(l);
                }
        }

        /// <summary>Disco irregular de radio ~1 (contorno con ruido) sobre el plano XZ.</summary>
        static Mesh MallaCharco(float semilla)
        {
            const int lados = 56;
            var vs = new Vector3[lados + 1];
            var uv = new Vector2[lados + 1];
            var radial = new Vector2[lados + 1];      // (0 en el centro, 1 en el contorno): agua y borde mojado
            var ts = new int[lados * 3];
            vs[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < lados; i++)
            {
                float a = i * Mathf.PI * 2 / lados;
                float r = 0.78f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(a) * 1.6f + semilla, Mathf.Sin(a) * 1.6f + semilla * 0.37f)
                          + 0.08f * Mathf.Sin(a * 3 + semilla);
                vs[i + 1] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                uv[i + 1] = new Vector2(vs[i + 1].x * 0.5f + 0.5f, vs[i + 1].z * 0.5f + 0.5f);
                radial[i + 1] = new Vector2(1f, 0f);
                ts[i * 3] = 0;
                ts[i * 3 + 1] = (i + 1) % lados + 1;
                ts[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = "Charco" };
            m.vertices = vs;
            m.uv = uv;
            m.uv2 = radial;
            m.triangles = ts;
            m.RecalculateNormals();
            if (m.normals[0].y < 0)
            {
                for (int i = 0; i < lados; i++) (ts[i * 3 + 1], ts[i * 3 + 2]) = (ts[i * 3 + 2], ts[i * 3 + 1]);
                m.triangles = ts;
                m.RecalculateNormals();
            }
            m.RecalculateBounds();
            return m;
        }
    }
}
