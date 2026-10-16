using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public enum RealBlendWallProfileMode
    {
        Rectangular,
        Custom
    }

    public enum RealBlendWallGeometryMode
    {
        Surface = 0,
        Solid = 1
    }

    [Serializable]
    public class RealBlendWallCreateMode : IRealBlendCreationMode
    {
        public float width = 5f;
        public float height = 5f;
        public RealBlendWallProfileMode profileMode = RealBlendWallProfileMode.Rectangular;
        public RealBlendWallGeometryMode geometryMode = RealBlendWallGeometryMode.Surface;
        public float thickness = 0.21f;
        public float customProfileScale = 1f;
        public List<Vector2> customProfile = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 5f)
        };
        public float newSegmentLength = 1f;
        public float newSegmentAngle = 0f;

        public string DisplayName => "Wall";

        public void OnGUI(RealBlendCreationSettings settings)
        {
            EnsureCustomProfile();

            width = Mathf.Max(0.01f, EditorGUILayout.FloatField("Width (X)", width));
            profileMode = (RealBlendWallProfileMode)EditorGUILayout.EnumPopup("Profile Mode", profileMode);

            if (profileMode == RealBlendWallProfileMode.Rectangular)
            {
                height = Mathf.Max(0.01f, EditorGUILayout.FloatField("Height (Y)", height));
                geometryMode = (RealBlendWallGeometryMode)EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Wall Geometry",
                        "Surface keeps the original single-sided wall. Solid builds a closed wall with front, back, end, top, and bottom faces."),
                    geometryMode);
                if (geometryMode == RealBlendWallGeometryMode.Solid)
                {
                    thickness = Mathf.Max(
                        0.01f,
                        EditorGUILayout.FloatField(
                            new GUIContent(
                                "Thickness (Z)",
                                "Closed solid-wall depth. Use Sculpt > Boolean Cut to create precise connected openings in solid walls."),
                            thickness));
                }
                return;
            }

            DrawCustomProfileGUI();
        }

        public void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            RealBlendCreationCore.UpdateScenePlacement(sceneView, settings, preview, false);
            RealBlendCreationCore.DrawCenterCrosshair(sceneView);
            if (!preview.hasValidPreview) return;

            Handles.matrix = Matrix4x4.TRS(preview.position, preview.rotation, Vector3.one);
            if (profileMode == RealBlendWallProfileMode.Rectangular)
            {
                if (geometryMode == RealBlendWallGeometryMode.Solid)
                    DrawSolidWallPreview(settings);
                else
                    RealBlendCreationCore.DrawGridPreview(GetRectVertexPosition, GetWidthCount(settings), GetHeightCount(settings), width, height, settings);
            }
            else
            {
                RealBlendCreationCore.DrawGridPreview(GetCustomVertexPosition, GetWidthCount(settings), GetCustomProfileCount(settings), width, GetCustomProfileLength(), settings);
                DrawCustomProfileGuides();
            }
            Handles.matrix = Matrix4x4.identity;
        }

        public bool CanGenerate(RealBlendCreationSettings settings)
        {
            if (width <= 0f) return false;
            return profileMode == RealBlendWallProfileMode.Rectangular
                ? height > 0f &&
                  (geometryMode != RealBlendWallGeometryMode.Solid || thickness > 0f)
                : GetCustomProfileLength() > 0.0001f;
        }

        public RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData
            {
                name = profileMode == RealBlendWallProfileMode.Rectangular
                    ? geometryMode == RealBlendWallGeometryMode.Solid
                        ? "RealBlend Solid Wall"
                        : "RealBlend Wall"
                    : "RealBlend Custom Wall"
            };

            if (profileMode == RealBlendWallProfileMode.Rectangular)
            {
                if (geometryMode == RealBlendWallGeometryMode.Solid)
                    BuildSolidWall(data, settings);
                else
                {
                    RealBlendCreationCore.AddGridSurface(
                        data,
                        GetWidthCount(settings),
                        GetHeightCount(settings),
                        GetRectVertexPosition,
                        (x, y) => new Vector2(x * width, y * height),
                        settings);
                }
                return data;
            }

            AddCustomProfilePatches(data, settings);

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
            if (profileMode == RealBlendWallProfileMode.Rectangular)
            {
                if (geometryMode == RealBlendWallGeometryMode.Solid)
                {
                    RealBlendMeshBuildData data = BuildMesh(settings);
                    vertexCount = data.vertices.Count;
                    triangleCount = data.triangles.Count / 3;
                }
                else
                {
                    int h = GetHeightCount(settings);
                    vertexCount = w * h;
                    triangleCount = (w - 1) * (h - 1) * 2;
                }
                return;
            }

            GetCustomProfilePatchStats(settings, w, out vertexCount, out triangleCount);
        }

        private void DrawCustomProfileGUI()
        {
            customProfileScale = Mathf.Max(0.01f, EditorGUILayout.FloatField("Profile Scale", customProfileScale));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Profile Graph", EditorStyles.boldLabel);
                for (int i = 0; i < customProfile.Count; i++)
                {
                    Vector2 point = customProfile[i];
                    point = EditorGUILayout.Vector2Field($"Key {i}  Z/Y", point);
                    if (i == 0) point.y = Mathf.Max(0f, point.y);
                    customProfile[i] = point;
                }

                GUILayout.Space(4);
                newSegmentLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("New Segment Length", newSegmentLength));
                newSegmentAngle = EditorGUILayout.Slider("New Segment Angle", newSegmentAngle, -89f, 89f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Segment")) AddSegmentKey();
                    using (new EditorGUI.DisabledScope(customProfile.Count <= 2))
                    {
                        if (GUILayout.Button("Remove Last")) customProfile.RemoveAt(customProfile.Count - 1);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Straight")) ResetStraightProfile();
                    if (GUILayout.Button("Offset Example")) ResetOffsetExampleProfile();
                }
            }
        }

        private void DrawCustomProfileGuides()
        {
            List<Vector2> points = GetScaledProfilePoints();
            float halfWidth = width * 0.5f;

            Handles.color = new Color(0f, 1f, 1f, 0.7f);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 aCenter = ToWallPoint(0f, points[i]);
                Vector3 bCenter = ToWallPoint(0f, points[i + 1]);
                Handles.DrawLine(aCenter, bCenter);
                Handles.DrawLine(ToWallPoint(-halfWidth, points[i]), ToWallPoint(-halfWidth, points[i + 1]));
                Handles.DrawLine(ToWallPoint(halfWidth, points[i]), ToWallPoint(halfWidth, points[i + 1]));
            }

            Handles.color = new Color(1f, 0.92f, 0.016f, 0.95f);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 center = ToWallPoint(0f, points[i]);
                Handles.SphereHandleCap(0, center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.04f, EventType.Repaint);
                Handles.Label(center, $"Key {i}");
                Handles.DrawLine(ToWallPoint(-halfWidth, points[i]), ToWallPoint(halfWidth, points[i]));
            }
        }

        private void AddSegmentKey()
        {
            EnsureCustomProfile();
            Vector2 last = customProfile[customProfile.Count - 1];
            float radians = newSegmentAngle * Mathf.Deg2Rad;
            Vector2 delta = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * newSegmentLength;
            customProfile.Add(last + delta);
        }

        private void ResetStraightProfile()
        {
            customProfile = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(0f, Mathf.Max(0.01f, height))
            };
        }

        private void ResetOffsetExampleProfile()
        {
            customProfile = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0.35f, 1.35f),
                new Vector2(0.35f, 3.35f),
                new Vector2(0f, 3.7f),
                new Vector2(0f, 4.7f)
            };
        }

        private int GetWidthCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(width, settings.resolutionPerMeter);
        private int GetHeightCount(RealBlendCreationSettings settings) => RealBlendCreationCore.ResolutionCount(height, settings.resolutionPerMeter);
        private int GetCustomProfileCount(RealBlendCreationSettings settings) => Mathf.Max(2, BuildProfileSamples(settings).Count);

        private Vector3 GetRectVertexPosition(float xNorm, float yNorm)
        {
            return new Vector3((xNorm - 0.5f) * width, yNorm * height, 0f);
        }

        private void DrawSolidWallPreview(RealBlendCreationSettings settings)
        {
            float halfDepth = Mathf.Max(0.01f, thickness) * 0.5f;
            int widthCount = GetWidthCount(settings);
            int heightCount = GetHeightCount(settings);

            RealBlendCreationCore.DrawGridPreview(
                (u, v) => new Vector3((u - 0.5f) * width, v * height, halfDepth),
                widthCount,
                heightCount,
                width,
                height,
                settings);
            RealBlendCreationCore.DrawGridPreview(
                (u, v) => new Vector3((0.5f - u) * width, v * height, -halfDepth),
                widthCount,
                heightCount,
                width,
                height,
                settings);
            RealBlendCreationCore.DrawBoxPreview(
                new Vector3(width, height, Mathf.Max(0.01f, thickness)),
                settings);
        }

        private void BuildSolidWall(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings)
        {
            float safeThickness = Mathf.Max(0.01f, thickness);
            float halfDepth = safeThickness * 0.5f;
            int widthCount = GetWidthCount(settings);
            int heightCount = GetHeightCount(settings);
            int depthCount = RealBlendCreationCore.ResolutionCount(
                safeThickness,
                settings.resolutionPerMeter);

            AddSolidPatch(
                data,
                settings,
                widthCount,
                heightCount,
                Vector3.forward,
                (u, v) => new Vector3(Mathf.Lerp(-width * 0.5f, width * 0.5f, u), v * height, halfDepth),
                (u, v) => new Vector2(u * width, v * height));

            AddSolidPatch(
                data,
                settings,
                widthCount,
                heightCount,
                Vector3.back,
                (u, v) => new Vector3(Mathf.Lerp(width * 0.5f, -width * 0.5f, u), v * height, -halfDepth),
                (u, v) => new Vector2(width + safeThickness + u * width, v * height));

            AddSolidPatch(
                data,
                settings,
                depthCount,
                heightCount,
                Vector3.right,
                (u, v) => new Vector3(width * 0.5f, v * height, Mathf.Lerp(halfDepth, -halfDepth, u)),
                (u, v) => new Vector2(width + u * safeThickness, v * height));

            AddSolidPatch(
                data,
                settings,
                depthCount,
                heightCount,
                Vector3.left,
                (u, v) => new Vector3(-width * 0.5f, v * height, Mathf.Lerp(-halfDepth, halfDepth, u)),
                (u, v) => new Vector2(width * 2f + safeThickness + u * safeThickness, v * height));

            AddSolidPatch(
                data,
                settings,
                widthCount,
                depthCount,
                Vector3.up,
                (u, v) => new Vector3(Mathf.Lerp(-width * 0.5f, width * 0.5f, u), height, Mathf.Lerp(-halfDepth, halfDepth, v)),
                (u, v) => new Vector2(u * width, v * safeThickness));

            AddSolidPatch(
                data,
                settings,
                widthCount,
                depthCount,
                Vector3.down,
                (u, v) => new Vector3(Mathf.Lerp(-width * 0.5f, width * 0.5f, u), 0f, Mathf.Lerp(halfDepth, -halfDepth, v)),
                (u, v) => new Vector2(u * width, v * safeThickness));
        }

        private static void AddSolidPatch(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            int uCount,
            int vCount,
            Vector3 normal,
            Func<float, float, Vector3> position,
            Func<float, float, Vector2> uvMeters)
        {
            RealBlendCreationCore.AddPatch(data, uCount, vCount, (u, v) =>
            {
                float uNorm = u / (float)(uCount - 1);
                float vNorm = v / (float)(vCount - 1);
                return new RealBlendPatchVertex(
                    position(uNorm, vNorm),
                    normal,
                    uvMeters(uNorm, vNorm));
            }, settings, false);
        }

        private Vector3 GetCustomVertexPosition(float xNorm, float profileNorm)
        {
            Vector2 profile = EvaluateCustomProfile(profileNorm);
            return new Vector3((xNorm - 0.5f) * width, profile.y, profile.x);
        }

        private Vector2 EvaluateCustomProfile(float profileNorm)
        {
            List<Vector2> points = GetScaledProfilePoints();
            float totalLength = GetPathLength(points);
            if (totalLength <= 0.0001f)
                return points[0];

            float target = Mathf.Clamp01(profileNorm) * totalLength;
            float distance = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                float segmentLength = Vector2.Distance(points[i], points[i + 1]);
                if (distance + segmentLength >= target)
                {
                    float t = segmentLength > 0.0001f ? (target - distance) / segmentLength : 0f;
                    return Vector2.Lerp(points[i], points[i + 1], t);
                }

                distance += segmentLength;
            }

            return points[points.Count - 1];
        }

        private List<WallProfileSample> BuildProfileSamples(RealBlendCreationSettings settings)
        {
            List<Vector2> points = GetScaledProfilePoints();
            List<WallProfileSample> samples = new List<WallProfileSample>();
            float distance = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                Vector2 delta = b - a;
                float segmentLength = delta.magnitude;
                if (segmentLength <= 0.0001f)
                    continue;

                int segmentCount = RealBlendCreationCore.SegmentCount(segmentLength, settings.resolutionPerMeter);
                Vector3 normal = GetProfileNormal(delta);

                for (int s = samples.Count == 0 ? 0 : 1; s <= segmentCount; s++)
                {
                    float t = s / (float)segmentCount;
                    Vector2 point = Vector2.Lerp(a, b, t);
                    samples.Add(new WallProfileSample
                    {
                        position = point,
                        normal = normal,
                        distance = distance + segmentLength * t
                    });
                }

                distance += segmentLength;
            }

            if (samples.Count < 2)
            {
                samples.Clear();
                samples.Add(new WallProfileSample { position = Vector2.zero, normal = Vector3.back, distance = 0f });
                samples.Add(new WallProfileSample { position = Vector2.up * Mathf.Max(0.01f, height), normal = Vector3.back, distance = Mathf.Max(0.01f, height) });
            }

            return samples;
        }

        private void AddCustomProfilePatches(RealBlendMeshBuildData data, RealBlendCreationSettings settings)
        {
            List<Vector2> points = GetScaledProfilePoints();
            int widthCount = GetWidthCount(settings);
            float distance = 0f;
            bool addedPatch = false;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                Vector2 delta = b - a;
                float segmentLength = delta.magnitude;
                if (segmentLength <= 0.0001f)
                    continue;

                int segmentCount = RealBlendCreationCore.SegmentCount(segmentLength, settings.resolutionPerMeter);
                int profileCount = segmentCount + 1;
                Vector3 normal = GetProfileNormal(delta);
                float startDistance = distance;

                RealBlendCreationCore.AddPatch(data, widthCount, profileCount, (u, v) =>
                {
                    float xNorm = u / (float)(widthCount - 1);
                    float profileNorm = v / (float)(profileCount - 1);
                    Vector2 point = Vector2.Lerp(a, b, profileNorm);
                    return new RealBlendPatchVertex(
                        new Vector3((xNorm - 0.5f) * width, point.y, point.x),
                        normal,
                        new Vector2(xNorm * width, startDistance + segmentLength * profileNorm));
                }, settings, false);

                distance += segmentLength;
                addedPatch = true;
            }

            if (addedPatch)
                return;

            RealBlendCreationCore.AddGridSurface(
                data,
                widthCount,
                GetHeightCount(settings),
                GetRectVertexPosition,
                (x, y) => new Vector2(x * width, y * height),
                settings);
        }

        private void GetCustomProfilePatchStats(RealBlendCreationSettings settings, int widthCount, out int vertexCount, out int triangleCount)
        {
            List<Vector2> points = GetScaledProfilePoints();
            vertexCount = 0;
            triangleCount = 0;

            for (int i = 0; i < points.Count - 1; i++)
            {
                float segmentLength = Vector2.Distance(points[i], points[i + 1]);
                if (segmentLength <= 0.0001f)
                    continue;

                int segmentCount = RealBlendCreationCore.SegmentCount(segmentLength, settings.resolutionPerMeter);
                vertexCount += widthCount * (segmentCount + 1);
                triangleCount += (widthCount - 1) * segmentCount * 2;
            }

            if (vertexCount > 0)
                return;

            int heightCount = GetHeightCount(settings);
            vertexCount = widthCount * heightCount;
            triangleCount = (widthCount - 1) * (heightCount - 1) * 2;
        }

        private Vector3 GetProfileNormal(Vector2 profileDelta)
        {
            Vector3 tangent = new Vector3(0f, profileDelta.y, profileDelta.x);
            if (tangent.sqrMagnitude < 0.000001f)
                return Vector3.back;

            return Vector3.Cross(tangent.normalized, Vector3.right).normalized;
        }

        private float GetCustomProfileLength()
        {
            return GetPathLength(GetScaledProfilePoints());
        }

        private float GetPathLength(List<Vector2> points)
        {
            float total = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                total += Vector2.Distance(points[i], points[i + 1]);
            return total;
        }

        private List<Vector2> GetScaledProfilePoints()
        {
            EnsureCustomProfile();
            float scale = Mathf.Max(0.01f, customProfileScale);
            List<Vector2> points = new List<Vector2>(customProfile.Count);
            for (int i = 0; i < customProfile.Count; i++)
                points.Add(customProfile[i] * scale);
            return points;
        }

        private void EnsureCustomProfile()
        {
            if (customProfile == null)
                customProfile = new List<Vector2>();

            if (customProfile.Count < 2)
                ResetStraightProfile();
        }

        private Vector3 ToWallPoint(float x, Vector2 profile)
        {
            return new Vector3(x, profile.y, profile.x);
        }

        private struct WallProfileSample
        {
            public Vector2 position;
            public Vector3 normal;
            public float distance;
        }
    }
}
