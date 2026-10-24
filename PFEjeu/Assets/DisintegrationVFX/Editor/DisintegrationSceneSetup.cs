using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Core.DisintegrationVFX.Editor
{
    /// <summary>Place la station de désintégration et des cibles dans ZONE_07_FX de SceneTrainingRoom.</summary>
    public static class DisintegrationSceneSetup
    {
        private const string GroupName = "DisintegrationVFX";

        private static readonly (string prefab, float x, float scale)[] Targets =
        {
            ("Assets/Prefabs/Enemies/Enemy_Golem.prefab", -3.2f, 1f),
            ("Assets/Prefabs/Enemies/Enemy_Brute.prefab", -1.1f, 1f),
            ("Assets/Prefabs/Enemies/Enemy_Test.prefab", 1.1f, 1f),
            ("Assets/Prefabs/Enemies/Enemy_Eclaireur.prefab", 3.2f, 1f),
        };

        [MenuItem("Tools/Disintegration VFX/Setup TrainingRoom FX Zone")]
        public static void Setup()
        {
            GameObject zone = GameObject.Find("ZONE_07_FX");
            if (zone == null)
            {
                Debug.LogError("[Disintegration] ZONE_07_FX introuvable (ouvre SceneTrainingRoom).");
                return;
            }

            Transform old = zone.transform.Find(GroupName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var group = new GameObject(GroupName);
            group.transform.SetParent(zone.transform, false);
            group.transform.position = new Vector3(122f, 0f, 50f);

            // pad
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(group.transform, false);
            pad.transform.localScale = new Vector3(1.4f, 0.05f, 1.4f);
            pad.transform.localPosition = new Vector3(0f, 0.025f, -4f);
            Object.DestroyImmediate(pad.GetComponent<Collider>());

            // station interactable
            var stationGo = new GameObject("Station_Disintegration");
            stationGo.transform.SetParent(group.transform, false);
            stationGo.transform.localPosition = new Vector3(0f, 0.6f, -4f);
            var col = stationGo.AddComponent<SphereCollider>();
            col.radius = 1.2f; col.isTrigger = true;
            var station = stationGo.AddComponent<DisintegrationStation>();

            var styles = new List<DisintegrationStyle>();
            foreach (string n in new[] { "PaperFire", "Spectral", "Void", "Sand", "Ice" })
                styles.Add(AssetDatabase.LoadAssetAtPath<DisintegrationStyle>(DisintegrationBuilder.Root + "/Styles/Style_" + n + ".asset"));

            var targets = new List<DisintegrationController>();
            foreach (var t in Targets)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(t.prefab);
                if (prefab == null) { Debug.LogWarning("[Disintegration] prefab manquant : " + t.prefab); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Target_" + prefab.name;
                inst.transform.localPosition = new Vector3(t.x, 0.05f, 0f);
                inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                Strip(inst);
                var rr = inst.GetComponentsInChildren<Renderer>(true);
                if (rr.Length > 0)
                {
                    Bounds bb = rr[0].bounds;
                    for (int k = 1; k < rr.Length; k++) bb.Encapsulate(rr[k].bounds);
                    inst.transform.position += Vector3.up * (group.transform.position.y + 0.05f - bb.min.y);
                }
                targets.Add(inst.AddComponent<DisintegrationController>());
            }

            station.Targets.AddRange(targets);
            station.Styles.AddRange(styles);

            EditorSceneManager.MarkSceneDirty(zone.scene);
            Debug.Log("[Disintegration] Zone FX prête : " + targets.Count + " cibles.");
        }

        // Cibles inertes : on garde mesh/animator/colliders, on retire toute logique de jeu.
        private static void Strip(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (b != null) Object.DestroyImmediate(b);
                foreach (var a in root.GetComponentsInChildren<NavMeshAgent>(true))
                    Object.DestroyImmediate(a);
                foreach (var o in root.GetComponentsInChildren<NavMeshObstacle>(true))
                    Object.DestroyImmediate(o);
                foreach (var r in root.GetComponentsInChildren<Rigidbody>(true))
                    Object.DestroyImmediate(r);
            }
        }

        [MenuItem("Tools/Disintegration VFX/Showcase (toggle loop)")]
        public static void ToggleShowcase()
        {
            var st = Object.FindAnyObjectByType<DisintegrationStation>();
            if (st == null) { Debug.LogWarning("[Disintegration] Pas de station dans la scène."); return; }
            st.Loop = !st.Loop;
            Debug.Log("[Disintegration] Showcase loop = " + st.Loop);
        }
    }
}
