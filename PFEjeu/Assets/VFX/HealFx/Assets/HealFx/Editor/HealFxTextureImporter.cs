using UnityEditor;

namespace HealFx.Editor
{
    /// <summary>
    /// Réglages automatiques des textures de Assets/HealFx/Textures : masques (donnée, pas couleur), bords clampés.
    /// v2 : le flipbook de volutes n'a pas de mipmaps (elles mélangeraient les 64 cases à distance) ; l'atlas de runes garde les siennes.
    /// </summary>
    public sealed class HealFxTextureImporter : AssetPostprocessor
    {
        private const string TexturesFolder = "/HealFx/Textures/";
        private const string FlipbookToken = "Flipbook";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturesFolder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;                 // masque lu dans le canal R : pas de correction gamma
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.mipmapEnabled = !assetPath.Contains(FlipbookToken);
        }
    }
}
