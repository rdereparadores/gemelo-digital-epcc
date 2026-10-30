using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Biblioteca de materiales de las mallas generadas (Assets/_Ambientacion/Materiales/<nombre>.mat), que el
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
        public bool Transparente; // mezcla alfa con el alfa del color (vidrios)
    }

    const string Suelo = "Assets/_Ambientacion/Suelo/Texturas", Viales = "Assets/_Ambientacion/Viales/Texturas", Parque = "Assets/_Ambientacion/Parque/Texturas";
    const string Edif = "Assets/_Ambientacion/Edificios/Texturas";

    static readonly Def[] Defs =
    {
        new Def { Nombre = "Asfalto", Textura = "asphalt_04", Carpeta = Suelo, Metros = 4.04f, Color = Color.white },
        new Def { Nombre = "Acera", Textura = "concrete_tiles", Carpeta = Viales, Metros = 1.85f, Color = Color.white },
        new Def { Nombre = "Bordillo", Textura = "concrete_floor_worn_001", Carpeta = Viales, Metros = 3f, Color = Color.white },
        // Pintura vial: blanco algo sucio con el relieve del asfalto debajo
        new Def { Nombre = "Pintura", Carpeta = Suelo, Metros = 4.04f, Color = new Color(0.86f, 0.86f, 0.84f), Suavidad = 0.3f, Relieve = true },
        // Plazas PMR (azul) y zigzag de parada (amarillo)
        new Def { Nombre = "Pintura_Azul", Carpeta = Suelo, Metros = 4.04f, Color = new Color(0.10f, 0.32f, 0.62f), Suavidad = 0.3f, Relieve = true },
        new Def { Nombre = "Pintura_Amarilla", Carpeta = Suelo, Metros = 4.04f, Color = new Color(0.86f, 0.70f, 0.12f), Suavidad = 0.3f, Relieve = true },
        // Parque
        new Def { Nombre = "Madera", Textura = "weathered_brown_planks", Carpeta = Parque, Metros = 1.8f, Color = Color.white },
        new Def { Nombre = "Rollizo", Textura = "rough_wood", Carpeta = Parque, Metros = 1.0f, Color = Color.white },
        new Def { Nombre = "Mamposteria", Textura = "stacked_stone_wall", Carpeta = Parque, Metros = 2f, Color = Color.white },
        new Def { Nombre = "Granito", Textura = "rock_boulder_dry", Carpeta = Parque, Metros = 1.8f, Color = new Color(0.82f, 0.8f, 0.76f) },
        new Def { Nombre = "Cartel_Norte", Imagen = "Assets/_Ambientacion/Parque/Texturas/cartel_norte.jpg", Metros = 1f, Color = Color.white, Suavidad = 0.45f },
        new Def { Nombre = "Cartel_Sur", Imagen = "Assets/_Ambientacion/Parque/Texturas/cartel_sur.jpg", Metros = 1f, Color = Color.white, Suavidad = 0.45f },
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
        new Def { Nombre = "Malla", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/malla.png", Metros = 0.5f, Color = Color.white, Suavidad = 0.4f, Recorte = true },
        new Def { Nombre = "Seto", Imagen = "Assets/_Ambientacion/Vegetacion/Texturas/Hojas_Arbusto.png", Metros = 0.6f, Color = Color.white, Suavidad = 0.1f, Recorte = true },
        new Def { Nombre = "Seto_Interior", Color = new Color(0.11f, 0.15f, 0.07f), Suavidad = 0.05f },
        new Def { Nombre = "Senal_Blanca", Color = new Color(0.9f, 0.9f, 0.9f), Suavidad = 0.5f },
        // Placas con dibujo (Herramientas/generar_equipamiento.py las pinta en Equipamiento/Texturas)
        new Def { Nombre = "Senal_R301_40", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_R301_40.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f, Recorte = true },
        new Def { Nombre = "Senal_R301_50", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_R301_50.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f, Recorte = true },
        new Def { Nombre = "Senal_R1", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_R1.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f, Recorte = true },
        new Def { Nombre = "Senal_S13", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_S13.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f, Recorte = true },
        // Vidrio de las marquesinas: ligeramente tintado y translúcido (fotos de las paradas, 2025)
        new Def { Nombre = "Vidrio_Marquesina", Color = new Color(0.62f, 0.72f, 0.72f, 0.28f), Suavidad = 0.92f, Transparente = true },
        new Def { Nombre = "Senal_STOP", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_STOP.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f, Recorte = true },
        new Def { Nombre = "Senal_Parada", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/senal_parada.png", Metros = 1f, Color = Color.white, Suavidad = 0.5f },
        new Def { Nombre = "Mupi", Imagen = "Assets/_Ambientacion/Equipamiento/Texturas/mupi.png", Metros = 1f, Color = Color.white, Suavidad = 0.85f },
        new Def { Nombre = "Granate", Color = new Color(0.42f, 0.10f, 0.10f), Suavidad = 0.45f, Metalico = 0.3f },
        new Def { Nombre = "Pista_Verde", Metros = 4.04f, Color = new Color(0.22f, 0.42f, 0.28f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Pista_Azul", Metros = 4.04f, Color = new Color(0.16f, 0.32f, 0.55f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Pista_Roja", Metros = 4.04f, Color = new Color(0.55f, 0.24f, 0.2f), Suavidad = 0.25f, Relieve = true },
        new Def { Nombre = "Tierra_Batida", Textura = "gravelly_sand", Carpeta = Suelo, Metros = 2.48f, Color = Color.white },
        new Def { Nombre = "Agua", Metros = 1f, Color = new Color(0.12f, 0.42f, 0.5f), Suavidad = 0.96f },
        // Campo lejano alrededor del Terrain
        new Def { Nombre = "Campo", Textura = "withered_grass", Carpeta = Suelo, Metros = 8f, Color = new Color(0.95f, 0.9f, 0.85f) },
    };

    [MenuItem("Tools/Materiales/Actualizar materiales del campus")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(McamImporter.MaterialesDir)) AssetDatabase.CreateFolder("Assets/_Ambientacion", "Materiales");
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
        Transparencia(mat, d.Transparente);

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

    static void Transparencia(Material mat, bool si)
    {
        if (!si) { mat.SetFloat("_Surface", 0f); mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); return; }
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
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
