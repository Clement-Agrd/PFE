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
            averageColorCache.Clear();
            colliderColorCache.Clear();
        }

        public static void ClearCache()
        {
            colliderCache.Clear();
            terrainCache.Clear();
            averageColorCache.Clear();
            colliderColorCache.Clear();
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

        // ------------------------------------------------------------------ ground colour

        static readonly Dictionary<Object, Color> averageColorCache = new Dictionary<Object, Color>(32);
        static readonly Dictionary<Collider, Color> colliderColorCache = new Dictionary<Collider, Color>(64);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        /// <summary>
        /// Average colour of the ground under 'point' (rgb) with alpha 1, or alpha 0 when unknown.
        /// Terrain: dominant splat layer's diffuse texture. Other colliders: renderer material (_BaseColor x _BaseMap).
        /// Cached per terrain layer / material / collider: steady state allocates nothing.
        /// </summary>
        public static Color GroundColor(Collider col, Vector3 point)
        {
            if (col == null) return default;

            if (col is TerrainCollider)
            {
                Terrain terrain = col.GetComponent<Terrain>();
                TerrainLayer layer = terrain != null ? DominantLayer(terrain, point) : null;
                return layer != null ? AverageTexture(layer, layer.diffuseTexture, Color.white) : default;
            }

            if (colliderColorCache.TryGetValue(col, out Color cached)) return cached;

            Color result = default;
            Renderer r = col.GetComponent<Renderer>();
            if (r == null) r = col.GetComponentInParent<Renderer>();
            if (r != null && r.sharedMaterial != null)
            {
                Material m = r.sharedMaterial;
                Color tint = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
                Texture tex = m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : m.HasProperty(MainTexId) ? m.GetTexture(MainTexId) : null;
                result = AverageTexture(m, tex, tint);
            }
            colliderColorCache[col] = result;
            return result;
        }

        static TerrainLayer DominantLayer(Terrain terrain, Vector3 point)
        {
            TerrainData data = terrain.terrainData;
            if (data == null) return null;
            if (!terrainCache.TryGetValue(data, out TerrainMap map))
            {
                map = BuildTerrainMap(data, DustSettings.Instance);
                terrainCache[data] = map;
            }
            if (map.dominant == null) return null;

            Vector3 local = point - terrain.transform.position;
            Vector3 size = data.size;
            int x = Mathf.Clamp((int)(local.x / size.x * map.width), 0, map.width - 1);
            int y = Mathf.Clamp((int)(local.z / size.z * map.height), 0, map.height - 1);
            int layer = map.dominant[y * map.width + x];
            TerrainLayer[] layers = data.terrainLayers;
            return layers != null && layer < layers.Length ? layers[layer] : null;
        }

        /// <summary>One-off per texture: downscale to 8x8 on the GPU and average (works on non-readable textures).</summary>
        static Color AverageTexture(Object key, Texture tex, Color tint)
        {
            if (averageColorCache.TryGetValue(key, out Color cached)) return cached;

            Color avg = Color.white;
            if (tex != null)
            {
                RenderTexture rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var small = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
                small.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Color[] px = small.GetPixels();
                Destroy(small);
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
                avg = new Color(r / px.Length, g / px.Length, b / px.Length, 1f);
            }
            avg = new Color(avg.r * tint.r, avg.g * tint.g, avg.b * tint.b, 1f);
            averageColorCache[key] = avg;
            return avg;
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
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
