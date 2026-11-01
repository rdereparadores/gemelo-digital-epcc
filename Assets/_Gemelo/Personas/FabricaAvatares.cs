// Crea el cuerpo de una persona (avatar con Animator humanoide) a partir de una semilla o de su aspecto. Es lo que
// queda registrado en GemeloApp.FabricaAvatar para que el módulo de control use el mismo cuerpo para el jugador.
using UnityEngine;

namespace Gemelo
{
    public static class FabricaAvatares
    {
        /// <summary>Solo para medir (A/B): si no es null, los NPC cercanos usan estos prefabs (los maniquíes) teñidos
        /// por variante en lugar de los de RecursosPersonas. DibujoMultitud rehace los avatares al cambiarlo.</summary>
        public static GameObject[] PruebaAvatares;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Avatar con Animator (parámetros de ParamAvatar y Movil) para la semilla dada; null si no hay recursos.</summary>
        public static GameObject Crear(int semilla) => Crear(Apariencia.Generar(semilla), null);

        public static GameObject Crear(Apariencia ap, Transform padre)
        {
            var rec = RecursosPersonas.Cargar();
            if (rec == null || rec.avatares == null || rec.avatares.Length == 0) return null;
            if (PruebaAvatares != null && PruebaAvatares.Length > 0)
            {
                // Medidas A/B: los maniquíes en lugar de los personajes, teñidos con la apariencia
                var m = Instanciar(PruebaAvatares[Mathf.Abs(ap.variante) % PruebaAvatares.Length], padre, rec);
                m.name = "Persona";
                m.transform.localScale = Vector3.one * ap.escala;
                Colorear(m, ap);
                return m;
            }
            int i = Cuerpo(ap, rec);
            var pf = rec.avatares[i];
            if (pf == null) return null;
            var go = Instanciar(pf, padre, rec);
            go.name = "Persona";
            go.transform.localScale = Vector3.one * Escala(ap, rec);
            if (rec.colorearAvatares) Colorear(go, ap);
            return go;
        }

        /// <summary>Índice del prefab de RecursosPersonas.avatares que lleva esta apariencia.</summary>
        public static int Cuerpo(Apariencia ap, RecursosPersonas rec = null)
        {
            if (PruebaAvatares != null && PruebaAvatares.Length > 0) return 1000 + Mathf.Abs(ap.variante) % PruebaAvatares.Length;
            rec ??= RecursosPersonas.Cargar();
            int n = rec != null && rec.avatares != null ? rec.avatares.Length : 0;
            if (n == 0) return 0;
            return ap.personaje >= 0 ? ap.personaje % n : Mathf.Abs(ap.variante) % n;
        }

        /// <summary>Escala del avatar cercano: la estatura de la apariencia (1,81 m · escala) sobre la altura del modelo.</summary>
        public static float Escala(Apariencia ap, RecursosPersonas rec = null)
        {
            if (PruebaAvatares != null && PruebaAvatares.Length > 0) return ap.escala;
            rec ??= RecursosPersonas.Cargar();
            var p = rec != null ? rec.Perfil(Cuerpo(ap, rec)) : null;
            return p != null && p.altura > 0.5f ? ap.escala * 1.81f / p.altura : ap.escala;
        }

        /// <summary>Cuerpo del jugador (0 = Brian, 1 = Megan) a su escala natural; si no hay, uno de los NPC.</summary>
        public static GameObject CrearJugador(int cual)
        {
            var rec = RecursosPersonas.Cargar();
            if (rec == null || rec.jugadores == null || rec.jugadores.Length == 0) return Crear(7);
            var pf = rec.jugadores[Mathf.Clamp(cual, 0, rec.jugadores.Length - 1)];
            if (pf == null) return Crear(7);
            var go = Instanciar(pf, null, rec);
            go.name = "Jugador_" + pf.name;
            return go;
        }

        static GameObject Instanciar(GameObject pf, Transform padre, RecursosPersonas rec)
        {
            var go = Object.Instantiate(pf, padre);
            var an = go.GetComponent<Animator>();
            if (an == null) an = go.AddComponent<Animator>();
            // Los personajes de Mixamo traen su controlador (masculino o femenino); los maniquíes, el común
            if (an.runtimeAnimatorController == null && rec.controlador != null) an.runtimeAnimatorController = rec.controlador;
            an.applyRootMotion = false;
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
