using System;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    [Serializable]
    public class RealBlendFloorCreateMode : IRealBlendCreationMode
    {
        public float width = 5f;
        public float length = 5f;

        public string DisplayName => "Floor";

        public void OnGUI(RealBlendCreationSettings settings)
        {
            width = Mathf.Max(0.01f, EditorGUILayout.FloatField("Width (X)", width));
            length = Mathf.Max(0.01f, EditorGUILayout.FloatField("Length (Z)", length));
        }

        public void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            RealBlendCreationCore.UpdateScenePlacement(sceneView, settings, preview, true);
            RealBlendCreationCore.DrawCenterCrosshair(sceneView);
            if (!preview.hasValidPreview) return;

            Handles.matrix = Matrix4x4.TRS(preview.position, preview.rotation, Vector3.one);
            RealBlendCreationCore.DrawGridPreview(GetVertexPosition, GetWidthCount(settings), GetLengthCount(settings), width, length, settings);
            Handles.matrix = Matrix4x4.identity;
        }

        public bool CanGenerate(RealBlendCreationSettings settings)
        {
            return width > 0f && length > 0f;
        }

        public RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData { name = "RealBlend Floor" };
            RealBlendCreationCore.AddGridSurface(
                data,
                GetWidthCount(settings),
                GetLengthCount(settings),
                GetVertexPosition,
                (x, z) => new Vector2(x * width, z * length),
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
            int l = GetLengthCount(settings);
            vertexCount = w * l;
            triangleCount = (w - 1) * (l - 1) * 2;
        }

        private int GetWidthCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(width, settings.resolutionPerMeter);
        private int GetLengthCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(length, settings.resolutionPerMeter);

        private Vector3 GetVertexPosition(float xNorm, float zNorm)
        {
            return new Vector3((xNorm - 0.5f) * width, 0f, (zNorm - 0.5f) * length);
        }
    }
}
