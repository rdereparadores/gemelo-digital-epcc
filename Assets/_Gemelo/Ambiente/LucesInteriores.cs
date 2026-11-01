// Luces interiores de cada local (Local.luces) según los sensores: lights_on si la estancia lo tiene; si no, la
// ocupación (y las circulaciones mientras el edificio tiene gente). Con apagón, todo apagado salvo las señales
// verdes de salida de emergencia sobre las puertas de fachada (con una luz verde tenue en las más cercanas).
// De noche, las ventanas (y los muros cortina) de los locales encendidos brillan por fuera (cuadros aditivos detrás del vidrio,
// instanciados): así se ve de lejos qué aulas están encendidas, aunque sus luces puntuales estén lejos de la
// cámara y apagadas por EdificioPolitecnica (que decide `enabled` por distancia; aquí solo se toca la intensidad).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class LucesInteriores : MonoBehaviour
    {
        static readonly Color ColorVentana = new(1.0f, 0.72f, 0.40f);
        static readonly Color ColorSalida = new(0.15f, 1.0f, 0.35f);
        const int MaxLucesSalida = 12;

        class LocalLuz
        {
            public InfoLocal info;
            public Light[] luces;
            public float[] base_;
            public float factor = 1f, objetivo = 1f;
            public float parpadeo;         // segundos de arranque de fluorescente que quedan
            public bool circulacion;
        }

        AmbienteGemelo amb;
        readonly List<LocalLuz> locales = new();
        readonly Dictionary<InfoLocal, LocalLuz> porInfo = new();
        readonly List<LocalLuz> animando = new();
        float siguiente;

        // Instancias de brillo (ventanas y señales)
        Material matBrillo;
        Mesh cuadro;
        readonly Matrix4x4[] matrices = new Matrix4x4[1023];
        readonly Vector4[] colores = new Vector4[1023];
        readonly Vector4[] formas = new Vector4[1023];
        int nVentanas, nSenales;
        readonly Matrix4x4[] matSenales = new Matrix4x4[1023];
        readonly Vector4[] colSenales = new Vector4[1023];
        readonly Vector4[] formaSenales = new Vector4[1023];
        MaterialPropertyBlock mpbVentanas, mpbSenales;
        readonly List<(Vector3 pos, InfoEdificio ed)> senales = new();
        readonly List<Light> lucesSalida = new();

        /// <summary>Factor de encendido (0–1) de un local, para otros componentes.</summary>
        public float Encendido(InfoLocal l) => l != null && porInfo.TryGetValue(l, out var x) ? x.factor : 0f;

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            foreach (var il in RegistroCampus.Locales.Values)
            {
                var ls = il.local.luces;
                if (ls == null || ls.Length == 0) continue;
                var x = new LocalLuz { info = il, luces = ls, base_ = new float[ls.Length], circulacion = EsCirculacion(il.local.uso) };
                for (int i = 0; i < ls.Length; i++) x.base_[i] = ls[i] != null ? ls[i].intensity : 0f;
                locales.Add(x);
                porInfo[il] = x;
            }
            // Señales de salida: sobre cada puerta de fachada, por las dos caras del muro
            foreach (var e in RegistroCampus.Edificios)
                foreach (var p in e.puertasExteriores)
                {
                    var c = RegistroCampus.CentroPuerta(p);
                    float alto = p.transform.lossyScale.y;
                    var f = p.transform.forward;
                    f.y = 0;
                    f.Normalize();
                    for (int s = -1; s <= 1; s += 2)
                        senales.Add((c + Vector3.up * (alto + 0.22f) + f * (0.2f * s), e));
                }
            var sh = Resources.Load<Shader>("Ambiente/GemeloBrillo");
            if (sh != null) matBrillo = new Material(sh) { name = "Brillo (gemelo)", enableInstancing = true };
            cuadro = CrearCuadro();
            mpbVentanas = new MaterialPropertyBlock();
            mpbSenales = new MaterialPropertyBlock();
            for (int i = 0; i < MaxLucesSalida; i++)
            {
                var go = new GameObject("Luz de emergencia");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = ColorSalida;
                l.range = 4.5f;
                l.intensity = 0.9f;
                l.shadows = LightShadows.None;
                go.SetActive(false);
                lucesSalida.Add(l);
            }
        }

        void OnDestroy()
        {
            if (matBrillo != null) Destroy(matBrillo);
            if (cuadro != null) Destroy(cuadro);
            // Las luces vuelven a su intensidad de la escena
            foreach (var x in locales)
                for (int i = 0; i < x.luces.Length; i++)
                    if (x.luces[i] != null) x.luces[i].intensity = x.base_[i];
        }

        static bool EsCirculacion(string uso) => uso switch
        {
            "pasillo" or "vestibulo" or "hall" or "distribuidor" or "escalera" or "galeria" or "aseo" => true,
            _ => false,
        };

        /// <summary>Cuadrado unidad en XY con la cara hacia +Z.</summary>
        public static Mesh CrearCuadro()
        {
            var m = new Mesh { name = "Cuadro (gemelo)" };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            return m;
        }

        void Update()
        {
            if (amb == null) return;
            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 0.5f;
                Decidir();
                PrepararVentanas();
                PrepararSenales();
            }
            Animar();
            Dibujar();
        }

        // ─── ¿Encendido o apagado?

        void Decidir()
        {
            var cli = ClienteGemelo.Instancia;
            bool hayEstado = cli != null && cli.Estado != null;
            foreach (var x in locales)
            {
                float obj = hayEstado ? (Encendida(x, cli) ? 1f : 0f) : 1f;
                if (obj != x.objetivo)
                {
                    if (obj > x.objetivo && x.factor < 0.05f) x.parpadeo = Random.Range(0.25f, 0.7f);
                    x.objetivo = obj;
                    if (!animando.Contains(x)) animando.Add(x);
                }
            }
        }

        bool Encendida(LocalLuz x, ClienteGemelo cli)
        {
            var l = x.info;
            if (amb.SinLuz(l)) return false;
            var st = AmbienteGemelo.Estado(l);
            if (st != null)
            {
                if (st.Metrica("power_on", out var pw) && pw.value < 0.5f) return false;
                if (st.Metrica("lights_on", out var lo)) return lo.value > 0.5f;
                float occ = st.Valor("occupancy");
                if (!float.IsNaN(occ))
                {
                    if (occ >= 0.5f) return true;
                    if (!x.circulacion) return false;
                }
            }
            // Circulaciones: encendidas mientras el edificio tenga gente. Locales sin dato: con gente en el edificio
            // y poca luz natural, unos cuantos (siempre los mismos)
            if (cli.Edificio(l.edificio.codigo, out var eb) && eb.metrics != null)
                foreach (var m in eb.metrics)
                    if (m.property == "occupancy")
                    {
                        if (m.value < 1f) return false;
                        if (x.circulacion) return true;
                        return st == null && amb.LuzNatural < 0.25f && Mathf.Repeat(l.centro.x * 0.731f + l.centro.z * 0.377f, 1f) < 0.35f;
                    }
            return false;
        }

        void Animar()
        {
            float dt = Time.deltaTime;
            for (int i = animando.Count - 1; i >= 0; i--)
            {
                var x = animando[i];
                float f;
                if (x.objetivo > x.factor && x.parpadeo > 0f)
                {
                    // Arranque de fluorescente: unos destellos antes de quedarse fijo
                    x.parpadeo -= dt;
                    f = Mathf.PerlinNoise(Time.time * 25f, x.info.centro.x) > 0.55f ? 0.8f : 0.05f;
                    x.factor = Mathf.Max(x.factor, 0.01f);
                    if (x.parpadeo <= 0f) x.factor = 1f;
                }
                else
                {
                    x.factor = Mathf.MoveTowards(x.factor, x.objetivo, dt * 3f);
                    f = x.factor;
                }
                for (int k = 0; k < x.luces.Length; k++)
                    if (x.luces[k] != null) x.luces[k].intensity = x.base_[k] * f;
                if (x.factor == x.objetivo && x.parpadeo <= 0f) animando.RemoveAt(i);
            }
        }

        // ─── Ventanas iluminadas y señales de salida

        void PrepararVentanas()
        {
            nVentanas = 0;
            float oscuro = Mathf.Clamp01(1f - amb.LuzNatural * 2.5f);
            if (oscuro < 0.02f || matBrillo == null) return;
            foreach (var v in DatosVentanas.Todas)
            {
                if (nVentanas >= matrices.Length) break;
                if (v.info == null || !porInfo.TryGetValue(v.info, out var x) || x.objetivo < 0.5f) continue;
                if (!v.info.planta.raiz.gameObject.activeInHierarchy) continue;
                float w = v.ancho - 2 * v.perfil, h = v.alto - 2 * v.perfil;
                if (w < 0.2f || h < 0.2f) continue;
                // (los paños de muro cortina tienen el vidrio en el eje de los montantes, de 12 cm: un poco más adentro)
                var pos = v.centro - v.normal * (v.cortina ? 0.1f : 0.06f);
                matrices[nVentanas] = Matrix4x4.TRS(pos, Quaternion.LookRotation(v.normal, Vector3.up), new Vector3(w, h, 1));
                // Un poco de variación entre aulas (tubos más o menos cálidos)
                float var_ = Mathf.Abs(Mathf.Sin(v.centro.x * 12.9898f + v.centro.z * 78.233f));
                var col = Color.Lerp(ColorVentana, new Color(0.85f, 0.88f, 1f), var_ * 0.45f);
                colores[nVentanas] = new Vector4(col.r, col.g, col.b, 0.75f * oscuro);
                formas[nVentanas] = new Vector4(0, 0.6f, 0.08f, 0);
                nVentanas++;
            }
            if (nVentanas > 0)
            {
                mpbVentanas.SetVectorArray("_Color", colores);
                mpbVentanas.SetVectorArray("_Forma", formas);
            }
        }

        void PrepararSenales()
        {
            nSenales = 0;
            AmbienteGemelo.PosicionCamara(out var pcam);
            int luz = 0;
            if (amb.EdificiosSinLuz.Count == 0 && amb.LocalesSinLuz.Count == 0)
            {
                foreach (var l in lucesSalida) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
                return;
            }
            foreach (var (pos, ed) in senales)
            {
                if (!amb.EdificiosSinLuz.Contains(ed.codigo) || nSenales >= matSenales.Length) continue;
                var f = pos - pcam;
                f.y = 0;
                // Cada señal es un cuadro verde de 36 × 14 cm de cara a la cámara en horizontal
                var rot = f.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-f.normalized, Vector3.up) : Quaternion.identity;
                matSenales[nSenales] = Matrix4x4.TRS(pos, rot, new Vector3(0.36f, 0.14f, 1));
                colSenales[nSenales] = new Vector4(ColorSalida.r, ColorSalida.g, ColorSalida.b, 3.5f);
                formaSenales[nSenales] = new Vector4(0, 0, 0.15f, 0);
                nSenales++;
                if (luz < lucesSalida.Count && (pos - pcam).sqrMagnitude < 60f * 60f)
                {
                    var l = lucesSalida[luz++];
                    l.transform.position = pos + Vector3.down * 0.3f;
                    if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
                }
            }
            for (int i = luz; i < lucesSalida.Count; i++)
                if (lucesSalida[i].gameObject.activeSelf) lucesSalida[i].gameObject.SetActive(false);
            if (nSenales > 0)
            {
                mpbSenales.SetVectorArray("_Color", colSenales);
                mpbSenales.SetVectorArray("_Forma", formaSenales);
            }
        }

        void Dibujar()
        {
            if (matBrillo == null) return;
            if (nVentanas > 0)
            {
                var rp = new RenderParams(matBrillo) { matProps = mpbVentanas, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMeshInstanced(rp, cuadro, 0, matrices, nVentanas);
            }
            if (nSenales > 0)
            {
                var rp = new RenderParams(matBrillo) { matProps = mpbSenales, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMeshInstanced(rp, cuadro, 0, matSenales, nSenales);
            }
        }
    }
}
