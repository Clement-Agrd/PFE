#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireVFX.EditorTools
{
    /// Prefabs basés sur les flipbooks simulés (FireFlipbook.shader).
    public static partial class FireVFXBuilder
    {
        static Texture2D LoadTex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(R + "/Textures/" + n + ".png");

        static Material FlipMat(string name, string tex, int mode, float intensity, float cut = 0.12f, float soft = 0.35f, float add = 0.75f, float boost = 5f, float gain = 1.15f, float bias = 0f)
        {
            var m = new Material(Shader.Find("FireVFX/FireFlipbook"));
            m.SetTexture("_MainTex", LoadTex(tex)); m.SetTexture("_Ramp", LoadTex("T_FireRamp"));
            m.SetFloat("_Mode", mode);
            m.EnableKeyword(mode == 0 ? "_MODE_FLAME" : mode == 1 ? "_MODE_SMOKE" : "_MODE_SPARK");
            m.SetFloat("_Intensity", intensity); m.SetFloat("_Cut", cut); m.SetFloat("_Soft", soft); m.SetFloat("_Additive", add);
            m.SetFloat("_Tiles", 8f); m.SetFloat("_EdgeFade", mode == 0 ? 0.12f : 0.2f); m.SetFloat("_HDRBoost", boost); m.SetFloat("_TempGain", gain); m.SetFloat("_TempBias", bias);
            m.renderQueue = 3000;
            AssetDatabase.CreateAsset(m, R + "/Materials/" + name + ".mat");
            return m;
        }

        /// Texture Sheet Animation 8x8 (toute la feuille), frame blending via vertex streams.
        static void Flip(ParticleSystem ps, float pivotY, float cycles = 1f, bool randomStart = false, int tiles = 8)
        {
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = tiles; ts.numTilesY = tiles; ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            ts.startFrame = randomStart ? new ParticleSystem.MinMaxCurve(0f, 1f) : new ParticleSystem.MinMaxCurve(0f);
            ts.cycleCount = Mathf.Max(1, Mathf.RoundToInt(cycles));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard; r.pivot = new Vector3(0f, pivotY, 0f); r.sortMode = ParticleSystemSortMode.Distance;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend });
        }

        static void Bursts(ParticleSystem ps, params (float t, int n)[] b)
        {
            var arr = new ParticleSystem.Burst[b.Length];
            for (int i = 0; i < b.Length; i++) arr[i] = new ParticleSystem.Burst(b[i].t, (short)b[i].n);
            ps.emission.SetBursts(arr);
        }

        static void Alpha(ParticleSystem ps, params (float t, float a)[] k)
        {
            var keys = new GradientAlphaKey[k.Length]; for (int i = 0; i < k.Length; i++) keys[i] = new GradientAlphaKey(k[i].a, k[i].t);
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, keys);
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void TintAlpha(ParticleSystem ps, Color[] cols, float[] times, params (float t, float a)[] k)
        {
            var ck = new GradientColorKey[cols.Length]; for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i], times[i]);
            var ak = new GradientAlphaKey[k.Length]; for (int i = 0; i < k.Length; i++) ak[i] = new GradientAlphaKey(k[i].a, k[i].t);
            var g = new Gradient(); g.SetKeys(ck, ak);
            var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SizeCurve(ParticleSystem ps, params (float t, float s)[] k)
        {
            var kf = new Keyframe[k.Length]; for (int i = 0; i < k.Length; i++) kf[i] = new Keyframe(k[i].t, k[i].s);
            var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(kf));
        }

        [MenuItem("Tools/Fire VFX/Rebuild Everything")]
        public static void RebuildEverything()
        {
            SetupURP();
            FireFlipbookGenerator.GenerateAll();
            BuildAll();
            FireVFXDemoBuilder.Build();
        }

        [MenuItem("Tools/Fire VFX/Build All")]
        public static void BuildAll()
        {
            if (Shader.Find("Universal Render Pipeline/Particles/Unlit") == null) { Debug.LogError("[FireVFX] URP introuvable."); return; }
            if (Shader.Find("FireVFX/FireFlipbook") == null || Shader.Find("FireVFX/FireBall") == null || Shader.Find("FireVFX/FireDistortion") == null)
            { Debug.LogError("[FireVFX] Shaders FireVFX/* introuvables (erreur de compilation ?)."); return; }
            if (!File.Exists(R + "/Textures/FB_Flame.png") || !File.Exists(R + "/Textures/FB_Explosion.png") || !File.Exists(R + "/Textures/FB_Smoke.png"))
            { Debug.LogError("[FireVFX] Flipbooks manquants : lance Tools/Fire VFX/Generate Flipbooks."); return; }
            foreach (var d in new[] { "Textures", "Materials", "Prefabs", "Meshes" }) Directory.CreateDirectory(R + "/" + d);
            AssetDatabase.Refresh();

            var tNoise = TexNoise(); var tGlow = TexGlow(); var tSpark = TexSpark(); var tRing = TexRing(); var tScorch = TexScorch();
            var Orange = new Color(1f, 0.55f, 0.15f); var Hot = new Color(1f, 0.85f, 0.5f); var Red = new Color(0.9f, 0.18f, 0.04f);

            // ---- matériaux
            var mCore = BallMat("M_FireCore", 0.55f, 2.6f, 0.5f, 0.2f, 0.07f, 3f);
            var mOuter = BallMat("M_FireOuter", 0.62f, 2.0f, 1.5f, 0.34f, 0.15f, 2.2f);
            var mLava = BallMat("M_Lava", 1.2f, 3.5f, 0f, 0.25f, 0.1f, 0.8f);
            var mTongues = BallMat("M_FireTongues", 1.1f, 2.6f, 0f, 0.3f, 0.04f, 2.6f);
            var mFlame = FlipMat("M_FlameFlip", "FB_Flame", 0, 0.9f, 0.10f, 0.40f, 0.78f, 3.4f, 0.95f);
            var mFlameB = FlipMat("M_FlameFlipB", "FB_FlameB", 0, 0.95f, 0.10f, 0.35f, 0.82f, 3.8f, 1.0f);
            var mExpl = FlipMat("M_ExplFlip", "FB_Explosion", 0, 0.9f, 0.06f, 0.45f, 0.62f, 2.6f, 0.62f);
            var mExplB = FlipMat("M_ExplFlipB", "FB_ExplosionB", 0, 0.9f, 0.06f, 0.4f, 0.65f, 2.6f, 0.64f);
            var mSmokeF = FlipMat("M_SmokeFlip", "FB_Smoke", 1, 1.0f); mSmokeF.SetFloat("_EdgeFade", 0.3f);
            var mHalo = Std("M_Halo", tGlow, true, new Color(1f, 0.4f, 0.08f), 1.8f);
            var mFlash = Std("M_Flash", tGlow, true, new Color(1f, 0.85f, 0.55f), 5f);
            var mSpark = Std("M_Spark", tSpark, true, new Color(1f, 0.62f, 0.2f), 5f);
            var mTrail = Std("M_SparkTrail", tGlow, true, new Color(1f, 0.3f, 0.05f), 2f);
            var mRing = Std("M_Ring", tRing, true, new Color(1f, 0.5f, 0.12f), 3f);
            var mScorch = Std("M_Scorch", tScorch, false, White, 1f);
            var mHaze = DistMat("M_HeatHaze", tNoise, tGlow, 0.035f, 1.5f);
            var mShockDist = DistMat("M_ShockDistortion", tNoise, tRing, 0.06f, 1f);

            var quad = Prim(PrimitiveType.Quad); var sphere = Prim(PrimitiveType.Sphere);
            var meshCore = LoadMesh("FireBall_Core"); var meshShell = LoadMesh("FireBall_Shell"); var meshTongues = LoadMesh("FireBall_Tongues");
            var chunkMeshes = new List<Mesh>();
            foreach (var cn in new[] { "LavaChunk_A", "LavaChunk_B", "LavaChunk_C" }) { var cm = LoadMesh(cn); if (cm) chunkMeshes.Add(cm); }
            bool hero = meshCore && meshShell;
            Debug.Log(hero ? "[FireVFX] Modèle Fireball Hero détecté : coque en goutte, langues et éclats utilisés." : "[FireVFX] Pas d'OBJ dans " + R + "/Meshes : sphères Unity utilisées.");
            if (hero) mOuter.SetFloat("_Stretch", 0.2f);

            // ============================ IMPACT ============================
            var imp = new GameObject("FireImpact");
            var ic = imp.AddComponent<FireImpact>();
            var lg = new GameObject("Light"); lg.transform.SetParent(imp.transform, false); lg.transform.localPosition = new Vector3(0, 0, 1.2f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f); l.range = 16f; l.intensity = 0f; ic.flashLight = l;

            // t=0 : flash blanc-jaune
            var flash = PS(imp.transform, "Flash", mFlash, false, 1f, false); Life(flash, 0.14f, 0.14f); Size(flash, 8f, 8f); Burst(flash, 1); Grow(flash, 0.4f, 1.2f);
            Colors(flash, White, Hot, 1f, 0.05f, 0.4f);

            // t=0..1.4 : boule de feu = billboards FB_Explosion aux orientations/positions différentes
            var fire = PS(imp.transform, "Fireball", mExpl, true, 1f, false); Max(fire, 8); Bursts(fire, (0f, 3));
            Life(fire, 1.1f, 1.5f); Size(fire, 5.5f, 7.5f); Speed(fire, 0.2f, 1.2f); Hemi(fire, 0.5f); { var m = fire.main; m.startRotation = C(-0.35f, 0.35f); m.gravityModifier = -0.04f; }
            Flip(fire, 0.24f); Alpha(fire, (0f, 1f), (0.3f, 0.9f), (0.7f, 0.3f), (1f, 0f)); Noise(fire, 0.25f, 0.4f);
            var fireB = PS(imp.transform, "FireballSecondary", mExplB, true, 1f, false); Max(fireB, 12); Bursts(fireB, (0.04f, 3), (0.12f, 3), (0.2f, 2));
            Life(fireB, 0.9f, 1.3f); Size(fireB, 2.6f, 4.2f); Speed(fireB, 3f, 8f); Hemi(fireB, 0.4f); { var m = fireB.main; m.startRotation = C(-0.5f, 0.5f); m.gravityModifier = -0.08f; }
            Flip(fireB, 0.24f); Alpha(fireB, (0f, 1f), (0.3f, 0.9f), (0.7f, 0.3f), (1f, 0f)); Noise(fireB, 0.8f, 0.6f);
            { var lv = fireB.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(1.5f, 1.5f); lv.dampen = 0.18f; }

            // t=0.05..0.9 : langues de flammes projetées
            var tongues = PS(imp.transform, "FlameBursts", mFlame, true, 1f, false); Max(tongues, 30); Bursts(tongues, (0.02f, 8), (0.1f, 4));
            Life(tongues, 0.5f, 1.0f); Size(tongues, 2.0f, 4.2f); Speed(tongues, 4f, 11f); Hemi(tongues, 0.25f); Noise(tongues, 1.0f, 0.8f); { var m0 = tongues.main; m0.startRotation = C(-0.6f, 0.6f); }
            { var m = tongues.main; m.gravityModifier = -0.15f; }
            Flip(tongues, 0.33f, 1f, true); FireLife(tongues, 0.08f);
            { var lv = tongues.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(2f, 2f); lv.dampen = 0.2f; }

            // t=0..0.5 : onde de choc (anneau additif) + distorsion
            var ring = PS(imp.transform, "Shockwave", mRing, false, 1f, false); Flat(ring, quad); Life(ring, 0.5f, 0.5f); Size(ring, 12f, 12f); Burst(ring, 1); Max(ring, 2);
            SizeCurve(ring, (0f, 0.08f), (0.25f, 0.7f), (1f, 1f));
            Colors(ring, White, Orange, 1f, 0.05f, 0.4f);
            var distR = PS(imp.transform, "DistortionRing", mShockDist, false, 1f, false); distR.transform.localPosition = new Vector3(0, 0, 0.3f); Max(distR, 2);
            Life(distR, 0.55f, 0.55f); Size(distR, 14f, 14f); Burst(distR, 1); Grow(distR, 0.1f, 1f);
            Colors(distR, White, White, 1f, 0.05f, 0.4f);
            var distH = PS(imp.transform, "DistortionHaze", mHaze, true, 3f, false); Hemi(distH, 0.6f); Max(distH, 12);
            Life(distH, 1.2f, 2f); Speed(distH, 0.4f, 1.5f); Size(distH, 3f, 5f); Rate(distH, 6f); Grow(distH, 0.6f, 1.4f);
            { var m = distH.main; m.gravityModifier = -0.1f; }
            Colors(distH, White, White, 0.8f, 0.2f, 0.5f);

            // étincelles à trails qui rebondissent / braises / éclats de lave
            var sp = PS(imp.transform, "Sparks", mSpark, true, 2f, false); Burst(sp, 55); Hemi(sp, 0.15f); Max(sp, 100);
            Life(sp, 0.8f, 1.9f); Speed(sp, 7f, 21f); Size(sp, 0.07f, 0.16f); Grav(sp, 1f); Noise(sp, 0.7f, 1f); World(sp);
            Colors(sp, White, Red, 1f, 0.02f, 0.7f); SparkTrails(sp, mTrail, 0.17f, new Color(1f, 0.55f, 0.15f), new Color(0.45f, 0.06f, 0.01f));
            var emb = PS(imp.transform, "Embers", mSpark, true, 4f, false); Bursts(emb, (0.05f, 30), (0.5f, 18)); Hemi(emb, 0.8f); Max(emb, 60);
            Life(emb, 2f, 4.8f); Speed(emb, 1f, 7f); Size(emb, 0.04f, 0.1f); Grav(emb, -0.04f); Noise(emb, 1.6f, 0.5f);
            Colors(emb, Hot, Red, 1f, 0.04f, 0.6f);
            var chunks = PS(imp.transform, "LavaChunks", mLava, true, 3f, false); Burst(chunks, 12); Hemi(chunks, 0.2f); Max(chunks, 14);
            { var r = chunks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.alignment = ParticleSystemRenderSpace.Local; if (chunkMeshes.Count > 0) r.SetMeshes(chunkMeshes.ToArray()); else r.mesh = sphere; }
            Life(chunks, 1f, 2.2f); Speed(chunks, 4f, 11f); Size(chunks, 0.12f, 0.3f); Grav(chunks, 1.6f); World(chunks);
            { var m = chunks.main; m.startRotation3D = true; m.startRotationX = C(0, 6.28f); m.startRotationY = C(0, 6.28f); m.startRotationZ = C(0, 6.28f); }
            { var s = chunks.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.6f, 0.9f), new Keyframe(1, 0))); }
            { var t = chunks.trails; t.enabled = true; t.ratio = 0.7f; t.lifetime = C(0.4f, 0.4f); t.worldSpace = true; t.dieWithParticles = true; t.widthOverTrail = new ParticleSystem.MinMaxCurve(0.5f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0))); chunks.GetComponent<ParticleSystemRenderer>().trailMaterial = mTrail; }

            // t=0.2..6 : champignon de fumée, orange éclairé à la base -> brun -> gris-brun
            var smoke = PS(imp.transform, "SmokeMushroom", mSmokeF, true, 4f, false); Max(smoke, 14);
            Bursts(smoke, (0.18f, 2), (0.3f, 2), (0.45f, 2), (0.65f, 2), (0.9f, 2));
            Life(smoke, 4f, 6f); Size(smoke, 4.5f, 7f); Speed(smoke, 0.5f, 2.2f); Hemi(smoke, 0.6f); { var m = smoke.main; m.startRotation = C(-0.3f, 0.3f); m.gravityModifier = -0.04f; }
            Flip(smoke, 0.3f); Noise(smoke, 0.5f, 0.25f);
            { var lv = smoke.limitVelocityOverLifetime; lv.enabled = true; lv.limit = C(1.1f, 1.1f); lv.dampen = 0.06f; }
            SizeCurve(smoke, (0f, 0.55f), (0.35f, 1f), (1f, 1.35f));
            TintAlpha(smoke, new[] { new Color(1f, 0.55f, 0.25f), new Color(0.32f, 0.22f, 0.17f), new Color(0.2f, 0.18f, 0.17f) }, new[] { 0f, 0.28f, 1f }, (0f, 0f), (0.1f, 0.85f), (0.6f, 0.65f), (1f, 0f));

            // t=0.3..3.5 : flammes résiduelles au sol
            var ground = PS(imp.transform, "GroundFire", mFlame, false, 3f, false); ground.transform.localPosition = new Vector3(0, 0, 0.05f); Max(ground, 40);
            { var s = ground.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = 1.6f; s.radiusThickness = 1f; s.rotation = Vector3.zero; }
            { var v = ground.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local; v.z = C(1.0f, 2.2f); v.x = C(0, 0); v.y = C(0, 0); }
            Life(ground, 0.7f, 1.2f); Size(ground, 1.2f, 2.8f); { var m0 = ground.main; m0.startRotation = C(-0.5f, 0.5f); } { var e = ground.emission; e.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0.1f, 22f), new Keyframe(0.7f, 16f), new Keyframe(1f, 0f))); e.rateOverTimeMultiplier = 1f; }
            Noise(ground, 0.4f, 0.8f); Flip(ground, 0.33f, 1f, true); FireLife(ground, 0.12f);

            var scorch = PS(imp.transform, "Scorch", mScorch, false, 1f, false); scorch.transform.localPosition = new Vector3(0, 0, 0.02f); Flat(scorch, quad); Max(scorch, 2);
            Life(scorch, 9f, 9f); Size(scorch, 5.4f, 6.2f); Burst(scorch, 1, 0.05f); Grow(scorch, 0.5f, 1f); Rot(scorch);
            { var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(White, 0), new GradientColorKey(White, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.04f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0, 1) }); var c = scorch.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g); }

            var impPrefab = PrefabUtility.SaveAsPrefabAsset(imp, R + "/Prefabs/FireImpact.prefab");
            Object.DestroyImmediate(imp);

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
                var tg = new GameObject("Tongues"); tg.transform.SetParent(vis.transform, false); tg.transform.localScale = Vector3.one * 1.1f; fv.spinTarget = tg.transform;
                tg.AddComponent<MeshFilter>().sharedMesh = meshTongues; tg.AddComponent<MeshRenderer>().sharedMaterial = mTongues;
            }
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>()) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }

            var halo = PS(root.transform, "Halo", mHalo); Life(halo, 0.5f, 0.5f); Size(halo, 2.4f, 2.8f); Max(halo, 10);
            Rate(halo, 14f); Colors(halo, Orange, Red, 0.28f, 0.3f, 0.6f);
            var whiteCore = PS(root.transform, "HotCore", mFlash); Life(whiteCore, 0.3f, 0.3f); Size(whiteCore, 1.1f, 1.4f); Max(whiteCore, 8);
            Rate(whiteCore, 14f); Colors(whiteCore, White, Hot, 0.32f, 0.3f, 0.6f);
            var hazeP = PS(root.transform, "DistortionHeat", mHaze, true); Life(hazeP, 0.5f, 0.5f); Size(hazeP, 2.4f, 2.8f); Max(hazeP, 10); Rate(hazeP, 14f);
            Colors(hazeP, White, White, 0.6f, 0.2f, 0.5f);

            var trails = new GameObject("Trails"); trails.transform.SetParent(root.transform, false); fp.trails = trails.transform;

            // (a) grandes flammes de sillage, flottabilité forte
            var ta = PS(trails.transform, "TrailFlamesBig", mFlame, true); Life(ta, 0.5f, 0.9f); Size(ta, 1.0f, 1.7f); Speed(ta, 0.1f, 0.8f); Max(ta, 160); Grav(ta, -0.2f); { var m0 = ta.main; m0.startRotation = C(-0.3f, 0.3f); m0.startSize3D = true; m0.startSizeX = C(1.5f, 2.2f); m0.startSizeY = C(0.8f, 1.2f); m0.startSizeZ = C(1f, 1f); }
            Noise(ta, 0.6f, 0.9f); Rate(ta, 44f, 14f); Flip(ta, 0.33f, 1f, true); FireLife(ta, 0.1f);
            { var s = ta.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.2f; SizeCurve(ta, (0f, 0.7f), (0.3f, 1.1f), (1f, 0.55f)); }
            // (b) flammes fines et rapides
            var tb = PS(trails.transform, "TrailFlamesFast", mFlameB, true); Life(tb, 0.22f, 0.32f); Size(tb, 0.5f, 0.9f); { var m1 = tb.main; m1.startSize3D = true; m1.startSizeX = C(0.9f, 1.4f); m1.startSizeY = C(0.4f, 0.7f); m1.startSizeZ = C(1f, 1f); } Speed(tb, 0.3f, 1.2f); Max(tb, 160); Grav(tb, -0.15f);
            Noise(tb, 0.9f, 1.6f); Rate(tb, 50f, 20f); Flip(tb, 0.33f, 1f, true); FireLife(tb, 0.08f);
            { var s = tb.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.25f; }
            // (c) fumée brun chaud qui se refroidit
            var tsm = PS(trails.transform, "TrailSmoke", mSmokeF, true); Life(tsm, 1.6f, 2.8f); Size(tsm, 1.2f, 1.9f); Speed(tsm, 0.1f, 0.5f); Max(tsm, 60); Grav(tsm, -0.08f);
            Noise(tsm, 0.4f, 0.5f); Rate(tsm, 10f, 8f); Flip(tsm, 0.3f, 1f, true); SizeCurve(tsm, (0f, 0.5f), (1f, 1.5f));
            { var s = tsm.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 0.15f; }
            TintAlpha(tsm, new[] { new Color(1f, 0.5f, 0.22f), new Color(0.26f, 0.19f, 0.15f), new Color(0.18f, 0.16f, 0.15f) }, new[] { 0f, 0.25f, 1f }, (0f, 0f), (0.12f, 0.5f), (0.6f, 0.4f), (1f, 0f));
            // (d) braises / étincelles à trails
            var te = PS(trails.transform, "TrailEmbers", mSpark, true); Life(te, 0.6f, 1.5f); Size(te, 0.04f, 0.1f); Speed(te, 0.5f, 3f); Max(te, 120);
            Noise(te, 1f, 1.2f); Rate(te, 40f, 10f); Grav(te, -0.03f);
            { var s = te.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 25f; s.radius = 0.12f; s.rotation = new Vector3(180, 0, 0); }
            Colors(te, White, Red, 1f, 0.05f, 0.6f); SparkTrails(te, mTrail, 0.2f, Hot, Red);

            // (f) lumière orange à bruit + lumière rouge large et faible
            var pl = new GameObject("Light"); pl.transform.SetParent(root.transform, false);
            var pLight = pl.AddComponent<Light>(); pLight.type = LightType.Point; pLight.color = new Color(1f, 0.52f, 0.18f); pLight.range = 10f; pLight.intensity = 30f; fp.glow = pLight;
            var pl2 = new GameObject("LightWide"); pl2.transform.SetParent(root.transform, false);
            var pLight2 = pl2.AddComponent<Light>(); pLight2.type = LightType.Point; pLight2.color = new Color(0.9f, 0.14f, 0.04f); pLight2.range = 24f; pLight2.intensity = 7f;

            PrefabUtility.SaveAsPrefabAsset(root, R + "/Prefabs/FireProjectile.prefab");
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            // sans ça les prefabs gardent en mémoire des références vers d anciennes instances de matériaux (rose en Play)
            AssetDatabase.ImportAsset(R + "/Prefabs/FireImpact.prefab", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(R + "/Prefabs/FireProjectile.prefab", ImportAssetOptions.ForceUpdate);
            Debug.Log("[FireVFX] OK (flipbooks). Prefabs : " + R + "/Prefabs/.");
        }
    }
}
#endif
