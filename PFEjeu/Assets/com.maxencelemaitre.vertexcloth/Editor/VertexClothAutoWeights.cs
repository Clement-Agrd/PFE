using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Génération automatique des poids (0 = fixe, 1 = libre) à partir d'un preset.</summary>
    public static class VertexClothAutoWeights
    {
        public static float[] Compute(ClothTopology topology, Mesh mesh, VertexClothPreset preset)
        {
            int count = topology.ParticleCount;
            float[] freedom = new float[count];

            if (preset.useVertexColorRed && TryFromVertexColors(topology, mesh, freedom))
                return freedom;

            ComputeGradient(topology, preset, freedom);
            return freedom;
        }

        private static void ComputeGradient(ClothTopology topology, VertexClothPreset preset, float[] freedom)
        {
            int count = topology.ParticleCount;
            Vector3 direction = preset.DirectionVector;
            Vector3[] positions = topology.ParticlePositions;

            int groups = preset.normalizePerIsland ? Mathf.Max(1, topology.IslandCount) : 1;
            float[] min = new float[groups];
            float[] max = new float[groups];
            for (int g = 0; g < groups; g++)
            {
                min[g] = float.MaxValue;
                max[g] = float.MinValue;
            }

            float[] projected = new float[count];
            for (int i = 0; i < count; i++)
            {
                projected[i] = Vector3.Dot(positions[i], direction);
                int group = preset.normalizePerIsland ? topology.IslandOfParticle[i] : 0;
                min[group] = Mathf.Min(min[group], projected[i]);
                max[group] = Mathf.Max(max[group], projected[i]);
            }

            for (int i = 0; i < count; i++)
            {
                int group = preset.normalizePerIsland ? topology.IslandOfParticle[i] : 0;
                float range = max[group] - min[group];
                float u = range > 1e-6f ? (projected[i] - min[group]) / range : 0f;

                float t = Mathf.InverseLerp(preset.pinnedUntil, preset.freeFrom, u);
                t = Mathf.Pow(t, preset.curve);
                freedom[i] = t * t * (3f - 2f * t);
            }
        }

        /// <summary>Canal rouge des vertex colors : 0 = fixe (noir), 1 = libre (blanc / rouge).</summary>
        private static bool TryFromVertexColors(ClothTopology topology, Mesh mesh, float[] freedom)
        {
            Color[] colors = mesh.colors;
            if (colors == null || colors.Length != mesh.vertexCount)
                return false;

            float[] sum = new float[freedom.Length];
            int[] samples = new int[freedom.Length];

            for (int v = 0; v < colors.Length; v++)
            {
                int particle = topology.VertexToParticle[v];
                sum[particle] += colors[v].r;
                samples[particle]++;
            }

            for (int p = 0; p < freedom.Length; p++)
                freedom[p] = samples[p] > 0 ? Mathf.Clamp01(sum[p] / samples[p]) : 0f;

            return true;
        }
    }
}
