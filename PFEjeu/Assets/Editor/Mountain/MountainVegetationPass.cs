using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>Eclaircit arbres et herbe selon l'altitude et la roche, depuis la BASE (deterministe).</summary>
    public static class MountainVegetationPass
    {
        public struct Stats
        {
            public int treesBefore;
            public int treesAfter;
            public int treesNearLane;
            public long grassBefore;
            public long grassAfter;
        }

        private const float LaneCheckMeters = 4f;
        private const float HashScale = 100000f;
        private const int TreeSalt = 11;
        private const int GrassSalt = 29;

        public static Stats Apply(ZoningMaps maps, MountainProfile profile, TerrainData live, TerrainData baseData)
        {
            var stats = new Stats();
            ApplyTrees(maps, profile, live, baseData, ref stats);
            ApplyGrass(maps, profile, live, baseData, ref stats);
            return stats;
        }

        private static void ApplyTrees(ZoningMaps maps, MountainProfile profile, TerrainData live, TerrainData baseData, ref Stats stats)
        {
            TreeInstance[] original = baseData.treeInstances;
            var kept = new List<TreeInstance>(original.Length);
            int salt = profile.Seed + TreeSalt;

            foreach (TreeInstance tree in original)
            {
                int k = maps.IndexOfNormalized(tree.position.x, tree.position.z);
                float roll = MountainMath.Hash01(Mathf.RoundToInt(tree.position.x * HashScale), Mathf.RoundToInt(tree.position.z * HashScale), salt);
                if (roll > maps.TreeKeep[k]) continue;
                kept.Add(tree);
                if (maps.LaneOutside[k] < LaneCheckMeters) stats.treesNearLane++;
            }

            live.SetTreeInstances(kept.ToArray(), true);
            stats.treesBefore = original.Length;
            stats.treesAfter = kept.Count;
        }

        private static void ApplyGrass(ZoningMaps maps, MountainProfile profile, TerrainData live, TerrainData baseData, ref Stats stats)
        {
            int detailRes = live.detailResolution;
            int layerCount = live.detailPrototypes.Length;
            int salt = profile.Seed + GrassSalt;

            for (int layer = 0; layer < layerCount; layer++)
            {
                int[,] map = baseData.GetDetailLayer(0, 0, detailRes, detailRes, layer);
                for (int j = 0; j < detailRes; j++)
                {
                    int mj = Mathf.Min(maps.Res - 1, j * maps.Res / detailRes);
                    for (int i = 0; i < detailRes; i++)
                    {
                        int value = map[j, i];
                        if (value == 0) continue;
                        stats.grassBefore += value;

                        int mi = Mathf.Min(maps.Res - 1, i * maps.Res / detailRes);
                        float scaled = value * maps.GrassKeep[maps.Index(mi, mj)];
                        int whole = Mathf.FloorToInt(scaled);
                        float roll = MountainMath.Hash01(i, j * 31 + layer, salt);
                        value = whole + (roll < scaled - whole ? 1 : 0);

                        map[j, i] = value;
                        stats.grassAfter += value;
                    }
                }
                live.SetDetailLayer(0, 0, layer, map);
            }
        }
    }
}
