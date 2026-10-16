using System;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public enum RealBlendCurvedWallPivotMode
    {
        Mesh_Center,
        Circle_Center
    }

    [Serializable]
    public class RealBlendCurvedWallCreateMode : IRealBlendCreationMode
    {
        public float width = 5f;
        public float height = 5f;
        [Range(-360f, 360f)] public float curvature = 90f;
        [Range(0.01f, 1f)] public float completeness = 1f;
        public RealBlendCurvedWallPivotMode pivotMode = RealBlendCurvedWallPivotMode.Mesh_Center;

        public string DisplayName => "Curved Wall";

        public void OnGUI(RealBlendCreationSettings settings)
        {
            pivotMode = (RealBlendCurvedWallPivotMode)EditorGUILayout.EnumPopup("Pivot Location", pivotMode);
            if (pivotMode == RealBlendCurvedWallPivotMode.Circle_Center)
                EditorGUILayout.HelpBox("Pivot is at the center of the room. The wall spawns one radius away.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Pivot is on the wall itself. Best for slight curves.", MessageType.None);

            width = Mathf.Max(0.01f, EditorGUILayout.FloatField("Width (Arc Length)", width));
            height = Mathf.Max(0.01f, EditorGUILayout.FloatField("Height (Y)", height));
            curvature = EditorGUILayout.Slider("Curvature (Deg)", curvature, -360f, 360f);

            float completenessPercent = completeness * 100f;
            completenessPercent = EditorGUILayout.Slider("Completeness %", completenessPercent, 1f, 100f);
            completeness = completenessPercent / 100f;
        }

        public void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            RealBlendCreationCore.UpdateScenePlacement(sceneView, settings, preview, false);
            RealBlendCreationCore.DrawCenterCrosshair(sceneView);
            if (!preview.hasValidPreview) return;

            Handles.matrix = Matrix4x4.TRS(preview.position, preview.rotation, Vector3.one);
            RealBlendCreationCore.DrawGridPreview(GetVertexPosition, GetWidthCount(settings), GetHeightCount(settings), EffectiveArcLength, height, settings);

            if (Mathf.Abs(curvature) > 0.01f)
            {
                Handles.color = new Color(0, 1, 1, 0.3f);
                Vector3 meshCenter = GetVertexPosition(0.5f, 0.5f);
                if (pivotMode == RealBlendCurvedWallPivotMode.Mesh_Center)
                {
                    float radius = Radius;
                    Vector3 circleCenter = new Vector3(0f, 0f, -radius);
                    Handles.DrawDottedLine(meshCenter, circleCenter, 5f);
                    Handles.Label(circleCenter, "Center of Curve");
                }
                else
                {
                    Handles.DrawDottedLine(Vector3.zero, meshCenter, 5f);
                    Handles.Label(Vector3.zero, "Pivot (Center of Curve)");
                }
            }

            Handles.matrix = Matrix4x4.identity;
        }

        public bool CanGenerate(RealBlendCreationSettings settings)
        {
            return width > 0f && height > 0f;
        }

        public RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData { name = Mathf.Abs(curvature) > 1f ? "RealBlend Curved Wall" : "RealBlend Wall" };
            RealBlendCreationCore.AddGridSurface(
                data,
                GetWidthCount(settings),
                GetHeightCount(settings),
                GetVertexPosition,
                (x, y) => new Vector2(x * EffectiveArcLength, y * height),
                settings);
            return data;
        }

        public RealBlendPlacement GetPlacement(RealBlendCreationPreviewState preview)
        {
            return preview.hasValidPreview
                ? new RealBlendPlacement { position = preview.position, rotation = preview.rotation }
                : RealBlendPlacement.Identity;
        }

        public void GetStats(RealBlendCreationSettings settings, out int vertexCount, out int triangleCount)
        {
            int w = GetWidthCount(settings);
            int h = GetHeightCount(settings);
            vertexCount = w * h;
            triangleCount = (w - 1) * (h - 1) * 2;
        }

        private float EffectiveArcLength => Mathf.Max(0.01f, Mathf.Abs(width * completeness));
        private float Radius => width / (curvature * Mathf.Deg2Rad);

        private int GetWidthCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(EffectiveArcLength, settings.resolutionPerMeter);
        private int GetHeightCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(height, settings.resolutionPerMeter);

        private Vector3 GetVertexPosition(float xNorm, float yNorm)
        {
            if (Mathf.Abs(curvature) > 0.01f)
            {
                float totalAngle = curvature * completeness;
                float totalRad = totalAngle * Mathf.Deg2Rad;
                float radius = Radius;
                float currentRad = (xNorm - 0.5f) * totalRad;
                float x = Mathf.Sin(currentRad) * radius;
                float z = Mathf.Cos(currentRad) * radius;

                if (pivotMode == RealBlendCurvedWallPivotMode.Mesh_Center)
                    z -= radius;

                return new Vector3(x, yNorm * height, z);
            }

            return new Vector3((xNorm - 0.5f) * EffectiveArcLength, yNorm * height, 0f);
        }
    }
}
