using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>Peint roche, eboulis, mousse et givre dans les alphamaps a partir de la BASE.</summary>
    public static class MountainAlphaPainter
    {
        public struct Stats
        {
            public float rockPercent;
            public float screePercent;
            public float frostPercent;
            public int changedCells;
        }

        private const float LegacySnowMargin = 5f;
        private const float ChangeEpsilon = 0.02f;

        public static Stats Apply(ZoningMaps maps, MountainProfile profile, TerrainData live, TerrainData baseData)
        {
            int rockLayer = RequireLayer(live, profile.RockLayerName);
            int screeLayer = RequireLayer(live, profile.ScreeLayerName);
            int frostLayer = RequireLayer(live, profile.FrostLayerName);
            int mossLayer = RequireLayer(live, profile.MossLayerName);

            int res = maps.Res;
            float[,,] alpha = baseData.GetAlphamaps(0, 0, res, res);
            int layers = alpha.GetLength(2);
            var weights = new float[layers];
            var stats = new Stats();
            double rockSum = 0, screeSum = 0, frostSum = 0;

            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    int k = maps.Index(i, j);
                    float rock = maps.Rock[k];
                    float scree = maps.Scree[k];
                    float frost = maps.Frost[k];
                    float moss = maps.Moss[k];
                    rockSum += rock; screeSum += scree; frostSum += frost;

                    for (int l = 0; l < layers; l++) weights[l] = alpha[j, i, l];
                    float before = weights[rockLayer] + weights[screeLayer] + weights[frostLayer];

                    // Neige heritee a basse altitude : le givre est reserve aux sommets.
                    bool lowSnow = maps.RelH[k] < maps.Altitudes.frostStart - LegacySnowMargin && maps.Protect[k] < 0.5f;
                    if (lowSnow && weights[frostLayer] > 0f)
                    {
                        weights[rockLayer] += weights[frostLayer];
                        weights[frostLayer] = 0f;
                    }

                    float keep = Mathf.Clamp01(1f - rock - scree);
                    for (int l = 0; l < layers; l++) weights[l] *= keep;
                    weights[rockLayer] += rock * (1f - moss);
                    weights[mossLayer] += rock * moss;
                    weights[screeLayer] += scree;

                    float keepFrost = 1f - frost;
                    for (int l = 0; l < layers; l++) weights[l] *= keepFrost;
                    weights[frostLayer] += frost;

                    Normalize(weights);
                    for (int l = 0; l < layers; l++) alpha[j, i, l] = weights[l];

                    float after = weights[rockLayer] + weights[screeLayer] + weights[frostLayer];
                    if (Mathf.Abs(after - before) > ChangeEpsilon) stats.changedCells++;
                }
            }

            live.SetAlphamaps(0, 0, alpha);
            double cells = (double)res * res;
            stats.rockPercent = (float)(100.0 * rockSum / cells);
            stats.screePercent = (float)(100.0 * screeSum / cells);
            stats.frostPercent = (float)(100.0 * frostSum / cells);
            return stats;
        }

        private static void Normalize(float[] weights)
        {
            float sum = 0f;
            for (int l = 0; l < weights.Length; l++) sum += weights[l];
            if (sum <= 1e-5f)
            {
                weights[0] = 1f;
                return;
            }
            float inv = 1f / sum;
            for (int l = 0; l < weights.Length; l++) weights[l] *= inv;
        }

        private static int RequireLayer(TerrainData data, string layerName)
        {
            TerrainLayer[] all = data.terrainLayers;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == layerName) return i;
            }
            var names = new List<string>();
            foreach (TerrainLayer layer in all) names.Add(layer != null ? layer.name : "null");
            throw new InvalidOperationException(
                "TerrainLayer manquant : '" + layerName + "'. Couches presentes : " + string.Join(", ", names));
        }
    }
}
