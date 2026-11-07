using System.IO;
using Core.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Core.Vfx.EditorTools
{
    /// <summary>
    /// Cree (ou met a jour) les assets et la hierarchie de la brume : texture procedurale, materiau,
    /// VfxProfile, racine "VFX_Mist" avec une couche au sol et trois couches lointaines.
    /// Idempotent : relancer ne duplique rien. Undo supporte. Ne touche a aucun objet existant.
    /// </summary>
    public static class VfxMistSetupTool
    {
        private const string Folder = "Assets/VFX/Ambiance";
        private const string TexturePath = Folder + "/T_MistPuff.png";
        private const string MaterialPath = Folder + "/M_MistPuff.mat";
        private const string ProfilePath = Folder + "/VfxProfile.asset";
        private const string ShaderName = "Core/Vfx/MistPuff";
        private const string RootName = "VFX_Mist";
        private const string EnemyPathName = "EnemyPath";
        private const string HerseName = "herse";
        private const int TextureSize = 128;
        private const float AreaMargin = 40f;

        [MenuItem("Tools/Defense/VFX/P1 - Installer la brume")]
        public static void Install()
        {
            EnsureFolder();
            Texture2D texture = EnsureTexture();
            Material material = EnsureMaterial(texture);
            VfxProfile profile = EnsureProfile();

            GameObject enemyPath = GameObject.Find(EnemyPathName);
            GameObject herse = GameObject.Find(HerseName);
            if (enemyPath == null || herse == null)
                throw new System.InvalidOperationException("EnemyPath ou herse introuvable dans la scene active.");

            SplineContainer[] lanes = enemyPath.GetComponentsInChildren<SplineContainer>();
            FitAreaToLanes(profile, lanes);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            Transform root = FindOrCreateRoot();
            BuildLayer(root, "Mist_Ground", MistLayer.LayerKind.Ground, 0, profile, material, lanes, herse.transform);
            for (int i = 0; i < profile.FarLayers.Length; i++)
                BuildLayer(root, "Mist_Far_" + (i + 1), MistLayer.LayerKind.Far, i, profile, material, lanes, herse.transform);

            Undo.CollapseUndoOperations(group);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Vfx] Brume installee.");
        }

        #region Assets

        private static void EnsureFolder()
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder));
            AssetDatabase.Refresh();
        }

        private static Texture2D EnsureTexture()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath) != null)
                return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = x / (TextureSize - 1f) * 2f - 1f;
                    float v = y / (TextureSize - 1f) * 2f - 1f;
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    radial = radial * radial * (3f - 2f * radial);
                    float noise = 0.6f + 0.4f * Mathf.PerlinNoise(x * 0.07f + 3.1f, y * 0.07f + 8.7f);
                    float a = radial * noise;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, TexturePath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private static Material EnsureMaterial(Texture2D texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null) throw new System.InvalidOperationException("Shader introuvable : " + ShaderName);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static VfxProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VfxProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VfxProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            return profile;
        }

        private static void FitAreaToLanes(VfxProfile profile, SplineContainer[] lanes)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (SplineContainer lane in lanes)
            {
                for (int i = 0; i <= 40; i++)
                {
                    Vector3 w = lane.EvaluatePosition(i / 40f);
                    min = Vector2.Min(min, new Vector2(w.x, w.z));
                    max = Vector2.Max(max, new Vector2(w.x, w.z));
                }
            }
            Undo.RecordObject(profile, "VFX mist area");
            profile.SetMistArea((min + max) * 0.5f, (max - min) * 0.5f + Vector2.one * AreaMargin);
            EditorUtility.SetDirty(profile);
        }

        #endregion

        #region Hierarchie

        private static Transform FindOrCreateRoot()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null) return existing.transform;
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "VFX mist root");
            return root.transform;
        }

        private static void BuildLayer(
            Transform root, string layerName, MistLayer.LayerKind kind, int farIndex,
            VfxProfile profile, Material material, SplineContainer[] lanes, Transform anchor)
        {
            Transform child = root.Find(layerName);
            if (child == null)
            {
                var go = new GameObject(layerName, typeof(ParticleSystem), typeof(MistLayer));
                Undo.RegisterCreatedObjectUndo(go, "VFX mist layer");
                go.transform.SetParent(root, false);
                child = go.transform;
            }

            var layer = child.GetComponent<MistLayer>();
            var so = new SerializedObject(layer);
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("farIndex").intValue = farIndex;
            so.FindProperty("material").objectReferenceValue = material;
            so.FindProperty("baseAnchor").objectReferenceValue = anchor;
            so.FindProperty("seed").intValue = 1337 + farIndex * 17 + (int)kind * 101;
            SerializedProperty list = so.FindProperty("lanes");
            list.arraySize = lanes.Length;
            for (int i = 0; i < lanes.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = lanes[i];
            so.ApplyModifiedProperties();
            layer.enabled = false;
            layer.enabled = true;
        }

        #endregion
    }
}
