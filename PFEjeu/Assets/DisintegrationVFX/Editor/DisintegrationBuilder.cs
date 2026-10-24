using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core.DisintegrationVFX.Editor
{
    /// <summary>Génère textures, meshes, matériaux, styles, prefab FX et config.</summary>
    public static class DisintegrationBuilder
    {
        public const string Root = "Assets/DisintegrationVFX";
        private const string Tex = Root + "/Textures";
        private const string Mat = Root + "/Materials";
        private const string Sty = Root + "/Styles";
        private const string Mesh = Root + "/Meshes";

        [MenuItem("Tools/Disintegration VFX/Rebuild Everything")]
        public static void RebuildEverything()
        {
            GenerateTextures();
            BuildMeshes();
            BuildMaterials();
            BuildStyles();
            BuildPrefab();
            BuildConfig();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Disintegration] Rebuild terminé.");
        }

        // ================================================================ TEXTURES

        [MenuItem("Tools/Disintegration VFX/Generate Textures")]
        public static void GenerateTextures()
        {
            Directory.CreateDirectory(Tex);
            const int N = 512;

            // --- T_BurnNoise : R perlin 2 octaves, G worley, B perlin (autre graine)
            var noise = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float p = TilePerlin(u, v, 6, 1) * 0.65f + TilePerlin(u, v, 13, 2) * 0.35f;
                float w = TileWorley(u, v, 7, 3);
                float p2 = TilePerlin(u, v, 9, 5) * 0.6f + TilePerlin(u, v, 21, 6) * 0.4f;
                float r = Mathf.Clamp01(p * 0.65f + (1f - w) * 0.35f);
                noise[y * N + x] = new Color(r, Mathf.Clamp01(1f - w), p2, 1f);
            }
            Save("T_BurnNoise", N, N, noise, false, TextureWrapMode.Repeat);

            // --- T_BurnCracks : 1 = matière intacte, 0 = fissure
            var cracks = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // bord de cellule Voronoi tordu par un bruit => fissures irrégulières
                float wu = u + (TilePerlin(u, v, 8, 11) - 0.5f) * 0.05f;
                float wv = v + (TilePerlin(u, v, 8, 12) - 0.5f) * 0.05f;
                Worley2(wu, wv, 9, 7, out float d1, out float d2);
                float edge = d2 - d1;
                float c = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / 0.07f));
                cracks[y * N + x] = new Color(c, c, c, 1f);
            }
            Save("T_BurnCracks", N, N, cracks, false, TextureWrapMode.Repeat);

            // --- Ramps 256x1 (blanc -> jaune -> orange -> rouge -> noir)
            SaveRamp("T_BurnRamp", Color.white, new Color(1f, 0.92f, 0.35f), new Color(1f, 0.5f, 0.06f), new Color(0.75f, 0.1f, 0.02f), new Color(0.03f, 0.01f, 0f));
            SaveRamp("T_BurnRamp_Spectral", Color.white, new Color(0.6f, 1f, 1f), new Color(0.1f, 0.8f, 1f), new Color(0.05f, 0.25f, 0.8f), Color.black);
            SaveRamp("T_BurnRamp_Void", Color.white, new Color(1f, 0.55f, 1f), new Color(0.65f, 0.15f, 1f), new Color(0.25f, 0.03f, 0.55f), Color.black);
            SaveRamp("T_BurnRamp_Sand", Color.white, new Color(1f, 0.95f, 0.55f), new Color(0.95f, 0.75f, 0.25f), new Color(0.6f, 0.4f, 0.12f), new Color(0.12f, 0.08f, 0.03f));
            SaveRamp("T_BurnRamp_Ice", Color.white, new Color(0.8f, 0.97f, 1f), new Color(0.45f, 0.8f, 1f), new Color(0.15f, 0.4f, 0.85f), new Color(0.02f, 0.05f, 0.15f));

            // --- T_AshFlake 64 : flocon irrégulier
            const int A = 64;
            var ash = new Color[A * A];
            for (int y = 0; y < A; y++)
            for (int x = 0; x < A; x++)
            {
                float dx = (x - A / 2f) / (A / 2f), dy = (y - A / 2f) / (A / 2f);
                float ang = Mathf.Atan2(dy, dx);
                float rad = Mathf.Sqrt(dx * dx + dy * dy);
                float lim = 0.55f + 0.25f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.7f + 3f, Mathf.Sin(ang) * 1.7f + 3f)
                                  + 0.15f * Mathf.PerlinNoise(Mathf.Cos(ang) * 5f + 9f, Mathf.Sin(ang) * 5f + 9f);
                float a = Mathf.Clamp01((lim - rad) / 0.12f);
                float g = 0.7f + 0.3f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                ash[y * A + x] = new Color(g, g, g, a);
            }
            Save("T_AshFlake", A, A, ash, true, TextureWrapMode.Clamp);

            // --- T_Ember 64 : point doux
            var ember = new Color[A * A];
            for (int y = 0; y < A; y++)
            for (int x = 0; x < A; x++)
            {
                float dx = (x - A / 2f) / (A / 2f), dy = (y - A / 2f) / (A / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                float core = Mathf.Clamp01(1f - r * 2.2f);
                ember[y * A + x] = new Color(1f, Mathf.Lerp(0.6f, 1f, core), Mathf.Lerp(0.3f, 1f, core), a);
            }
            Save("T_Ember", A, A, ember, true, TextureWrapMode.Clamp);

            // --- T_Scorch 256 : tache irrégulière, bord noir déchiqueté
            const int S = 256;
            var sc = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                float ang = Mathf.Atan2(dy, dx);
                float rad = Mathf.Sqrt(dx * dx + dy * dy);
                float lim = 0.62f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 7f, Mathf.Sin(ang) * 2.2f + 7f)
                                  + 0.1f * Mathf.PerlinNoise(Mathf.Cos(ang) * 7f + 1f, Mathf.Sin(ang) * 7f + 1f)
                                  + 0.06f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                float a = Mathf.Clamp01((lim - rad) / 0.18f);
                float inner = Mathf.Clamp01(1f - rad / lim);
                float grain = 0.6f + 0.4f * Mathf.PerlinNoise(x * 0.09f + 4f, y * 0.09f + 4f);
                float alpha = a * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(inner * 2f)) * grain;
                sc[y * S + x] = new Color(0.02f, 0.015f, 0.012f, alpha);
            }
            Save("T_Scorch", S, S, sc, true, TextureWrapMode.Clamp);

            // --- T_SmokeFlipbook 8x8 (64 frames de 64px), non bouclé : apparition, expansion, dissolution
            const int F = 64, G = 8, T = F * G;
            var smoke = new Color[T * T];
            for (int f = 0; f < G * G; f++)
            {
                int cx = f % G, cy = G - 1 - f / G;
                float t = f / (float)(G * G - 1);
                float grow = Mathf.Lerp(0.55f, 1f, t);
                float life = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.85f + 0.1f);
                float fade = Mathf.Clamp01(life) * (1f - Mathf.SmoothStep(0.65f, 1f, t));
                for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    float dx = (x - F / 2f) / (F / 2f) / grow, dy = (y - F / 2f) / (F / 2f) / grow;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float nz = Mathf.PerlinNoise(x * 0.11f + t * 3f + f * 0.02f, y * 0.11f + 20f) * 0.6f
                             + Mathf.PerlinNoise(x * 0.25f - t * 2f, y * 0.25f + 40f) * 0.4f;
                    float a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01(nz * 1.5f - 0.2f);
                    a = Mathf.Clamp01(a * fade * 1.3f);
                    float g = 0.55f + 0.45f * nz;
                    smoke[(cy * F + y) * T + cx * F + x] = new Color(g, g, g, a);
                }
            }
            Save("T_SmokeFlipbook", T, T, smoke, true, TextureWrapMode.Clamp);

            AssetDatabase.Refresh();
        }

        private static void Save(string name, int w, int h, Color[] px, bool srgb, TextureWrapMode wrap)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            t.SetPixels(px);
            t.Apply();
            string path = Tex + "/" + name + ".png";
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = srgb;
            ti.wrapMode = wrap;
            ti.mipmapEnabled = w > 8 && h > 8;
            ti.alphaIsTransparency = srgb;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }

        private static void SaveRamp(string name, Color c0, Color c1, Color c2, Color c3, Color c4)
        {
            var px = new Color[256];
            Color[] k = { c0, c1, c2, c3, c4 };
            for (int i = 0; i < 256; i++)
            {
                float f = i / 255f * 4f;
                int a = Mathf.Min(3, (int)f);
                px[i] = Color.Lerp(k[a], k[a + 1], f - a);
            }
            Save(name, 256, 1, px, true, TextureWrapMode.Clamp);
        }

        // --- bruits tileables
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static float TilePerlin(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0 % period, y0 % period, seed), b = Hash((x0 + 1) % period, y0 % period, seed);
            float c = Hash(x0 % period, (y0 + 1) % period, seed), d = Hash((x0 + 1) % period, (y0 + 1) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static void Worley2(float u, float v, int cells, int seed, out float d1, out float d2)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            d1 = d2 = 10f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 17);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y)) / cells;
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) d2 = d;
            }
        }

        private static float TileWorley(float u, float v, int cells, int seed)
        {
            Worley2(u, v, cells, seed, out float d1, out _);
            return Mathf.Clamp01(d1 * cells * 1.1f);
        }

        // ================================================================ MESHES

        public static void BuildMeshes()
        {
            Directory.CreateDirectory(Mesh);
            for (int v = 0; v < 3; v++)
            {
                var rnd = new System.Random(100 + v);
                int pts = 7 + v;
                var verts = new System.Collections.Generic.List<Vector3>();
                var uvs = new System.Collections.Generic.List<Vector2>();
                var tris = new System.Collections.Generic.List<int>();
                verts.Add(Vector3.zero); uvs.Add(new Vector2(0.5f, 0f));
                for (int i = 0; i < pts; i++)
                {
                    float a = (i + (float)rnd.NextDouble() * 0.5f) / pts * Mathf.PI * 2f;
                    float r = 0.35f + (float)rnd.NextDouble() * 0.2f;
                    float x = Mathf.Cos(a) * r, y = Mathf.Sin(a) * r * 0.8f;
                    // recourbé comme du papier brûlé
                    float z = (x * x + y * y) * 1.4f * (v == 1 ? -1f : 1f);
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2((Mathf.Cos(a) + 1f) * 0.5f, 1f));
                }
                for (int i = 0; i < pts; i++)
                {
                    tris.Add(0); tris.Add(1 + i); tris.Add(1 + (i + 1) % pts);
                }
                var m = new UnityEngine.Mesh { name = "paper_scrap_" + (v + 1) };
                m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                string p = Mesh + "/paper_scrap_" + (v + 1) + ".asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(m, p);
            }
        }

        // ================================================================ MATERIALS

        private static Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + n + ".png");

        private static Material MakeMat(string name, Shader sh)
        {
            Directory.CreateDirectory(Mat);
            string p = Mat + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, p);
            }
            m.shader = sh;
            return m;
        }

        public static void BuildMaterials()
        {
            Shader dis = Shader.Find("VFX/Disintegrate");
            Shader par = Shader.Find("VFX/DisintegrateParticle");

            Material d = MakeMat("Mat_Disintegrate", dis);
            d.SetTexture("_BurnNoise", T("T_BurnNoise"));
            d.SetTexture("_BurnCracks", T("T_BurnCracks"));
            d.SetTexture("_BurnRamp", T("T_BurnRamp"));
            EditorUtility.SetDirty(d);

            Material scrap = MakeMat("Mat_Scrap", par);
            scrap.SetTexture("_MainTex", T("T_BurnNoise"));
            scrap.SetFloat("_Mode", 1); scrap.SetFloat("_Add", 0);
            scrap.SetFloat("_SrcBlend", 1); scrap.SetFloat("_DstBlend", 0);
            scrap.SetColor("_Tint", new Color(0.1f, 0.08f, 0.07f, 1f));
            scrap.SetColor("_HotColor", new Color(6f, 2.5f, 0.6f, 1f));
            scrap.renderQueue = 2450;
            EditorUtility.SetDirty(scrap);

            Material ash = MakeMat("Mat_Ash", par);
            ash.SetTexture("_MainTex", T("T_AshFlake"));
            ash.SetFloat("_Mode", 0); ash.SetFloat("_Add", 0);
            ash.SetFloat("_SrcBlend", 5); ash.SetFloat("_DstBlend", 10);
            ash.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(ash);

            Material emb = MakeMat("Mat_Ember", par);
            emb.SetTexture("_MainTex", T("T_Ember"));
            emb.SetFloat("_Mode", 0); emb.SetFloat("_Add", 1);
            emb.SetFloat("_SrcBlend", 1); emb.SetFloat("_DstBlend", 1);
            emb.SetColor("_Tint", new Color(5f, 5f, 5f, 1f));
            EditorUtility.SetDirty(emb);

            Material smk = MakeMat("Mat_Smoke", par);
            smk.SetTexture("_MainTex", T("T_SmokeFlipbook"));
            smk.SetFloat("_Mode", 0); smk.SetFloat("_Add", 0);
            smk.SetFloat("_SrcBlend", 5); smk.SetFloat("_DstBlend", 10);
            smk.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(smk);

            Material scorch = MakeMat("Mat_Scorch", par);
            scorch.SetTexture("_MainTex", T("T_Scorch"));
            scorch.SetFloat("_Mode", 0); scorch.SetFloat("_Add", 0);
            scorch.SetFloat("_SrcBlend", 5); scorch.SetFloat("_DstBlend", 10);
            scorch.SetColor("_Tint", Color.white);
            scorch.renderQueue = 2500;
            EditorUtility.SetDirty(scorch);
        }

        // ================================================================ STYLES

        private static DisintegrationStyle Style(string name)
        {
            Directory.CreateDirectory(Sty);
            string p = Sty + "/Style_" + name + ".asset";
            var s = AssetDatabase.LoadAssetAtPath<DisintegrationStyle>(p);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<DisintegrationStyle>();
                AssetDatabase.CreateAsset(s, p);
            }
            return s;
        }

        public static void BuildStyles()
        {
            var fire = Style("PaperFire");
            fire.ramp = T("T_BurnRamp");
            EditorUtility.SetDirty(fire);

            var spec = Style("Spectral");
            spec.ramp = T("T_BurnRamp_Spectral");
            spec.charColor = new Color(0.02f, 0.05f, 0.08f);
            spec.flashColor = new Color(1.5f, 3.5f, 4f);
            spec.motion = ParticleMotion.Rise; spec.speed = 1.7f; spec.gravity = -0.05f;
            spec.smokeCount = 0; spec.curl = 0.02f; spec.noiseAmp = 0.3f;
            spec.scrapColor = new Color(0.04f, 0.1f, 0.14f); spec.scrapHot = new Color(0.8f, 3.5f, 5f);
            spec.ashColor = new Color(0.4f, 0.9f, 1f); spec.emberColor = new Color(0.5f, 1f, 1f); spec.emberColorEnd = new Color(0.1f, 0.4f, 1f);
            spec.lightColor = new Color(0.2f, 0.8f, 1f);
            EditorUtility.SetDirty(spec);

            var vo = Style("Void");
            vo.ramp = T("T_BurnRamp_Void");
            vo.charColor = new Color(0.03f, 0.01f, 0.06f);
            vo.flashColor = new Color(3f, 1f, 4f);
            vo.motion = ParticleMotion.Inward; vo.speed = 1.1f; vo.gravity = 0f; vo.smokeCount = 0;
            vo.scrapColor = new Color(0.06f, 0.02f, 0.1f); vo.scrapHot = new Color(3.5f, 0.8f, 5f);
            vo.ashColor = new Color(0.55f, 0.25f, 0.8f); vo.emberColor = new Color(1f, 0.5f, 1f); vo.emberColorEnd = new Color(0.4f, 0.05f, 0.8f);
            vo.lightColor = new Color(0.65f, 0.2f, 1f);
            EditorUtility.SetDirty(vo);

            var sand = Style("Sand");
            sand.ramp = T("T_BurnRamp_Sand");
            sand.charColor = new Color(0.32f, 0.22f, 0.1f);
            sand.flashColor = new Color(3.5f, 2.8f, 1.2f);
            sand.motion = ParticleMotion.Fall; sand.speed = 0.8f; sand.gravity = 0.7f;
            sand.noiseScale = 3f; sand.edgeWidth = 0.1f; sand.charWidth = 0.2f; sand.curl = 0f; sand.edgeIntensity = 3f;
            sand.smokeCount = 5; sand.smokeColor = new Color(0.55f, 0.45f, 0.28f);
            sand.scrapColor = new Color(0.5f, 0.38f, 0.18f); sand.scrapHot = new Color(2.5f, 1.8f, 0.5f);
            sand.ashColor = new Color(0.85f, 0.7f, 0.4f); sand.emberColor = new Color(1f, 0.9f, 0.4f); sand.emberColorEnd = new Color(0.7f, 0.5f, 0.15f);
            sand.ashCount = 220; sand.lightColor = new Color(1f, 0.8f, 0.35f); sand.lightIntensity = 0.8f;
            EditorUtility.SetDirty(sand);

            var ice = Style("Ice");
            ice.ramp = T("T_BurnRamp_Ice");
            ice.charColor = new Color(0.12f, 0.22f, 0.32f);
            ice.flashColor = new Color(2.5f, 3.5f, 4f);
            ice.motion = ParticleMotion.Shatter; ice.speed = 1.3f; ice.gravity = 1.1f;
            ice.curl = 0.01f; ice.edgeWidth = 0.05f; ice.edgeIntensity = 4f; ice.smokeCount = 0; ice.noiseScale = 2.4f;
            ice.scrapColor = new Color(0.35f, 0.6f, 0.8f); ice.scrapHot = new Color(1.5f, 3f, 4f);
            ice.ashColor = new Color(0.8f, 0.95f, 1f); ice.emberColor = new Color(0.8f, 1f, 1f); ice.emberColorEnd = new Color(0.3f, 0.7f, 1f);
            ice.lightColor = new Color(0.55f, 0.85f, 1f); ice.lightIntensity = 1f;
            EditorUtility.SetDirty(ice);
        }

        // ================================================================ PREFAB

        private static ParticleSystem MakePS(Transform parent, string name, Material m, int max, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            main.startLifetime = 1.5f;
            main.startSize = 0.1f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            return ps;
        }

        public static void BuildPrefab()
        {
            Directory.CreateDirectory(Root + "/Prefabs");
            var root = new GameObject("DisintegrationFX");

            Material mScrap = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Scrap.mat");
            Material mAsh = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Ash.mat");
            Material mEmber = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Ember.mat");
            Material mSmoke = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Smoke.mat");
            Material mScorch = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Scorch.mat");

            // a. éclats de papier
            ParticleSystem scraps = MakePS(root.transform, "Scraps", mScrap, 30, ParticleSystemRenderMode.Mesh);
            var sr = scraps.GetComponent<ParticleSystemRenderer>();
            sr.SetMeshes(new[]
            {
                AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Mesh + "/paper_scrap_1.asset"),
                AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Mesh + "/paper_scrap_2.asset"),
                AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Mesh + "/paper_scrap_3.asset"),
            });
            var sm = scraps.main; sm.startRotation3D = true;
            var sd = scraps.limitVelocityOverLifetime; sd.enabled = true; sd.dampen = 0.08f; sd.limit = 2f;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 0.45f), new GradientColorKey(Color.black, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            var sc = scraps.colorOverLifetime; sc.color = sg;

            // b. cendres
            ParticleSystem ash = MakePS(root.transform, "Ash", mAsh, 120, ParticleSystemRenderMode.Billboard);
            var an = ash.noise; an.enabled = true; an.strength = 0.5f; an.frequency = 0.7f; an.scrollSpeed = 0.3f; an.quality = ParticleSystemNoiseQuality.Low;
            var al = ash.limitVelocityOverLifetime; al.enabled = true; al.dampen = 0.12f; al.limit = 1.2f;
            var asz = ash.sizeOverLifetime; asz.enabled = true; asz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.5f));

            // c. braises
            ParticleSystem emb = MakePS(root.transform, "Embers", mEmber, 60, ParticleSystemRenderMode.Stretch);
            var er = emb.GetComponent<ParticleSystemRenderer>(); er.velocityScale = 0.06f; er.lengthScale = 1.8f;
            var en = emb.noise; en.enabled = true; en.strength = 0.6f; en.frequency = 1.4f; en.scrollSpeed = 1f; en.quality = ParticleSystemNoiseQuality.Low;
            var esz = emb.sizeOverLifetime; esz.enabled = true; esz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));

            // d. fumée
            ParticleSystem smk = MakePS(root.transform, "Smoke", mSmoke, 10, ParticleSystemRenderMode.Billboard);
            var ts = smk.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 8; ts.numTilesY = 8;
            ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            ts.cycleCount = 1;
            var ssz = smk.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 2.4f));
            var sn = smk.noise; sn.enabled = true; sn.strength = 0.2f; sn.frequency = 0.4f; sn.quality = ParticleSystemNoiseQuality.Low;

            // e. lueur
            var lgo = new GameObject("Glow");
            lgo.transform.SetParent(root.transform, false);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.12f);
            light.range = 3f; light.intensity = 0f; light.shadows = LightShadows.None;

            // f. tache de brûlure
            var qgo = new GameObject("Scorch");
            qgo.transform.SetParent(root.transform, false);
            var mf = qgo.AddComponent<MeshFilter>();
            mf.sharedMesh = Resources.GetBuiltinResource<UnityEngine.Mesh>("Quad.fbx");
            var mr = qgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mScorch;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var fx = root.AddComponent<DisintegrationFX>();
            var so = new SerializedObject(fx);
            so.FindProperty("scraps").objectReferenceValue = scraps;
            so.FindProperty("ash").objectReferenceValue = ash;
            so.FindProperty("embers").objectReferenceValue = emb;
            so.FindProperty("smoke").objectReferenceValue = smk;
            so.FindProperty("glow").objectReferenceValue = light;
            so.FindProperty("scorch").objectReferenceValue = mr;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/DisintegrationFX.prefab");
            Object.DestroyImmediate(root);
        }

        // ================================================================ CONFIG

        public static void BuildConfig()
        {
            Directory.CreateDirectory(Root + "/Resources");
            string p = Root + "/Resources/DisintegrationConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<DisintegrationConfig>(p);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<DisintegrationConfig>();
                AssetDatabase.CreateAsset(cfg, p);
            }
            cfg.fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/DisintegrationFX.prefab").GetComponent<DisintegrationFX>();
            cfg.template = AssetDatabase.LoadAssetAtPath<Material>(Mat + "/Mat_Disintegrate.mat");
            cfg.defaultStyle = AssetDatabase.LoadAssetAtPath<DisintegrationStyle>(Sty + "/Style_PaperFire.asset");
            EditorUtility.SetDirty(cfg);
        }
    }
}
