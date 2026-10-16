using UnityEngine;
using UnityEditor;
using System.IO;


namespace RealBlend
{
    [CustomEditor(typeof(VertexPaintStorage))]
    public class VertexPaintStorageEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // CRITICAL: Check if target was destroyed
            if (target == null)
            {
                EditorGUILayout.LabelField("VertexPaintStorage was removed.", EditorStyles.wordWrappedLabel);
                return;
            }

            VertexPaintStorage storage = (VertexPaintStorage)target;

            DrawDefaultInspector();

            GUILayout.Space(10);
            GUILayout.Label("Mesh Management", EditorStyles.boldLabel);

            if (storage.IsProBuilderOwnedMesh)
            {
                DrawProBuilderManagement(storage);
                return;
            }

            if (storage.hasOriginalData)
                EditorGUILayout.HelpBox($"Ready • {storage.originalVertices.Length} vertices", MessageType.Info);
            else
                EditorGUILayout.HelpBox("No base mesh captured yet", MessageType.Warning);

            GUILayout.Space(10);

            // Global Folder
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Bake Folder", VertexPaintStorage.GlobalBakePath);

            if (GUILayout.Button("Change", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFolderPanel("Global Bake Folder", VertexPaintStorage.GlobalBakePath, "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                {
                    VertexPaintStorage.SetGlobalBakePath(
                        "Assets" + path.Substring(Application.dataPath.Length).Replace("\\", "/"));
                }
            }

            if (GUILayout.Button("Open", GUILayout.Width(60)))
            {
                string full = Path.GetFullPath(VertexPaintStorage.GlobalBakePath);
                if (Directory.Exists(full)) EditorUtility.RevealInFinder(full);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Bake to Asset (Permanent)", GUILayout.Height(45)))
            {
                storage.BakeMeshAsset();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            if (GUILayout.Button("Force Rebuild Mesh", GUILayout.Height(30)))
                storage.RebuildMesh();

            if (storage.hasOriginalData)
            {
                GUILayout.Space(8);
                string preview = $"{VertexPaintStorage.GlobalBakePath}/{storage.gameObject.name}_Baked.asset";
                EditorGUILayout.LabelField("Save path:", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(preview, GUILayout.Height(18));
            }

            GUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "After baking:\n" +
                "• Vertex colors are permanently saved\n" +
                "• GameObject uses the new mesh asset\n" +
                "• Component can be safely removed",
                MessageType.Info);
        }

        private static void DrawProBuilderManagement(
            VertexPaintStorage storage)
        {
            MeshFilter filter = storage.GetComponent<MeshFilter>();
            EditorGUILayout.HelpBox(
                "ProBuilder owns this object's editable geometry. RealBlend paint is sent to ProBuilder automatically, and completed ProBuilder edits are captured back into this storage component.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent(
                        "Sync From ProBuilder",
                        "Rebuilds ProBuilder's compiled mesh and adopts its current geometry and vertex colors as RealBlend's stored state."),
                    GUILayout.Height(32)))
            {
                if (!RealBlendProBuilderColorBridge.TrySyncFromProBuilder(
                        filter,
                        storage,
                        out string error))
                {
                    EditorUtility.DisplayDialog(
                        "ProBuilder Sync Failed",
                        string.IsNullOrEmpty(error)
                            ? "RealBlend could not read this ProBuilder mesh."
                            : error,
                        "OK");
                }
            }

            if (GUILayout.Button(
                    new GUIContent(
                        "Send Paint to ProBuilder",
                        "Maps RealBlend's stored vertex colors onto ProBuilder without replacing its topology."),
                    GUILayout.Height(32)))
            {
                if (!RealBlendProBuilderColorBridge.TrySendPaintToProBuilder(
                        filter,
                        storage,
                        out string error))
                {
                    EditorUtility.DisplayDialog(
                        "Paint Sync Failed",
                        string.IsNullOrEmpty(error)
                            ? "RealBlend could not send its stored paint to ProBuilder."
                            : error,
                        "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button("Bake to Asset (Finalize ProBuilder First)",
                    GUILayout.Height(36));
                GUILayout.Button("Force Rebuild Mesh (Owned by ProBuilder)",
                    GUILayout.Height(28));
            }
            EditorGUILayout.HelpBox(
                "Storage-side topology rebuild, Bake to Asset, and RealBlend's topology-changing Sculpt tools are disabled while ProBuilder is attached because those results would be replaced by ProBuilder's authoritative mesh. Vertex painting remains fully supported. Finalize or remove ProBuilder first for RealBlend topology edits or a permanent baked Unity mesh.",
                MessageType.Warning);
        }
    }
}
