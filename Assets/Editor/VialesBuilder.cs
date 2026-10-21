using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Coloca en la escena la red viaria que genera Herramientas/generar_viales.py: aplica al Terrain
// el heightmap con el rebaje bajo los viales, instancia Assets/Viales/viales.mcam bajo
// Entorno/Viales y vuelve a asentar el parking norte sobre el asfalto.
public static class VialesBuilder
{
    const string Mallas = "Assets/Viales/viales.mcam";
    const string Heightmap = "Datos/Viales/heightmap_viales.raw";

    [MenuItem("Tools/Viales/Aplicar red viaria")]
    public static void Build()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[Viales] No hay Terrain activo"); return; }

        MaterialesCampus.Build();
        AplicarHeightmap(terrain.terrainData, Heightmap);

        if (Entorno.Colocar(Mallas, "Viales") == null) return;

        ParkingNorteBuilder.Build();

        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        EditorSceneManager.SaveScene(terrain.gameObject.scene);
        Debug.Log("[Viales] Red viaria aplicada");
    }

    public static void AplicarHeightmap(TerrainData data, string ruta)
    {
        int n = data.heightmapResolution;
        var bytes = File.ReadAllBytes(ruta);
        if (bytes.Length != n * n * 2) { Debug.LogError($"[Viales] {ruta} no mide {n}x{n}"); return; }
        var h = new float[n, n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = (z * n + x) * 2;
            h[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
        }
        data.SetHeights(0, 0, h);
        data.SyncHeightmap();
        EditorUtility.SetDirty(data);
    }
}

// Raíz común de todo lo generado del entorno en la escena
public static class Entorno
{
    public static Transform Raiz()
    {
        var go = GameObject.Find("Entorno");
        if (go == null) go = new GameObject("Entorno");
        return go.transform;
    }

    // Instancia un .mcam importado bajo Entorno/<nombre>, sustituyendo el anterior
    public static GameObject Colocar(string mcam, string nombre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(mcam);
        if (prefab == null) { Debug.LogError($"[Entorno] Falta {mcam}: ejecuta su generador en Herramientas/"); return null; }
        var raiz = Raiz();
        var viejo = raiz.Find(nombre);
        if (viejo != null) Object.DestroyImmediate(viejo.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz);
        go.name = nombre;
        Physics.SyncTransforms();
        return go;
    }

    public static void Guardar()
    {
        var escena = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        AssetDatabase.SaveAssets();
    }
}

public static class ParqueBuilder
{
    [MenuItem("Tools/Parque/Colocar parque y cauce")]
    public static void Build()
    {
        MaterialesCampus.Build();
        if (Entorno.Colocar("Assets/Parque/parque.mcam", "Parque") == null) return;
        Entorno.Guardar();
        Debug.Log("[Parque] Parque colocado");
    }
}
