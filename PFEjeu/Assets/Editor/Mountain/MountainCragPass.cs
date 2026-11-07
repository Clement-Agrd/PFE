using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Passe de "crags" sur la heightmap finale : brise les surfaces lisses et droites des montagnes
    /// (aretes vives, redans, corniches). L'effet croit avec la pente. Rien n'est modifie sous cragStartRel
    /// (au-dessus du volume NavMesh). Deterministe : repart toujours de la heightmap fraichement sculptee.
    /// </summary>
    public static class MountainCragPass
    {
        private const float RidgedBias = 0.4f;
        private const float LedgePatchScale = 24f;
        private const float WarpScale = 30f;

        public static void Apply(float[,] heights, Vector3 origin, Vector3 size, MountainRidgeProfile profile, float valleyY)
        {
            if (!profile.CragEnabled) return;

            int res = heights.GetLength(0);
            float cellX = size.x / (res - 1);
            float cellZ = size.z / (res - 1);
            var source = (float[,])heights.Clone();

            for (int j = 1; j < res - 1; j++)
            {
                float z = origin.z + j * cellZ;
                for (int i = 1; i < res - 1; i++)
                {
                    float y = source[j, i] * size.y + origin.y;
                    float rel = y - valleyY;
                    float altitudeWeight = MountainMath.Smooth(profile.CragStartRel, profile.CragFullRel, rel);
                    if (altitudeWeight <= 0f && rel < profile.CragSteepMinRel) continue;

                    float x = origin.x + i * cellX;
                    float slope = SlopeDegrees(source, i, j, size.y, cellX, cellZ);
                    float slopeWeight = MountainMath.Smooth(profile.CragSlopeLow, profile.CragSlopeHigh, slope);
                    float weight = altitudeWeight * Mathf.Lerp(profile.CragGentleShare, 1f, slopeWeight);

                    // Parois tres raides (infranchissables) : brisees meme sous cragStartRel.
                    float steepWeight = MountainMath.Smooth(profile.CragSteepMinRel, profile.CragSteepMinRel + 10f, rel)
                                        * MountainMath.Smooth(profile.CragSteepSlopeLow, profile.CragSteepSlopeHigh, slope);
                    weight = Mathf.Max(weight, steepWeight);
                    if (weight <= 0f) continue;

                    float wall = Mathf.Max(slopeWeight, steepWeight);
                    float result = y + Displacement(x, z, profile, steepWeight) * weight;
                    result = Ledges(x, z, result, profile, weight * wall, Mathf.Lerp(profile.LedgeStrength, profile.LedgeStrengthOnWalls, steepWeight));
                    heights[j, i] = Mathf.Clamp01((result - origin.y) / size.y);
                }
            }
        }

        private static float Displacement(float x, float z, MountainRidgeProfile p, float wallWeight)
        {
            float wx = x + (Mathf.PerlinNoise(x / WarpScale + 17.3f, z / WarpScale) - 0.5f) * 2f * p.CragWarp;
            float wz = z + (Mathf.PerlinNoise(x / WarpScale, z / WarpScale + 43.9f) - 0.5f) * 2f * p.CragWarp;
            float d = p.CragLargeAmplitude * (Ridged(wx, wz, p.CragLargeScale, 3.7f, 29.1f) - RidgedBias)
                    + p.CragFineAmplitude * (Ridged(wx, wz, p.CragFineScale, 67.3f, 11.9f) - RidgedBias);
            // Parois tres raides : grandes formes (contreforts, dalles, encoches) en plus.
            if (wallWeight > 0f)
                d += p.CragWallAmplitude * wallWeight * (Ridged(wx, wz, p.CragWallScale, 101.3f, 57.9f) - RidgedBias);
            return d;
        }

        /// <summary>Corniches : paliers de ledgeHeight m par plaques, surtout sur les parois raides.</summary>
        private static float Ledges(float x, float z, float y, MountainRidgeProfile p, float strengthWeight, float baseStrength)
        {
            float patch = MountainMath.Smooth(0.35f, 0.65f, MountainMath.Noise01(x, z, LedgePatchScale, 91.7f, 5.3f));
            float strength = baseStrength * strengthWeight * patch;
            if (strength <= 0f) return y;

            float t = y / p.LedgeHeight;
            float whole = Mathf.Floor(t);
            float terraced = (whole + MountainMath.Smooth(0.2f, 0.8f, t - whole)) * p.LedgeHeight;
            return Mathf.Lerp(y, terraced, strength);
        }

        private static float SlopeDegrees(float[,] h, int i, int j, float sizeY, float cellX, float cellZ)
        {
            float dx = (h[j, i + 1] - h[j, i - 1]) * sizeY / (2f * cellX);
            float dz = (h[j + 1, i] - h[j - 1, i]) * sizeY / (2f * cellZ);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }

        private static float Ridged(float x, float z, float scaleMeters, float offsetX, float offsetZ) =>
            1f - Mathf.Abs(2f * MountainMath.Noise01(x, z, scaleMeters, offsetX, offsetZ) - 1f);
    }
}
