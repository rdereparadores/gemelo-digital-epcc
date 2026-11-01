// Ajustes de importación de las imágenes de la UI del gemelo (logotipos de la UEx): sin reescalar a potencia
// de dos (si no, cambiaría su proporción), con mipmaps para que se vean finos a cualquier tamaño y sin compresión.
using UnityEditor;

namespace Gemelo.EditorUI
{
    class ImportadorUI : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Gemelo/UI/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.mipmapEnabled = true;
            ti.alphaIsTransparency = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
