using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace RealBlend
{
    public class RealBlend : EditorWindow
    {
        // --- Tab Management ---
        public enum PainterTab { Paint = 0, Create = 1, Sculpt = 2 }
        private PainterTab _currentTab = PainterTab.Paint;
        private string[] _tabNames = { "🎨 Paint", "📐 Create (Beta)", "🗿 Sculpt (Beta)" };

        // --- The Sub-Tools ---
        // [SerializeField] ensures the state of the tabs is saved during reloads
        [SerializeField] private VertexPainter_PaintTab _paintTool;
        [SerializeField] private VertexPainter_CreationTab _createTool;
        [SerializeField] private VertexPainter_SculptTab _sculptTool;
        private bool _sceneToolsSuspendedForHiddenWindow;

        private static readonly FieldInfo ParentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly BindingFlags InstanceAnyVisibility = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/RealBlend/Real Blend")]
        public static void ShowWindow()
        {
            RealBlend wnd = GetWindow<RealBlend>("Real Blend");
            wnd.Show();
        }

        void OnEnable()
        {
            // 1. Initialise tools if they don't exist, then rebind transient owner refs after reloads.
            EnsureTools();

            // 2. Call OnEnable on the active tool
            GetActiveTool()?.OnEnable();

            // 3. Subscribe to Editor Events
            SceneView.duringSceneGui -= OnSceneGUI; // Prevent double subscription
            SceneView.duringSceneGui += OnSceneGUI;

            Undo.undoRedoEvent -= OnUndoRedoEvent;
            Undo.undoRedoEvent += OnUndoRedoEvent;

            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        void OnDisable()
        {
            // CRITICAL FIX: We do NOT call tool.OnDisable() or stop the paint mode here.
            // OnDisable is called when maximizing/unmaximizing windows.

            // Cached Boolean probe meshes are transient native objects, not a
            // scene interaction mode. Releasing only these is safe for window
            // layout changes and protects unusual disable/reload ordering.
            _sculptTool?.ReleaseTransientBooleanResults();

            // Then unsubscribe editor events.
            EditorApplication.delayCall -= SuspendSceneToolsIfHidden;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            Selection.selectionChanged -= OnSelectionChanged;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        void OnDestroy()
        {
            // This is where we actually stop the tool when the window is closed
            EditorApplication.delayCall -= SuspendSceneToolsIfHidden;
            _paintTool?.ForceStop();
            _paintTool?.ReleaseUndoCallbacks();
            _createTool?.OnDisable();
            _sculptTool?.OnDisable();
        }

        void OnFocus()
        {
            ResumeSceneToolsIfVisible();
        }

        void OnLostFocus()
        {
            EditorApplication.delayCall -= SuspendSceneToolsIfHidden;
            EditorApplication.delayCall += SuspendSceneToolsIfHidden;
        }

        void OnUndoRedo()
        {
            EnsureTools();

            IVertexPainterTab activeTool = GetActiveTool();
            activeTool?.OnUndoRedo();
            if (!ReferenceEquals(activeTool, _sculptTool))
                _sculptTool?.OnUndoRedo();
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnUndoRedoEvent(in UndoRedoInfo undo)
        {
            EnsureTools();

            if (VertexPaintUndoDispatcher.IsVertexColorUndo(undo.undoName))
            {
                // Vertex paint changes only Mesh.colors. Avoid the Sculpt
                // refresh, collider work, and paint topology-cache rebuild used
                // by geometry Undo operations.
                SceneView.RepaintAll();
                Repaint();
                return;
            }

            OnUndoRedo();
        }

        void OnSelectionChanged()
        {
            // Force tools to re-evaluate if they should be active
            GetActiveTool()?.OnSelectionChange();
            Repaint();
            SceneView.RepaintAll();
        }

        void OnAfterAssemblyReload()
        {
            EnsureTools();

            // Re-init everything to fix broken textures/references
            if (_paintTool != null) _paintTool.OnReload();
            if (_sculptTool != null) _sculptTool.OnReload();
            if (_createTool != null) _createTool.OnReload();

            Repaint();
        }

        void OnBeforeAssemblyReload()
        {
            // Commit any live color stroke before transient editor state is
            // discarded by the domain reload.
            _paintTool?.ForceStop();
            _sculptTool?.ReleaseTransientBooleanResults();
        }

        private IVertexPainterTab GetActiveTool()
        {
            switch (_currentTab)
            {
                case PainterTab.Paint: return _paintTool;
                case PainterTab.Create: return _createTool;
                case PainterTab.Sculpt: return _sculptTool;
                default: return _paintTool;
            }
        }

        void OnGUI()
        {
            EnsureTools();
            ResumeSceneToolsIfVisible();

            // --- Styling for the Tabs ---
            // Create a style based on the standard button but larger and bolder
            GUIStyle tabStyle = new GUIStyle(GUI.skin.button);
            tabStyle.fixedHeight = 35;       // Taller, more clickable
            tabStyle.fontSize = 13;          // Larger text
            tabStyle.fontStyle = FontStyle.Bold;

            // Remove margins so the buttons touch each other (segmented look)
            tabStyle.margin = new RectOffset(0, 0, 0, 0);

            // Optional: Ensure normal text color is readable
            tabStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

            // --- Tab Header Container ---
            GUILayout.Space(10);

            // Wrap the tabs in a HelpBox style for a nice background frame
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(2); // Small padding inside the box

                EditorGUI.BeginChangeCheck();

                // Using standard button style base creates a "Segmented" look when margins are 0
                var newTab = (PainterTab)GUILayout.Toolbar((int)_currentTab, _tabNames, tabStyle, GUILayout.Height(35));

                if (EditorGUI.EndChangeCheck())
                {
                    // Transient disable for switching tabs
                    if (_currentTab == PainterTab.Paint) _paintTool.ForceStop();
                    else GetActiveTool()?.OnDisable();

                    _currentTab = newTab;
                    GetActiveTool()?.OnEnable();
                    SceneView.RepaintAll();
                }
                GUILayout.Space(2);
            }

            GUILayout.Space(10);

            // Wrap logic in Try-Catch to prevent entire window from freezing if a single tool crashes
            try
            {
                GetActiveTool()?.OnGUI();
            }
            catch (System.Exception e)
            {
                // If the GUI crashes, we want to know why, but we don't want the window to become unusable
                // Only log if it's a Repaint event to avoid spamming console during Layout
                if (Event.current.type == EventType.Repaint)
                    Debug.LogError("Painter GUI Error: " + e.Message + "\n" + e.StackTrace);
            }
        }

        void OnSceneGUI(SceneView sceneView)
        {
            EnsureTools();
            if (!SceneToolsCanRun())
                return;

            GetActiveTool()?.OnSceneGUI(sceneView);
        }

        private bool SceneToolsCanRun()
        {
            if (!IsVisibleEditorTab())
            {
                SuspendSceneInteractionForHiddenWindow();
                return false;
            }

            ResumeSceneToolsIfVisible();
            return true;
        }

        private void SuspendSceneToolsIfHidden()
        {
            if (this == null)
                return;

            if (!IsVisibleEditorTab())
                SuspendSceneInteractionForHiddenWindow();
        }

        private void SuspendSceneInteractionForHiddenWindow()
        {
            if (_sceneToolsSuspendedForHiddenWindow)
                return;

            EnsureTools();
            if (_currentTab == PainterTab.Paint)
                _paintTool?.ForceStop();
            else
                GetActiveTool()?.OnDisable();

            _sceneToolsSuspendedForHiddenWindow = true;
            SceneView.RepaintAll();
        }

        private void ResumeSceneToolsIfVisible()
        {
            if (!_sceneToolsSuspendedForHiddenWindow || !IsVisibleEditorTab())
                return;

            _sceneToolsSuspendedForHiddenWindow = false;
            GetActiveTool()?.OnEnable();
            SceneView.RepaintAll();
        }

        private bool IsVisibleEditorTab()
        {
            object parent = ParentField?.GetValue(this);
            if (parent == null)
                return true;

            EditorWindow actualView = GetActualView(parent);
            return actualView == null || actualView == this;
        }

        private static EditorWindow GetActualView(object parent)
        {
            if (parent == null)
                return null;

            for (System.Type parentType = parent.GetType(); parentType != null; parentType = parentType.BaseType)
            {
                PropertyInfo property = parentType.GetProperty("actualView", InstanceAnyVisibility);
                if (property != null && typeof(EditorWindow).IsAssignableFrom(property.PropertyType))
                    return property.GetValue(parent, null) as EditorWindow;

                FieldInfo field = parentType.GetField("actualView", InstanceAnyVisibility);
                if (field != null && typeof(EditorWindow).IsAssignableFrom(field.FieldType))
                    return field.GetValue(parent) as EditorWindow;
            }

            return null;
        }

        private void EnsureTools()
        {
            if (_paintTool == null) _paintTool = new VertexPainter_PaintTab(this);
            else _paintTool.RebindOwner(this);

            if (_createTool == null) _createTool = new VertexPainter_CreationTab(this);
            else _createTool.RebindOwner(this);

            if (_sculptTool == null) _sculptTool = new VertexPainter_SculptTab(this);
            else _sculptTool.RebindOwner(this);
        }
    }

}
