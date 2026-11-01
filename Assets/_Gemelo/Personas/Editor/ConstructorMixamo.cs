// Monta los personajes de Mixamo del gemelo (Tools ▸ Gemelo ▸ Personajes de Mixamo), a partir de lo que deja
// Herramientas/mixamo/preparar_mixamo.py en Assets/_Gemelo/Personas/Mixamo/:
//  · materiales URP/Lit de cada personaje (Personajes/<Nombre>/Materiales/), con color, normal y brillo (máscara con
//    el gloss en A → smoothness); pelo y pestañas con recorte alfa y a dos caras (sin el halo blanco de los nonPBR);
//    el FBX se reimporta con esos materiales (ImportadorMixamo los pone con AddRemap);
//  · dos AnimatorOverrideController sobre Maniquies/Persona.controller (Persona_Hombre y Persona_Mujer: cambian el
//    andar, el reposo, el hablar y el sentado), con los clips de Animaciones/;
//  · un prefab por personaje (Prefabs/<Nombre>.prefab) con su Animator y el móvil en la mano derecha;
//  · RecursosPersonas: los 17 NPC en Avatares con su perfil (sexo, papel, altura y colores dominantes), Brian y Megan
//    en Jugadores, sin teñir, sin móvil sentado y la altura del asiento de la postura sentada (medida aquí).
// Volver a los maniquíes: Tools ▸ Gemelo ▸ Maniquíes.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gemelo
{
    public static class ConstructorMixamo
    {
        const string Carpeta = ImportadorMixamo.Carpeta;
        const string CarpetaPrefabs = Carpeta + "/Prefabs";

        static readonly (string nombre, bool mujer, PapelPersonaje papel)[] Npcs =
        {
            ("Remy", false, PapelPersonaje.Alumnado), ("Bryce", false, PapelPersonaje.Alumnado), ("James", false, PapelPersonaje.Alumnado),
            ("Lewis", false, PapelPersonaje.Alumnado), ("David", false, PapelPersonaje.Alumnado),
            ("Kate", true, PapelPersonaje.Alumnado), ("Sophie", true, PapelPersonaje.Alumnado), ("Elizabeth", true, PapelPersonaje.Alumnado),
            ("Jody", true, PapelPersonaje.Alumnado), ("Louise", true, PapelPersonaje.Alumnado),
            ("Josh", false, PapelPersonaje.Profesorado), ("Joe", false, PapelPersonaje.Profesorado),
            ("Martha", true, PapelPersonaje.Profesorado), ("Suzie", true, PapelPersonaje.Profesorado), ("Roth", true, PapelPersonaje.Profesorado),
            ("Pete", false, PapelPersonaje.Mantenimiento), ("Alex", false, PapelPersonaje.Vigilancia),
        };
        static readonly (string nombre, bool mujer)[] Jugadores = { ("Brian", false), ("Megan", true) };

        // Clip del maniquí → animación de Mixamo (Animaciones/<nombre>.fbx)
        static readonly Dictionary<string, string> Comunes = new()
        {
            ["Correr"] = "Run_Jogging", ["CorrerRapido"] = "Run_Standard", ["SentadoHablando"] = "Sit_Talking",
            ["SentadoMovil"] = "Sit_Typing", ["SentadoEscribiendo"] = "Sit_Writing", ["MovilDePie"] = "Texting_Standing",
            ["Aire"] = "Falling_Idle",
        };
        static readonly Dictionary<string, string> DeHombre = new()
        {
            ["Reposo"] = "Idle_Standing", ["Andar"] = "Walk_MaleStandard", ["HablarDePie"] = "Talk_GeneralConversation", ["Sentado"] = "Sit_Breathing",
        };
        static readonly Dictionary<string, string> DeMujer = new()
        {
            ["Reposo"] = "Idle_WeightShift", ["Andar"] = "Walk_Female", ["HablarDePie"] = "Talk_AskingOneHand", ["Sentado"] = "Sit_StillChair",
        };

        // Móvil en la mano derecha de los Mixamo (espacio local del hueso mixamorig:RightHand, a escala 1)
        public static readonly Vector3 MovilPos = new(0.005f, 0.075f, 0.03f);   // dedos en +Y, palma hacia +Z
        public static readonly Vector3 MovilGiro = Vector3.zero;              // a lo largo de la mano, pantalla a la cara

        [MenuItem("Tools/Gemelo/Personajes de Mixamo")]
        public static void ConstruirDesdeMenu() => EditorUtility.DisplayDialog("Personajes de Mixamo", Construir(), "Vale");

        public static string Construir()
        {
            var rec = ConstructorNavegacion.Recursos();
            var baseCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Gemelo/Personas/Maniquies/Persona.controller");
            if (baseCtrl == null) return "Falta Maniquies/Persona.controller: Tools ▸ Gemelo ▸ Maniquíes";
            Carpetas(CarpetaPrefabs);
            var log0 = new List<string>();

            // Las posturas sentadas de Mixamo no ponen la cadera en el mismo sitio: «Male Writing While Seated» se sienta
            // 20 cm más adelante (las piernas atravesaban la mesa) y «Sitting Still In A Chair», «Sitting And Talking» y
            // la de escribir, 5-8 cm más altas (en una silla más alta: flotaban sobre la nuestra). Se usan copias con el cuerpo
            // llevado a la cadera de «Sitting At A Computer And Typing» en planta y a medio camino en altura (bajarla del
            // todo hundía los pies en el suelo)
            foreach (var n in new[] { "Sit_Writing", "Sit_Talking", "Sit_StillChair" })
                recentrados[n] = Recentrar(n, "Sit_Typing", log0);
            var hombre = Override(baseCtrl, "Persona_Hombre", DeHombre);
            var mujer = Override(baseCtrl, "Persona_Mujer", DeMujer);

            var avatares = new List<GameObject>();
            var perfiles = new List<PerfilPersonaje>();
            var log = log0;
            foreach (var (n, m, papel) in Npcs)
            {
                var pf = Personaje(n, m ? mujer : hombre, out var perfil, log);
                if (pf == null) continue;
                perfil.mujer = m;
                perfil.papel = papel;
                avatares.Add(pf);
                perfiles.Add(perfil);
            }
            var jugadores = new List<GameObject>();
            foreach (var (n, m) in Jugadores)
            {
                var pf = Personaje(n, m ? mujer : hombre, out _, log);
                if (pf != null) jugadores.Add(pf);
            }

            float asiento = MedirAsiento(avatares, perfiles);
            rec.avatares = avatares.ToArray();
            rec.perfiles = perfiles.ToArray();
            rec.jugadores = jugadores.ToArray();
            rec.controlador = baseCtrl;
            rec.colorearAvatares = false;
            rec.movilSentado = false;
            rec.alturaAsientoClip = asiento;
            if (escena.IsValid()) { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(escena); escena = default; }
            EditorUtility.SetDirty(rec);
            AssetDatabase.SaveAssets();
            return $"{avatares.Count} NPC y {jugadores.Count} jugadores; asiento de la postura sentada a {asiento:F3} m (escala 1)" +
                   (log.Count > 0 ? "\n" + string.Join("\n", log) : "");
        }

        // ───────────────────────── Controladores ─────────────────────────

        static readonly Dictionary<string, AnimationClip> recentrados = new();

        static AnimationClip Clip(string nombre)
        {
            if (recentrados.TryGetValue(nombre, out var rc) && rc != null) return rc;
            var ruta = $"{ImportadorMixamo.CarpetaAnimaciones}/{nombre}.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        }

        static AnimatorOverrideController Override(AnimatorController baseCtrl, string nombre, Dictionary<string, string> propios)
        {
            var ruta = $"{Carpeta}/{nombre}.overrideController";
            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(ruta);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(baseCtrl) { name = nombre };
                AssetDatabase.CreateAsset(oc, ruta);
            }
            oc.runtimeAnimatorController = baseCtrl;
            var pares = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pares);
            for (int i = 0; i < pares.Count; i++)
            {
                var original = pares[i].Key;
                string destino = propios.TryGetValue(original.name, out var d) ? d : Comunes.TryGetValue(original.name, out d) ? d : null;
                var clip = destino != null ? Clip(destino) : null;
                if (destino != null && clip == null) Debug.LogWarning($"[Mixamo] Falta la animación {destino}");
                pares[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, clip);
            }
            oc.ApplyOverrides(pares);
            EditorUtility.SetDirty(oc);
            return oc;
        }

        // Copia del clip (Animaciones/<nombre>_Centrado.anim) con el cuerpo (RootT, en unidades del avatar) desplazado
        // para que la cadera quede donde en la referencia (medido sobre Bryce a mitad de cada clip)
        static AnimationClip Recentrar(string nombre, string referencia, List<string> log)
        {
            recentrados.Remove(nombre);
            var clip = Clip(nombre);
            var refc = Clip(referencia);
            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>($"{ImportadorMixamo.CarpetaPersonajes}/Bryce/Bryce.fbx");
            if (clip == null || refc == null || modelo == null) { log.Add($"No se pudo recentrar {nombre}"); return clip; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(modelo, Escena());
            Vector3 d;
            float hs;
            try
            {
                var an = go.GetComponent<Animator>();
                Vector3 Cadera(AnimationClip c)
                {
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, c, c.length * 0.5f);
                    AnimationMode.EndSampling();
                    var h = an.GetBoneTransform(HumanBodyBones.Hips).position;
                    AnimationMode.StopAnimationMode();
                    return h;
                }
                d = Cadera(clip) - Cadera(refc);
                hs = Mathf.Max(0.1f, an.humanScale);
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
            var ruta = $"{ImportadorMixamo.CarpetaAnimaciones}/{nombre}_Centrado.anim";
            var nuevo = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
            bool crear = nuevo == null;
            if (crear) nuevo = new AnimationClip();
            else nuevo.ClearCurves();
            nuevo.name = nombre + "_Centrado";
            nuevo.frameRate = clip.frameRate;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                float dx = b.propertyName == "RootT.x" ? d.x / hs : b.propertyName == "RootT.y" ? 0.5f * d.y / hs : b.propertyName == "RootT.z" ? d.z / hs : 0f;
                if (dx != 0f)
                {
                    var k = c.keys;
                    for (int i = 0; i < k.Length; i++) k[i].value -= dx;
                    c.keys = k;
                }
                AnimationUtility.SetEditorCurve(nuevo, b, Reducir(c, b.propertyName.StartsWith("Root") ? 0.0007f : 0.002f));
            }
            AnimationUtility.SetAnimationClipSettings(nuevo, AnimationUtility.GetAnimationClipSettings(clip));
            if (crear) AssetDatabase.CreateAsset(nuevo, ruta);
            else EditorUtility.SetDirty(nuevo);
            log.Add($"{nombre}: cadera movida ({-d.x:F2}, {-0.5f * d.y:F2}, {-d.z:F2}) m (a la de {referencia})");
            return nuevo;
        }

        // Quita las claves que una interpolación suave entre sus vecinas ya da (error ≤ eps): la copia en .anim (texto)
        // de una animación larga ocupaba 15 MB
        static AnimationCurve Reducir(AnimationCurve c, float eps)
        {
            var k = c.keys;
            if (k.Length < 3) return c;
            var quedan = new List<Keyframe> { k[0] };
            int ultimo = 0;
            for (int i = 1; i < k.Length - 1; i++)
            {
                // ¿Se puede saltar de la última clave guardada a la siguiente (i + 1) sin pasar por las de en medio?
                var a = k[ultimo];
                var b = k[i + 1];
                bool vale = true;
                for (int j = ultimo + 1; j <= i && vale; j++)
                {
                    float t = (k[j].time - a.time) / Mathf.Max(1e-6f, b.time - a.time);
                    float s = t * t * (3f - 2f * t);
                    float lin = Mathf.Lerp(a.value, b.value, t), suave = Mathf.Lerp(a.value, b.value, s);
                    vale = Mathf.Abs(k[j].value - lin) <= eps && Mathf.Abs(k[j].value - suave) <= eps * 4f;
                }
                if (vale) continue;
                quedan.Add(k[i]);
                ultimo = i;
            }
            quedan.Add(k[^1]);
            var r = new AnimationCurve(quedan.ToArray()) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
            for (int i = 0; i < r.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(r, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(r, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            return r;
        }

        // ───────────────────────── Personajes ─────────────────────────

        static GameObject Personaje(string nombre, RuntimeAnimatorController ctrl, out PerfilPersonaje perfil, List<string> log)
        {
            perfil = null;
            var rutaFbx = $"{ImportadorMixamo.CarpetaPersonajes}/{nombre}/{nombre}.fbx";
            var info = ImportadorMixamo.Info(rutaFbx);
            if (info == null || AssetDatabase.LoadAssetAtPath<GameObject>(rutaFbx) == null)
            {
                log.Add($"Falta {rutaFbx} (Herramientas/mixamo/preparar_mixamo.py)");
                return null;
            }
            Materiales(rutaFbx, info);
            AssetDatabase.ImportAsset(rutaFbx, ImportAssetOptions.ForceUpdate);
            var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(rutaFbx);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(rutaFbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) { log.Add($"{nombre}: avatar humanoide no válido"); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(modelo, Escena());
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = nombre;
            var an = go.GetComponent<Animator>();
            if (an == null) an = go.AddComponent<Animator>();
            an.avatar = avatar;
            an.runtimeAnimatorController = ctrl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
                r.allowOcclusionWhenDynamic = true;
            }
            // Móvil en la mano derecha, apagado
            var rec = RecursosPersonas.Cargar();
            var mano = an.GetBoneTransform(HumanBodyBones.RightHand);
            if (mano != null && rec != null && rec.movil != null)
            {
                var tel = new GameObject("Movil");
                tel.transform.SetParent(mano, false);
                float k = 1f / Mathf.Max(0.001f, mano.lossyScale.x);
                tel.transform.localPosition = MovilPos * k;
                tel.transform.localRotation = Quaternion.Euler(MovilGiro);
                tel.transform.localScale = Vector3.one * k;
                tel.AddComponent<MeshFilter>().sharedMesh = rec.movil;
                var mr = tel.AddComponent<MeshRenderer>();
                mr.sharedMaterial = rec.materialMovil;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tel.SetActive(false);
            }
            var pf = PrefabUtility.SaveAsPrefabAsset(go, $"{CarpetaPrefabs}/{nombre}.prefab");
            Object.DestroyImmediate(go);

            perfil = new PerfilPersonaje
            {
                nombre = nombre,
                altura = info.altura * ImportadorMixamo.EscalaImportacion(info),
                piel = Color(info.colores?.piel), top = Color(info.colores?.top), mangas = Color(info.colores?.mangas),
                bajo = Color(info.colores?.bajo), zapatos = Color(info.colores?.zapatos), pelo = Color(info.colores?.pelo),
            };
            return pf;
        }

        static Color32 Color(string hex) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? (Color32)c : new Color32(128, 128, 128, 255);

        static void Materiales(string rutaFbx, ImportadorMixamo.InfoPersonaje info)
        {
            var dir = Path.GetDirectoryName(rutaFbx)!.Replace('\\', '/');
            Carpetas(dir + "/Materiales");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var m in info.materiales)
            {
                var ruta = ImportadorMixamo.RutaMaterial(rutaFbx, m.nombre);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
                if (mat == null) { mat = new Material(lit) { name = m.nombre }; AssetDatabase.CreateAsset(mat, ruta); }
                mat.shader = lit;
                mat.shaderKeywords = new string[0];
                Texture2D T(string f) => string.IsNullOrEmpty(f) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{f}");
                var color = T(m.color);
                mat.SetTexture("_BaseMap", color);
                mat.SetColor("_BaseColor", UnityEngine.Color.white);
                var normal = T(m.normal);
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                if (normal != null) mat.EnableKeyword("_NORMALMAP");
                var mascara = T(m.mascara);
                mat.SetFloat("_WorkflowMode", 1f);   // metálico
                mat.SetFloat("_Metallic", 0f);
                mat.SetTexture("_MetallicGlossMap", mascara);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                // El gloss de los nonPBR da piel y tela demasiado brillantes en URP: se escala
                mat.SetFloat("_Smoothness", mascara != null ? 0.5f : (m.recorte ? 0.3f : 0.2f));
                if (mascara != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SpecularHighlights", 1f);
                mat.SetFloat("_EnvironmentReflections", 1f);
                mat.SetFloat("_Surface", 0f);
                if (m.recorte)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", ImportadorMixamo.RecorteAlfa);
                    mat.SetFloat("_Cull", 0f);              // pelo de tarjetas: a dos caras
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                }
                else
                {
                    mat.SetFloat("_AlphaClip", 0f);
                    mat.SetFloat("_Cull", 2f);
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                }
                mat.enableInstancing = false;
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        // Altura del asiento en la postura «Sentado»: el punto más bajo de la malla bajo la cadera (las nalgas), por
        // unidad de escala de la apariencia (1 = 1,81 m). DibujoMultitud sube o baja al sentado para que ese punto
        // quede a la altura de la silla.
        static float MedirAsiento(List<GameObject> prefabs, List<PerfilPersonaje> perfiles)
        {
            var valores = new List<float>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], Escena());
                try
                {
                    var an = go.GetComponent<Animator>();
                    var oc = an.runtimeAnimatorController as AnimatorOverrideController;
                    var clip = oc != null ? oc["Sentado"] : null;
                    if (clip == null) continue;
                    go.transform.position = Vector3.zero;
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, clip, clip.length * 0.5f);
                    AnimationMode.EndSampling();
                    var cadera = an.GetBoneTransform(HumanBodyBones.Hips).position;
                    float min = float.MaxValue;
                    var malla = new Mesh();
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        r.BakeMesh(malla, true);
                        var m = r.transform.localToWorldMatrix;
                        foreach (var v in malla.vertices)
                        {
                            var w = m.MultiplyPoint3x4(v);
                            float dx = w.x - cadera.x, dz = w.z - cadera.z;
                            if (dx * dx + dz * dz < 0.12f * 0.12f && w.y < cadera.y && w.y < min) min = w.y;
                        }
                    }
                    Object.DestroyImmediate(malla);
                    AnimationMode.StopAnimationMode();
                    if (min < float.MaxValue) valores.Add(min / (perfiles[i].altura / 1.81f));
                }
                finally
                {
                    if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                    Object.DestroyImmediate(go);
                }
            }
            if (valores.Count == 0) return 0.46f;
            valores.Sort();
            return valores[valores.Count / 2];
        }

        // Escena de previsualización para montar y medir sin tocar la escena abierta
        static UnityEngine.SceneManagement.Scene escena;
        static UnityEngine.SceneManagement.Scene Escena()
        {
            if (!escena.IsValid()) escena = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            return escena;
        }

        static void Carpetas(string ruta)
        {
            if (AssetDatabase.IsValidFolder(ruta)) return;
            var padre = Path.GetDirectoryName(ruta)!.Replace('\\', '/');
            Carpetas(padre);
            AssetDatabase.CreateFolder(padre, Path.GetFileName(ruta));
        }
    }
}
