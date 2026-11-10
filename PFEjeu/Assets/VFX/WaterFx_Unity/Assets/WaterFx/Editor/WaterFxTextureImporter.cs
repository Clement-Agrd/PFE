using UnityEditor;
using UnityEngine;

namespace WaterFx.Editor
{
    /// <summary>
    /// Règle automatiquement l'import des textures d'eau (dossier WaterFx/Textures) :
    /// normale en type NormalMap, écume en données linéaires, wrap Repeat, mipmaps + filtrage anisotrope.
    /// </summary>
    public sealed class WaterFxTextureImporter : AssetPostprocessor
    {
        private const string TexturesFolder = "/WaterFx/Textures/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturesFolder)) return;
            var importer = (TextureImporter)assetImporter;

            bool isNormal = assetPath.Contains("T_WaterNormal");
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;                       // les deux textures sont des données, pas des couleurs
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 1024;
        }
    }
}
