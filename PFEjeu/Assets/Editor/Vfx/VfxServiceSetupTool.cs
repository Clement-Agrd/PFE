using Core.Vfx;
using Core.WaveSystem;
using UnityEditor;
using UnityEngine;

namespace Core.Vfx.EditorTools
{
    /// <summary>
    /// P2 : construit les prefabs d'effets et installe (ou met a jour) l'objet "VFX_Service" dans la scene.
    /// Idempotent, compatible Undo. N'altere ni WaveSpawner, ni SpawnZone, ni les ennemis.
    /// </summary>
    public static class VfxServiceSetupTool
    {
        private const string ServiceName = "VFX_Service";
        private const string ProfilePath = "Assets/VFX/Ambiance/VfxProfile.asset";

        [MenuItem("Tools/Defense/VFX/P2 - Installer spawns et ennemis")]
        public static void Install()
        {
            VfxGameplayPrefabBuilder.Result prefabs = VfxGameplayPrefabBuilder.BuildAll();
            var profile = AssetDatabase.LoadAssetAtPath<VfxProfile>(ProfilePath);

            WaveSpawner spawner = Object.FindFirstObjectByType<WaveSpawner>(FindObjectsInactive.Include);
            if (spawner == null) throw new System.InvalidOperationException("WaveSpawner introuvable dans la scene.");
            SpawnZone[] zones = Object.FindObjectsByType<SpawnZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            GameObject host = GameObject.Find(ServiceName);
            if (host == null)
            {
                host = new GameObject(ServiceName, typeof(VfxService));
                Undo.RegisterCreatedObjectUndo(host, "VFX service");
            }

            var service = host.GetComponent<VfxService>();
            var so = new SerializedObject(service);
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("waveSpawner").objectReferenceValue = spawner;
            so.FindProperty("spawnPortalPrefab").objectReferenceValue = prefabs.portal;
            so.FindProperty("enemyBindingPrefab").objectReferenceValue = prefabs.binding;
            so.FindProperty("hitPrefab").objectReferenceValue = prefabs.hit;
            so.FindProperty("deathPrefab").objectReferenceValue = prefabs.death;
            SerializedProperty list = so.FindProperty("zones");
            list.arraySize = zones.Length;
            for (int i = 0; i < zones.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
            so.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Vfx] P2 installe : " + zones.Length + " SpawnZone, WaveSpawner " + spawner.name);
        }
    }
}
