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

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Mallas);
        if (prefab == null) { Debug.LogError($"[Viales] Falta {Mallas}: ejecuta Herramientas/generar_viales.py"); return; }
        var entorno = Entorno.Raiz();
        var viejo = entorno.Find("Viales");
        if (viejo != null) Object.DestroyImmediate(viejo.gameObject);
        var viales = (GameObject)PrefabUtility.InstantiatePrefab(prefab, entorno);
        viales.name = "Viales";
        Physics.SyncTransforms();

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
}
