#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FireVFX.EditorTools
{
    /// Tools/Fire VFX/Build Demo Scene : scène de test (sol, mur, plafond, cibles, crépuscule, post-process).
    public static class FireVFXDemoBuilder
    {
        const string R = "Assets/VFX/Fire";

        static Material Lit(string name, Color c, float smooth, float metal = 0f)
        {
            var p = R + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, Material m, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name;
            g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        [MenuItem("Tools/Fire VFX/Build Demo Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(R + "/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- décor
            var env = new GameObject("Environment").transform;
            var floorM = Lit("D_Floor", new Color(0.07f, 0.07f, 0.085f), 0.55f);
            var stoneM = Lit("D_Stone", new Color(0.30f, 0.28f, 0.27f), 0.15f);
            var dummyM = Lit("D_Dummy", new Color(0.45f, 0.42f, 0.40f), 0.3f);
            Box("Floor", new Vector3(0, -0.5f, 5f), new Vector3(60, 1, 60), floorM, env);
            var wall = Box("StoneWall", new Vector3(0, 4f, 15.5f), new Vector3(24, 8, 1), stoneM, env);
            Box("Ceiling", new Vector3(-14f, 4.7f, 4f), new Vector3(12, 1, 12), stoneM, env);
            for (int i = 0; i < 6; i++) Box("WallBlock" + i, new Vector3(-10 + i * 4f, 0.6f, 14.4f), new Vector3(1.2f, 1.2f, 0.9f), stoneM, env);

            var targets = new GameObject("Targets").transform;
            Vector3[] tp = { new Vector3(4f, 0.6f, 8f), new Vector3(-3f, 0.6f, 10f), new Vector3(0f, 0.6f, 12f) };
            for (int i = 0; i < tp.Length; i++)
            {
                var t = Box("Target" + i, tp[i], new Vector3(1.2f, 1.2f, 1.2f), dummyM, targets);
                t.AddComponent<TargetDummy>();
                var rb = t.AddComponent<Rigidbody>(); rb.mass = 20f;
            }

            // --- ambiance crépuscule
            var skyShader = Shader.Find("Skybox/Procedural");
            var sky = new Material(skyShader) { name = "D_Sky" };
            sky.SetColor("_SkyTint", new Color(0.42f, 0.34f, 0.62f)); sky.SetColor("_GroundColor", new Color(0.09f, 0.08f, 0.12f));
            sky.SetFloat("_AtmosphereThickness", 1.1f); sky.SetFloat("_Exposure", 0.7f);
            AssetDatabase.CreateAsset(sky, R + "/Materials/D_Sky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.24f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.13f, 0.12f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.12f, 0.12f, 0.22f); RenderSettings.fogDensity = 0.012f;

            var sun = new GameObject("MoonLight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(0.45f, 0.5f, 0.95f); sun.intensity = 0.55f;
            sun.transform.rotation = Quaternion.Euler(38f, -28f, 0f); sun.shadows = LightShadows.Soft;

            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0, 3f, 5f); probe.size = new Vector3(50, 12, 50);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;

            // --- post-process
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true); bloom.threshold.Override(0.9f); bloom.intensity.Override(1.8f); bloom.scatter.Override(0.75f);
            var tone = prof.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.25f);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.2f); ca.contrast.Override(10f);
            AssetDatabase.DeleteAsset(R + "/Demo_Profile.asset");
            AssetDatabase.CreateAsset(prof, R + "/Demo_Profile.asset");
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            var vol = new GameObject("Global Volume").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;

            // --- caméra
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 60f; cam.allowHDR = true; cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(0, 1.7f, -8f); camGo.transform.rotation = Quaternion.identity;
            var ud = camGo.AddComponent<UniversalAdditionalCameraData>(); ud.renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            var shooter = camGo.AddComponent<FireShooter>();
            var projPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/FireProjectile.prefab");
            var impPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(R + "/Prefabs/FireImpact.prefab");
            shooter.projectile = projPrefab;
            var rig = camGo.AddComponent<FireCaptureRig>(); rig.projectilePrefab = projPrefab; rig.impactPrefab = impPrefab; rig.cam = cam;
            camGo.AddComponent<FireFlyCam>();

            EditorSceneManager.SaveScene(scene, R + "/Demo.unity");
            Debug.Log("[FireVFX] Scène démo : " + R + "/Demo.unity");
        }
    }
}
#endif
