using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>Arbres et détails (herbe, petits objets) : prototypes, dispersion automatique et pinceaux.</summary>
    public static class VegetationOps
    {
        #region Prototypes
        public static int EnsureTreePrototype(TerrainData data, GameObject prefab)
        {
            TreePrototype[] existing = data.treePrototypes;
            for (int i = 0; i < existing.Length; i++)
                if (existing[i].prefab == prefab) return i;

            var list = new List<TreePrototype>(existing) { new TreePrototype { prefab = prefab } };
            data.treePrototypes = list.ToArray();
            return list.Count - 1;
        }

        public static int EnsureDetailPrototype(TerrainData data, DetailRule rule)
        {
            DetailPrototype[] existing = data.detailPrototypes;
            for (int i = 0; i < existing.Length; i++)
            {
                bool same = rule.useMesh
                    ? existing[i].usePrototypeMesh && existing[i].prototype == rule.prefab
                    : !existing[i].usePrototypeMesh && existing[i].prototypeTexture == rule.texture;
                if (same) return i;
            }

            var proto = new DetailPrototype
            {
                usePrototypeMesh = rule.useMesh,
                prototype = rule.useMesh ? rule.prefab : null,
                prototypeTexture = rule.useMesh ? null : rule.texture,
                renderMode = rule.useMesh ? DetailRenderMode.VertexLit : DetailRenderMode.GrassBillboard,
                useInstancing = rule.useMesh,
                minWidth = rule.widthRange.x,
                maxWidth = rule.widthRange.y,
                minHeight = rule.heightRange.x,
                maxHeight = rule.heightRange.y,
                healthyColor = rule.healthyColor,
                dryColor = rule.dryColor
            };
            var list = new List<DetailPrototype>(existing) { proto };
            data.detailPrototypes = list.ToArray();
            return list.Count - 1;
        }

        private static bool IsUsable(DetailRule r) => r.useMesh ? r.prefab != null : r.texture != null;
        #endregion

        #region Dispersion automatique
        public static void ScatterDetails(Terrain terrain, EasyTerrainProfile profile)
        {
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Disperser herbe");

            int dw = data.detailWidth, dh = data.detailHeight;
            Vector3 size = data.size, origin = terrain.transform.position;

            // Hauteur / pente calculées une seule fois, partagées par toutes les règles.
            var heightCache = new float[dh, dw];
            var slopeCache = new float[dh, dw];
            try
            {
                for (int y = 0; y < dh; y++)
                {
                    if ((y & 31) == 0) EditorUtility.DisplayProgressBar("Easy Terrain", "Analyse du terrain...", y / (float)dh * 0.5f);
                    float nz = y / (float)(dh - 1);
                    for (int x = 0; x < dw; x++)
                    {
                        float nx = x / (float)(dw - 1);
                        heightCache[y, x] = data.GetInterpolatedHeight(nx, nz);
                        slopeCache[y, x] = data.GetSteepness(nx, nz);
                    }
                }

                float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                int aw = data.alphamapWidth, ah = data.alphamapHeight;
                int ruleNumber = 0;

                foreach (DetailRule rule in profile.details)
                {
                    ruleNumber++;
                    if (!IsUsable(rule)) continue;
                    EditorUtility.DisplayProgressBar("Easy Terrain", "Herbe : " + rule.label, 0.5f + 0.5f * ruleNumber / Mathf.Max(1, profile.details.Count));

                    int proto = EnsureDetailPrototype(data, rule);
                    int layerIndex = LayerFactory.IndexOf(data, rule.onlyOnLayer);
                    var map = new int[dh, dw];
                    var rng = new System.Random(profile.seed * 131 + ruleNumber);

                    for (int y = 0; y < dh; y++)
                    {
                        float nz = y / (float)(dh - 1);
                        int ay = Mathf.Min(ah - 1, Mathf.RoundToInt(nz * (ah - 1)));
                        for (int x = 0; x < dw; x++)
                        {
                            float p = NoiseUtil.RangeWeight(heightCache[y, x], rule.altitudeRange.x, rule.altitudeRange.y, 3f);
                            if (p <= 0f || slopeCache[y, x] > rule.maxSlope) continue;

                            float nx = x / (float)(dw - 1);
                            if (layerIndex >= 0)
                            {
                                int ax = Mathf.Min(aw - 1, Mathf.RoundToInt(nx * (aw - 1)));
                                p *= alpha[ay, ax, layerIndex];
                                if (p <= 0.02f) continue;
                            }

                            if (rule.clumping > 0f)
                            {
                                float wx = origin.x + nx * size.x, wz = origin.z + nz * size.z;
                                float n = NoiseUtil.Contrast(NoiseUtil.Fbm(wx / Mathf.Max(rule.clumpScale, 0.1f) + ruleNumber * 5f, wz / Mathf.Max(rule.clumpScale, 0.1f), 2));
                                p *= Mathf.Lerp(1f, n, rule.clumping);
                            }

                            map[y, x] = Mathf.RoundToInt(rule.density * p * (0.6f + 0.4f * (float)rng.NextDouble()));
                        }
                    }
                    data.SetDetailLayer(0, 0, proto, map);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void ScatterTrees(Terrain terrain, EasyTerrainProfile profile)
        {
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Disperser arbres");

            Vector3 size = data.size, origin = terrain.transform.position;
            float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            int aw = data.alphamapWidth, ah = data.alphamapHeight;
            float areaHectares = size.x * size.z / 10000f;
            var instances = new List<TreeInstance>();
            int ruleNumber = 0;

            foreach (TreeRule rule in profile.trees)
            {
                ruleNumber++;
                if (rule.prefab == null) continue;

                int proto = EnsureTreePrototype(data, rule.prefab);
                int layerIndex = LayerFactory.IndexOf(data, rule.onlyOnLayer);
                var rng = new System.Random(profile.seed * 977 + ruleNumber);
                int target = Mathf.RoundToInt(rule.treesPerHectare * areaHectares);
                int maxAttempts = Mathf.Max(target * 6, 1);
                float spacing = Mathf.Max(rule.minSpacing, 0.1f);
                var grid = new Dictionary<long, List<Vector2>>();
                int placed = 0;

                for (int attempt = 0; attempt < maxAttempts && placed < target; attempt++)
                {
                    float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                    float wx = origin.x + nx * size.x, wz = origin.z + nz * size.z;

                    float height = data.GetInterpolatedHeight(nx, nz);
                    if (height < rule.heightRange.x || height > rule.heightRange.y) continue;
                    if (data.GetSteepness(nx, nz) > rule.maxSlope) continue;

                    if (layerIndex >= 0)
                    {
                        int ax = Mathf.Min(aw - 1, Mathf.RoundToInt(nx * (aw - 1)));
                        int ay = Mathf.Min(ah - 1, Mathf.RoundToInt(nz * (ah - 1)));
                        if (alpha[ay, ax, layerIndex] < rule.minLayerWeight) continue;
                    }

                    if (rule.clumping > 0f)
                    {
                        float n = NoiseUtil.Contrast(NoiseUtil.Fbm(wx / Mathf.Max(rule.clumpScale, 0.1f) + ruleNumber * 3f, wz / Mathf.Max(rule.clumpScale, 0.1f), 2));
                        if ((float)rng.NextDouble() > Mathf.Lerp(1f, n, rule.clumping)) continue;
                    }

                    if (!TryReserve(grid, spacing, new Vector2(wx, wz))) continue;

                    float scale = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                    byte tint = (byte)(255f * (1f - rule.colorVariation * (float)rng.NextDouble()));
                    instances.Add(new TreeInstance
                    {
                        position = new Vector3(nx, 0f, nz),          // y recalé par SetTreeInstances(..., true)
                        widthScale = scale,
                        heightScale = scale * Mathf.Lerp(0.92f, 1.08f, (float)rng.NextDouble()),
                        rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                        color = new Color32(tint, tint, tint, 255),
                        lightmapColor = new Color32(255, 255, 255, 255),
                        prototypeIndex = proto
                    });
                    placed++;
                }
            }
            data.SetTreeInstances(instances.ToArray(), true);
        }

        /// <summary>Grille spatiale : refuse le point s'il y a déjà un arbre plus près que "spacing".</summary>
        private static bool TryReserve(Dictionary<long, List<Vector2>> grid, float spacing, Vector2 p)
        {
            int cx = Mathf.FloorToInt(p.x / spacing), cz = Mathf.FloorToInt(p.y / spacing);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.TryGetValue(Key(cx + dx, cz + dz), out List<Vector2> cell)) continue;
                    foreach (Vector2 other in cell)
                        if ((other - p).sqrMagnitude < spacing * spacing) return false;
                }
            }
            long key = Key(cx, cz);
            if (!grid.TryGetValue(key, out List<Vector2> list)) grid[key] = list = new List<Vector2>();
            list.Add(p);
            return true;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public static void ClearTrees(Terrain terrain)
        {
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Effacer arbres");
            terrain.terrainData.SetTreeInstances(new TreeInstance[0], true);
        }

        public static void ClearDetails(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Effacer herbe");
            var empty = new int[data.detailHeight, data.detailWidth];
            for (int i = 0; i < data.detailPrototypes.Length; i++)
                data.SetDetailLayer(0, 0, i, empty);
        }
        #endregion

        #region Pinceaux
        public static void PaintDetail(Terrain terrain, Vector3 hitWorld, float radiusMeters, float strength, DetailRule rule, bool erase)
        {
            TerrainData data = terrain.terrainData;
            if (!erase && !IsUsable(rule)) return;

            int proto = erase ? -1 : EnsureDetailPrototype(data, rule);
            Vector3 origin = terrain.transform.position, size = data.size;
            int dw = data.detailWidth, dh = data.detailHeight;

            int cx = Mathf.RoundToInt((hitWorld.x - origin.x) / size.x * (dw - 1));
            int cz = Mathf.RoundToInt((hitWorld.z - origin.z) / size.z * (dh - 1));
            int rx = Mathf.CeilToInt(radiusMeters / size.x * (dw - 1));
            int rz = Mathf.CeilToInt(radiusMeters / size.z * (dh - 1));
            int x0 = Mathf.Clamp(cx - rx, 0, dw - 1), x1 = Mathf.Clamp(cx + rx, 0, dw - 1);
            int z0 = Mathf.Clamp(cz - rz, 0, dh - 1), z1 = Mathf.Clamp(cz + rz, 0, dh - 1);
            int width = x1 - x0 + 1, depth = z1 - z0 + 1;
            if (width < 1 || depth < 1) return;

            int first = erase ? 0 : proto;
            int last = erase ? data.detailPrototypes.Length - 1 : proto;
            for (int layer = first; layer <= last; layer++)
            {
                int[,] cells = data.GetDetailLayer(x0, z0, width, depth, layer);
                for (int j = 0; j < depth; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        float wx = origin.x + (x0 + i) / (float)(dw - 1) * size.x;
                        float wz = origin.z + (z0 + j) / (float)(dh - 1) * size.z;
                        float d = Mathf.Sqrt((wx - hitWorld.x) * (wx - hitWorld.x) + (wz - hitWorld.z) * (wz - hitWorld.z)) / radiusMeters;
                        if (d >= 1f) continue;
                        float f = Mathf.SmoothStep(1f, 0f, d) * strength;

                        if (erase) { if (Random.value < f) cells[j, i] = 0; }
                        else if (Random.value < f) cells[j, i] = Mathf.Max(cells[j, i], Mathf.RoundToInt(rule.density * Random.Range(0.6f, 1f)));
                    }
                }
                data.SetDetailLayer(x0, z0, layer, cells);
            }
        }

        public static void PlaceTrees(Terrain terrain, Vector3 hitWorld, float radiusMeters, TreeRule rule, int count)
        {
            if (rule == null || rule.prefab == null) return;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position, size = data.size;
            int proto = EnsureTreePrototype(data, rule.prefab);

            for (int k = 0; k < count; k++)
            {
                Vector2 offset = Random.insideUnitCircle * radiusMeters;
                float nx = (hitWorld.x + offset.x - origin.x) / size.x;
                float nz = (hitWorld.z + offset.y - origin.z) / size.z;
                if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) continue;

                float scale = Random.Range(rule.scaleRange.x, rule.scaleRange.y);
                byte tint = (byte)(255f * (1f - rule.colorVariation * Random.value));
                terrain.AddTreeInstance(new TreeInstance
                {
                    position = new Vector3(nx, data.GetInterpolatedHeight(nx, nz) / size.y, nz),
                    widthScale = scale,
                    heightScale = scale * Random.Range(0.92f, 1.08f),
                    rotation = Random.value * Mathf.PI * 2f,
                    color = new Color32(tint, tint, tint, 255),
                    lightmapColor = new Color32(255, 255, 255, 255),
                    prototypeIndex = proto
                });
            }
        }

        public static void EraseTrees(Terrain terrain, Vector3 hitWorld, float radiusMeters)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position, size = data.size;
            TreeInstance[] all = data.treeInstances;
            var kept = new List<TreeInstance>(all.Length);
            float r2 = radiusMeters * radiusMeters;

            foreach (TreeInstance tree in all)
            {
                float dx = origin.x + tree.position.x * size.x - hitWorld.x;
                float dz = origin.z + tree.position.z * size.z - hitWorld.z;
                if (dx * dx + dz * dz > r2) kept.Add(tree);
            }
            if (kept.Count != all.Length) data.SetTreeInstances(kept.ToArray(), true);
        }
        #endregion
    }
}
