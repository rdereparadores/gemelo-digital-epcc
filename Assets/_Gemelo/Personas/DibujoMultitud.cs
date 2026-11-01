// Dibujo de la multitud: los más cercanos a la cámara (hasta Ajustes.maxAnimadas) con un avatar humanoide animado
// por su Animator; el resto, hasta Ajustes.distanciaPersonas, como maniquíes rígidos instanciados en la GPU
// (Graphics.RenderMeshIndirect, animados en el shader MultitudInstanciada). Más allá, nada. Se ocultan los que quedan
// por encima del corte de su edificio y, desde lejos y fuera, los que están dentro de un edificio.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class DibujoMultitud
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Instancia
        {
            public Vector4 posRumbo, anim;
            public uint a0, a1, a2, a3, b0, b1, b2, b3;
        }

        class Cercano
        {
            public GameObject go;
            public Transform tr;
            public Animator an;
            public GameObject movil;
            public SkinnedMeshRenderer[] rend;
            public bool sombra = true;
            public int variante;
            public Persona p;
        }

        public const int TopeAnimadas = 80;
        const float RadioAnimadas = 40f, RadioSombras = 35f, RadioSombrasAnimadas = 14f, InteriorLejos = 70f, CadaSeleccion = 0.2f;

        readonly RecursosPersonas rec;
        readonly Multitud m;
        readonly Mesh[] mallas;
        readonly Material mat;
        GraphicsBuffer datos, args;
        Instancia[] buf = new Instancia[0];
        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] argsBuf;
        readonly MaterialPropertyBlock[] mpbs;
        readonly int[] cuenta, base_;
        readonly Plane[] planos = new Plane[6];
        readonly List<Cercano> cercanos = new();
        readonly List<Cercano> reserva = new();
        Transform raiz;
        float tSeleccion;
        readonly List<(float d, Persona p)> candidatos = new();
        public int Animadas, Instanciadas;

        static readonly int idVel = Animator.StringToHash(ParamAvatar.Velocidad), idSentado = Animator.StringToHash(ParamAvatar.Sentado),
                            idHabla = Animator.StringToHash(ParamAvatar.Hablando), idMovil = Animator.StringToHash("Movil"),
                            idSuelo = Animator.StringToHash(ParamAvatar.EnSuelo);
        static readonly int idInst = Shader.PropertyToID("_Instancias"), idBase = Shader.PropertyToID("_Base");

        public DibujoMultitud(RecursosPersonas rec, Multitud m, Transform padre)
        {
            this.rec = rec;
            this.m = m;
            mallas = rec != null ? rec.mallasLejanas : new Mesh[0];
            mat = rec != null ? rec.materialLejano : null;
            int grupos = Mathf.Max(1, mallas.Length) * 2;
            argsBuf = new GraphicsBuffer.IndirectDrawIndexedArgs[grupos];
            mpbs = new MaterialPropertyBlock[grupos];
            for (int i = 0; i < grupos; i++) mpbs[i] = new MaterialPropertyBlock();
            cuenta = new int[grupos];
            base_ = new int[grupos];
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, grupos, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            raiz = new GameObject("Personas").transform;
            raiz.SetParent(padre, false);
        }

        public void Mostrar(bool si)
        {
            if (raiz != null) raiz.gameObject.SetActive(si);
        }

        public void Liberar()
        {
            datos?.Release();
            args?.Release();
            datos = args = null;
            if (raiz != null) Object.Destroy(raiz.gameObject);
        }

        /// <summary>Si no es null, la cámara que manda (para capturas y pruebas); si no, Camera.main.</summary>
        public static Camera CamaraForzada;

        static Camera Camara()
        {
            if (CamaraForzada != null && CamaraForzada.isActiveAndEnabled) return CamaraForzada;
            var c = Camera.main;
            if (c != null && c.isActiveAndEnabled) return c;
            foreach (var o in Camera.allCameras) if (o.isActiveAndEnabled && o.targetTexture == null) return o;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
        }

        bool Oculta(Persona p, Vector3 cam, float d2)
        {
            var e = p.edificio;
            if (e == null) return false;
            var app = GemeloApp.Instancia;
            int corte = app != null ? app.Corte(e.info) : -1;
            if (corte >= 0)
            {
                // Lo que queda por encima de la planta visible no se ve
                if (corte + 1 < e.info.plantas.Count && p.pos.y > e.info.plantas[corte + 1].cota - 0.5f) return true;
                return false;
            }
            // Dentro de un edificio, visto desde fuera y lejos: los muros lo tapan
            return d2 > InteriorLejos * InteriorLejos && !e.info.limites.Contains(cam);
        }

        public void Dibujar()
        {
            var cam = Camara();
            if (cam == null) return;
            var cp = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planos);
            float maxD = Ajustes.Actual.distanciaPersonas, maxD2 = maxD * maxD;
            // Cada avatar animado cuesta ~0,05 ms de GPU y ~0,07 ms de hilo principal (Animator, skinning y sus pasadas);
            // por encima de TopeAnimadas se sale del presupuesto de la escena, que ya va al límite de la GPU
            int maxAnim = Mathf.Clamp(Ajustes.Actual.maxAnimadas, 0, TopeAnimadas);
            var lista = m.activas;

            // Visibilidad
            foreach (var p in lista)
            {
                var c = p.pos + Vector3.up * 0.9f;
                float d2 = (c - cp).sqrMagnitude;
                bool vis = d2 < maxD2 && !Oculta(p, cp, d2);
                if (vis)
                    for (int i = 0; i < 6; i++)
                        if (planos[i].GetDistanceToPoint(c) < -1.2f) { vis = false; break; }
                p.visible = vis;
            }

            // Selección de los animados (cada poco)
            if (Time.time >= tSeleccion)
            {
                tSeleccion = Time.time + CadaSeleccion;
                candidatos.Clear();
                foreach (var p in lista)
                {
                    if (!p.visible) continue;
                    float d2 = (p.pos - cp).sqrMagnitude;
                    if (d2 < RadioAnimadas * RadioAnimadas) candidatos.Add((d2, p));
                }
                candidatos.Sort((a, b) => a.d.CompareTo(b.d));
                // Fuera los que ya no toca
                for (int i = cercanos.Count - 1; i >= 0; i--)
                {
                    var c = cercanos[i];
                    bool sigue = c.p.Activa && c.p.visible && c.p.avatar >= 0;
                    if (sigue)
                    {
                        int pos = candidatos.FindIndex(x => x.p == c.p);
                        sigue = pos >= 0 && pos < maxAnim + 4;   // un poco de holgura para no parpadear
                    }
                    if (!sigue) Devolver(i);
                }
                foreach (var (d, p) in candidatos)
                {
                    if (cercanos.Count >= maxAnim) break;
                    if (p.avatar >= 0) continue;
                    Asignar(p);
                }
                // Solo los más cercanos proyectan sombra (cada sombra es un dibujo más por cascada)
                foreach (var c in cercanos)
                {
                    bool sombra = (c.p.pos - cp).sqrMagnitude < RadioSombrasAnimadas * RadioSombrasAnimadas;
                    if (sombra == c.sombra || c.rend == null) continue;
                    c.sombra = sombra;
                    foreach (var r in c.rend) r.shadowCastingMode = sombra ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
            }

            // Avatares cercanos: transformar y animar
            foreach (var c in cercanos)
            {
                var p = c.p;
                float y = p.sentada && p.estado == EstadoPersona.Quieta ? p.alturaAsiento - 0.46f * p.ap.escala : 0f;
                c.tr.SetPositionAndRotation(p.pos + Vector3.up * y, Quaternion.Euler(0f, p.rumbo * Mathf.Rad2Deg, 0f));
                if (!c.go.activeSelf) c.go.SetActive(true);
                if (c.an != null)
                {
                    c.an.SetFloat(idVel, p.vel, 0.12f, Time.deltaTime);
                    c.an.SetBool(idSentado, p.sentada);
                    c.an.SetBool(idHabla, p.gesto == Gesto.Habla);
                    c.an.SetBool(idMovil, p.gesto == Gesto.Movil);
                }
                bool movil = p.gesto == Gesto.Movil && p.vel < 0.3f;
                if (c.movil != null && c.movil.activeSelf != movil) c.movil.SetActive(movil);
            }
            Animadas = cercanos.Count;

            // Instanciados
            if (mat == null || mallas.Length == 0) { Instanciadas = 0; return; }
            int nv = mallas.Length;
            System.Array.Clear(cuenta, 0, cuenta.Length);
            foreach (var p in lista)
            {
                if (!p.visible || p.avatar >= 0) continue;
                int g = Grupo(p, cp, nv);
                cuenta[g]++;
            }
            int total = 0;
            for (int g = 0; g < cuenta.Length; g++) { base_[g] = total; total += cuenta[g]; }
            Instanciadas = total;
            if (total == 0) return;
            if (datos == null || datos.count < total)
            {
                datos?.Release();
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, total));
                datos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, Marshal.SizeOf<Instancia>());
                buf = new Instancia[cap];
            }
            var fill = (int[])base_.Clone();
            foreach (var p in lista)
            {
                if (!p.visible || p.avatar >= 0) continue;
                int g = Grupo(p, cp, nv);
                ref var it = ref buf[fill[g]++];
                it.posRumbo = new Vector4(p.pos.x, p.pos.y, p.pos.z, p.rumbo);
                uint f = 0;
                bool quieta = p.vel < 0.3f;
                if (p.sentada && p.estado == EstadoPersona.Quieta) f |= 1;
                if (quieta && p.gesto == Gesto.Habla) f |= 2;
                if (quieta && p.gesto == Gesto.Movil) f |= 4;
                if (p.edificio != null) f |= 8;
                it.anim = new Vector4(p.ap.escala, p.fase, p.vel, f);
                it.a0 = U(p.ap.piel); it.a1 = U(p.ap.top); it.a2 = U(p.ap.mangas); it.a3 = U(p.ap.bajo);
                it.b0 = U(p.ap.zapatos); it.b1 = U(p.ap.pelo); it.b2 = U(p.ap.mochila);
                it.b3 = (uint)System.BitConverter.SingleToInt32Bits(p.alturaAsiento);
            }
            datos.SetData(buf, 0, 0, total);
            for (int g = 0; g < cuenta.Length; g++)
            {
                var mesh = mallas[g % nv];
                argsBuf[g] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = mesh.GetIndexCount(0), instanceCount = (uint)cuenta[g],
                    startIndex = mesh.GetIndexStart(0), baseVertexIndex = mesh.GetBaseVertex(0), startInstance = 0,
                };
            }
            args.SetData(argsBuf);
            var limites = new Bounds(Multitud.Zona.center, Multitud.Zona.size + Vector3.one * 50f);
            for (int g = 0; g < cuenta.Length; g++)
            {
                if (cuenta[g] == 0) continue;
                mpbs[g].SetBuffer(idInst, datos);
                mpbs[g].SetInteger(idBase, base_[g]);
                var rp = new RenderParams(mat)
                {
                    worldBounds = limites, matProps = mpbs[g], receiveShadows = true,
                    shadowCastingMode = g < nv ? ShadowCastingMode.On : ShadowCastingMode.Off,
                };
                Graphics.RenderMeshIndirect(rp, mallas[g % nv], args, 1, g);
            }
        }

        static int Grupo(Persona p, Vector3 cp, int nv)
        {
            bool sombra = (p.pos - cp).sqrMagnitude < RadioSombras * RadioSombras;
            return (sombra ? 0 : nv) + Mathf.Clamp(p.ap.variante, 0, nv - 1);
        }

        static uint U(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | 0xFF000000u;

        void Asignar(Persona p)
        {
            Cercano c = null;
            for (int i = reserva.Count - 1; i >= 0; i--)
                if (reserva[i].variante == p.ap.variante) { c = reserva[i]; reserva.RemoveAt(i); break; }
            if (c == null)
            {
                if (cercanos.Count + reserva.Count >= TopeAnimadas + 24 && reserva.Count > 0)
                {
                    // Recicla uno de otra variante
                    Object.Destroy(reserva[0].go);
                    reserva.RemoveAt(0);
                }
                var go = FabricaAvatares.Crear(p.ap, raiz);
                if (go == null) return;
                c = new Cercano { go = go, tr = go.transform, an = go.GetComponent<Animator>(), variante = p.ap.variante, movil = FabricaAvatares.Movil(go),
                                 rend = go.GetComponentsInChildren<SkinnedMeshRenderer>(true) };
            }
            else
            {
                c.tr.localScale = Vector3.one * p.ap.escala;
                if (rec.colorearAvatares) FabricaAvatares.Colorear(c.go, p.ap);
            }
            c.p = p;
            p.avatar = cercanos.Count;
            cercanos.Add(c);
            float y = p.sentada ? p.alturaAsiento - 0.46f * p.ap.escala : 0f;
            c.tr.SetPositionAndRotation(p.pos + Vector3.up * y, Quaternion.Euler(0f, p.rumbo * Mathf.Rad2Deg, 0f));
            c.go.SetActive(true);
            if (c.an != null)
            {
                // Que aparezca ya en su postura, sin la transición desde la anterior
                c.an.SetBool(idSuelo, true);
                c.an.SetBool(idSentado, p.sentada);
                c.an.SetBool(idHabla, p.gesto == Gesto.Habla);
                c.an.SetBool(idMovil, p.gesto == Gesto.Movil);
                c.an.SetFloat(idVel, p.vel);
                string estado = p.sentada ? (p.gesto == Gesto.Habla ? "SentadoHablando" : p.gesto == Gesto.Movil ? "SentadoMovil" : "Sentado")
                              : p.vel < 0.3f && p.gesto == Gesto.Habla ? "HablarDePie" : p.vel < 0.3f && p.gesto == Gesto.Movil ? "MovilDePie" : "Locomocion";
                c.an.Play(estado, 0, (p.id * 0.137f) % 1f);
            }
        }

        void Devolver(int i)
        {
            var c = cercanos[i];
            c.p.avatar = -1;
            c.p = null;
            c.go.SetActive(false);
            if (c.movil != null) c.movil.SetActive(false);
            int ultimo = cercanos.Count - 1;
            if (i != ultimo)
            {
                cercanos[i] = cercanos[ultimo];
                cercanos[i].p.avatar = i;
            }
            cercanos.RemoveAt(ultimo);
            reserva.Add(c);
        }
    }
}
