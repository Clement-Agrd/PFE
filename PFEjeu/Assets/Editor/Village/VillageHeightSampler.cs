using Defense.Relief.EditorTools;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Lecture bilineaire (Y monde) d'une heightmap normalisee [j, i].
    /// Sert de "terrain sous-jacent" au village : relief de BASE + sculpt manuel, avant les etages.
    /// </summary>
    public sealed class VillageHeightSampler
    {
        private readonly float[,] _heights;
        private readonly Vector3 _origin;
        private readonly Vector3 _size;
        private readonly int _res;

        public VillageHeightSampler(float[,] heights, Vector3 origin, Vector3 size)
        {
            _heights = heights;
            _origin = origin;
            _size = size;
            _res = heights.GetLength(0);
        }

        /// <summary>
        /// Recalcule le terrain sous-jacent depuis la BASE (sans les etages). Lecture seule, quelques secondes.
        /// </summary>
        public static VillageHeightSampler BuildUnderlying()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            float[,] heights = DefenseReliefSculptTool.ComputeTargetHeights(config, false);
            VillageManualDelta.Apply(heights);
            return new VillageHeightSampler(
                (float[,])heights.Clone(), DefenseReliefScene.Terrain.transform.position, config.BaseTerrainData.size);
        }

        public float Sample(float worldX, float worldZ)
        {
            float fx = Mathf.Clamp((worldX - _origin.x) / _size.x * (_res - 1), 0f, _res - 1.001f);
            float fz = Mathf.Clamp((worldZ - _origin.z) / _size.z * (_res - 1), 0f, _res - 1.001f);
            int i = (int)fx;
            int j = (int)fz;
            float kx = fx - i;
            float kz = fz - j;
            float a = Mathf.Lerp(_heights[j, i], _heights[j, i + 1], kx);
            float b = Mathf.Lerp(_heights[j + 1, i], _heights[j + 1, i + 1], kx);
            return Mathf.Lerp(a, b, kz) * _size.y + _origin.y;
        }
    }
}
