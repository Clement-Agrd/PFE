using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Recale sur le nouveau terrain : points de spline, slots, torches, SpawnZone.
    /// Nouvelle Y = hauteur du terrain + offset d'origine (capture en etape 1).
    /// Cree aussi les slots du groupe 05. Idempotent et compatible Undo.
    /// </summary>
    public static class DefenseReliefAlignTool
    {
        [MenuItem("Tools/Defense/Relief/3 - Recaler objets et splines")]
        public static void Align()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            if (!config.HasCapturedRoutes) throw new System.InvalidOperationException("Lancer l'etape 1 (capture) d'abord.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            EnsureNewSlots(config);
            AlignTransforms(config, DefenseReliefScene.ExistingSlots());
            AlignTransforms(config, DefenseReliefScene.Torches());
            AlignTransforms(config, DefenseReliefScene.SpawnZones());
            AlignSplines(config);

            Undo.CollapseUndoOperations(group);
            DefenseReliefScene.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] Recalage termine.");
        }

        #region Transforms

        private static void AlignTransforms(DefenseReliefConfig config, List<Transform> targets)
        {
            foreach (Transform t in targets)
            {
                string path = DefenseReliefScene.HierarchyPath(t);
                if (!config.TryGetOffset(path, out float offset))
                {
                    Debug.LogWarning("[DefenseRelief] Offset non capture, objet ignore : " + path);
                    continue;
                }
                Vector3 p = t.position;
                float y = DefenseReliefScene.TerrainY(p.x, p.z) + offset;
                if (Mathf.Abs(p.y - y) < 0.0005f) continue;
                Undo.RecordObject(t, "Defense Relief : align");
                t.position = new Vector3(p.x, y, p.z);
                EditorUtility.SetDirty(t);
            }
        }

        #endregion

        #region Splines

        private static void AlignSplines(DefenseReliefConfig config)
        {
            foreach (DefenseReliefConfig.RouteSettings route in config.Routes)
            {
                SplineContainer container = DefenseReliefScene.FindRouteSpline(route.splineName);
                Undo.RecordObject(container, "Defense Relief : align spline");

                Spline spline = container.Spline;
                Transform tr = container.transform;
                spline.Clear();

                foreach (Vector2 xz in ResampleCenterline(route.centerline, config.SplineKnotSpacing))
                {
                    float worldY = DefenseReliefScene.TerrainY(xz.x, xz.y);
                    Vector3 local = tr.InverseTransformPoint(new Vector3(xz.x, worldY, xz.y));
                    spline.Add(new BezierKnot(new float3(local.x, local.y, local.z)), TangentMode.AutoSmooth);
                }
                EditorUtility.SetDirty(container);
            }
        }

        /// <summary>Points regulierement espaces sur la polyligne capturee, extremites incluses.</summary>
        private static List<Vector2> ResampleCenterline(Vector2[] line, float spacing)
        {
            var result = new List<Vector2> { line[0] };
            float carried = 0f;
            for (int i = 1; i < line.Length; i++)
            {
                Vector2 a = line[i - 1];
                Vector2 b = line[i];
                float segment = Vector2.Distance(a, b);
                float cursor = spacing - carried;
                while (cursor <= segment)
                {
                    result.Add(Vector2.Lerp(a, b, cursor / segment));
                    cursor += spacing;
                }
                carried = segment - (cursor - spacing);
            }

            Vector2 last = line[line.Length - 1];
            if (Vector2.Distance(result[result.Count - 1], last) < spacing * 0.5f && result.Count > 1) result.RemoveAt(result.Count - 1);
            result.Add(last);
            return result;
        }

        #endregion

        #region Nouveaux slots

        private static void EnsureNewSlots(DefenseReliefConfig config)
        {
            Transform slotsRoot = DefenseReliefScene.Require(DefenseReliefScene.SlotsRootName).transform;
            Transform torchRoot = DefenseReliefScene.Require(DefenseReliefScene.TorchRootName).transform;
            GameObject slotSource = DefenseReliefScene.Require("TowerSlot_01_01");
            GameObject torchSource = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(torchRoot.GetChild(0).gameObject);
            ReliefField field = DefenseReliefScene.BuildField(config);

            foreach (DefenseReliefConfig.NewSlot entry in config.NewSlots)
            {
                Transform slot = slotsRoot.Find(entry.slotName);
                if (slot == null)
                {
                    GameObject created = Object.Instantiate(slotSource, slotsRoot);
                    created.name = entry.slotName;
                    Undo.RegisterCreatedObjectUndo(created, "Defense Relief : new slot");
                    slot = created.transform;
                    slot.rotation = FaceNearestLane(field, entry.position);
                    EnsureOffset(config, DefenseReliefScene.HierarchyPath(slot), 0f);
                }

                slot.position = new Vector3(entry.position.x, DefenseReliefScene.TerrainY(entry.position.x, entry.position.y), entry.position.y);
                EnsureTorch(config, slot, torchRoot, torchSource);
            }
        }

        private static void EnsureTorch(DefenseReliefConfig config, Transform slot, Transform torchRoot, GameObject torchSource)
        {
            string torchName = DefenseReliefScene.TorchPrefix + "_Flamee_V2 (" + slot.name + ")";
            Transform torch = torchRoot.Find(torchName);
            if (torch == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(torchSource, torchRoot);
                instance.name = torchName;
                Undo.RegisterCreatedObjectUndo(instance, "Defense Relief : new torch");
                torch = instance.transform;
                torch.position = slot.TransformPoint(config.SlotTorchLocalOffset);
                torch.rotation = Quaternion.identity;
                EnsureOffset(config, DefenseReliefScene.HierarchyPath(torch), config.SlotTorchLocalOffset.y);
            }
        }

        private static void EnsureOffset(DefenseReliefConfig config, string path, float offset)
        {
            if (!config.TryGetOffset(path, out _)) config.SetOffset(path, offset);
        }

        private static Quaternion FaceNearestLane(ReliefField field, Vector2 position)
        {
            field.Query(position, out _, out int index, out _);
            Vector2 dir = field.Samples[index].position - position;
            if (dir.sqrMagnitude < 1e-4f) return Quaternion.identity;
            return Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
        }

        #endregion
    }
}
