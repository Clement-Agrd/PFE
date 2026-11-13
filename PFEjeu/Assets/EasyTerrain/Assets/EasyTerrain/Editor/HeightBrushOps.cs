using UnityEngine;

namespace EasyTerrain.EditorTools
{
    public enum HeightBrushKind { Raise, Lower, Smooth, Flatten }

    /// <summary>Pinceaux de relief : opèrent sur une petite zone de la heightmap (une allocation par coup de pinceau).</summary>
    public static class HeightBrushOps
    {
        private const float RaiseMetersPerStroke = 0.4f;   // à force = 1

        public static void Apply(Terrain terrain, Vector3 hitWorld, float radiusMeters, float strength, HeightBrushKind kind, float flattenHeightMeters)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int res = data.heightmapResolution;

            int centerX = Mathf.RoundToInt((hitWorld.x - origin.x) / size.x * (res - 1));
            int centerZ = Mathf.RoundToInt((hitWorld.z - origin.z) / size.z * (res - 1));
            int radiusX = Mathf.CeilToInt(radiusMeters / size.x * (res - 1));
            int radiusZ = Mathf.CeilToInt(radiusMeters / size.z * (res - 1));

            int x0 = Mathf.Clamp(centerX - radiusX, 0, res - 1), x1 = Mathf.Clamp(centerX + radiusX, 0, res - 1);
            int z0 = Mathf.Clamp(centerZ - radiusZ, 0, res - 1), z1 = Mathf.Clamp(centerZ + radiusZ, 0, res - 1);
            int width = x1 - x0 + 1, depth = z1 - z0 + 1;
            if (width < 1 || depth < 1) return;

            float[,] heights = data.GetHeights(x0, z0, width, depth);                 // [z, x]
            float[,] source = kind == HeightBrushKind.Smooth ? (float[,])heights.Clone() : null;
            float deltaNormalized = strength * RaiseMetersPerStroke / size.y;
            float flattenNormalized = flattenHeightMeters / size.y;

            for (int j = 0; j < depth; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float wx = origin.x + (x0 + i) / (float)(res - 1) * size.x;
                    float wz = origin.z + (z0 + j) / (float)(res - 1) * size.z;
                    float d = Mathf.Sqrt((wx - hitWorld.x) * (wx - hitWorld.x) + (wz - hitWorld.z) * (wz - hitWorld.z)) / radiusMeters;
                    if (d >= 1f) continue;
                    float falloff = Mathf.SmoothStep(1f, 0f, d);

                    switch (kind)
                    {
                        case HeightBrushKind.Raise:
                            heights[j, i] = Mathf.Clamp01(heights[j, i] + deltaNormalized * falloff);
                            break;
                        case HeightBrushKind.Lower:
                            heights[j, i] = Mathf.Clamp01(heights[j, i] - deltaNormalized * falloff);
                            break;
                        case HeightBrushKind.Smooth:
                            heights[j, i] = Mathf.Lerp(heights[j, i], Average(source, i, j, width, depth), Mathf.Clamp01(falloff * strength));
                            break;
                        case HeightBrushKind.Flatten:
                            heights[j, i] = Mathf.Lerp(heights[j, i], flattenNormalized, Mathf.Clamp01(falloff * strength * 0.5f));
                            break;
                    }
                }
            }

            data.SetHeightsDelayLOD(x0, z0, heights);
        }

        /// <summary>À appeler en fin de coup de pinceau : recalcule les LOD et le collider.</summary>
        public static void Finish(Terrain terrain)
        {
            terrain.terrainData.SyncHeightmap();
        }

        private static float Average(float[,] a, int x, int z, int width, int depth)
        {
            float sum = 0f;
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
                    sum += a[nz, nx];
                    count++;
                }
            }
            return count > 0 ? sum / count : a[z, x];
        }
    }
}
