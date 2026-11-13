using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>Création de TerrainLayer (couches) et de variantes anti-répétition.</summary>
    public static class LayerFactory
    {
        public const string RootFolder = "Assets/EasyTerrain";
        public const string LayersFolder = RootFolder + "/Layers";

        public static TerrainLayer Create(Texture2D albedo, Texture2D normal, Texture2D mask, float tileSize)
        {
            EnsureFolder(LayersFolder);
            var layer = new TerrainLayer
            {
                diffuseTexture = albedo,
                normalMapTexture = normal,
                maskMapTexture = mask,
                tileSize = new Vector2(tileSize, tileSize),
                smoothness = 0f
            };
            string name = albedo != null ? albedo.name : "Layer";
            AssetDatabase.CreateAsset(layer, AssetDatabase.GenerateUniqueAssetPath(LayersFolder + "/" + name + ".terrainlayer"));
            AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// Variante : mêmes textures, taille de tuile différente (ratio non entier) + décalage aléatoire.
        /// Mélangée par bruit avec la couche de base, elle masque le motif répétitif.
        /// </summary>
        public static TerrainLayer CreateVariant(TerrainLayer source, float tileScale, int seed)
        {
            EnsureFolder(LayersFolder);
            var rng = new System.Random(seed * 7919 + 13);
            var variant = new TerrainLayer
            {
                diffuseTexture = source.diffuseTexture,
                normalMapTexture = source.normalMapTexture,
                maskMapTexture = source.maskMapTexture,
                normalScale = source.normalScale,
                smoothness = source.smoothness,
                metallic = source.metallic,
                tileSize = source.tileSize * Mathf.Max(tileScale, 0.05f),
                tileOffset = new Vector2((float)rng.NextDouble() * source.tileSize.x, (float)rng.NextDouble() * source.tileSize.y)
            };
            string name = source.name + "_variant";
            AssetDatabase.CreateAsset(variant, AssetDatabase.GenerateUniqueAssetPath(LayersFolder + "/" + name + ".terrainlayer"));
            AssetDatabase.SaveAssets();
            return variant;
        }

        public static int IndexOf(TerrainData data, TerrainLayer layer)
        {
            if (layer == null) return -1;
            TerrainLayer[] layers = data.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] == layer) return i;
            return -1;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
