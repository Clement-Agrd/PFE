#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LayeredSky.EditorTools
{
    /// <summary>
    /// Tools ▸ Layered Sky
    ///   1) Créer les presets par défaut   → Assets/LayeredSky/Presets (Jour 16h, Crépuscule, Nuit)
    ///   2) Installer dans la scène        → matériau + SkyController + Directional Light, assignés et prévisualisés
    ///   3) Réinitialiser les presets      → ÉCRASE les 3 presets par défaut (confirmation demandée)
    /// Ne remplace rien d'existant : les assets déjà présents sont réutilisés.
    /// </summary>
    public static class LayeredSkyMenu
    {
        #region Constantes
        private const string Root = "Assets/LayeredSky";
        private const string PresetFolder = Root + "/Presets";
        private const string MaterialPath = Root + "/Mat_LayeredFantasySky.mat";
        private const string ShaderName = "Skybox/LayeredFantasySky";
        private const string StarTextureName = "LayeredSky_StarsNebula";
        #endregion

        [MenuItem("Tools/Layered Sky/1 - Créer les presets par défaut")]
        public static void CreateDefaultPresets()
        {
            CreateAllPresets(overwrite: false);
        }

        [MenuItem("Tools/Layered Sky/3 - Réinitialiser les presets (écrase)")]
        public static void ResetDefaultPresets()
        {
            if (!EditorUtility.DisplayDialog("Layered Sky", "Écraser SkyPreset_Jour / Crepuscule / Nuit avec les valeurs par défaut ?\nVos réglages sur ces 3 assets seront perdus.", "Écraser", "Annuler"))
                return;
            CreateAllPresets(overwrite: true);
        }

        private static void CreateAllPresets(bool overwrite)
        {
            EnsureFolder(Root);
            EnsureFolder(PresetFolder);
            CreatePreset("SkyPreset_Jour", "Jour (16h)", LayeredSkyDefaults.Jour(), overwrite);
            CreatePreset("SkyPreset_Crepuscule", "Crépuscule", LayeredSkyDefaults.Crepuscule(), overwrite);
            CreatePreset("SkyPreset_Nuit", "Nuit", LayeredSkyDefaults.Nuit(), overwrite);
            AssetDatabase.SaveAssets();
            Debug.Log("[LayeredSky] Presets " + (overwrite ? "réinitialisés" : "créés/vérifiés") + " dans " + PresetFolder);
        }

        [MenuItem("Tools/Layered Sky/2 - Installer dans la scène")]
        public static void InstallInScene()
        {
            CreateDefaultPresets();

            var starTexture = FindStarTexture();
            if (starTexture == null)
            {
                Debug.LogError("[LayeredSky] Texture '" + StarTextureName + "' introuvable. Importez LayeredSky_StarsNebula.hdr dans Assets/.");
                return;
            }

            Material material = CreateOrLoadMaterial(starTexture);
            if (material == null) return;

            var jour = AssetDatabase.LoadAssetAtPath<SkyPreset>(PresetFolder + "/SkyPreset_Jour.asset");
            var crepuscule = AssetDatabase.LoadAssetAtPath<SkyPreset>(PresetFolder + "/SkyPreset_Crepuscule.asset");
            var nuit = AssetDatabase.LoadAssetAtPath<SkyPreset>(PresetFolder + "/SkyPreset_Nuit.asset");

            SkyController controller = FindController();
            if (controller == null)
            {
                var go = new GameObject("SkyController");
                Undo.RegisterCreatedObjectUndo(go, "Create SkyController");
                controller = go.AddComponent<SkyController>();
            }

            Light light = FindDirectionalLight();
            if (light == null)
            {
                var lightGo = new GameObject("Directional Light (Sky)");
                Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
                light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
            }

            var so = new SerializedObject(controller);
            so.FindProperty("skyMaterial").objectReferenceValue = material;
            so.FindProperty("mainLight").objectReferenceValue = light;
            so.FindProperty("initialPreset").objectReferenceValue = jour;

            SerializedProperty timeline = so.FindProperty("timeline");
            if (timeline.arraySize == 0)   // ne jamais écraser une timeline déjà réglée
            {
                timeline.arraySize = 3;
                timeline.GetArrayElementAtIndex(0).objectReferenceValue = jour;
                timeline.GetArrayElementAtIndex(1).objectReferenceValue = crepuscule;
                timeline.GetArrayElementAtIndex(2).objectReferenceValue = nuit;
            }
            so.ApplyModifiedProperties();

            controller.PreviewInEditor();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeObject = controller.gameObject;
            Debug.Log("[LayeredSky] Installé. Caméra : Clear Flags = Skybox. Lancez Play : le SkyController gère tout.");
        }

        #region Helpers
        private static void CreatePreset(string fileName, string displayName, SkyState state, bool overwrite)
        {
            string path = PresetFolder + "/" + fileName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<SkyPreset>(path);
            if (existing != null)
            {
                if (!overwrite) return;   // par défaut : ne jamais écraser vos réglages
                // on réécrit l'asset EXISTANT (le GUID est conservé → références Inspector intactes)
                Undo.RecordObject(existing, "Reset Sky Preset");
                existing.EditorInitialize(displayName, in state);
                EditorUtility.SetDirty(existing);
                return;
            }

            var preset = ScriptableObject.CreateInstance<SkyPreset>();
            preset.EditorInitialize(displayName, in state);
            AssetDatabase.CreateAsset(preset, path);
        }

        private static Material CreateOrLoadMaterial(Texture2D starTexture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError("[LayeredSky] Shader '" + ShaderName + "' introuvable (erreur de compilation ? voir la Console).");
                    return null;
                }

                EnsureFolder(Root);
                material = new Material(shader) { name = "Mat_LayeredFantasySky" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.SetTexture("_StarTex", starTexture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D FindStarTexture()
        {
            foreach (string guid in AssetDatabase.FindAssets(StarTextureName + " t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == StarTextureName)
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        private static SkyController FindController()
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindFirstObjectByType<SkyController>();
#else
            return Object.FindObjectOfType<SkyController>();
#endif
        }

        private static Light FindDirectionalLight()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
#if UNITY_2022_2_OR_NEWER
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] lights = Object.FindObjectsOfType<Light>();
#endif
            foreach (Light l in lights)
                if (l.type == LightType.Directional) return l;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
#endif
