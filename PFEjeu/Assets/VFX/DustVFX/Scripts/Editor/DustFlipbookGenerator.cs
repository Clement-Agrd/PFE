using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DustVFX.EditorTools
{
    /// <summary>
    /// Offline 2D smoke simulation baked to 8x8 flipbooks.
    /// Channels: R = density, G = dissipation speed (thin parts erode first), B/A = normal XY (0.5 = flat).
    /// Grid 128^2, semi-Lagrangian advection, Jacobi pressure projection, curl-noise forcing, weak buoyancy.
    /// </summary>
    public static class DustFlipbookGenerator
    {
        public const string Dir = "Assets/DustVFX/Flipbooks";
        const int Grid = 128;
        const int Tiles = 8;

        enum Kind { Puff, Ring, Wisp }

        struct Params
        {
            public Kind kind;
            public int seed;
            public int stepsPerFrame;
            public int sourceSteps;
            public float damping;      // velocity damping per step
            public float dissipation;  // density multiplier per step
            public float buoyancy;     // upward accel per unit density
            public float curlAmp;      // curl-noise forcing (cells/step^2)
            public float curlFreq;
            public float push;         // source impulse (cells/step)
            public float amount;       // source density per step
            public float gain;         // density -> alpha gain at render
            public float edgeLo, edgeHi;
            public float detail;       // hi-res noise breakup 0..1
            public float normalStrength;
        }

        [MenuItem("Tools/Dust VFX/Generate Flipbooks")]
        public static void GenerateAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/DustVFX", "Flipbooks");

            Generate("FB_DustPuff", 2048, new Params
            {
                kind = Kind.Puff, seed = 11, stepsPerFrame = 3, sourceSteps = 7,
                damping = 0.965f, dissipation = 0.9915f, buoyancy = 0.006f, curlAmp = 0.045f, curlFreq = 0.04f,
                push = 2.3f, amount = 0.6f, gain = 1.5f, edgeLo = 0.05f, edgeHi = 0.8f, detail = 0.85f, normalStrength = 5.5f
            });
            Generate("FB_DustPuffB", 2048, new Params
            {
                kind = Kind.Puff, seed = 53, stepsPerFrame = 3, sourceSteps = 8,
                damping = 0.96f, dissipation = 0.992f, buoyancy = 0.005f, curlAmp = 0.06f, curlFreq = 0.05f,
                push = 2.5f, amount = 0.55f, gain = 1.5f, edgeLo = 0.05f, edgeHi = 0.8f, detail = 0.95f, normalStrength = 5.5f
            });
            Generate("FB_DustRing", 1024, new Params
            {
                kind = Kind.Ring, seed = 7, stepsPerFrame = 3, sourceSteps = 5,
                damping = 0.965f, dissipation = 0.989f, buoyancy = 0f, curlAmp = 0.1f, curlFreq = 0.06f,
                push = 1.7f, amount = 0.5f, gain = 1.7f, edgeLo = 0.05f, edgeHi = 0.8f, detail = 0.8f, normalStrength = 4.5f
            });
            Generate("FB_DustWisp", 1024, new Params
            {
                kind = Kind.Wisp, seed = 3, stepsPerFrame = 3, sourceSteps = 16,
                damping = 0.985f, dissipation = 0.9915f, buoyancy = 0.004f, curlAmp = 0.025f, curlFreq = 0.07f,
                push = 1.5f, amount = 0.55f, gain = 2.1f, edgeLo = 0.08f, edgeHi = 0.55f, detail = 0.55f, normalStrength = 3f
            });

            AssetDatabase.Refresh();
            Debug.Log("[DustVFX] Flipbooks generated in " + Dir);
        }

        // ------------------------------------------------------------------ sim

        sealed class Sim
        {
            public readonly int N;
            public float[] d, u, v;
            readonly float[] tmpA, tmpB, tmpC, p, div;

            public Sim(int n)
            {
                N = n;
                int c = n * n;
                d = new float[c]; u = new float[c]; v = new float[c];
                tmpA = new float[c]; tmpB = new float[c]; tmpC = new float[c];
                p = new float[c]; div = new float[c];
            }

            public float Sample(float[] f, float x, float y)
            {
                x = Mathf.Clamp(x, 0f, N - 1.001f);
                y = Mathf.Clamp(y, 0f, N - 1.001f);
                int x0 = (int)x, y0 = (int)y;
                float fx = x - x0, fy = y - y0;
                int i = y0 * N + x0;
                float a = f[i] + (f[i + 1] - f[i]) * fx;
                float b = f[i + N] + (f[i + N + 1] - f[i + N]) * fx;
                return a + (b - a) * fy;
            }

            void Advect(float[] src, float[] dst)
            {
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        int k = j * N + i;
                        dst[k] = Sample(src, i - u[k], j - v[k]);
                    }
            }

            public void Step(Params P, float time, int step)
            {
                // forcing: curl noise on velocity, weak buoyancy
                float t = time * 0.02f;
                float s0 = P.seed * 37.17f;
                for (int j = 1; j < N - 1; j++)
                    for (int i = 1; i < N - 1; i++)
                    {
                        int k = j * N + i;
                        float m = Mathf.Clamp01(d[k] * 3f);
                        if (m <= 0.0005f) continue;
                        float f = P.curlFreq;
                        float dpdy = Psi((i) * f + s0, (j + 1) * f + t) - Psi((i) * f + s0, (j - 1) * f + t);
                        float dpdx = Psi((i + 1) * f + s0, (j) * f + t) - Psi((i - 1) * f + s0, (j) * f + t);
                        u[k] += dpdy * P.curlAmp * m * 6f;
                        v[k] += -dpdx * P.curlAmp * m * 6f + P.buoyancy * d[k];
                    }

                if (P.kind != Kind.Wisp) Project();

                Advect(u, tmpA); Advect(v, tmpB);
                Array.Copy(tmpA, u, u.Length); Array.Copy(tmpB, v, v.Length);
                if (P.kind != Kind.Wisp) Project();
                Advect(d, tmpC);
                Array.Copy(tmpC, d, d.Length);

                float damp = P.damping, dis = P.dissipation;
                for (int k = 0; k < d.Length; k++)
                {
                    u[k] *= damp; v[k] *= damp;
                    d[k] *= dis;
                    if (d[k] < 1e-4f) d[k] = 0f;
                }
            }

            static float Psi(float x, float y)
            {
                return Mathf.PerlinNoise(x, y) + 0.5f * Mathf.PerlinNoise(x * 2.1f + 17f, y * 2.1f + 5f);
            }

            void Project()
            {
                int n = N;
                for (int j = 1; j < n - 1; j++)
                    for (int i = 1; i < n - 1; i++)
                    {
                        int k = j * n + i;
                        div[k] = -0.5f * (u[k + 1] - u[k - 1] + v[k + n] - v[k - n]);
                        p[k] = 0f;
                    }
                for (int it = 0; it < 24; it++)
                    for (int j = 1; j < n - 1; j++)
                        for (int i = 1; i < n - 1; i++)
                        {
                            int k = j * n + i;
                            p[k] = (div[k] + p[k - 1] + p[k + 1] + p[k - n] + p[k + n]) * 0.25f;
                        }
                for (int j = 1; j < n - 1; j++)
                    for (int i = 1; i < n - 1; i++)
                    {
                        int k = j * n + i;
                        u[k] -= 0.5f * (p[k + 1] - p[k - 1]);
                        v[k] -= 0.5f * (p[k + n] - p[k - n]);
                    }
            }

            public void Splat(float cx, float cy, float r, float amount, float ux, float vy)
            {
                int x0 = Mathf.Max(1, (int)(cx - r * 2.5f)), x1 = Mathf.Min(N - 2, (int)(cx + r * 2.5f));
                int y0 = Mathf.Max(1, (int)(cy - r * 2.5f)), y1 = Mathf.Min(N - 2, (int)(cy + r * 2.5f));
                float inv = 1f / (r * r);
                for (int j = y0; j <= y1; j++)
                    for (int i = x0; i <= x1; i++)
                    {
                        float dx = i - cx, dy = j - cy;
                        float w = Mathf.Exp(-(dx * dx + dy * dy) * inv);
                        int k = j * N + i;
                        d[k] += amount * w;
                        u[k] += ux * w;
                        v[k] += vy * w;
                    }
            }
        }

        static void Source(Sim s, Params P, int step, System.Random rng)
        {
            if (step >= P.sourceSteps) return;
            float N = s.N;
            float fall = 1f - 0.45f * step / P.sourceSteps;
            switch (P.kind)
            {
                case Kind.Puff:
                {
                    float cx = N * 0.5f, cy = N * 0.4f;
                    for (int b = 0; b < 6; b++)
                    {
                        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float rad = (float)rng.NextDouble() * N * 0.07f;
                        float bx = cx + Mathf.Cos(ang) * rad * 1.5f;
                        float by = cy + Mathf.Sin(ang) * rad * 0.7f;
                        float dx = bx - cx, dy = by - cy;
                        float len = Mathf.Max(0.001f, Mathf.Sqrt(dx * dx + dy * dy));
                        float r = N * (0.05f + 0.03f * (float)rng.NextDouble());
                        float push = P.push * fall;
                        s.Splat(bx, by, r, P.amount * fall, dx / len * push * 0.5f, (dy / len * 0.45f + 0.18f) * push * 0.5f);
                    }
                    break;
                }
                case Kind.Ring:
                {
                    float cx = N * 0.5f, cy = N * 0.5f, r0 = N * 0.13f;
                    const int samples = 140;
                    for (int a = 0; a < samples; a++)
                    {
                        float ang = a / (float)samples * Mathf.PI * 2f;
                        float n = Mathf.PerlinNoise(Mathf.Cos(ang) * 1.7f + 20f, Mathf.Sin(ang) * 1.7f + 8f + P.seed);
                        float dens = P.amount * fall * (0.35f + 1.3f * n);
                        float px = cx + Mathf.Cos(ang) * r0, py = cy + Mathf.Sin(ang) * r0;
                        float push = P.push * fall * (0.7f + 0.6f * n);
                        s.Splat(px, py, N * 0.028f, dens * 0.3f, Mathf.Cos(ang) * push * 0.1f, Mathf.Sin(ang) * push * 0.1f);
                    }
                    break;
                }
                case Kind.Wisp:
                {
                    const int strands = 5;
                    float cy = N * 0.5f;
                    for (int k = 0; k < strands; k++)
                    {
                        float off = (k - (strands - 1) * 0.5f) * N * 0.045f + ((float)rng.NextDouble() - 0.5f) * N * 0.02f;
                        for (int x = 0; x < 3; x++)
                            s.Splat(N * 0.12f + x * 2f, cy + off, N * 0.011f, P.amount * 0.3f * fall, P.push * 0.9f, ((float)rng.NextDouble() - 0.5f) * 0.1f);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ bake

        static void Generate(string name, int size, Params P)
        {
            int F = size / Tiles;
            var sim = new Sim(Grid);
            var rng = new System.Random(P.seed);
            var pixels = new Color32[size * size];
            int totalFrames = Tiles * Tiles;

            int step = 0;
            for (int f = 0; f < totalFrames; f++)
            {
                for (int s = 0; s < P.stepsPerFrame; s++)
                {
                    Source(sim, P, step, rng);
                    sim.Step(P, step, step);
                    step++;
                }
                if (f % 8 == 0)
                    EditorUtility.DisplayProgressBar("Dust flipbook " + name, $"frame {f}/{totalFrames}", f / (float)totalFrames);
                RenderFrame(sim, P, F, f / (float)(totalFrames - 1), pixels, size, (f % Tiles) * F, (Tiles - 1 - f / Tiles) * F);
            }
            EditorUtility.ClearProgressBar();

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = size;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.crunchedCompression = false;
            imp.SaveAndReimport();
        }

        static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static float Fbm(float x, float y, int oct)
        {
            float a = 0.5f, sum = 0f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                sum += a * Mathf.PerlinNoise(x, y);
                norm += a;
                x = x * 2.03f + 11.7f; y = y * 2.03f + 3.1f; a *= 0.5f;
            }
            return sum / norm;
        }

        static void RenderFrame(Sim sim, Params P, int F, float t01, Color32[] sheet, int sheetSize, int ox, int oy)
        {
            float scale = sim.N / (float)F;
            var R = new float[F * F];
            float s0 = P.seed * 13.37f;

            for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    float gx = (x + 0.5f) * scale, gy = (y + 0.5f) * scale;
                    // domain warp breaks the bilinear upsampling
                    float wx = (Fbm(x * 0.045f + s0, y * 0.045f + t01 * 2f, 2) - 0.5f) * 5.5f;
                    float wy = (Fbm(x * 0.045f + 40f, y * 0.045f + s0 + t01 * 2f, 2) - 0.5f) * 5.5f;
                    float dens = sim.Sample(sim.d, gx + wx, gy + wy) * P.gain;

                    float nz = Fbm(x * 0.11f + s0, y * 0.11f + 9.1f, 3);
                    float breakup = Mathf.Lerp(1f, 0.35f + 1.3f * nz, P.detail);
                    float a = dens * breakup;
                    float r = SS(P.edgeLo, P.edgeHi, a);

                    // zero padding on every side of the frame (no bleeding between tiles)
                    int e = Mathf.Min(Mathf.Min(x, y), Mathf.Min(F - 1 - x, F - 1 - y));
                    float pad = F * 0.03f;
                    r *= SS(pad, pad + F * 0.07f, e);

                    if (P.kind == Kind.Wisp)
                    {
                        float u = x / (float)(F - 1);
                        r *= SS(0.02f, 0.14f, u) * SS(0.02f, 0.14f, 1f - u);
                    }
                    R[y * F + x] = r;
                }

            var blur = Blur(Blur(R, F, 3), F, 3);

            for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    int k = y * F + x;
                    int xm = Mathf.Max(0, x - 1), xp = Mathf.Min(F - 1, x + 1);
                    int ym = Mathf.Max(0, y - 1), yp = Mathf.Min(F - 1, y + 1);
                    // height = rounded body, so the lighting reads as volume
                    float hx0 = Mathf.Sqrt(blur[y * F + xm]), hx1 = Mathf.Sqrt(blur[y * F + xp]);
                    float hy0 = Mathf.Sqrt(blur[ym * F + x]), hy1 = Mathf.Sqrt(blur[yp * F + x]);
                    float nx = -(hx1 - hx0) * P.normalStrength;
                    float ny = -(hy1 - hy0) * P.normalStrength;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    nx *= inv; ny *= inv;

                    float thin = 1f - SS(0f, 0.85f, blur[k]);
                    float n2 = Fbm(x * 0.07f + 3f, y * 0.07f + 77f + P.seed, 2);
                    float g = Mathf.Clamp01((0.12f + 0.88f * thin) * (0.6f + 0.8f * n2));

                    float r = R[k];
                    // outside the cloud keep the neutral normal so blends stay flat
                    byte rb = (byte)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f);
                    byte gb = (byte)Mathf.RoundToInt(g * 255f);
                    byte bb = (byte)Mathf.RoundToInt((nx * 0.5f + 0.5f) * 255f);
                    byte ab = (byte)Mathf.RoundToInt((ny * 0.5f + 0.5f) * 255f);
                    sheet[(oy + y) * sheetSize + ox + x] = new Color32(rb, gb, bb, ab);
                }
        }

        static float[] Blur(float[] src, int F, int radius)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float inv = 1f / (radius * 2 + 1);
            for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    float s = 0f;
                    for (int k = -radius; k <= radius; k++) s += src[y * F + Mathf.Clamp(x + k, 0, F - 1)];
                    tmp[y * F + x] = s * inv;
                }
            for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    float s = 0f;
                    for (int k = -radius; k <= radius; k++) s += tmp[Mathf.Clamp(y + k, 0, F - 1) * F + x];
                    dst[y * F + x] = s * inv;
                }
            return dst;
        }
    }
}
