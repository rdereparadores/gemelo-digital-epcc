// Capturas de comprobación del módulo Ambiente en Play Mode: una cámara temporal con el postprocesado de la
// principal que renderiza en el ciclo normal (así salen también los dibujos instanciados) y guarda un PNG.
// Uso (unity command eval): Gemelo.CapturaPrueba.Tomar(pos, mira, "/ruta.png"); y esperar a que exista el archivo.
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gemelo
{
    public class CapturaPrueba : MonoBehaviour
    {
        string ruta;
        Camera cam;
        RenderTexture rt;
        int espera = 2;

        public static void Tomar(Vector3 pos, Vector3 mira, string ruta, float fov = 60f, int ancho = 1280, int alto = 720)
        {
            var go = new GameObject("CapturaPrueba");
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(mira - pos));
            var c = go.AddComponent<Camera>();
            var principal = Camera.main;
            if (principal != null) c.CopyFrom(principal);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(mira - pos));
            c.fieldOfView = fov;
            c.nearClipPlane = 0.1f;
            c.farClipPlane = 2000f;
            c.depth = -100;
            var datos = c.GetUniversalAdditionalCameraData();
            datos.renderPostProcessing = true;
            datos.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var cp = go.AddComponent<CapturaPrueba>();
            cp.cam = c;
            cp.ruta = ruta;
            cp.rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            c.targetTexture = cp.rt;
            RenderPipelineManager.endCameraRendering += cp.Fin;
        }

        void Fin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || --espera > 0) return;
            RenderPipelineManager.endCameraRendering -= Fin;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Destroy(tex);
            cam.targetTexture = null;
            rt.Release();
            Destroy(gameObject);
        }

        void OnDestroy() => RenderPipelineManager.endCameraRendering -= Fin;
    }
}
