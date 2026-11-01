// Galería de los avatares cercanos (para revisar materiales, escala y poses sin entrar en Play Mode): pone en fila
// los prefabs de RecursosPersonas (NPC y jugadores) en una escena de previsualización, con el estado del Animator
// que se pida (Locomocion, Sentado, SentadoMovil, MovilDePie, HablarDePie…) y la renderiza a un PNG.
//   Gemelo.GaleriaPersonajes.Render("/ruta/galeria.png", "Sentado", 1.5f)
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gemelo
{
    public static class GaleriaPersonajes
    {
        /// <summary>Para ajustar el móvil en la mano: giro y posición (local del hueso, escala 1) en lugar de los del prefab.</summary>
        public static Vector3? PruebaGiroMovil, PruebaPosMovil;
        /// <summary>Renderiza la fila de avatares con el clip dado (nombre del clip del maniquí: Reposo, Andar, Sentado,
        /// SentadoMovil, SentadoEscribiendo, MovilDePie…; cada uno con el suyo de Mixamo) en el segundo t.
        /// <paramref name="detalle"/> &gt; 0 acerca la cámara a ese avatar (índice en la fila).</summary>
        public static string Render(string ruta, string clip = "Reposo", float t = 1.5f, int detalle = -1, int ancho = 2400, int alto = 900)
        {
            var rec = RecursosPersonas.Cargar();
            var lista = new List<GameObject>();
            if (rec.avatares != null) lista.AddRange(rec.avatares);
            if (rec.jugadores != null) lista.AddRange(rec.jugadores);
            var escena = EditorSceneManager.NewPreviewScene();
            var objs = new List<GameObject>();
            var muestras = new List<(GameObject, AnimationClip)>();
            try
            {
                var luz = new GameObject("Sol").AddComponent<Light>();
                luz.type = LightType.Directional;
                luz.intensity = 1.6f;
                luz.shadows = LightShadows.Soft;
                luz.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
                objs.Add(luz.gameObject);
                var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
                suelo.transform.localScale = new Vector3(6f, 1f, 2f);
                suelo.transform.position = new Vector3(lista.Count * 0.45f, 0f, 0f);
                objs.Add(suelo);
                for (int i = 0; i < lista.Count; i++)
                {
                    if (lista[i] == null) continue;
                    var go = Object.Instantiate(lista[i]);
                    objs.Add(go);
                    go.transform.position = new Vector3(i * 0.9f, 0f, 0f);
                    go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    var mov = go.transform.Find("Movil") ?? BuscarMovil(go.transform);
                    if (mov != null) mov.gameObject.SetActive(clip == "MovilDePie");
                    if (mov != null && PruebaGiroMovil != null)
                    {
                        float k = mov.localScale.x;
                        mov.localRotation = Quaternion.Euler(PruebaGiroMovil.Value);
                        mov.localPosition = PruebaPosMovil.Value * k;
                    }
                    var an = go.GetComponent<Animator>();
                    var oc = an != null ? an.runtimeAnimatorController as AnimatorOverrideController : null;
                    var c = oc != null ? oc[clip] : null;
                    if (c == null && an != null && an.runtimeAnimatorController != null)
                        foreach (var x in an.runtimeAnimatorController.animationClips) if (x.name == clip) c = x;
                    if (c != null) muestras.Add((go, c));
                }
                var camGo = new GameObject("Camara");
                objs.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.62f, 0.7f);
                cam.fieldOfView = 30f;
                float largo = lista.Count * 0.9f;
                if (detalle >= 0)
                {
                    cam.transform.position = new Vector3(detalle * 0.9f + 0.3f, 1.3f, -2.6f);
                    cam.transform.LookAt(new Vector3(detalle * 0.9f, clip.StartsWith("Sentado") ? 0.8f : 1.05f, 0f));
                }
                else
                {
                    cam.transform.position = new Vector3(largo * 0.5f - 0.45f, 1.0f, -largo * 0.62f);
                    cam.transform.LookAt(new Vector3(largo * 0.5f - 0.45f, 0.95f, 0f));
                }
                foreach (var o in objs) EditorSceneManager.MoveGameObjectToScene(o, escena);
                cam.scene = escena;
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                foreach (var (go, c) in muestras) AnimationMode.SampleAnimationClip(go, c, c.length > 0 ? t % c.length : 0f);
                AnimationMode.EndSampling();
                var rt = new RenderTexture(ancho, alto, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(ruta, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                return ruta;
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                foreach (var o in objs) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(escena);
            }
        }

        static Transform BuscarMovil(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == "Movil") return c;
            return null;
        }
    }
}
