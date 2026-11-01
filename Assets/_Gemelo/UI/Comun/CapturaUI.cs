// Capturas de comprobación con la interfaz: la cámara principal en una RenderTexture y, encima, el panel de la UI
// (que se dibuja un momento en otra RenderTexture). No depende del foco de la ventana de juego del editor.
// Uso (unity command eval): Gemelo.CapturaUI.Tomar("/ruta.png", 1920, 1080); y esperar a que exista el archivo.
using System.Collections;
using System.IO;
using UnityEngine;

namespace Gemelo
{
    public class CapturaUI : MonoBehaviour
    {
        public static void Tomar(string ruta, int ancho = 1920, int alto = 1080)
        {
            var go = new GameObject("CapturaUI");
            go.AddComponent<CapturaUI>().StartCoroutine(go.GetComponent<CapturaUI>().Capturar(ruta, ancho, alto));
        }

        IEnumerator Capturar(string ruta, int w, int h)
        {
            var ui = GemeloUI.Instancia;
            var ps = ui != null ? ui.AjustesPanel : null;
            var rtUI = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var rtCam = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            // La propia cámara principal (con lo que le añadan los módulos), dibujando un momento en la textura
            var principal = Camera.main;
            RenderTexture antes = null;
            if (principal != null) { antes = principal.targetTexture; principal.targetTexture = rtCam; }
            bool clear = false;
            if (ps != null)
            {
                clear = ps.clearColor;
                ps.targetTexture = rtUI;
                ps.clearColor = true;
                ps.colorClearValue = new Color(0, 0, 0, 0);
            }
            for (int i = 0; i < 4; i++) yield return new WaitForEndOfFrame();
            var cam = Leer(rtCam, w, h);
            var capa = Leer(rtUI, w, h);
            var px = cam.GetPixels32();
            var up = capa.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                // El panel sale premultiplicado sobre transparente
                float a = up[i].a / 255f;
                px[i] = new Color32(
                    (byte)Mathf.Min(255, up[i].r + px[i].r * (1 - a)),
                    (byte)Mathf.Min(255, up[i].g + px[i].g * (1 - a)),
                    (byte)Mathf.Min(255, up[i].b + px[i].b * (1 - a)), 255);
            }
            cam.SetPixels32(px);
            cam.Apply();
            File.WriteAllBytes(ruta, cam.EncodeToPNG());
            if (ps != null) { ps.targetTexture = null; ps.clearColor = clear; }
            if (principal != null) principal.targetTexture = antes;
            Destroy(cam); Destroy(capa);
            rtUI.Release(); rtCam.Release();
            Destroy(gameObject);
        }

        static Texture2D Leer(RenderTexture rt, int w, int h)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }
    }
}
