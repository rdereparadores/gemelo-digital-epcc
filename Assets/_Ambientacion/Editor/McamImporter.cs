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
// El material se busca en la carpeta Materiales/ más cercana subiendo desde el .mcam (así los de la
// Politécnica usan Assets/_Politecnica/Materiales) y, si no está, en Assets/_Ambientacion/Materiales.
// Los nombres "Grupo_resto" se agrupan bajo un hijo "Grupo". Las piezas de mobiliario (carpeta
// Mobiliario/) llevan una sola BoxCollider en la raíz en vez de MeshCollider por malla, porque se
// instancian miles de veces, y un LODGroup que las oculta cuando se ven muy pequeñas.
[ScriptedImporter(6, "mcam")]
public class McamImporter : ScriptedImporter
{
    public const string MaterialesDir = "Assets/_Ambientacion/Materiales";

    // Materiales que no llevan collider (pintura, follaje...)
    // (el cerco de las puertas tampoco: es el tapajuntas y estrechaba el paso 5 cm por lado; las caras del hueco
    // ya las da el muro)
    static readonly HashSet<string> SinCollider = new HashSet<string> { "Pintura", "Agua", "Follaje", "Campo", "Marco_Puerta" };

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
        var grupos = new Dictionary<string, Transform>();
        bool mobiliario = ctx.assetPath.Replace('\\', '/').Contains("/Mobiliario/");
        var limites = new Bounds();
        bool hayLimites = false;

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
                var mat = BuscarMaterial(ctx, material);
                mr.sharedMaterial = mat != null ? mat : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                if (material == "Pintura" || material == "Luminaria") mr.shadowCastingMode = ShadowCastingMode.Off;
                if (mobiliario)
                {
                    if (hayLimites) limites.Encapsulate(mesh.bounds); else limites = mesh.bounds;
                    hayLimites = true;
                }
                else if (!SinCollider.Contains(material)) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                // Lo opaco de la arquitectura tapa lo que hay detrás (oclusión); el vidrio y los muebles, no
                if (!mobiliario && mr.sharedMaterial != null && mr.sharedMaterial.renderQueue < 2450 && mesh.bounds.size.magnitude > 2f)
                    flags |= StaticEditorFlags.OccluderStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
            }
        }
        if (mobiliario && hayLimites)
        {
            var bc = root.AddComponent<BoxCollider>();
            bc.center = limites.center;
            bc.size = limites.size;
            // Cada pieza deja de dibujarse (y de dar sombra) cuando ocupa menos del 3,5 % de la altura de la
            // pantalla: con el lodBias 2 del proyecto, una silla a ~25 m y un armario a ~100 m. A esa distancia está dentro de un edificio y
            // no se distingue; ahorra miles de renderers y proyectores de sombra en las vistas exteriores
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.035f, root.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
    }

    // Materiales/<nombre>.mat en la carpeta más cercana subiendo desde el .mcam; si no, la del campus.
    // Se declaran como dependencia todas las rutas probadas para reimportar si aparece el material.
    static Material BuscarMaterial(AssetImportContext ctx, string nombre)
    {
        string dir = Path.GetDirectoryName(ctx.assetPath).Replace('\\', '/');
        while (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets"))
        {
            string ruta = $"{dir}/Materiales/{nombre}.mat";
            ctx.DependsOnSourceAsset(ruta);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (mat != null) return mat;
            int i = dir.LastIndexOf('/');
            dir = i > 0 ? dir.Substring(0, i) : null;
        }
        string comun = $"{MaterialesDir}/{nombre}.mat";
        ctx.DependsOnSourceAsset(comun);
        return AssetDatabase.LoadAssetAtPath<Material>(comun);
    }

    static string Cadena(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
}
