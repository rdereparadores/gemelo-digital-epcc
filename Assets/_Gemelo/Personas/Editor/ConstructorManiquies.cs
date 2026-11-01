// Genera los maniquíes de las personas del gemelo (Tools ▸ Gemelo ▸ Maniquíes):
//  · cuatro cuerpos (hombre y mujer, con y sin mochila) hechos de piezas redondeadas, con esqueleto humanoide de
//    nombres estándar (Hips, Spine, Chest, Neck, Head, LeftUpperArm…), su Avatar (AvatarBuilder) y un prefab con
//    Animator y SkinnedMeshRenderer (una submalla por color de ropa, para teñirlas por persona);
//  · clips humanoides procedurales (curvas de músculos): reposo, andar, correr, sentado, sentado hablando, sentado
//    con el móvil, hablar de pie gesticulando, mirar el móvil de pie y en el aire (para el salto del jugador);
//  · el AnimatorController con los parámetros de ParamAvatar (Velocidad, Sentado, Hablando, EnSuelo, Saltar) y Movil;
//  · las mallas rígidas de la multitud lejana (brazos caídos, cada vértice con su pieza y sus articulaciones en
//    uv2–uv4) que anima el shader MultitudInstanciada.
// Todo se apunta en Resources/RecursosPersonas.asset. Las poses se escriben como giros de huesos (en grados) y se
// convierten a músculos con HumanPoseHandler, así que valen para cualquier esqueleto humanoide (Mixamo incluido).
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gemelo
{
    public static class ConstructorManiquies
    {
        const string Carpeta = "Assets/_Gemelo/Personas/Maniquies";
        const string CarpetaClips = "Assets/_Gemelo/Personas/Maniquies/Clips";

        // Regiones de color (submallas del avatar; uv2.y en la malla lejana)
        public const int Piel = 0, Top = 1, Mangas = 2, Bajo = 3, Zapatos = 4, Pelo = 5, Mochila = 6, NRegiones = 7;
        // Piezas rígidas de la malla lejana (uv2.x)
        const int PPelvis = 0, PTorso = 1, PCabeza = 2, PBrazoI = 3, PAntebrazoI = 4, PBrazoD = 5, PAntebrazoD = 6,
                  PMusloI = 7, PPiernaI = 8, PMusloD = 9, PPiernaD = 10;

        static readonly string[] Huesos =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        };
        static readonly Dictionary<string, string> Padre = new()
        {
            ["Hips"] = null, ["Spine"] = "Hips", ["Chest"] = "Spine", ["Neck"] = "Chest", ["Head"] = "Neck",
            ["LeftShoulder"] = "Chest", ["LeftUpperArm"] = "LeftShoulder", ["LeftLowerArm"] = "LeftUpperArm", ["LeftHand"] = "LeftLowerArm",
            ["RightShoulder"] = "Chest", ["RightUpperArm"] = "RightShoulder", ["RightLowerArm"] = "RightUpperArm", ["RightHand"] = "RightLowerArm",
            ["LeftUpperLeg"] = "Hips", ["LeftLowerLeg"] = "LeftUpperLeg", ["LeftFoot"] = "LeftLowerLeg", ["LeftToes"] = "LeftFoot",
            ["RightUpperLeg"] = "Hips", ["RightLowerLeg"] = "RightUpperLeg", ["RightFoot"] = "RightLowerLeg", ["RightToes"] = "RightFoot",
        };

        // Posición (T-pose, mundo) de cada articulación. El maniquí mira a +Z; su izquierda es −X.
        static Dictionary<string, Vector3> Articulaciones(bool mujer)
        {
            float hombro = mujer ? 0.165f : 0.19f, cadera = mujer ? 0.1f : 0.095f;
            var j = new Dictionary<string, Vector3>
            {
                ["Hips"] = new(0, 0.96f, 0), ["Spine"] = new(0, 1.06f, 0), ["Chest"] = new(0, 1.24f, 0),
                ["Neck"] = new(0, 1.47f, 0), ["Head"] = new(0, 1.575f, 0),
            };
            foreach (var (lado, s) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                j[lado + "Shoulder"] = new(s * 0.04f, 1.43f, 0);
                j[lado + "UpperArm"] = new(s * hombro, 1.43f, 0);
                j[lado + "LowerArm"] = new(s * (hombro + 0.28f), 1.43f, 0);
                j[lado + "Hand"] = new(s * (hombro + 0.53f), 1.43f, 0);
                j[lado + "UpperLeg"] = new(s * cadera, 0.92f, 0);
                j[lado + "LowerLeg"] = new(s * cadera, 0.51f, 0);
                j[lado + "Foot"] = new(s * cadera, 0.09f, 0);
                j[lado + "Toes"] = new(s * cadera, 0.025f, 0.13f);
            }
            return j;
        }

        // ───────────────────────── Mallas ─────────────────────────

        class Malla
        {
            public readonly List<Vector3> v = new(), n = new(), p1 = new(), p2 = new();
            public readonly List<Vector2> uv2 = new();
            public readonly List<BoneWeight> w = new();
            public readonly List<int>[] tri = new List<int>[NRegiones];
            public int seg = 12, cap = 4;
            public Malla() { for (int i = 0; i < NRegiones; i++) tri[i] = new List<int>(); }

            int hueso, region, parte;
            Vector3 piv1, piv2;
            public void Pieza(int region, int hueso, int parte, Vector3 piv1, Vector3 piv2)
            {
                this.region = region; this.hueso = hueso; this.parte = parte; this.piv1 = piv1; this.piv2 = piv2;
            }

            void Vertice(Vector3 p, Vector3 normal)
            {
                v.Add(p); n.Add(normal.normalized);
                w.Add(new BoneWeight { boneIndex0 = hueso, weight0 = 1f });
                uv2.Add(new Vector2(parte, region)); p1.Add(piv1); p2.Add(piv2);
            }

            // Rejilla de anillos (cada anillo con `seg` vértices; los extremos colapsan en polos)
            void Coser(int inicio, int anillos)
            {
                var t = tri[region];
                for (int r = 0; r < anillos - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = inicio + r * seg + s, b = inicio + r * seg + (s + 1) % seg;
                    int c = a + seg, d = b + seg;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            }

            public void Elipsoide(Vector3 c, Vector3 r, int anillos = 0)
            {
                if (anillos <= 0) anillos = cap * 2 + 1;
                int ini = v.Count;
                for (int i = 0; i < anillos; i++)
                {
                    float phi = Mathf.Lerp(-Mathf.PI / 2, Mathf.PI / 2, i / (anillos - 1f));
                    for (int s = 0; s < seg; s++)
                    {
                        float th = s * Mathf.PI * 2f / seg;
                        var u = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                        var p = c + Vector3.Scale(u, r);
                        Vertice(p, new Vector3(u.x / r.x, u.y / r.y, u.z / r.z));
                    }
                }
                Coser(ini, anillos);
            }

            // Cápsula con radios distintos en cada extremo; `plano` aplasta la sección en la dirección w (profundidad)
            public void Capsula(Vector3 a, Vector3 b, float ra, float rb, float plano = 1f)
            {
                var d = (b - a).normalized;
                var u = Mathf.Abs(d.y) < 0.9f ? Vector3.Cross(d, Vector3.up).normalized : Vector3.Cross(d, Vector3.forward).normalized;
                var wv = Vector3.Cross(d, u).normalized;
                if (Mathf.Abs(d.y) >= 0.9f) { u = Vector3.right; wv = Vector3.Cross(d, u).normalized; }
                var perfil = new List<(Vector3 c, float r, float s)>();   // centro, radio, seno del ángulo de la normal
                for (int i = 0; i <= cap; i++)
                {
                    float phi = Mathf.Lerp(-Mathf.PI / 2, 0, i / (float)cap);
                    perfil.Add((a + d * (ra * Mathf.Sin(phi)), ra * Mathf.Cos(phi), Mathf.Sin(phi)));
                }
                for (int i = 0; i <= cap; i++)
                {
                    float phi = Mathf.Lerp(0, Mathf.PI / 2, i / (float)cap);
                    perfil.Add((b + d * (rb * Mathf.Sin(phi)), rb * Mathf.Cos(phi), Mathf.Sin(phi)));
                }
                int ini = v.Count;
                foreach (var (c, r, sn) in perfil)
                {
                    float cs = Mathf.Sqrt(Mathf.Max(0, 1 - sn * sn));
                    for (int s = 0; s < seg; s++)
                    {
                        float th = s * Mathf.PI * 2f / seg;
                        var radial = u * Mathf.Cos(th) + wv * (Mathf.Sin(th) * plano);
                        var nr = u * Mathf.Cos(th) + wv * (Mathf.Sin(th) / plano);
                        Vertice(c + radial * r, nr.normalized * cs + d * sn);
                    }
                }
                Coser(ini, perfil.Count);
            }

            public Mesh Crear(string nombre, bool submallas, bool pesos = true)
            {
                var m = new Mesh { name = nombre };
                m.SetVertices(v);
                m.SetNormals(n);
                // uv0: texel de la región en la paleta de 8×1 de cada persona (una sola submalla, un solo material)
                var uv0 = new List<Vector2>(uv2.Count);
                foreach (var u in uv2) uv0.Add(new Vector2((u.y + 0.5f) / 8f, 0.5f));
                m.SetUVs(0, uv0);
                m.SetUVs(1, uv2);
                m.SetUVs(2, p1);
                m.SetUVs(3, p2);
                {
                    var todos = new List<int>();
                    foreach (var t in tri) todos.AddRange(t);
                    m.SetTriangles(todos, 0);
                }
                if (pesos) m.boneWeights = w.ToArray();
                m.RecalculateBounds();
                return m;
            }
        }

        // Cuerpo en T-pose (brazosCaidos = pose de la malla lejana, con los brazos a los lados)
        static Malla Cuerpo(bool mujer, bool mochila, bool baja, bool brazosCaidos)
        {
            var j = Articulaciones(mujer);
            var m = new Malla { seg = baja ? 7 : 14, cap = baja ? 2 : 4 };
            int H(string h) => Array.IndexOf(Huesos, h);
            Vector3 cero = Vector3.zero;

            // Pelvis y torso
            m.Pieza(Bajo, H("Hips"), PPelvis, cero, cero);
            m.Elipsoide(new Vector3(0, 0.95f, -0.005f), new Vector3(mujer ? 0.158f : 0.15f, 0.11f, 0.1f));
            m.Pieza(Top, H("Spine"), PTorso, j["Spine"], cero);
            m.Capsula(new Vector3(0, 0.99f, 0), new Vector3(0, 1.2f, 0), mujer ? 0.145f : 0.148f, mujer ? 0.145f : 0.16f, 0.68f);
            m.Pieza(Top, H("Chest"), PTorso, j["Spine"], cero);
            m.Capsula(new Vector3(0, 1.22f, 0), new Vector3(0, 1.37f, 0), mujer ? 0.15f : 0.165f, mujer ? 0.16f : 0.185f, 0.62f);
            foreach (float s in new[] { -1f, 1f })
                m.Elipsoide(new Vector3(s * (j["RightUpperArm"].x - 0.035f), 1.405f, 0), new Vector3(0.062f, 0.055f, 0.06f));
            if (mochila)
            {
                m.Pieza(Mochila, H("Chest"), PTorso, j["Spine"], cero);
                m.Elipsoide(new Vector3(0, 1.2f, -0.2f), new Vector3(0.15f, 0.2f, 0.085f));
            }

            // Cuello, cabeza, nariz y pelo
            m.Pieza(Piel, H("Neck"), PCabeza, j["Neck"], cero);
            m.Capsula(new Vector3(0, 1.43f, 0), new Vector3(0, 1.56f, 0), 0.05f, 0.048f);
            m.Pieza(Piel, H("Head"), PCabeza, j["Neck"], cero);
            m.Elipsoide(new Vector3(0, 1.675f, 0.01f), new Vector3(0.092f, 0.115f, 0.105f));
            m.Elipsoide(new Vector3(0, 1.665f, 0.108f), new Vector3(0.016f, 0.026f, 0.02f), baja ? 3 : 0);
            m.Pieza(Pelo, H("Head"), PCabeza, j["Neck"], cero);
            m.Elipsoide(new Vector3(0, 1.705f, -0.012f), new Vector3(0.099f, 0.103f, 0.108f));
            if (mujer) m.Capsula(new Vector3(0, 1.68f, -0.055f), new Vector3(0, 1.49f, -0.085f), 0.088f, 0.07f, 0.75f);

            // Brazos
            foreach (var (lado, s) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                bool izq = s < 0;
                Vector3 hom = j[lado + "UpperArm"], cod = j[lado + "LowerArm"], mun = j[lado + "Hand"];
                int ini = m.v.Count;
                m.Pieza(Top, H(lado + "UpperArm"), izq ? PBrazoI : PBrazoD, hom, cod);
                m.Capsula(hom + new Vector3(s * 0.01f, 0, 0), cod, 0.055f, 0.045f);
                m.Pieza(Mangas, H(lado + "LowerArm"), izq ? PAntebrazoI : PAntebrazoD, hom, cod);
                m.Capsula(cod + new Vector3(s * 0.01f, 0, 0), mun, 0.044f, 0.034f);
                m.Pieza(Piel, H(lado + "Hand"), izq ? PAntebrazoI : PAntebrazoD, hom, cod);
                m.Elipsoide(mun + new Vector3(s * 0.075f, -0.005f, 0.005f), new Vector3(0.075f, 0.028f, 0.045f));
                if (brazosCaidos)
                {
                    // Gira el brazo entero sobre el hombro hasta dejarlo a lo largo del cuerpo
                    var q = Quaternion.AngleAxis(izq ? 80f : -80f, Vector3.forward);
                    for (int i = ini; i < m.v.Count; i++)
                    {
                        m.v[i] = hom + q * (m.v[i] - hom);
                        m.n[i] = q * m.n[i];
                        m.p2[i] = hom + q * (m.p2[i] - hom);
                    }
                }
            }

            // Piernas
            foreach (var (lado, s) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                bool izq = s < 0;
                Vector3 cad = j[lado + "UpperLeg"], rod = j[lado + "LowerLeg"], tob = j[lado + "Foot"];
                m.Pieza(Bajo, H(lado + "UpperLeg"), izq ? PMusloI : PMusloD, cad, rod);
                m.Capsula(cad + new Vector3(0, -0.02f, 0), rod, 0.085f, 0.06f);
                m.Pieza(Bajo, H(lado + "LowerLeg"), izq ? PPiernaI : PPiernaD, cad, rod);
                m.Capsula(rod + new Vector3(0, -0.01f, 0), tob + new Vector3(0, 0.03f, 0), 0.058f, 0.042f);
                m.Pieza(Zapatos, H(lado + "Foot"), izq ? PPiernaI : PPiernaD, cad, rod);
                m.Elipsoide(new Vector3(tob.x, 0.045f, 0.045f), new Vector3(0.05f, 0.045f, 0.125f));
            }
            return m;
        }

        // ───────────────────────── Construcción ─────────────────────────

        [MenuItem("Tools/Gemelo/Maniquíes")]
        public static void ConstruirDesdeMenu() => EditorUtility.DisplayDialog("Maniquíes", Construir(), "Vale");

        public static string Construir()
        {
            Directory(Carpeta);
            Directory(CarpetaClips);
            var rec = ConstructorNavegacion.Recursos();

            // Materiales
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var matCuerpo = Material(Carpeta + "/Maniqui.mat", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f));
                m.SetFloat("_Smoothness", 0.28f);
                m.enableInstancing = true;
            });
            var matMovil = Material(Carpeta + "/Movil.mat", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.07f));
                m.SetFloat("_Smoothness", 0.85f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.05f, 0.08f, 0.12f));
                // Sin esto URP da la emisión por negra y le quita _EMISSION al validar el material
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            var shLejano = Shader.Find("Gemelo/MultitudInstanciada");
            Material matLejano = shLejano != null ? Material(Carpeta + "/MultitudInstanciada.mat", shLejano, m => { }) : null;

            // Móvil (una losa fina)
            var movil = Guardar(MallaMovil(), Carpeta + "/Movil.asset");

            var variantes = new[] { ("Hombre", false, false), ("HombreMochila", false, true), ("Mujer", true, false), ("MujerMochila", true, true) };
            var prefabs = new List<GameObject>();
            var lejanas = new List<Mesh>();
            Avatar avatarH = null;
            GameObject modeloH = null;
            foreach (var (nombre, mujer, mochila) in variantes)
            {
                var malla = Guardar(Cuerpo(mujer, mochila, false, false).Crear("Maniqui_" + nombre, true), $"{Carpeta}/Maniqui_{nombre}.asset");
                lejanas.Add(Guardar(Cuerpo(mujer, mochila, true, true).Crear("Lejano_" + nombre, false, false), $"{Carpeta}/Lejano_{nombre}.asset"));
                var go = Esqueleto("Maniqui_" + nombre, mujer, out var huesos);
                // Pesos: los índices de BoneWeight siguen el orden de Huesos
                var bind = new Matrix4x4[Huesos.Length];
                for (int i = 0; i < Huesos.Length; i++) bind[i] = huesos[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
                malla.bindposes = bind;
                EditorUtility.SetDirty(malla);
                var cuerpo = new GameObject("Cuerpo");
                cuerpo.transform.SetParent(go.transform, false);
                var smr = cuerpo.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = malla;
                smr.bones = huesos;
                smr.rootBone = huesos[0];
                smr.sharedMaterial = matCuerpo;
                smr.updateWhenOffscreen = false;
                smr.localBounds = new Bounds(new Vector3(0, 0, 0), new Vector3(1.8f, 2.2f, 1.2f));

                // El móvil va en la mano derecha, apagado
                var tel = new GameObject("Movil");
                tel.transform.SetParent(huesos[Array.IndexOf(Huesos, "RightHand")], false);
                tel.transform.localPosition = new Vector3(0.085f, 0.03f, 0.02f);
                tel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(-70f, 0f, 0f);
                tel.AddComponent<MeshFilter>().sharedMesh = movil;
                var mr = tel.AddComponent<MeshRenderer>();
                mr.sharedMaterial = matMovil;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tel.SetActive(false);

                var avatar = CrearAvatar(go, mujer);
                avatar.name = "Avatar_" + nombre;
                avatar = Guardar(avatar, $"{Carpeta}/Avatar_{nombre}.asset");
                var anim = go.AddComponent<Animator>();
                anim.avatar = avatar;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                if (avatarH == null) { avatarH = avatar; modeloH = Object.Instantiate(go); modeloH.name = "ModeloPoses"; }
                var ruta = $"{Carpeta}/Maniqui_{nombre}.prefab";
                var pf = PrefabUtility.SaveAsPrefabAsset(go, ruta);
                Object.DestroyImmediate(go);
                prefabs.Add(pf);
            }

            // Clips y controlador (las poses se resuelven sobre el maniquí de hombre)
            var clips = Clips(modeloH, avatarH);
            Object.DestroyImmediate(modeloH);
            var ctrl = Controlador(clips);
            foreach (var pf in prefabs)
            {
                var a = pf.GetComponent<Animator>();
                a.runtimeAnimatorController = ctrl;
                EditorUtility.SetDirty(pf);
                PrefabUtility.SavePrefabAsset(pf);
            }

            rec.avatares = prefabs.ToArray();
            rec.controlador = ctrl;
            rec.movil = movil;
            rec.materialMovil = matMovil;
            rec.mallasLejanas = lejanas.ToArray();
            rec.materialLejano = matLejano;
            rec.colorearAvatares = true;
            rec.perfiles = new PerfilPersonaje[0];          // (los de Mixamo los vuelve a poner ConstructorMixamo)
            rec.alturaAsientoClip = 0.46f;
            rec.movilSentado = true;
            EditorUtility.SetDirty(rec);
            AssetDatabase.SaveAssets();
            return $"{prefabs.Count} maniquíes, {clips.Count} clips, controlador y {lejanas.Count} mallas lejanas " +
                   $"(vértices: cercano {prefabs[0].GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertexCount}, lejano {lejanas[0].vertexCount})" +
                   (matLejano == null ? " — falta el shader Gemelo/MultitudInstanciada" : "");
        }

        static GameObject Esqueleto(string nombre, bool mujer, out Transform[] huesos)
        {
            var j = Articulaciones(mujer);
            var raiz = new GameObject(nombre);
            var porNombre = new Dictionary<string, Transform>();
            huesos = new Transform[Huesos.Length];
            for (int i = 0; i < Huesos.Length; i++)
            {
                var h = Huesos[i];
                var t = new GameObject(h).transform;
                var p = Padre[h];
                t.SetParent(p == null ? raiz.transform : porNombre[p], false);
                t.position = j[h];
                t.rotation = Quaternion.identity;
                porNombre[h] = t;
                huesos[i] = t;
            }
            return raiz;
        }

        static Avatar CrearAvatar(GameObject raiz, bool mujer)
        {
            var humanos = new List<HumanBone>();
            foreach (var h in Huesos)
                humanos.Add(new HumanBone { humanName = h, boneName = h, limit = new HumanLimit { useDefaultValues = true } });
            var esqueleto = new List<SkeletonBone>();
            foreach (var t in raiz.GetComponentsInChildren<Transform>())
                esqueleto.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            var desc = new HumanDescription
            {
                human = humanos.ToArray(), skeleton = esqueleto.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            var av = AvatarBuilder.BuildHumanAvatar(raiz, desc);
            if (!av.isValid || !av.isHuman) throw new Exception("Avatar humanoide no válido");
            return av;
        }

        // ───────────────────────── Poses y clips ─────────────────────────

        class Pose
        {
            public Vector3 cadera = new(0, 0.96f, 0);
            public readonly Dictionary<string, Quaternion> giro = new();
            public Pose Copia()
            {
                var p = new Pose { cadera = cadera };
                foreach (var kv in giro) p.giro[kv.Key] = kv.Value;
                return p;
            }
            // Giros en grados. Pierna: + adelante; rodilla: + flexión. Brazo: bajar (0 = cruz, 80 = pegado al cuerpo),
            // adelante (+ hacia delante) y codo (+ flexión). Tronco y cabeza: + hacia delante; giro + a la izquierda.
            public Pose Pierna(string lado, float adelante, float rodilla, float abrir = 0f)
            {
                float s = lado == "Left" ? -1 : 1;
                giro[lado + "UpperLeg"] = Quaternion.AngleAxis(-adelante, Vector3.right) * Quaternion.AngleAxis(s * abrir, Vector3.forward);
                giro[lado + "LowerLeg"] = Quaternion.AngleAxis(rodilla, Vector3.right);
                return this;
            }
            public Pose Brazo(string lado, float bajar, float adelante, float codo, float girar = 0f)
            {
                bool izq = lado == "Left";
                giro[lado + "UpperArm"] = Quaternion.AngleAxis(-adelante, Vector3.right) * Quaternion.AngleAxis(izq ? bajar : -bajar, Vector3.forward)
                                          * Quaternion.AngleAxis(izq ? girar : -girar, Vector3.right);
                giro[lado + "LowerArm"] = Quaternion.AngleAxis(izq ? codo : -codo, Vector3.up);
                return this;
            }
            public Pose Tronco(float adelante, float girar = 0f, float ladear = 0f)
            {
                giro["Spine"] = Quaternion.AngleAxis(girar * 0.5f, Vector3.up) * Quaternion.AngleAxis(adelante * 0.5f, Vector3.right) * Quaternion.AngleAxis(ladear, Vector3.forward);
                giro["Chest"] = Quaternion.AngleAxis(girar * 0.5f, Vector3.up) * Quaternion.AngleAxis(adelante * 0.5f, Vector3.right);
                return this;
            }
            public Pose Cabeza(float adelante, float girar = 0f, float ladear = 0f)
            {
                giro["Neck"] = Quaternion.AngleAxis(girar * 0.4f, Vector3.up) * Quaternion.AngleAxis(adelante * 0.4f, Vector3.right);
                giro["Head"] = Quaternion.AngleAxis(girar * 0.6f, Vector3.up) * Quaternion.AngleAxis(adelante * 0.6f, Vector3.right) * Quaternion.AngleAxis(ladear, Vector3.forward);
                return this;
            }
            public Pose Pies(float izq, float der)
            {
                giro["LeftFoot"] = Quaternion.AngleAxis(izq, Vector3.right);
                giro["RightFoot"] = Quaternion.AngleAxis(der, Vector3.right);
                return this;
            }
        }

        static Pose DePie(float respira = 0f)
        {
            var p = new Pose();
            p.Pierna("Left", 0, 2).Pierna("Right", 0, 2).Brazo("Left", 78, 3, 10).Brazo("Right", 78, 3, 10).Tronco(1 + respira).Cabeza(2);
            return p;
        }

        static Pose Sentada(float respira = 0f)
        {
            var p = new Pose { cadera = new Vector3(0, 0.55f, -0.04f) };
            p.Pierna("Left", 86, 84, 4).Pierna("Right", 86, 86, 4).Pies(-6, -6)
             .Brazo("Left", 70, 38, 62, 10).Brazo("Right", 70, 38, 62, 10).Tronco(6 + respira).Cabeza(6);
            return p;
        }

        // Clips por nombre → lista de (tiempo, pose)
        static Dictionary<string, AnimationClip> Clips(GameObject modelo, Avatar avatar)
        {
            var huesos = new Dictionary<string, Transform>();
            foreach (var t in modelo.GetComponentsInChildren<Transform>()) huesos[t.name] = t;
            var bindPos = new Dictionary<string, Vector3>();
            foreach (var h in Huesos) bindPos[h] = huesos[h].localPosition;
            var handler = new HumanPoseHandler(avatar, modelo.transform);

            HumanPose Resolver(Pose p)
            {
                foreach (var h in Huesos) { huesos[h].localPosition = bindPos[h]; huesos[h].localRotation = Quaternion.identity; }
                huesos["Hips"].localPosition = p.cadera;
                foreach (var kv in p.giro) huesos[kv.Key].localRotation = kv.Value;
                var hp = new HumanPose();
                handler.GetHumanPose(ref hp);
                return hp;
            }

            var defs = new Dictionary<string, List<(float t, Pose p)>>();

            defs["Reposo"] = new() { (0f, DePie()), (1.6f, DePie(1.2f).Cabeza(2, 6)), (3.2f, DePie()) };

            // Andar: ciclo de 1 s a 1,4 m/s (zancada de 0,7 m)
            Pose Paso(float fase, float amp, float rodSwing, float rodApoyo, float brazo, float codo, float baja, float tronco)
            {
                var p = new Pose();
                float s = Mathf.Sin(fase * Mathf.PI * 2f);
                float c = Mathf.Cos(fase * Mathf.PI * 2f);
                // Pierna izquierda adelante en fase 0; la que oscila dobla la rodilla
                float ri = s < 0 ? rodApoyo + rodSwing * Mathf.Max(0, -Mathf.Sin(fase * Mathf.PI * 2f + 0.5f)) : rodApoyo;
                float rd = s > 0 ? rodApoyo + rodSwing * Mathf.Max(0, Mathf.Sin(fase * Mathf.PI * 2f + 0.5f)) : rodApoyo;
                p.Pierna("Left", amp * c, ri).Pierna("Right", -amp * c, rd)
                 .Brazo("Left", 76, -brazo * c, codo + 6 * Mathf.Max(0, -c)).Brazo("Right", 76, brazo * c, codo + 6 * Mathf.Max(0, c))
                 .Tronco(tronco, 4 * c).Cabeza(2, -2 * c);
                p.cadera = new Vector3(0.012f * s, 0.96f - baja * (1 - Mathf.Abs(Mathf.Sin(fase * Mathf.PI * 4f + 0.2f))) - 0.012f, 0);
                return p;
            }
            defs["Andar"] = new();
            for (int k = 0; k <= 8; k++) defs["Andar"].Add((k / 8f, Paso(k / 8f, 24, 52, 6, 16, 14, 0.025f, 2)));
            defs["Correr"] = new();
            for (int k = 0; k <= 8; k++) defs["Correr"].Add((0.7f * k / 8f, Paso(k / 8f, 40, 85, 20, 32, 80, 0.06f, 11)));
            defs["CorrerRapido"] = new();
            for (int k = 0; k <= 8; k++) defs["CorrerRapido"].Add((0.62f * k / 8f, Paso(k / 8f, 48, 100, 22, 40, 85, 0.07f, 14)));

            defs["Sentado"] = new() { (0f, Sentada()), (2f, Sentada(1.5f).Cabeza(8, 4)), (4f, Sentada()) };
            defs["SentadoHablando"] = new()
            {
                (0f, Sentada().Cabeza(4, 18).Brazo("Right", 55, 45, 85)),
                (0.8f, Sentada(1).Cabeza(2, 22).Brazo("Right", 45, 55, 100, -20)),
                (1.6f, Sentada().Cabeza(5, 16).Brazo("Right", 60, 40, 75)),
                (2.4f, Sentada(1).Cabeza(3, 20).Brazo("Right", 48, 50, 95, -10)),
                (3.2f, Sentada().Cabeza(4, 18).Brazo("Right", 55, 45, 85)),
            };
            Pose SentadaMovil(float c) => Sentada().Cabeza(28 + c, 0).Brazo("Left", 72, 42, 112).Brazo("Right", 72, 42, 112);
            defs["SentadoMovil"] = new() { (0f, SentadaMovil(0)), (2f, SentadaMovil(3)), (4f, SentadaMovil(0)) };
            // Escribiendo en la mesa: cabeza baja, mano derecha que va y viene sobre el papel
            Pose Escribir(float c) => Sentada().Tronco(14).Cabeza(24, -4).Brazo("Right", 66, 50 + c, 100 - c).Brazo("Left", 68, 46, 96);
            defs["SentadoEscribiendo"] = new() { (0f, Escribir(0)), (0.5f, Escribir(4)), (1f, Escribir(-2)), (1.5f, Escribir(3)), (2f, Escribir(0)) };

            Pose Hablar(float a, float b) => DePie().Brazo("Right", 50 + a, 40 + b, 80 + b, -15).Brazo("Left", 72, 15 + a * 0.3f, 35).Cabeza(-2, 6 - a * 0.3f).Tronco(1, a * 0.2f);
            defs["HablarDePie"] = new() { (0f, Hablar(0, 0)), (0.7f, Hablar(-12, 14)), (1.5f, Hablar(6, -6)), (2.2f, Hablar(-8, 10)), (3f, Hablar(0, 0)) };
            Pose Movil(float c) => DePie().Cabeza(30 + c, 0).Brazo("Right", 74, 30, 118).Brazo("Left", 74, 26, 100);
            defs["MovilDePie"] = new() { (0f, Movil(0)), (2f, Movil(3)), (4f, Movil(0)) };
            Pose Aire(float c) { var p = new Pose(); p.Pierna("Left", 35 + c, 60).Pierna("Right", 10, 35).Brazo("Left", 35, 10, 30).Brazo("Right", 35, 10, 30).Tronco(6).Cabeza(-4); p.cadera = new Vector3(0, 0.93f, 0); return p; }
            defs["Aire"] = new() { (0f, Aire(0)), (0.5f, Aire(5)), (1f, Aire(0)) };

            var clips = new Dictionary<string, AnimationClip>();
            foreach (var kv in defs)
            {
                var claves = new List<(float t, HumanPose hp)>();
                foreach (var (t, p) in kv.Value) claves.Add((t, Resolver(p)));
                clips[kv.Key] = GuardarClip(kv.Key, claves);
            }
            handler.Dispose();
            return clips;
        }

        static AnimationClip GuardarClip(string nombre, List<(float t, HumanPose hp)> claves)
        {
            var ruta = $"{CarpetaClips}/{nombre}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
            bool nuevo = clip == null;
            if (nuevo) clip = new AnimationClip { name = nombre };
            else clip.ClearCurves();
            clip.frameRate = 30;

            void Curva(string prop, Func<HumanPose, float> f)
            {
                var c = new AnimationCurve();
                foreach (var (t, hp) in claves) c.AddKey(t, f(hp));
                for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), c);
            }
            Curva("RootT.x", h => 0f);
            Curva("RootT.y", h => h.bodyPosition.y);
            Curva("RootT.z", h => h.bodyPosition.z);
            Curva("RootQ.x", h => h.bodyRotation.x);
            Curva("RootQ.y", h => h.bodyRotation.y);
            Curva("RootQ.z", h => h.bodyRotation.z);
            Curva("RootQ.w", h => h.bodyRotation.w);
            for (int i = 0; i < 55; i++)
            {
                if (i >= 15 && i <= 20) continue;   // ojos y mandíbula
                int k = i;
                Curva(HumanTrait.MuscleName[i], h => h.muscles[k]);
            }
            var ajustes = AnimationUtility.GetAnimationClipSettings(clip);
            ajustes.loopTime = true;
            ajustes.loopBlend = true;
            ajustes.loopBlendOrientation = true;
            ajustes.loopBlendPositionY = true;
            ajustes.loopBlendPositionXZ = true;
            ajustes.keepOriginalOrientation = true;
            ajustes.keepOriginalPositionY = true;
            ajustes.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, ajustes);
            if (nuevo) AssetDatabase.CreateAsset(clip, ruta);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        // ───────────────────────── Controlador ─────────────────────────

        public const string ParamMovil = "Movil";

        static AnimatorController Controlador(Dictionary<string, AnimationClip> clips)
        {
            // Se construye aparte y se copia encima del anterior para conservar su GUID (lo usan los prefabs, el
            // asset de recursos y los AnimatorOverrideController de Mixamo)
            var ruta = Carpeta + "/Persona.controller";
            var tmp = Carpeta + "/Tmp/Persona.controller";
            Directory(Carpeta + "/Tmp");
            AssetDatabase.DeleteAsset(tmp);
            var c = AnimatorController.CreateAnimatorControllerAtPath(tmp);
            c.AddParameter(ParamAvatar.Velocidad, AnimatorControllerParameterType.Float);
            c.AddParameter(ParamAvatar.Sentado, AnimatorControllerParameterType.Bool);
            c.AddParameter(ParamAvatar.Hablando, AnimatorControllerParameterType.Bool);
            c.AddParameter(new AnimatorControllerParameter { name = ParamAvatar.EnSuelo, type = AnimatorControllerParameterType.Bool, defaultBool = true });
            c.AddParameter(ParamAvatar.Saltar, AnimatorControllerParameterType.Trigger);
            c.AddParameter(ParamMovil, AnimatorControllerParameterType.Bool);
            c.AddParameter(new AnimatorControllerParameter { name = ParamAvatar.Escribiendo, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var sm = c.layers[0].stateMachine;
            var loco = c.CreateBlendTreeInController("Locomocion", out var arbol, 0);
            arbol.blendType = BlendTreeType.Simple1D;
            arbol.blendParameter = ParamAvatar.Velocidad;
            arbol.useAutomaticThresholds = false;
            arbol.AddChild(clips["Reposo"], 0f);
            arbol.AddChild(clips["Andar"], 1.4f);
            arbol.AddChild(clips["Correr"], 3.0f);
            arbol.AddChild(clips["CorrerRapido"], 4.6f);
            sm.defaultState = loco;

            AnimatorState Estado(string n, Vector3 pos)
            {
                var s = sm.AddState(n, pos);
                s.motion = clips[n];
                return s;
            }
            var hablar = Estado("HablarDePie", new Vector3(300, 0));
            var movil = Estado("MovilDePie", new Vector3(300, 80));
            var sentado = Estado("Sentado", new Vector3(600, 0));
            var sentHabla = Estado("SentadoHablando", new Vector3(600, 80));
            // Sentado con las manos ocupadas: teclear (Escribiendo = 0) o escribir (1); los maniquíes, móvil o escribir
            var sentMovil = c.CreateBlendTreeInController("SentadoMovil", out var arbolMovil, 0);
            arbolMovil.blendType = BlendTreeType.Simple1D;
            arbolMovil.blendParameter = ParamAvatar.Escribiendo;
            arbolMovil.useAutomaticThresholds = false;
            arbolMovil.AddChild(clips["SentadoMovil"], 0f);
            arbolMovil.AddChild(clips["SentadoEscribiendo"], 1f);
            var aire = Estado("Aire", new Vector3(0, 250));

            const AnimatorConditionMode Si = AnimatorConditionMode.If, No = AnimatorConditionMode.IfNot;
            (AnimatorConditionMode, float, string) C(AnimatorConditionMode m, string p, float v = 0) => (m, v, p);
            var condiciones = new Dictionary<AnimatorState, List<(AnimatorConditionMode, float, string)[]>>
            {
                [loco] = new()
                {
                    new[] { C(No, ParamAvatar.Sentado), C(No, ParamAvatar.Hablando), C(No, ParamMovil) },
                    new[] { C(No, ParamAvatar.Sentado), C(AnimatorConditionMode.Greater, ParamAvatar.Velocidad, 0.3f) },
                },
                [hablar] = new() { new[] { C(No, ParamAvatar.Sentado), C(Si, ParamAvatar.Hablando), C(AnimatorConditionMode.Less, ParamAvatar.Velocidad, 0.3f) } },
                [movil] = new() { new[] { C(No, ParamAvatar.Sentado), C(No, ParamAvatar.Hablando), C(Si, ParamMovil), C(AnimatorConditionMode.Less, ParamAvatar.Velocidad, 0.3f) } },
                [sentado] = new() { new[] { C(Si, ParamAvatar.Sentado), C(No, ParamAvatar.Hablando), C(No, ParamMovil) } },
                [sentHabla] = new() { new[] { C(Si, ParamAvatar.Sentado), C(Si, ParamAvatar.Hablando) } },
                [sentMovil] = new() { new[] { C(Si, ParamAvatar.Sentado), C(No, ParamAvatar.Hablando), C(Si, ParamMovil) } },
            };
            foreach (var desde in condiciones.Keys)
            foreach (var kv in condiciones)
            {
                if (kv.Key == desde) continue;
                bool sentarse = (desde == sentado || desde == sentHabla || desde == sentMovil) != (kv.Key == sentado || kv.Key == sentHabla || kv.Key == sentMovil);
                foreach (var conj in kv.Value)
                {
                    var t = desde.AddTransition(kv.Key);
                    t.hasExitTime = false;
                    t.duration = sentarse ? 0.55f : 0.3f;
                    t.AddCondition(Si, 0, ParamAvatar.EnSuelo);
                    foreach (var (m, v, p) in conj) t.AddCondition(m, v, p);
                }
            }
            var aAire = sm.AddAnyStateTransition(aire);
            aAire.AddCondition(No, 0, ParamAvatar.EnSuelo);
            aAire.canTransitionToSelf = false;
            aAire.duration = 0.15f;
            var salto = sm.AddAnyStateTransition(aire);
            salto.AddCondition(Si, 0, ParamAvatar.Saltar);
            salto.canTransitionToSelf = false;
            salto.duration = 0.1f;
            var aterrizar = aire.AddTransition(loco);
            aterrizar.hasExitTime = true;
            aterrizar.exitTime = 0.3f;
            aterrizar.duration = 0.2f;
            aterrizar.AddCondition(Si, 0, ParamAvatar.EnSuelo);
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            System.IO.File.Copy(tmp, ruta, true);
            AssetDatabase.DeleteAsset(Carpeta + "/Tmp");
            AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(ruta);
        }

        // ───────────────────────── Utilidades ─────────────────────────

        static Mesh MallaMovil()
        {
            var m = new Malla { seg = 4, cap = 1 };
            m.Pieza(0, 0, 0, Vector3.zero, Vector3.zero);
            // Losa de 7 × 14,5 × 0,8 cm como cápsula aplastada
            m.Capsula(new Vector3(0, -0.055f, 0), new Vector3(0, 0.055f, 0), 0.036f, 0.036f, 0.12f);
            return m.Crear("Movil", false, false);
        }

        static T Guardar<T>(T obj, string ruta) where T : Object
        {
            var previo = AssetDatabase.LoadAssetAtPath<T>(ruta);
            // CopySerialized sobre una malla existente pierde canales (uv0): las mallas se rehacen
            if (previo != null && obj is Mesh) { AssetDatabase.DeleteAsset(ruta); previo = null; }
            if (previo == null) { AssetDatabase.CreateAsset(obj, ruta); return obj; }
            EditorUtility.CopySerialized(obj, previo);
            Object.DestroyImmediate(obj);
            EditorUtility.SetDirty(previo);
            return previo;
        }

        static Material Material(string ruta, Shader sh, Action<Material> config)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
            m.shader = sh;
            config(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Directory(string ruta)
        {
            if (AssetDatabase.IsValidFolder(ruta)) return;
            var padre = System.IO.Path.GetDirectoryName(ruta)!.Replace('\\', '/');
            Directory(padre);
            AssetDatabase.CreateFolder(padre, System.IO.Path.GetFileName(ruta));
        }
    }
}
