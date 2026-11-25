using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Etat d'edition partage entre l'Inspector, la Scene View et les gizmos (groupe actif, point selectionne).</summary>
    internal static class FenceEditState
    {
        internal static FenceLinePath Path;
        internal static int Group;
        internal static int Point = -1;

        internal static bool IsActive(FenceLinePath path) => Path == path;

        internal static void Select(FenceLinePath path, int group, int point)
        {
            Path = path;
            Group = group;
            Point = point;
        }

        internal static FenceGroup ActiveGroup()
        {
            if (Path == null || Group < 0 || Group >= Path.Groups.Count) return null;
            return Path.Group(Group);
        }

        internal static void ClampSelection()
        {
            if (Path == null) return;
            Group = Mathf.Clamp(Group, 0, Mathf.Max(0, Path.Groups.Count - 1));
            var g = ActiveGroup();
            if (g == null || Point >= g.Points.Count) Point = -1;
        }
    }
}
