using System.IO;
using UnityEditor;
using UnityEngine;

// Sustituye la ortofoto del Terrain por 8 capas de material PBR (texturas CC0 de Poly Haven)
// repartidas con el splatmap que genera Herramientas/generar_splatmap.py.
public static class SueloBuilder
{
    const string TexDir = "Assets/_Ambientacion/Suelo/Texturas";
    const string CapasDir = "Assets/_Ambientacion/Suelo/Capas";
    const string Material = "Assets/_Ambientacion/Suelo/Terreno.mat";
    const string Splat03 = "Datos/Suelo/splat_0_3.png";
    const string Splat47 = "Datos/Suelo/splat_4_7.png";

    // Mismo orden que CAPAS en generar_splatmap.py. Tamaño de tesela = medida real de la textura.
    static readonly (string nombre, string textura, float metros)[] Capas =
    {
        ("Pasto_Seco", "withered_grass", 2.0f),
        ("Tierra", "dry_ground_rocks", 4.0f),
        ("Jabre", "gravelly_sand", 2.48f),
        ("Hojarasca", "dry_decay_leaves", 2.0f),
        ("Cesped", "leafy_grass", 2.0f),
        ("Asfalto", "asphalt_04", 4.04f),
        ("Pavimento", "concrete_pavement", 1.8f),
        ("Pedregal", "rocky_trail", 2.0f),
    };

    [MenuItem("Tools/Suelo/Aplicar capas y splatmap al Terrain")]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[Suelo] No hay Terrain activo"); return; }
        if (!File.Exists(Splat03) || !File.Exists(Splat47))
        {
            Debug.LogError("[Suelo] Falta el splatmap: ejecuta /usr/bin/python3.14 Herramientas/generar_splatmap.py");
            return;
        }
        if (!AssetDatabase.IsValidFolder(CapasDir)) AssetDatabase.CreateFolder("Assets/_Ambientacion/Suelo", "Capas");

        var layers = new TerrainLayer[Capas.Length];
        for (int i = 0; i < Capas.Length; i++)
            layers[i] = CrearCapa(Capas[i].nombre, Capas[i].textura, Capas[i].metros);

        var data = terrain.terrainData;
        data.terrainLayers = layers;
        data.alphamapResolution = 2048;
        data.baseMapResolution = 2048;
        data.SetAlphamaps(0, 0, LeerSplat(data.alphamapResolution));

        terrain.materialTemplate = MaterialTerreno();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 2f;      // más fiel al MDT de cerca
        terrain.basemapDistance = 400f;

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(terrain.gameObject.scene);
        Debug.Log("[Suelo] Capas y splatmap aplicados");
    }

    static TerrainLayer CrearCapa(string nombre, string textura, float metros)
    {
        var diff = Importar($"{TexDir}/{textura}_diff.jpg", TextureImporterType.Default, true);
        var normal = Importar($"{TexDir}/{textura}_normal.jpg", TextureImporterType.NormalMap, false);
        var mask = Importar($"{TexDir}/{textura}_mask.png", TextureImporterType.Default, false);

        string path = $"{CapasDir}/{nombre}.terrainlayer";
        var capa = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (capa == null)
        {
            capa = new TerrainLayer();
            AssetDatabase.CreateAsset(capa, path);
        }
        capa.diffuseTexture = diff;
        capa.normalMapTexture = normal;
        capa.maskMapTexture = mask;
        capa.normalScale = 1f;
        capa.tileSize = new Vector2(metros, metros);
        capa.tileOffset = Vector2.zero;
        EditorUtility.SetDirty(capa);
        return capa;
    }

    static Texture2D Importar(string path, TextureImporterType tipo, bool srgb)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) { Debug.LogError($"[Suelo] No encuentro {path}"); return null; }
        if (imp.textureType != tipo || imp.sRGBTexture != srgb || imp.maxTextureSize != 2048 || !imp.mipmapEnabled)
        {
            imp.textureType = tipo;
            imp.sRGBTexture = srgb;
            imp.maxTextureSize = 2048;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Los PNG van con el norte arriba; LoadImage los carga con la fila 0 abajo (sur), que es
    // justo el orden [z, x] del alphamap.
    static float[,,] LeerSplat(int res)
    {
        var a = Cargar(Splat03, res);
        var b = Cargar(Splat47, res);
        var map = new float[res, res, 8];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            var p = a[y * res + x];
            var q = b[y * res + x];
            float[] w = { p.r, p.g, p.b, p.a, q.r, q.g, q.b, q.a };
            float suma = 0f;
            foreach (var v in w) suma += v;
            if (suma <= 0f) { w[0] = 1f; suma = 1f; }
            for (int k = 0; k < 8; k++) map[y, x, k] = w[k] / suma;
        }
        return map;
    }

    static Color32[] Cargar(string path, int res)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        tex.LoadImage(File.ReadAllBytes(path));
        if (tex.width != res || tex.height != res)
            Debug.LogError($"[Suelo] {path} mide {tex.width}x{tex.height}, se esperaba {res}");
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return px;
    }

    static Material MaterialTerreno()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Material);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            AssetDatabase.CreateAsset(mat, Material);
        }
        // Las capas llevan normal y máscara (metálico, AO, altura, suavidad)
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_MASKMAP");
        mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
