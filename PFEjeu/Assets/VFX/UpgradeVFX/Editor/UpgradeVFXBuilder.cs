#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UpgradeVFX.EditorTools
{
    /// Menus : Tools/Upgrade VFX/Setup URP, Build All, Create Demo Caserne, Add UpgradeSite To Selection
    public static class UpgradeVFXBuilder
    {
        const string R = "Assets/VFX/UpgradeVFX";
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
        static Texture2D TexDust() => Tex("T_Dust", 256, (u, v) =>
        {
            float n = Mathf.Clamp01((FBM(u, v, 4) - 0.2f) * 1.7f);
            float nx = (u - 0.5f) * 2f, ny = (v - 0.5f) * 2f, d = Mathf.Sqrt(nx * nx + ny * ny) + (FBM(u + 0.31f, v + 0.17f, 3, 4) - 0.5f) * 0.55f;
            return new Color(n, Mathf.SmoothStep(1f, 0.15f, d), 0, 1);
        }, false);
        static Texture2D TexRingBand() => Tex("T_RingBand", 256, (u, v) =>
        {
            float a = Mathf.Pow(v, 2.4f) * Mathf.SmoothStep(1f, 0.93f, v) * (0.6f + 0.8f * FBM(u, v * 0.3f, 8, 3));
            return new Color(1, 1, 1, Mathf.Clamp01(a));
        });
        static Texture2D TexMote() => Tex("T_Mote", 64, (u, v) => { float r = Rad(u, v); return new Color(1, 1, 1, Mathf.Clamp01(Mathf.Exp(-r * r * 12f)) * Mathf.Clamp01(1.2f - r)); });
        static Texture2D TexWhite() => Tex("T_White", 4, (u, v) => White);
        static Texture2D TexPulse() => Tex("T_Pulse", 128, (u, v) => { float r = Rad(u, v); float a = Mathf.SmoothStep(0.55f, 1f, r) * Mathf.SmoothStep(1f, 0.9f, r) + Mathf.Clamp01(1f - r) * 0.18f; return new Color(1, 1, 1, Mathf.Clamp01(a)); });
        static Texture2D TexBrick() => Tex("T_Brick", 256, (u, v) =>
        {
            int row = Mathf.FloorToInt(v * 8f); float off = (row % 2) * 0.5f, bu = u * 4f + off; int col = Mathf.FloorToInt(bu);
            float bx = bu - col, by = v * 8f - row; bool mortar = bx < 0.05f || by < 0.09f;
            float g = FBM(u, v, 8, 4); float vr = 0.82f + 0.36f * Hash(row, Wrap(col, 4));
            Color c = mortar ? new Color(0.62f, 0.6f, 0.55f) : new Color(0.55f * vr, 0.3f * vr, 0.21f * vr);
            return c * (0.75f + 0.4f * g);
        }, true, true);
        static Texture2D TexPlank() => Tex("T_Plank", 256, (u, v) =>
        {
            int col = Mathf.FloorToInt(u * 8f); float bx = u * 8f - col; float g = FBM(u * 0.4f, v * 3f, 4, 4);
            float vr = 0.8f + 0.4f * Hash(col, 3); Color c = new Color(0.52f * vr, 0.38f * vr, 0.22f * vr) * (0.7f + 0.5f * g);
            return bx < 0.04f ? c * 0.45f : c;
        }, true, true);

        static void Blend(Material m, bool additive)
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); m.renderQueue = 3000;
        }
        static Material Std(string name, Texture2D t, bool additive, Color c, float intensity, bool twoSided = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0); Blend(m, additive); m.SetFloat("_ZWrite", 0); if (twoSided) m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a));
            m.SetFloat("_SoftParticlesEnabled", 1); m.EnableKeyword("_SOFTPARTICLES_ON"); m.SetFloat("_SoftParticlesNearFadeDistance", 0f); m.SetFloat("_SoftParticlesFarFadeDistance", 0.6f);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }
        static Material Lit(string name, Color c, float smooth, Texture2D t = null, Vector2? tiling = null, Color? emit = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            if (t) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            if (emit.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit.Value); }
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }
        static Material Beam(string name, float mode, float streaks, float speed)
        {
            var m = new Material(Shader.Find("UpgradeVFX/UpgradeBeam")); m.SetFloat("_Mode", mode); m.SetFloat("_Streaks", streaks); m.SetFloat("_Speed", speed); m.renderQueue = 3000;
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }

        static ParticleSystem.MinMaxCurve C(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static ParticleSystem PS(Transform parent, string name, Material mat, bool loop = false, float dur = 1f, bool auto = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            var m = ps.main; m.duration = dur; m.loop = loop; m.playOnAwake = auto; m.startSpeed = 0f; m.maxParticles = 300;
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
        static void VelUp(ParticleSystem ps, float a, float b)
        {
            var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
            vo.x = new ParticleSystem.MinMaxCurve(0f, 0f); vo.z = new ParticleSystem.MinMaxCurve(0f, 0f); vo.y = C(a, b);
        }
        static GameObject Prim(Transform parent, string name, Mesh mesh, Material mat)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var r = g.GetComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return g;
        }

        [MenuItem("Tools/Upgrade VFX/Setup URP")]
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
            Debug.Log(n == 0 ? "[UpgradeVFX] Aucun URP Asset trouvé." : "[UpgradeVFX] URP : Depth Texture + HDR activés (" + n + " asset(s)).");
        }

        [MenuItem("Tools/Upgrade VFX/Build All")]
        public static void BuildAll()
        {
            foreach (var s in new[] { "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Particles/Lit", "Universal Render Pipeline/Lit", "UpgradeVFX/UpgradeDust", "UpgradeVFX/UpgradeSwap", "UpgradeVFX/UpgradeBeam" })
                if (Shader.Find(s) == null) { Debug.LogError("[UpgradeVFX] Shader introuvable : " + s + " (URP installé ? erreur de compilation ?)"); return; }
            foreach (var d in new[] { "Textures", "Materials", "Prefabs", "Meshes" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();

            var tMote = TexMote(); var tBand = TexRingBand(); var tWhite = TexWhite(); var tPulse = TexPulse(); var tBrick = TexBrick(); var tPlank = TexPlank();
            var mDust = new Material(Shader.Find("UpgradeVFX/UpgradeDust")); mDust.SetTexture("_MainTex", TexDust()); mDust.renderQueue = 3000; AssetDatabase.CreateAsset(mDust, R + "/Materials/M_Dust.mat");
            var mDebris = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit")); mDebris.SetColor("_BaseColor", White); mDebris.SetFloat("_Smoothness", 0.05f); AssetDatabase.CreateAsset(mDebris, R + "/Materials/M_Debris.mat");
            var mRingDust = Std("M_RingDust", tBand, false, White, 1f); var mRingTeam = Std("M_RingTeam", tBand, true, White, 1f);
            var mMote = Std("M_Mote", tMote, true, White, 1f); var mChevron = Std("M_Chevron", tWhite, true, White, 1f, true); var mPulse = Std("M_Pulse", tPulse, true, White, 1f);
            var mTrail = Beam("M_FrontTrail", 0f, 36f, 1.5f); var mFlat = Beam("M_FrontRing", 2f, 36f, 1.5f); var mPillar = Beam("M_Pillar", 1f, 28f, 1.2f);
            // matériaux démo
            Lit("M_DemoWood", White, 0.05f, tPlank, new Vector2(2f, 1f)); Lit("M_DemoThatch", new Color(0.42f, 0.34f, 0.18f), 0.05f); Lit("M_DemoDoor", new Color(0.2f, 0.12f, 0.07f), 0.1f);
            Lit("M_DemoBrick", White, 0.05f, tBrick, new Vector2(3f, 1.5f)); Lit("M_DemoSlate", new Color(0.22f, 0.25f, 0.31f), 0.25f); Lit("M_DemoStone", new Color(0.55f, 0.54f, 0.52f), 0.1f);
            Lit("M_DemoWindow", new Color(0.15f, 0.12f, 0.08f), 0.3f, null, null, new Color(2.2f, 1.5f, 0.6f)); Lit("M_DemoDoorMetal", new Color(0.18f, 0.18f, 0.2f), 0.4f);

            var band = LoadMesh("UpgradeBand"); var fring = LoadMesh("UpgradeFrontRing"); var chev = LoadMesh("UpgradeChevron"); var shock = LoadMesh("UpgradeShock"); var disc = LoadMesh("UpgradeDisc");
            var debris = new System.Collections.Generic.List<Mesh>();
            foreach (var n in new[] { "Debris_Brick", "Debris_Stone", "Debris_Plank", "Debris_Shard" }) { var m = LoadMesh(n); if (m) debris.Add(m); }
            if (!band || !fring || !chev || !shock || !disc || debris.Count == 0) { Debug.LogError("[UpgradeVFX] OBJ manquants dans " + R + "/Meshes."); return; }
            Color lite = new Color(1f, 1f, 1f), mid = new Color(0.86f, 0.86f, 0.86f), dark = new Color(0.7f, 0.7f, 0.7f);
            var up = new Vector3(-90, 0, 0); var flat = new Vector3(90, 0, 0);

            // ============================ AURA (charge + chevrons) ============================
            var ax = new GameObject("UpgradeAura"); var au = ax.AddComponent<UpgradeAura>();
            var bd = PS(ax.transform, "BaseDust", mDust, true, 5f, false);
            { var s = bd.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Rectangle; s.rotation = up; s.scale = new Vector3(12, 9, 1); }
            Rate(bd, 12f); Life(bd, 2.5f, 4f); Speed(bd, 0.2f, 0.9f); Size(bd, 1.2f, 2.2f); Rot(bd); Grow(bd, 0.4f, 1.4f); Noise(bd, 0.4f, 0.4f); Grav(bd, -0.01f); Max(bd, 140); Tone(bd, dark, mid); Alpha(bd, 0.4f, 0.2f, 0.5f);
            var mi = PS(ax.transform, "MotesIn", mMote, true, 5f, false); Circle(mi, 1f, 1f, up); { var s = mi.shape; s.scale = new Vector3(9, 7, 1); }
            Rate(mi, 90f); Life(mi, 0.7f, 0.95f); Size(mi, 0.06f, 0.15f); Max(mi, 300); Tone(mi, White, White); Alpha(mi, 1f, 0.15f, 0.6f);
            var ch = PS(ax.transform, "Chevrons", mChevron, true, 5f, false); MeshR(ch, chev); Circle(ch, 1f, 0f, up); { var s = ch.shape; s.scale = new Vector3(8, 6, 1); s.alignToDirection = true; }
            Rate(ch, 14f); Life(ch, 1.4f, 1.9f); Speed(ch, 0.05f, 0.2f); Size(ch, 0.7f, 1.0f); Max(ch, 90); Tone(ch, White, White); Alpha(ch, 0.9f, 0.15f, 0.55f); VelUp(ch, 5f, 7f);
            var gp = PS(ax.transform, "GroundPulse", mPulse, true, 2f, false); gp.transform.localPosition = new Vector3(0, 0.02f, 0); MeshR(gp, disc); Rate(gp, 1.6f); Life(gp, 0.9f, 0.9f); Size(gp, 12f, 12f); Max(gp, 4); Grow(gp, 0.55f, 1.05f); Tone(gp, White, White); Alpha(gp, 0.55f, 0.15f, 0.4f);
            var gl = new GameObject("Light"); gl.transform.SetParent(ax.transform, false); var al = gl.AddComponent<Light>(); al.type = LightType.Point; al.intensity = 0; al.range = 16; al.shadows = LightShadows.None;
            au.baseDust = bd; au.motesIn = mi; au.chevrons = ch; au.groundPulse = gp; au.glow = al;
            PrefabUtility.SaveAsPrefabAsset(ax, R + "/Prefabs/UpgradeAura.prefab"); UnityEngine.Object.DestroyImmediate(ax);

            // ============================ FRONT (bande + anneau + étincelles) ============================
            var fx = new GameObject("UpgradeFront"); var uf = fx.AddComponent<UpgradeFront>();
            uf.band = Prim(fx.transform, "Band", band, mTrail).transform; uf.ring = Prim(fx.transform, "Ring", fring, mFlat).transform;
            var sp = PS(fx.transform, "Sparks", mMote, true, 5f, false); Circle(sp, 1f, 0.2f, up); { var s = sp.shape; s.scale = new Vector3(7, 5, 1); }
            Rate(sp, 140f); Life(sp, 0.5f, 1.3f); Speed(sp, 0.6f, 2.4f); Size(sp, 0.08f, 0.22f); Grav(sp, 0.12f); Max(sp, 400); Noise(sp, 0.4f, 0.6f); Tone(sp, White, White); Alpha(sp, 1f, 0.05f, 0.4f);
            var fl = new GameObject("Light"); fl.transform.SetParent(fx.transform, false); fl.transform.localPosition = new Vector3(0, 1f, 0); var fL = fl.AddComponent<Light>(); fL.type = LightType.Point; fL.range = 14; fL.intensity = 0; fL.shadows = LightShadows.None;
            uf.sparks = sp; uf.glow = fL;
            PrefabUtility.SaveAsPrefabAsset(fx, R + "/Prefabs/UpgradeFront.prefab"); UnityEngine.Object.DestroyImmediate(fx);

            // ============================ BURST (référence : rayon 6 m) ============================
            var imp = new GameObject("UpgradeBurst"); var bu = imp.AddComponent<UpgradeBurst>();
            var lg = new GameObject("Light"); lg.transform.SetParent(imp.transform, false); lg.transform.localPosition = new Vector3(0, 3f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.intensity = 0f; l.range = 20f; l.shadows = LightShadows.None; bu.flash = l;
            var pil = Prim(imp.transform, "Pillar", band, mPillar); pil.SetActive(false); bu.pillar = pil.transform;

            var burst = PS(imp.transform, "DustBurst", mDust); burst.transform.localPosition = new Vector3(0, 0.2f, 0); Burst(burst, 24); Circle(burst, 5.2f, 0.12f, flat); Max(burst, 30);
            Life(burst, 2.2f, 3.6f); Speed(burst, 4f, 7.5f); Size(burst, 1.8f, 3f); Rot(burst); Spin(burst, 0.2f); Grow(burst, 0.45f, 1.6f); Noise(burst, 0.5f, 0.35f); Drag(burst, 1.3f, 0.1f); Grav(burst, -0.02f); Tone(burst, mid, lite); Alpha(burst, 0.65f);
            var low = PS(imp.transform, "DustLow", mDust); low.transform.localPosition = new Vector3(0, 0.3f, 0); Burst(low, 14, 0.05f); Circle(low, 3.5f, 1f, flat); Max(low, 20);
            Life(low, 3.5f, 5.5f); Speed(low, 1f, 3.5f); Size(low, 2.6f, 4.4f); Rot(low); Grow(low, 0.5f, 1.5f); Noise(low, 0.4f, 0.25f); Drag(low, 1f, 0.08f); Tone(low, dark, mid); Alpha(low, 0.5f, 0.15f, 0.55f);
            var air = PS(imp.transform, "AirDust", mDust); Burst(air, 40, 0.15f); Hemi(air, 5f); Max(air, 60);
            Life(air, 3f, 6f); Speed(air, 0.3f, 2.2f); Size(air, 0.35f, 0.8f); Rot(air, 3f); Noise(air, 0.6f, 0.5f); Grav(air, -0.01f); Tone(air, mid, lite); Alpha(air, 0.3f, 0.1f, 0.6f);
            var rd = PS(imp.transform, "ShockwaveDust", mRingDust); rd.transform.localPosition = new Vector3(0, 0.12f, 0); MeshR(rd, shock); Burst(rd, 1); Max(rd, 2); Life(rd, 1.0f, 1.0f); Size(rd, 10f, 10f);
            { var s = rd.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.35f, 0.78f), new Keyframe(1, 1f))); }
            Tone(rd, lite, lite); Alpha(rd, 0.45f, 0.05f, 0.4f);
            var rt = PS(imp.transform, "ShockwaveTeam", mRingTeam); rt.transform.localPosition = new Vector3(0, 0.14f, 0); MeshR(rt, shock); Burst(rt, 1, 0.02f); Max(rt, 2); Life(rt, 0.8f, 0.8f); Size(rt, 9f, 9f);
            { var s = rt.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.25f), new Keyframe(0.3f, 0.8f), new Keyframe(1, 1f))); }
            Tone(rt, White, White); Alpha(rt, 1f, 0.05f, 0.35f);
            var em = PS(imp.transform, "Embers", mMote); em.transform.localPosition = new Vector3(0, 0.3f, 0); Burst(em, 70, 0.05f); Cone(em, 28f, 4.5f, 0.2f, up); Max(em, 90);
            Life(em, 1.2f, 2.6f); Speed(em, 3f, 9f); Size(em, 0.07f, 0.18f); Grav(em, 0.2f); Noise(em, 0.7f, 0.5f); Tone(em, White, White); Alpha(em, 1f, 0.05f, 0.5f);
            var cv = PS(imp.transform, "Chevrons", mChevron); cv.transform.localPosition = new Vector3(0, 0.2f, 0); MeshR(cv, chev); Burst(cv, 14, 0.05f); Circle(cv, 1f, 0f, up); { var s = cv.shape; s.scale = new Vector3(7, 5, 1); s.alignToDirection = true; }
            Life(cv, 1.1f, 1.5f); Speed(cv, 0.05f, 0.2f); Size(cv, 0.9f, 1.3f); Max(cv, 20); Tone(cv, White, White); Alpha(cv, 1f, 0.1f, 0.5f); VelUp(cv, 8f, 12f); bu.chevrons = cv;
            var db = PS(imp.transform, "Debris", mDebris); db.transform.localPosition = new Vector3(0, 0.3f, 0); MeshR(db, debris.ToArray()); Tumble(db); Burst(db, 10); Cone(db, 34f, 4.5f, 0.2f, up); Max(db, 14);
            Life(db, 1.8f, 3f); Speed(db, 4f, 9f); Size(db, 0.15f, 0.4f); Grav(db, 2.4f); World(db); ShrinkEnd(db); Tone(db, new Color(0.62f, 0.58f, 0.52f), new Color(0.95f, 0.92f, 0.86f));
            bu.dust = new[] { burst, low, air, rd }; bu.team = new[] { rt, em };
            PrefabUtility.SaveAsPrefabAsset(imp, R + "/Prefabs/UpgradeBurst.prefab"); UnityEngine.Object.DestroyImmediate(imp);

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[UpgradeVFX] OK. Lance Setup URP, puis Create Demo Caserne (ou Add UpgradeSite To Selection).");
        }

        public static void Wire(UpgradeSite s)
        {
            s.swapShader = Shader.Find("UpgradeVFX/UpgradeSwap");
            s.auraFx = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/UpgradeAura.prefab");
            s.frontFx = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/UpgradeFront.prefab");
            s.burstFx = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/UpgradeBurst.prefab");
            EditorUtility.SetDirty(s);
        }

        /// Sélection : 1 objet = effet seul (montée en puissance). 2 objets = A (ancien) puis B (nouveau), le composant est posé sur le parent commun ou sur A.
        [MenuItem("Tools/Upgrade VFX/Add UpgradeSite To Selection")]
        public static void AddToSelection()
        {
            var sel = Selection.gameObjects;
            if (sel.Length == 2)
            {
                var host = sel[0].transform.parent ? sel[0].transform.parent.gameObject : sel[0];
                var s = host.GetComponent<UpgradeSite>() ?? Undo.AddComponent<UpgradeSite>(host); s.stateA = sel[0]; s.stateB = sel[1]; Wire(s);
                Debug.Log("[UpgradeVFX] A = " + sel[0].name + ", B = " + sel[1].name + ". Si l'ordre est inverse, échange-les dans l'inspecteur.");
                return;
            }
            foreach (var go in sel) { var s = go.GetComponent<UpgradeSite>() ?? Undo.AddComponent<UpgradeSite>(go); Wire(s); }
        }

        static GameObject Part(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler; g.GetComponent<MeshRenderer>().sharedMaterial = m; return g;
        }

        [MenuItem("Tools/Upgrade VFX/Create Demo Caserne")]
        public static void CreateDemo()
        {
            var wood = LoadMat("M_DemoWood"); if (!wood) { Debug.LogError("[UpgradeVFX] Lance d'abord Tools/Upgrade VFX/Build All."); return; }
            var thatch = LoadMat("M_DemoThatch"); var door = LoadMat("M_DemoDoor"); var brick = LoadMat("M_DemoBrick"); var slate = LoadMat("M_DemoSlate"); var stone = LoadMat("M_DemoStone"); var win = LoadMat("M_DemoWindow"); var metal = LoadMat("M_DemoDoorMetal");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "DemoGround"; ground.transform.localScale = Vector3.one * 10f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.36f, 0.4f, 0.3f)); gm.SetFloat("_Smoothness", 0.05f); AssetDatabase.CreateAsset(gm, R + "/Materials/M_DemoGround.mat"); ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            var root = new GameObject("DemoCaserne"); var a = new GameObject("State1_Wood"); a.transform.SetParent(root.transform, false); var b = new GameObject("State2_Stone"); b.transform.SetParent(root.transform, false);
            Part(a.transform, "Body", new Vector3(0, 2f, 0), new Vector3(12, 4, 9), Vector3.zero, wood);
            Part(a.transform, "Base", new Vector3(0, 0.2f, 0), new Vector3(12.4f, 0.4f, 9.4f), Vector3.zero, stone);
            Part(a.transform, "Roof", new Vector3(0, 4.2f, 0), new Vector3(12.8f, 0.4f, 9.8f), Vector3.zero, thatch);
            Part(a.transform, "Door", new Vector3(0, 1.4f, 4.55f), new Vector3(2f, 2.8f, 0.2f), Vector3.zero, door);
            Part(b.transform, "Body", new Vector3(0, 3f, 0), new Vector3(12, 6, 9), Vector3.zero, brick);
            Part(b.transform, "Base", new Vector3(0, 0.25f, 0), new Vector3(12.5f, 0.5f, 9.5f), Vector3.zero, stone);
            Part(b.transform, "RoofL", new Vector3(-3.0f, 7.3f, 0), new Vector3(7f, 0.35f, 9.8f), new Vector3(0, 0, 28), slate);
            Part(b.transform, "RoofR", new Vector3(3.0f, 7.3f, 0), new Vector3(7f, 0.35f, 9.8f), new Vector3(0, 0, -28), slate);
            Part(b.transform, "Tower", new Vector3(3.6f, 5f, -2f), new Vector3(3, 10, 3), Vector3.zero, stone);
            Part(b.transform, "TowerCap", new Vector3(3.6f, 10.25f, -2f), new Vector3(3.6f, 0.5f, 3.6f), Vector3.zero, slate);
            Part(b.transform, "Door", new Vector3(0, 1.8f, 4.55f), new Vector3(2.2f, 3.6f, 0.3f), Vector3.zero, metal);
            Part(b.transform, "WindowL", new Vector3(-4f, 3.8f, 4.5f), new Vector3(1.4f, 1.4f, 0.2f), Vector3.zero, win); Part(b.transform, "WindowR", new Vector3(4f, 3.8f, 4.5f), new Vector3(1.4f, 1.4f, 0.2f), Vector3.zero, win);
            b.SetActive(false);
            var site = root.AddComponent<UpgradeSite>(); site.stateA = a; site.stateB = b; Wire(site);
            var cam = Camera.main;
            if (cam) { if (!cam.GetComponent<UpgradeCameraShake>() && cam.GetComponent("BuildCameraShake") == null) cam.gameObject.AddComponent<UpgradeCameraShake>(); cam.transform.position = new Vector3(0, 5.5f, -26f); cam.transform.LookAt(new Vector3(0, 5f, 0)); }
            var demo = root.AddComponent<UpgradeDemo>(); demo.target = site;
            Selection.activeGameObject = root;
            Debug.Log("[UpgradeVFX] Démo créée. Play, puis U = améliorer, R = revenir à l'état 1, 1/2/3 = niveau, T = couleur d'équipe.");
        }
    }
}
#endif
