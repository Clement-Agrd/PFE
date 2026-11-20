#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DamageFx.Editor
{
    /// <summary>
    /// Dessine les icônes (formes) des types de dégâts et l'éclat de crit par calcul (distance signée) : aucune image à fournir.
    /// Chaque icône = forme blanche + contour sombre, teintée ensuite par la couleur du style.
    /// </summary>
    internal static class DamageFxIconFactory
    {
        private const int Size = 64;
        private const float OutlineWidth = 0.1f;

        public static Sprite CreateIcon(string path, DamageFxKind kind)
        {
            return Save(path, Size, p => IconPixel(Shape(kind, p)), false);
        }

        public static Sprite CreateCritIcon(string path)
        {
            return Save(path, Size, p => IconPixel(Star(p, 0.85f, 0.45f, 5)), false);
        }

        /// <summary>Éclat derrière un crit : étoile à 8 branches, dégradé doux vers le bord.</summary>
        public static Sprite CreateBurst(string path)
        {
            return Save(path, 128, p =>
            {
                float r = p.magnitude;
                float theta = Mathf.Atan2(p.y, p.x);
                float lobe = Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * theta)), 5f);
                float f = r / (0.22f + 0.78f * lobe);
                float a = Mathf.Pow(Mathf.Clamp01(1f - f), 1.4f);
                return new Color(1f, 1f, 1f, a);
            }, true);
        }

        #region Pixels
        private static Color IconPixel(float d)
        {
            const float px = 2f / Size;
            float alpha = Mathf.Clamp01(0.5f - (d - OutlineWidth) / px);
            float inside = Mathf.Clamp01(0.5f - d / px);
            Color c = Color.Lerp(new Color(0.07f, 0.05f, 0.04f), Color.white, inside);
            c.a = alpha;
            return c;
        }

        private static float Shape(DamageFxKind kind, Vector2 p)
        {
            switch (kind)
            {
                case DamageFxKind.Fire: return Flame(p);
                case DamageFxKind.Ice: return Snowflake(p);
                case DamageFxKind.Poison: return Mathf.Min(Mathf.Abs(p.magnitude - 0.52f) - 0.16f, p.magnitude - 0.17f);
                case DamageFxKind.Magic: return Star(p, 0.85f, 0.35f, 4);
                case DamageFxKind.Heal: return Mathf.Min(Box(p, new Vector2(0.72f, 0.24f)), Box(p, new Vector2(0.24f, 0.72f)));
                case DamageFxKind.TowerHit: return Shield(p);
                default: return (Mathf.Abs(p.x) / 0.5f + Mathf.Abs(p.y) / 0.9f - 1f) * 0.38f;   // Physique : losange (lame)
            }
        }
        #endregion

        #region Formes (distance signée, p dans [-1, 1])
        private static float Box(Vector2 p, Vector2 b)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x) - b.x, Mathf.Abs(p.y) - b.y);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        private static float Flame(Vector2 p)
        {
            float body = (p - new Vector2(0f, -0.28f)).magnitude - 0.5f;
            float w = 0.5f * (1f - (p.y + 0.2f) / 1.15f);
            float tip = Mathf.Max(Mathf.Abs(p.x) - w, Mathf.Max(-(p.y + 0.2f), p.y - 0.95f)) * 0.9f;
            return Mathf.Min(body, tip);
        }

        private static float Snowflake(Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 60f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.82f;
                d = Mathf.Min(d, Segment(p, -dir, dir));
            }
            return d - 0.11f;
        }

        private static float Star(Vector2 p, float outer, float inner, int points)
        {
            float r = p.magnitude;
            float theta = Mathf.Atan2(p.y, p.x);
            float k = Mathf.Abs(Mathf.Cos(points * 0.5f * theta));
            float radius = Mathf.Lerp(inner, outer, Mathf.Pow(k, 2.2f));
            return (r - radius) * 0.7f;
        }

        private static float Shield(Vector2 p)
        {
            float side = Mathf.Abs(p.x) - 0.68f;
            float top = p.y - 0.78f;
            float taper = Mathf.Abs(p.x) * 0.9f + (-p.y) * 0.55f - 0.95f;
            return Mathf.Max(Mathf.Max(side, top), taper) * 0.8f;
        }
        #endregion

        #region Sauvegarde
        private static Sprite Save(string path, int size, Func<Vector2, Color> pixel, bool soft)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    pixels[y * size + x] = pixel(p);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;      // la forme fait exactement 1 unité de haut
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        #endregion
    }
}
#endif
