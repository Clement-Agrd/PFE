using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HealFx.Editor
{
    /// <summary>
    /// Génère par script les textures que le kit n'a pas : flipbook de volutes bruitées (8x8) et atlas de runes (4x4).
    ///   Tools > Heal FX > 0 Générer les textures
    /// Sorties (dans le dossier Textures du kit) :
    ///   T_HealSmokeFlipbook.png : 1024 x 1024, 64 images de 128 px. Canal R = densité de la volute, canal G = bruit d'érosion.
    ///   T_HealRuneAtlas.png     : 512 x 512, 16 glyphes de 128 px. Canal R = trait de la rune.
    /// Déterministe (graine fixe) : relancer régénère exactement les mêmes images. HealFxTextureImporter règle l'import (non-sRGB, Clamp).
    /// </summary>
    public static class HealFxTextureGenerator
    {
        #region Constantes
        private const string TexturesDir = "Assets/VFX/HealFx/Assets/HealFx/Textures";
        private const string SmokeName = "T_HealSmokeFlipbook.png";
        private const string RuneName = "T_HealRuneAtlas.png";

        private const int FlipbookGrid = 8;
        private const int FlipbookFrameSize = 128;
        private const int RuneGrid = 4;
        private const int RuneCellSize = 128;
        private const int RuneSeed = 20260511;
        #endregion

        [MenuItem("Tools/Heal FX/0 Générer les textures (volutes + runes)")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(TexturesDir);
            WritePng(SmokeName, BuildSmokeFlipbook());
            WritePng(RuneName, BuildRuneAtlas());
            AssetDatabase.Refresh();
            Debug.Log("[HealFx] Textures générées dans " + TexturesDir);
        }

        public static bool TexturesExist()
        {
            return File.Exists(Path.Combine(TexturesDir, SmokeName)) && File.Exists(Path.Combine(TexturesDir, RuneName));
        }

        #region Flipbook de volutes
        private static Texture2D BuildSmokeFlipbook()
        {
            int size = FlipbookGrid * FlipbookFrameSize;
            var pixels = new Color32[size * size];
            int frameCount = FlipbookGrid * FlipbookGrid;

            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = frame / (float)frameCount;
                // Le flipbook se lit de haut en bas dans Unity (la rangée 0 est en haut) : on écrit donc la ligne inversée.
                int column = frame % FlipbookGrid;
                int row = FlipbookGrid - 1 - frame / FlipbookGrid;

                for (int y = 0; y < FlipbookFrameSize; y++)
                {
                    for (int x = 0; x < FlipbookFrameSize; x++)
                    {
                        float u = x / (float)(FlipbookFrameSize - 1);
                        float v = y / (float)(FlipbookFrameSize - 1);
                        float nx = (u - 0.5f) * 2f;
                        float ny = (v - 0.5f) * 2f;
                        float radial = Mathf.Sqrt(nx * nx + ny * ny);

                        // masque en cloche : la volute ne touche jamais le bord de sa case
                        float shape = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1f, radial));   // 1 au centre, 0 exactement avant le bord de la case

                        // bruit qui dérive vers le haut au fil des images (la brume "monte")
                        float density = Fbm3(u * 3.2f, v * 3.2f - time * 1.6f, time * 1.1f, 4);
                        float r = Mathf.Clamp01(shape * (density * 1.9f - 0.45f));

                        // bruit d'érosion : décorrélé, plus fin
                        float erosion = Fbm3(u * 6.5f + 11.3f, v * 6.5f + 4.7f, time * 0.6f + 2.1f, 3);

                        int px = column * FlipbookFrameSize + x;
                        int py = row * FlipbookFrameSize + y;
                        pixels[py * size + px] = new Color32((byte)(r * 255f), (byte)(erosion * 255f), 0, 255);
                    }
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            return texture;
        }

        // Bruit 3D approché par trois bruits de Perlin 2D croisés (assez pour de la brume), en octaves.
        private static float Fbm3(float x, float y, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f, frequency = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float a = Mathf.PerlinNoise(x * frequency + 17f * i, y * frequency + z * frequency * 0.7f);
                float b = Mathf.PerlinNoise(y * frequency + 31f * i, z * frequency + x * frequency * 0.7f);
                float c = Mathf.PerlinNoise(z * frequency + 53f * i, x * frequency + y * frequency * 0.7f);
                sum += amplitude * (a + b + c) / 3f;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / norm;
        }
        #endregion

        #region Atlas de runes
        private static Texture2D BuildRuneAtlas()
        {
            int size = RuneGrid * RuneCellSize;
            var pixels = new Color32[size * size];
            var random = new System.Random(RuneSeed);

            for (int glyph = 0; glyph < RuneGrid * RuneGrid; glyph++)
            {
                int column = glyph % RuneGrid;
                int row = glyph / RuneGrid;
                DrawGlyph(pixels, size, column * RuneCellSize, row * RuneCellSize, random);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            return texture;
        }

        // Un glyphe = 3 à 5 traits entre les points d'une grille 5x5 (+ parfois un petit cercle) : look "rune" sans copier une écriture réelle.
        private static void DrawGlyph(Color32[] pixels, int size, int originX, int originY, System.Random random)
        {
            const int gridPoints = 5;
            int strokeCount = 3 + random.Next(3);
            var starts = new Vector2[strokeCount];
            var ends = new Vector2[strokeCount];
            for (int s = 0; s < strokeCount; s++)
            {
                starts[s] = GridPoint(random, gridPoints);
                do { ends[s] = GridPoint(random, gridPoints); } while (ends[s] == starts[s]);
            }

            bool hasDot = random.Next(2) == 0;
            Vector2 dot = GridPoint(random, gridPoints);

            const float margin = 0.18f;          // le glyphe reste à l'intérieur de sa case
            const float strokeHalfWidth = 0.035f;
            const float softness = 0.02f;

            for (int y = 0; y < RuneCellSize; y++)
            {
                for (int x = 0; x < RuneCellSize; x++)
                {
                    var uv = new Vector2(x / (float)(RuneCellSize - 1), y / (float)(RuneCellSize - 1));
                    float distance = float.MaxValue;
                    for (int s = 0; s < strokeCount; s++)
                        distance = Mathf.Min(distance, DistanceToSegment(uv, Remap(starts[s], margin), Remap(ends[s], margin)));
                    if (hasDot) distance = Mathf.Min(distance, Mathf.Abs(Vector2.Distance(uv, Remap(dot, margin)) - 0.07f));

                    float value = Mathf.Clamp01((strokeHalfWidth + softness - distance) / softness);
                    byte b = (byte)(value * 255f);
                    pixels[(originY + y) * size + originX + x] = new Color32(b, b, b, 255);
                }
            }
        }

        private static Vector2 GridPoint(System.Random random, int gridPoints)
        {
            return new Vector2(random.Next(gridPoints) / (float)(gridPoints - 1), random.Next(gridPoints) / (float)(gridPoints - 1));
        }

        private static Vector2 Remap(Vector2 point, float margin)
        {
            return new Vector2(Mathf.Lerp(margin, 1f - margin, point.x), Mathf.Lerp(margin, 1f - margin, point.y));
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
            return Vector2.Distance(p, a + ab * t);
        }
        #endregion

        private static void WritePng(string fileName, Texture2D texture)
        {
            string path = Path.Combine(TexturesDir, fileName);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        }
    }
}
