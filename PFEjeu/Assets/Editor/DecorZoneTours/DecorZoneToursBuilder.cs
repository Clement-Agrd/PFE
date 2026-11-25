using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core.TowerDefense.EditorTools
{
    /// <summary>
    /// Pose une "DallePourLesTours" sous chaque slot de la zone de défense devant la herse,
    /// puis habille la zone (voir <see cref="DecorZonePlacer"/>). Idempotent et annulable (Undo).
    /// </summary>
    public static class DecorZoneToursBuilder
    {
        #region Constants
        private const string SlotsRootName = "TowerSlots_Temporary";
        private const string DecorRootName = "Decor_ZoneTours";
        private const string DalleChildName = "DallePourLesTours";
        private const string DalleAssetPath = "Assets/Arts/3D/DallePourLesTours.fbx";
        private const string MarkerName = "TemporaryTowerCube";
        private const string AnchorChildName = "BuildAnchor";
        private const float MarkerHalfHeight = 0.9f; // cube de 1,8 m, pivot au centre

        private static readonly string[] SlotNames =
        {
            "TowerSlot_05_01", "TowerSlot_05_02", "TowerSlot_05_03", "TowerSlot_05_04"
        };

        // Profil de reference : dalle reglee a la main (TowerSlot_03_04), appliquee a tous les slots.
        private static readonly Vector3 DalleLocalPosition = new Vector3(0f, -0.41f, 0f);
        private static readonly Vector3 DalleLocalScale = new Vector3(1.385881f, 0.736196f, 1.385881f);
        private const string DalleMaterialPath = "Assets/YughuesFreeRockMaterials/Materials/T_YFRM_16_d.mat";
        #endregion

        [MenuItem("Tools/Defense/Decor Zone Tours (regenerer)")]
        public static void Rebuild()
        {
            var slotsRoot = GameObject.Find(SlotsRootName);
            var dalleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DalleAssetPath);
            var terrain = Terrain.activeTerrain;
            if (slotsRoot == null || dalleAsset == null || terrain == null)
            {
                Debug.LogError("[DecorZoneTours] Slots, asset de dalle ou terrain introuvable.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Decor Zone Tours");

            var lanes = DecorZonePlacer.SampleLanes();
            var report = new StringBuilder();
            var slots = new List<Transform>(SlotNames.Length);

            foreach (string slotName in SlotNames)
            {
                Transform slot = slotsRoot.transform.Find(slotName);
                if (slot == null) { report.AppendLine("slot manquant: " + slotName); continue; }
                slots.Add(slot);
                PlaceDalle(slot, dalleAsset, terrain, lanes, report);
            }

            RebuildDecorRoot(slots, terrain, lanes, report);

            EditorSceneManager.MarkSceneDirty(slotsRoot.scene);
            Debug.Log("[DecorZoneTours]\n" + report);
        }

        /// <summary>Pose (ou remplace) une dalle sous le TemporaryTowerCube de TOUS les slots de la scene.</summary>
        [MenuItem("Tools/Defense/Dalles sous tous les slots")]
        public static void PlaceDallesOnAllSlots()
        {
            var slotsRoot = GameObject.Find(SlotsRootName);
            var dalleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DalleAssetPath);
            var terrain = Terrain.activeTerrain;
            if (slotsRoot == null || dalleAsset == null || terrain == null)
            {
                Debug.LogError("[DecorZoneTours] Slots, asset de dalle ou terrain introuvable.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Dalles tous slots");
            var lanes = DecorZonePlacer.SampleAllLanes();
            var report = new StringBuilder();
            int count = 0;
            foreach (Transform slot in slotsRoot.transform)
            {
                if (slot.Find(MarkerName) == null) continue;
                PlaceDalle(slot, dalleAsset, terrain, lanes, report);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(slotsRoot.scene);
            Debug.Log($"[DecorZoneTours] {count} dalles\n{report}");
        }

        #region Dalles
        private static void PlaceDalle(Transform slot, GameObject asset, Terrain terrain,
                                       IReadOnlyList<Vector3> lanes, StringBuilder report)
        {
            Transform existing = slot.Find(DalleChildName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var dalle = (GameObject)PrefabUtility.InstantiatePrefab(asset, slot);
            Undo.RegisterCreatedObjectUndo(dalle, "Dalle");
            dalle.name = DalleChildName;
            dalle.transform.SetLocalPositionAndRotation(DalleLocalPosition, Quaternion.identity);
            dalle.transform.localScale = DalleLocalScale;

            var rend = dalle.GetComponentInChildren<Renderer>();
            var material = AssetDatabase.LoadAssetAtPath<Material>(DalleMaterialPath);
            if (material != null) rend.sharedMaterial = material;

            AddSolidCollider(dalle, rend);
            float topLocal = rend.bounds.max.y - slot.position.y;
            WireBuildAnchor(slot, topLocal);

            float ground = terrain.SampleHeight(slot.position) + terrain.transform.position.y;
            float laneDist = DecorZonePlacer.DistanceToLanes(lanes, slot.position);
            Bounds b = rend.bounds;
            report.AppendLine($"{slot.name}: dalle {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} m, " +
                              $"haut={b.max.y - ground:F3} m, bas={ground - b.min.y:F3} m sous le sol, " +
                              $"lane={laneDist:F1} m");
        }

        /// <summary>BoxCollider plein calque sur le mesh de la dalle (le mesh est un bloc).</summary>
        private static void AddSolidCollider(GameObject dalle, Renderer rend)
        {
            var filter = rend.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var box = Undo.AddComponent<BoxCollider>(rend.gameObject);
            Bounds mesh = filter.sharedMesh.bounds;
            box.center = mesh.center;
            box.size = mesh.size;
        }

        /// <summary>
        /// Cree/maj un enfant "BuildAnchor" au sommet de la dalle et le branche sur DefenseBuildSlot.buildAnchor,
        /// puis pose le cube temporaire dessus. Les batiments (et l'anneau de selection) suivent BuildAnchor.
        /// </summary>
        private static void WireBuildAnchor(Transform slot, float topLocal)
        {
            Transform anchor = slot.Find(AnchorChildName);
            if (anchor == null)
            {
                var go = new GameObject(AnchorChildName);
                Undo.RegisterCreatedObjectUndo(go, "BuildAnchor");
                anchor = go.transform;
                anchor.SetParent(slot, false);
            }
            anchor.localRotation = Quaternion.identity;
            anchor.localPosition = new Vector3(0f, topLocal, 0f);

            var build = slot.GetComponent<DefenseBuildSlot>();
            if (build != null)
            {
                var so = new SerializedObject(build);
                so.FindProperty("buildAnchor").objectReferenceValue = anchor;
                so.ApplyModifiedProperties();
            }

            Transform marker = slot.Find(MarkerName);
            if (marker != null)
            {
                Undo.RecordObject(marker, "Marker sur dalle");
                marker.localPosition = new Vector3(0f, topLocal + MarkerHalfHeight, 0f);
            }
        }
        #endregion

        #region Decor
        private static void RebuildDecorRoot(IReadOnlyList<Transform> slots, Terrain terrain,
                                             IReadOnlyList<Vector3> lanes, StringBuilder report)
        {
            var old = GameObject.Find(DecorRootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(DecorRootName);
            Undo.RegisterCreatedObjectUndo(root, "Decor root");

            int placed = DecorZonePlacer.Scatter(root.transform, slots, terrain, lanes);
            report.AppendLine($"decor: {placed} elements sous {DecorRootName}");
        }
        #endregion
    }
}
