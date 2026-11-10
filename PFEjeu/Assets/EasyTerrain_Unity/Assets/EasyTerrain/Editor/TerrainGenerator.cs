using System;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    public enum TerrainPreset { Hills, Mountains, Plateaus, Island, Dunes, ValleyRiver }
    public enum HeightBlend { Replace, Add, Max }

    /// <summary>Paramètres de génération du relief (sérialisés dans la fenêtre).</summary>
    [Serializable]
    public sealed class GeneratorSettings
    {
        public TerrainPreset preset = TerrainPreset.Hills;
        public int seed = 1;
        [Tooltip("Taille d'une grande forme, en mètres.")]
        public float featureSize = 300f;
        [Range(0f, 1f)] public float amplitude = 0.4f;     // fraction de la hauteur du terrain
        [Range(1, 8)] public int octaves = 5;
        [Range(0f, 0.5f)] public float baseHeight = 0.05f;
        public HeightBlend blend = HeightBlend.Replace;
        [Header("Plateaux")] [Range(2, 12)] public int terraceSteps = 5;
        [Header("Vallée / rivière")] public float riverWidth = 18f;
        [Range(0f, 1f)] public float riverDepth = 0.85f;
    }

    /// <summary>Génération et retouches globales du relief (fonctions statiques sans état).</summary>
    public static class TerrainGenerator
    {
        public static void Generate(Terrain terrain, GeneratorSettings s)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            Vector3 size = data.size;
            float[,] old = data.GetHeights(0, 0, res, res);
            var result = new float[res, res];
            float seedX = s.seed * 37.1f, seedZ = s.seed * 91.7f;
            float feature = Mathf.Max(s.featureSize, 1f);

            for (int z = 0; z < res; z++)
            {
                float nz = z / (float)(res - 1);
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    float px = nx * size.x / feature + seedX;
                    float pz = nz * size.z / feature + seedZ;
                    float h = Mathf.Clamp01(Evaluate(s, size, nx, nz, px, pz));

                    switch (s.blend)
                    {
                        case HeightBlend.Add: result[z, x] = Mathf.Clamp01(old[z, x] + h); break;
                        case HeightBlend.Max: result[z, x] = Mathf.Max(old[z, x], h); break;
                        default: result[z, x] = h; break;
                    }
                }
            }
            data.SetHeights(0, 0, result);
        }

        private static float Evaluate(GeneratorSettings s, Vector3 size, float nx, float nz, float px, float pz)
        {
            switch (s.preset)
            {
                case TerrainPreset.Mountains:
                    return Ridged(px, pz, s.octaves) * s.amplitude + s.baseHeight;

                case TerrainPreset.Plateaus:
                {
                    float n = NoiseUtil.Contrast(NoiseUtil.Fbm(px, pz, s.octaves));
                    return Terrace(n, s.terraceSteps, 0.7f) * s.amplitude + s.baseHeight;
                }

                case TerrainPreset.Island:
                {
                    float n = NoiseUtil.Contrast(NoiseUtil.Fbm(px, pz, s.octaves));
                    float dx = nx - 0.5f, dz = nz - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dz * dz) * 2f + (NoiseUtil.Fbm(px * 0.7f + 11f, pz * 0.7f, 3) - 0.5f) * 0.35f;
                    float mask = 1f - Mathf.SmoothStep(0.3f, 0.95f, d);
                    return (0.35f + 0.65f * n) * mask * s.amplitude;
                }

                case TerrainPreset.Dunes:
                {
                    float warp = NoiseUtil.Fbm(px * 0.6f, pz * 0.6f, 3) * 3f;
                    float crest = 1f - Mathf.Abs(Mathf.Sin((px + warp) * Mathf.PI));
                    float envelope = NoiseUtil.Contrast(NoiseUtil.Fbm(px * 0.4f + 5f, pz * 0.4f, 3));
                    return (crest * crest * 0.6f + 0.4f * NoiseUtil.Fbm(px * 2f, pz * 2f, 2)) * (0.4f + 0.6f * envelope) * s.amplitude + s.baseHeight;
                }

                case TerrainPreset.ValleyRiver:
                {
                    float hills = NoiseUtil.Contrast(NoiseUtil.Fbm(px, pz, s.octaves)) * s.amplitude + s.baseHeight;
                    float riverZ = 0.5f + 0.12f * Mathf.Sin(nx * Mathf.PI * 2f * 1.3f + s.seed) + 0.05f * Mathf.Sin(nx * Mathf.PI * 2f * 3.1f);
                    float distanceMeters = Mathf.Abs(nz - riverZ) * size.z;
                    float bed = s.baseHeight * 0.5f;
                    float carve = 1f - Mathf.SmoothStep(s.riverWidth * 0.5f, s.riverWidth * 2.5f, distanceMeters);
                    return Mathf.Lerp(hills, bed, carve * s.riverDepth);
                }

                default: // Hills
                    return NoiseUtil.Contrast(NoiseUtil.Fbm(px, pz, s.octaves)) * s.amplitude + s.baseHeight;
            }
        }

        private static float Ridged(float px, float pz, int octaves)
        {
            float amplitude = 0.5f, frequency = 1f, sum = 0f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(px * frequency + 3.7f * i, pz * frequency - 5.3f * i) - 1f);
                sum += n * n * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }
            return Mathf.Pow(sum / total, 1.15f);
        }

        private static float Terrace(float n, int steps, float sharpness)
        {
            float scaled = Mathf.Clamp01(n) * steps;
            float whole = Mathf.Floor(scaled);
            float fraction = scaled - whole;
            float k = Mathf.Lerp(0.5f, 0.05f, sharpness);
            fraction = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f - k, 0.5f + k, fraction));
            return (whole + fraction) / steps;
        }

        #region Retouches globales
        public static void Smooth(Terrain terrain, int iterations)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] a = data.GetHeights(0, 0, res, res);
            var b = new float[res, res];
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float sum = 0f; int count = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= res || nz >= res) continue;
                                sum += a[nz, nx]; count++;
                            }
                        }
                        b[z, x] = sum / count;
                    }
                }
                (a, b) = (b, a);
            }
            data.SetHeights(0, 0, a);
        }

        public static void Flatten(Terrain terrain, float heightMeters)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float value = Mathf.Clamp01(heightMeters / data.size.y);
            var a = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    a[z, x] = value;
            data.SetHeights(0, 0, a);
        }

        /// <summary>Érosion thermique : les pentes plus raides que l'angle de talus s'effondrent vers le bas.</summary>
        public static void ErodeThermal(Terrain terrain, int iterations, float talusDegrees)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float cellMeters = data.size.x / (res - 1);
            float talus = Mathf.Tan(talusDegrees * Mathf.Deg2Rad) * cellMeters / data.size.y;
            float[,] a = data.GetHeights(0, 0, res, res);
            int[] dxs = { 1, -1, 0, 0 };
            int[] dzs = { 0, 0, 1, -1 };

            for (int it = 0; it < iterations; it++)
            {
                for (int z = 1; z < res - 1; z++)
                {
                    for (int x = 1; x < res - 1; x++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + dxs[k], nz = z + dzs[k];
                            float diff = a[z, x] - a[nz, nx];
                            if (diff <= talus) continue;
                            float move = (diff - talus) * 0.25f;
                            a[z, x] -= move;
                            a[nz, nx] += move;
                        }
                    }
                }
            }
            data.SetHeights(0, 0, a);
        }
        #endregion
    }
}
