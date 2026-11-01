// Ajustes de importación de los personajes y animaciones de Mixamo (Assets/_Gemelo/Personas/Mixamo/), que deja ahí
// Herramientas/mixamo/preparar_mixamo.py:
//  · personajes (Personajes/<Nombre>/<Nombre>.fbx): Humanoid con «Create From This Model», sin animaciones ni
//    blendshapes, escala del archivo (Remy viene a 3,8 m: se corrige con globalScale) y los materiales del FBX
//    sustituidos por los URP/Lit de Personajes/<Nombre>/Materiales/ (los crea ConstructorMixamo) con AddRemap, y LOD
//    de malla generados al importar (Mesh LOD);
//  · animaciones (Animaciones/*.fbx): Humanoid con su propio avatar (esqueleto de Y Bot), sin materiales; raíz
//    horneada en la pose (giro, altura y posición, con la referencia original): son «in place»; en bucle salvo los
//    paso de sentarse y levantarse;
//  · texturas: 1024 px como mucho; normales como Normal map; máscaras (brillo en A) lineales; el color con alfa
//    conserva la cobertura en los mipmaps para que el pelo no se deshaga de lejos.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gemelo
{
    public class ImportadorMixamo : AssetPostprocessor
    {
        public const string Carpeta = "Assets/_Gemelo/Personas/Mixamo";
        public const string CarpetaPersonajes = Carpeta + "/Personajes";
        public const string CarpetaAnimaciones = Carpeta + "/Animaciones";
        public const float RecorteAlfa = 0.45f;
        static readonly string[] SinBucle = { "StandToSit", "SitToStand" };

        [System.Serializable]
        public class InfoMaterial { public string nombre, color, normal, mascara; public bool recorte; }
        [System.Serializable]
        public class InfoColores { public string piel, top, mangas, bajo, zapatos, pelo; }
        [System.Serializable]
        public class InfoPersonaje { public string nombre; public float altura; public InfoColores colores; public InfoMaterial[] materiales; }

        public static InfoPersonaje Info(string rutaFbx)
        {
            var json = Path.ChangeExtension(rutaFbx, ".json");
            return File.Exists(json) ? JsonUtility.FromJson<InfoPersonaje>(File.ReadAllText(json)) : null;
        }

        /// <summary>Escala de importación para que el personaje mida lo que una persona (Remy viene a 3,8 m).</summary>
        public static float EscalaImportacion(InfoPersonaje info) => info != null && info.altura > 2.3f ? 1.78f / info.altura : 1f;

        public static string RutaMaterial(string rutaFbx, string material) =>
            $"{Path.GetDirectoryName(rutaFbx)!.Replace('\\', '/')}/Materiales/{material}.mat";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Carpeta + "/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.useFileScale = true;
            mi.optimizeGameObjects = false;
            if (assetPath.StartsWith(CarpetaAnimaciones + "/"))
            {
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.globalScale = 1f;
                return;
            }
            // Personaje
            mi.importAnimation = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            // LOD de malla de Unity 6 (también con SkinnedMeshRenderer): de lejos se dibujan versiones simplificadas
            mi.generateMeshLods = true;
            var info = Info(assetPath);
            mi.globalScale = EscalaImportacion(info);
            if (info?.materiales == null) return;
            foreach (var m in info.materiales)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial(assetPath, m.nombre));
                if (mat != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.nombre), mat);
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CarpetaAnimaciones + "/")) return;
            var mi = (ModelImporter)assetImporter;
            var nombre = Path.GetFileNameWithoutExtension(assetPath);
            bool bucle = System.Array.IndexOf(SinBucle, nombre) < 0;
            var clips = mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                c.name = clips.Length == 1 ? nombre : nombre + "_" + i;
                c.loopTime = bucle;
                c.loopPose = false;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                c.heightFromFeet = false;
                c.mirror = false;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CarpetaPersonajes + "/")) return;
            var ti = (TextureImporter)assetImporter;
            var n = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 2;
            ti.textureCompression = TextureImporterCompression.Compressed;
            if (n.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = false;
            }
            else if (n.EndsWith("_Mascara"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                bool alfa = assetPath.EndsWith(".png");
                ti.alphaSource = alfa ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = alfa;
                ti.mipMapsPreserveCoverage = alfa;
                ti.alphaTestReferenceValue = RecorteAlfa;
            }
        }
    }
}
