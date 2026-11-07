using System.Collections.Generic;
using Core.Vfx;
using UnityEditor;
using UnityEngine;

namespace Core.Vfx.EditorTools
{
    /// <summary>
    /// P3 : installe "VFX_Torches" (lueur simulee + budget de lights) sur les torches existantes de la scene.
    /// Ne modifie ni le prefab ni les scripts des torches. Idempotent, Undo.
    /// </summary>
    public static class VfxTorchSetupTool
    {
        private const string RootName = "VFX_Torches";
        private const string TorchRootName = "Torch";
        private const string SocketName = "FlameSocket";
        private const string ProfilePath = "Assets/VFX/Ambiance/VfxProfile.asset";
        private const string GlowMaterialPath = "Assets/VFX/Ambiance/M_GlowAdditive.mat";

        [MenuItem("Tools/Defense/VFX/P3 - Installer les torches")]
        public static void Install()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VfxProfile>(ProfilePath);
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            GameObject torchRoot = GameObject.Find(TorchRootName);
            if (profile == null || glow == null || torchRoot == null)
                throw new System.InvalidOperationException("Profil, materiau de lueur ou racine Torch introuvable (P2 installe ?).");

            var sockets = new List<Transform>();
            var lights = new List<Light>();
            foreach (Transform torch in torchRoot.transform)
            {
                Transform socket = torch.Find(SocketName);
                if (socket == null) continue;
                sockets.Add(socket);
                Light light = torch.GetComponentInChildren<Light>(true);
                if (light != null) lights.Add(light);
            }

            GameObject host = GameObject.Find(RootName);
            if (host == null)
            {
                host = new GameObject(RootName, typeof(ParticleSystem), typeof(TorchGlowField), typeof(TorchLightBudget));
                Undo.RegisterCreatedObjectUndo(host, "VFX torches");
            }

            Fill(host.GetComponent<TorchGlowField>(), profile, glow, sockets);
            FillBudget(host.GetComponent<TorchLightBudget>(), profile, lights);
            host.GetComponent<TorchGlowField>().enabled = false;
            host.GetComponent<TorchGlowField>().enabled = true;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Vfx] P3 : " + sockets.Count + " lueurs, " + lights.Count + " lights sous budget.");
        }

        private static void Fill(TorchGlowField field, VfxProfile profile, Material glow, List<Transform> sockets)
        {
            var so = new SerializedObject(field);
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("material").objectReferenceValue = glow;
            SerializedProperty list = so.FindProperty("sockets");
            list.arraySize = sockets.Count;
            for (int i = 0; i < sockets.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = sockets[i];
            so.ApplyModifiedProperties();
        }

        private static void FillBudget(TorchLightBudget budget, VfxProfile profile, List<Light> lights)
        {
            var so = new SerializedObject(budget);
            so.FindProperty("profile").objectReferenceValue = profile;
            SerializedProperty list = so.FindProperty("torchLights");
            list.arraySize = lights.Count;
            for (int i = 0; i < lights.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            so.ApplyModifiedProperties();
        }
    }
}
