using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DustVFX.Demo;

namespace DustVFX.EditorTools
{
    /// <summary>Builds Assets/DustVFX/Demo.unity: 4+ surface zones, test character, volume, grazing sun.</summary>
    public static class DustSceneBuilder
    {
        public const string ScenePath = "Assets/DustVFX/Demo.unity";
        const string DemoDir = "Assets/DustVFX/Demo";
        const float ZoneW = 12f;
        const float ZoneD = 26f;

        struct Zone
        {
            public string name;
            public Color color;
            public float smooth;
            public bool planks;
        }

        static readonly Zone[] Zones =
        {
            new Zone { name = "Earth", color = new Color(0.46f, 0.33f, 0.22f), smooth = 0.05f },
            new Zone { name = "Sand",  color = new Color(0.82f, 0.70f, 0.46f), smooth = 0.05f },
            new Zone { name = "Stone", color = new Color(0.52f, 0.53f, 0.56f), smooth = 0.25f },
            new Zone { name = "Snow",  color = new Color(0.90f, 0.93f, 0.98f), smooth = 0.35f },
            new Zone { name = "Grass", color = new Color(0.30f, 0.42f, 0.17f), smooth = 0.05f },
            new Zone { name = "Wood",  color = new Color(0.55f, 0.40f, 0.26f), smooth = 0.2f, planks = true },
            new Zone { name = "Water", color = new Color(0.10f, 0.24f, 0.32f), smooth = 0.9f },
            new Zone { name = "Slope", color = new Color(0.46f, 0.33f, 0.22f), smooth = 0.05f },
        };

        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(DemoDir)) AssetDatabase.CreateFolder("Assets/DustVFX", "Demo");

            var settings = AssetDatabase.LoadAssetAtPath<DustSettings>(DustSurfaceBuilder.SettingsPath);
            if (settings == null) settings = DustSurfaceBuilder.BuildAll();

            Texture2D noise = EnsureTexture("Tex_GroundNoise", false);
            Texture2D planks = EnsureTexture("Tex_Planks", true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- environment
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            sky.SetColor("_SkyTint", new Color(0.55f, 0.62f, 0.75f));
            string skyPath = DemoDir + "/Mat_Sky.mat";
            AssetDatabase.DeleteAsset(skyPath);
            AssetDatabase.CreateAsset(sky, skyPath);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.52f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.30f, 0.26f);
            RenderSettings.fog = false;

            // ---- sun
            var sunGo = new GameObject("Sun (grazing)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.8f, 0.58f);
            sun.intensity = 2.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(9f, 215f, 0f);
            RenderSettings.sun = sun;

            // ---- volume
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.22f);
            bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            string profPath = DemoDir + "/DemoVolumeProfile.asset";
            AssetDatabase.DeleteAsset(profPath);
            AssetDatabase.CreateAsset(profile, profPath);
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // ---- ground zones
            var groundRoot = new GameObject("Ground");
            for (int i = 0; i < Zones.Length; i++)
                BuildZone(groundRoot.transform, i, Zones[i], noise, planks, settings);

            // visual-only base so the strips do not float in the void
            var basePlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            basePlane.name = "Base (visual only)";
            Object.DestroyImmediate(basePlane.GetComponent<Collider>());
            basePlane.transform.SetParent(groundRoot.transform, false);
            basePlane.transform.localPosition = new Vector3(48f, -0.45f, 0f);
            basePlane.transform.localScale = new Vector3(40f, 1f, 40f);
            var baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            baseMat.SetColor("_BaseColor", new Color(0.30f, 0.26f, 0.20f));
            baseMat.SetFloat("_Smoothness", 0f);
            AssetDatabase.DeleteAsset(DemoDir + "/Mat_Base.mat");
            AssetDatabase.CreateAsset(baseMat, DemoDir + "/Mat_Base.mat");
            basePlane.GetComponent<MeshRenderer>().sharedMaterial = baseMat;

            // ---- character
            var player = BuildCharacter(settings);

            // ---- camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cd.requiresDepthTexture = true; // soft particles need the depth texture
            var followCam = camGo.AddComponent<DustDemoCamera>();
            followCam.target = player.transform;
            camGo.transform.position = new Vector3(-6f, 2.4f, -1f);
            player.GetComponent<DustDemoDriver>().referenceCamera = cam;

            // ---- capture runner
            var runnerGo = new GameObject("DustDemoRunner");
            var runner = runnerGo.AddComponent<DustDemoRunner>();
            runner.driver = player.GetComponent<DustDemoDriver>();
            runner.followCam = followCam;
            runner.captureCamera = cam;
            runner.sun = sun;
            runner.footsteps = player.GetComponent<FootstepDust>();
            runner.zoneSpacing = ZoneW;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); // reload so asset references are live
            Debug.Log("[DustVFX] Demo scene built: " + ScenePath);
        }

        static void BuildZone(Transform parent, int i, Zone z, Texture2D noise, Texture2D planks, DustSettings settings)
        {
            bool slope = z.name == "Slope";
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Zone_" + z.name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(ZoneW, 0.4f, ZoneD);
            if (slope)
            {
                go.transform.localPosition = new Vector3(i * ZoneW, 0.637f, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            }
            else
            {
                go.transform.localPosition = new Vector3(i * ZoneW, -0.2f, 0f);
            }

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", z.color);
            mat.SetTexture("_BaseMap", z.planks ? planks : noise);
            mat.SetTextureScale("_BaseMap", z.planks ? new Vector2(3f, 5f) : new Vector2(6f, 10f));
            mat.SetFloat("_Smoothness", z.smooth);
            string matPath = $"{DemoDir}/Mat_Ground_{z.name}.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var col = go.GetComponent<BoxCollider>();
            switch (z.name)
            {
                case "Earth":
                case "Slope":
                    col.sharedMaterial = DustSurfaceBuilder.LoadOrCreatePhysics("Earth", 0.7f, 0.05f);
                    break;
                case "Sand":
                    col.sharedMaterial = DustSurfaceBuilder.LoadOrCreatePhysics("Sand", 0.8f, 0f);
                    break;
                case "Stone":
                    col.sharedMaterial = DustSurfaceBuilder.LoadOrCreatePhysics("Stone", 0.6f, 0.1f);
                    break;
                case "Snow":
                    col.sharedMaterial = DustSurfaceBuilder.LoadOrCreatePhysics("Snow", 0.4f, 0f);
                    break;
                case "Grass":
                    go.tag = "DustGrass";
                    break;
                case "Wood":
                    go.tag = "DustWood";
                    break;
                case "Water":
                    // explicit override: water never raises dust
                    var tag = go.AddComponent<DustSurfaceTag>();
                    tag.surface = settings.surfaces[settings.surfaces.Length - 1];
                    break;
            }
        }

        static GameObject BuildCharacter(DustSettings settings)
        {
            var root = new GameObject("TestCharacter");
            root.transform.position = new Vector3(0f, 0.05f, 0f);
            root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.04f;

            var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bodyMat.SetColor("_BaseColor", new Color(0.30f, 0.38f, 0.52f));
            bodyMat.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.DeleteAsset(DemoDir + "/Mat_Character.mat");
            AssetDatabase.CreateAsset(bodyMat, DemoDir + "/Mat_Character.mat");

            var footMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            footMat.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.09f));
            AssetDatabase.DeleteAsset(DemoDir + "/Mat_CharacterFoot.mat");
            AssetDatabase.CreateAsset(footMat, DemoDir + "/Mat_CharacterFoot.mat");

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            visual.transform.localScale = new Vector3(0.6f, 0.85f, 0.6f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.55f, 0.27f);
            nose.transform.localScale = new Vector3(0.14f, 0.1f, 0.14f);
            nose.GetComponent<MeshRenderer>().sharedMaterial = footMat;

            Transform MakeFoot(string n)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = n;
                Object.DestroyImmediate(f.GetComponent<Collider>());
                f.transform.SetParent(root.transform, false);
                f.transform.localScale = new Vector3(0.14f, 0.1f, 0.3f);
                f.GetComponent<MeshRenderer>().sharedMaterial = footMat;
                return f.transform;
            }

            Transform footL = MakeFoot("FootL");
            Transform footR = MakeFoot("FootR");

            var driver = root.AddComponent<DustDemoDriver>();
            driver.footL = footL;
            driver.footR = footR;
            driver.visual = visual.transform;

            var fd = root.AddComponent<FootstepDust>();
            fd.footL = footL;
            fd.footR = footR;
            fd.walkSpeed = driver.walkSpeed;
            fd.sprintSpeed = driver.sprintSpeed;
            return root;
        }

        // ---------------------------------------------------------------- textures

        static Texture2D EnsureTexture(string name, bool planks)
        {
            string path = $"{DemoDir}/{name}.png";
            if (File.Exists(path))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = TileNoise(u, v, 4) * 0.5f + TileNoise(u, v, 12) * 0.3f + TileNoise(u, v, 40) * 0.2f;
                    float g = 0.62f + n * 0.5f;
                    if (planks)
                    {
                        float plank = (y * 4 / N) % 1f;
                        float row = Mathf.Floor(y * 4f / N);
                        float seam = Mathf.SmoothStep(0f, 0.03f, Mathf.Abs(((y * 4f / N) % 1f) - 0.5f) > 0.47f ? 0f : 1f);
                        float grain = TileNoise(u * 1f, (v + row * 0.37f) * 14f % 1f, 3) * 0.2f;
                        g = (0.7f + n * 0.25f + grain * 0.5f) * Mathf.Lerp(0.45f, 1f, seam);
                        _ = plank;
                    }
                    px[y * N + x] = new Color(g, g, g, 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Tileable value-ish noise built from periodic Perlin.
        static float TileNoise(float u, float v, int freq)
        {
            float a = Mathf.PerlinNoise(u * freq, v * freq);
            float b = Mathf.PerlinNoise((u - 1f) * freq, v * freq);
            float c = Mathf.PerlinNoise(u * freq, (v - 1f) * freq);
            float d = Mathf.PerlinNoise((u - 1f) * freq, (v - 1f) * freq);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) - 0.5f;
        }
    }
}
