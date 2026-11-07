using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Couleur moyenne (espace lineaire, telle que l'echantillonne le shader) d'une texture, via un blit 1x1.
    /// Sert a caler la luminosite des roches du kit sur celle du pack.
    /// </summary>
    public static class MountainTextureStats
    {
        private static readonly string[] MainTextureProperties = { "_BaseMap", "_Base_Map", "_MainTex", "_Albedo" };
        private static readonly Dictionary<Texture, Color> Cache = new Dictionary<Texture, Color>();

        public static Texture MainTexture(Material material)
        {
            foreach (string property in MainTextureProperties)
            {
                if (!material.HasProperty(property)) continue;
                Texture texture = material.GetTexture(property);
                if (texture != null) return texture;
            }
            return null;
        }

        public static Color Mean(Texture texture)
        {
            if (texture == null) return Color.gray;
            if (Cache.TryGetValue(texture, out Color cached)) return cached;

            RenderTexture target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var reader = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            reader.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            Color mean = reader.GetPixel(0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(reader);

            mean.a = 1f;
            Cache[texture] = mean;
            return mean;
        }

        public static float Luminance(Color c) => (c.r + c.g + c.b) / 3f;
    }
}
