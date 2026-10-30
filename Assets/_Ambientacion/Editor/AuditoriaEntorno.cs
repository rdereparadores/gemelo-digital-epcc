using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Exporta los triángulos de todo el entorno (todo menos los edificios de la Politécnica, que tienen su propia
// auditoría) en coordenadas del mundo, para que Herramientas/auditar_entorno.py busque caras coplanarias que
// se solapan (z-fighting) y piezas metidas unas en otras. Una malla por objeto y material.
// Formato: int n; por malla: string nombre (int bytes + UTF-8), string material, int triángulos, float[9·t].
public static class AuditoriaEntorno
{
    public const string Salida = "Datos/entorno_triangulos.bin";

    [MenuItem("Tools/Auditoría/Exportar triángulos del entorno")]
    public static string Exportar()
    {
        int mallas = 0, tris = 0;
        using (var w = new BinaryWriter(File.Create(Salida)))
        {
            w.Write(0);
            foreach (var raiz in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (raiz.name == "Politecnica_Edificios" || !raiz.activeInHierarchy) continue;
                foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    var mesh = mf.sharedMesh;
                    if (mr == null || !mr.enabled || mesh == null) continue;
                    var v = mesh.vertices;
                    var m = mf.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var t = mesh.GetTriangles(s);
                        if (t.Length == 0) continue;
                        var mat = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null ? mr.sharedMaterials[s].name : "?";
                        Cadena(w, Ruta(mf.transform));
                        Cadena(w, mat);
                        w.Write(t.Length / 3);
                        foreach (int k in t) { w.Write(v[k].x); w.Write(v[k].y); w.Write(v[k].z); }
                        mallas++;
                        tris += t.Length / 3;
                    }
                }
            }
            w.Seek(0, SeekOrigin.Begin);
            w.Write(mallas);
        }
        return $"{mallas} mallas, {tris} triángulos -> {Salida}";
    }

    public const string SalidaBarrido = "Datos/entorno_barrido.bin";

    // Barrido cenital de todo el Terrain cada `paso` m: por celda, cota del Terrain, cota de lo primero que se
    // ve desde arriba (sin vegetación del Terrain) y qué es (índice en Datos/entorno_barrido.txt; −1 nada).
    // Formato: int ancho, int alto, float paso, y float[3] por celda (fila a fila, de sur a norte).
    [MenuItem("Tools/Auditoría/Barrido cenital del terreno")]
    public static string Barrido() => Barrido(0.5f);

    public static string Barrido(float paso)
    {
        var t = Terrain.activeTerrain;
        var size = t.terrainData.size;
        int W = Mathf.FloorToInt(size.x / paso), H = Mathf.FloorToInt(size.z / paso);
        var nombres = new System.Collections.Generic.Dictionary<string, int>();
        var lista = new System.Collections.Generic.List<string>();
        var golpes = new RaycastHit[8];
        using (var w = new BinaryWriter(File.Create(SalidaBarrido)))
        {
            w.Write(W); w.Write(H); w.Write(paso);
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    var p = new Vector3(t.transform.position.x + (i + 0.5f) * paso, 200f, t.transform.position.z + (j + 0.5f) * paso);
                    float th = t.SampleHeight(p) + t.transform.position.y;
                    float hy = -999f; int code = -1;
                    if (Physics.Raycast(p, Vector3.down, out var h, 400f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        hy = h.point.y;
                        string clave;
                        if (h.collider is TerrainCollider) clave = "Terrain";
                        else
                        {
                            var tr = h.collider.transform;
                            var raiz = tr; while (raiz.parent != null && raiz.parent.parent != null) raiz = raiz.parent;
                            clave = (raiz != tr ? raiz.name + "/" : "") + (tr.parent != null ? tr.parent.name + "/" : "") + tr.name;
                        }
                        if (!nombres.TryGetValue(clave, out code)) { code = lista.Count; nombres[clave] = code; lista.Add(clave); }
                    }
                    w.Write(th); w.Write(hy); w.Write((float)code);
                }
        }
        File.WriteAllLines("Datos/entorno_barrido.txt", lista);
        return $"{W}x{H} celdas, {lista.Count} superficies -> {SalidaBarrido}";
    }

    static void Cadena(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(b.Length);
        w.Write(b);
    }

    static string Ruta(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
