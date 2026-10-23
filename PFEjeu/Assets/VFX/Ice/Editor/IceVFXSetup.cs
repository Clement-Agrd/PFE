#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace IceVFX.EditorTools
{
    /// Menus : Tools/Ice VFX/Setup URP et Tools/Ice VFX/Build Demo Scene.
    public static class IceVFXSetup
    {
        const string DemoScenePath = IceVFXBuilder.R + "/Demo.unity";

        // ---------------------------------------------------------------- URP
        [MenuItem("Tools/Ice VFX/Setup URP")]
        public static void SetupURP()
        {
            var done = new System.Collections.Generic.HashSet<UniversalRenderPipelineAsset>();
            void Apply(string where, RenderPipelineAsset rp)
            {
                if (rp is not UniversalRenderPipelineAsset urp) { Debug.LogWarning($"[IceVFX] {where}: pas d'asset URP ({(rp ? rp.name : "null")})."); return; }
                if (!done.Add(urp)) return;
                urp.supportsCameraOpaqueTexture = true;
                urp.supportsCameraDepthTexture = true;
                urp.supportsHDR = true;
                EditorUtility.SetDirty(urp);
                Debug.Log($"[IceVFX] URP '{urp.name}' ({where}) : Opaque Texture = ON, Depth Texture = ON, HDR = ON");
            }

            if (GraphicsSettings.defaultRenderPipeline) Apply("GraphicsSettings par défaut", GraphicsSettings.defaultRenderPipeline);
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                Apply("Quality '" + QualitySettings.names[i] + "'", QualitySettings.renderPipeline);
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- Demo
        static Material Lit(string name, Color c, float smooth, float metal = 0f)
        {
            string p = IceVFXBuilder.R + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; g.transform.SetParent(parent, false);
            g.transform.position = pos; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        [MenuItem("Tools/Ice VFX/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            SetupURP();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IceVFXBuilder.R + "/Prefabs/IceProjectile.prefab");
            if (!prefab) { Debug.LogError("[IceVFX] Lance d'abord Tools/Ice VFX/Build All."); return; }
            Directory.CreateDirectory(IceVFXBuilder.R + "/Demo");

            // scène vide : additive si une scène ouverte a des modifications non sauvegardées (on ne les écrase pas)
            bool anyDirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) anyDirty |= SceneManager.GetSceneAt(i).isDirty;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, anyDirty ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            if (anyDirty) Debug.LogWarning("[IceVFX] Une scène ouverte a des modifs non sauvegardées : Demo ouverte en additif.");

            var root = new GameObject("DemoRoot").transform;

            // matériaux
            var mGround = Lit("M_DemoGround", new Color(0.05f, 0.06f, 0.08f), 0.6f);
            var mWall = Lit("M_DemoWall", new Color(0.16f, 0.18f, 0.22f), 0.25f);
            var mCube = Lit("M_DemoCube", new Color(0.3f, 0.32f, 0.38f), 0.45f);
            var mTarget = Lit("M_DemoTarget", new Color(0.55f, 0.18f, 0.12f), 0.35f);

            // décor
            Box("Ground", PrimitiveType.Cube, new Vector3(0, -0.25f, 12), new Vector3(60, 0.5f, 60), mGround, root);
            Box("WallBack", PrimitiveType.Cube, new Vector3(0, 3, 26), new Vector3(30, 6, 1), mWall, root);
            Box("WallLeft", PrimitiveType.Cube, new Vector3(-9, 2, 14), new Vector3(1, 4, 16), mWall, root);
            Box("WallRight", PrimitiveType.Cube, new Vector3(9, 2, 14), new Vector3(1, 4, 16), mWall, root);
            Box("CubeA", PrimitiveType.Cube, new Vector3(-4, 0.5f, 9), new Vector3(1, 1, 1), mCube, root);
            Box("CubeB", PrimitiveType.Cube, new Vector3(3.5f, 0.75f, 11), new Vector3(1.5f, 1.5f, 1.5f), mCube, root);
            Box("CubeC", PrimitiveType.Cube, new Vector3(-3, 1f, 17), new Vector3(2, 2, 2), mCube, root);
            Box("Target", PrimitiveType.Cube, new Vector3(0, 1.5f, 15), new Vector3(2, 3, 2), mTarget, root);

            // éclairage nocturne bleu
            var sun = new GameObject("Moon").AddComponent<Light>();
            sun.transform.SetParent(root, false); sun.transform.rotation = Quaternion.Euler(48, 28, 0);
            sun.type = LightType.Directional; sun.color = new Color(0.42f, 0.55f, 1f); sun.intensity = 0.9f; sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.05f, 0.08f, 0.16f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.018f; RenderSettings.fogColor = new Color(0.04f, 0.07f, 0.14f);
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_Exposure", 0.35f); sky.SetFloat("_AtmosphereThickness", 0.5f); sky.SetColor("_SkyTint", new Color(0.1f, 0.15f, 0.35f)); sky.SetColor("_GroundColor", new Color(0.03f, 0.04f, 0.07f));
            string skyPath = IceVFXBuilder.R + "/Materials/M_DemoSky.mat";
            AssetDatabase.DeleteAsset(skyPath); AssetDatabase.CreateAsset(sky, skyPath);
            RenderSettings.skybox = sky;

            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root, false); probe.transform.position = new Vector3(0, 3, 12);
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.resolution = 128; probe.size = new Vector3(40, 12, 40); probe.boxProjection = true;

            // Global Volume
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(0.9f); bloom.intensity.Override(1.6f); bloom.scatter.Override(0.7f);
            prof.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            prof.Add<Vignette>(true).intensity.Override(0.25f);
            prof.Add<ChromaticAberration>(true).intensity.Override(0.08f);
            string profPath = IceVFXBuilder.R + "/Demo/DemoVolumeProfile.asset";
            AssetDatabase.DeleteAsset(profPath); AssetDatabase.CreateAsset(prof, profPath);
            AssetDatabase.AddObjectToAsset(bloom, prof);
            foreach (var c in prof.components) if (c != bloom && !AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, prof);
            var vol = new GameObject("GlobalVolume").AddComponent<Volume>();
            vol.transform.SetParent(root, false); vol.isGlobal = true; vol.sharedProfile = prof;

            // caméra + tireur
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0, 1.6f, 0); camGo.transform.rotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 62; cam.nearClipPlane = 0.1f; cam.farClipPlane = 120f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var shooter = camGo.AddComponent<IceShooter>(); shooter.projectile = prefab;

            EditorSceneManager.SaveScene(scene, DemoScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[IceVFX] Scène de démo créée : " + DemoScenePath);
        }
    }
}
#endif
