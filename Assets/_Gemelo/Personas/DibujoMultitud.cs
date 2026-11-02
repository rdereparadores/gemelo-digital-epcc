// Dibujo de la multitud: los más cercanos a la cámara (hasta Ajustes.maxAnimadas) con un avatar humanoide animado
// por su Animator; el resto, hasta Ajustes.distanciaPersonas, como maniquíes rígidos instanciados en la GPU
// (Graphics.RenderMeshIndirect, animados en el shader MultitudInstanciada). Más allá, nada. Se ocultan los que quedan
// por encima del corte de su edificio, desde lejos y fuera los que están dentro de un edificio y, con la cámara en un
// local cerrado, los que están bajo techo en otros locales (tras los muros). Quien está quieto bajo techo no da sombra
// del sol. En la vista cenital con capa de color, todos marcan el bit 3 del stencil para que la capa no los tiña.
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
            public float[] nat;            // velocidad natural de sus clips de locomoción (ParamAvatar.Marcha)
            public GameObject movil;
            public SkinnedMeshRenderer[] rend;
            public Material[][] mats;      // materiales propios de cada renderer (sin la marca del stencil)
            public bool marcado;
            public bool sombra = true, fino = true;
            public bool[] conSombra;       // los que pueden dar sombra (no el pelo ni las pestañas con recorte alfa)
            public int cuerpo;             // índice del prefab (personaje o variante)
            public List<GameObject> marcas; // copias de los renderers de varias submallas con la marca en todas (Mixamo)
            public Persona p;
            public float y = float.NaN;    // altura sobre el suelo aplicada (suavizada al sentarse y levantarse)
        }

        public const int TopeAnimadas = 80;
        const float RadioAnimadas = 40f, RadioSombras = 35f, RadioSombrasAnimadas = 14f, InteriorLejos = 70f, CadaSeleccion = 0.2f, RadioPielFina = 8f;

        readonly RecursosPersonas rec;
        readonly Multitud m;
        readonly Mesh[] mallas;
        readonly Material mat;   // copia propia del material lejano: lleva siempre un buffer de instancias asignado
        GraphicsBuffer datos, args;
        // Buffers sustituidos al crecer: se liberan unos fotogramas después, cuando ya no los usa ningún dibujo en curso
        readonly List<(GraphicsBuffer b, int frame)> retirados = new();
        Instancia[] buf = new Instancia[0];
        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] argsBuf;
        readonly MaterialPropertyBlock[] mpbs;
        readonly int[] cuenta, base_;
        readonly Plane[] planos = new Plane[6];
        readonly List<Cercano> cercanos = new();
        readonly List<Cercano> reserva = new();
        Transform raiz;
        float tSeleccion;
        GameObject[] pruebaPrevia;
        // Marca de stencil de los avatares (bit 3: «persona») mientras la vista cenital tiene una capa de color
        readonly Material matMarca;
        bool marcar;
        readonly List<(float d, Persona p)> candidatos = new();
        public int Animadas, Instanciadas;
        /// <summary>Avatares creados (instanciados) desde el principio: con la reserva caliente apenas crece.</summary>
        public int Creados;
        int creadosCiclo;
        // Crear un personaje de Mixamo cuesta 1-3 ms (≈60 huesos y 6-9 renderers): como mucho unos pocos por selección
        const int CrearPorSeleccion = 3;

        static readonly int idVel = Animator.StringToHash(ParamAvatar.Velocidad), idSentado = Animator.StringToHash(ParamAvatar.Sentado),
                            idHabla = Animator.StringToHash(ParamAvatar.Hablando), idMovil = Animator.StringToHash("Movil"),
                            idSuelo = Animator.StringToHash(ParamAvatar.EnSuelo), idEscribe = Animator.StringToHash(ParamAvatar.Escribiendo);
        static readonly int idInst = Shader.PropertyToID("_Instancias"), idBase = Shader.PropertyToID("_Base");

        public DibujoMultitud(RecursosPersonas rec, Multitud m, Transform padre)
        {
            this.rec = rec;
            this.m = m;
            mallas = rec != null ? rec.mallasLejanas : new Mesh[0];
            mat = rec != null && rec.materialLejano != null ? new Material(rec.materialLejano) { name = rec.materialLejano.name + " (gemelo)" } : null;
            int grupos = Mathf.Max(1, mallas.Length) * 2;
            argsBuf = new GraphicsBuffer.IndirectDrawIndexedArgs[grupos];
            mpbs = new MaterialPropertyBlock[grupos];
            for (int i = 0; i < grupos; i++) mpbs[i] = new MaterialPropertyBlock();
            cuenta = new int[grupos];
            base_ = new int[grupos];
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, grupos, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            // El buffer de instancias se reserva de entrada para el máximo de personas (64 B cada una) y se asigna
            // también al material: así ningún dibujo de este shader se queda sin él (el aviso de Vulkan «Shader requires
            // a compute buffer "_Instancias"» y la llamada descartada), ni siquiera mientras se compila un shader
            if (mat != null) Reservar(Mathf.Max(256, Ajustes.Actual.maxPersonas));
            raiz = new GameObject("Personas").transform;
            raiz.SetParent(padre, false);
            var sh = Resources.Load<Shader>("GemeloPersonas/MarcaPersonas");
            if (sh != null) matMarca = new Material(sh) { name = "Marca de personas (gemelo)" };
        }

        public void Mostrar(bool si)
        {
            if (raiz != null) raiz.gameObject.SetActive(si);
        }

        void Reservar(int n)
        {
            if (datos != null) retirados.Add((datos, Time.frameCount));
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, n));
            datos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, Marshal.SizeOf<Instancia>());
            buf = new Instancia[cap];
            mat.SetBuffer(idInst, datos);
        }

        void LiberarRetirados()
        {
            for (int i = retirados.Count - 1; i >= 0; i--)
                if (Time.frameCount - retirados[i].frame > 3) { retirados[i].b.Release(); retirados.RemoveAt(i); }
        }

        public void Liberar()
        {
            foreach (var r in retirados) r.b.Release();
            retirados.Clear();
            datos?.Release();
            args?.Release();
            datos = args = null;
            if (raiz != null) Object.Destroy(raiz.gameObject);
            if (matMarca != null) Object.Destroy(matMarca);
            if (mat != null) Object.Destroy(mat);
        }

        /// <summary>Para pruebas: si es true, también dan sombra del sol los que están quietos bajo techo.</summary>
        public static bool SombrasBajoTecho;

        // Quien está quieto en un local cubierto no da sombra del sol: el techo ya lo tapa (solo entraría algo por las
        // ventanas) y las luces interiores no tienen sombras. En un aula llena eran 40 avatares y cientos de maniquíes
        // dibujados otra vez en cada cascada de la sombra del sol
        static bool BajoTecho(Persona p) => p.donde != null && !p.donde.alAireLibre && !SombrasBajoTecho;

        // ─── Gente tras los muros. Con la cámara dentro de un local cerrado (aula, despacho, laboratorio… y no junto a
        // su puerta), los que están bajo techo en otro local solo se dibujan si están en la misma planta y a menos de
        // RadioTrasMuros (el pasillo que se ve por la puerta); los de fuera, sí (se ven por las ventanas). Sin esto, en
        // un aula se dibujaban cientos de maniquíes de las otras aulas, plantas y edificios en cada pasada. (En los
        // pasillos y vestíbulos de la misma planta, hasta RadioPasillo: se ven por la puerta abierta.)
        /// <summary>Para pruebas: si es false, se dibujan también los que quedan tras los muros.</summary>
        public static bool OcultarTrasMuros = true;
        const float RadioTrasMuros = 10f, RadioPasillo = 25f, RadioPuerta = 2.5f;
        static readonly HashSet<string> Abiertos = new()
        {
            "pasillo", "vestibulo", "hall", "distribuidor", "escalera", "galeria", "patio", "terraza", "porche", "cafeteria", "comedor",
        };
        InfoLocal camLocal;
        bool camCerrada;
        float tCam;

        void LocalDeCamara(Vector3 cp)
        {
            if (Time.time < tCam) return;
            tCam = Time.time + 0.15f;
            camLocal = OcultarTrasMuros ? RegistroCampus.LocalEn(cp) : null;
            camCerrada = camLocal != null && !Abiertos.Contains(camLocal.local.uso ?? "");
            if (!camCerrada) return;
            foreach (var pu in camLocal.puertas)
                if (pu != null && (RegistroCampus.CentroPuerta(pu) - cp).sqrMagnitude < RadioPuerta * RadioPuerta) { camCerrada = false; return; }
        }

        /// <summary>Local (bajo techo o patio) en el que está la persona: el suyo si está quieta; andando, el que pisa.</summary>
        static InfoLocal LocalDe(Persona p)
        {
            if (p.donde != null) return p.donde.info;
            if (p.edificio == null) return null;
            if (Time.time >= p.tLocal)
            {
                p.tLocal = Time.time + 0.4f + (p.id % 7) * 0.03f;
                p.localAndando = RegistroCampus.LocalEn(p.pos);
            }
            return p.localAndando;
        }

        bool TrasLosMuros(Persona p, float d2)
        {
            var l = LocalDe(p);
            if (l == null || l == camLocal) return false;
            var uso = l.local.uso ?? "";
            if (uso == "patio" || uso == "terraza") return false;
            float r = Abiertos.Contains(uso) ? RadioPasillo : RadioTrasMuros;
            return l.planta != camLocal.planta || d2 > r * r;
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
            if (camCerrada && TrasLosMuros(p, d2)) return true;
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
            LocalDeCamara(cp);

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

            if (FabricaAvatares.PruebaAvatares != pruebaPrevia)
            {
                // Cambio de cuerpos (medidas A/B): fuera todos los avatares, que se rehacen con los nuevos
                pruebaPrevia = FabricaAvatares.PruebaAvatares;
                for (int i = cercanos.Count - 1; i >= 0; i--) Devolver(i);
                foreach (var r in reserva) Object.Destroy(r.go);
                reserva.Clear();
                tSeleccion = 0f;
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
                        sigue = pos >= 0 && pos < maxAnim + (maxAnim > 0 ? 4 : 0);   // un poco de holgura para no parpadear
                    }
                    if (!sigue) Devolver(i);
                }
                creadosCiclo = 0;
                foreach (var (d, p) in candidatos)
                {
                    if (cercanos.Count >= maxAnim) break;
                    if (p.avatar >= 0) continue;
                    Asignar(p);
                }
                // Solo los más cercanos proyectan sombra (cada sombra es un dibujo más por cascada)
                foreach (var c in cercanos)
                {
                    if (c.rend == null) continue;
                    float d2 = (c.p.pos - cp).sqrMagnitude;
                    // Piel de 4 huesos solo de cerca (cara y manos); más allá basta con 2
                    bool fino = d2 < RadioPielFina * RadioPielFina;
                    if (fino != c.fino)
                    {
                        c.fino = fino;
                        foreach (var r in c.rend) r.quality = fino ? SkinQuality.Auto : SkinQuality.Bone2;
                    }
                    bool sombra = !BajoTecho(c.p) && d2 < RadioSombrasAnimadas * RadioSombrasAnimadas;
                    if (sombra == c.sombra) continue;
                    c.sombra = sombra;
                    for (int i = 0; i < c.rend.Length; i++)
                        c.rend[i].shadowCastingMode = sombra && (c.conSombra == null || c.conSombra[i]) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
            }

            // Con la capa de color de la vista cenital, los avatares marcan el stencil para que no se tiñan
            var app = GemeloApp.Instancia;
            marcar = matMarca != null && app != null && app.Modo == Modo.Cenital && app.Capa != Capa.Ninguna;

            // Avatares cercanos: transformar y animar
            foreach (var c in cercanos)
            {
                if (c.marcado != marcar) Marcar(c, marcar);
                var p = c.p;
                // La altura del asiento acompaña a la transición del Animator (~0,5 s) en vez de saltar de golpe
                float y = p.sentada && p.estado == EstadoPersona.Quieta ? p.alturaAsiento - AsientoClip * p.ap.escala : 0f;
                c.y = float.IsNaN(c.y) ? y : Mathf.Lerp(c.y, y, 1f - Mathf.Exp(-9f * Time.deltaTime));
                c.tr.SetPositionAndRotation(p.pos + Vector3.up * c.y, Quaternion.Euler(0f, p.rumbo * Mathf.Rad2Deg, 0f));
                if (!c.go.activeSelf) c.go.SetActive(true);
                float escribe = Escritura(p);
                bool manos = ManosOcupadas(p, escribe);
                if (c.an != null)
                {
                    // sin patinar: parámetro y cadencia según la velocidad natural de sus clips
                    ParamAvatar.Marcha(c.nat, p.sentada ? 0f : p.vel, out float parVel, out float cadencia);
                    c.an.SetFloat(idVel, parVel, 0.12f, Time.deltaTime);
                    c.an.speed = cadencia;
                    c.an.SetBool(idSentado, p.sentada);
                    c.an.SetBool(idHabla, p.gesto == Gesto.Habla);
                    c.an.SetBool(idMovil, manos);
                    c.an.SetFloat(idEscribe, escribe);
                }
                bool movil = p.gesto == Gesto.Movil && p.vel < 0.3f && (!p.sentada || MovilSentado);
                if (c.movil != null && c.movil.activeSelf != movil) c.movil.SetActive(movil);
            }
            Animadas = cercanos.Count;

            // Instanciados
            if (mat == null || mallas.Length == 0) { Instanciadas = 0; return; }
            LiberarRetirados();
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
            if (datos == null || datos.count < total) Reservar(total);
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
            bool sombra = !BajoTecho(p) && (p.pos - cp).sqrMagnitude < RadioSombras * RadioSombras;
            return (sombra ? 0 : nv) + Mathf.Clamp(p.ap.variante, 0, nv - 1);
        }

        static uint U(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | 0xFF000000u;

        float AsientoClip => rec != null ? rec.alturaAsientoClip : 0.46f;
        bool MovilSentado => rec == null || rec.movilSentado;

        /// <summary>ParamAvatar.Escribiendo: 0 si su puesto tiene ordenador (teclea), 1 si no (escribe en la mesa).</summary>
        static float Escritura(Persona p)
        {
            var e = p.donde;
            if (e == null || p.plaza < 0 || p.plaza >= e.plazas.Length || p.destino != e) return 1f;
            return e.plazas[p.plaza].ordenador ? 0f : 1f;
        }

        // Parámetro Movil del controlador: de pie, mirar el móvil; sentado, el estado SentadoMovil. Con los personajes
        // de Mixamo ese estado teclea o escribe, así que solo vale donde hay mesa (en la cafetería, el salón de actos o
        // un banco del hall se quedan sentados sin más)
        bool ManosOcupadas(Persona p, float escribe)
        {
            if (p.gesto != Gesto.Movil) return false;
            if (!p.sentada || MovilSentado) return true;
            return p.donde != null && (p.donde.mesas || escribe < 0.5f);
        }

        void Asignar(Persona p)
        {
            Cercano c = null;
            int cuerpo = FabricaAvatares.Cuerpo(p.ap, rec);
            for (int i = reserva.Count - 1; i >= 0; i--)
                if (reserva[i].cuerpo == cuerpo) { c = reserva[i]; reserva.RemoveAt(i); break; }
            if (c == null)
            {
                if (creadosCiclo >= CrearPorSeleccion) return;     // (sigue de maniquí hasta la siguiente selección)
                creadosCiclo++;
                Creados++;
                if (cercanos.Count + reserva.Count >= TopeAnimadas + 24 && reserva.Count > 0)
                {
                    // Recicla uno de otra variante
                    Object.Destroy(reserva[0].go);
                    reserva.RemoveAt(0);
                }
                var go = FabricaAvatares.Crear(p.ap, raiz);
                if (go == null) return;
                c = new Cercano { go = go, tr = go.transform, an = go.GetComponent<Animator>(), cuerpo = cuerpo, movil = FabricaAvatares.Movil(go),
                                 rend = go.GetComponentsInChildren<SkinnedMeshRenderer>(true) };
                c.nat = c.an != null ? ParamAvatar.Naturales(c.an.runtimeAnimatorController) : null;
                // Pelo y pestañas (recorte alfa, a dos caras) no dan sombra: es la pasada de sombra más cara y no se nota
                c.conSombra = new bool[c.rend.Length];
                for (int i = 0; i < c.rend.Length; i++)
                {
                    bool recorte = false;
                    foreach (var m in c.rend[i].sharedMaterials) if (m != null && m.IsKeywordEnabled("_ALPHATEST_ON")) recorte = true;
                    c.conSombra[i] = !recorte;
                    if (recorte) c.rend[i].shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            else
            {
                c.tr.localScale = Vector3.one * FabricaAvatares.Escala(p.ap, rec);
                if (rec.colorearAvatares) FabricaAvatares.Colorear(c.go, p.ap);
            }
            c.p = p;
            c.y = float.NaN;
            p.avatar = cercanos.Count;
            cercanos.Add(c);
            float y = p.sentada ? p.alturaAsiento - AsientoClip * p.ap.escala : 0f;
            float escribe = Escritura(p);
            bool manos = ManosOcupadas(p, escribe);
            c.tr.SetPositionAndRotation(p.pos + Vector3.up * y, Quaternion.Euler(0f, p.rumbo * Mathf.Rad2Deg, 0f));
            c.go.SetActive(true);
            if (c.an != null)
            {
                // Que aparezca ya en su postura, sin la transición desde la anterior
                c.an.SetBool(idSuelo, true);
                c.an.SetBool(idSentado, p.sentada);
                c.an.SetBool(idHabla, p.gesto == Gesto.Habla);
                c.an.SetBool(idMovil, manos);
                c.an.SetFloat(idEscribe, escribe);
                ParamAvatar.Marcha(c.nat, p.sentada ? 0f : p.vel, out float parVel0, out float cadencia0);
                c.an.SetFloat(idVel, parVel0);
                c.an.speed = cadencia0;
                string estado = p.sentada ? (p.gesto == Gesto.Habla ? "SentadoHablando" : manos ? "SentadoMovil" : "Sentado")
                              : p.vel < 0.3f && p.gesto == Gesto.Habla ? "HablarDePie" : p.vel < 0.3f && p.gesto == Gesto.Movil ? "MovilDePie" : "Locomocion";
                c.an.Play(estado, 0, (p.id * 0.137f) % 1f);
            }
        }

        void Marcar(Cercano c, bool si)
        {
            c.marcado = si;
            if (c.rend == null) return;
            if (c.mats == null)
            {
                c.mats = new Material[c.rend.Length][];
                for (int i = 0; i < c.rend.Length; i++) c.mats[i] = c.rend[i].sharedMaterials;
            }
            if (c.marcas != null)
                foreach (var g in c.marcas) if (g != null) g.SetActive(si);
            for (int i = 0; i < c.rend.Length; i++)
            {
                var r = c.rend[i];
                if (r == null) continue;
                // Un material de más que submallas vuelve a dibujar solo la última submalla: vale para los cuerpos de una
                // submalla (maniquíes y casi todas las piezas de Mixamo); los de varias (James, Lewis…) llevan al lado
                // una copia del renderer, con los mismos huesos, que dibuja la marca en todas sus submallas
                var malla = r.sharedMesh;
                if (malla != null && malla.subMeshCount > 1)
                {
                    if (si && (c.marcas == null || !c.marcas.Exists(g => g != null && g.name == "Marca_" + r.name))) CopiaMarca(c, r);
                    continue;
                }
                if (!si) { r.sharedMaterials = c.mats[i]; continue; }
                var m = new Material[c.mats[i].Length + 1];
                c.mats[i].CopyTo(m, 0);
                m[^1] = matMarca;
                r.sharedMaterials = m;
            }
        }

        void CopiaMarca(Cercano c, SkinnedMeshRenderer r)
        {
            var g = new GameObject("Marca_" + r.name);
            g.transform.SetParent(r.transform.parent, false);
            g.transform.localPosition = r.transform.localPosition;
            g.transform.localRotation = r.transform.localRotation;
            g.transform.localScale = r.transform.localScale;
            var s = g.AddComponent<SkinnedMeshRenderer>();
            s.sharedMesh = r.sharedMesh;
            s.bones = r.bones;
            s.rootBone = r.rootBone;
            s.localBounds = r.localBounds;
            s.quality = r.quality;
            s.updateWhenOffscreen = false;
            s.shadowCastingMode = ShadowCastingMode.Off;
            s.receiveShadows = false;
            var mats = new Material[r.sharedMesh.subMeshCount];
            for (int k = 0; k < mats.Length; k++) mats[k] = matMarca;
            s.sharedMaterials = mats;
            (c.marcas ??= new List<GameObject>()).Add(g);
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
