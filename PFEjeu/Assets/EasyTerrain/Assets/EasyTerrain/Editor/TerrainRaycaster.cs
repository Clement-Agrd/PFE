using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>
    /// Rayon -> point sur le terrain, calculé directement sur la heightmap (pas de collider) :
    /// toujours à jour pendant qu'on sculpte.
    /// </summary>
    public static class TerrainRaycaster
    {
        private const int RefineIterations = 10;

        public static bool TryHit(Terrain terrain, Ray ray, out Vector3 hit)
        {
            hit = default;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;

            var bounds = new Bounds(origin + size * 0.5f, size);
            float tStart;
            if (bounds.Contains(ray.origin)) tStart = 0f;
            else if (!bounds.IntersectRay(ray, out tStart)) return false;

            float tEnd = tStart + size.magnitude * 1.2f;
            float step = Mathf.Max(0.25f, size.x / data.heightmapResolution);

            float previous = tStart;
            if (SampleBelow(terrain, ray.GetPoint(tStart))) return false;   // départ sous le terrain

            for (float t = tStart + step; t <= tEnd; t += step)
            {
                if (SampleBelow(terrain, ray.GetPoint(t)))
                {
                    float lo = previous, hi = t;
                    for (int i = 0; i < RefineIterations; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (SampleBelow(terrain, ray.GetPoint(mid))) hi = mid; else lo = mid;
                    }
                    hit = ray.GetPoint(hi);
                    return IsInside(terrain, hit);
                }
                previous = t;
            }
            return false;
        }

        private static bool SampleBelow(Terrain terrain, Vector3 p)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            float nx = (p.x - origin.x) / data.size.x;
            float nz = (p.z - origin.z) / data.size.z;
            if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) return false;
            return p.y <= origin.y + data.GetInterpolatedHeight(nx, nz);
        }

        private static bool IsInside(Terrain terrain, Vector3 p)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return p.x >= origin.x && p.x <= origin.x + size.x && p.z >= origin.z && p.z <= origin.z + size.z;
        }
    }
}
