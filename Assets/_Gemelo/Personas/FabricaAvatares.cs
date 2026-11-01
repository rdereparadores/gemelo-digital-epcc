// Crea el cuerpo de una persona (avatar con Animator humanoide) a partir de una semilla o de su aspecto. Es lo que
// queda registrado en GemeloApp.FabricaAvatar para que el módulo de control use el mismo cuerpo para el jugador.
using UnityEngine;

namespace Gemelo
{
    public static class FabricaAvatares
    {
        static MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Avatar con Animator (parámetros de ParamAvatar y Movil) para la semilla dada; null si no hay recursos.</summary>
        public static GameObject Crear(int semilla) => Crear(Apariencia.Generar(semilla), null);

        public static GameObject Crear(Apariencia ap, Transform padre)
        {
            var rec = RecursosPersonas.Cargar();
            if (rec == null || rec.avatares == null || rec.avatares.Length == 0) return null;
            var pf = rec.avatares[Mathf.Abs(ap.variante) % rec.avatares.Length];
            if (pf == null) return null;
            var go = Object.Instantiate(pf, padre);
            go.name = "Persona";
            go.transform.localScale = Vector3.one * ap.escala;
            var an = go.GetComponent<Animator>();
            if (an == null) an = go.AddComponent<Animator>();
            if (rec.controlador != null) an.runtimeAnimatorController = rec.controlador;
            an.applyRootMotion = false;
            if (rec.colorearAvatares) Colorear(go, ap);
            return go;
        }

        /// <summary>Tiñe el maniquí: su malla lleva en uv0 el texel de cada región (piel, top, mangas, bajo, zapatos,
        /// pelo, mochila) en una paleta de 8×1 propia de cada avatar.</summary>
        public static void Colorear(GameObject go, Apariencia ap)
        {
            mpb ??= new MaterialPropertyBlock();
            var paleta = go.GetComponent<PaletaAvatar>();
            if (paleta == null) paleta = go.AddComponent<PaletaAvatar>();
            paleta.Pintar(ap);
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                mpb.Clear();
                mpb.SetTexture(BaseMap, paleta.textura);
                mpb.SetColor(BaseColor, Color.white);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>El objeto del móvil que el maniquí lleva en la mano derecha (null en los personajes que no lo traen).</summary>
        public static GameObject Movil(GameObject avatar)
        {
            var an = avatar.GetComponent<Animator>();
            var mano = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (mano == null) return null;
            var t = mano.Find("Movil");
            if (t != null) return t.gameObject;
            // Personajes sin móvil (Mixamo): se les pone uno
            var rec = RecursosPersonas.Cargar();
            if (rec == null || rec.movil == null) return null;
            var go = new GameObject("Movil");
            go.transform.SetParent(mano, false);
            go.transform.localPosition = new Vector3(0.085f, 0.03f, 0.02f) / Mathf.Max(0.01f, mano.lossyScale.x);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(-70f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = rec.movil;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rec.materialMovil;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }
    }

    /// <summary>Paleta 8×1 de colores de un avatar (se destruye con él).</summary>
    public class PaletaAvatar : MonoBehaviour
    {
        public Texture2D textura;
        readonly Color32[] px = new Color32[8];

        public void Pintar(Apariencia ap)
        {
            if (textura == null)
                textura = new Texture2D(8, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Paleta" };
            for (int i = 0; i < 7; i++) px[i] = ap.Region(i);
            px[7] = ap.piel;
            textura.SetPixels32(px);
            textura.Apply(false);
        }

        void OnDestroy()
        {
            if (textura != null) Destroy(textura);
        }
    }
}
