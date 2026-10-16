using System;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    /// <summary>An additive, explicit mesh-update tool; no model import hooks are installed.</summary>
    public sealed class RealBlendMeshPaintTransferWindow : EditorWindow
    {
        const string ExampleUsageUrl = "https://drive.google.com/file/d/1gd83T5ocgQ1maOtPFNSNLnbU2ksAD-9r/view?usp=sharing";

        [SerializeField] MeshFilter source;
        [SerializeField] Mesh updatedMesh;
        [SerializeField] float positionTolerance = .0001f;
        [SerializeField] bool projectNewVertices;
        [SerializeField] float projectionDistance = .05f;
        [SerializeField] float maximumNormalAngle = 60f;
        [SerializeField] bool hideOriginal = true;
        [SerializeField] int channel;
        [SerializeField] Vector2 orbit = new Vector2(20f, -25f);
        Vector2 scroll;
        MeshPaintTransferPreview preview;
        PreviewRenderUtility previewRenderer;
        PreviewRenderUtility updatedPreviewRenderer;
        Material previewMaterial;
        string message;
        MessageType messageType;
        static readonly string[] Channels = { "RGB", "R", "G", "B", "Alpha" };

        [MenuItem("Tools/RealBlend/Update Mesh and Transfer Paint")]
        public static void ShowWindow()
        {
            var window = GetWindow<RealBlendMeshPaintTransferWindow>();
            window.titleContent = new GUIContent("Transfer Mesh Paint");
            window.minSize = new Vector2(560, 660);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Transfer Mesh Paint");
            minSize = new Vector2(560, 660);
        }

        void OnDisable()
        {
            ClearPreview();
            previewRenderer?.Cleanup();
            previewRenderer = null;
            updatedPreviewRenderer?.Cleanup();
            updatedPreviewRenderer = null;
            if (previewMaterial != null) DestroyImmediate(previewMaterial);
            previewMaterial = null;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Update Mesh and Transfer Paint", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Keep the original painted object assigned to its original mesh. Choose the re-exported mesh below; this tool previews the paint transfer and creates a separate updated copy.", MessageType.Info);
            if (GUILayout.Button(new GUIContent("Example usage", "Open a demonstration of this tool in your browser (Google Drive).")))
                Application.OpenURL(ExampleUsageUrl);
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            source = (MeshFilter)EditorGUILayout.ObjectField(new GUIContent("Painted scene object", "The original static Mesh Filter with the paint you want to retain."), source, typeof(MeshFilter), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Use Selected Object"))
                {
                    source = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MeshFilter>() : null;
                    GUI.changed = true;
                }
            }
            updatedMesh = (Mesh)EditorGUILayout.ObjectField(new GUIContent("Updated mesh asset", "Expand the re-exported model in the Project window, then drag its mesh here."), updatedMesh, typeof(Mesh), false);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Match paint in mesh local space", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Use the same export origin, axes and scale as the original mesh.", EditorStyles.wordWrappedLabel);
            positionTolerance = EditorGUILayout.FloatField(new GUIContent("Position tolerance", "Local mesh units. Vertices at the same position retain their paint even when vertex indices change."), positionTolerance);
            projectNewVertices = EditorGUILayout.Toggle(new GUIContent("Project onto old surface", "Optional: transfer paint to new vertices from nearby triangles of the old mesh."), projectNewVertices);
            if (projectNewVertices)
            {
                EditorGUI.indentLevel++;
                projectionDistance = EditorGUILayout.FloatField(new GUIContent("Maximum distance", "Maximum projection distance in local mesh units."), projectionDistance);
                maximumNormalAngle = EditorGUILayout.Slider(new GUIContent("Maximum normal angle", "Reject nearby surfaces facing a different direction."), maximumNormalAngle, 0f, 180f);
                EditorGUI.indentLevel--;
            }
            if (EditorGUI.EndChangeCheck())
            {
                ClearPreview();
                message = null;
            }

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(source == null || updatedMesh == null))
                if (GUILayout.Button("Preview Paint Transfer", GUILayout.Height(30))) BuildPreview();

            if (preview != null)
            {
                EditorGUILayout.Space(8);
                PaintTransferReport report = preview.Report;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Transfer preview", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Same position", report.ExactMatches.ToString("N0"));
                    EditorGUILayout.LabelField("Projected onto surface", report.ProjectedMatches.ToString("N0"));
                    EditorGUILayout.LabelField("Unmatched", report.UnmatchedVertices.ToString("N0"));
                    EditorGUILayout.LabelField("Ambiguous (included in unmatched)", report.AmbiguousVertices.ToString("N0"));
                    EditorGUILayout.LabelField("Unmatched vertices retain the updated mesh's colors, or default paint when it has no colors.", EditorStyles.wordWrappedLabel);
                }
                foreach (string warning in preview.SourceSnapshot.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                foreach (string warning in report.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                channel = GUILayout.Toolbar(channel, Channels);
                Rect previewRect = GUILayoutUtility.GetRect(100, 230, GUILayout.ExpandWidth(true));
                HandleOrbit(previewRect);
                float halfWidth = (previewRect.width - 8f) * .5f;
                Rect oldRect = new Rect(previewRect.x, previewRect.y + 20, halfWidth, previewRect.height - 20);
                Rect newRect = new Rect(previewRect.x + halfWidth + 8, previewRect.y + 20, halfWidth, previewRect.height - 20);
                GUI.Label(new Rect(oldRect.x, previewRect.y, halfWidth, 20), "Original committed paint", EditorStyles.centeredGreyMiniLabel);
                GUI.Label(new Rect(newRect.x, previewRect.y, halfWidth, 20), "Updated mesh + transferred paint", EditorStyles.centeredGreyMiniLabel);
                Bounds bounds = preview.SourceSnapshot.Mesh.bounds;
                bounds.Encapsulate(preview.UpdatedMesh.bounds);
                DrawMeshPreview(oldRect, preview.SourceSnapshot.Mesh, bounds);
                DrawMeshPreview(newRect, preview.UpdatedMesh, bounds);
                EditorGUILayout.LabelField("Drag to rotate both previews. RGB shows vertex paint; Alpha shows its fourth channel.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space(8);
            hideOriginal = EditorGUILayout.ToggleLeft("Hide original object after creating the copy (kept as a backup)", hideOriginal);
            EditorGUILayout.HelpBox("The painted mesh is saved in Assets/RealBlendTransferredMeshes. Undo restores the original object and removes the scene copy; the new mesh asset is retained. Rebuild baked lighting for the updated copy, and review any references to the original object.", MessageType.Info);
            using (new EditorGUI.DisabledScope(preview == null))
                if (GUILayout.Button("Create Updated Painted Copy", GUILayout.Height(32))) CreateCopy();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        PaintTransferSettings CurrentSettings() => new PaintTransferSettings
        {
            PositionTolerance = positionTolerance,
            ProjectNewVertices = projectNewVertices,
            ProjectionDistance = projectionDistance,
            MaximumNormalAngle = maximumNormalAngle
        };

        void BuildPreview()
        {
            ClearPreview();
            try
            {
                if (updatedMesh == null || !EditorUtility.IsPersistent(updatedMesh) || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(updatedMesh)))
                    throw new InvalidOperationException("Choose a saved mesh asset from the Project window.");
                preview = RealBlendMeshPaintTransferWorkflow.BuildPreview(source, updatedMesh, CurrentSettings());
                message = null;
            }
            catch (Exception e) { message = e.GetBaseException().Message; messageType = MessageType.Error; }
            Repaint();
        }

        void CreateCopy()
        {
            try
            {
                if (!RealBlendMeshPaintTransferWorkflow.IsPreviewCurrent(preview, CurrentSettings(), out string reason))
                {
                    ClearPreview();
                    throw new InvalidOperationException(reason);
                }
                GameObject created = RealBlendMeshPaintTransferWorkflow.CreateCopy(preview, hideOriginal);
                Selection.activeGameObject = created;
                EditorGUIUtility.PingObject(created);
                ClearPreview();
                message = "Created " + created.name + ". The original paint and mesh are preserved. Save the scene when ready.";
                messageType = MessageType.Info;
            }
            catch (Exception e) { message = e.GetBaseException().Message; messageType = MessageType.Error; }
            Repaint();
        }

        void ClearPreview() { preview?.Dispose(); preview = null; }

        void HandleOrbit(Rect rect)
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseDrag && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                orbit.y += evt.delta.x * .5f;
                orbit.x = Mathf.Clamp(orbit.x + evt.delta.y * .5f, -89f, 89f);
                evt.Use();
                Repaint();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Orbit);
        }

        void DrawMeshPreview(Rect rect, Mesh mesh, Bounds bounds)
        {
            if (Event.current.type != EventType.Repaint || rect.width < 1 || rect.height < 1) return;
            if (previewMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/RealBlend/PaintTransferPreview");
                if (shader == null) { GUI.Label(rect, "Paint preview shader is unavailable."); return; }
                previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            bool isUpdated = preview != null && mesh == preview.UpdatedMesh;
            if (isUpdated && updatedPreviewRenderer == null) updatedPreviewRenderer = new PreviewRenderUtility();
            if (!isUpdated && previewRenderer == null) previewRenderer = new PreviewRenderUtility();
            PreviewRenderUtility renderer = isUpdated ? updatedPreviewRenderer : previewRenderer;
            previewMaterial.SetFloat("_Channel", channel - 1);
            renderer.BeginPreview(rect, GUIStyle.none);
            Camera camera = renderer.camera;
            float radius = Mathf.Max(bounds.extents.magnitude, .001f);
            float aspect = rect.width / rect.height;
            float distance = radius * 3.2f / Mathf.Min(1f, aspect);
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .12f, .12f, 1f);
            camera.nearClipPlane = Mathf.Max(radius * .001f, .00001f);
            camera.farClipPlane = distance + radius * 4f;
            camera.transform.position = Quaternion.Euler(orbit.x, orbit.y, 0) * new Vector3(0, 0, -distance);
            camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position, Vector3.up);
            Matrix4x4 matrix = Matrix4x4.Translate(-bounds.center);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) renderer.DrawMesh(mesh, matrix, previewMaterial, submesh);
            renderer.Render(false);
            Texture texture = renderer.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }
    }
}
