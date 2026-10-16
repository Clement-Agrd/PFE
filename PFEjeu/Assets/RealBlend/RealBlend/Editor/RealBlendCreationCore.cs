using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    public enum RealBlendUVRotation
    {
        Degrees0,
        Degrees90Right,
        Degrees180,
        Degrees270Right
    }

    [Serializable]
    public class RealBlendCreationSettings
    {
        [Range(1, 10)] public int resolutionPerMeter = 2;
        public Material defaultMaterial;
        public bool showPreview = true;
        public bool showTriangles = false;
        public bool smartPreview = true;
        public bool incrementMode = false;
        public float increment = 0.5f;
        public Vector2 tileSize = Vector2.one;
        public RealBlendUVRotation uvRotation = RealBlendUVRotation.Degrees0;
        public int raycastLayerMask = Physics.DefaultRaycastLayers;

        public Vector2 ApplyUV(Vector2 meters)
        {
            return RealBlendCreationCore.ApplyUV(meters, this);
        }
    }

    public class RealBlendCreationPreviewState
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool hasValidPreview;
    }

    public struct RealBlendPlacement
    {
        public Vector3 position;
        public Quaternion rotation;

        public static RealBlendPlacement Identity => new RealBlendPlacement
        {
            position = Vector3.zero,
            rotation = Quaternion.identity
        };
    }

    public struct RealBlendPatchVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uvMeters;

        public RealBlendPatchVertex(Vector3 position, Vector3 normal, Vector2 uvMeters)
        {
            this.position = position;
            this.normal = normal;
            this.uvMeters = uvMeters;
        }
    }

    public class RealBlendMeshBuildData
    {
        public string name = "RealBlend Mesh";
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public bool hasExplicitNormals;

        public int AddVertex(Vector3 position, Vector2 uv)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uvs.Add(uv);
            colors.Add(Color.clear);
            normals.Add(Vector3.zero);
            return index;
        }

        public int AddVertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uvs.Add(uv);
            colors.Add(Color.clear);
            normals.Add(normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up);
            hasExplicitNormals = true;
            return index;
        }

        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);

            if (hasExplicitNormals && normals.Count == vertices.Count)
                mesh.SetNormals(normals);
            else
                mesh.RecalculateNormals();

            RealBlendMeshUVUtility.GenerateLightmapUVs(mesh);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public interface IRealBlendCreationMode
    {
        string DisplayName { get; }
        void OnGUI(RealBlendCreationSettings settings);
        void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview);
        bool CanGenerate(RealBlendCreationSettings settings);
        RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings);
        RealBlendPlacement GetPlacement(RealBlendCreationPreviewState preview);
        void GetStats(RealBlendCreationSettings settings, out int vertexCount, out int triangleCount);
    }

    public struct RealBlendAdditionalMeshBuild
    {
        public RealBlendMeshBuildData data;
        public Material material;
        public RealBlendPlacement placement;
    }

    public interface IRealBlendAdditionalCreationMode
    {
        List<RealBlendAdditionalMeshBuild> BuildAdditionalMeshes(RealBlendCreationSettings settings, RealBlendCreationPreviewState preview);
    }

    public static class RealBlendLayerMaskUtility
    {
        public static readonly string[] LayerNames = BuildLayerNames();

        private static string[] BuildLayerNames()
        {
            string[] names = new string[32];
            for (int i = 0; i < names.Length; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                names[i] = string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName;
            }

            return names;
        }
    }

    public static class RealBlendCreationCore
    {
        public static Vector2 ApplyUV(Vector2 meters, RealBlendCreationSettings settings)
        {
            float tileU = Mathf.Max(0.0001f, Mathf.Abs(settings.tileSize.x));
            float tileV = Mathf.Max(0.0001f, Mathf.Abs(settings.tileSize.y));
            Vector2 uv = new Vector2(meters.x / tileU, meters.y / tileV);

            switch (settings.uvRotation)
            {
                case RealBlendUVRotation.Degrees90Right:
                    return new Vector2(uv.y, -uv.x);
                case RealBlendUVRotation.Degrees180:
                    return new Vector2(-uv.x, -uv.y);
                case RealBlendUVRotation.Degrees270Right:
                    return new Vector2(-uv.y, uv.x);
                default:
                    return uv;
            }
        }

        public static int ResolutionCount(float meters, int resolutionPerMeter)
        {
            return Mathf.Max(Mathf.RoundToInt(Mathf.Max(0.01f, Mathf.Abs(meters)) * Mathf.Max(1, resolutionPerMeter)) + 1, 2);
        }

        public static int SegmentCount(float meters, int resolutionPerMeter)
        {
            return Mathf.Max(ResolutionCount(meters, resolutionPerMeter) - 1, 1);
        }

        public static void DrawSharedSettings(RealBlendCreationSettings settings)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Shared Mesh Settings", EditorStyles.boldLabel);
            settings.resolutionPerMeter = EditorGUILayout.IntSlider("Density (Verts/m)", settings.resolutionPerMeter, 1, 10);
            settings.defaultMaterial = (Material)EditorGUILayout.ObjectField("Default Material", settings.defaultMaterial, typeof(Material), false);

            Vector2 tileSize = settings.tileSize;
            tileSize.x = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Tile Size U", tileSize.x));
            tileSize.y = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Tile Size V", tileSize.y));
            settings.tileSize = tileSize;
            settings.uvRotation = (RealBlendUVRotation)EditorGUILayout.EnumPopup("UV Rotation", settings.uvRotation);
            if (settings.raycastLayerMask == 0)
                settings.raycastLayerMask = Physics.DefaultRaycastLayers;
            settings.raycastLayerMask = EditorGUILayout.MaskField("Raycast Layers", settings.raycastLayerMask, RealBlendLayerMaskUtility.LayerNames);

            GUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.showPreview = EditorGUILayout.Toggle("Show Preview", settings.showPreview);
                using (new EditorGUI.DisabledScope(!settings.showPreview))
                    settings.showTriangles = EditorGUILayout.Toggle("Show Triangles", settings.showTriangles);
            }

            using (new EditorGUI.DisabledScope(!settings.showPreview))
                settings.smartPreview = EditorGUILayout.Toggle("Smart Preview", settings.smartPreview);

            settings.incrementMode = EditorGUILayout.Toggle("Increment Mode", settings.incrementMode);
            if (settings.incrementMode)
                settings.increment = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Increment Value", settings.increment));
        }

        public static void DrawStats(int vertexCount, int triangleCount)
        {
            GUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Triangle Info:", EditorStyles.boldLabel);
                GUILayout.Label($" Vertices: {vertexCount:N0}");
                GUILayout.Label($" Triangles: {triangleCount:N0}");
            }
        }

        public static GameObject CreatePaintableObject(RealBlendMeshBuildData data, Material defaultMaterial, RealBlendPlacement placement)
        {
            if (data == null || data.vertices.Count < 3 || data.triangles.Count < 3)
            {
                Debug.LogWarning("RealBlend could not create a mesh because the generated topology is empty.");
                return null;
            }

            Mesh mesh = data.ToMesh();
            GameObject go = new GameObject(mesh.name);
            go.transform.SetPositionAndRotation(placement.position, placement.rotation);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = defaultMaterial != null
                ? defaultMaterial
                : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            VertexPaintStorage storage = go.AddComponent<VertexPaintStorage>();
            storage.CaptureOriginals(mesh);
            storage.SetOwnedEditableMesh(mesh);
            storage.SetGeneratedSolidWall(string.Equals(
                mesh.name,
                "RealBlend Solid Wall",
                StringComparison.OrdinalIgnoreCase));
            storage.paintedColors = mesh.colors;

            Undo.RegisterCreatedObjectUndo(go, "Create RealBlend Mesh");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            return go;
        }

        public static void UpdateScenePlacement(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview, bool floorMode)
        {
            Camera cam = sceneView.camera;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            bool hitFound = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, settings.raycastLayerMask, QueryTriggerInteraction.Ignore);
            Vector3 targetPoint = hitFound ? hit.point : ray.GetPoint(5f);

            float snappedY = Mathf.Round(cam.transform.eulerAngles.y / 45f) * 45f;
            preview.rotation = Quaternion.Euler(0, snappedY, 0);
            preview.position = floorMode ? targetPoint + new Vector3(0, 0.02f, 0) : targetPoint;

            if (settings.incrementMode && settings.increment > 0f)
            {
                preview.position.x = Mathf.Round(preview.position.x / settings.increment) * settings.increment;
                preview.position.y = Mathf.Round(preview.position.y / settings.increment) * settings.increment;
                preview.position.z = Mathf.Round(preview.position.z / settings.increment) * settings.increment;
            }

            preview.hasValidPreview = true;
        }

        public static void DrawCenterCrosshair(SceneView sceneView)
        {
            Handles.BeginGUI();
            Vector3 center = new Vector3(sceneView.position.width / 2f, sceneView.position.height / 2f, 0);
            Handles.color = new Color(0, 1, 1, 0.8f);
            Handles.DrawLine(center - new Vector3(15, 0, 0), center + new Vector3(15, 0, 0));
            Handles.DrawLine(center - new Vector3(0, 15, 0), center + new Vector3(0, 15, 0));
            Handles.EndGUI();
        }

        public static void DrawGridPreview(
            Func<float, float, Vector3> getPosition,
            int xCount,
            int yCount,
            float widthMeters,
            float heightMeters,
            RealBlendCreationSettings settings)
        {
            if (settings.smartPreview)
            {
                Handles.color = new Color(1f, 0.92f, 0.016f, 0.45f);
                DrawGridOutline(getPosition, Mathf.Clamp(xCount * 2, 8, 128));

                Handles.color = new Color(1f, 0.92f, 0.016f, 0.8f);
                DrawDensitySample(getPosition, widthMeters, heightMeters, settings);
            }
            else
            {
                Handles.color = new Color(1f, 0.92f, 0.016f, 0.6f);
                DrawFullGrid(getPosition, xCount, yCount, settings.showTriangles);
            }
        }

        public static void DrawBoxPreview(Vector3 size, RealBlendCreationSettings settings)
        {
            Vector3 min = new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f);
            Vector3 max = new Vector3(size.x * 0.5f, size.y, size.z * 0.5f);
            Vector3[] corners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };

            Handles.color = new Color(1f, 0.92f, 0.016f, 0.65f);
            DrawLine(corners, 0, 1);
            DrawLine(corners, 1, 2);
            DrawLine(corners, 2, 3);
            DrawLine(corners, 3, 0);
            DrawLine(corners, 4, 5);
            DrawLine(corners, 5, 6);
            DrawLine(corners, 6, 7);
            DrawLine(corners, 7, 4);
            DrawLine(corners, 0, 4);
            DrawLine(corners, 1, 5);
            DrawLine(corners, 2, 6);
            DrawLine(corners, 3, 7);
        }

        private static void DrawLine(Vector3[] corners, int a, int b)
        {
            Handles.DrawLine(corners[a], corners[b]);
        }

        private static void DrawGridOutline(Func<float, float, Vector3> getPosition, int edgeSegments)
        {
            DrawEdge(getPosition, 0f, true, edgeSegments);
            DrawEdge(getPosition, 1f, true, edgeSegments);
            DrawEdge(getPosition, 0f, false, edgeSegments);
            DrawEdge(getPosition, 1f, false, edgeSegments);
        }

        private static void DrawEdge(Func<float, float, Vector3> getPosition, float fixedValue, bool horizontal, int segments)
        {
            Vector3 previous = horizontal ? getPosition(0f, fixedValue) : getPosition(fixedValue, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 current = horizontal ? getPosition(t, fixedValue) : getPosition(fixedValue, t);
                Handles.DrawLine(previous, current);
                previous = current;
            }
        }

        private static void DrawDensitySample(Func<float, float, Vector3> getPosition, float widthMeters, float heightMeters, RealBlendCreationSettings settings)
        {
            float effectiveWidth = Mathf.Max(0.01f, Mathf.Abs(widthMeters));
            float effectiveHeight = Mathf.Max(0.01f, Mathf.Abs(heightMeters));
            float sampleWidthMeters = Mathf.Min(1f, effectiveWidth);
            float sampleHeightMeters = Mathf.Min(1f, effectiveHeight);
            float sampleXSpan = sampleWidthMeters / effectiveWidth;
            float sampleYSpan = sampleHeightMeters / effectiveHeight;
            float sampleXStart = Mathf.Clamp01(0.5f - (sampleXSpan * 0.5f));
            float sampleYStart = 0f;
            int xCells = Mathf.Max(1, Mathf.RoundToInt(sampleWidthMeters * settings.resolutionPerMeter));
            int yCells = Mathf.Max(1, Mathf.RoundToInt(sampleHeightMeters * settings.resolutionPerMeter));

            Color baseColor = Handles.color;
            Color triangleColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.4f);

            for (int y = 0; y < yCells; y++)
            {
                float yNorm = sampleYStart + (y / (float)yCells) * sampleYSpan;
                float yNormNext = sampleYStart + ((y + 1) / (float)yCells) * sampleYSpan;

                for (int x = 0; x < xCells; x++)
                {
                    float xNorm = sampleXStart + (x / (float)xCells) * sampleXSpan;
                    float xNormNext = sampleXStart + ((x + 1) / (float)xCells) * sampleXSpan;
                    Vector3 p0 = getPosition(xNorm, yNorm);
                    Vector3 p1 = getPosition(xNormNext, yNorm);
                    Vector3 p2 = getPosition(xNorm, yNormNext);
                    Vector3 p3 = getPosition(xNormNext, yNormNext);

                    Handles.DrawLine(p0, p1);
                    Handles.DrawLine(p0, p2);
                    if (x == xCells - 1) Handles.DrawLine(p1, p3);
                    if (y == yCells - 1) Handles.DrawLine(p2, p3);

                    if (settings.showTriangles)
                    {
                        Handles.color = triangleColor;
                        Handles.DrawLine(p0, p3);
                        Handles.color = baseColor;
                    }
                }
            }

            Handles.Label(getPosition(sampleXStart + sampleXSpan, sampleYStart + sampleYSpan), "1m x 1m density sample");
        }

        private static void DrawFullGrid(Func<float, float, Vector3> getPosition, int xCount, int yCount, bool showTriangles)
        {
            if (xCount * yCount > 15000) return;

            Color baseColor = Handles.color;
            Color triangleColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.4f);

            for (int y = 0; y < yCount - 1; y++)
            {
                float yNorm = y / (float)(yCount - 1);
                float yNormNext = (y + 1) / (float)(yCount - 1);

                for (int x = 0; x < xCount - 1; x++)
                {
                    float xNorm = x / (float)(xCount - 1);
                    float xNormNext = (x + 1) / (float)(xCount - 1);
                    Vector3 p0 = getPosition(xNorm, yNorm);
                    Vector3 p1 = getPosition(xNormNext, yNorm);
                    Vector3 p2 = getPosition(xNorm, yNormNext);
                    Vector3 p3 = getPosition(xNormNext, yNormNext);

                    Handles.DrawLine(p0, p1);
                    Handles.DrawLine(p0, p2);
                    if (x == xCount - 2) Handles.DrawLine(p1, p3);
                    if (y == yCount - 2) Handles.DrawLine(p2, p3);

                    if (showTriangles)
                    {
                        Handles.color = triangleColor;
                        Handles.DrawLine(p0, p3);
                        Handles.color = baseColor;
                    }
                }
            }
        }

        public static void AddGridSurface(
            RealBlendMeshBuildData data,
            int xCount,
            int yCount,
            Func<float, float, Vector3> getPosition,
            Func<float, float, Vector2> getUVMeters,
            RealBlendCreationSettings settings,
            bool flip = false)
        {
            int[,] indices = new int[yCount, xCount];
            for (int y = 0; y < yCount; y++)
            {
                float yNorm = y / (float)(yCount - 1);
                for (int x = 0; x < xCount; x++)
                {
                    float xNorm = x / (float)(xCount - 1);
                    indices[y, x] = data.AddVertex(
                        getPosition(xNorm, yNorm),
                        settings.ApplyUV(getUVMeters(xNorm, yNorm)));
                }
            }

            for (int y = 0; y < yCount - 1; y++)
            {
                for (int x = 0; x < xCount - 1; x++)
                {
                    AddQuad(data, indices[y, x], indices[y, x + 1], indices[y + 1, x], indices[y + 1, x + 1], flip);
                }
            }
        }

        public static void AddPatch(
            RealBlendMeshBuildData data,
            int uCount,
            int vCount,
            Func<int, int, RealBlendPatchVertex> getVertex,
            RealBlendCreationSettings settings,
            bool invertNormals)
        {
            if (uCount < 2 || vCount < 2) return;

            int[,] indices = new int[vCount, uCount];
            for (int v = 0; v < vCount; v++)
            {
                for (int u = 0; u < uCount; u++)
                {
                    RealBlendPatchVertex vertex = getVertex(u, v);
                    Vector3 normal = invertNormals ? -vertex.normal : vertex.normal;
                    indices[v, u] = data.AddVertex(vertex.position, settings.ApplyUV(vertex.uvMeters), normal);
                }
            }

            for (int v = 0; v < vCount - 1; v++)
            {
                for (int u = 0; u < uCount - 1; u++)
                {
                    AddOrientedQuad(data, indices[v, u], indices[v, u + 1], indices[v + 1, u], indices[v + 1, u + 1]);
                }
            }
        }

        public static void AddQuad(RealBlendMeshBuildData data, int lowerLeft, int lowerRight, int upperLeft, int upperRight, bool flip)
        {
            if (!flip)
            {
                data.triangles.Add(lowerLeft);
                data.triangles.Add(upperLeft);
                data.triangles.Add(lowerRight);
                data.triangles.Add(lowerRight);
                data.triangles.Add(upperLeft);
                data.triangles.Add(upperRight);
            }
            else
            {
                data.triangles.Add(lowerLeft);
                data.triangles.Add(lowerRight);
                data.triangles.Add(upperLeft);
                data.triangles.Add(lowerRight);
                data.triangles.Add(upperRight);
                data.triangles.Add(upperLeft);
            }
        }

        public static void AddOrientedQuad(RealBlendMeshBuildData data, int lowerLeft, int lowerRight, int upperLeft, int upperRight)
        {
            Vector3 a = data.vertices[lowerLeft];
            Vector3 b = data.vertices[lowerRight];
            Vector3 c = data.vertices[upperLeft];
            Vector3 desired = (
                data.normals[lowerLeft] +
                data.normals[lowerRight] +
                data.normals[upperLeft] +
                data.normals[upperRight]) * 0.25f;

            Vector3 triangleNormal = Vector3.Cross(c - a, b - a);
            bool flip = desired.sqrMagnitude > 0.000001f && Vector3.Dot(triangleNormal, desired) < 0f;
            AddQuad(data, lowerLeft, lowerRight, upperLeft, upperRight, flip);
        }
    }
}
