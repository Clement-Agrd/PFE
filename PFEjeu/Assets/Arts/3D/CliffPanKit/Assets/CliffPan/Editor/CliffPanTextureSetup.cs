#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CliffPan.Editor
{
    /// <summary>
    /// Outil d'import du kit "Pan de falaise modulaire" (URP) :
    ///   - règle automatiquement les textures CliffPan_*_D.png (albedo sRGB) et CliffPan_*_N.png (normal map OpenGL) ;
    ///   - menu Tools > CliffPan > Create URP Materials : crée / met à jour M_CliffPan_A, _B, _C
    ///     (shader Universal Render Pipeline/Lit) avec leurs textures.
    ///
    /// Ce script ne touche à aucun asset existant en dehors des textures et matériaux "CliffPan_".
    /// </summary>
    public sealed class CliffPanTextureSetup : AssetPostprocessor
    {
        private const string TexturePrefix = "CliffPan_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private static readonly string[] Variants = { "A", "B", "C" };

        #region Import des textures
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturePrefix)) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            bool isNormal = assetPath.EndsWith("_N.png");
            bool isAlbedo = assetPath.EndsWith("_D.png");
            if (!isNormal && !isAlbedo) return;

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = isAlbedo;       // la normal map est une donnée, jamais de conversion sRGB
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
        #endregion

        #region Matériaux URP
        [MenuItem("Tools/CliffPan/Create URP Materials")]
        private static void CreateMaterials()
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null)
            {
                Debug.LogError($"[CliffPan] Shader '{LitShaderName}' introuvable : le projet n'est pas en URP ?");
                return;
            }

            int created = 0;
            foreach (string variant in Variants)
            {
                string albedoPath = FindTexturePath($"{TexturePrefix}{variant}_D");
                string normalPath = FindTexturePath($"{TexturePrefix}{variant}_N");
                if (albedoPath == null || normalPath == null)
                {
                    Debug.LogWarning($"[CliffPan] Textures de la variante {variant} introuvables, matériau ignoré.");
                    continue;
                }

                string rootFolder = Path.GetDirectoryName(Path.GetDirectoryName(albedoPath))?.Replace('\\', '/') ?? "Assets";
                string materialFolder = $"{rootFolder}/Materials";
                if (!AssetDatabase.IsValidFolder(materialFolder))
                {
                    AssetDatabase.CreateFolder(rootFolder, "Materials");
                }

                string materialPath = $"{materialFolder}/M_{TexturePrefix}{variant}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                bool isNew = material == null;
                if (isNew)
                {
                    material = new Material(lit) { name = $"M_{TexturePrefix}{variant}" };
                }

                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                material.SetFloat("_BumpScale", 1f);
                material.SetFloat("_Smoothness", 0.12f);
                material.SetFloat("_Metallic", 0f);
                material.EnableKeyword("_NORMALMAP");

                if (isNew)
                {
                    AssetDatabase.CreateAsset(material, materialPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(material);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CliffPan] Matériaux prêts ({created} créé(s)). Assigne M_CliffPan_A/B/C aux modèles.");
        }

        // Recherche exacte par nom de fichier : FindAssets fait de la recherche partielle.
        private static string FindTexturePath(string textureName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{textureName} t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == textureName) return path;
            }

            return null;
        }
        #endregion
    }
}
#endif
