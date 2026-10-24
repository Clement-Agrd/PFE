using System.IO;
using UnityEditor;
using UnityEngine;

namespace SlashVFX.EditorTools
{
    /// <summary>Generates the slash masks (fine streaks / thick / torn), the noise and the colour ramps.</summary>
    public static class SlashTextureGenerator
    {
        public const string Dir = "Assets/SlashVFX/Textures";

        [MenuItem("Tools/Slash VFX/Generate Textures")]
        public static void GenerateAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/SlashVFX", "Textures");

            Mask("T_SlashMask_A", 11, 1.5f, 3f, 26f, 0.9f, 0.18f, 0f);
            Mask("T_SlashMask_B", 23, 0.75f, 2f, 6f, 0.45f, 0.12f, 0f);
            Mask("T_SlashMask_C", 37, 1.0f, 3f, 14f, 0.7f, 0.7f, 0.5f);
            Noise("T_SlashNoise");

            Ramp("T_SlashRamp_Blue", new[]
            {
                (0f, new Color(0f, 0f, 0f)), (0.12f, new Color(0f, 0.02f, 0.2f)), (0.38f, new Color(0f, 0.22f, 0.95f)),
                (0.68f, new Color(0.08f, 0.75f, 1f)), (0.9f, new Color(0.7f, 0.97f, 1f)), (1f, Color.white)
            });
            Ramp("T_SlashRamp_Violet", new[]
            {
                (0f, new Color(0f, 0f, 0f)), (0.12f, new Color(0.1f, 0f, 0.22f)), (0.4f, new Color(0.45f, 0.05f, 0.95f)),
                (0.7f, new Color(0.75f, 0.3f, 1f)), (0.92f, new Color(0.95f, 0.8f, 1f)), (1f, Color.white)
            });
            Ramp("T_SlashRamp_Fire", new[]
            {
                (0f, new Color(0f, 0f, 0f)), (0.12f, new Color(0.22f, 0.02f, 0f)), (0.38f, new Color(0.95f, 0.18f, 0f)),
                (0.68f, new Color(1f, 0.55f, 0.04f)), (0.9f, new Color(1f, 0.9f, 0.55f)), (1f, Color.white)
            });
            Ramp("T_SlashRamp_Holy", new[]
            {
                (0f, new Color(0f, 0f, 0f)), (0.12f, new Color(0.25f, 0.16f, 0f)), (0.4f, new Color(0.9f, 0.6f, 0.08f)),
                (0.7f, new Color(1f, 0.88f, 0.45f)), (0.92f, new Color(1f, 1f, 0.9f)), (1f, Color.white)
            });
            Ramp("T_SlashRamp_Void", new[]
            {
                (0f, new Color(0f, 0f, 0f)), (0.12f, new Color(0.12f, 0f, 0.14f)), (0.4f, new Color(0.5f, 0f, 0.55f)),
                (0.7f, new Color(0.9f, 0.18f, 0.85f)), (0.92f, new Color(1f, 0.8f, 0.95f)), (1f, Color.white)
            });

            AssetDatabase.Refresh();
            Debug.Log("[SlashVFX] Textures generated in " + Dir);
        }

        // U (x) = arc length, V (y) = width. Bright toward the head, pointed at both ends.
        static void Mask(string name, int seed, float crossPow, float streakU, float streakV, float streakStrength, float tearAmt, float rough)
        {
            const int W = 512, H = 256;
            var px = new Color32[W * H];
            float s = seed * 7.13f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1), v = y / (float)(H - 1);
                    float cross = 1f - Mathf.Abs(2f * v - 1f);
                    float profile = Mathf.Pow(cross, crossPow);
                    float lon = SS(0f, 0.3f, u) * (0.42f + 0.58f * u) * (1f - SS(0.9f, 1f, u));

                    float sn = Mathf.PerlinNoise(u * streakU + s, v * streakV + s * 0.5f) * 0.6f
                             + Mathf.PerlinNoise(u * streakU * 2.3f + 9f, v * streakV * 1.7f + s) * 0.4f;
                    float streak = Mathf.Lerp(1f, SS(0.2f, 0.85f, sn) * 1.25f, streakStrength);

                    float tear = Mathf.PerlinNoise(u * (7f + 9f * rough) + s, v * (3f + 3f * rough) + 3f) * 0.65f + Mathf.PerlinNoise(u * (17f + 15f * rough) + 9f, v * (8f + 5f * rough) + s) * 0.35f;
                    float m = profile * lon * streak - tearAmt * (1f - cross) * tear * 1.4f;
                    // a bright central spine so the HDR core reads
                    m += Mathf.Pow(cross, 5f) * lon * 0.35f;
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(m * 1.25f) * 255f);
                    px[y * W + x] = new Color32(b, b, b, 255);
                }
            Save(name, W, H, px, true, TextureWrapMode.Repeat, false);
        }

        static void Noise(string name)
        {
            const int N = 256;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = Tile(u, v, 3) * 0.62f + Tile(u, v, 6) * 0.3f + Tile(u, v, 12) * 0.08f;
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(n + 0.5f) * 255f);
                    px[y * N + x] = new Color32(b, b, b, 255);
                }
            Save(name, N, N, px, false, TextureWrapMode.Repeat, true);
        }

        static void Ramp(string name, (float, Color)[] stops)
        {
            const int W = 256;
            var px = new Color32[W];
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)(W - 1);
                Color c = stops[stops.Length - 1].Item2;
                for (int i = 0; i < stops.Length - 1; i++)
                    if (t <= stops[i + 1].Item1)
                    {
                        float k = Mathf.InverseLerp(stops[i].Item1, stops[i + 1].Item1, t);
                        c = Color.Lerp(stops[i].Item2, stops[i + 1].Item2, SS(0f, 1f, k));
                        break;
                    }
                px[x] = c;
            }
            Save(name, W, 1, px, true, TextureWrapMode.Clamp, false);
        }

        static void Save(string name, int w, int h, Color32[] px, bool srgb, TextureWrapMode wrap, bool mips)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            tex.SetPixels32(px);
            tex.Apply(false);
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb;
            imp.wrapMode = wrap;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = mips;
            imp.alphaIsTransparency = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }

        static float SS(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Tile(float u, float v, int f)
        {
            float a = Mathf.PerlinNoise(u * f + 50f, v * f + 50f);
            float b = Mathf.PerlinNoise((u - 1f) * f + 50f, v * f + 50f);
            float c = Mathf.PerlinNoise(u * f + 50f, (v - 1f) * f + 50f);
            float d = Mathf.PerlinNoise((u - 1f) * f + 50f, (v - 1f) * f + 50f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) - 0.5f;
        }
    }
}
