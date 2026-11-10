using System;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>Paramètres de création d'un terrain.</summary>
    [Serializable]
    public sealed class TerrainCreateSettings
    {
        public string terrainName = "Terrain";
        public float sizeX = 500f;
        public float sizeZ = 500f;
        public float height = 120f;
        public int heightmapResolution = 513;   // 2^n + 1
        public int alphamapResolution = 512;
        public int detailResolution = 1024;
        public int detailPerPatch = 16;
    }

    public static class TerrainFactory
    {
        private const string DataFolder = LayerFactory.RootFolder + "/Data";

        public static Terrain Create(TerrainCreateSettings s)
        {
            LayerFactory.EnsureFolder(DataFolder);

            var data = new TerrainData { name = s.terrainName };
            data.heightmapResolution = s.heightmapResolution;      // avant "size"
            data.alphamapResolution = s.alphamapResolution;
            data.baseMapResolution = Mathf.Min(s.alphamapResolution, 1024);
            data.SetDetailResolution(s.detailResolution, s.detailPerPatch);
            data.size = new Vector3(s.sizeX, s.height, s.sizeZ);

            AssetDatabase.CreateAsset(data, AssetDatabase.GenerateUniqueAssetPath(DataFolder + "/" + s.terrainName + ".asset"));

            GameObject go = Terrain.CreateTerrainGameObject(data);   // Terrain + TerrainCollider
            go.name = s.terrainName;
            Undo.RegisterCreatedObjectUndo(go, "Créer terrain");
            Selection.activeGameObject = go;
            return go.GetComponent<Terrain>();
        }
    }
}
