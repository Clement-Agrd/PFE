#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LayeredSky.EditorTools
{
    /// <summary>
    /// Réglages d'import HDR pour les textures "LayeredSky_*" (première importation uniquement).
    /// Max Size 4096 (défaut Unity = 2048), BC6H, linéaire, Repeat en U / Clamp en V.
    /// </summary>
    public sealed class LayeredSkyTextureImporter : AssetPostprocessor
    {
        private const string NamePrefix = "LayeredSky_";

        public static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.mipmapEnabled = false;                 // le shader échantillonne au mip 0 (tex2Dlod)
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(NamePrefix)) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.importSettingsMissing) Apply(importer);
        }
    }
}
#endif
