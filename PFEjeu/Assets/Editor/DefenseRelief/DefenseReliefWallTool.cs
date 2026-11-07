using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Remet la muraille sur le nouveau terrain : les splines des SplineWallBuilder gardent leur trace XZ
    /// mais suivent la hauteur du sol (un knot tous les ~6 m), puis le mur est regenere.
    /// Idempotent : la trace XZ est relue sur la spline courante, seul Y change.
    /// </summary>
    public static class DefenseReliefWallTool
    {
        private const string WallRootName = "muraille";
        private const float KnotSpacing = 6f;
        private const float WallLift = 0.2f;

        [MenuItem("Tools/Defense/Relief/3c - Recaler la muraille")]
        public static void Align()
        {
            Transform root = DefenseReliefScene.Require(WallRootName).transform;
            foreach (Transform child in root)
            {
                var builder = child.GetComponent<SplineWallBuilder>();
                if (builder == null) continue;

                var so = new SerializedObject(builder);
                var container = so.FindProperty("splineContainer").objectReferenceValue as SplineContainer;
                if (container == null) continue;

                AlignSpline(container);
                builder.Rebuild();
                EditorUtility.SetDirty(builder);
            }
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[DefenseRelief] Muraille recalee sur le terrain.");
        }

        private static void AlignSpline(SplineContainer container)
        {
            Spline spline = container.Spline;
            const int samples = 600;
            var path = new List<Vector2>(samples + 1);
            for (int i = 0; i <= samples; i++)
            {
                Vector3 w = container.EvaluatePosition(i / (float)samples);
                path.Add(new Vector2(w.x, w.z));
            }

            var points = new List<Vector2> { path[0] };
            float carried = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                float seg = Vector2.Distance(a, b);
                float cursor = KnotSpacing - carried;
                while (cursor <= seg)
                {
                    points.Add(Vector2.Lerp(a, b, cursor / seg));
                    cursor += KnotSpacing;
                }
                carried = seg - (cursor - KnotSpacing);
            }
            Vector2 last = path[path.Count - 1];
            if (points.Count > 1 && Vector2.Distance(points[points.Count - 1], last) < KnotSpacing * 0.5f) points.RemoveAt(points.Count - 1);
            points.Add(last);

            Undo.RecordObject(container, "Defense Relief : wall spline");
            spline.Clear();
            foreach (Vector2 xz in points)
            {
                float y = DefenseReliefScene.TerrainY(xz.x, xz.y) + WallLift;
                Vector3 local = container.transform.InverseTransformPoint(new Vector3(xz.x, y, xz.y));
                spline.Add(new BezierKnot(new float3(local.x, local.y, local.z)), TangentMode.AutoSmooth);
            }
            EditorUtility.SetDirty(container);
        }
    }
}
