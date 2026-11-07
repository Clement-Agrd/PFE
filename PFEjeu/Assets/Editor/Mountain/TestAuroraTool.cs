using System.IO;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// TEST : cree "Test_Aurora" (deux rubans d'aurore boreale : vert et violet) dans le ciel nord-est.
    /// Tout est prefixe Test (objets, materiaux, shader, scripts). Aucune lumiere. Idempotent et annulable.
    /// </summary>
    public static class TestAuroraTool
    {
        private const string RootName = "Test_Aurora";
        private const string ShaderName = "Test/AuroraRibbon";
        private const string MaterialFolder = "Assets/Materials/Mountain";

        private readonly struct Ribbon
        {
            public readonly string name;
            public readonly Color bottom;
            public readonly Color top;
            public readonly float intensity;
            public readonly Vector2 azimuth;
            public readonly float baseElevation;
            public readonly float height;
            public readonly float waves;
            public readonly float phase;
            public readonly float speed;

            public Ribbon(string name, Color bottom, Color top, float intensity, Vector2 azimuth, float baseElevation,
                float height, float waves, float phase, float speed)
            {
                this.name = name; this.bottom = bottom; this.top = top; this.intensity = intensity; this.azimuth = azimuth;
                this.baseElevation = baseElevation; this.height = height; this.waves = waves; this.phase = phase; this.speed = speed;
            }
        }

        private static readonly Ribbon[] Ribbons =
        {
            new Ribbon("Test_Aurora_Green", new Color(0.25f, 1f, 0.55f, 1f), new Color(0.45f, 0.35f, 1f, 1f), 0.30f,
                new Vector2(25f, 115f), 15f, 24f, 3f, 0f, 0.05f),
            new Ribbon("Test_Aurora_Violet", new Color(0.55f, 0.45f, 1f, 1f), new Color(0.95f, 0.30f, 0.80f, 1f), 0.18f,
                new Vector2(55f, 140f), 21f, 18f, 2.2f, 1.7f, 0.035f),
        };

        [MenuItem("Tools/Defense/Mountain/Test - Aurore - Creer ou recreer")]
        public static void CreateOrRebuild()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[Test] Shader introuvable : " + ShaderName);
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            EnsureFolder(MaterialFolder);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Test : aurore");

            foreach (Ribbon ribbon in Ribbons)
            {
                var go = new GameObject(ribbon.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = LoadOrCreateMaterial(shader, ribbon);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var aurora = go.AddComponent<TestAurora>();
                var so = new SerializedObject(aurora);
                so.FindProperty("azimuthRange").vector2Value = ribbon.azimuth;
                so.FindProperty("baseElevation").floatValue = ribbon.baseElevation;
                so.FindProperty("curtainHeight").floatValue = ribbon.height;
                so.FindProperty("waves").floatValue = ribbon.waves;
                so.FindProperty("phase").floatValue = ribbon.phase;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Test] " + RootName + " cree : " + Ribbons.Length + " rubans.");
        }

        [MenuItem("Tools/Defense/Mountain/Test - Aurore - Retirer")]
        public static void Remove()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Test] " + RootName + " retire.");
        }

        private static Material LoadOrCreateMaterial(Shader shader, Ribbon ribbon)
        {
            string path = MaterialFolder + "/" + ribbon.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(shader) { name = ribbon.name };

            material.SetColor("_ColorBottom", ribbon.bottom);
            material.SetColor("_ColorTop", ribbon.top);
            material.SetFloat("_Intensity", ribbon.intensity);
            material.SetFloat("_Speed", ribbon.speed);
            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
