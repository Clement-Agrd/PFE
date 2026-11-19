using UnityEditor;

namespace ZoneFx.Editor
{
    /// <summary>Réglages automatiques des textures de Assets/ZoneFx/Textures : masques (données, pas de gamma), bords clampés, mipmaps.</summary>
    public sealed class ZoneFxTextureImporter : AssetPostprocessor
    {
        private const string TexturesFolder = "/ZoneFx/Textures/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturesFolder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;                       // masque : le canal R est une valeur, pas une couleur
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
        }
    }
}
