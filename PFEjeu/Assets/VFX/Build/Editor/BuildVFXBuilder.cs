#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildVFX.EditorTools
{
    /// Menus : Tools/Build VFX/Setup URP, Build All, Create Demo Caserne, Add BuildSite To Selection
    public static class BuildVFXBuilder
    {
        const string R = "Assets/VFX/Build";
        static readonly Color White = Color.white;

        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
        static int Wrap(int v, int p) { return ((v % p) + p) % p; }
        static float VN(float x, float y, int per)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = Wrap(xi, per), x1 = Wrap(xi + 1, per), y0 = Wrap(yi, per), y1 = Wrap(yi + 1, per);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0), Hash(x1, y0), fx), Mathf.Lerp(Hash(x0, y1), Hash(x1, y1), fx), fy);
        }
        static float FBM(float u, float v, int per, int oct = 5)
        {
            float a = 0.5f, s = 0, x = u * per, y = v * per; int p = per;
            for (int i = 0; i < oct; i++) { s += a * VN(x, y, p); x *= 2f; y *= 2f; p *= 2; a *= 0.5f; }
            return s;
        }
        static float Rad(float u, float v) { float dx = u - 0.5f, dy = v - 0.5f; return Mathf.Sqrt(dx * dx + dy * dy) * 2f; }

        static Texture2D Tex(string name, int n, Func<float, float, Color> f, bool srgb = true, bool repeat = false)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n, (y + 0.5f) / n);
            t.SetPixels32(px);
            string p = R + "/Textures/" + name + ".png";
            File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true; ti.sRGBTexture = srgb; ti.mipmapEnabled = true;
            ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        // R = bruit, G = masque de bouffée
        static Texture2D TexDust() => Tex("T_Dust", 256, (u, v) =>
        {
            float n = Mathf.Clamp01((FBM(u, v, 4) - 0.2f) * 1.7f);
            float nx = (u - 0.5f) * 2f, ny = (v - 0.5f) * 2f, d = Mathf.Sqrt(nx * nx + ny * ny) + (FBM(u + 0.31f, v + 0.17f, 3, 4) - 0.5f) * 0.55f;
            return new Color(n, Mathf.SmoothStep(1f, 0.15f, d), 0, 1);
        }, false);
        // u = angle (tileable), v = 0 intérieur → 1 bord extérieur
        static Texture2D TexRingBand() => Tex("T_RingBand", 256, (u, v) =>
        {
            float a = Mathf.Pow(v, 2.4f) * Mathf.SmoothStep(1f, 0.93f, v) * (0.6f + 0.8f * FBM(u, v * 0.3f, 8, 3));
            return new Color(1, 1, 1, Mathf.Clamp01(a));
        });
        static Texture2D TexStain() => Tex("T_Stain", 256, (u, v) =>
        {
            float r = Rad(u, v) + (FBM(u, v, 4, 4) - 0.5f) * 0.7f;
            return new Color(1, 1, 1, Mathf.SmoothStep(1f, 0.2f, r) * (0.5f + 0.5f * FBM(u + 0.4f, v, 6, 4)) * 0.85f);
        });
        static Texture2D TexMote() => Tex("T_Mote", 64, (u, v) => { float r = Rad(u, v); return new Color(1, 1, 1, Mathf.Clamp01(Mathf.Exp(-r * r * 12f)) * Mathf.Clamp01(1.2f - r)); });
        static Texture2D TexBrick() => Tex("T_Brick", 256, (u, v) =>
        {
            int row = Mathf.FloorToInt(v * 8f); float off = (row % 2) * 0.5f, bu = u * 4f + off; int col = Mathf.FloorToInt(bu);
            float bx = bu - col, by = v * 8f - row; bool mortar = bx < 0.05f || by < 0.09f;
            float g = FBM(u, v, 8, 4); float vr = 0.82f + 0.36f * Hash(row, Wrap(col, 4));
            Color c = mortar ? new Color(0.62f, 0.6f, 0.55f) : new Color(0.55f * vr, 0.3f * vr, 0.21f * vr);
            return c * (0.75f + 0.4f * g);
        }, true, true);

        static void Blend(Material m, bool additive)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); m.renderQueue = 3000;
        }
        static Material Std(string name, Texture2D t, bool additive, Color c, float intensity)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0); Blend(m, additive); m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a));
            m.SetFloat("_SoftParticlesEnabled", 1); m.EnableKeyword("_SOFTPARTICLES_ON"); m.SetFloat("_SoftParticlesNearFadeDistance", 0f); m.SetFloat("_SoftParticlesFarFadeDistance", 0.6f);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }
        static Material Lit(string name, Color c, float smooth, Texture2D t = null, Vector2? tiling = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            if (t) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }
        static Material DustMat()
        {
            var m = new Material(Shader.Find("BuildVFX/BuildDust")); m.SetTexture("_MainTex", TexDust()); m.renderQueue = 3000;
            AssetDatabase.CreateAsset(m, R + "/Materials/M_Dust.mat"); return m;
        }
        static Material DebrisMat()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit")); m.SetColor("_BaseColor", White); m.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(m, R + "/Materials/M_Debris.mat"); return m;
        }

        // ---------------------------------------------------------------- helpers particules
        static ParticleSystem.MinMaxCurve C(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static ParticleSystem PS(Transform parent, string name, Material mat, bool loop = false, float dur = 1f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            var m = ps.main; m.duration = dur; m.loop = loop; m.playOnAwake = true; m.startSpeed = 0f; m.maxParticles = 300;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var sh = ps.shape; sh.enabled = false; var em = ps.emission; em.rateOverTime = 0f;
            return ps;
        }
        static void Life(ParticleSystem ps, float a, float b) { var m = ps.main; m.startLifetime = C(a, b); }
        static void Size(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSize = C(a, b); }
        static void Speed(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSpeed = C(a, b); }
        static void Grav(ParticleSystem ps, float g) { var m = ps.main; m.gravityModifier = g; }
        static void Tone(ParticleSystem ps, float a, float b) { var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(new Color(a, a, a, 1), new Color(b, b, b, 1)); }
        static void Tone(ParticleSystem ps, Color a, Color b) { var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(a, b); }
        static void Max(ParticleSystem ps, int n) { var m = ps.main; m.maxParticles = n; }
        static void Rot(ParticleSystem ps, float r = 0.35f) { var m = ps.main; m.startRotation = C(-r, r); }
        static void Spin(ParticleSystem ps, float s) { var r = ps.rotationOverLifetime; r.enabled = true; r.z = C(-s, s); }
        static void Burst(ParticleSystem ps, int n, float t = 0f) { var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(t, (short)n) }); }
        static void Rate(ParticleSystem ps, float r) { var e = ps.emission; e.rateOverTime = r; }
        static void Grow(ParticleSystem ps, float s0, float s1)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, s0), new Keyframe(0.3f, s0 + (s1 - s0) * 0.78f), new Keyframe(1, s1)));
        }
        static void Alpha(ParticleSystem ps, float a, float fadeIn = 0.12f, float hold = 0.5f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, fadeIn), new GradientAlphaKey(a * 0.8f, hold), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }
        static void Noise(ParticleSystem ps, float str, float freq) { var n = ps.noise; n.enabled = true; n.strength = str; n.frequency = freq; n.scrollSpeed = 0.4f; n.quality = ParticleSystemNoiseQuality.Medium; }
        static void Drag(ParticleSystem ps, float limit, float dampen) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(limit, limit); lv.dampen = dampen; }
        static void Circle(ParticleSystem ps, float r, float thick, Vector3 rot) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = r; s.radiusThickness = thick; s.rotation = rot; }
        static void Cone(ParticleSystem ps, float angle, float r, float thick, Vector3 rot) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Cone; s.angle = angle; s.radius = r; s.radiusThickness = thick; s.rotation = rot; }
        static void Hemi(ParticleSystem ps, float r) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = r; }
        static void MeshR(ParticleSystem ps, params Mesh[] meshes)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.alignment = ParticleSystemRenderSpace.Local;
            if (meshes.Length > 1) r.SetMeshes(meshes); else r.mesh = meshes[0];
        }
        static void Tumble(ParticleSystem ps) { var m = ps.main; m.startRotation3D = true; m.startRotationX = C(0, 6.28f); m.startRotationY = C(0, 6.28f); m.startRotationZ = C(0, 6.28f); }
        static void World(ParticleSystem ps, float bounce = 0.3f) { var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.bounce = bounce; c.dampen = 0.5f; c.lifetimeLoss = 0.02f; c.radiusScale = 0.5f; }
        static void ShrinkEnd(ParticleSystem ps) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.75f, 1), new Keyframe(1, 0))); }
        static Mesh LoadMesh(string n) { foreach (var o in AssetDatabase.LoadAllAssetsAtPath(R + "/Meshes/" + n + ".obj")) if (o is Mesh m) return m; return null; }
        static Material LoadMat(string n) => AssetDatabase.LoadAssetAtPath<Material>(R + "/Materials/" + n + ".mat");

        [MenuItem("Tools/Build VFX/Setup URP")]
        public static void SetupURP()
        {
            int n = 0;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset; if (a == null) continue;
                a.supportsCameraDepthTexture = true; a.supportsHDR = true; EditorUtility.SetDirty(a); n++;
            }
            var d = GraphicsSettings.defaultRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (d != null) { d.supportsCameraDepthTexture = true; d.supportsHDR = true; EditorUtility.SetDirty(d); n++; }
            AssetDatabase.SaveAssets();
            Debug.Log(n == 0 ? "[BuildVFX] Aucun URP Asset trouvé." : "[BuildVFX] URP : Depth Texture + HDR activés (" + n + " asset(s)).");
        }

        [MenuItem("Tools/Build VFX/Build All")]
        public static void BuildAll()
        {
            foreach (var s in new[] { "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Particles/Lit", "Universal Render Pipeline/Lit", "BuildVFX/BuildDust", "BuildVFX/BuildReveal" })
                if (Shader.Find(s) == null) { Debug.LogError("[BuildVFX] Shader introuvable : " + s + " (URP installé ? erreur de compilation ?)"); return; }
            foreach (var d in new[] { "Textures", "Materials", "Prefabs", "Meshes" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();

            var tGlowMote = TexMote(); var tBand = TexRingBand(); var tStain = TexStain(); var tBrick = TexBrick();
            var mDust = DustMat(); var mDebris = DebrisMat();
            var mRingDust = Std("M_RingDust", tBand, false, new Color(1, 1, 1, 1), 1f);
            var mRingTeam = Std("M_RingTeam", tBand, true, White, 1f);
            var mStain = Std("M_Stain", tStain, false, White, 1f);
            var mMote = Std("M_Mote", tGlowMote, true, White, 1f);
            var mScaffold = Lit("M_Scaffold", new Color(0.46f, 0.33f, 0.2f), 0.08f);
            var mFoundation = Lit("M_Foundation", new Color(0.5f, 0.49f, 0.46f), 0.1f);
            Lit("M_DemoBrick", White, 0.05f, tBrick, new Vector2(3f, 1.5f)); Lit("M_DemoRoof", new Color(0.28f, 0.17f, 0.14f), 0.1f); Lit("M_DemoWood", new Color(0.2f, 0.12f, 0.07f), 0.1f);

            var ring = LoadMesh("BuildRing"); var disc = LoadMesh("BuildDisc");
            var debris = new System.Collections.Generic.List<Mesh>();
            foreach (var n in new[] { "Debris_Brick", "Debris_Stone", "Debris_Plank", "Debris_Shard" }) { var m = LoadMesh(n); if (m) debris.Add(m); }
            if (ring == null || disc == null || debris.Count == 0) { Debug.LogError("[BuildVFX] OBJ manquants dans " + R + "/Meshes (BuildRing, BuildDisc, Debris_*)."); return; }
            var stone = LoadMesh("Debris_Stone") ?? debris[0];
            Color lite = new Color(1f, 1f, 1f), mid = new Color(0.86f, 0.86f, 0.86f), dark = new Color(0.7f, 0.7f, 0.7f);

            // ============================ IMPACT (référence : rayon 6 m, sol = plan XZ) ============================
            var imp = new GameObject("BuildImpact"); var bi = imp.AddComponent<BuildImpact>();
            var lg = new GameObject("Light"); lg.transform.SetParent(imp.transform, false); lg.transform.localPosition = new Vector3(0, 3f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.intensity = 0f; l.range = 18f; l.shadows = LightShadows.None; bi.flash = l;
            var up = new Vector3(-90, 0, 0); var flat = new Vector3(90, 0, 0);

            var burst = PS(imp.transform, "DustBurst", mDust); burst.transform.localPosition = new Vector3(0, 0.2f, 0); Burst(burst, 30); Circle(burst, 5.2f, 0.12f, flat); Max(burst, 40);
            Life(burst, 2.6f, 4.2f); Speed(burst, 5f, 9f); Size(burst, 2f, 3.4f); Rot(burst); Spin(burst, 0.2f); Grow(burst, 0.45f, 1.6f); Noise(burst, 0.5f, 0.35f); Drag(burst, 1.4f, 0.1f); Grav(burst, -0.02f); Tone(burst, mid, lite); Alpha(burst, 0.8f);

            var low = PS(imp.transform, "DustLow", mDust); low.transform.localPosition = new Vector3(0, 0.3f, 0); Burst(low, 22, 0.05f); Circle(low, 3.5f, 1f, flat); Max(low, 30);
            Life(low, 4f, 6.5f); Speed(low, 1f, 4f); Size(low, 3f, 5f); Rot(low); Grow(low, 0.5f, 1.5f); Noise(low, 0.4f, 0.25f); Drag(low, 1f, 0.08f); Tone(low, dark, mid); Alpha(low, 0.6f, 0.15f, 0.55f);

            var col = PS(imp.transform, "DustColumn", mDust); col.transform.localPosition = new Vector3(0, 0.3f, 0); Burst(col, 16, 0.1f); Cone(col, 14f, 5.2f, 0.15f, up); Max(col, 24);
            Life(col, 3.5f, 5.5f); Speed(col, 3f, 7f); Size(col, 2.4f, 4f); Rot(col); Grow(col, 0.4f, 1.5f); Noise(col, 0.45f, 0.3f); Drag(col, 2.2f, 0.08f); Grav(col, -0.03f); Tone(col, mid, lite); Alpha(col, 0.65f, 0.15f, 0.5f);

            var perim = PS(imp.transform, "PerimeterDust", mDust); perim.transform.localPosition = new Vector3(0, 0.2f, 0); Burst(perim, 18, 0.3f); Circle(perim, 7f, 0.15f, flat); Max(perim, 24);
            Life(perim, 2f, 3.5f); Speed(perim, 2f, 5f); Size(perim, 1.4f, 2.4f); Rot(perim); Grow(perim, 0.5f, 1.5f); Noise(perim, 0.4f, 0.4f); Drag(perim, 1.2f, 0.1f); Tone(perim, mid, lite); Alpha(perim, 0.6f);

            var air = PS(imp.transform, "AirDust", mDust); Burst(air, 70, 0.15f); Hemi(air, 5f); Max(air, 90);
            Life(air, 3f, 7f); Speed(air, 0.3f, 2.5f); Size(air, 0.35f, 0.9f); Rot(air, 3f); Noise(air, 0.6f, 0.5f); Grav(air, -0.01f); Tone(air, mid, lite); Alpha(air, 0.38f, 0.1f, 0.6f);

            var rd = PS(imp.transform, "ShockwaveDust", mRingDust); rd.transform.localPosition = new Vector3(0, 0.12f, 0); MeshR(rd, ring); Burst(rd, 1); Max(rd, 2); Life(rd, 1.0f, 1.0f); Size(rd, 11f, 11f);
            { var s = rd.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.35f, 0.78f), new Keyframe(1, 1f))); }
            Tone(rd, lite, lite); Alpha(rd, 0.6f, 0.05f, 0.4f);

            var rt = PS(imp.transform, "ShockwaveTeam", mRingTeam); rt.transform.localPosition = new Vector3(0, 0.14f, 0); MeshR(rt, ring); Burst(rt, 1, 0.04f); Max(rt, 2); Life(rt, 0.75f, 0.75f); Size(rt, 8.5f, 8.5f);
            { var s = rt.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.25f), new Keyframe(0.3f, 0.8f), new Keyframe(1, 1f))); }
            Tone(rt, White, White); Alpha(rt, 1f, 0.05f, 0.35f);

            var db = PS(imp.transform, "Debris", mDebris); db.transform.localPosition = new Vector3(0, 0.3f, 0); MeshR(db, debris.ToArray()); Tumble(db); Burst(db, 20); Cone(db, 38f, 4.5f, 0.2f, up); Max(db, 24);
            Life(db, 2.2f, 3.6f); Speed(db, 6f, 13f); Size(db, 0.22f, 0.55f); Grav(db, 2.4f); World(db); ShrinkEnd(db); Tone(db, new Color(0.62f, 0.58f, 0.52f), new Color(0.95f, 0.92f, 0.86f));

            var ch = PS(imp.transform, "Chips", mDebris); ch.transform.localPosition = new Vector3(0, 0.3f, 0); MeshR(ch, stone, debris[0]); Tumble(ch); Burst(ch, 70); Cone(ch, 55f, 4.5f, 0.3f, up); Max(ch, 80);
            Life(ch, 1.2f, 2.4f); Speed(ch, 7f, 18f); Size(ch, 0.05f, 0.15f); Grav(ch, 2.8f); World(ch, 0.35f); ShrinkEnd(ch); Tone(ch, new Color(0.55f, 0.5f, 0.45f), new Color(0.9f, 0.86f, 0.8f));

            var st = PS(imp.transform, "DustStain", mStain); st.transform.localPosition = new Vector3(0, 0.03f, 0); MeshR(st, disc); Burst(st, 1, 0.05f); Max(st, 2); Life(st, 7f, 7f); Size(st, 7.5f, 7.5f); Grow(st, 0.7f, 1f);
            Tone(st, mid, mid);
            { var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.55f, 0.08f), new GradientAlphaKey(0.4f, 0.6f), new GradientAlphaKey(0, 1) }); var c = st.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g); }

            bi.dust = new[] { burst, low, col, perim, air, rd, st }; bi.team = new[] { rt };
            PrefabUtility.SaveAsPrefabAsset(imp, R + "/Prefabs/BuildImpact.prefab"); UnityEngine.Object.DestroyImmediate(imp);

            // ============================ CONSTRUCTION (poussière au sol + particules sur le front) ============================
            var cf = new GameObject("BuildConstructFX"); var bc = cf.AddComponent<BuildConstructFX>();
            var bd = PS(cf.transform, "BaseDust", mDust, true, 5f); bd.transform.localRotation = Quaternion.identity;
            { var s = bd.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Rectangle; s.rotation = up; s.scale = new Vector3(12, 9, 1); }
            Rate(bd, 14f); Life(bd, 2.5f, 4f); Speed(bd, 0.2f, 0.9f); Size(bd, 1.2f, 2.2f); Rot(bd); Grow(bd, 0.4f, 1.4f); Noise(bd, 0.4f, 0.4f); Grav(bd, -0.01f); Max(bd, 160); Tone(bd, dark, mid); Alpha(bd, 0.45f, 0.2f, 0.5f);
            var mo = PS(cf.transform, "Motes", mMote, true, 5f);
            { var s = mo.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Rectangle; s.rotation = up; s.scale = new Vector3(12, 9, 1); }
            Rate(mo, 60f); Life(mo, 1f, 2.2f); Speed(mo, 0.3f, 1.2f); Size(mo, 0.05f, 0.12f); Noise(mo, 0.5f, 0.6f); Grav(mo, -0.02f); Max(mo, 400); Tone(mo, White, White); Alpha(mo, 1f, 0.1f, 0.5f);
            bc.baseDust = bd; bc.motes = mo;
            PrefabUtility.SaveAsPrefabAsset(cf, R + "/Prefabs/BuildConstructFX.prefab"); UnityEngine.Object.DestroyImmediate(cf);

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[BuildVFX] OK. Lance Setup URP, puis Create Demo Caserne (ou Add BuildSite To Selection).");
        }

        // ---------------------------------------------------------------- câblage / démo
        public static void Wire(BuildSite s)
        {
            s.revealShader = Shader.Find("BuildVFX/BuildReveal");
            s.constructFx = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/BuildConstructFX.prefab");
            s.impactFx = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/BuildImpact.prefab");
            s.poleMesh = LoadMesh("Scaffold_Pole"); s.plankMesh = LoadMesh("Scaffold_Plank"); s.foundationMesh = LoadMesh("BuildFoundation");
            s.scaffoldMat = LoadMat("M_Scaffold"); s.foundationMat = LoadMat("M_Foundation");
            EditorUtility.SetDirty(s);
        }

        [MenuItem("Tools/Build VFX/Add BuildSite To Selection")]
        public static void AddToSelection()
        {
            foreach (var go in Selection.gameObjects) { var s = go.GetComponent<BuildSite>() ?? Undo.AddComponent<BuildSite>(go); Wire(s); }
        }

        static GameObject Part(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler; g.GetComponent<MeshRenderer>().sharedMaterial = m; return g;
        }

        [MenuItem("Tools/Build VFX/Create Demo Caserne")]
        public static void CreateDemo()
        {
            var brick = LoadMat("M_DemoBrick"); var roof = LoadMat("M_DemoRoof"); var wood = LoadMat("M_DemoWood");
            if (!brick) { Debug.LogError("[BuildVFX] Lance d'abord Tools/Build VFX/Build All."); return; }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "DemoGround"; ground.transform.localScale = Vector3.one * 10f;
            ground.GetComponent<MeshRenderer>().sharedMaterial = Lit("M_DemoGround", new Color(0.36f, 0.4f, 0.3f), 0.05f);
            var root = new GameObject("DemoCaserne"); var t = root.transform;
            Part(t, "Body", new Vector3(0, 2.5f, 0), new Vector3(12, 5, 9), Vector3.zero, brick);
            Part(t, "RoofL", new Vector3(-3.0f, 5.7f, 0), new Vector3(6.8f, 0.35f, 9.8f), new Vector3(0, 0, 28), roof);
            Part(t, "RoofR", new Vector3(3.0f, 5.7f, 0), new Vector3(6.8f, 0.35f, 9.8f), new Vector3(0, 0, -28), roof);
            Part(t, "Tower", new Vector3(4.8f, 4.5f, -3.2f), new Vector3(3, 9, 3), Vector3.zero, brick);
            Part(t, "Door", new Vector3(0, 1.3f, 4.55f), new Vector3(2.2f, 2.6f, 0.2f), Vector3.zero, wood);
            var site = root.AddComponent<BuildSite>(); Wire(site);
            var cam = Camera.main;
            if (cam) { if (!cam.GetComponent<BuildCameraShake>()) cam.gameObject.AddComponent<BuildCameraShake>(); cam.transform.position = new Vector3(0, 4.5f, -24f); cam.transform.LookAt(new Vector3(0, 4f, 0)); }
            var demo = root.AddComponent<BuildDemo>(); demo.target = site;
            Selection.activeGameObject = root;
            Debug.Log("[BuildVFX] Démo créée. Play, puis B = construire, T = couleur d'équipe.");
        }
    }
}
#endif
