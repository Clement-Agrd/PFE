using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>Peinture des textures : automatique (profil) et au pinceau (couche simple ou overlay à masque de bruit).</summary>
    public static class SplatOps
    {
        #region Peinture automatique
        public static void AutoPaint(Terrain terrain, EasyTerrainProfile profile)
        {
            TerrainData data = terrain.terrainData;

            // 1) Liste des couches finales : base + variante éventuelle, dans l'ordre des règles.
            var layers = new List<TerrainLayer>();
            var rules = new List<LayerRule>();
            var baseIndex = new List<int>();
            var variantIndex = new List<int>();

            foreach (LayerRule rule in profile.layers)
            {
                if (rule.layer == null) continue;

                int b = layers.Count;
                layers.Add(rule.layer);
                int v = -1;
                if (rule.breakRepetition)
                {
                    if (rule.variantLayer == null)
                    {
                        rule.variantLayer = LayerFactory.CreateVariant(rule.layer, rule.variantTileScale, profile.seed + rules.Count);
                        EditorUtility.SetDirty(profile);
                    }
                    v = layers.Count;
                    layers.Add(rule.variantLayer);
                }
                rules.Add(rule);
                baseIndex.Add(b);
                variantIndex.Add(v);
            }

            if (rules.Count == 0)
            {
                Debug.LogWarning("[EasyTerrain] Le profil n'a aucune couche avec une texture : rien à peindre.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(data, "Peinture automatique");
            data.terrainLayers = layers.ToArray();

            int w = data.alphamapWidth, h = data.alphamapHeight, count = layers.Count;
            var map = new float[h, w, count];
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;
            var weights = new float[rules.Count];

            try
            {
                for (int y = 0; y < h; y++)
                {
                    if ((y & 15) == 0 && EditorUtility.DisplayCancelableProgressBar("Easy Terrain", "Peinture automatique...", y / (float)h))
                        return;

                    float nz = y / (float)(h - 1);
                    float wz = origin.z + nz * size.z;
                    for (int x = 0; x < w; x++)
                    {
                        float nx = x / (float)(w - 1);
                        float wx = origin.x + nx * size.x;
                        float height = data.GetInterpolatedHeight(nx, nz);
                        float slope = data.GetSteepness(nx, nz);

                        float total = 0f;
                        for (int r = 0; r < rules.Count; r++)
                        {
                            weights[r] = EvaluateRule(rules[r], height, slope, wx, wz, profile.seed + r * 31);
                            total += weights[r];
                        }
                        if (total < 0.0001f) { weights[0] = 1f; total = 1f; }

                        for (int r = 0; r < rules.Count; r++)
                        {
                            float weight = weights[r] / total;
                            if (variantIndex[r] < 0)
                            {
                                map[y, x, baseIndex[r]] = weight;
                                continue;
                            }
                            var mask = new MaskParams { Scale = rules[r].breakupNoiseScale, Amount = rules[r].breakupAmount, Sharpness = rules[r].breakupSharpness, Seed = profile.seed + r * 57 };
                            float m = NoiseUtil.Mask(wx, wz, mask);
                            map[y, x, baseIndex[r]] = weight * (1f - m);
                            map[y, x, variantIndex[r]] = weight * m;
                        }
                    }
                }
                data.SetAlphamaps(0, 0, map);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static float EvaluateRule(LayerRule rule, float height, float slope, float wx, float wz, int seed)
        {
            float w = rule.weight
                * NoiseUtil.RangeWeight(height, rule.heightRange.x, rule.heightRange.y, rule.heightFalloff)
                * NoiseUtil.RangeWeight(slope, rule.slopeRange.x, rule.slopeRange.y, rule.slopeFalloff);
            if (w <= 0f || rule.noiseAmount <= 0f) return w;

            float n = NoiseUtil.Contrast(NoiseUtil.Fbm(wx / Mathf.Max(rule.noiseScale, 0.1f) + seed * 0.21f, wz / Mathf.Max(rule.noiseScale, 0.1f) - seed * 0.13f, 2));
            return w * Mathf.Lerp(1f, n, rule.noiseAmount);
        }

        /// <summary>Retire les variantes générées pour qu'elles soient recréées (nouvelle taille de tuile).</summary>
        public static void ResetVariants(EasyTerrainProfile profile)
        {
            Undo.RecordObject(profile, "Réinitialiser variantes");
            foreach (LayerRule rule in profile.layers) rule.variantLayer = null;
            EditorUtility.SetDirty(profile);
        }
        #endregion

        #region Pinceau
        /// <summary>
        /// Peint "layerIndex" dans un disque. Avec un masque de bruit = overlay : la texture n'apparaît qu'en taches
        /// par-dessus l'existant (casse manuelle d'une zone répétitive).
        /// </summary>
        public static void Paint(Terrain terrain, Vector3 hitWorld, float radiusMeters, float strength, int layerIndex, MaskParams? overlay)
        {
            TerrainData data = terrain.terrainData;
            if (layerIndex < 0 || layerIndex >= data.alphamapLayers) return;

            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int w = data.alphamapWidth, h = data.alphamapHeight;

            int centerX = Mathf.RoundToInt((hitWorld.x - origin.x) / size.x * (w - 1));
            int centerZ = Mathf.RoundToInt((hitWorld.z - origin.z) / size.z * (h - 1));
            int radiusX = Mathf.CeilToInt(radiusMeters / size.x * (w - 1));
            int radiusZ = Mathf.CeilToInt(radiusMeters / size.z * (h - 1));

            int x0 = Mathf.Clamp(centerX - radiusX, 0, w - 1), x1 = Mathf.Clamp(centerX + radiusX, 0, w - 1);
            int z0 = Mathf.Clamp(centerZ - radiusZ, 0, h - 1), z1 = Mathf.Clamp(centerZ + radiusZ, 0, h - 1);
            int width = x1 - x0 + 1, depth = z1 - z0 + 1;
            if (width < 1 || depth < 1) return;

            float[,,] a = data.GetAlphamaps(x0, z0, width, depth);   // [z, x, layer]
            int layerCount = data.alphamapLayers;

            for (int j = 0; j < depth; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float wx = origin.x + (x0 + i) / (float)(w - 1) * size.x;
                    float wz = origin.z + (z0 + j) / (float)(h - 1) * size.z;
                    float d = Mathf.Sqrt((wx - hitWorld.x) * (wx - hitWorld.x) + (wz - hitWorld.z) * (wz - hitWorld.z)) / radiusMeters;
                    if (d >= 1f) continue;

                    float f = Mathf.SmoothStep(1f, 0f, d) * strength;
                    if (overlay.HasValue) f *= NoiseUtil.Mask(wx, wz, overlay.Value);
                    if (f <= 0.0001f) continue;

                    float current = a[j, i, layerIndex];
                    float target = Mathf.Min(1f, current + f);
                    float others = 1f - current;
                    if (others <= 0.00001f) continue;

                    float k = (1f - target) / others;      // redistribue le reste sur les autres couches
                    for (int l = 0; l < layerCount; l++)
                        if (l != layerIndex) a[j, i, l] *= k;
                    a[j, i, layerIndex] = target;
                }
            }
            data.SetAlphamaps(x0, z0, a);
        }
        #endregion
    }
}
