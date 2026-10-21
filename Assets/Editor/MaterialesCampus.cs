using UnityEditor;
using UnityEngine;

// Biblioteca de materiales de las mallas generadas (Assets/Materiales/<nombre>.mat), que el
// importador de .mcam asigna por nombre. Las texturas PBR llevan la máscara empaquetada como
// R metálico, G AO, B altura, A suavidad, que encaja con los mapas de URP Lit.
public static class MaterialesCampus
{
    struct Def
    {
        public string Nombre, Textura, Carpeta;
        public float Metros;      // tamaño real de la textura (las UV de las mallas van en metros)
        public Color Color;
        public float Suavidad;    // solo si no hay textura
    }

    static readonly Def[] Defs =
    {
        new Def { Nombre = "Asfalto", Textura = "asphalt_04", Carpeta = "Assets/Suelo/Texturas", Metros = 4.04f, Color = Color.white },
        new Def { Nombre = "Acera", Textura = "concrete_tiles", Carpeta = "Assets/Viales/Texturas", Metros = 1.85f, Color = Color.white },
        new Def { Nombre = "Bordillo", Textura = "concrete_floor_worn_001", Carpeta = "Assets/Viales/Texturas", Metros = 3f, Color = Color.white },
        // Pintura vial: blanco algo sucio con el relieve del asfalto debajo
        new Def { Nombre = "Pintura", Textura = null, Carpeta = "Assets/Suelo/Texturas", Metros = 4.04f, Color = new Color(0.86f, 0.86f, 0.84f), Suavidad = 0.3f },
    };

    [MenuItem("Tools/Materiales/Actualizar materiales del campus")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(McamImporter.MaterialesDir)) AssetDatabase.CreateFolder("Assets", "Materiales");
        foreach (var d in Defs) Crear(d);
        AssetDatabase.SaveAssets();
    }

    static void Crear(Def d)
    {
        string path = $"{McamImporter.MaterialesDir}/{d.Nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.enableInstancing = true;
        mat.SetColor("_BaseColor", d.Color);
        var escala = new Vector2(1f / d.Metros, 1f / d.Metros);

        if (d.Textura != null)
        {
            var diff = Textura($"{d.Carpeta}/{d.Textura}_diff.jpg", TextureImporterType.Default, true);
            var mask = Textura($"{d.Carpeta}/{d.Textura}_mask.png", TextureImporterType.Default, false);
            mat.SetTexture("_BaseMap", diff);
            mat.SetTextureScale("_BaseMap", escala);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            mat.SetTexture("_BaseMap", null);
            mat.SetTexture("_MetallicGlossMap", null);
            mat.SetTexture("_OcclusionMap", null);
            mat.SetFloat("_Smoothness", d.Suavidad);
            mat.SetFloat("_Metallic", 0f);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.DisableKeyword("_OCCLUSIONMAP");
        }
        // El relieve: el de la propia textura, o el del asfalto para la pintura
        string normalTex = d.Textura ?? "asphalt_04";
        var normal = Textura($"{d.Carpeta}/{normalTex}_normal.jpg", TextureImporterType.NormalMap, false);
        mat.SetTexture("_BumpMap", normal);
        mat.SetTextureScale("_BaseMap", escala);
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);
    }

    static Texture2D Textura(string path, TextureImporterType tipo, bool srgb)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) { Debug.LogError($"[Materiales] No encuentro {path}"); return null; }
        if (imp.textureType != tipo || imp.sRGBTexture != srgb || imp.maxTextureSize != 2048)
        {
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
