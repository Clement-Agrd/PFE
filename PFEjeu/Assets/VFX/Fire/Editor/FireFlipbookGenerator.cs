#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FireVFX.EditorTools
{
    /// Simulateur de fluide 2D (stable fluids : advection semi-lagrangienne RK2, projection de pression par Jacobi,
    /// flottabilité, vorticity confinement, curl noise) qui génère les flipbooks 8x8 utilisés par FireFlipbook.shader.
    public static class FireFlipbookGenerator
    {
        const string R = "Assets/VFX/Fire";

        // =============================================================== fluide
        sealed class Fluid
        {
            public readonly int N; public readonly float h;
            public float[] u, v, d, T, u0, v0, d0, T0, p, div, w;
            public Fluid(int n)
            {
                N = n; h = 1f / n; int c = n * n;
                u = new float[c]; v = new float[c]; d = new float[c]; T = new float[c];
                u0 = new float[c]; v0 = new float[c]; d0 = new float[c]; T0 = new float[c];
                p = new float[c]; div = new float[c]; w = new float[c];
            }
            public int I(int x, int y) => y * N + x;

            public float Sample(float[] f, float x, float y)
            {
                x = Mathf.Clamp(x, 0f, N - 1.001f); y = Mathf.Clamp(y, 0f, N - 1.001f);
                int x0 = (int)x, y0 = (int)y; float fx = x - x0, fy = y - y0;
                int i = y0 * N + x0;
                return Mathf.Lerp(Mathf.Lerp(f[i], f[i + 1], fx), Mathf.Lerp(f[i + N], f[i + N + 1], fx), fy);
            }

            void Advect(float[] dst, float[] src, float dt)
            {
                float k = dt * N;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        float mx = x - 0.5f * k * u[i], my = y - 0.5f * k * v[i];
                        float vx = Sample(u, mx, my), vy = Sample(v, mx, my);
                        dst[i] = Sample(src, x - k * vx, y - k * vy);
                    }
            }

            public void Step(float dt, float buoyancy, float drag, float cool, float burn, float vort, float turb, float turbFreq, float time, int seed, float gravD = 0f)
            {
                // forces
                for (int y = 1; y < N - 1; y++)
                    for (int x = 1; x < N - 1; x++)
                    {
                        int i = y * N + x;
                        v[i] += (buoyancy * T[i] - gravD * d[i]) * dt;
                        u[i] *= 1f - drag * dt; v[i] *= 1f - drag * dt;
                        if (turb > 0f)
                        {
                            float fx = x * h * turbFreq, fy = y * h * turbFreq, e = 0.02f;
                            float pyp = Psi(fx, fy + e, time, seed), pym = Psi(fx, fy - e, time, seed);
                            float pxp = Psi(fx + e, fy, time, seed), pxm = Psi(fx - e, fy, time, seed);
                            float amp = turb * (0.15f + T[i] + d[i] * 0.3f);
                            u[i] += (pyp - pym) / (2f * e) * amp * dt;
                            v[i] -= (pxp - pxm) / (2f * e) * amp * dt;
                        }
                    }
                // vorticity confinement
                for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
                    { int i = y * N + x; w[i] = ((v[i + 1] - v[i - 1]) - (u[i + N] - u[i - N])) * 0.5f / h; }
                if (vort > 0f)
                    for (int y = 2; y < N - 2; y++)
                        for (int x = 2; x < N - 2; x++)
                        {
                            int i = y * N + x;
                            float gx = (Mathf.Abs(w[i + 1]) - Mathf.Abs(w[i - 1])) * 0.5f, gy = (Mathf.Abs(w[i + N]) - Mathf.Abs(w[i - N])) * 0.5f;
                            float l = Mathf.Sqrt(gx * gx + gy * gy) + 1e-5f; gx /= l; gy /= l;
                            u[i] += vort * h * gy * w[i] * dt;
                            v[i] -= vort * h * gx * w[i] * dt;
                        }
                Project();
                Advect(u0, u, dt); Advect(v0, v, dt); Swap(ref u, ref u0); Swap(ref v, ref v0);
                Project();
                Advect(d0, d, dt); Advect(T0, T, dt); Swap(ref d, ref d0); Swap(ref T, ref T0);
                for (int i = 0; i < d.Length; i++)
                {
                    T[i] *= 1f - cool * dt;
                    d[i] *= 1f - (burn + (1f - Mathf.Clamp01(T[i])) * burn) * dt;
                    if (d[i] < 0f) d[i] = 0f; if (T[i] < 0f) T[i] = 0f;
                }
            }

            static void Swap(ref float[] a, ref float[] b) { var t = a; a = b; b = t; }

            static float Psi(float x, float y, float t, int seed)
            {
                float s = seed * 13.37f;
                return 0.6f * Mathf.PerlinNoise(x + s, y + t * 0.9f) + 0.3f * Mathf.PerlinNoise(x * 2.1f + s + 5f, y * 2.1f + t * 1.3f + 9f) + 0.1f * Mathf.PerlinNoise(x * 4.3f + s, y * 4.3f + t * 1.7f);
            }

            void Project()
            {
                for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
                    { int i = y * N + x; div[i] = -0.5f * h * (u[i + 1] - u[i - 1] + v[i + N] - v[i - N]); p[i] = 0f; }
                for (int it = 0; it < 36; it++)
                    for (int y = 1; y < N - 1; y++)
                        for (int x = 1; x < N - 1; x++)
                        { int i = y * N + x; p[i] = (div[i] + p[i - 1] + p[i + 1] + p[i - N] + p[i + N]) * 0.25f; }
                for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
                    { int i = y * N + x; u[i] -= 0.5f * (p[i + 1] - p[i - 1]) / h; v[i] -= 0.5f * (p[i + N] - p[i - N]) / h; }
                // frontières ouvertes : on recopie l'intérieur
                for (int k = 0; k < N; k++)
                {
                    u[k] = u[k + N]; v[k] = v[k + N]; u[(N - 1) * N + k] = u[(N - 2) * N + k]; v[(N - 1) * N + k] = v[(N - 2) * N + k];
                    u[k * N] = u[k * N + 1]; v[k * N] = v[k * N + 1]; u[k * N + N - 1] = u[k * N + N - 2]; v[k * N + N - 1] = v[k * N + N - 2];
                }
            }

            public void Splat(float cx, float cy, float rad, float dAmt, float tAmt, float vx = 0f, float vy = 0f, float radial = 0f, float noise = 0f, float seed = 0f)
            {
                int r = Mathf.CeilToInt(rad * N * 2.2f);
                int ix = Mathf.RoundToInt(cx * N), iy = Mathf.RoundToInt(cy * N);
                for (int y = Mathf.Max(1, iy - r); y < Mathf.Min(N - 1, iy + r); y++)
                    for (int x = Mathf.Max(1, ix - r); x < Mathf.Min(N - 1, ix + r); x++)
                    {
                        float dx = (x + 0.5f) * h - cx, dy = (y + 0.5f) * h - cy;
                        float g = Mathf.Exp(-(dx * dx + dy * dy) / (rad * rad));
                        if (noise > 0f) g *= 1f - noise + noise * 2f * Mathf.PerlinNoise(x * 0.16f + 17.1f, y * 0.16f + seed);
                        int i = y * N + x;
                        d[i] += dAmt * g; T[i] += tAmt * g;
                        float l = Mathf.Sqrt(dx * dx + dy * dy) + 1e-4f;
                        u[i] += (vx + radial * dx / l) * g; v[i] += (vy + radial * dy / l) * g;
                    }
            }
        }

        // =============================================================== utilitaires de rendu
        static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }
        static float Fbm(float x, float y, int seed)
        {
            float s = seed * 17.3f;
            return 0.5f * Mathf.PerlinNoise(x + s, y + s) + 0.27f * Mathf.PerlinNoise(x * 2.03f + s + 5.2f, y * 2.03f + 1.7f) + 0.15f * Mathf.PerlinNoise(x * 4.1f + s + 9.7f, y * 4.1f + 3.1f) + 0.08f * Mathf.PerlinNoise(x * 8.3f + s + 1.3f, y * 8.3f + 7.7f);
        }
        static Vector2 CurlWarp(float x, float y, float t, int seed, float e = 0.015f)
        {
            Func<float, float, float> P = (a, b) => Fbm(a, b + t, seed);
            return new Vector2((P(x, y + e) - P(x, y - e)) / (2f * e), -(P(x + e, y) - P(x - e, y)) / (2f * e));
        }
        static float Edge(float u, float v, float m = 0.07f)
        {
            float e = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            return Smooth(0.012f, m, e);
        }

        struct Spec
        {
            public string name; public int grid, frames, frameSize, seed; public bool loop; public int kind; // 0 flame, 1 explosion, 2 smoke
            public float fps;
        }

        static void SimulateAndSave(Spec s)
        {
            int cols = 8, size = s.frameSize, atlas = cols * size;
            int extra = s.loop ? 16 : 0, total = s.frames + extra;
            var sim = new Fluid(s.grid);
            var rec = new float[total][];
            float t = 0f, dt = 1f / 60f; int sub = Mathf.Max(1, Mathf.RoundToInt(60f / s.fps));
            int warm = s.loop ? 150 : 0;
            int step = 0;
            for (int f = -Mathf.CeilToInt(warm / (float)sub); f < total; f++)
            {
                for (int k = 0; k < sub; k++, step++, t += dt)
                {
                    Inject(sim, s, step - warm, t);
                    switch (s.kind)
                    {
                        case 0: sim.Step(dt, 9f, 0.5f, 1.8f, 0.7f, 6f, 13f, 3f, t, s.seed); break;
                        case 1: sim.Step(dt, 4.2f, 0.6f, 1.9f, 0.85f, 26f, 4.8f, 2.6f, t, s.seed, 0.4f); break;
                        default: sim.Step(dt, 1.7f, 0.7f, 1.9f, 0.05f, 24f, 3.6f, 2.1f, t, s.seed, 0.1f); break;
                    }
                }
                if (f >= 0) rec[f] = Render(sim, s, size, f / (float)Mathf.Max(1, s.frames - 1), t);
                if (f % 8 == 0) EditorUtility.DisplayProgressBar("Flipbook " + s.name, "frame " + f + "/" + total, (f + 1f) / total);
            }
            // boucle parfaite : crossfade des 16 dernières frames vers les 16 premières
            var final = new float[s.frames][];
            for (int f = 0; f < s.frames; f++)
            {
                if (s.loop && f < extra)
                {
                    float k = Smooth(0f, 1f, f / (float)extra); var a = rec[s.frames + f]; var b = rec[f]; var o = new float[a.Length];
                    for (int i = 0; i < o.Length; i++) o[i] = Mathf.Lerp(a[i], b[i], k);
                    final[f] = o;
                }
                else final[f] = rec[f];
            }

            var px = new Color32[atlas * atlas];
            for (int f = 0; f < s.frames; f++)
            {
                int col = f % cols, row = f / cols; int ox = col * size, oy = (cols - 1 - row) * size;
                var fr = final[f];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int si = (y * size + x) * 4;
                        px[(oy + y) * atlas + ox + x] = new Color32(To8(fr[si]), To8(fr[si + 1]), To8(fr[si + 2]), To8(fr[si + 3]));
                    }
            }
            WriteTexture(s.name, atlas, px);
            EditorUtility.ClearProgressBar();
        }

        static byte To8(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        static void WriteTexture(string name, int w, Color32[] px, bool linear = true)
        {
            var tex = new Texture2D(w, w, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px); tex.Apply();
            string path = R + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = !linear; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 4096; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.crunchedCompression = false;
            ti.SaveAndReimport();
        }

        // =============================================================== sources
        static void Inject(Fluid f, Spec s, int step, float t)
        {
            float time = step / 60f;
            switch (s.kind)
            {
                case 0: // flamme : source en bas, un peu agitée
                    {
                        float wob = 0.02f * Mathf.Sin(t * 5.3f + s.seed) + 0.015f * Mathf.Sin(t * 9.1f);
                        f.Splat(0.5f + wob, 0.1f, 0.13f, 0.7f, 1.0f, 0f, 0.45f, 0f, 0.7f, t * 2.6f + s.seed * 11f);
                        break;
                    }
                case 1: // explosion : impulsion radiale initiale puis combustion entretenue
                    {
                        if (step == 0) f.Splat(0.5f, 0.26f, 0.08f, 3.2f, 3.2f, 0f, 0.3f, 4.4f, 0.45f, s.seed * 11f);
                        if (step >= 0 && step < 26) f.Splat(0.5f, 0.24f, 0.07f, 0.35f, 0.35f, 0f, 1.1f, 0.5f, 0.5f, s.seed * 11f + step * 0.35f);
                        break;
                    }
                default: // fumée : colonne chaude qui se refroidit
                    {
                        if (step >= 0 && step < 80) f.Splat(0.5f, 0.16f, 0.085f, 0.6f, 0.55f, 0f, 0.55f, 0.3f, 0.7f, t * 1.7f + s.seed * 11f);
                        if (step == 0) f.Splat(0.5f, 0.2f, 0.1f, 2.2f, 1.2f, 0f, 0.7f, 2.2f, 0.4f, s.seed * 11f);
                        break;
                    }
            }
        }

        // =============================================================== rendu d'une frame
        static float[] Render(Fluid sim, Spec s, int size, float phase, float t)
        {
            var o = new float[size * size * 4];
            int N = sim.N; float inv = 1f / size;
            float detailT = t * 0.9f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * inv, v = (y + 0.5f) * inv;
                    // domain warp à deux échelles pour ajouter des volutes sous la résolution de la grille
                    var w1 = CurlWarp(u * 2.4f, v * 2.4f, detailT, s.seed);
                    var w2 = CurlWarp(u * 6.0f + 3f, v * 6.0f, detailT * 1.4f, s.seed + 3, 0.01f);
                    float k1 = s.kind == 0 ? 0.03f : 0.022f, k2 = 0.005f;
                    float wu = u + (w1.x * k1 + w2.x * k2) * 0.12f, wv = v + (w1.y * k1 + w2.y * k2) * 0.12f;
                    float gx = Mathf.Clamp(wu, 0f, 1f) * (N - 1), gy = Mathf.Clamp(wv, 0f, 1f) * (N - 1);
                    float d = sim.Sample(sim.d, gx, gy), T = sim.Sample(sim.T, gx, gy), vy = sim.Sample(sim.v, gx, gy);
                    float n = Fbm(u * 9f, v * 9f - detailT * 1.2f, s.seed + 1), n2 = Fbm(u * 19f + 4f, v * 19f - detailT * 2.2f, s.seed + 2);
                    float e = Edge(u, v);
                    int i = (y * size + x) * 4;
                    if (s.kind == 2)
                    {
                        float eps = 1.5f;
                        float dl = sim.Sample(sim.d, gx - eps, gy), dr = sim.Sample(sim.d, gx + eps, gy), db = sim.Sample(sim.d, gx, gy - eps), dt2 = sim.Sample(sim.d, gx, gy + eps);
                        float dens = Mathf.Clamp01(d * 1.15f);
                        dens = Mathf.Clamp01(dens * (0.55f + 0.9f * n) + (n2 - 0.5f) * 0.25f * Mathf.Min(1f, dens * 2f) - 0.04f);
                        float a = Smooth(0.03f, 0.55f, dens) * e;
                        Vector3 nm = new Vector3(-(dr - dl) * 5f + (n - 0.5f) * 0.5f, -(dt2 - db) * 5f + (n2 - 0.5f) * 0.4f, 1f);
                        nm.Normalize();
                        o[i] = nm.x * 0.5f + 0.5f; o[i + 1] = nm.y * 0.5f + 0.5f; o[i + 2] = Mathf.Clamp01(d) * e; o[i + 3] = a;
                    }
                    else
                    {
                        float dens = d, temp = T; dens = Mathf.Max(dens, temp * 0.7f);
                        float rag = s.kind == 0 ? 0.42f : 0.28f;
                        dens = Mathf.Clamp01(dens * 1.5f - 0.12f + (n - 0.5f) * rag * (1.1f - dens) + (n2 - 0.5f) * 0.28f * (1f - dens));
                        temp = Mathf.Clamp01(temp * (0.8f + 0.5f * n2));
                        float vn = Mathf.Clamp01(0.5f + vy * 0.5f);
                        o[i] = dens * e; o[i + 1] = temp * e; o[i + 2] = vn; o[i + 3] = Smooth(0.02f, 0.4f, dens) * e;
                    }
                }
            return o;
        }

        // =============================================================== étincelles (4x4)
        static void GenerateSparks()
        {
            int cols = 4, size = 128, atlas = cols * size;
            var px = new Color32[atlas * atlas];
            var rnd = new System.Random(7);
            for (int f = 0; f < cols * cols; f++)
            {
                int ox = (f % cols) * size, oy = (cols - 1 - f / cols) * size;
                float flick = 0.75f + 0.25f * (float)rnd.NextDouble(), len = 0.55f + 0.4f * (float)rnd.NextDouble(), curve = ((float)rnd.NextDouble() - 0.5f) * 0.18f;
                float star = f % 4 == 1 ? 0.8f : 0.25f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f; // v>0 = avant (+Y)
                        float cu = u - curve * v * v * Mathf.Sign(v);
                        float taper = v > 0f ? Mathf.Exp(-v * v * 38f) : Mathf.Exp(-Mathf.Pow(-v / len, 1.3f) * 3.2f);
                        float widthS = 0.05f + 0.09f * Mathf.Clamp01(1f + v);
                        float streak = Mathf.Exp(-cu * cu / (widthS * widthS)) * taper;
                        float head = Mathf.Exp(-(u * u + v * v) * 120f) * 1.4f;
                        float cross = star * (Mathf.Exp(-Mathf.Abs(u) * 40f) * Mathf.Exp(-v * v * 9f) + Mathf.Exp(-Mathf.Abs(v) * 40f) * Mathf.Exp(-u * u * 9f)) * 0.55f;
                        float a = Mathf.Clamp01((streak + head + cross) * flick);
                        float heat = Mathf.Clamp01(0.35f + 0.65f * Mathf.Clamp01(v + 0.4f) + head * 0.5f);
                        float edge = Edge((x + 0.5f) / size, (y + 0.5f) / size, 0.12f);
                        a *= edge;
                        px[(oy + y) * atlas + ox + x] = new Color32(To8(a), To8(heat * a), 0, To8(a));
                    }
            }
            WriteTexture("FB_Spark", atlas, px);
        }

        // =============================================================== rampe de corps noir (HDR via shader)
        static void GenerateRamp()
        {
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false, false);
            Color[] key = { new Color(0.02f, 0.004f, 0.002f), new Color(0.22f, 0.025f, 0.006f), new Color(0.62f, 0.07f, 0.01f), new Color(1f, 0.28f, 0.03f), new Color(1f, 0.58f, 0.1f), new Color(1f, 0.84f, 0.38f), new Color(1f, 0.96f, 0.82f) };
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f * (key.Length - 1); int a = Mathf.Min((int)t, key.Length - 2); float f = t - a;
                var c = Color.Lerp(key[a], key[a + 1], f * f * (3 - 2 * f)); c.a = 1f;
                tex.SetPixel(i, 0, c);
            }
            tex.Apply();
            string path = R + "/Textures/T_FireRamp.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.filterMode = FilterMode.Bilinear; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        [MenuItem("Tools/Fire VFX/Generate Flipbooks")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(R + "/Textures"); AssetDatabase.Refresh();
            try
            {
                GenerateRamp();
                GenerateSparks();
                SimulateAndSave(new Spec { name = "FB_Flame", grid = 192, frames = 64, frameSize = 256, seed = 1, loop = true, kind = 0, fps = 24f });
                SimulateAndSave(new Spec { name = "FB_FlameB", grid = 160, frames = 64, frameSize = 128, seed = 5, loop = true, kind = 0, fps = 24f });
                SimulateAndSave(new Spec { name = "FB_Explosion", grid = 192, frames = 64, frameSize = 256, seed = 2, loop = false, kind = 1, fps = 30f });
                SimulateAndSave(new Spec { name = "FB_ExplosionB", grid = 160, frames = 64, frameSize = 128, seed = 9, loop = false, kind = 1, fps = 30f });
                SimulateAndSave(new Spec { name = "FB_Smoke", grid = 192, frames = 64, frameSize = 256, seed = 3, loop = false, kind = 2, fps = 15f });
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh();
            Debug.Log("[FireVFX] Flipbooks générés dans " + R + "/Textures.");
        }

        /// Variante rapide (1 texture) pour itérer sur le rendu : Tools/Fire VFX/Generate Flipbooks (Flame only)
        [MenuItem("Tools/Fire VFX/Debug/Flame only")]
        public static void FlameOnly()
        {
            Directory.CreateDirectory(R + "/Textures"); AssetDatabase.Refresh();
            try { GenerateRamp(); SimulateAndSave(new Spec { name = "FB_Flame", grid = 192, frames = 64, frameSize = 256, seed = 1, loop = true, kind = 0, fps = 24f }); SimulateAndSave(new Spec { name = "FB_FlameB", grid = 160, frames = 64, frameSize = 128, seed = 5, loop = true, kind = 0, fps = 24f }); }
            finally { EditorUtility.ClearProgressBar(); }
        }
        /// Aperçu lisible d'un flipbook : 8 frames réparties, rampe appliquée, sur fond sombre. Écrit Temp/fb_preview.png.
        [MenuItem("Tools/Fire VFX/Debug/Preview flipbooks")]
        public static void Preview()
        {
            var ramp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false); ramp.LoadImage(File.ReadAllBytes(R + "/Textures/T_FireRamp.png")); ramp.wrapMode = TextureWrapMode.Clamp;
            string[] names = { "FB_Flame", "FB_Explosion", "FB_Smoke" };
            int cell = 256, per = 8; var outPx = new Color32[cell * per * cell * names.Length];
            int W = cell * per;
            for (int ni = 0; ni < names.Length; ni++)
            {
                string p = R + "/Textures/" + names[ni] + ".png";
                if (!File.Exists(p)) continue;
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); src.LoadImage(File.ReadAllBytes(p));
                int fs = src.width / 8;
                var px = src.GetPixels32(); int sw = src.width;
                for (int k = 0; k < per; k++)
                {
                    int f = k * 8 + (ni == 1 ? 0 : 0); f = Mathf.Min(63, k * 9);
                    int ox = (f % 8) * fs, oy = (7 - f / 8) * fs;
                    for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++)
                    {
                        var c = px[(oy + y * fs / cell) * sw + ox + x * fs / cell];
                        Color col;
                        if (ni < 2) { float dn = c.r / 255f, tm = c.g / 255f; var rc = ramp.GetPixelBilinear(Mathf.Clamp01(tm * dn * 1.4f + 0.1f), 0.5f); float a = Mathf.Clamp01(dn * 1.6f); col = new Color(rc.r, rc.g, rc.b) * (0.5f + 2.5f * tm) * a; }
                        else { float a = c.a / 255f; float nx = c.r / 127.5f - 1f, ny = c.g / 127.5f - 1f; float l = Mathf.Clamp01(0.3f + 0.7f * (nx * -0.5f + ny * 0.6f + 0.6f)); col = new Color(0.55f, 0.5f, 0.46f) * l * a; }
                        col = new Color(Mathf.Min(1f, col.r), Mathf.Min(1f, col.g), Mathf.Min(1f, col.b)); col.a = 1f;
                        col = col + new Color(0.03f, 0.03f, 0.05f);
                        outPx[(ni * cell + y) * W + k * cell + x] = col;
                    }
                }
                UnityEngine.Object.DestroyImmediate(src);
            }
            var t = new Texture2D(W, cell * names.Length, TextureFormat.RGBA32, false); t.SetPixels32(outPx); t.Apply();
            File.WriteAllBytes("Temp/fb_preview.png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
        }

        [MenuItem("Tools/Fire VFX/Debug/Explosion only")]
        public static void ExplosionOnly()
        {
            Directory.CreateDirectory(R + "/Textures"); AssetDatabase.Refresh();
            try { SimulateAndSave(new Spec { name = "FB_Explosion", grid = 192, frames = 64, frameSize = 256, seed = 2, loop = false, kind = 1, fps = 30f }); }
            finally { EditorUtility.ClearProgressBar(); }
        }
        [MenuItem("Tools/Fire VFX/Debug/Smoke only")]
        public static void SmokeOnly()
        {
            Directory.CreateDirectory(R + "/Textures"); AssetDatabase.Refresh();
            try { SimulateAndSave(new Spec { name = "FB_Smoke", grid = 192, frames = 64, frameSize = 256, seed = 3, loop = false, kind = 2, fps = 15f }); }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
#endif
