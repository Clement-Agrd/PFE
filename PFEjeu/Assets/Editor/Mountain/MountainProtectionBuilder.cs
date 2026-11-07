using System.Collections.Generic;
using Defense.Relief.EditorTools;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Calcule la distance aux lanes et le masque de protection (1 = ne rien modifier) :
    /// lanes, plateformes de slots, zones habitees, plateau de la base.
    /// </summary>
    public static class MountainProtectionBuilder
    {
        public static void Fill(ZoningMaps maps, MountainProfile profile, ReliefField field, Vector2 herse, bool includePlateau = true)
        {
            FillLaneDistance(maps, field, profile.LaneMargin + profile.LaneFade + 14f);

            for (int k = 0; k < maps.Protect.Length; k++)
            {
                maps.Protect[k] = 1f - MountainMath.Smooth(profile.LaneMargin, profile.LaneMargin + profile.LaneFade, maps.LaneOutside[k]);
            }

            foreach (ReliefField.SlotPlatform slot in field.Slots)
            {
                RaiseProtection(maps, slot.position, profile.SlotRadius, profile.SlotFade);
            }

            foreach (Vector2 point in CollectHabitedPoints(profile))
            {
                RaiseProtection(maps, point, profile.HabitedRadius, profile.HabitedFade);
            }

            if (includePlateau) RaiseProtection(maps, herse, profile.PlateauRadius, profile.PlateauFade);
        }

        #region Lanes

        private static void FillLaneDistance(ZoningMaps maps, ReliefField field, float reach)
        {
            foreach (ReliefField.LaneSample sample in field.Samples)
            {
                float radius = reach + sample.halfWidth;
                ForEachCellInRadius(maps, sample.position, radius, (index, distance) =>
                {
                    float outside = distance - sample.halfWidth;
                    if (outside < maps.LaneOutside[index]) maps.LaneOutside[index] = outside;
                });
            }
        }

        #endregion

        #region Zones

        private static void RaiseProtection(ZoningMaps maps, Vector2 center, float radius, float fade)
        {
            ForEachCellInRadius(maps, center, radius + fade, (index, distance) =>
            {
                float p = 1f - MountainMath.Smooth(radius, radius + fade, distance);
                if (p > maps.Protect[index]) maps.Protect[index] = p;
            });
        }

        private static List<Vector2> CollectHabitedPoints(MountainProfile profile)
        {
            var points = new List<Vector2>();
            foreach (string objectName in profile.HabitedObjectNames)
            {
                GameObject go = GameObject.Find(objectName);
                if (go == null)
                {
                    Debug.LogWarning("[Mountain] Objet habite introuvable (ignore) : " + objectName);
                    continue;
                }
                points.Add(new Vector2(go.transform.position.x, go.transform.position.z));
            }

            if (string.IsNullOrEmpty(profile.HabitedChildrenRoot)) return points;
            GameObject root = GameObject.Find(profile.HabitedChildrenRoot);
            if (root == null) return points;
            foreach (Transform child in root.transform)
            {
                points.Add(new Vector2(child.position.x, child.position.z));
            }
            return points;
        }

        #endregion

        private static void ForEachCellInRadius(ZoningMaps maps, Vector2 center, float radius, System.Action<int, float> visit)
        {
            int iMin = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - maps.Origin.x) / maps.CellX));
            int iMax = Mathf.Min(maps.Res - 1, Mathf.CeilToInt((center.x + radius - maps.Origin.x) / maps.CellX));
            int jMin = Mathf.Max(0, Mathf.FloorToInt((center.y - radius - maps.Origin.z) / maps.CellZ));
            int jMax = Mathf.Min(maps.Res - 1, Mathf.CeilToInt((center.y + radius - maps.Origin.z) / maps.CellZ));
            float radiusSqr = radius * radius;

            for (int j = jMin; j <= jMax; j++)
            {
                for (int i = iMin; i <= iMax; i++)
                {
                    float d2 = (maps.CellCenter(i, j) - center).sqrMagnitude;
                    if (d2 > radiusSqr) continue;
                    visit(maps.Index(i, j), Mathf.Sqrt(d2));
                }
            }
        }
    }
}
