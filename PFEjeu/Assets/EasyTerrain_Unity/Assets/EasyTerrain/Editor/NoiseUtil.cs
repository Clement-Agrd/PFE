using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>Paramètres d'un masque de bruit (taches) utilisé par l'overlay et les variantes.</summary>
    public struct MaskParams
    {
        public float Scale;       // taille d'une tache en mètres
        public float Amount;      // 0 = aucune tache, 1 = presque tout
        public float Sharpness;   // 0 = bords doux, 1 = bords nets
        public int Seed;
    }

    /// <summary>Fonctions pures de bruit et de pondération (aucun état, aucune allocation).</summary>
    public static class NoiseUtil
    {
        public static float Fbm(float x, float z, int octaves)
        {
            float amplitude = 0.5f, frequency = 1f, sum = 0f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency + 17.3f * i, z * frequency - 9.1f * i);
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / total;
        }

        /// <summary>1 dans [min, max], décroît en douceur sur "falloff" de chaque côté.</summary>
        public static float RangeWeight(float value, float min, float max, float falloff)
        {
            float f = Mathf.Max(falloff, 0.0001f);
            float lo = Mathf.Clamp01((value - (min - f)) / f);
            float hi = Mathf.Clamp01(((max + f) - value) / f);
            return Mathf.SmoothStep(0f, 1f, lo) * Mathf.SmoothStep(0f, 1f, hi);
        }

        /// <summary>Masque 0..1 en taches (coordonnées monde en mètres).</summary>
        public static float Mask(float worldX, float worldZ, MaskParams p)
        {
            float scale = Mathf.Max(p.Scale, 0.1f);
            float n = Fbm(worldX / scale + p.Seed * 0.137f, worldZ / scale + p.Seed * 0.311f, 3);
            float threshold = Mathf.Lerp(0.62f, 0.38f, Mathf.Clamp01(p.Amount));
            float half = Mathf.Lerp(0.12f, 0.015f, Mathf.Clamp01(p.Sharpness));
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - half, threshold + half, n));
        }

        /// <summary>Normalise un fbm (centré ~0.5) vers 0..1.</summary>
        public static float Contrast(float fbm) => Mathf.InverseLerp(0.3f, 0.7f, fbm);
    }
}
