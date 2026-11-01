using UnityEditor;

// Mip streaming en las texturas de las mallas (edificios, mobiliario, viales, equipamiento): en memoria solo
// quedan los mips que hacen falta para lo que se ve, con el mismo detalle de cerca. Las del Terrain (Suelo/,
// Terreno/) y las de los árboles y la hierba (Vegetacion/) se quedan enteras: el Terrain no informa al
// sistema de streaming de los mips que necesita y se cargarían borrosas.
public class TexturasStreaming : AssetPostprocessor
{
    static readonly string[] Carpetas =
    {
        "Assets/_Politecnica/Texturas/",
        "Assets/_Ambientacion/Edificios/Texturas/",
        "Assets/_Ambientacion/Equipamiento/Texturas/",
        "Assets/_Ambientacion/Parque/Texturas/",
        "Assets/_Ambientacion/Viales/Texturas/",
    };

    public override uint GetVersion() => 1;

    void OnPreprocessTexture()
    {
        foreach (var c in Carpetas)
            if (assetPath.StartsWith(c))
            {
                var imp = (TextureImporter)assetImporter;
                if (imp.mipmapEnabled) imp.streamingMipmaps = true;
                return;
            }
    }
}
