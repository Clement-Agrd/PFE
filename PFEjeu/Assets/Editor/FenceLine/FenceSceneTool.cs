using UnityEditor;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>
    /// Edition dans la Scene View (groupe actif) :
    ///  - Ctrl+clic (Cmd sur Mac) sur le terrain : ajoute un point en fin de trace.
    ///  - Ctrl+Shift+clic : insere un point sur le segment le plus proche.
    ///  - Clic sur une sphere : selectionne (un point d'un autre groupe active ce groupe) ; handle standard pour deplacer.
    ///  - Shift+Suppr : supprime le point selectionne (Suppr seul supprimerait le GameObject).
    /// Les points restent colles au terrain (Y = sol).
    /// </summary>
    internal static class FenceSceneTool
    {
        private const float TerrainRayLength = 10000f;
        private const float DotSize = 0.1f;
        private const float OtherDotSize = 0.07f;

        internal static void OnSceneGUI(FenceLinePath path, Terrain terrain, TerrainCollider collider, System.Action repaint)
        {
            FenceEditState.ClampSelection();
            Event e = Event.current;
            bool modifier = e.control || e.command;
            if (e.type == EventType.Layout && modifier)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var group = FenceEditState.ActiveGroup();
            if (group != null)
            {
                HandleAdd(path, group, collider, e, modifier, repaint);
                HandleDelete(path, group, e, repaint);
            }
            DrawHandles(path, terrain, repaint);
        }

        #region Add / insert / delete
        private static void HandleAdd(FenceLinePath path, FenceGroup group, TerrainCollider collider, Event e, bool modifier, System.Action repaint)
        {
            if (e.type != EventType.MouseDown || e.button != 0 || !modifier || e.alt || collider == null) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!collider.Raycast(ray, out RaycastHit hit, TerrainRayLength)) return;

            Undo.RecordObject(path, e.shift ? "Inserer point de barriere" : "Ajouter point de barriere");
            if (e.shift && group.Points.Count >= 2)
            {
                int index = NearestSegmentEnd(group, hit.point);
                group.InsertPoint(index, hit.point);
                FenceEditState.Point = index;
            }
            else
            {
                group.AddPoint(hit.point);
                FenceEditState.Point = group.Points.Count - 1;
            }
            EditorUtility.SetDirty(path);
            e.Use();
            repaint();
        }

        /// <summary>Index d'insertion (fin du segment le plus proche, boucle comprise).</summary>
        private static int NearestSegmentEnd(FenceGroup group, Vector3 point)
        {
            var pts = group.Points;
            int segments = group.ClosedLoop && pts.Count > 2 ? pts.Count : pts.Count - 1;
            int best = pts.Count;
            float bestDist = float.MaxValue;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                float d = DistanceToSegmentXZ(point, a, b);
                if (d >= bestDist) continue;
                bestDist = d;
                best = i + 1;
            }
            return best;
        }

        private static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 ap = new Vector2(p.x - a.x, p.z - a.z), ab = new Vector2(b.x - a.x, b.z - a.z);
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / len2) : 0f;
            return (ap - ab * t).magnitude;
        }

        private static void HandleDelete(FenceLinePath path, FenceGroup group, Event e, System.Action repaint)
        {
            if (e.type != EventType.KeyDown || !e.shift || e.keyCode != KeyCode.Delete) return;
            if (FenceEditState.Point < 0 || FenceEditState.Point >= group.Points.Count) return;
            Undo.RecordObject(path, "Supprimer point");
            group.RemovePointAt(FenceEditState.Point);
            FenceEditState.Point = Mathf.Min(FenceEditState.Point, group.Points.Count - 1);
            EditorUtility.SetDirty(path);
            e.Use();
            repaint();
        }
        #endregion

        #region Handles
        private static void DrawHandles(FenceLinePath path, Terrain terrain, System.Action repaint)
        {
            for (int g = 0; g < path.Groups.Count; g++)
            {
                var group = path.Group(g);
                bool active = g == FenceEditState.Group;
                Handles.color = group.GizmoColor;
                for (int i = 0; i < group.Points.Count; i++)
                {
                    Vector3 p = group.Points[i];
                    if (active && i == FenceEditState.Point) DrawMoveHandle(path, group, i, terrain);
                    else DrawSelectButton(path, g, i, p, active ? DotSize : OtherDotSize, repaint);
                }
            }
        }

        private static void DrawSelectButton(FenceLinePath path, int g, int i, Vector3 p, float dot, System.Action repaint)
        {
            float size = HandleUtility.GetHandleSize(p) * dot;
            if (!Handles.Button(p, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap)) return;
            FenceEditState.Select(path, g, i);
            repaint();
        }

        private static void DrawMoveHandle(FenceLinePath path, FenceGroup group, int index, Terrain terrain)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(group.Points[index], Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;
            if (terrain != null) moved.y = FenceMetrics.Ground(terrain, moved.x, moved.z);
            Undo.RecordObject(path, "Deplacer point de barriere");
            group.SetPoint(index, moved);
            EditorUtility.SetDirty(path);
        }
        #endregion
    }
}
