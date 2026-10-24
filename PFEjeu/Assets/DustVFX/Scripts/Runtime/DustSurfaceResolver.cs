using System.Collections.Generic;
using UnityEngine;

namespace DustVFX
{
    /// <summary>
    /// Ground collider -> DustSurface. Priority: DustSurfaceTag component, PhysicsMaterial, tag,
    /// Terrain splat, default. Results are cached per collider so steady-state lookups allocate nothing.
    /// </summary>
    public static class DustSurfaceResolver
    {
        sealed class TerrainMap
        {
            public byte[] dominant;
            public int width, height;
            public DustSurface[] perLayer;
        }

        static readonly Dictionary<Collider, DustSurface> colliderCache = new Dictionary<Collider, DustSurface>(64);
        static readonly Dictionary<TerrainData, TerrainMap> terrainCache = new Dictionary<TerrainData, TerrainMap>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            colliderCache.Clear();
            terrainCache.Clear();
        }

        public static void ClearCache()
        {
            colliderCache.Clear();
            terrainCache.Clear();
        }

        public static DustSurface Resolve(Collider col, Vector3 point)
        {
            DustSettings settings = DustSettings.Instance;
            if (col == null)
                return settings.defaultSurface;

            if (col is TerrainCollider)
            {
                Terrain terrain = col.GetComponent<Terrain>();
                if (terrain != null)
                {
                    DustSurface s = ResolveTerrain(terrain, point, settings);
                    if (s != null) return s;
                }
            }

            if (colliderCache.TryGetValue(col, out DustSurface cached))
                return cached;

            DustSurface result = ResolveUncached(col, settings);
            colliderCache[col] = result;
            return result;
        }

        static DustSurface ResolveUncached(Collider col, DustSettings settings)
        {
            DustSurfaceTag explicitTag = col.GetComponentInParent<DustSurfaceTag>();
            if (explicitTag != null && explicitTag.surface != null)
                return explicitTag.surface;

            DustSurface[] list = settings.surfaces;
            if (list != null)
            {
                PhysicsMaterial pm = col.sharedMaterial;
                if (pm != null)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        PhysicsMaterial[] mats = list[i] != null ? list[i].physicsMaterials : null;
                        if (mats == null) continue;
                        for (int m = 0; m < mats.Length; m++)
                            if (mats[m] == pm) return list[i];
                    }
                }

                string tag = col.tag;
                if (!string.IsNullOrEmpty(tag) && tag != "Untagged")
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        string[] tags = list[i] != null ? list[i].tags : null;
                        if (tags == null) continue;
                        for (int t = 0; t < tags.Length; t++)
                            if (tags[t] == tag) return list[i];
                    }
                }
            }

            return settings.defaultSurface;
        }

        static DustSurface ResolveTerrain(Terrain terrain, Vector3 point, DustSettings settings)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) return null;

            if (!terrainCache.TryGetValue(data, out TerrainMap map))
            {
                map = BuildTerrainMap(data, settings);
                terrainCache[data] = map;
            }

            if (map.dominant == null) return null;

            Vector3 local = point - terrain.transform.position;
            Vector3 size = data.size;
            int x = Mathf.Clamp((int)(local.x / size.x * map.width), 0, map.width - 1);
            int y = Mathf.Clamp((int)(local.z / size.z * map.height), 0, map.height - 1);
            int layer = map.dominant[y * map.width + x];
            return layer < map.perLayer.Length ? map.perLayer[layer] : null;
        }

        static TerrainMap BuildTerrainMap(TerrainData data, DustSettings settings)
        {
            var map = new TerrainMap();
            TerrainLayer[] layers = data.terrainLayers;
            if (layers == null || layers.Length == 0 || data.alphamapLayers == 0)
                return map;

            map.perLayer = new DustSurface[layers.Length];
            for (int l = 0; l < layers.Length; l++)
            {
                DustSurface found = null;
                if (settings.surfaces != null)
                {
                    for (int i = 0; i < settings.surfaces.Length && found == null; i++)
                    {
                        TerrainLayer[] tl = settings.surfaces[i] != null ? settings.surfaces[i].terrainLayers : null;
                        if (tl == null) continue;
                        for (int k = 0; k < tl.Length; k++)
                            if (tl[k] == layers[l]) { found = settings.surfaces[i]; break; }
                    }
                }
                map.perLayer[l] = found != null ? found : settings.defaultSurface;
            }

            int w = data.alphamapWidth, h = data.alphamapHeight, n = data.alphamapLayers;
            float[,,] alpha = data.GetAlphamaps(0, 0, w, h); // one-off allocation, cached below
            map.width = w;
            map.height = h;
            map.dominant = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int best = 0;
                    float bestV = -1f;
                    for (int l = 0; l < n; l++)
                    {
                        float v = alpha[y, x, l];
                        if (v > bestV) { bestV = v; best = l; }
                    }
                    map.dominant[y * w + x] = (byte)best;
                }
            }
            return map;
        }
    }
}
