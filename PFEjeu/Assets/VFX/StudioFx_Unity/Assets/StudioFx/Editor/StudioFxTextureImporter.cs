using UnityEditor;

namespace StudioFx.Editor
{
    /// <summary>
    /// Réglages automatiques des textures de Assets/StudioFx/Textures :
    ///  - flipbooks et masques : données (pas de gamma), sans mipmaps pour les flipbooks (évite le mélange entre images voisines) ;
    ///  - rampe de couleur : sRGB, sans mipmaps, bords clampés.
    /// </summary>
    public sealed class StudioFxTextureImporter : AssetPostprocessor
    {
        private const string TexturesFolder = "/StudioFx/Textures/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturesFolder)) return;

            var importer = (TextureImporter)assetImporter;
            bool isRamp = assetPath.Contains("Ramp");
            bool isFlipbook = assetPath.Contains("_8x8");

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = isRamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.mipmapEnabled = !(isRamp || isFlipbook);
            importer.maxTextureSize = 2048;
        }
    }
}
