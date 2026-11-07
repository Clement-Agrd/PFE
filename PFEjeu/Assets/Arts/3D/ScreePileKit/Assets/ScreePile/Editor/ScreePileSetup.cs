#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScreePile.Editor
{
    /// <summary>
    /// Outil d'import du kit "Blocs rocheux optimisés" (URP).
    ///
    ///  - Import automatique : ScreePile_D.png (albédo sRGB) et ScreePile_N.png (normal map OpenGL) ;
    ///    modèles ScreePile_*.obj (normales importées, tangentes MikkTSpace, pas de collider auto).
    ///  - Menu Tools > ScreePile > Create Material And Prefabs :
    ///      1. crée / met à jour le matériau unique M_ScreePile (URP/Lit) ;
    ///      2. crée les prefabs ScreePile_A..E avec LODGroup (LOD0 / LOD1 / LOD2), MeshCollider (LOD2) et matériau.
    ///
    /// Aucun asset en dehors des fichiers "ScreePile" / "ScreePile" n'est modifié.
    ///
    ///   Prefab ScreePile_A
    ///   ├── LOD0   (MeshRenderer,   1500 tris)  ─┐
    ///   ├── LOD1   (MeshRenderer,   700 tris)   ├─ LODGroup
    ///   └── LOD2   (MeshRenderer,   250 tris)  ─┘
    ///   + MeshCollider non convexe (maillage LOD2, 250 triangles)
    /// </summary>
    public sealed class ScreePileSetup : AssetPostprocessor
    {
        private const string TexturePrefix = "ScreePile_";
        private const string ModelPrefix = "ScreePile_";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string MaterialName = "M_ScreePile";
        private static readonly string[] Variants = { "A", "B" };

        // Hauteur d'écran (fraction) à partir de laquelle chaque LOD s'affiche. Sous LOD2_CULL le bloc est masqué.
        private static readonly float[] LodScreenHeights = { 0.10f, 0.04f, 0.015f };
        private const float CullScreenHeight = 0.008f;

        #region Import des textures et des modèles
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(TexturePrefix)) return;

            bool isNormal = assetPath.EndsWith("_N.png");
            bool isAlbedo = assetPath.EndsWith("_D.png");
            if (!isNormal && !isAlbedo) return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = isAlbedo;        // la normal map est une donnée, jamais de conversion sRGB
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.Contains(ModelPrefix) || !assetPath.EndsWith(".obj")) return;

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.importNormals = ModelImporterNormals.Import;               // normales du fichier (arêtes vives conservées)
            importer.importTangents = ModelImporterTangents.CalculateMikk;      // même convention que la normal map cuite
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;       // éviter la quantification des petits maillages
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // le matériau vient de M_ScreePile
        }
        #endregion

        #region Matériau et prefabs
        [MenuItem("Tools/ScreePile/Create Material And Prefabs")]
        private static void CreateMaterialAndPrefabs()
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null)
            {
                Debug.LogError($"[ScreePile] Shader '{LitShaderName}' introuvable : le projet n'est pas en URP ?");
                return;
            }

            string albedoPath = FindAssetPath($"{TexturePrefix}D", "t:Texture2D");
            string normalPath = FindAssetPath($"{TexturePrefix}N", "t:Texture2D");
            if (albedoPath == null || normalPath == null)
            {
                Debug.LogError("[ScreePile] Textures ScreePile_D / ScreePile_N introuvables.");
                return;
            }

            string rootFolder = Path.GetDirectoryName(Path.GetDirectoryName(albedoPath))?.Replace('\\', '/') ?? "Assets";
            Material material = CreateOrUpdateMaterial(lit, rootFolder, albedoPath, normalPath);
            int created = 0;

            foreach (string variant in Variants)
            {
                if (CreatePrefab(variant, rootFolder, material)) created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ScreePile] Matériau prêt, {created} prefab(s) créé(s) ou mis à jour dans {rootFolder}/Prefabs.");
        }

        private static Material CreateOrUpdateMaterial(Shader lit, string rootFolder, string albedoPath, string normalPath)
        {
            string folder = EnsureFolder(rootFolder, "Materials");
            string path = $"{folder}/{MaterialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = material == null;
            if (isNew) material = new Material(lit) { name = MaterialName };

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Smoothness", 0.10f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;           // même matériau + même mesh = instanciation GPU
            material.EnableKeyword("_NORMALMAP");

            if (isNew) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static bool CreatePrefab(string variant, string rootFolder, Material material)
        {
            string modelPath = FindAssetPath($"{ModelPrefix}{variant}", "t:Model");
            if (modelPath == null)
            {
                Debug.LogWarning($"[ScreePile] Modèle ScreePile_{variant} introuvable, prefab ignoré.");
                return false;
            }

            Mesh[] lodMeshes = new Mesh[3];
            foreach (Mesh mesh in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Mesh>())
            {
                for (int lod = 0; lod < lodMeshes.Length; lod++)
                {
                    if (mesh.name.EndsWith($"_LOD{lod}")) lodMeshes[lod] = mesh;
                }
            }

            if (lodMeshes.Any(m => m == null))
            {
                Debug.LogWarning($"[ScreePile] ScreePile_{variant} : les 3 maillages _LOD0/_LOD1/_LOD2 n'ont pas tous été trouvés " +
                                 $"(trouvés : {string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Mesh>().Select(m => m.name))}).");
                return false;
            }

            var root = new GameObject($"{ModelPrefix}{variant}");
            try
            {
                var renderers = new List<Renderer>();
                var lods = new List<LOD>();
                for (int lod = 0; lod < lodMeshes.Length; lod++)
                {
                    var child = new GameObject($"LOD{lod}");
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = lodMeshes[lod];
                    var meshRenderer = child.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = material;
                    renderers.Add(meshRenderer);

                    float height = LodScreenHeights[lod];
                    lods.Add(new LOD(height, new Renderer[] { meshRenderer }));
                }

                var group = root.AddComponent<LODGroup>();
                group.SetLODs(lods.ToArray());
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();

                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = lodMeshes[2];       // 250 triangles, non convexe : décor statique (un tas n'est pas convexe)

                string folder = EnsureFolder(rootFolder, "Prefabs");
                PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{ModelPrefix}{variant}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return true;
        }
        #endregion

        #region Utilitaires
        // FindAssets fait de la recherche partielle : on exige le nom exact.
        private static string FindAssetPath(string assetName, string typeFilter)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} {typeFilter}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == assetName) return path;
            }

            return null;
        }

        private static string EnsureFolder(string parent, string name)
        {
            string path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
            return path;
        }
        #endregion
    }
}
#endif
