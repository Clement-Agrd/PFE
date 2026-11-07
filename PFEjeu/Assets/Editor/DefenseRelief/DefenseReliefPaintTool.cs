using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Lisibilite : peint la couche de chemin sur les lanes, retire herbe et arbres
    /// dans les couloirs et sur les plateformes de slots, puis rebake le NavMesh.
    /// Base = TerrainData de backup, donc idempotent.
    /// </summary>
    public static class DefenseReliefPaintTool
    {
        private const string SurfaceObjectName = "DefenseNavigation";

        [MenuItem("Tools/Defense/Relief/4 - Peindre chemin et nettoyer")]
        public static void PaintAndClean()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            ReliefField field = DefenseReliefScene.BuildField(config);
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            TerrainData source = config.BaseTerrainData;
            Vector3 origin = terrain.transform.position;

            Undo.RegisterCompleteObjectUndo(data, "Defense Relief : paint and clean");
            PaintPath(config, field, data, source, origin);
            ClearDetails(config, field, data, source, origin);
            ClearTrees(config, field, data, source, origin);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] Peinture et nettoyage termines.");
        }

        [MenuItem("Tools/Defense/Relief/5 - Rebake NavMesh")]
        public static void RebakeNavMesh()
        {
            GameObject host = DefenseReliefScene.Require(SurfaceObjectName);
            var surface = host.GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface);
            DefenseReliefScene.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] NavMesh rebake.");
        }

        #region Texture

        private static void PaintPath(DefenseReliefConfig config, ReliefField field, TerrainData data, TerrainData source, Vector3 origin)
        {
            int layer = FindLayer(data, config.PathLayerName);
            if (layer < 0)
            {
                Debug.LogError("[DefenseRelief] TerrainLayer introuvable : " + config.PathLayerName);
                return;
            }

            int res = data.alphamapResolution;
            float[,,] alpha = source.GetAlphamaps(0, 0, res, res);
            int layers = alpha.GetLength(2);
            float cellX = data.size.x / res;
            float cellZ = data.size.z / res;
            Rect box = field.Bounds;

            for (int j = 0; j < res; j++)
            {
                float z = origin.z + (j + 0.5f) * cellZ;
                if (z < box.yMin || z > box.yMax) continue;
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + (i + 0.5f) * cellX;
                    if (x < box.xMin || x > box.xMax) continue;

                    float outside = field.DistanceOutsideLane(new Vector2(x, z));
                    float mask = 1f - ReliefField.Smooth(0f, config.PathPaintFade, outside);
                    if (mask <= 0f) continue;

                    for (int l = 0; l < layers; l++) alpha[j, i, l] *= 1f - mask;
                    alpha[j, i, layer] += mask;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
        }

        private static int FindLayer(TerrainData data, string layerName)
        {
            TerrainLayer[] all = data.terrainLayers;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == layerName) return i;
            }
            return -1;
        }

        #endregion

        #region Herbe

        private static void ClearDetails(DefenseReliefConfig config, ReliefField field, TerrainData data, TerrainData source, Vector3 origin)
        {
            int res = data.detailResolution;
            float cellX = data.size.x / res;
            float cellZ = data.size.z / res;
            Rect box = field.Bounds;
            int iMin = Mathf.Max(0, Mathf.FloorToInt((box.xMin - origin.x) / cellX));
            int iMax = Mathf.Min(res - 1, Mathf.CeilToInt((box.xMax - origin.x) / cellX));
            int jMin = Mathf.Max(0, Mathf.FloorToInt((box.yMin - origin.z) / cellZ));
            int jMax = Mathf.Min(res - 1, Mathf.CeilToInt((box.yMax - origin.z) / cellZ));

            var cleared = new List<Vector2Int>();
            for (int j = jMin; j <= jMax; j++)
            {
                for (int i = iMin; i <= iMax; i++)
                {
                    var p = new Vector2(origin.x + (i + 0.5f) * cellX, origin.z + (j + 0.5f) * cellZ);
                    if (IsCleared(config, field, p)) cleared.Add(new Vector2Int(i, j));
                }
            }

            int layerCount = data.detailPrototypes.Length;
            for (int layer = 0; layer < layerCount; layer++)
            {
                int[,] map = source.GetDetailLayer(0, 0, res, res, layer);
                foreach (Vector2Int c in cleared) map[c.y, c.x] = 0;
                data.SetDetailLayer(0, 0, layer, map);
            }
        }

        #endregion

        #region Arbres

        private static void ClearTrees(DefenseReliefConfig config, ReliefField field, TerrainData data, TerrainData source, Vector3 origin)
        {
            TreeInstance[] original = source.treeInstances;
            var kept = new List<TreeInstance>(original.Length);
            foreach (TreeInstance tree in original)
            {
                var p = new Vector2(origin.x + tree.position.x * data.size.x, origin.z + tree.position.z * data.size.z);
                if (!IsCleared(config, field, p)) kept.Add(tree);
            }
            data.SetTreeInstances(kept.ToArray(), true);
            Debug.Log("[DefenseRelief] Arbres : " + original.Length + " -> " + kept.Count);
        }

        #endregion

        private static bool IsCleared(DefenseReliefConfig config, ReliefField field, Vector2 p)
        {
            if (!field.Bounds.Contains(p)) return false;
            if (field.DistanceOutsideLane(p) <= config.ClearMargin) return true;
            return field.InsideSlotPlatform(p, 1f);
        }
    }
}
