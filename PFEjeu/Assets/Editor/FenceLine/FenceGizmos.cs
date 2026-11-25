using UnityEditor;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Gizmos : un trace colore par groupe, points numerotes sur le groupe actif, nom du groupe sur les autres.</summary>
    internal static class FenceGizmos
    {
        private const float DotRadius = 0.18f;
        private const float LabelHeight = 0.6f;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void Draw(FenceLinePath path, GizmoType type)
        {
            for (int g = 0; g < path.Groups.Count; g++)
            {
                var group = path.Group(g);
                var pts = group.Points;
                if (pts.Count == 0) continue;
                bool active = FenceEditState.IsActive(path) && FenceEditState.Group == g;

                Color color = group.GizmoColor;
                if (!group.Enabled) color.a = 0.35f;
                Gizmos.color = color;

                for (int i = 0; i < pts.Count; i++)
                {
                    Gizmos.DrawSphere(pts[i], active ? DotRadius : DotRadius * 0.6f);
                    if (i > 0) Gizmos.DrawLine(pts[i - 1], pts[i]);
                    if (active) Handles.Label(pts[i] + Vector3.up * LabelHeight, i.ToString());
                }
                if (group.ClosedLoop && pts.Count > 2) Gizmos.DrawLine(pts[pts.Count - 1], pts[0]);
                if (!active) Handles.Label(pts[0] + Vector3.up * LabelHeight, group.Name);
            }
        }
    }
}
