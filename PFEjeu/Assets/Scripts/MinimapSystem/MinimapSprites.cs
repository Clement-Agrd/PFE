using UnityEngine;

namespace Core.Minimap
{
    /// <summary>Petits sprites générés à la volée (aucun asset requis).</summary>
    public static class MinimapSprites
    {
        private static Sprite s_circle, s_square, s_triangle, s_diamond, s_ring;

        public static Sprite Circle => s_circle != null ? s_circle : s_circle = Make(64, (x, y) => Mathf.Clamp01((0.5f - Mathf.Sqrt(x * x + y * y)) * 64f));
        public static Sprite Ring => s_ring != null ? s_ring : s_ring = Make(64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((0.5f - d) * 64f) * Mathf.Clamp01((d - 0.42f) * 64f);
        });
        public static Sprite Square => s_square != null ? s_square : s_square = Make(32, (x, y) => 1f);
        public static Sprite Diamond => s_diamond != null ? s_diamond : s_diamond = Make(64, (x, y) => Mathf.Clamp01((0.5f - (Mathf.Abs(x) + Mathf.Abs(y))) * 48f));
        // pointe vers le haut (+Y)
        public static Sprite Triangle => s_triangle != null ? s_triangle : s_triangle = Make(64, (x, y) =>
        {
            float v = y + 0.5f;                // 0 bas -> 1 haut
            float halfWidth = (1f - v) * 0.5f; // large en bas, pointu en haut
            return Mathf.Clamp01((halfWidth - Mathf.Abs(x)) * 48f) * Mathf.Clamp01(v * 48f);
        });

        public static Sprite For(MarkerType t) => t switch
        {
            MarkerType.Player => Triangle,
            MarkerType.Enemy => Circle,
            MarkerType.EnemySpawn => Diamond,
            MarkerType.Tower => Diamond,
            _ => Square
        };

        private static Sprite Make(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = (i + 0.5f) / size - 0.5f;
                float y = (j + 0.5f) / size - 0.5f;
                px[j * size + i] = new Color(1f, 1f, 1f, alpha(x, y));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
    }
}
