using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SlashVFX.Demo;

namespace SlashVFX.EditorTools
{
    /// <summary>Builds materials, ribbon meshes, the four slash prefabs, step assets, settings and the demo scene.</summary>
    public static class SlashBuilder
    {
        const string Root = "Assets/SlashVFX";
        const string ScenePath = Root + "/Demo.unity";

        // ------------------------------------------------------------------ materials

        static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{n}.png");

        static Material Mat(string name, string shader, System.Action<Material> tune)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            tune(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Blade(Material m, float edge, float headSoft, float edgeFade, float tilingU)
        {
            m.SetTexture("_MainTex", Tex("T_SlashMask_A"));
            m.SetTexture("_NoiseTex", Tex("T_SlashNoise"));
            m.SetTexture("_Ramp", Tex("T_SlashRamp_Blue"));
            m.SetFloat("_Edge", edge);
            m.SetFloat("_HeadSoft", headSoft);
            m.SetFloat("_EdgeFade", edgeFade);
            m.SetFloat("_TilingU", tilingU);
            m.SetVector("_NoiseTile", new Vector4(3f, 1f, 0f, 0f));
        }

        struct Mats { public Material halo, body, core, streak, flash, ring, scorch, spark, debris; }

        static Mats BuildMaterials()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            var r = new Mats();
            r.halo = Mat("Mat_SlashHalo", "VFX/SlashBlade", m => Blade(m, 0.2f, 0.25f, 0.45f, 1f));
            r.body = Mat("Mat_SlashBody", "VFX/SlashBlade", m => Blade(m, 0.09f, 0.1f, 0.22f, 1f));
            r.core = Mat("Mat_SlashCore", "VFX/SlashBlade", m => Blade(m, 0.05f, 0.07f, 0.3f, 1f));
            r.streak = Mat("Mat_SlashStreak", "VFX/SlashBlade", m => Blade(m, 0.06f, 0.06f, 0.35f, 1.5f));
            r.flash = Mat("Mat_SlashFlash", "VFX/SlashGlow", m => { m.SetFloat("_Mode", 0f); m.SetFloat("_Star", 0.7f); m.SetFloat("_DstBlend", 1f); m.SetColor("_Color", new Color(6f, 4f, 3f, 1f)); });
            r.ring = Mat("Mat_SlashRing", "VFX/SlashGlow", m => { m.SetFloat("_Mode", 1f); m.SetFloat("_Ring", 0.85f); m.SetFloat("_Thick", 0.3f); m.SetFloat("_DstBlend", 1f); m.SetColor("_Color", new Color(4f, 2f, 1f, 1f)); });
            r.scorch = Mat("Mat_SlashScorch", "VFX/SlashGlow", m => { m.SetFloat("_Mode", 2f); m.SetFloat("_DstBlend", 10f); m.SetColor("_Color", new Color(3f, 1.2f, 0.4f, 1f)); });
            r.spark = Mat("Mat_SlashSpark", "VFX/SlashGlow", m => { m.SetFloat("_Mode", 0f); m.SetFloat("_Star", 0f); m.SetFloat("_DstBlend", 1f); m.SetColor("_Color", new Color(5f, 3.5f, 2.5f, 1f)); });
            r.debris = Mat("Mat_SlashDebris", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.09f, 0.07f, 0.06f));
                m.SetFloat("_Smoothness", 0.2f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(1.2f, 0.35f, 0.08f));
            });
            return r;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Flat arc ribbon in XZ, opening to +Z. U = arc length (sweeps from right to left), V = width.</summary>
        static Mesh ArcRibbon(string name, float arc, float radius, float width, float yOffset, int segs = 72)
        {
            var verts = new Vector3[(segs + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float phi = arc * 0.5f - u * arc;
                var radial = new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                float w = width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.72f))), 0.85f);
                Vector3 c = radial * radius + Vector3.up * yOffset;
                verts[i * 2] = c - radial * (w * 0.5f);
                verts[i * 2 + 1] = c + radial * (w * 0.5f);
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[i * 6] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
            }
            var m = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateBounds();
            string path = $"{Root}/Meshes/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        static Mesh Quad()
        {
            string path = $"{Root}/Meshes/SlashQuad.asset";
            AssetDatabase.DeleteAsset(path);
            var m = new Mesh
            {
                name = "SlashQuad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 }
            };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // ------------------------------------------------------------------ prefabs

        struct ComboShape
        {
            public string name;
            public float arc, radius, width;
            public bool cross;
        }

        static readonly ComboShape[] Shapes =
        {
            new ComboShape { name = "Slash_Combo1", arc = 2.0f, radius = 1.5f, width = 0.72f },
            new ComboShape { name = "Slash_Combo2", arc = 2.2f, radius = 1.55f, width = 0.86f },
            new ComboShape { name = "Slash_Combo3", arc = 2.6f, radius = 1.5f, width = 0.78f },
            new ComboShape { name = "Slash_Finisher", arc = 3.0f, radius = 1.6f, width = 0.86f, cross = true },
        };

        static GameObject Child(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g;
        }

        static MeshRenderer Ribbon(Transform parent, string name, Mesh mesh, Material mat)
        {
            GameObject g = Child(parent, name);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static SlashLayer L(Renderer r, SlashLayerKind kind, float delay, float revealSpeed, float intensityMul, float alphaMul, float dissolveLag, bool second)
        {
            return new SlashLayer
            {
                renderer = r, kind = kind, delay = delay, revealSpeed = revealSpeed,
                intensityMul = intensityMul, alphaMul = alphaMul, dissolveLag = dissolveLag, secondBlade = second
            };
        }

        static void BuildBlade(Transform parent, string suffix, ComboShape s, Mats m, bool second, float roll, List<SlashLayer> layers, Mesh[] shared)
        {
            Transform t = Child(parent, "Blade" + suffix).transform;
            t.localRotation = Quaternion.Euler(0f, 0f, roll);

            Mesh halo = ArcRibbon($"{s.name}{suffix}_Halo", s.arc, s.radius, s.width * 2.6f, 0f);
            Mesh body = ArcRibbon($"{s.name}{suffix}_Body", s.arc, s.radius, s.width, 0f);
            Mesh core = ArcRibbon($"{s.name}{suffix}_Core", s.arc * 0.97f, s.radius, s.width * 0.3f, 0.01f);

            layers.Add(L(Ribbon(t, "Halo", halo, m.halo), SlashLayerKind.Halo, 0.01f, 1f, 0.55f, 0.85f, 0.03f, second));
            layers.Add(L(Ribbon(t, "Body", body, m.body), SlashLayerKind.Body, 0f, 1f, 1f, 1f, 0f, second));
            layers.Add(L(Ribbon(t, "Core", core, m.core), SlashLayerKind.Core, 0f, 1f, 1.9f, 1f, -0.02f, second));

            MeshRenderer after = Ribbon(t, "Afterimage", body, m.body);
            after.transform.localScale = Vector3.one * 1.06f;
            layers.Add(L(after, SlashLayerKind.After, 0.03f, 1f, 0.4f, 0.4f, 0.02f, second));

            float[] radialOff = { 0.24f, -0.2f, 0.05f };
            float[] yOff = { 0.07f, -0.05f, 0.12f };
            float[] arcMul = { 0.8f, 0.7f, 0.9f };
            for (int i = 0; i < 3; i++)
            {
                Mesh streak = ArcRibbon($"{s.name}{suffix}_Streak{i + 1}", s.arc * arcMul[i], s.radius + radialOff[i], s.width * 0.1f, yOff[i], 48);
                layers.Add(L(Ribbon(t, "Streak" + (i + 1), streak, m.streak), SlashLayerKind.Streak, 0.02f + i * 0.008f, 1f, 0.7f, 0.8f, 0f, second));
            }
        }

        static ParticleSystem MakeSparks(Transform parent, Material spark, int max)
        {
            GameObject g = Child(parent, "Sparks");
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.gravityModifier = 0.25f;
            main.maxParticles = max;
            main.startColor = Color.white;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.08f;
            var lvl = ps.limitVelocityOverLifetime; lvl.enabled = true; lvl.limit = 100f; lvl.dampen = 1f; lvl.drag = 2.2f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.6f), new Keyframe(1f, 0f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.4f), 0.6f), new GradientColorKey(new Color(0.6f, 0.2f, 0.1f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 3f; noise.scrollSpeed = 2f;
            var trails = ps.trails; trails.enabled = true; trails.ratio = 1f; trails.lifetime = 0.3f; trails.minVertexDistance = 0.05f;
            trails.worldSpace = true; trails.dieWithParticles = true; trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = spark; r.trailMaterial = spark;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static SlashInstance BuildPrefab(ComboShape s, Mats m, Mesh quad)
        {
            var root = new GameObject(s.name);
            var inst = root.AddComponent<SlashInstance>();
            Transform blade = Child(root.transform, "BladeRoot").transform;
            inst.bladeRoot = blade;

            var layers = new List<SlashLayer>();
            if (s.cross)
            {
                BuildBlade(blade, "_1", s, m, false, 45f, layers, null);
                BuildBlade(blade, "_2", s, m, true, -45f, layers, null);
            }
            else
            {
                BuildBlade(blade, "", s, m, false, 0f, layers, null);
            }
            inst.layers = layers.ToArray();

            inst.sparks = MakeSparks(root.transform, m.spark, s.cross ? 60 : 40);

            // impact flash (billboard quad) + light
            GameObject fl = Child(root.transform, "ImpactFlash");
            fl.AddComponent<MeshFilter>().sharedMesh = quad;
            var fr = fl.AddComponent<MeshRenderer>();
            fr.sharedMaterial = m.flash; fr.shadowCastingMode = ShadowCastingMode.Off; fr.receiveShadows = false; fr.enabled = false;
            inst.flash = fl.transform; inst.flashRenderer = fr;

            GameObject lg = Child(root.transform, "ImpactLight");
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point; light.range = 6f; light.intensity = 0f; light.shadows = LightShadows.None; light.enabled = false;
            inst.impactLight = light;

            if (s.cross)
            {
                GameObject sw = Child(root.transform, "Shockwave");
                sw.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                sw.AddComponent<MeshFilter>().sharedMesh = quad;
                var swr = sw.AddComponent<MeshRenderer>();
                swr.sharedMaterial = m.ring; swr.shadowCastingMode = ShadowCastingMode.Off; swr.receiveShadows = false; swr.enabled = false;
                inst.shockwave = sw.transform; inst.shockwaveRenderer = swr;

                GameObject sc = Child(root.transform, "Scorch");
                sc.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                sc.AddComponent<MeshFilter>().sharedMesh = quad;
                var scr = sc.AddComponent<MeshRenderer>();
                scr.sharedMaterial = m.scorch; scr.shadowCastingMode = ShadowCastingMode.Off; scr.receiveShadows = false; scr.enabled = false;
                inst.scorch = sc.transform; inst.scorchRenderer = scr;

                GameObject dp = Child(root.transform, "DebrisPlane");
                inst.debrisPlane = dp.transform;
                GameObject dg = Child(root.transform, "Debris");
                var ps = dg.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false; main.playOnAwake = false; main.duration = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                main.gravityModifier = 1f;
                main.maxParticles = 16;
                var em = ps.emission; em.enabled = false;
                var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.3f;
                var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
                var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.mode = ParticleSystemCollisionMode.Collision3D;
                col.SetPlane(0, dp.transform); col.bounce = 0.35f; col.dampen = 0.3f; col.lifetimeLoss = 0f; col.radiusScale = 0.5f;
                var dr = dg.GetComponent<ParticleSystemRenderer>();
                dr.renderMode = ParticleSystemRenderMode.Mesh;
                dr.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                dr.sharedMaterial = m.debris;
                dr.shadowCastingMode = ShadowCastingMode.Off;
                inst.debris = ps;
            }

            string path = $"{Root}/Prefabs/{s.name}.prefab";
            AssetDatabase.DeleteAsset(path);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<SlashInstance>();
        }

        // ------------------------------------------------------------------ steps / settings

        static SlashComboStep Step(string name, SlashInstance prefab, System.Action<SlashComboStep> init)
        {
            string path = $"{Root}/Steps/{name}.asset";
            var s = AssetDatabase.LoadAssetAtPath<SlashComboStep>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<SlashComboStep>();
                AssetDatabase.CreateAsset(s, path);
            }
            s.prefab = prefab;
            init(s);
            EditorUtility.SetDirty(s);
            return s;
        }

        public static SlashComboStep[] BuildAssets()
        {
            foreach (string sub in new[] { "Prefabs", "Meshes", "Steps", "Resources" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{sub}")) AssetDatabase.CreateFolder(Root, sub);

            Mats m = BuildMaterials();
            Mesh quad = Quad();
            var prefabs = new SlashInstance[Shapes.Length];
            for (int i = 0; i < Shapes.Length; i++) prefabs[i] = BuildPrefab(Shapes[i], m, quad);

            Texture2D blue = Tex("T_SlashRamp_Blue"), violet = Tex("T_SlashRamp_Violet"), fire = Tex("T_SlashRamp_Fire");
            Texture2D maskA = Tex("T_SlashMask_A"), maskB = Tex("T_SlashMask_B"), maskC = Tex("T_SlashMask_C");

            var s1 = Step("Step1_Combo1", prefabs[0], s =>
            {
                s.pitchDegrees = -55f; s.duration = 0.18f; s.intensity = 5.5f; s.scale = 1f; s.mirror = false; s.tiltDegrees = 0f; s.randomRoll = 4f;
                s.hitStop = 0.02f; s.shake = 0.04f; s.sparkCount = 8; s.hitFlashScale = 0.8f; s.ramp = blue; s.mask = maskA;
                s.flashColor = new Color(0.45f, 0.85f, 1f); s.impactFraction = 0.4f; s.shockwave = false; s.bloomPulse = false;
            });
            var s2 = Step("Step2_Combo2", prefabs[1], s =>
            {
                s.pitchDegrees = -55f; s.duration = 0.20f; s.intensity = 6.2f; s.scale = 1.15f; s.mirror = true; s.tiltDegrees = 0f; s.randomRoll = 4f;
                s.hitStop = 0.03f; s.shake = 0.07f; s.sparkCount = 10; s.hitFlashScale = 1f; s.ramp = blue; s.mask = maskB;
                s.flashColor = new Color(0.4f, 0.8f, 1f); s.impactFraction = 0.4f; s.shockwave = false; s.bloomPulse = false;
            });
            var s3 = Step("Step3_Combo3", prefabs[2], s =>
            {
                s.pitchDegrees = -45f; s.duration = 0.26f; s.intensity = 7.8f; s.scale = 1.25f; s.mirror = false; s.tiltDegrees = 38f; s.randomRoll = 3f;
                s.hitStop = 0.05f; s.shake = 0.15f; s.sparkCount = 12; s.hitFlashScale = 1.3f; s.ramp = violet; s.mask = maskC;
                s.flashColor = new Color(0.75f, 0.4f, 1f); s.impactFraction = 0.42f; s.shockwave = false; s.bloomPulse = false;
            });
            var s4 = Step("Step4_Finisher", prefabs[3], s =>
            {
                s.pitchDegrees = -50f; s.duration = 0.40f; s.intensity = 10f; s.scale = 1.4f; s.mirror = false; s.tiltDegrees = 0f; s.randomRoll = 0f;
                s.hitStop = 0.09f; s.shake = 0.3f; s.sparkCount = 22; s.hitFlashScale = 2f; s.ramp = fire; s.mask = maskC;
                s.flashColor = new Color(1f, 0.6f, 0.2f); s.impactFraction = 0.45f; s.secondBladeDelay = 0.06f; s.shockwave = true; s.bloomPulse = true;
            });

            string sp = $"{Root}/Resources/SlashSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<SlashSettings>(sp);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SlashSettings>();
                AssetDatabase.CreateAsset(settings, sp);
            }
            settings.rampIce = blue; settings.rampVoid = Tex("T_SlashRamp_Void"); settings.rampFire = fire; settings.rampHoly = Tex("T_SlashRamp_Holy");
            EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            return new[] { s1, s2, s3, s4 };
        }

        // ------------------------------------------------------------------ demo scene

        public static void BuildScene()
        {
            SlashComboStep[] steps = BuildAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // dark ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.04f, 0.045f, 0.055f));
            gm.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.DeleteAsset(Root + "/Materials/Mat_DemoGround.mat");
            AssetDatabase.CreateAsset(gm, Root + "/Materials/Mat_DemoGround.mat");
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;

            // mannequin target
            var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bodyMat.SetColor("_BaseColor", new Color(0.36f, 0.36f, 0.4f));
            bodyMat.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.DeleteAsset(Root + "/Materials/Mat_Mannequin.mat");
            AssetDatabase.CreateAsset(bodyMat, Root + "/Materials/Mat_Mannequin.mat");
            var man = new GameObject("Mannequin");
            man.transform.position = new Vector3(0f, 0f, 1.35f);
            void Part(PrimitiveType t, Vector3 pos, Vector3 scale, string n)
            {
                var g = GameObject.CreatePrimitive(t);
                g.name = n;
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(man.transform, false);
                g.transform.localPosition = pos; g.transform.localScale = scale;
                g.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;
            }
            Part(PrimitiveType.Capsule, new Vector3(0f, 0.95f, 0f), new Vector3(0.5f, 0.6f, 0.35f), "Torso");
            Part(PrimitiveType.Sphere, new Vector3(0f, 1.62f, 0f), new Vector3(0.24f, 0.26f, 0.24f), "Head");
            Part(PrimitiveType.Capsule, new Vector3(-0.38f, 1.0f, 0f), new Vector3(0.14f, 0.45f, 0.14f), "ArmL");
            Part(PrimitiveType.Capsule, new Vector3(0.38f, 1.0f, 0f), new Vector3(0.14f, 0.45f, 0.14f), "ArmR");
            Part(PrimitiveType.Capsule, new Vector3(-0.16f, 0.4f, 0f), new Vector3(0.17f, 0.4f, 0.17f), "LegL");
            Part(PrimitiveType.Capsule, new Vector3(0.16f, 0.4f, 0f), new Vector3(0.17f, 0.4f, 0.17f), "LegR");

            // light (dim, cool)
            var lg = new GameObject("Dim Key Light");
            var light = lg.AddComponent<Light>();
            light.type = LightType.Directional; light.color = new Color(0.6f, 0.7f, 1f); light.intensity = 0.9f;
            lg.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.09f, 0.1f, 0.13f);
            RenderSettings.skybox = null;

            // volume: bloom threshold 1 intensity 1.5, ACES, vignette 0.2
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f); bloom.intensity.Override(1.5f); bloom.scatter.Override(0.65f);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.2f);
            string pp = Root + "/Demo/DemoVolume.asset";
            AssetDatabase.DeleteAsset(pp);
            AssetDatabase.CreateAsset(profile, pp);
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = profile;

            // camera (gameplay view) + Origin as its child
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.02f, 0.03f);
            cam.fieldOfView = 50f; cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            camGo.transform.position = new Vector3(0f, 2.3f, -4.6f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 1.4f) - camGo.transform.position);

            var origin = new GameObject("Origin").transform;
            origin.position = new Vector3(0f, 1.1f, 0f);
            origin.rotation = Quaternion.identity;

            var ctrlGo = new GameObject("SlashController");
            var ctrl = ctrlGo.AddComponent<SlashComboController>();
            ctrl.steps = steps;
            ctrl.origin = origin;
            ctrl.bloomVolume = vol;
            ctrl.aim = Vector3.forward;

            var runnerGo = new GameObject("SlashDemoRunner");
            var runner = runnerGo.AddComponent<SlashDemoRunner>();
            runner.controller = ctrl; runner.cam = cam; runner.pivot = origin; runner.mannequin = man.transform;
            var show = runnerGo.AddComponent<SlashShowcase>();
            show.controller = ctrl; show.cam = cam; show.pivot = origin;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[SlashVFX] Demo scene built: " + ScenePath);
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("Tools/Slash VFX/Rebuild Everything")]
        public static void RebuildEverything()
        {
            SlashTextureGenerator.GenerateAll();
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(Root + "/Demo")) AssetDatabase.CreateFolder(Root, "Demo");
            BuildScene();
            Debug.Log("[SlashVFX] Rebuild Everything done.");
        }

        [MenuItem("Tools/Slash VFX/Build Prefabs + Steps")]
        public static void BuildAssetsMenu() => BuildAssets();

        [MenuItem("Tools/Slash VFX/Showcase")]
        public static void Showcase()
        {
            if (!System.IO.File.Exists(ScenePath)) RebuildEverything();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PlayerPrefs.SetInt(SlashShowcase.PrefKey, 1);
            EditorApplication.isPlaying = true;
        }
    }
}
