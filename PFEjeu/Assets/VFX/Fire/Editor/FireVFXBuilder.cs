#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireVFX.EditorTools
{
    /// Menus : Tools/Fire VFX/Setup URP  puis  Tools/Fire VFX/Build All
    public static partial class FireVFXBuilder
    {
        const string R = "Assets/VFX/Fire";
        static readonly Color White = Color.white;

        // ---------------------------------------------------------------- bruit
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
        static int Wrap(int v, int p) { return ((v % p) + p) % p; }
        static float VN(float x, float y, int per)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = Wrap(xi, per), x1 = Wrap(xi + 1, per), y0 = Wrap(yi, per), y1 = Wrap(yi + 1, per);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0), Hash(x1, y0), fx), Mathf.Lerp(Hash(x0, y1), Hash(x1, y1), fx), fy);
        }
        /// FBM tileable (période de base `per` cellules, doublée à chaque octave).
        static float FBM(float u, float v, int per, int oct = 5)
        {
            float a = 0.5f, s = 0, x = u * per, y = v * per; int p = per;
            for (int i = 0; i < oct; i++) { s += a * VN(x, y, p); x *= 2f; y *= 2f; p *= 2; a *= 0.5f; }
            return s;
        }

        // ---------------------------------------------------------------- textures
        static Texture2D Tex(string name, int n, Func<float, float, Color> f, bool srgb = true, bool repeat = false)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n, (y + 0.5f) / n);
            t.SetPixels32(px);
            string p = R + "/Textures/" + name + ".png";
            File.WriteAllBytes(p, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true;
            ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.sRGBTexture = srgb;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        static float Rad(float u, float v) { float dx = u - 0.5f, dy = v - 0.5f; return Mathf.Sqrt(dx * dx + dy * dy) * 2f; }

        // R = bruit tileable, G = masque de bouffée (plus haute que large), pour FireParticle
        static Texture2D TexFlame() => Tex("T_Flame", 256, (u, v) =>
        {
            float n = FBM(u, v, 4);
            n = Mathf.Clamp01((n - 0.25f) * 1.9f);
            float nx = (u - 0.5f) * 2f * 1.05f, ny = (v - 0.5f) * 2f * 0.92f;
            float d = Mathf.Sqrt(nx * nx + ny * ny);
            d += (FBM(u + 0.31f, v + 0.17f, 3, 4) - 0.5f) * 0.45f;
            float m = Mathf.SmoothStep(1f, 0.12f, d);
            return new Color(n, m, 0, 1);
        }, false, true);
        static Texture2D TexNoise() => Tex("T_Noise", 256, (u, v) => { float n = FBM(u, v, 4, 5); return new Color(n, n, n, 1); }, false, true);
        static Texture2D TexPuff() => Tex("T_Puff", 256, (u, v) =>
        {
            float r = Rad(u, v); float a = Mathf.Clamp01(1 - r); a = a * a * (3 - 2 * a);
            a = Mathf.Clamp01(a * (0.3f + 1.4f * FBM(u, v, 5, 4)));
            return new Color(1, 1, 1, a);
        });
        static Texture2D TexGlow() => Tex("T_Glow", 128, (u, v) => new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - Rad(u, v)), 2.4f)));
        static Texture2D TexSpark() => Tex("T_Spark", 64, (u, v) =>
        {
            float r = Rad(u, v); float a = Mathf.Exp(-r * r * 14f);
            return new Color(1, 1, 1, Mathf.Clamp01(a) * Mathf.Clamp01(1.2f - r));
        });
        static Texture2D TexRing() => Tex("T_Ring", 256, (u, v) =>
        {
            float r = Rad(u, v);
            float a = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.08f, 2f));
            a += 0.2f * Mathf.Exp(-Mathf.Pow((r - 0.55f) / 0.3f, 2f)) * FBM(u, v, 4, 4);
            a *= 0.7f + 0.6f * FBM(u + 0.5f, v, 6, 3);
            return new Color(1, 1, 1, Mathf.Clamp01(a) * Mathf.Clamp01((1f - r) * 8f + 0.2f));
        });
        static Texture2D TexScorch() => Tex("T_Scorch", 512, (u, v) =>
        {
            float r = Rad(u, v); float ang = Mathf.Atan2(v - 0.5f, u - 0.5f);
            float edge = 0.78f + (FBM(u, v, 5, 5) - 0.5f) * 0.5f + 0.06f * Mathf.Sin(ang * 7f);
            float a = Mathf.Clamp01((edge - r) * 5f);
            a *= 0.55f + 0.45f * FBM(u + 0.2f, v + 0.6f, 9, 4);
            float core = Mathf.Clamp01(1f - r * 1.6f);
            return new Color(0.02f + core * 0.01f, 0.016f, 0.012f, Mathf.Clamp01(a * (0.7f + core * 0.5f)));
        });

        // ---------------------------------------------------------------- matériaux
        static void Blend(Material m, bool additive)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.renderQueue = 3000;
        }
        static Material FireMat(string name, Texture2D t, bool additive, float intensity, float cut = 0.22f, float soft = 0.22f, float scroll = 0.5f)
        {
            var m = new Material(Shader.Find("FireVFX/FireParticle"));
            m.SetTexture("_MainTex", t); m.SetFloat("_Intensity", intensity); m.SetFloat("_Cut", cut); m.SetFloat("_Soft", soft); m.SetFloat("_Scroll", scroll);
            Blend(m, additive);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat");
            return m;
        }
        static Material BallMat(string name, float intensity, float noise, float stretch, float cut, float displace, float speed)
        {
            var m = new Material(Shader.Find("FireVFX/FireBall"));
            m.SetFloat("_Intensity", intensity); m.SetFloat("_NoiseScale", noise); m.SetFloat("_Stretch", stretch);
            m.SetFloat("_Cut", cut); m.SetFloat("_Displace", displace); m.SetFloat("_Speed", speed);
            Blend(m, true);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat");
            return m;
        }
        static Material DistMat(string name, Texture2D noise, Texture2D mask, float strength, float tiling)
        {
            var m = new Material(Shader.Find("FireVFX/FireDistortion"));
            m.SetTexture("_MainTex", noise); m.SetTexture("_MaskTex", mask); m.SetFloat("_Strength", strength); m.SetFloat("_Tiling", tiling);
            m.renderQueue = 2995;
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat");
            return m;
        }
        /// Matériau URP Particles/Unlit (étincelles, fumée, brûlure, halos)
        static Material Std(string name, Texture2D t, bool additive, Color c, float intensity)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a));
            m.SetFloat("_SoftParticlesEnabled", 1); m.EnableKeyword("_SOFTPARTICLES_ON");
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f); m.SetFloat("_SoftParticlesFarFadeDistance", 0.8f);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat");
            return m;
        }

        // ---------------------------------------------------------------- helpers particules
        static ParticleSystem PS(Transform parent, string name, Material mat, bool world = false, float dur = 5f, bool loop = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            var m = ps.main; m.duration = dur; m.loop = loop; m.playOnAwake = true; m.startSpeed = 0f; m.maxParticles = 300;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            var sh = ps.shape; sh.enabled = false;
            var em = ps.emission; em.rateOverTime = 0f;
            return ps;
        }
        static ParticleSystem.MinMaxCurve C(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static void Life(ParticleSystem ps, float a, float b) { var m = ps.main; m.startLifetime = C(a, b); }
        static void Size(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSize = C(a, b); }
        static void Speed(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSpeed = C(a, b); }
        static void Grav(ParticleSystem ps, float g) { var m = ps.main; m.gravityModifier = g; }
        static void Max(ParticleSystem ps, int n) { var m = ps.main; m.maxParticles = n; }
        static void Rot(ParticleSystem ps) { var m = ps.main; m.startRotation = C(0f, 6.283f); }
        static void Spin(ParticleSystem ps, float s) { var r = ps.rotationOverLifetime; r.enabled = true; r.z = C(-s, s); }
        static void Burst(ParticleSystem ps, int n, float t = 0f) { var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(t, (short)n) }); }
        static void Rate(ParticleSystem ps, float time, float dist = 0f) { var e = ps.emission; e.rateOverTime = time; e.rateOverDistance = dist; }
        static void Grow(ParticleSystem ps, float s0, float s1)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, s0), new Keyframe(0.3f, s0 + (s1 - s0) * 0.78f), new Keyframe(1, s1)));
        }
        static void Colors(ParticleSystem ps, Color c0, Color c1, float a, float fadeIn = 0.15f, float hold = 0.5f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c0, 0), new GradientColorKey(c1, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, fadeIn), new GradientAlphaKey(a * 0.7f, hold), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }
        /// Alpha = "vie" pour FireParticle (montée rapide puis décroissance → la flamme se dissout depuis les bords).
        static void FireLife(ParticleSystem ps, float fadeIn = 0.07f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) },
                      new[] { new GradientAlphaKey(0.35f, 0), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }
        static void Noise(ParticleSystem ps, float str, float freq) { var n = ps.noise; n.enabled = true; n.strength = str; n.frequency = freq; n.scrollSpeed = 0.5f; n.quality = ParticleSystemNoiseQuality.Medium; }
        static void Hemi(ParticleSystem ps, float radius) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = radius; }
        static void Flat(ParticleSystem ps, Mesh quad)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = quad; r.alignment = ParticleSystemRenderSpace.Local;
        }
        static void World(ParticleSystem ps) { var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.bounce = 0.35f; c.dampen = 0.35f; c.lifetimeLoss = 0.1f; c.radiusScale = 0.4f; }
        static void SparkTrails(ParticleSystem ps, Material trailMat, float time, Color a, Color b)
        {
            var t = ps.trails; t.enabled = true; t.mode = ParticleSystemTrailMode.PerParticle; t.ratio = 1f; t.lifetime = C(time, time);
            t.minVertexDistance = 0.08f; t.worldSpace = true; t.dieWithParticles = true; t.inheritParticleColor = false; t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.6f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            t.colorOverTrail = new ParticleSystem.MinMaxGradient(g);
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = trailMat;
        }
        /// Charge un OBJ de Assets/FireVFX/Meshes (modèle "Fireball Hero"), ou null.
        static Mesh LoadMesh(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(R + "/Meshes/" + name + ".obj")) if (o is Mesh m) return m;
            return null;
        }
        static Mesh Prim(PrimitiveType p)
        {
            var go = GameObject.CreatePrimitive(p); var m = go.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(go); return m;
        }

        // ---------------------------------------------------------------- URP
        [MenuItem("Tools/Fire VFX/Setup URP")]
        public static void SetupURP()
        {
            int n = 0;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (a == null) continue;
                a.supportsCameraOpaqueTexture = true; a.supportsCameraDepthTexture = true; a.supportsHDR = true;
                EditorUtility.SetDirty(a); n++;
                Debug.Log("[FireVFX] URP asset '" + a.name + "' : Opaque Texture, Depth Texture, HDR activés (niveau " + QualitySettings.names[i] + ").");
            }
            var d = GraphicsSettings.defaultRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (d != null) { d.supportsCameraOpaqueTexture = true; d.supportsCameraDepthTexture = true; d.supportsHDR = true; EditorUtility.SetDirty(d); n++; }
            AssetDatabase.SaveAssets();
            if (n == 0) Debug.LogWarning("[FireVFX] Aucun URP Asset trouvé.");
        }

        // ---------------------------------------------------------------- build
        static void LegacyBuildAll()
        {
            if (Shader.Find("Universal Render Pipeline/Particles/Unlit") == null) { Debug.LogError("[FireVFX] URP introuvable."); return; }
            if (Shader.Find("FireVFX/FireParticle") == null || Shader.Find("FireVFX/FireBall") == null || Shader.Find("FireVFX/FireDistortion") == null)
            { Debug.LogError("[FireVFX] Shaders FireVFX/* introuvables (erreur de compilation ?)."); return; }
            foreach (var d in new[] { "Textures", "Materials", "Prefabs", "Meshes" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();

            var tFlame = TexFlame(); var tNoise = TexNoise(); var tPuff = TexPuff(); var tGlow = TexGlow();
            var tSpark = TexSpark(); var tRing = TexRing(); var tScorch = TexScorch();

            var Orange = new Color(1f, 0.55f, 0.15f); var Hot = new Color(1f, 0.85f, 0.5f); var Red = new Color(0.9f, 0.18f, 0.04f);

            var mCore = BallMat("M_FireCore", 1.6f, 2.6f, 0.5f, 0.2f, 0.07f, 3f);
            var mOuter = BallMat("M_FireOuter", 1.0f, 2.0f, 1.5f, 0.34f, 0.15f, 2.2f);
            var mLava = BallMat("M_Lava", 1.2f, 3.5f, 0f, 0.25f, 0.1f, 0.8f);
            var mFlame = FireMat("M_Flame", tFlame, true, 1f);
            var mFlameBig = FireMat("M_FlameBig", tFlame, true, 0.9f, 0.26f, 0.25f, 0.6f);
            var mFlameDark = FireMat("M_FlameAlpha", tFlame, false, 0.8f, 0.3f, 0.2f, 0.5f);
            var mHalo = Std("M_Halo", tGlow, true, new Color(1f, 0.4f, 0.08f), 1.8f);
            var mFlash = Std("M_Flash", tGlow, true, new Color(1f, 0.85f, 0.55f), 5f);
            var mSpark = Std("M_Spark", tSpark, true, new Color(1f, 0.62f, 0.2f), 5f);
            var mTrail = Std("M_SparkTrail", tGlow, true, new Color(1f, 0.5f, 0.12f), 4f);
            var mRing = Std("M_Ring", tRing, true, new Color(1f, 0.5f, 0.12f), 3f);
            var mSmoke = Std("M_Smoke", tPuff, false, new Color(1, 1, 1), 1f);
            var mScorch = Std("M_Scorch", tScorch, false, White, 1f);
            var mHaze = DistMat("M_HeatHaze", tNoise, tGlow, 0.035f, 1.5f);
            var mShockDist = DistMat("M_ShockDistortion", tNoise, tRing, 0.06f, 1f);

            var quad = Prim(PrimitiveType.Quad); var sphere = Prim(PrimitiveType.Sphere);
            var meshCore = LoadMesh("FireBall_Core"); var meshShell = LoadMesh("FireBall_Shell"); var meshTongues = LoadMesh("FireBall_Tongues");
            var chunkMeshes = new System.Collections.Generic.List<Mesh>();
            foreach (var cn in new[] { "LavaChunk_A", "LavaChunk_B", "LavaChunk_C" }) { var cm = LoadMesh(cn); if (cm) chunkMeshes.Add(cm); }
            bool hero = meshCore && meshShell;
            Debug.Log(hero ? "[FireVFX] Modèle Fireball Hero détecté : coque en goutte, langues et éclats utilisés." : "[FireVFX] Pas d'OBJ dans " + R + "/Meshes : sphères Unity utilisées.");
            if (hero) mOuter.SetFloat("_Stretch", 0.2f);
            var mTongues = BallMat("M_FireTongues", 1.1f, 2.6f, 0f, 0.3f, 0.04f, 2.6f);

            // ============================ IMPACT ============================
            var imp = new GameObject("FireImpact");
            var ic = imp.AddComponent<FireImpact>();
            var lg = new GameObject("Light"); lg.transform.SetParent(imp.transform, false); lg.transform.localPosition = new Vector3(0, 0, 1f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f); l.range = 14f; l.intensity = 0f; ic.flashLight = l;

            var flash = PS(imp.transform, "Flash", mFlash, false, 1f, false); Life(flash, 0.16f, 0.16f); Size(flash, 7f, 7f); Burst(flash, 1); Grow(flash, 0.4f, 1.2f);
            Colors(flash, White, Hot, 1f, 0.05f, 0.4f);

            // boule de feu principale : bouffées FireParticle larges
            var fb = PS(imp.transform, "Fireball", mFlameBig, true, 1f, false); Burst(fb, 16); Hemi(fb, 0.35f); Max(fb, 40);
            Life(fb, 0.55f, 1.0f); Speed(fb, 2.5f, 8f); Size(fb, 2.6f, 4.6f); Rot(fb); Spin(fb, 0.7f); Grow(fb, 0.35f, 1.15f); Noise(fb, 0.8f, 0.45f); FireLife(fb, 0.06f);
            { var lv = fb.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(1.2f, 1.2f); lv.dampen = 0.15f; }
            var dome = PS(imp.transform, "FireDome", mFlameBig, true, 1f, false); Burst(dome, 9, 0.05f); Hemi(dome, 0.4f); Max(dome, 20);
            Life(dome, 0.9f, 1.5f); Speed(dome, 0.8f, 3f); Size(dome, 3.2f, 5.2f); Rot(dome); Spin(dome, 0.5f); Grow(dome, 0.5f, 1.2f); Noise(dome, 0.6f, 0.35f); FireLife(dome, 0.1f);
            { var m = dome.main; m.gravityModifier = -0.12f; }
            var tongues = PS(imp.transform, "Tongues", mFlame, true, 1f, false); Burst(tongues, 24, 0.02f); Hemi(tongues, 0.2f); Max(tongues, 40);
            Life(tongues, 0.4f, 0.9f); Speed(tongues, 6f, 14f); Size(tongues, 0.7f, 1.5f); Rot(tongues); Grow(tongues, 0.6f, 1.6f); Noise(tongues, 1.2f, 0.9f); FireLife(tongues, 0.05f);

            var ring = PS(imp.transform, "Shockwave", mRing, false, 1f, false); Flat(ring, quad); Life(ring, 0.5f, 0.5f); Size(ring, 11f, 11f); Burst(ring, 1); Max(ring, 2);
            { var rs = ring.sizeOverLifetime; rs.enabled = true; rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.08f), new Keyframe(0.25f, 0.7f), new Keyframe(1, 1f))); }
            Colors(ring, White, Orange, 1f, 0.05f, 0.4f);

            var distR = PS(imp.transform, "DistortionRing", mShockDist, false, 1f, false); distR.transform.localPosition = new Vector3(0, 0, 0.3f); Max(distR, 2);
            Life(distR, 0.55f, 0.55f); Size(distR, 13f, 13f); Burst(distR, 1); Grow(distR, 0.1f, 1f);
            Colors(distR, White, White, 1f, 0.05f, 0.4f);

            var distH = PS(imp.transform, "DistortionHaze", mHaze, true, 3f, false); Hemi(distH, 0.6f); Max(distH, 12);
            Life(distH, 1.2f, 2f); Speed(distH, 0.4f, 1.5f); Size(distH, 3f, 5f); Rate(distH, 6f); Grow(distH, 0.6f, 1.4f);
            { var m = distH.main; m.gravityModifier = -0.1f; }
            Colors(distH, White, White, 0.8f, 0.2f, 0.5f);

            var sp = PS(imp.transform, "Sparks", mSpark, true, 2f, false); Burst(sp, 70); Hemi(sp, 0.15f); Max(sp, 90);
            Life(sp, 0.8f, 1.8f); Speed(sp, 7f, 20f); Size(sp, 0.07f, 0.16f); Grav(sp, 1f); Noise(sp, 0.7f, 1f); World(sp);
            Colors(sp, White, Red, 1f, 0.02f, 0.7f); SparkTrails(sp, mTrail, 0.28f, Hot, Red);

            var emb = PS(imp.transform, "Embers", mSpark, true, 4f, false); Burst(emb, 45, 0.05f); Hemi(emb, 0.8f); Max(emb, 60);
            Life(emb, 2f, 4.5f); Speed(emb, 1f, 7f); Size(emb, 0.04f, 0.1f); Grav(emb, -0.04f); Noise(emb, 1.6f, 0.5f);
            Colors(emb, Hot, Red, 1f, 0.04f, 0.6f);

            var chunks = PS(imp.transform, "LavaChunks", mLava, true, 3f, false); Burst(chunks, 12); Hemi(chunks, 0.2f); Max(chunks, 14);
            { var r = chunks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.alignment = ParticleSystemRenderSpace.Local; if (chunkMeshes.Count > 0) r.SetMeshes(chunkMeshes.ToArray()); else r.mesh = sphere; }
            Life(chunks, 1f, 2f); Speed(chunks, 4f, 11f); Size(chunks, 0.12f, 0.3f); Grav(chunks, 1.6f); World(chunks);
            { var m = chunks.main; m.startRotation3D = true; m.startRotationX = C(0, 6.28f); m.startRotationY = C(0, 6.28f); m.startRotationZ = C(0, 6.28f); }
            { var s = chunks.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.6f, 0.9f), new Keyframe(1, 0))); }
            { var t = chunks.trails; t.enabled = true; t.ratio = 0.7f; t.lifetime = C(0.4f, 0.4f); t.worldSpace = true; t.dieWithParticles = true; t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.5f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0))); chunks.GetComponent<ParticleSystemRenderer>().trailMaterial = mTrail; }

            var smoke = PS(imp.transform, "Smoke", mSmoke, true, 4f, false); Burst(smoke, 16, 0.12f); Hemi(smoke, 0.5f); Max(smoke, 24);
            Life(smoke, 3f, 5f); Speed(smoke, 0.6f, 3f); Size(smoke, 2.5f, 4.5f); Rot(smoke); Spin(smoke, 0.25f); Grow(smoke, 0.4f, 1.7f); Noise(smoke, 0.5f, 0.25f);
            { var m = smoke.main; m.gravityModifier = -0.06f; var lv = smoke.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(0.9f, 0.9f); lv.dampen = 0.08f; }
            Colors(smoke, new Color(1f, 0.45f, 0.12f), new Color(0.13f, 0.12f, 0.11f), 0.8f, 0.12f, 0.5f);

            var scorch = PS(imp.transform, "Scorch", mScorch, false, 1f, false); scorch.transform.localPosition = new Vector3(0, 0, 0.02f); Flat(scorch, quad); Max(scorch, 2);
            Life(scorch, 9f, 9f); Size(scorch, 5.2f, 6f); Burst(scorch, 1, 0.05f); Grow(scorch, 0.5f, 1f); Rot(scorch);
            { var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.04f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0, 1) }); var c = scorch.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g); }

            var ground = PS(imp.transform, "GroundFire", mFlame, false, 3f, false); ground.transform.localPosition = new Vector3(0, 0, 0.05f); Max(ground, 40);
            { var s = ground.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = 1.6f; s.radiusThickness = 1f; }
            { var v = ground.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local; v.z = C(1.2f, 2.4f); v.x = C(0, 0); v.y = C(0, 0); }
            Life(ground, 0.5f, 0.9f); Size(ground, 0.8f, 1.5f); Rate(ground, 22f); Rot(ground); Grow(ground, 0.7f, 1.4f); Noise(ground, 0.5f, 0.8f);
            { var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, new[] { new GradientAlphaKey(0.4f, 0), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0, 1) }); var c = ground.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g); }
            { var sz = ground.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.7f, 1), new Keyframe(1, 0.2f))); }

            var impPrefab = PrefabUtility.SaveAsPrefabAsset(imp, R + "/Prefabs/FireImpact.prefab");
            UnityEngine.Object.DestroyImmediate(imp);

            // ============================ PROJECTILE ============================
            var root = new GameObject("FireProjectile");
            var fp = root.AddComponent<FireProjectile>(); fp.impactPrefab = impPrefab;

            var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false);
            var fv = vis.AddComponent<FireBallVisual>();
            var outer = new GameObject("Outer"); outer.transform.SetParent(vis.transform, false); outer.transform.localScale = Vector3.one * (hero ? 1.1f : 1.15f);
            outer.AddComponent<MeshFilter>().sharedMesh = hero ? meshShell : sphere; outer.AddComponent<MeshRenderer>().sharedMaterial = mOuter;
            var core = new GameObject("Core"); core.transform.SetParent(vis.transform, false); core.transform.localScale = Vector3.one * 0.7f;
            core.AddComponent<MeshFilter>().sharedMesh = hero ? meshCore : sphere; core.AddComponent<MeshRenderer>().sharedMaterial = mCore;
            if (hero && meshTongues)
            {
                var tg = new GameObject("Tongues"); tg.transform.SetParent(vis.transform, false); tg.transform.localScale = Vector3.one * 1.1f;
                tg.AddComponent<MeshFilter>().sharedMesh = meshTongues; tg.AddComponent<MeshRenderer>().sharedMaterial = mTongues;
            }
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>()) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }

            var halo = PS(root.transform, "Halo", mHalo); Life(halo, 0.5f, 0.5f); Size(halo, 3.6f, 4.2f); Max(halo, 10);
            Rate(halo, 14f); Colors(halo, Orange, Red, 0.55f, 0.3f, 0.6f);
            var whiteCore = PS(root.transform, "HotCore", mFlash); Life(whiteCore, 0.3f, 0.3f); Size(whiteCore, 1.1f, 1.4f); Max(whiteCore, 8);
            Rate(whiteCore, 14f); Colors(whiteCore, White, Hot, 0.9f, 0.3f, 0.6f);

            var hazeP = PS(root.transform, "DistortionHeat", mHaze, true); Life(hazeP, 0.5f, 0.5f); Size(hazeP, 2.4f, 2.8f); Max(hazeP, 10); Rate(hazeP, 14f);
            Colors(hazeP, White, White, 0.6f, 0.2f, 0.5f);

            var trails = new GameObject("Trails"); trails.transform.SetParent(root.transform, false); fp.trails = trails.transform;

            var tf = PS(trails.transform, "Flames", mFlame, true); Life(tf, 0.45f, 0.8f); Size(tf, 0.7f, 1.3f); Speed(tf, 0.1f, 0.8f); Rot(tf); Spin(tf, 0.8f); Max(tf, 120);
            Grav(tf, -0.18f); Noise(tf, 0.5f, 0.9f); Rate(tf, 30f, 26f); FireLife(tf, 0.08f);
            { var s = tf.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.15f; var sz = tf.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(0.3f, 1.1f), new Keyframe(1, 0.4f))); }

            var tsm = PS(trails.transform, "Smoke", mSmoke, true); Life(tsm, 1.4f, 2.4f); Size(tsm, 0.5f, 0.9f); Speed(tsm, 0.1f, 0.5f); Rot(tsm); Spin(tsm, 0.3f); Max(tsm, 100);
            Grav(tsm, -0.08f); Noise(tsm, 0.4f, 0.5f); Grow(tsm, 0.5f, 2.6f); Rate(tsm, 8f, 8f);
            { var s = tsm.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.12f; }
            Colors(tsm, new Color(1f, 0.4f, 0.1f), new Color(0.12f, 0.11f, 0.1f), 0.45f, 0.12f, 0.45f);

            var te = PS(trails.transform, "Embers", mSpark, true); Life(te, 0.6f, 1.5f); Size(te, 0.04f, 0.1f); Speed(te, 0.5f, 3f); Max(te, 120);
            Noise(te, 1f, 1.2f); Rate(te, 40f, 10f); Grav(te, -0.03f);
            { var s = te.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 25f; s.radius = 0.12f; s.rotation = new Vector3(180, 0, 0); }
            Colors(te, White, Red, 1f, 0.05f, 0.6f); SparkTrails(te, mTrail, 0.2f, Hot, Red);

            var pl = new GameObject("Light"); pl.transform.SetParent(root.transform, false);
            var pLight = pl.AddComponent<Light>(); pLight.type = LightType.Point; pLight.color = new Color(1f, 0.52f, 0.18f); pLight.range = 9f; pLight.intensity = 5f; fp.glow = pLight;

            PrefabUtility.SaveAsPrefabAsset(root, R + "/Prefabs/FireProjectile.prefab");
            UnityEngine.Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[FireVFX] OK. Prefabs : " + R + "/Prefabs/. Lance Tools/Fire VFX/Setup URP, puis ajoute FireShooter sur la caméra.");
        }
    }
}
#endif
