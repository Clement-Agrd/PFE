#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpiritVFX.EditorTools
{
    /// Menus : Tools/Spirit VFX/Setup URP, Build All, Create Demo
    public static class SpiritVFXBuilder
    {
        const string R = "Assets/VFX/PortalVFX";
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
        static Texture2D Tex(string name, int n, Func<float, float, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n, (y + 0.5f) / n);
            t.SetPixels32(px); string p = R + "/Textures/" + name + ".png";
            File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); AssetDatabase.ImportAsset(p);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p); ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        static Texture2D TexMote() => Tex("T_Mote", 64, (u, v) => { float r = Rad(u, v); return new Color(1, 1, 1, Mathf.Clamp01(Mathf.Exp(-r * r * 12f)) * Mathf.Clamp01(1.2f - r)); });
        static Texture2D TexLeaf() => Tex("T_Leaf", 128, (u, v) =>
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f; float rx = x * 0.7071f + y * 0.7071f, ry = -x * 0.7071f + y * 0.7071f;
            float w = 0.42f * (1f - rx * rx) * Mathf.Clamp01(1.05f - Mathf.Abs(rx)); float edge = Mathf.SmoothStep(w + 0.03f, w - 0.03f, Mathf.Abs(ry));
            float rib = Mathf.SmoothStep(0.03f, 0f, Mathf.Abs(ry)) * 0.35f; float shade = 0.75f + 0.25f * FBM(u, v, 5, 3);
            return new Color(shade - rib, shade - rib * 0.5f, shade - rib, Mathf.Clamp01(edge) * (Mathf.Abs(rx) < 1f ? 1f : 0f));
        });
        static Texture2D TexSmoke() => Tex("T_Smoke", 128, (u, v) =>
        {
            float r = Rad(u, v) + (FBM(u, v, 4, 4) - 0.5f) * 0.6f; float a = Mathf.SmoothStep(1f, 0.1f, r) * (0.45f + 0.9f * FBM(u + 0.3f, v + 0.2f, 5, 4));
            return new Color(1, 1, 1, Mathf.Clamp01(a));
        });

        static Material Std(string name, Texture2D t, bool additive)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); m.renderQueue = 3000; m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", White);
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }
        static Material LoadMat(string n) => AssetDatabase.LoadAssetAtPath<Material>(R + "/Materials/" + n + ".mat");
        static Material Sh(string shader, string name, int queue)
        {
            var m = new Material(Shader.Find(shader)); m.renderQueue = queue; AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat"); return m;
        }

        static ParticleSystem.MinMaxCurve C(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static ParticleSystem PS(Transform parent, string name, Material mat, bool loop = true, bool auto = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            var m = ps.main; m.duration = 5f; m.loop = loop; m.playOnAwake = auto; m.startSpeed = 0f; m.maxParticles = 400;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var sh = ps.shape; sh.enabled = false; var em = ps.emission; em.rateOverTime = 0f; return ps;
        }
        static void Life(ParticleSystem ps, float a, float b) { var m = ps.main; m.startLifetime = C(a, b); }
        static void Size(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSize = C(a, b); }
        static void Speed(ParticleSystem ps, float a, float b) { var m = ps.main; m.startSpeed = C(a, b); }
        static void Grav(ParticleSystem ps, float g) { var m = ps.main; m.gravityModifier = g; }
        static void Tone(ParticleSystem ps, Color a, Color b) { var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(a, b); }
        static void Max(ParticleSystem ps, int n) { var m = ps.main; m.maxParticles = n; }
        static void Rot(ParticleSystem ps, float r = 3f) { var m = ps.main; m.startRotation = C(-r, r); }
        static void Rate(ParticleSystem ps, float r) { var e = ps.emission; e.rateOverTime = r; }
        static void Burst(ParticleSystem ps, int n) { var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)n) }); }
        static void Circle(ParticleSystem ps, float thick) { var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = 1f; s.radiusThickness = thick; }
        static void Noise(ParticleSystem ps, float str, float freq) { var n = ps.noise; n.enabled = true; n.strength = str; n.frequency = freq; n.scrollSpeed = 0.4f; n.quality = ParticleSystemNoiseQuality.Medium; }
        static void Alpha(ParticleSystem ps, float a, float fadeIn = 0.08f, float hold = 0.5f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, fadeIn), new GradientAlphaKey(a * 0.8f, hold), new GradientAlphaKey(0, 1) });
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }
        static void Shrink(ParticleSystem ps) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 0.7f), new Keyframe(1, 0.1f))); }
        static void Grow(ParticleSystem ps, float s0, float s1) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, s0), new Keyframe(1, s1))); }
        static void Vel(ParticleSystem ps, Vector2 x, Vector2 y, Vector2 z)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local; v.x = C(x.x, x.y); v.y = C(y.x, y.y); v.z = C(z.x, z.y);
        }
        static void Drag(ParticleSystem ps, float limit, float dampen) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(limit, limit); lv.dampen = dampen; }

        [MenuItem("Tools/Spirit VFX/Setup URP")]
        public static void SetupURP()
        {
            int n = 0;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset; if (a == null) continue;
                a.supportsCameraDepthTexture = true; a.supportsCameraOpaqueTexture = true; a.supportsHDR = true; EditorUtility.SetDirty(a); n++;
            }
            var d = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (d != null) { d.supportsCameraDepthTexture = true; d.supportsCameraOpaqueTexture = true; d.supportsHDR = true; EditorUtility.SetDirty(d); n++; }
            AssetDatabase.SaveAssets();
            Debug.Log(n == 0 ? "[SpiritVFX] Aucun URP Asset trouvé." : "[SpiritVFX] URP : Depth Texture + Opaque Texture + HDR activés (" + n + " asset(s)).");
        }

        [MenuItem("Tools/Spirit VFX/Build All")]
        public static void BuildAll()
        {
            foreach (var s in new[] { "Universal Render Pipeline/Particles/Unlit", "SpiritVFX/SpiritSurface", "SpiritVFX/SpiritGround", "SpiritVFX/SpiritDistortion" })
                if (Shader.Find(s) == null) { Debug.LogError("[SpiritVFX] Shader introuvable : " + s + " (URP installé ? erreur de compilation ?)"); return; }
            foreach (var d in new[] { "Textures", "Materials", "Prefabs" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();
            var tMote = TexMote(); var tSmoke = TexSmoke(); var tLeaf = TexLeaf();
            var mMote = Std("M_Mote", tMote, true); var mAsh = Std("M_Mist", tSmoke, false); var mLeaf = Std("M_Leaf", tLeaf, false); mLeaf.SetFloat("_Cull", 0);
            var mSurf = Sh("SpiritVFX/SpiritSurface", "M_SpiritSurface", 3001); var mGround = Sh("SpiritVFX/SpiritGround", "M_SpiritGround", 3000); var mDist = Sh("SpiritVFX/SpiritDistortion", "M_SpiritDistortion", 2990);
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var hot = new Color(1, 1, 1, 1);

            var root = new GameObject("SpiritGate"); var g = root.AddComponent<SpiritGate>();
            GameObject Q(string n, Material m) { var o = new GameObject(n); o.transform.SetParent(root.transform, false); o.AddComponent<MeshFilter>().sharedMesh = quad; var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return o; }
            g.distortionR = Q("Distortion", mDist).GetComponent<Renderer>(); g.ground = Q("Ground", mGround).GetComponent<Renderer>(); g.surface = Q("Surface", mSurf).GetComponent<Renderer>();

            var cs = PS(root.transform, "Spores", mMote); Circle(cs, 1f); Rate(cs, 46f); Life(cs, 1.2f, 2.6f); Speed(cs, 0.1f, 0.5f); Size(cs, 0.07f, 0.16f); Grav(cs, -0.02f); Max(cs, 200); Noise(cs, 0.6f, 0.4f); Tone(cs, hot, hot); Alpha(cs, 0.9f, 0.15f, 0.6f);
            var rs = PS(root.transform, "Fireflies", mMote); Circle(rs, 1f); Rate(rs, 18f); Life(rs, 3f, 5.5f); Speed(rs, 0.05f, 0.3f); Size(rs, 0.1f, 0.22f); Grav(rs, -0.01f); Max(rs, 160); Noise(rs, 0.9f, 0.35f); Tone(rs, hot, hot); Alpha(rs, 0.9f, 0.2f, 0.6f);
            var em = PS(root.transform, "Spirits", mMote); Circle(em, 1f); Rate(em, 8f); Life(em, 3f, 5f); Size(em, 0.5f, 1f); Max(em, 60); Noise(em, 0.8f, 0.25f); Grav(em, -0.02f); Tone(em, hot, hot); Alpha(em, 0.25f, 0.2f, 0.6f);
            Vel(em, new Vector2(-0.2f, 0.2f), new Vector2(0f, 0.4f), new Vector2(0.8f, 2.2f));
            var ash = PS(root.transform, "Mist", mAsh); Circle(ash, 0.3f); Rate(ash, 5f); Life(ash, 4f, 7f); Size(ash, 3f, 6f); Rot(ash); Max(ash, 60); Noise(ash, 0.4f, 0.25f); Grow(ash, 0.6f, 1.6f);
            Tone(ash, new Color(0.5f, 0.75f, 0.65f, 1), new Color(0.65f, 0.85f, 0.75f, 1)); Alpha(ash, 0.06f, 0.2f, 0.6f); Vel(ash, new Vector2(-0.15f, 0.15f), new Vector2(0.05f, 0.2f), new Vector2(0.05f, 0.3f));
            var bu = PS(root.transform, "Burst", mMote, false); Circle(bu, 1f); Burst(bu, 200); Life(bu, 1f, 2.6f); Speed(bu, 1f, 6f); Size(bu, 0.1f, 0.3f); Grav(bu, -0.05f); Max(bu, 260); Drag(bu, 5f, 0.15f); Tone(bu, hot, hot); Alpha(bu, 1f, 0.03f, 0.4f); Shrink(bu);
            Vel(bu, new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(1f, 6f));
            var gs = PS(root.transform, "GroundFireflies", mMote); gs.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            { var s = gs.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(9.75f, 4f, 1.5f); }
            Rate(gs, 12f); Life(gs, 3f, 6f); Speed(gs, 0.05f, 0.3f); Size(gs, 0.08f, 0.18f); Grav(gs, -0.01f); Max(gs, 100); Noise(gs, 0.8f, 0.35f); Tone(gs, hot, hot); Alpha(gs, 0.9f, 0.2f, 0.6f);
            var lv = PS(root.transform, "Leaves", mLeaf); Circle(lv, 0.2f); Rate(lv, 2.6f); Life(lv, 4f, 6.5f); Speed(lv, 0.2f, 0.8f); Size(lv, 0.35f, 0.65f); Grav(lv, 0.08f); Max(lv, 140); Noise(lv, 1.2f, 0.3f); Rot(lv, 3.14f);
            Tone(lv, new Color(0.1f, 0.32f, 0.08f, 1), new Color(0.55f, 0.7f, 0.15f, 1)); Alpha(lv, 0.95f, 0.1f, 0.7f); Vel(lv, new Vector2(-0.2f, 0.2f), new Vector2(0f, 0.3f), new Vector2(0.2f, 1.2f));
            { var sp = lv.rotationOverLifetime; sp.enabled = true; sp.z = C(-2f, 2f); }
            g.leaves = lv; g.crackSparks = cs; g.rimSparks = rs; g.embers = em; g.ash = ash; g.burst = bu; g.groundSparks = gs;
            var l1 = new GameObject("Glow"); l1.transform.SetParent(root.transform, false); var gl = l1.AddComponent<Light>(); gl.type = LightType.Point; gl.intensity = 0; gl.range = 40; gl.shadows = LightShadows.None; g.glow = gl;
            var l2 = new GameObject("LowGlow"); l2.transform.SetParent(root.transform, false); var lg = l2.AddComponent<Light>(); lg.type = LightType.Point; lg.intensity = 0; lg.range = 20; lg.shadows = LightShadows.None; g.lowGlow = lg;
            g.Layout();
            PrefabUtility.SaveAsPrefabAsset(root, R + "/Prefabs/SpiritGate.prefab"); UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[SpiritVFX] OK. Lance Setup URP, puis Create Demo.");
        }

        [MenuItem("Tools/Spirit VFX/Create Demo")]
        public static void CreateDemo()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/SpiritGate.prefab");
            if (!prefab) { Debug.LogError("[SpiritVFX] Lance d'abord Tools/Spirit VFX/Build All."); return; }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "DemoGround"; ground.transform.localScale = Vector3.one * 30f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.08f, 0.16f, 0.1f)); gm.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(gm, R + "/Materials/M_DemoGround.mat"); ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.transform.position = Vector3.zero; var gate = go.GetComponent<SpiritGate>();
            var cam = Camera.main;
            if (cam)
            {
                if (!cam.GetComponent<SpiritCameraShake>()) cam.gameObject.AddComponent<SpiritCameraShake>();
                cam.transform.position = new Vector3(9f, 4.6f, 24f); cam.transform.LookAt(new Vector3(0, 4.6f, 0)); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.05f, 0.04f); cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            }
            var light = UnityEngine.Object.FindFirstObjectByType<Light>(); if (light && light.type == LightType.Directional) { light.intensity = 0.2f; light.color = new Color(0.45f, 0.55f, 0.75f); }
            var vgo = new GameObject("DemoVolume"); var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.72f); bloom.highQualityFiltering.Override(true);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.3f);
            AssetDatabase.CreateAsset(prof, R + "/DemoVolumeProfile.asset"); vol.sharedProfile = prof;
            var demo = go.AddComponent<SpiritDemo>(); demo.target = gate;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.021f; RenderSettings.fogColor = new Color(0.03f, 0.08f, 0.06f);
            Selection.activeGameObject = go;
            Debug.Log("[SpiritVFX] Démo créée (Volume Bloom + ACES inclus). Play, puis P = ouvrir / fermer.");
        }
    }
}
#endif
