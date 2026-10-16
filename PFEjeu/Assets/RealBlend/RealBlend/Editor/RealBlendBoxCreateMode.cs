using System;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public enum RealBlendBoxFaceDirection
    {
        Outward,
        Inward
    }

    [Serializable]
    public class RealBlendBoxCreateMode : IRealBlendCreationMode
    {
        public float length = 5f;
        public float width = 5f;
        public float height = 3f;
        public RealBlendBoxFaceDirection faceDirection = RealBlendBoxFaceDirection.Outward;
        public bool includeTop = true;
        public bool includeBottom = true;
        public float bevelSize = 0f;
        [Range(1, 12)] public int bevelSegments = 3;

        public string DisplayName => "Box";

        public void OnGUI(RealBlendCreationSettings settings)
        {
            length = Mathf.Max(0.01f, EditorGUILayout.FloatField("Length (Z)", length));
            width = Mathf.Max(0.01f, EditorGUILayout.FloatField("Width (X)", width));
            height = Mathf.Max(0.01f, EditorGUILayout.FloatField("Height (Y)", height));
            faceDirection = (RealBlendBoxFaceDirection)EditorGUILayout.EnumPopup("Face Direction", faceDirection);

            using (new EditorGUILayout.HorizontalScope())
            {
                includeTop = EditorGUILayout.Toggle("Include Top", includeTop);
                includeBottom = EditorGUILayout.Toggle("Include Bottom", includeBottom);
            }

            bevelSize = Mathf.Max(0f, EditorGUILayout.FloatField("Bevel Size", bevelSize));
            using (new EditorGUI.DisabledScope(bevelSize <= 0.0001f))
                bevelSegments = EditorGUILayout.IntSlider("Bevel Segments", bevelSegments, 1, 12);
        }

        public void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            RealBlendCreationCore.UpdateScenePlacement(sceneView, settings, preview, false);
            RealBlendCreationCore.DrawCenterCrosshair(sceneView);
            if (!preview.hasValidPreview) return;

            Handles.matrix = Matrix4x4.TRS(preview.position, preview.rotation, Vector3.one);
            DrawBoxCreationPreview(settings);
            Handles.matrix = Matrix4x4.identity;
        }

        public bool CanGenerate(RealBlendCreationSettings settings)
        {
            return width > 0f && length > 0f && height > 0f;
        }

        public RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData { name = "RealBlend Box" };
            float radius = ClampedBevelSize;
            if (radius <= 0.0001f)
                BuildSharpBox(data, settings);
            else
                BuildRoundedBox(data, settings, radius, Mathf.Max(1, bevelSegments));
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
            RealBlendMeshBuildData data = BuildMesh(settings);
            vertexCount = data.vertices.Count;
            triangleCount = data.triangles.Count / 3;
        }

        private void DrawBoxCreationPreview(RealBlendCreationSettings settings)
        {
            float radius = ClampedBevelSize;
            if (radius <= 0.0001f)
                DrawSharpBoxPreview(settings);
            else
                DrawRoundedBoxPreview(settings, radius, Mathf.Max(1, bevelSegments));
        }

        private void DrawSharpBoxPreview(RealBlendCreationSettings settings)
        {
            float hx = width * 0.5f;
            float hz = length * 0.5f;
            int xCount = RealBlendCreationCore.ResolutionCount(width, settings.resolutionPerMeter);
            int zCount = RealBlendCreationCore.ResolutionCount(length, settings.resolutionPerMeter);
            int yCount = RealBlendCreationCore.ResolutionCount(height, settings.resolutionPerMeter);

            if (settings.smartPreview)
            {
                Handles.color = new Color(1f, 0.92f, 0.016f, 0.65f);
                DrawSharpOutline(hx, hz);
                if (includeTop)
                    DrawSharpCapMarker(hx, hz, height);
                if (includeBottom)
                    DrawSharpCapMarker(hx, hz, 0f);

                Handles.color = new Color(1f, 0.92f, 0.016f, 0.85f);
                DrawPreviewDensitySample(
                    (u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), v * height, hz),
                    width,
                    height,
                    settings);

                return;
            }

            Handles.color = new Color(1f, 0.92f, 0.016f, 0.55f);
            DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), v * height, hz), xCount, yCount, settings.showTriangles);
            DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(hx, -hx, u), v * height, -hz), xCount, yCount, settings.showTriangles);
            DrawPreviewPatch((u, v) => new Vector3(hx, v * height, Mathf.Lerp(hz, -hz, u)), zCount, yCount, settings.showTriangles);
            DrawPreviewPatch((u, v) => new Vector3(-hx, v * height, Mathf.Lerp(-hz, hz, u)), zCount, yCount, settings.showTriangles);

            if (includeTop)
                DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), height, Mathf.Lerp(-hz, hz, v)), xCount, zCount, settings.showTriangles);

            if (includeBottom)
                DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), 0f, Mathf.Lerp(hz, -hz, v)), xCount, zCount, settings.showTriangles);
        }

        private void DrawRoundedBoxPreview(RealBlendCreationSettings settings, float radius, int segments)
        {
            float hx = width * 0.5f;
            float hz = length * 0.5f;
            float ix = hx - radius;
            float iz = hz - radius;
            float yb = radius;
            float yt = height - radius;
            float sideBottom = includeBottom ? yb : 0f;
            float sideTop = includeTop ? yt : height;
            float flatWidth = Mathf.Max(0.001f, ix * 2f);
            float flatLength = Mathf.Max(0.001f, iz * 2f);
            float flatHeight = Mathf.Max(0.001f, sideTop - sideBottom);
            int flatX = RealBlendCreationCore.ResolutionCount(flatWidth, settings.resolutionPerMeter);
            int flatZ = RealBlendCreationCore.ResolutionCount(flatLength, settings.resolutionPerMeter);
            int flatY = RealBlendCreationCore.ResolutionCount(flatHeight, settings.resolutionPerMeter);
            RoundedRingPoint[] ring = BuildRoundedRing(settings, radius, ix, iz, hx, hz, segments);

            if (settings.smartPreview)
            {
                Handles.color = new Color(1f, 0.92f, 0.016f, 0.7f);
                DrawVerticalRingPreview(ring, sideBottom, sideTop, flatY, true, settings.showTriangles);

                if (includeTop)
                {
                    DrawBevelPreview(ring, radius, yt, 1f, segments, true, settings.showTriangles);
                    DrawCapRectOutline(ix, iz, height);
                }

                if (includeBottom)
                {
                    DrawBevelPreview(ring, radius, yb, -1f, segments, true, settings.showTriangles);
                    DrawCapRectOutline(ix, iz, 0f);
                }

                Handles.color = new Color(1f, 0.92f, 0.016f, 0.9f);
                DrawPreviewDensitySample(
                    (u, v) => new Vector3(Mathf.Lerp(-ix, ix, u), Mathf.Lerp(sideBottom, sideTop, v), hz),
                    flatWidth,
                    flatHeight,
                    settings);
                return;
            }

            Handles.color = new Color(1f, 0.92f, 0.016f, 0.5f);
            DrawVerticalRingPreview(ring, sideBottom, sideTop, flatY, false, settings.showTriangles);

            if (includeTop)
            {
                DrawBevelPreview(ring, radius, yt, 1f, segments, false, settings.showTriangles);
                DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(-ix, ix, u), height, Mathf.Lerp(-iz, iz, v)), flatX, flatZ, settings.showTriangles);
            }

            if (includeBottom)
            {
                DrawBevelPreview(ring, radius, yb, -1f, segments, false, settings.showTriangles);
                DrawPreviewPatch((u, v) => new Vector3(Mathf.Lerp(-ix, ix, u), 0f, Mathf.Lerp(iz, -iz, v)), flatX, flatZ, settings.showTriangles);
            }
        }

        private void DrawSharpOutline(float hx, float hz)
        {
            Vector3[] corners =
            {
                new Vector3(-hx, 0f, -hz),
                new Vector3(hx, 0f, -hz),
                new Vector3(hx, 0f, hz),
                new Vector3(-hx, 0f, hz),
                new Vector3(-hx, height, -hz),
                new Vector3(hx, height, -hz),
                new Vector3(hx, height, hz),
                new Vector3(-hx, height, hz)
            };

            DrawPreviewLine(corners[0], corners[1]);
            DrawPreviewLine(corners[1], corners[2]);
            DrawPreviewLine(corners[2], corners[3]);
            DrawPreviewLine(corners[3], corners[0]);
            DrawPreviewLine(corners[4], corners[5]);
            DrawPreviewLine(corners[5], corners[6]);
            DrawPreviewLine(corners[6], corners[7]);
            DrawPreviewLine(corners[7], corners[4]);
            DrawPreviewLine(corners[0], corners[4]);
            DrawPreviewLine(corners[1], corners[5]);
            DrawPreviewLine(corners[2], corners[6]);
            DrawPreviewLine(corners[3], corners[7]);
        }

        private void DrawSharpCapMarker(float hx, float hz, float y)
        {
            DrawPreviewLine(new Vector3(-hx, y, -hz), new Vector3(hx, y, hz));
            DrawPreviewLine(new Vector3(hx, y, -hz), new Vector3(-hx, y, hz));
        }

        private void DrawVerticalRingPreview(RoundedRingPoint[] ring, float yStart, float yEnd, int heightCount, bool smart, bool showTriangles)
        {
            if (ring.Length < 2) return;

            if (smart)
            {
                DrawRing(ring, yStart);
                DrawRing(ring, yEnd);
                int step = Mathf.Max(1, (ring.Length - 1) / 12);
                for (int i = 0; i < ring.Length; i += step)
                    DrawPreviewLine(ToPreviewPoint(ring[i], yStart), ToPreviewPoint(ring[i], yEnd));
                return;
            }

            for (int y = 0; y < heightCount; y++)
            {
                float yNorm = y / (float)(heightCount - 1);
                DrawRing(ring, Mathf.Lerp(yStart, yEnd, yNorm));
            }

            for (int i = 0; i < ring.Length; i++)
                DrawPreviewLine(ToPreviewPoint(ring[i], yStart), ToPreviewPoint(ring[i], yEnd));

            if (!showTriangles) return;

            Color baseColor = Handles.color;
            Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.45f);
            for (int y = 0; y < heightCount - 1; y++)
            {
                float y0 = Mathf.Lerp(yStart, yEnd, y / (float)(heightCount - 1));
                float y1 = Mathf.Lerp(yStart, yEnd, (y + 1) / (float)(heightCount - 1));
                for (int i = 0; i < ring.Length - 1; i++)
                    DrawPreviewLine(ToPreviewPoint(ring[i], y0), ToPreviewPoint(ring[i + 1], y1));
            }
            Handles.color = baseColor;
        }

        private void DrawBevelPreview(RoundedRingPoint[] ring, float radius, float yCenter, float ySign, int segments, bool smart, bool showTriangles)
        {
            int bevelSteps = Mathf.Max(1, segments);
            for (int s = 0; s <= bevelSteps; s++)
            {
                float phi = (s / (float)bevelSteps) * Mathf.PI * 0.5f;
                DrawBevelRing(ring, radius, yCenter, ySign, phi);
            }

            int pointStep = smart ? Mathf.Max(1, (ring.Length - 1) / 12) : 1;
            for (int i = 0; i < ring.Length; i += pointStep)
                DrawBevelSpoke(ring[i], radius, yCenter, ySign, bevelSteps);

            if (!showTriangles || smart) return;

            Color baseColor = Handles.color;
            Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.45f);
            for (int s = 0; s < bevelSteps; s++)
            {
                float phi0 = (s / (float)bevelSteps) * Mathf.PI * 0.5f;
                float phi1 = ((s + 1) / (float)bevelSteps) * Mathf.PI * 0.5f;
                for (int i = 0; i < ring.Length - 1; i++)
                    DrawPreviewLine(GetBevelPoint(ring[i], radius, yCenter, ySign, phi0), GetBevelPoint(ring[i + 1], radius, yCenter, ySign, phi1));
            }
            Handles.color = baseColor;
        }

        private void DrawPreviewPatch(Func<float, float, Vector3> getPosition, int uCount, int vCount, bool showTriangles)
        {
            if (uCount < 2 || vCount < 2) return;

            for (int v = 0; v < vCount; v++)
            {
                float vNorm = v / (float)(vCount - 1);
                Vector3 previous = getPosition(0f, vNorm);
                for (int u = 1; u < uCount; u++)
                {
                    float uNorm = u / (float)(uCount - 1);
                    Vector3 current = getPosition(uNorm, vNorm);
                    DrawPreviewLine(previous, current);
                    previous = current;
                }
            }

            for (int u = 0; u < uCount; u++)
            {
                float uNorm = u / (float)(uCount - 1);
                Vector3 previous = getPosition(uNorm, 0f);
                for (int v = 1; v < vCount; v++)
                {
                    float vNorm = v / (float)(vCount - 1);
                    Vector3 current = getPosition(uNorm, vNorm);
                    DrawPreviewLine(previous, current);
                    previous = current;
                }
            }

            if (!showTriangles) return;

            Color baseColor = Handles.color;
            Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.45f);
            for (int v = 0; v < vCount - 1; v++)
            {
                float v0 = v / (float)(vCount - 1);
                float v1 = (v + 1) / (float)(vCount - 1);
                for (int u = 0; u < uCount - 1; u++)
                {
                    float u0 = u / (float)(uCount - 1);
                    float u1 = (u + 1) / (float)(uCount - 1);
                    DrawPreviewLine(getPosition(u0, v0), getPosition(u1, v1));
                }
            }
            Handles.color = baseColor;
        }

        private void DrawPreviewDensitySample(Func<float, float, Vector3> getPosition, float widthMeters, float heightMeters, RealBlendCreationSettings settings)
        {
            float effectiveWidth = Mathf.Max(0.01f, Mathf.Abs(widthMeters));
            float effectiveHeight = Mathf.Max(0.01f, Mathf.Abs(heightMeters));
            float sampleWidthMeters = Mathf.Min(1f, effectiveWidth);
            float sampleHeightMeters = Mathf.Min(1f, effectiveHeight);
            float sampleUSpan = sampleWidthMeters / effectiveWidth;
            float sampleVSpan = sampleHeightMeters / effectiveHeight;
            float sampleUStart = Mathf.Clamp01(0.5f - sampleUSpan * 0.5f);
            float sampleVStart = 0f;
            int uCells = Mathf.Max(1, Mathf.RoundToInt(sampleWidthMeters * settings.resolutionPerMeter));
            int vCells = Mathf.Max(1, Mathf.RoundToInt(sampleHeightMeters * settings.resolutionPerMeter));

            DrawPreviewPatch((u, v) =>
            {
                float sampleU = sampleUStart + u * sampleUSpan;
                float sampleV = sampleVStart + v * sampleVSpan;
                return getPosition(sampleU, sampleV);
            }, uCells + 1, vCells + 1, settings.showTriangles);

            Handles.Label(getPosition(sampleUStart + sampleUSpan, sampleVStart + sampleVSpan), "1m x 1m density sample");
        }

        private void DrawRing(RoundedRingPoint[] ring, float y)
        {
            for (int i = 0; i < ring.Length - 1; i++)
                DrawPreviewLine(ToPreviewPoint(ring[i], y), ToPreviewPoint(ring[i + 1], y));
        }

        private void DrawBevelRing(RoundedRingPoint[] ring, float radius, float yCenter, float ySign, float phi)
        {
            for (int i = 0; i < ring.Length - 1; i++)
                DrawPreviewLine(GetBevelPoint(ring[i], radius, yCenter, ySign, phi), GetBevelPoint(ring[i + 1], radius, yCenter, ySign, phi));
        }

        private void DrawBevelSpoke(RoundedRingPoint point, float radius, float yCenter, float ySign, int segments)
        {
            Vector3 previous = GetBevelPoint(point, radius, yCenter, ySign, 0f);
            for (int s = 1; s <= segments; s++)
            {
                float phi = (s / (float)segments) * Mathf.PI * 0.5f;
                Vector3 current = GetBevelPoint(point, radius, yCenter, ySign, phi);
                DrawPreviewLine(previous, current);
                previous = current;
            }
        }

        private void DrawCapRectOutline(float ix, float iz, float y)
        {
            DrawPreviewLine(new Vector3(-ix, y, -iz), new Vector3(ix, y, -iz));
            DrawPreviewLine(new Vector3(ix, y, -iz), new Vector3(ix, y, iz));
            DrawPreviewLine(new Vector3(ix, y, iz), new Vector3(-ix, y, iz));
            DrawPreviewLine(new Vector3(-ix, y, iz), new Vector3(-ix, y, -iz));
        }

        private Vector3 ToPreviewPoint(RoundedRingPoint point, float y)
        {
            return new Vector3(point.point.x, y, point.point.y);
        }

        private Vector3 GetBevelPoint(RoundedRingPoint point, float radius, float yCenter, float ySign, float phi)
        {
            Vector2 center = point.point - point.normal * radius;
            Vector2 planar = center + point.normal * (Mathf.Cos(phi) * radius);
            return new Vector3(planar.x, yCenter + ySign * Mathf.Sin(phi) * radius, planar.y);
        }

        private void DrawPreviewLine(Vector3 a, Vector3 b)
        {
            Handles.DrawLine(a, b);
        }

        private bool Inward => faceDirection == RealBlendBoxFaceDirection.Inward;
        private float ClampedBevelSize => Mathf.Min(bevelSize, width * 0.49f, length * 0.49f, height * 0.49f);

        private void BuildSharpBox(RealBlendMeshBuildData data, RealBlendCreationSettings settings)
        {
            float hx = width * 0.5f;
            float hz = length * 0.5f;
            int xCount = RealBlendCreationCore.ResolutionCount(width, settings.resolutionPerMeter);
            int zCount = RealBlendCreationCore.ResolutionCount(length, settings.resolutionPerMeter);
            int yCount = RealBlendCreationCore.ResolutionCount(height, settings.resolutionPerMeter);

            AddPlanarPatch(data, xCount, yCount, Vector3.forward,
                (u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), Mathf.Lerp(0f, height, v), hz),
                (u, v) => new Vector2(u * width, v * height),
                settings);

            AddPlanarPatch(data, xCount, yCount, Vector3.back,
                (u, v) => new Vector3(Mathf.Lerp(hx, -hx, u), Mathf.Lerp(0f, height, v), -hz),
                (u, v) => new Vector2(width + length + u * width, v * height),
                settings);

            AddPlanarPatch(data, zCount, yCount, Vector3.right,
                (u, v) => new Vector3(hx, Mathf.Lerp(0f, height, v), Mathf.Lerp(hz, -hz, u)),
                (u, v) => new Vector2(width + u * length, v * height),
                settings);

            AddPlanarPatch(data, zCount, yCount, Vector3.left,
                (u, v) => new Vector3(-hx, Mathf.Lerp(0f, height, v), Mathf.Lerp(-hz, hz, u)),
                (u, v) => new Vector2(width + length + width + u * length, v * height),
                settings);

            if (includeTop)
            {
                AddPlanarPatch(data, xCount, zCount, Vector3.up,
                    (u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), height, Mathf.Lerp(-hz, hz, v)),
                    (u, v) => new Vector2(u * width, v * length),
                    settings);
            }

            if (includeBottom)
            {
                AddPlanarPatch(data, xCount, zCount, Vector3.down,
                    (u, v) => new Vector3(Mathf.Lerp(-hx, hx, u), 0f, Mathf.Lerp(hz, -hz, v)),
                    (u, v) => new Vector2(u * width, v * length),
                    settings);
            }
        }

        private void BuildRoundedBox(RealBlendMeshBuildData data, RealBlendCreationSettings settings, float radius, int segments)
        {
            float hx = width * 0.5f;
            float hz = length * 0.5f;
            float ix = hx - radius;
            float iz = hz - radius;
            float yb = radius;
            float yt = height - radius;
            float sideBottom = includeBottom ? yb : 0f;
            float sideTop = includeTop ? yt : height;

            float flatWidth = ix * 2f;
            float flatLength = iz * 2f;
            float flatHeight = Mathf.Max(0.001f, sideTop - sideBottom);
            int flatX = RealBlendCreationCore.ResolutionCount(flatWidth, settings.resolutionPerMeter);
            int flatZ = RealBlendCreationCore.ResolutionCount(flatLength, settings.resolutionPerMeter);
            int flatY = RealBlendCreationCore.ResolutionCount(flatHeight, settings.resolutionPerMeter);
            int edge = segments + 1;
            RoundedRingPoint[] ring = BuildRoundedRing(settings, radius, ix, iz, hx, hz, segments);

            AddVerticalRingSurface(data, settings, ring, sideBottom, sideTop, flatY);

            if (includeTop)
            {
                AddPlanarPatch(data, flatX, flatZ, Vector3.up,
                    (u, v) => new Vector3(Mathf.Lerp(-ix, ix, u), height, Mathf.Lerp(-iz, iz, v)),
                    (u, v) => new Vector2(u * flatWidth, v * flatLength),
                    settings);
            }

            if (includeBottom)
            {
                AddPlanarPatch(data, flatX, flatZ, Vector3.down,
                    (u, v) => new Vector3(Mathf.Lerp(-ix, ix, u), 0f, Mathf.Lerp(iz, -iz, v)),
                    (u, v) => new Vector2(u * flatWidth, v * flatLength),
                    settings);
            }

            if (includeTop)
            {
                AddHorizontalEdges(data, settings, ring, radius, segments, flatX, flatZ, ix, iz, yt, true);
                AddCorners(data, settings, ring, radius, edge, ix, iz, yt, true);
            }

            if (includeBottom)
            {
                AddHorizontalEdges(data, settings, ring, radius, segments, flatX, flatZ, ix, iz, yb, false);
                AddCorners(data, settings, ring, radius, edge, ix, iz, yb, false);
            }
        }

        private void AddPlanarPatch(
            RealBlendMeshBuildData data,
            int uCount,
            int vCount,
            Vector3 normal,
            Func<float, float, Vector3> getPosition,
            Func<float, float, Vector2> getUVMeters,
            RealBlendCreationSettings settings)
        {
            RealBlendCreationCore.AddPatch(data, uCount, vCount, (u, v) =>
            {
                float uNorm = u / (float)(uCount - 1);
                float vNorm = v / (float)(vCount - 1);
                return new RealBlendPatchVertex(getPosition(uNorm, vNorm), normal, getUVMeters(uNorm, vNorm));
            }, settings, Inward);
        }

        private void AddVerticalEdges(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            float radius,
            int segments,
            int heightCount,
            float ix,
            float iz,
            float yb,
            float yt)
        {
            AddVerticalEdge(data, settings, radius, segments, heightCount, ix, iz, yb, yt, 1f, 1f);
            AddVerticalEdge(data, settings, radius, segments, heightCount, ix, iz, yb, yt, -1f, 1f);
            AddVerticalEdge(data, settings, radius, segments, heightCount, ix, iz, yb, yt, 1f, -1f);
            AddVerticalEdge(data, settings, radius, segments, heightCount, ix, iz, yb, yt, -1f, -1f);
        }

        private void AddVerticalEdge(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            float radius,
            int segments,
            int heightCount,
            float ix,
            float iz,
            float yb,
            float yt,
            float sx,
            float sz)
        {
            Vector3 center = new Vector3(sx * ix, yb, sz * iz);
            float heightSpan = Mathf.Max(0.001f, yt - yb);
            RealBlendCreationCore.AddPatch(data, segments + 1, heightCount, (u, v) =>
            {
                float theta = (u / (float)segments) * Mathf.PI * 0.5f;
                float y = yb + (v / (float)(heightCount - 1)) * heightSpan;
                Vector3 normal = new Vector3(sx * Mathf.Sin(theta), 0f, sz * Mathf.Cos(theta)).normalized;
                Vector3 position = new Vector3(center.x, y, center.z) + normal * radius;
                return new RealBlendPatchVertex(position, normal, new Vector2(theta * radius, y - yb));
            }, settings, Inward);
        }

        private void AddVerticalRingSurface(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            RoundedRingPoint[] ring,
            float yStart,
            float yEnd,
            int heightCount)
        {
            RealBlendCreationCore.AddPatch(data, ring.Length, heightCount, (u, v) =>
            {
                float yNorm = v / (float)(heightCount - 1);
                float y = Mathf.Lerp(yStart, yEnd, yNorm);
                RoundedRingPoint point = ring[u];
                return new RealBlendPatchVertex(
                    new Vector3(point.point.x, y, point.point.y),
                    new Vector3(point.normal.x, 0f, point.normal.y),
                    new Vector2(point.distance, y));
            }, settings, Inward);
        }

        private RoundedRingPoint[] BuildRoundedRing(
            RealBlendCreationSettings settings,
            float radius,
            float ix,
            float iz,
            float hx,
            float hz,
            int bevelArcSegments)
        {
            System.Collections.Generic.List<RoundedRingPoint> ring = new System.Collections.Generic.List<RoundedRingPoint>();
            AddRingPoint(ring, new Vector2(-ix, hz), Vector2.up);

            AddStraight(ring, new Vector2(ix, hz), Vector2.up, settings);
            AddArc(ring, new Vector2(ix, iz), radius, 90f, 0f, bevelArcSegments);
            AddStraight(ring, new Vector2(hx, -iz), Vector2.right, settings);
            AddArc(ring, new Vector2(ix, -iz), radius, 0f, -90f, bevelArcSegments);
            AddStraight(ring, new Vector2(-ix, -hz), Vector2.down, settings);
            AddArc(ring, new Vector2(-ix, -iz), radius, -90f, -180f, bevelArcSegments);
            AddStraight(ring, new Vector2(-hx, iz), Vector2.left, settings);
            AddArc(ring, new Vector2(-ix, iz), radius, 180f, 90f, bevelArcSegments);

            return ring.ToArray();
        }

        private void AddStraight(
            System.Collections.Generic.List<RoundedRingPoint> ring,
            Vector2 end,
            Vector2 normal,
            RealBlendCreationSettings settings)
        {
            Vector2 start = ring[ring.Count - 1].point;
            int segments = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(start, end) * settings.resolutionPerMeter));
            for (int i = 1; i <= segments; i++)
                AddRingPoint(ring, Vector2.Lerp(start, end, i / (float)segments), normal);
        }

        private void AddArc(
            System.Collections.Generic.List<RoundedRingPoint> ring,
            Vector2 center,
            float radius,
            float startDegrees,
            float endDegrees,
            int segments)
        {
            segments = Mathf.Max(1, segments);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float degrees = Mathf.Lerp(startDegrees, endDegrees, t);
                float radians = degrees * Mathf.Deg2Rad;
                Vector2 normal = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
                AddRingPoint(ring, center + normal * radius, normal);
            }
        }

        private void AddRingPoint(System.Collections.Generic.List<RoundedRingPoint> ring, Vector2 point, Vector2 normal)
        {
            float distance = 0f;
            if (ring.Count > 0)
            {
                RoundedRingPoint previous = ring[ring.Count - 1];
                if ((previous.point - point).sqrMagnitude < 0.0000001f)
                    return;
                distance = previous.distance + Vector2.Distance(previous.point, point);
            }

            ring.Add(new RoundedRingPoint
            {
                point = point,
                normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector2.up,
                distance = distance
            });
        }

        private void AddHorizontalEdges(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            RoundedRingPoint[] ring,
            float radius,
            int segments,
            int xCount,
            int zCount,
            float ix,
            float iz,
            float y,
            bool top)
        {
            Vector3 capNormal = top ? Vector3.up : Vector3.down;

            AddHorizontalEdge(data, settings, radius, segments, xCount, new Vector3(-ix, y, iz), Vector3.right, ix * 2f, Vector3.forward, capNormal, FindRingDistance(ring, new Vector2(-ix, iz + radius)));
            AddHorizontalEdge(data, settings, radius, segments, zCount, new Vector3(ix, y, iz), Vector3.back, iz * 2f, Vector3.right, capNormal, FindRingDistance(ring, new Vector2(ix + radius, iz)));
            AddHorizontalEdge(data, settings, radius, segments, xCount, new Vector3(ix, y, -iz), Vector3.left, ix * 2f, Vector3.back, capNormal, FindRingDistance(ring, new Vector2(ix, -iz - radius)));
            AddHorizontalEdge(data, settings, radius, segments, zCount, new Vector3(-ix, y, -iz), Vector3.forward, iz * 2f, Vector3.left, capNormal, FindRingDistance(ring, new Vector2(-ix - radius, -iz)));
        }

        private void AddHorizontalEdge(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            float radius,
            int segments,
            int axisCount,
            Vector3 startCenter,
            Vector3 axis,
            float axisLength,
            Vector3 sideNormal,
            Vector3 capNormal,
            float baseDistance)
        {
            axisLength = Mathf.Max(0.001f, axisLength);
            float capSign = Mathf.Sign(capNormal.y);
            RealBlendCreationCore.AddPatch(data, axisCount, segments + 1, (u, v) =>
            {
                float axisNorm = u / (float)(axisCount - 1);
                float theta = (v / (float)segments) * Mathf.PI * 0.5f;
                Vector3 normal = (sideNormal * Mathf.Cos(theta) + capNormal * Mathf.Sin(theta)).normalized;
                Vector3 position = startCenter + axis.normalized * (axisNorm * axisLength) + normal * radius;
                return new RealBlendPatchVertex(
                    position,
                    normal,
                    new Vector2(baseDistance + axisNorm * axisLength, startCenter.y + capSign * theta * radius));
            }, settings, Inward);
        }

        private void AddCorners(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            RoundedRingPoint[] ring,
            float radius,
            int count,
            float ix,
            float iz,
            float yCenter,
            bool top)
        {
            float sy = top ? 1f : -1f;
            AddCorner(data, settings, ring, radius, count, ix, iz, yCenter, 1f, 1f, sy);
            AddCorner(data, settings, ring, radius, count, ix, iz, yCenter, 1f, -1f, sy);
            AddCorner(data, settings, ring, radius, count, ix, iz, yCenter, -1f, -1f, sy);
            AddCorner(data, settings, ring, radius, count, ix, iz, yCenter, -1f, 1f, sy);
        }

        private void AddCorner(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            RoundedRingPoint[] ring,
            float radius,
            int count,
            float ix,
            float iz,
            float yCenter,
            float sx,
            float sz,
            float sy)
        {
            Vector3 center = new Vector3(sx * ix, yCenter, sz * iz);
            float startDistance = FindRingDistance(ring, new Vector2(center.x, center.z + sz * radius));
            float endDistance = FindRingDistance(ring, new Vector2(center.x + sx * radius, center.z));
            float distanceDelta = ResolveRingDistanceDelta(ring, startDistance, endDistance, Mathf.PI * 0.5f * radius);
            RealBlendCreationCore.AddPatch(data, count, count, (u, v) =>
            {
                float theta = (u / (float)(count - 1)) * Mathf.PI * 0.5f;
                float thetaNorm = u / (float)(count - 1);
                float phi = (v / (float)(count - 1)) * Mathf.PI * 0.5f;
                Vector3 horizontal = new Vector3(sx * Mathf.Sin(theta), 0f, sz * Mathf.Cos(theta));
                Vector3 normal = (horizontal * Mathf.Cos(phi) + Vector3.up * (sy * Mathf.Sin(phi))).normalized;
                Vector3 position = center + normal * radius;
                return new RealBlendPatchVertex(
                    position,
                    normal,
                    new Vector2(startDistance + distanceDelta * thetaNorm, yCenter + sy * phi * radius));
            }, settings, Inward);
        }

        private float FindRingDistance(RoundedRingPoint[] ring, Vector2 point)
        {
            if (ring == null || ring.Length == 0)
                return 0f;

            int bestIndex = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < ring.Length; i++)
            {
                float sqrDistance = (ring[i].point - point).sqrMagnitude;
                if (sqrDistance >= bestDistance)
                    continue;

                bestDistance = sqrDistance;
                bestIndex = i;
            }

            return ring[bestIndex].distance;
        }

        private float ResolveRingDistanceDelta(RoundedRingPoint[] ring, float startDistance, float endDistance, float expectedArcLength)
        {
            if (ring == null || ring.Length == 0)
                return endDistance - startDistance;

            float perimeter = Mathf.Max(0.0001f, ring[ring.Length - 1].distance);
            float direct = endDistance - startDistance;
            float forwardWrap = direct + perimeter;
            float backwardWrap = direct - perimeter;

            float best = direct;
            float bestScore = Mathf.Abs(Mathf.Abs(direct) - expectedArcLength);
            float forwardScore = Mathf.Abs(Mathf.Abs(forwardWrap) - expectedArcLength);
            if (forwardScore < bestScore)
            {
                best = forwardWrap;
                bestScore = forwardScore;
            }

            float backwardScore = Mathf.Abs(Mathf.Abs(backwardWrap) - expectedArcLength);
            if (backwardScore < bestScore)
                best = backwardWrap;

            return best;
        }

        private struct RoundedRingPoint
        {
            public Vector2 point;
            public Vector2 normal;
            public float distance;
        }
    }
}
