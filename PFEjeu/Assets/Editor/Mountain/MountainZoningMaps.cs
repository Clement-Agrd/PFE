using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>Seuils d'altitude resolus (metres relatifs a la vallee).</summary>
    public struct AltitudeSet
    {
        public float treeThinStart;
        public float treeNone;
        public float screeStart;
        public float cliffStart;
        public float summitStart;
        public float frostStart;
    }

    /// <summary>
    /// Cartes de zonage a la resolution de l'alphamap : altitude, pente, protections,
    /// masques de roche/eboulis/givre et densites de vegetation a conserver.
    /// </summary>
    public sealed class ZoningMaps
    {
        public const float FarLane = 1000f;

        public readonly int Res;
        public readonly Vector3 Origin;
        public readonly Vector3 Size;
        public readonly float CellX;
        public readonly float CellZ;

        public readonly float[] RelH;
        public readonly float[] Slope;
        public readonly float[] Protect;
        public readonly float[] LaneOutside;
        public readonly float[] Rock;
        public readonly float[] Scree;
        public readonly float[] Frost;
        public readonly float[] Moss;
        public readonly float[] TreeKeep;
        public readonly float[] GrassKeep;

        public AltitudeSet Altitudes;

        public ZoningMaps(TerrainData data, Vector3 origin)
        {
            Res = data.alphamapResolution;
            Origin = origin;
            Size = data.size;
            CellX = Size.x / Res;
            CellZ = Size.z / Res;
            int n = Res * Res;
            RelH = new float[n];
            Slope = new float[n];
            Protect = new float[n];
            LaneOutside = new float[n];
            Rock = new float[n];
            Scree = new float[n];
            Frost = new float[n];
            Moss = new float[n];
            TreeKeep = new float[n];
            GrassKeep = new float[n];
            for (int i = 0; i < n; i++) LaneOutside[i] = FarLane;
        }

        public int Index(int i, int j) => j * Res + i;

        public Vector2 CellCenter(int i, int j) =>
            new Vector2(Origin.x + (i + 0.5f) * CellX, Origin.z + (j + 0.5f) * CellZ);

        /// <summary>Cellule contenant une position normalisee de terrain (0..1).</summary>
        public int IndexOfNormalized(float u, float v)
        {
            int i = Mathf.Clamp((int)(u * Res), 0, Res - 1);
            int j = Mathf.Clamp((int)(v * Res), 0, Res - 1);
            return j * Res + i;
        }
    }

    /// <summary>Fonctions mathematiques pures partagees par les outils (deterministes).</summary>
    public static class MountainMath
    {
        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Hash deterministe -> [0,1).</summary>
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public static float Noise01(float worldX, float worldZ, float scaleMeters, float offsetX, float offsetZ)
        {
            return Mathf.PerlinNoise(worldX / scaleMeters + offsetX, worldZ / scaleMeters + offsetZ);
        }

        public static float[] BoxBlur(float[] src, int res, int radius)
        {
            if (radius <= 0) return (float[])src.Clone();
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float inv = 1f / (2 * radius + 1);

            for (int j = 0; j < res; j++)
            {
                int row = j * res;
                float sum = 0f;
                for (int t = -radius; t <= radius; t++) sum += src[row + Mathf.Clamp(t, 0, res - 1)];
                for (int i = 0; i < res; i++)
                {
                    tmp[row + i] = sum * inv;
                    sum += src[row + Mathf.Clamp(i + radius + 1, 0, res - 1)] - src[row + Mathf.Clamp(i - radius, 0, res - 1)];
                }
            }

            for (int i = 0; i < res; i++)
            {
                float sum = 0f;
                for (int t = -radius; t <= radius; t++) sum += tmp[Mathf.Clamp(t, 0, res - 1) * res + i];
                for (int j = 0; j < res; j++)
                {
                    dst[j * res + i] = sum * inv;
                    sum += tmp[Mathf.Clamp(j + radius + 1, 0, res - 1) * res + i] - tmp[Mathf.Clamp(j - radius, 0, res - 1) * res + i];
                }
            }
            return dst;
        }
    }
}
