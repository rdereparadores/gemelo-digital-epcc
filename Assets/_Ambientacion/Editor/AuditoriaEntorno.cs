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
