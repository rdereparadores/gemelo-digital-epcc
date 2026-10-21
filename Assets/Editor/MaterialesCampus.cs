using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
        public float Suavidad;    // solo si no hay textura PBR
        public float Metalico;    // solo si no hay textura PBR
        public string Imagen;     // imagen como color base (carteles, malla, seto)
        public bool Recorte;      // recorte por alfa y doble cara
        public bool Relieve;      // sin textura propia: usa el relieve del asfalto
    }

    const string Suelo = "Assets/Suelo/Texturas", Viales = "Assets/Viales/Texturas", Parque = "Assets/Parque/Texturas";
    const string Edif = "Assets/Edificios/Texturas";

    static readonly Def[] Defs =
    {
        new Def { Nombre = "Asfalto", Textura = "asphalt_04", Carpeta = Suelo, Metros = 4.04f, Color = Color.white },
        new Def { Nombre = "Acera", Textura = "concrete_tiles", Carpeta = Viales, Metros = 1.85f, Color = Color.white },
        new Def { Nombre = "Bordillo", Textura = "concrete_floor_worn_001", Carpeta = Viales, Metros = 3f, Color = Color.white },
        // Pintura vial: blanco algo sucio con el relieve del asfalto debajo
        new Def { Nombre = "Pintura", Carpeta = Suelo, Metros = 4.04f, Color = new Color(0.86f, 0.86f, 0.84f), Suavidad = 0.3f, Relieve = true },
        // Parque
        new Def { Nombre = "Madera", Textura = "weathered_brown_planks", Carpeta = Parque, Metros = 1.8f, Color = Color.white },
        new Def { Nombre = "Rollizo", Textura = "rough_wood", Carpeta = Parque, Metros = 1.0f, Color = Color.white },
        new Def { Nombre = "Mamposteria", Textura = "stacked_stone_wall", Carpeta = Parque, Metros = 2f, Color = Color.white },
        new Def { Nombre = "Granito", Textura = "rock_boulder_dry", Carpeta = Parque, Metros = 1.8f, Color = new Color(0.82f, 0.8f, 0.76f) },
        new Def { Nombre = "Cartel_Norte", Imagen = "Assets/Parque/Texturas/cartel_norte.jpg", Metros = 1f, Color = Color.white, Suavidad = 0.45f },
        new Def { Nombre = "Cartel_Sur", Imagen = "Assets/Parque/Texturas/cartel_sur.jpg", Metros = 1f, Color = Color.white, Suavidad = 0.45f },
        // Edificios
        new Def { Nombre = "Fachada_Blanca", Textura = "plastered_wall", Carpeta = Edif, Metros = 2f, Color = Color.white },
        new Def { Nombre = "Fachada_Ocre", Textura = "yellow_plaster", Carpeta = Edif, Metros = 2f, Color = Color.white },
        new Def { Nombre = "Fachada_Ladrillo", Textura = "red_brick", Carpeta = Edif, Metros = 1.4f, Color = Color.white },
        new Def { Nombre = "Cubierta", Textura = "concrete_pavement", Carpeta = Suelo, Metros = 1.8f, Color = new Color(0.85f, 0.85f, 0.85f) },
        new Def { Nombre = "Albardilla", Textura = "concrete_floor_worn_001", Carpeta = Viales, Metros = 3f, Color = Color.white },
        new Def { Nombre = "Hormigon", Textura = "concrete_floor_worn_001", Carpeta = Viales, Metros = 3f, Color = new Color(0.9f, 0.9f, 0.88f) },
        new Def { Nombre = "Vidrio", Color = new Color(0.13f, 0.16f, 0.19f), Suavidad = 0.92f, Metalico = 0.35f },
        new Def { Nombre = "Metal", Color = new Color(0.56f, 0.58f, 0.59f), Suavidad = 0.45f, Metalico = 0.75f },
        // Equipamiento
        new Def { Nombre = "Malla", Imagen = "Assets/Equipamiento/Texturas/malla.png", Metros = 0.5f, Color = Color.white, Suavidad = 0.4f, Recorte = true },
        new Def { Nombre = "Seto", Imagen = "Assets/Vegetacion/Texturas/Hojas_Arbusto.png", Metros = 0.6f, Color = Color.white, Suavidad = 0.1f, Recorte = true },
        new Def { Nombre = "Seto_Interior", Color = new Color(0.11f, 0.15f, 0.07f), Suavidad = 0.05f },
        new Def { Nombre = "Senal_Azul", Color = new Color(0.05f, 0.25f, 0.6f), Suavidad = 0.5f },
        new Def { Nombre = "Senal_Roja", Color = new Color(0.7f, 0.06f, 0.06f), Suavidad = 0.5f },
        new Def { Nombre = "Senal_Blanca", Color = new Color(0.9f, 0.9f, 0.9f), Suavidad = 0.5f },
        new Def { Nombre = "Pista_Verde", Metros = 4.04f, Color = new Color(0.22f, 0.42f, 0.28f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Pista_Azul", Metros = 4.04f, Color = new Color(0.16f, 0.32f, 0.55f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Pista_Roja", Metros = 4.04f, Color = new Color(0.55f, 0.24f, 0.2f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Tierra_Batida", Textura = "gravelly_sand", Carpeta = Suelo, Metros = 2.48f, Color = Color.white },
        new Def { Nombre = "Agua", Metros = 1f, Color = new Color(0.12f, 0.42f, 0.5f), Suavidad = 0.96f },
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
        float metros = d.Metros > 0f ? d.Metros : 1f;
        Recorte(mat, d.Recorte);

        if (d.Textura != null)
        {
            var diff = Textura($"{d.Carpeta}/{d.Textura}_diff.jpg", TextureImporterType.Default, true);
            var mask = Textura($"{d.Carpeta}/{d.Textura}_mask.png", TextureImporterType.Default, false);
            mat.SetTexture("_BaseMap", diff);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            mat.SetTexture("_BaseMap", d.Imagen != null ? Textura(d.Imagen, TextureImporterType.Default, true) : null);
            mat.SetTexture("_MetallicGlossMap", null);
            mat.SetTexture("_OcclusionMap", null);
            mat.SetFloat("_Smoothness", d.Suavidad);
            mat.SetFloat("_Metallic", d.Metalico);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.DisableKeyword("_OCCLUSIONMAP");
        }
        mat.SetTextureScale("_BaseMap", new Vector2(1f / metros, 1f / metros));

        // El relieve: el de la propia textura, o el del asfalto para la pintura y las pistas
        string normalTex = d.Textura ?? (d.Relieve ? "asphalt_04" : null);
        if (normalTex != null)
        {
            var carpeta = d.Textura != null ? d.Carpeta : Suelo;
            mat.SetTexture("_BumpMap", Textura($"{carpeta}/{normalTex}_normal.jpg", TextureImporterType.NormalMap, false));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(mat);
    }

    static void Recorte(Material mat, bool si)
    {
        mat.SetFloat("_AlphaClip", si ? 1f : 0f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Cull", (float)(si ? CullMode.Off : CullMode.Back));
        if (si) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
        mat.SetOverrideTag("RenderType", si ? "TransparentCutout" : "Opaque");
        mat.renderQueue = (int)(si ? RenderQueue.AlphaTest : RenderQueue.Geometry);
        mat.doubleSidedGI = si;
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
