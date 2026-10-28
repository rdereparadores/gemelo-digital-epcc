using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Materiales de la Escuela Politécnica (Assets/_Politecnica/Materiales/<nombre>.mat), definidos en los
// .json de esa carpeta: materiales.json (a mano) y materiales_interiores.json (lo genera
// Herramientas/politecnica/interiores.py con los colores de la especificación de interiores). Las texturas las prepara Herramientas/politecnica/preparar_texturas.py
// con la máscara empaquetada de URP (R metálico, G AO, B altura, A suavidad) y su tamaño real en
// Texturas/texturas.json; las UV de las mallas van en metros.
public static class MaterialesPolitecnica
{
    public const string Raiz = "Assets/_Politecnica";
    public const string Carpeta = Raiz + "/Materiales";
    const string Texturas = Raiz + "/Texturas";

    [System.Serializable]
    class Def
    {
        public string nombre, color = "#FFFFFF", textura, emision, imagen;   // imagen: textura de color sin PBR (rótulos)
        public bool recorte;                                                 // recorte por alfa
        public float metros, suavidad = -1f, metalico, alfa = 1f, intensidad = 1f;
    }

    [System.Serializable]
    class Lista { public Def[] materiales; }

    [MenuItem("Tools/Politécnica/Actualizar materiales")]
    public static void Build()
    {
        var metros = LeerMetros();
        int n = 0;
        foreach (var ruta in Directory.GetFiles(Carpeta, "*.json"))
        {
            var lista = JsonUtility.FromJson<Lista>(File.ReadAllText(ruta));
            if (lista?.materiales == null) continue;
            foreach (var d in lista.materiales) Crear(d, metros);
            n += lista.materiales.Length;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Politécnica] {n} materiales actualizados");
    }

    // texturas.json: {"nombre": [ancho, alto]} con el tamaño real que cubre cada textura (JsonUtility
    // no lee diccionarios, así que se extrae con una expresión regular)
    static Dictionary<string, Vector2> LeerMetros()
    {
        var r = new Dictionary<string, Vector2>();
        string ruta = $"{Texturas}/texturas.json";
        if (!File.Exists(ruta)) return r;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     File.ReadAllText(ruta), @"""(\w+)""\s*:\s*\[?\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*\])?"))
        {
            float a = float.Parse(m.Groups[2].Value, inv);
            float b = m.Groups[3].Success ? float.Parse(m.Groups[3].Value, inv) : a;
            r[m.Groups[1].Value] = new Vector2(a, b);
        }
        return r;
    }

    static void Crear(Def d, Dictionary<string, Vector2> metrosTex)
    {
        string path = $"{Carpeta}/{d.nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.enableInstancing = true;
        ColorUtility.TryParseHtmlString(d.color, out var color);
        color.a = d.alfa;
        mat.SetColor("_BaseColor", color);

        string tex = string.IsNullOrEmpty(d.textura) ? null : d.textura.Contains("/") ? d.textura : $"{Texturas}/{d.textura}";
        if (tex != null)
        {
            var m = d.metros > 0f ? new Vector2(d.metros, d.metros)
                  : metrosTex.TryGetValue(Path.GetFileName(tex), out var mt) ? mt : Vector2.one;
            var mask = Textura($"{tex}_mask.png", TextureImporterType.Default, false);
            mat.SetTexture("_BaseMap", Textura($"{tex}_diff.jpg", TextureImporterType.Default, true));
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetTexture("_BumpMap", Textura($"{tex}_normal.jpg", TextureImporterType.NormalMap, false));
            mat.SetFloat("_Smoothness", d.suavidad >= 0f ? d.suavidad : 1f);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.SetFloat("_BumpScale", 1f);
            mat.SetTextureScale("_BaseMap", new Vector2(1f / m.x, 1f / m.y));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_OCCLUSIONMAP");
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            foreach (var t in new[] { "_BaseMap", "_MetallicGlossMap", "_OcclusionMap", "_BumpMap" }) mat.SetTexture(t, null);
            if (!string.IsNullOrEmpty(d.imagen))
                mat.SetTexture("_BaseMap", Textura($"{Texturas}/{d.imagen}", TextureImporterType.Default, true, d.recorte));
            mat.SetFloat("_Smoothness", d.suavidad >= 0f ? d.suavidad : 0.3f);
            mat.SetFloat("_Metallic", d.metalico);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.DisableKeyword("_OCCLUSIONMAP");
            mat.DisableKeyword("_NORMALMAP");
        }

        if (!string.IsNullOrEmpty(d.emision) && ColorUtility.TryParseHtmlString(d.emision, out var em))
        {
            mat.SetColor("_EmissionColor", em * d.intensidad);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        Transparente(mat, d.alfa < 0.999f);
        mat.SetFloat("_AlphaClip", d.recorte ? 1f : 0f);
        mat.SetFloat("_Cutoff", 0.5f);
        if (d.recorte)
        {
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else mat.DisableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(mat);
    }

    // Vidrio: transparente con reflejos completos (premultiplicado, que conserva el especular)
    static void Transparente(Material mat, bool si)
    {
        mat.SetFloat("_Surface", si ? 1f : 0f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_BlendModePreserveSpecular", si ? 1f : 0f);
        mat.SetFloat("_SrcBlend", (float)(si ? BlendMode.One : BlendMode.One));
        mat.SetFloat("_DstBlend", (float)(si ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(si ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        mat.SetFloat("_ZWrite", si ? 0f : 1f);
        mat.SetFloat("_Cull", (float)(si ? CullMode.Off : CullMode.Back));
        if (si)
        {
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else
        {
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        mat.SetOverrideTag("RenderType", si ? "Transparent" : "Opaque");
        mat.renderQueue = (int)(si ? RenderQueue.Transparent : RenderQueue.Geometry);
        mat.SetShaderPassEnabled("DepthOnly", !si);
        mat.SetShaderPassEnabled("ShadowCaster", !si);
    }

    static Texture2D Textura(string path, TextureImporterType tipo, bool srgb, bool alfa = false)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) { Debug.LogError($"[Politécnica] No encuentro {path}"); return null; }
        if (imp.textureType != tipo || imp.sRGBTexture != srgb || imp.maxTextureSize != 2048 || imp.alphaIsTransparency != alfa)
        {
            imp.alphaIsTransparency = alfa;
            imp.textureType = tipo;
            imp.sRGBTexture = srgb;
            imp.maxTextureSize = 2048;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
