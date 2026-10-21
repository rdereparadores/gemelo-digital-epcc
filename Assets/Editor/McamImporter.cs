using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Importa los .mcam que generan los scripts de Herramientas/ (viales, parque...) como un prefab
// con una malla por entrada. Las mallas viven en Library, así que git solo guarda el binario.
//
// Formato (little endian): "MCAM", nº de mallas, y por malla: nombre, material (cadenas con
// longitud int32), nº de vértices, posiciones float3, UV float2, nº de triángulos, índices int3.
// El material se busca en Assets/Materiales/<material>.mat. Los nombres "Grupo_resto" se
// agrupan bajo un hijo "Grupo".
[ScriptedImporter(3, "mcam")]
public class McamImporter : ScriptedImporter
{
    public const string MaterialesDir = "Assets/Materiales";

    // Materiales que no llevan collider (pintura, follaje...)
    static readonly HashSet<string> SinCollider = new HashSet<string> { "Pintura", "Agua", "Follaje" };

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
        var grupos = new Dictionary<string, Transform>();

        using (var r = new BinaryReader(File.OpenRead(ctx.assetPath)))
        {
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "MCAM")
            {
                ctx.LogImportError("No es un fichero MCAM");
                return;
            }
            int n = r.ReadInt32();
            for (int k = 0; k < n; k++)
            {
                string nombre = Cadena(r), material = Cadena(r);
                int nv = r.ReadInt32();
                var v = new Vector3[nv];
                var uv = new Vector2[nv];
                for (int i = 0; i < nv; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                int nt = r.ReadInt32();
                var t = new int[nt * 3];
                for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32();

                var mesh = new Mesh { name = nombre };
                if (nv > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                ctx.AddObjectToAsset(nombre, mesh);

                string grupo = nombre.Contains("_") ? nombre.Substring(0, nombre.IndexOf('_')) : nombre;
                if (!grupos.TryGetValue(grupo, out var padre))
                {
                    padre = new GameObject(grupo).transform;
                    padre.SetParent(root.transform, false);
                    grupos[grupo] = padre;
                }

                var go = new GameObject(nombre);
                go.transform.SetParent(padre, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                string matPath = $"{MaterialesDir}/{material}.mat";
                ctx.DependsOnSourceAsset(matPath);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                mr.sharedMaterial = mat != null ? mat : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                if (material == "Pintura") mr.shadowCastingMode = ShadowCastingMode.Off;
                if (!SinCollider.Contains(material)) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                           StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
        }
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
    }

    static string Cadena(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
}
