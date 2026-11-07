using System.IO;
using Core.TowerDefense;
using Core.Vfx;
using Core.WaveSystem;
using UnityEditor;
using UnityEngine;

namespace Core.Vfx.EditorTools
{
    /// <summary>
    /// P4 : anneaux de slots, voile de la herse, braseros du plateau. Idempotent, Undo.
    /// Positions des braseros = [Guessing], a valider sur le plateau.
    /// </summary>
    public static class VfxBaseSetupTool
    {
        private const string Folder = "Assets/VFX/Ambiance";
        private const string RingTexturePath = Folder + "/T_Ring.png";
        private const string RingMaterialPath = Folder + "/M_RingAdditive.mat";
        private const string GlowMaterialPath = Folder + "/M_GlowAdditive.mat";
        private const string ProfilePath = Folder + "/VfxProfile.asset";
        private const int TextureSize = 128;
        private const float BrazierRadius = 13f;

        private static readonly float[] BrazierAngles = { 25f, 70f, 205f, 250f };

        [MenuItem("Tools/Defense/VFX/P4 - Installer slots, herse, plateau")]
        public static void Install()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VfxProfile>(ProfilePath);
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            GameObject herse = GameObject.Find("herse");
            if (profile == null || glow == null || herse == null) throw new System.InvalidOperationException("Profil, lueur ou herse introuvable.");

            Material ring = EnsureRingMaterial();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            InstallSlotRings(profile, ring);
            InstallGateVeil(profile, glow, herse.transform);
            InstallBraziers(profile, glow, herse.transform);
            Undo.CollapseUndoOperations(group);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Vfx] P4 installe.");
        }

        #region Anneaux

        private static Material EnsureRingMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath) == null)
            {
                var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                for (int y = 0; y < TextureSize; y++)
                {
                    for (int x = 0; x < TextureSize; x++)
                    {
                        float u = x / (TextureSize - 1f) * 2f - 1f;
                        float v = y / (TextureSize - 1f) * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.09f);
                        float inner = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) / 0.03f) * 0.5f;
                        float glow = Mathf.Clamp01(1f - r) * 0.12f;
                        float edge = Mathf.Clamp01((1f - r) / 0.08f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(ring + inner + glow) * edge));
                    }
                }
                tex.Apply();
                File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, RingTexturePath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(RingTexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(RingTexturePath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(RingMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Core/Vfx/GlowAdditive"));
                AssetDatabase.CreateAsset(material, RingMaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void InstallSlotRings(VfxProfile profile, Material ring)
        {
            GameObject host = FindOrCreate("VFX_SlotRings", typeof(ParticleSystem), typeof(SlotRingField));
            var slots = new System.Collections.Generic.List<Transform>();
            GameObject slotRoot = GameObject.Find("TowerSlots_Temporary");
            if (slotRoot != null) foreach (Transform child in slotRoot.transform) if (child.name.StartsWith("TowerSlot_")) slots.Add(child);
            var so = new SerializedObject(host.GetComponent<SlotRingField>());
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("material").objectReferenceValue = ring;
            so.FindProperty("waveSpawner").objectReferenceValue = Object.FindAnyObjectByType<WaveSpawner>(FindObjectsInactive.Include);
            SerializedProperty list = so.FindProperty("slots");
            list.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            so.ApplyModifiedProperties();
            var field = host.GetComponent<SlotRingField>();
            field.enabled = false;
            field.enabled = true;
        }

        #endregion

        #region Herse et braseros

        private static void InstallGateVeil(VfxProfile profile, Material glow, Transform herse)
        {
            GameObject host = FindOrCreate("VFX_GateVeil", typeof(GateVeilFx));
            host.transform.position = herse.position;

            ParticleSystem veil = FindOrCreateSystem(host.transform, "Veil", glow, ParticleSystemRenderMode.Billboard);
            ConfigureLoop(veil, lifetime: 3f, size: 7f, speed: 0.15f, rate: 1.6f, color: new Color(1f, 1f, 1f, 0.28f));
            veil.transform.localPosition = new Vector3(0f, 3f, 0f);
            Shape(veil, new Vector3(6f, 3f, 1.5f));
            Fade(veil);

            ParticleSystem embers = FindOrCreateSystem(host.transform, "Embers", glow, ParticleSystemRenderMode.Billboard);
            ConfigureLoop(embers, lifetime: 2.4f, size: 0.22f, speed: 1.4f, rate: 10f, color: new Color(1f, 1f, 1f, 1f));
            embers.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Shape(embers, new Vector3(6f, 0.5f, 1.5f));
            Fade(embers);

            var so = new SerializedObject(host.GetComponent<GateVeilFx>());
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("playerBase").objectReferenceValue = Object.FindAnyObjectByType<PlayerBase>(FindObjectsInactive.Include);
            SerializedProperty list = so.FindProperty("systems");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = veil;
            list.GetArrayElementAtIndex(1).objectReferenceValue = embers;
            so.ApplyModifiedProperties();
        }

        private static void InstallBraziers(VfxProfile profile, Material glow, Transform herse)
        {
            Terrain terrain = Terrain.activeTerrain;
            GameObject root = FindOrCreate("VFX_Braziers");
            for (int i = 0; i < BrazierAngles.Length; i++)
            {
                float a = BrazierAngles[i] * Mathf.Deg2Rad;
                float x = herse.position.x + Mathf.Cos(a) * BrazierRadius;
                float z = herse.position.z + Mathf.Sin(a) * BrazierRadius;
                float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

                Transform t = root.transform.Find("Brazier_" + i);
                if (t == null)
                {
                    var go = new GameObject("Brazier_" + i);
                    Undo.RegisterCreatedObjectUndo(go, "VFX brazier");
                    go.transform.SetParent(root.transform, false);
                    t = go.transform;
                }
                t.position = new Vector3(x, y, z);

                Color c = profile.DefenseColor;
                ParticleSystem flame = FindOrCreateSystem(t, "Flame", glow, ParticleSystemRenderMode.Billboard);
                ConfigureLoop(flame, lifetime: 0.9f, size: 1.1f, speed: 0.9f, rate: 14f, color: new Color(c.r, c.g, c.b, 0.55f));
                flame.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Fade(flame);

                ParticleSystem halo = FindOrCreateSystem(t, "Halo", glow, ParticleSystemRenderMode.Billboard);
                ConfigureLoop(halo, lifetime: 2f, size: 5f, speed: 0f, rate: 0.8f, color: new Color(c.r, c.g, c.b, 0.22f));
                halo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                Fade(halo);

                ParticleSystem sparks = FindOrCreateSystem(t, "Sparks", glow, ParticleSystemRenderMode.Billboard);
                ConfigureLoop(sparks, lifetime: 1.8f, size: 0.12f, speed: 1.6f, rate: 6f, color: new Color(1f, 1f, 1f, 1f));
                sparks.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                Fade(sparks);
            }
        }

        #endregion

        #region Outils

        private static GameObject FindOrCreate(string name, params System.Type[] components)
        {
            GameObject go = GameObject.Find(name);
            if (go != null) return go;
            go = new GameObject(name, components);
            Undo.RegisterCreatedObjectUndo(go, "VFX " + name);
            return go;
        }

        private static ParticleSystem FindOrCreateSystem(Transform parent, string name, Material material, ParticleSystemRenderMode mode)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                var go = new GameObject(name, typeof(ParticleSystem));
                Undo.RegisterCreatedObjectUndo(go, "VFX " + name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return child.GetComponent<ParticleSystem>();
        }

        private static void ConfigureLoop(ParticleSystem ps, float lifetime, float size, float speed, float rate, Color color)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;
            main.gravityModifier = -0.02f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private static void Shape(ParticleSystem ps, Vector3 scale)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = scale;
        }

        private static void Fade(ParticleSystem ps)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = g;
        }

        #endregion
    }
}
