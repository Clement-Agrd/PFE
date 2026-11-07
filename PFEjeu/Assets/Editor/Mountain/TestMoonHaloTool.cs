using System.IO;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// TEST : cree "Test_MoonHalo" (coeur, halo et anneau de 22 deg autour de la lune du skybox). Tout est prefixe Test,
    /// y compris les materiaux (copies des materiaux additifs existants, qui restent inchanges). Aucune lumiere.
    /// </summary>
    public static class TestMoonHaloTool
    {
        private const string RootName = "Test_MoonHalo";
        private const string GlowMaterialPath = "Assets/VFX/Ambiance/M_GlowAdditive.mat";
        private const string RingMaterialPath = "Assets/VFX/Ambiance/M_RingAdditive.mat";
        private const string MaterialFolder = "Assets/Materials/Mountain";
        private const float RingTextureRadius = 0.81f;
        private const float RingAngularRadius = 22f;

        // Direction mesuree en rendant le skybox (pixel le plus brillant, recentre sur le disque).
        private const float MoonAzimuth = 343.87f;
        private const float MoonElevation = 27.85f;

        private readonly struct Layer
        {
            public readonly string name;
            public readonly string source;
            public readonly float halfWidth;
            public readonly Color tint;
            public readonly float intensity;

            public Layer(string name, string source, float halfWidth, Color tint, float intensity)
            {
                this.name = name; this.source = source; this.halfWidth = halfWidth; this.tint = tint; this.intensity = intensity;
            }
        }

        private static readonly Layer[] Layers =
        {
            // Halo diffus large, derriere tout le reste.
            new Layer("Test_Moon_Glow", GlowMaterialPath, 20f, new Color(0.45f, 0.60f, 1f, 1f), 0.28f),
            // Anneau de 22 deg : le demi-cote du quad est choisi pour que l'anneau de la texture tombe a 22 deg.
            new Layer("Test_Moon_Ring", RingMaterialPath, RingAngularRadius / RingTextureRadius, new Color(0.70f, 0.80f, 1f, 1f), 0.02f),
            // Coeur lumineux autour du disque de la lune.
            new Layer("Test_Moon_Core", GlowMaterialPath, 6f, new Color(0.85f, 0.92f, 1f, 1f), 0.35f),
        };

        [MenuItem("Tools/Defense/Mountain/Test - Halo de lune - Creer ou recreer")]
        public static void CreateOrRebuild()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            EnsureFolder(MaterialFolder);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Test : halo de lune");
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            foreach (Layer layer in Layers)
            {
                var go = new GameObject(layer.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = quad;

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = LoadOrCreateMaterial(layer);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var halo = go.AddComponent<TestMoonHalo>();
                var so = new SerializedObject(halo);
                so.FindProperty("azimuth").floatValue = MoonAzimuth;
                so.FindProperty("elevation").floatValue = MoonElevation;
                so.FindProperty("angularHalfWidth").floatValue = layer.halfWidth;
                so.FindProperty("baseIntensity").floatValue = layer.intensity;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Test] " + RootName + " cree : lune a l'azimut " + MoonAzimuth + " et a " + MoonElevation + " deg d'elevation.");
        }

        [MenuItem("Tools/Defense/Mountain/Test - Halo de lune - Retirer")]
        public static void Remove()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Test] " + RootName + " retire.");
        }

        private static Material LoadOrCreateMaterial(Layer layer)
        {
            string path = MaterialFolder + "/" + layer.name + ".mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>(layer.source);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(source) { name = layer.name };

            material.SetColor("_Tint", layer.tint);
            material.SetFloat("_Intensity", layer.intensity);
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
