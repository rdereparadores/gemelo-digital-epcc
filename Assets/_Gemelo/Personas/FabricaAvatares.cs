// Crea el cuerpo de una persona (avatar con Animator humanoide) a partir de una semilla o de su aspecto. Es lo que
// queda registrado en GemeloApp.FabricaAvatar para que el módulo de control use el mismo cuerpo para el jugador.
using UnityEngine;

namespace Gemelo
{
    public static class FabricaAvatares
    {
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
            var mm = MaterialMovil(rec);
            if (mm != null)
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial == rec.materialMovil) mr.sharedMaterial = mm;
            return go;
        }

        // Copia en ejecución del material del móvil con la emisión (la pantalla) encendida: la validación de URP le
        // quita _EMISSION al asset cada vez que lo reimporta, así que no se confía en la palabra clave guardada
        static Material movilRt;
        static Material MaterialMovil(RecursosPersonas rec)
        {
            if (rec.materialMovil == null) return null;
            if (movilRt == null)
            {
                movilRt = new Material(rec.materialMovil) { name = rec.materialMovil.name + " (pantalla)", hideFlags = HideFlags.DontSave };
                movilRt.EnableKeyword("_EMISSION");
                movilRt.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return movilRt;
        }

        /// <summary>Tiñe el maniquí: su malla lleva en uv0 el texel de cada región (piel, top, mangas, bajo, zapatos,
        /// pelo, mochila) en una paleta de 8×1 propia de cada avatar.</summary>
        public static void Colorear(GameObject go, Apariencia ap)
        {
            var paleta = go.GetComponent<PaletaAvatar>();
            if (paleta == null) paleta = go.AddComponent<PaletaAvatar>();
            paleta.Pintar(ap);
            if (paleta.materiales.Count > 0) return;      // (ya tiene sus materiales con la paleta)
            // Un material propio por avatar con su paleta, en vez de un MaterialPropertyBlock: con bloques de propiedades
            // el SRP Batcher no agrupa y cada avatar era un cambio de estado en cada pasada (color, profundidad, normales
            // del SSAO y sombras); así comparten sombreador y solo cambia la textura
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var src = r.sharedMaterials;
                var inst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    if (src[i] == null) continue;
                    inst[i] = new Material(src[i]) { name = src[i].name + " (avatar)" };
                    inst[i].SetTexture(BaseMap, paleta.textura);
                    inst[i].SetColor(BaseColor, Color.white);
                    paleta.materiales.Add(inst[i]);
                }
                r.SetPropertyBlock(null);
                r.sharedMaterials = inst;
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
            mr.sharedMaterial = MaterialMovil(rec);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }
    }

    /// <summary>Paleta 8×1 de colores de un avatar y sus materiales (se destruyen con él).</summary>
    public class PaletaAvatar : MonoBehaviour
    {
        public Texture2D textura;
        public readonly System.Collections.Generic.List<Material> materiales = new();
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
            foreach (var m in materiales) if (m != null) Destroy(m);
        }
    }
}
