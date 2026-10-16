using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace RealBlend
{
    [System.Serializable]
    public class VertexPainter_SculptTab : IVertexPainterTab
    {
        [System.NonSerialized] private EditorWindow _owner;

        // "Simplify" is now "Revert to Base" in the enum
        // Added advanced modes for direct surface editing.
        public enum SculptMode
        {
            Move = 0,
            Push = 1,
            Smooth = 2,
            Revert_Pose = 3,
            Refine_Add = 4,
            Remove = 5,
            Nudge = 6,
            Flatten_Height = 7,
            Select_Vertex = 8,
            Boolean_Cut = 9
        }
        public SculptMode currentMode = SculptMode.Move;

        // --- Brush Shape ---
        public enum BrushShape { Circle = 0, Square = 1 }
        public BrushShape brushShape = BrushShape.Circle;
        public enum LocalFlattenAxis { X = 0, Y = 2, Z = 4 }
        public enum ElementSelectionMode { Vertex = 0, Edge = 1, Face = 2 }
        public ElementSelectionMode elementSelectionMode = ElementSelectionMode.Vertex;

        // For square brushes the user can choose separate width and height. Values represent world-space dimensions.
        public float squareBrushWidth = 1.0f;
        public float squareBrushHeight = 1.0f;

        // Cache the last hit position and normal from the scene view.
        private Vector3 lastHitPoint;
        private Vector3 lastHitNormal;
        private Vector3 lastHitRayDirection;
        private bool nudgeStrokeActive;
        private Vector3 nudgeLastHitPoint;
        private int selectedVertexIndex = -1;
        private int hoveredVertexIndex = -1;
        private int[] selectedElementAnchors;
        private int[] hoveredElementAnchors;
        private List<int> selectedElementWeldedIndices;
        private ElementSelectionMode selectedElementMode;
        private Quaternion elementHandleRotation = Quaternion.identity;
        private Vector3 elementHandleScale = Vector3.one;
        private Vector3[] elementHandleStartWorldPositions;
        private Vector3 elementHandleStartCenter;

        // --- Brush Settings ---
        public float brushSize = 1.0f;
        public float brushStrength = 0.5f;
        public Texture2D brushAlpha;
        public float brushRotation = 0f;
        public bool invertBrushAlpha = false;
        // These fields used to belong to an unfinished shaped-remove path, so
        // keep their serialized names for window-state compatibility. Circle
        // shaping moves only the existing boundary vertices. The square wall
        // option is a separately validated opening/reveal operation.
        [HideInInspector] public bool shapeCircularRemoveBoundary = false;
        [HideInInspector, Range(8, 64)] public int circularRemoveSegments = 32;
        [HideInInspector] public bool removeThroughWall = false;
        public bool removeUseSeparateInteriorSubmesh;
        public Material removeInteriorMaterial;
        [SerializeField] private int removeTopologySettingsVersion;
        public bool autoSampleFlattenHeight = true;
        public LocalFlattenAxis flattenLocalAxis = LocalFlattenAxis.Y;
        public float flattenHeight = 0f;
        public float flattenHeightOffset = 0f;

        // --- Visuals ---
        public bool showVertexGizmo = true;
        public bool showTriangleGizmo = true;
        public Color gizmoColor = Color.yellow;
        public float gizmoSize = 0.03f;

        // --- Boolean Cut ---
        [SerializeField] private List<GameObject> booleanTargets = new List<GameObject>();
        // Legacy single-target field retained so existing serialized editor
        // windows and integrations migrate without losing their selection.
        public GameObject booleanTarget;
        public GameObject booleanCutter;
        public Material booleanCutSurfaceMaterial;
        public Color booleanCutSurfaceColor = Color.clear;
        public bool booleanUseSeparateInteriorSubmesh;
        public bool booleanLeaveCutOpen;
        public bool hideBooleanCutterAfterApply = true;
        public bool showBooleanCutPanel;
        // Legacy serialized fields retained for window-state compatibility.
        // Strictness is internal: the kernel heals inputs, then certifies that
        // the result is safe before the editor commits it.
        public bool requireClosedBooleanTarget = true;
        public bool requireClosedBooleanCutter = true;
        public bool showBooleanAdvanced;
        public float booleanTolerance = 0.00001f;
        public int booleanTriangleLimit = 25000;
        public RealBlendBooleanResourceBudget booleanResourceBudget =
            RealBlendBooleanResourceBudget.Expanded;
        [SerializeField] private int booleanSettingsVersion;

        [System.NonSerialized] private string booleanStatus;
        [System.NonSerialized] private MessageType booleanStatusType = MessageType.None;
        [System.NonSerialized] private GameObject booleanLastAutoSelection;
        [System.NonSerialized] private int booleanLastAutoRejectedCandidates;
        [System.NonSerialized] private List<CachedBooleanProbe> booleanProbeCache;
        [System.NonSerialized] private int booleanCachedProbeReuseCount;

        private sealed class PreparedBooleanCut
        {
            public MeshFilter targetFilter;
            public MeshRenderer targetRenderer;
            public Mesh previousMesh;
            public Mesh result;
            public int cutSubmesh;
            public RealBlendMeshBooleanReport report;
        }

        private sealed class CachedBooleanProbe
        {
            public MeshFilter targetFilter;
            public MeshFilter cutterFilter;
            public Mesh targetMesh;
            public Mesh cutterMesh;
            public Matrix4x4 targetMatrix;
            public Matrix4x4 cutterMatrix;
            public ulong targetFingerprint;
            public ulong cutterFingerprint;
            public float tolerance;
            public int triangleLimit;
            public RealBlendBooleanResourceBudget resourceBudget;
            public int cutSubmesh;
            public Color cutSurfaceColor;
            public Material cutSurfaceMaterial;
            public bool separateInteriorSubmesh;
            public bool leaveCutOpen;
            public Mesh result;
            public RealBlendMeshBooleanReport report;
        }

        private struct BooleanCommitState
        {
            public GameObject targetObject;
            public MeshFilter targetFilter;
            public VertexPaintStorage storage;
            public Mesh finalMesh;
        }

        private struct OrientedMeshBounds
        {
            public Vector3 center;
            public Vector3 axisX;
            public Vector3 axisY;
            public Vector3 axisZ;
            public Vector3 extents;
        }

        private struct SculptTriangleRecord
        {
            public int submesh;
            public int triangle;
            public int a;
            public int b;
            public int c;

            public SculptTriangleRecord(
                int submesh,
                int triangle,
                int a,
                int b,
                int c)
            {
                this.submesh = submesh;
                this.triangle = triangle;
                this.a = a;
                this.b = b;
                this.c = c;
            }
        }

        // --- State ---
        private GameObject currentSelection;
        private MeshFilter currentMeshFilter;
        private Mesh workingMesh;
        private VertexPaintStorage currentStorage;

        private Vector3[] currentVertices;
        private Vector3[] originalVertices;
        private bool meshIsDirty = true;

        private Dictionary<Vector3Int, List<int>> vertexBuckets;
        private Dictionary<Vector3Int, List<int>> triangleBuckets;
        private List<SculptTriangleRecord> triangleRecords;
        private List<int> largeTriangleRecords;
        // Unity restores this serializable tab without reliably running field
        // initializers. Keep reusable scratch storage explicitly transient and
        // recreate it lazily so entering Sculpt after a domain/window reload
        // cannot leave the Remove tool with null buffers.
        [System.NonSerialized] private HashSet<int> triangleCandidateScratch = new HashSet<int>();
        [System.NonSerialized] private HashSet<int> removeVertexCandidateScratch = new HashSet<int>();
        [System.NonSerialized] private HashSet<int> removeAffectedVertexScratch = new HashSet<int>();
        [System.NonSerialized] private List<Vector3> depthClipScratchA = new List<Vector3>(7);
        [System.NonSerialized] private List<Vector3> depthClipScratchB = new List<Vector3>(7);
        [System.NonSerialized] private List<Vector2> projectedTriangleScratch = new List<Vector2>(7);
        [System.NonSerialized] private string removeTopologyStatus;
        [System.NonSerialized] private MessageType removeTopologyStatusType = MessageType.None;
        private float bucketSize = 1.0f;
        private Mesh cachedSpatialMesh;
        private Matrix4x4 cachedSpatialLocalToWorld;

        [System.NonSerialized] private bool removeStrokeActive;
        [System.NonSerialized] private Mesh removeStrokeMesh;
        [System.NonSerialized] private MeshFilter removeStrokeMeshFilter;
        [System.NonSerialized] private VertexPaintStorage removeStrokeStorage;
        [System.NonSerialized] private int[][] removeStrokeSubmeshTriangles;
        [System.NonSerialized] private int[] removeStrokeSubmeshBaseVertices;
        [System.NonSerialized] private List<int>[] removeStrokeKeptTriangles;
        [System.NonSerialized] private int removeStrokeVertexCount;
        [System.NonSerialized] private Dictionary<int, HashSet<int>> removeStrokeTrianglesBySubmesh;
        [System.NonSerialized] private HashSet<int> removeStrokeSelectedVertices;
        [System.NonSerialized] private bool removeStrokeHasLastSample;
        [System.NonSerialized] private Vector3 removeStrokeLastPoint;
        [System.NonSerialized] private Vector3 removeStrokeLastNormal;
        [System.NonSerialized] private int removeStrokeUndoGroup = -1;
        [System.NonSerialized] private bool removeStrokeOwnsUndoGroup;
        [System.NonSerialized] private bool removeStrokeMutationPrepared;
        [System.NonSerialized] private bool removeStrokeGeometryApplied;
        [System.NonSerialized] private List<RealBlendSculptRemoveSample> removeStrokeSamples;
        [System.NonSerialized] private bool sculptMeshStateCommitPending;
        [System.NonSerialized] private int sculptMutationUndoGroup = -1;
        [System.NonSerialized] private bool sculptOwnsUndoGroup;

        private int cachedTriCount = 0;
        private int cachedVertCount = 0;

        public VertexPainter_SculptTab(EditorWindow owner)
        {
            RebindOwner(owner);
        }

        public void RebindOwner(EditorWindow owner)
        {
            _owner = owner;
        }

        public void OnEnable()
        {
            // This tab is nested serialized state on an EditorWindow. Unity can
            // recreate it without running field initializers, which leaves the
            // transient Undo group IDs at 0 instead of the -1 sentinel. Never
            // let restored/default interaction state reach CollapseUndoOperations.
            ResetTransientSculptInteractionState();
            meshIsDirty = true;
            EnsureRemoveTopologySettings();
            EnsureSculptScratchBuffers();
            EnsureBooleanProbeCache();
            RealBlendProBuilderColorBridge.AddMeshModifiedListener(
                OnExternalMeshModified);
        }

        void EnsureSculptScratchBuffers()
        {
            if (triangleCandidateScratch == null)
                triangleCandidateScratch = new HashSet<int>();
            if (removeVertexCandidateScratch == null)
                removeVertexCandidateScratch = new HashSet<int>();
            if (removeAffectedVertexScratch == null)
                removeAffectedVertexScratch = new HashSet<int>();
            if (depthClipScratchA == null)
                depthClipScratchA = new List<Vector3>(7);
            if (depthClipScratchB == null)
                depthClipScratchB = new List<Vector3>(7);
            if (projectedTriangleScratch == null)
                projectedTriangleScratch = new List<Vector2>(7);
        }

        void EnsureRemoveTopologySettings()
        {
            if (removeTopologySettingsVersion < 1)
            {
                // Version zero includes windows serialized while these controls
                // were hidden and permanently false. The completed behaviours are
                // safe-by-validation and should be on for both existing and new
                // users, while still remaining individually switchable.
                shapeCircularRemoveBoundary = true;
                removeThroughWall = true;
                circularRemoveSegments = Mathf.Clamp(
                    circularRemoveSegments,
                    8,
                    64);
                removeTopologySettingsVersion = 1;
            }
            if (removeTopologySettingsVersion < 2)
            {
                // Dedicated reveal submeshes alter the material-slot layout,
                // so keep this new feature opt-in for existing projects.
                removeTopologySettingsVersion = 2;
            }
            if (booleanSettingsVersion < 1)
            {
                // Keep legacy material routing until the customer explicitly
                // enables the dedicated interior-submesh option.
                booleanResourceBudget =
                    RealBlendBooleanResourceBudget.Expanded;
                booleanSettingsVersion = 1;
            }
        }

        public void OnDisable()
        {
            CommitRemoveStroke();
            FinalizePendingSculptStroke();
            ResetElementHandleState();
            RealBlendProBuilderColorBridge.RemoveMeshModifiedListener(
                OnExternalMeshModified);
            DestroyBooleanProbeCache();
        }

        private void OnExternalMeshModified(MeshFilter meshFilter)
        {
            if (meshFilter == null || meshFilter != currentMeshFilter)
                return;
            CommitRemoveStroke();
            FinalizePendingSculptStroke();
            ResetElementHandleState();
            ClearElementSelection();
            workingMesh = meshFilter.sharedMesh;
            currentVertices = workingMesh != null ? workingMesh.vertices : null;
            meshIsDirty = true;
            _owner?.Repaint();
        }

        public void ReleaseTransientBooleanResults()
        {
            CommitRemoveStroke();
            FinalizePendingSculptStroke();
            DestroyBooleanProbeCache();
        }

        public void OnSelectionChange()
        {
            if (Selection.activeGameObject != currentSelection)
            {
                CommitRemoveStroke();
                FinalizePendingSculptStroke();
                ClearCurrentMeshState();
            }
        }

        public void OnReload()
        {
            // OnBeforeAssemblyReload commits live gestures on the old instance.
            // The restored instance contains no trustworthy cross-event Undo
            // ownership, so abandon its transient fields instead of finalizing
            // a default/stale group ID into Unity's current Undo history.
            ResetTransientSculptInteractionState();
            DestroyBooleanProbeCache();
            ClearCurrentMeshState();
            booleanLastAutoSelection = null;
            NormalizeFlattenAxisFromLegacyValue();
            // Domain reloads also occur when leaving Play Mode. Rebuilding the
            // complete vertex/triangle spatial cache here stalls the editor on
            // high-poly selections even when Sculpt is not the active tab.
            // OnGUI/OnSceneGUI validate lazily as soon as Sculpt is actually
            // displayed or used.
        }

        void ResetTransientSculptInteractionState()
        {
            ResetRemoveStrokeState();
            sculptMeshStateCommitPending = false;
            sculptMutationUndoGroup = -1;
            sculptOwnsUndoGroup = false;
            nudgeStrokeActive = false;
        }

        public void OnUndoRedo()
        {
            // Undo already restored the authoritative mesh/storage state. Do
            // not let a half-finished editor gesture write the pre-Undo state
            // back on the next MouseUp or tab change.
            ResetRemoveStrokeState();
            sculptMeshStateCommitPending = false;
            sculptMutationUndoGroup = -1;
            sculptOwnsUndoGroup = false;
            ResetElementHandleState();
            ClearElementHover();
            DestroyBooleanProbeCache();
            if (currentMode == SculptMode.Boolean_Cut)
            {
                _owner?.Repaint();
                SceneView.RepaintAll();
                return;
            }

            ValidateMeshState();
            if (currentMeshFilter != null)
            {
                workingMesh = currentMeshFilter.sharedMesh;
                if (workingMesh == null)
                {
                    currentVertices = null;
                    meshIsDirty = true;
                    _owner?.Repaint();
                    return;
                }

                currentVertices = workingMesh.vertices;
                // Mesh topology Undo can restore the managed Mesh state before
                // the renderer and physics scene invalidate their native copy.
                // Push it immediately so Ctrl+Z does not depend on a later mouse
                // move/Scene GUI event to make the restored geometry visible.
                workingMesh.MarkModified();
                workingMesh.UploadMeshData(false);
                RefreshSculptMeshColliders(currentMeshFilter, workingMesh, null);
                meshIsDirty = true;
                UpdateStats();
                EditorApplication.QueuePlayerLoopUpdate();
                _owner?.Repaint();
                SceneView.RepaintAll();
            }
        }

        // --- GUI ---

        public void OnGUI()
        {
            NormalizeFlattenAxisFromLegacyValue();
            EnsureRemoveTopologySettings();

            GUILayout.Label("🗿 Mesh Sculpting", EditorStyles.boldLabel);

            string[] tabs = new string[]
            {
                "Pull (Move)",
                "Push",
                "Smooth",
                "Revert Pose",
                "Refine (Add Detail)",
                "Remove",
                "Nudge (Drag)",
                "Flatten Height",
                "Edit Elements",
                "Boolean Cut"
            };
            float availableWidth = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 28f);
            int tabColumns = Mathf.Clamp(Mathf.FloorToInt(availableWidth / 145f), 1, 4);
            GUIStyle sculptModeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fixedHeight = 34f
            };
            SculptMode previousMode = currentMode;
            currentMode = (SculptMode)GUILayout.SelectionGrid((int)currentMode, tabs, tabColumns, sculptModeStyle);
            if (previousMode != currentMode)
            {
                if (previousMode == SculptMode.Remove)
                    CommitRemoveStroke();
                else
                    FinalizePendingSculptStroke();
                ResetElementHandleState();
                ClearElementSelection();
            }
            if (previousMode == SculptMode.Boolean_Cut &&
                currentMode != SculptMode.Boolean_Cut)
            {
                DestroyBooleanProbeCache();
            }

            if (currentMode == SculptMode.Boolean_Cut)
            {
                DrawBooleanCutPanel();
                return;
            }

            ValidateMeshState();

            if (currentMeshFilter != null &&
                RealBlendProBuilderColorBridge.HasProBuilderMesh(currentMeshFilter))
            {
                EditorGUILayout.HelpBox(
                    "ProBuilder owns this object's topology. Vertex painting can be shared safely between RealBlend and ProBuilder, but RealBlend Sculpt, Remove, Refine, and element transforms are disabled until you finalize or remove the ProBuilder component.",
                    MessageType.Warning);
                return;
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);

            if (brushShape == BrushShape.Circle)
            {
                brushSize = EditorGUILayout.Slider(
                    new GUIContent(
                        "Size [ ]",
                        "Use [ and ], or Ctrl/Cmd + mouse wheel in the Scene view."),
                    brushSize,
                    0.1f,
                    10f);
            }

            if (currentMode == SculptMode.Refine_Add)
            {
                EditorGUILayout.HelpBox("Refine: Splits edges (1 -> 4). Creates cleaner 'square-like' topology than standard subdivision.", MessageType.Info);
                EditorGUILayout.HelpBox("Warning: Refine changes topology. In refined regions, Revert Pose cannot fully recover the original vertex layout/context.", MessageType.Warning);
            }
            else if (currentMode == SculptMode.Remove)
            {
                EditorGUILayout.HelpBox(
                    "Remove deletes existing front-facing faces. Square includes every face the brush touches. Circle can round the resulting rim by moving existing boundary vertices only; it does not add topology. Boolean Cut remains the precise arbitrary-object cutter.",
                    MessageType.Info);
            }
            else if (currentMode == SculptMode.Nudge)
            {
                EditorGUILayout.HelpBox("Nudge: Click and drag to slide surface points along the hit plane.", MessageType.Info);
                brushStrength = EditorGUILayout.Slider(
                    new GUIContent(
                        "Strength",
                        "Ctrl/Cmd + Shift + mouse wheel adjusts strength in the Scene view."),
                    brushStrength,
                    0.1f,
                    5.0f);
            }
            else if (currentMode == SculptMode.Flatten_Height)
            {
                EditorGUILayout.HelpBox("Flatten Height: Pulls vertices toward a selected local-space axis height.", MessageType.Info);
                brushStrength = EditorGUILayout.Slider(
                    new GUIContent(
                        "Strength",
                        "Ctrl/Cmd + Shift + mouse wheel adjusts strength in the Scene view."),
                    brushStrength,
                    0.1f,
                    5.0f);
                flattenLocalAxis = (LocalFlattenAxis)EditorGUILayout.EnumPopup("Local Axis", flattenLocalAxis);
                autoSampleFlattenHeight = EditorGUILayout.Toggle("Sample Height On Click", autoSampleFlattenHeight);
                flattenHeight = EditorGUILayout.FloatField("Target Height (Local Axis)", flattenHeight);
                flattenHeightOffset = EditorGUILayout.FloatField("Offset (+/-)", flattenHeightOffset);
                EditorGUILayout.LabelField("Effective Target", GetEffectiveFlattenHeight().ToString("0.###"));
            }
            else if (currentMode == SculptMode.Select_Vertex)
            {
                EditorGUILayout.HelpBox(
                    "Edit Elements: pick a vertex, edge, or face, then transform it. Coincident seam vertices move together to prevent cracks.",
                    MessageType.Info);
                EditorGUI.BeginChangeCheck();
                ElementSelectionMode newElementMode =
                    (ElementSelectionMode)EditorGUILayout.EnumPopup(
                        "Element",
                        elementSelectionMode);
                if (EditorGUI.EndChangeCheck())
                {
                    FinalizePendingSculptStroke();
                    elementSelectionMode = newElementMode;
                    ResetElementHandleState();
                    ClearElementSelection();
                    _owner?.Repaint();
                    SceneView.RepaintAll();
                }

                int activeTransform = Tools.current == Tool.Rotate
                    ? 1
                    : Tools.current == Tool.Scale
                        ? 2
                        : 0;
                EditorGUI.BeginChangeCheck();
                int newTransform = GUILayout.Toolbar(
                    activeTransform,
                    new[] { "Move", "Rotate", "Scale" },
                    EditorStyles.miniButton);
                if (EditorGUI.EndChangeCheck())
                {
                    FinalizePendingSculptStroke();
                    Tools.current = newTransform == 1
                        ? Tool.Rotate
                        : newTransform == 2
                            ? Tool.Scale
                            : Tool.Move;
                    ResetElementHandleState();
                    _owner?.Repaint();
                    SceneView.RepaintAll();
                }
            }
            else
            {
                brushStrength = EditorGUILayout.Slider(
                    new GUIContent(
                        "Strength",
                        "Ctrl/Cmd + Shift + mouse wheel adjusts strength in the Scene view."),
                    brushStrength,
                    0.1f,
                    5.0f);
            }

            brushShape = (BrushShape)EditorGUILayout.EnumPopup("Brush Shape", brushShape);
            if (brushShape == BrushShape.Square)
            {
                squareBrushWidth = EditorGUILayout.FloatField("Width", Mathf.Max(0.1f, squareBrushWidth));
                squareBrushHeight = EditorGUILayout.FloatField("Height", Mathf.Max(0.1f, squareBrushHeight));
            }

            if (currentMode == SculptMode.Remove)
            {
                if (brushShape == BrushShape.Circle)
                {
                    shapeCircularRemoveBoundary = EditorGUILayout.Toggle(
                        new GUIContent(
                            "Round Existing Rim",
                            "Moves the existing closed boundary loop toward the circular brush. No vertices or triangles are inserted."),
                        shapeCircularRemoveBoundary);
                }
                else
                {
                    removeThroughWall = EditorGUILayout.Toggle(
                        new GUIContent(
                            "Cut Through Solid Walls",
                            "On RealBlend Solid Walls, removes aligned front/back grid cells and joins the opening with reveal faces. Other meshes keep the normal surface Remove behaviour."),
                        removeThroughWall);
                    if (removeThroughWall)
                    {
                        removeUseSeparateInteriorSubmesh =
                            EditorGUILayout.Toggle(
                                new GUIContent(
                                    "Separate Interior Submesh",
                                    "Writes the new wall reveal faces into a dedicated submesh so they can use a different material. Surface-only and circle Remove do not create interior faces."),
                                removeUseSeparateInteriorSubmesh);
                        using (new EditorGUI.DisabledScope(
                                   !removeUseSeparateInteriorSubmesh))
                        {
                            removeInteriorMaterial =
                                (Material)EditorGUILayout.ObjectField(
                                    new GUIContent(
                                        "Interior Material",
                                        "Optional material for the dedicated reveal submesh. If empty, material slot 0 is copied so the wall initially keeps the same appearance."),
                                    removeInteriorMaterial,
                                    typeof(Material),
                                    false);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(removeTopologyStatus) &&
                    removeTopologyStatusType != MessageType.None)
                {
                    EditorGUILayout.HelpBox(
                        removeTopologyStatus,
                        removeTopologyStatusType);
                }
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Custom Brush", GUILayout.Width(100));
            brushAlpha = (Texture2D)EditorGUILayout.ObjectField(brushAlpha, typeof(Texture2D), false, GUILayout.Width(64), GUILayout.Height(64));
            GUILayout.EndHorizontal();

            if (brushAlpha != null)
            {
                brushRotation = EditorGUILayout.Slider("Rotation", brushRotation, 0f, 360f);
                invertBrushAlpha = EditorGUILayout.Toggle("Invert Alpha", invertBrushAlpha);
            }

            GUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Visuals", EditorStyles.boldLabel);
            showVertexGizmo = EditorGUILayout.Toggle("Show Vertices", showVertexGizmo);
            if (currentMode == SculptMode.Remove)
            {
                showTriangleGizmo = EditorGUILayout.Toggle("Show Triangles", showTriangleGizmo);
            }
            if (showVertexGizmo)
            {
                gizmoSize = EditorGUILayout.Slider("Dot Size", gizmoSize, 0.01f, 0.1f);
                gizmoColor = EditorGUILayout.ColorField("Dot Color", gizmoColor);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);
            EditorGUILayout.LabelField("Topology Management", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Revert Topology & Keep Paint", "Restores original triangles but projects current colors onto them.")))
            {
                RevertTopologyKeepPaint();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recalculate Normals") && workingMesh != null)
            {
                BeginPendingSculptMutation("Recalculate Normals");
                if (!EnsureSculptMeshEditable("Prepare Mesh for Normal Recalculation"))
                {
                    CancelEmptyPendingSculptMutation();
                    GUILayout.EndHorizontal();
                    return;
                }
                Undo.RecordObject(workingMesh, "Recalculate Normals");
                workingMesh.RecalculateNormals();
                workingMesh.RecalculateBounds();
                sculptMeshStateCommitPending = true;
                FinalizePendingSculptStroke();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (workingMesh != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("📊 Mesh Statistics", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Vertices:", $"{cachedVertCount}");
                EditorGUILayout.LabelField("Triangles:", $"{cachedTriCount}");
                EditorGUILayout.EndVertical();
            }

        }

        void UpdateStats()
        {
            if (workingMesh != null)
            {
                cachedVertCount = workingMesh.vertexCount;
                cachedTriCount = CountTriangles(workingMesh);
            }
        }

        // --- Scene Logic ---

        public void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            NormalizeFlattenAxisFromLegacyValue();
            if (e.type == EventType.MouseLeaveWindow)
            {
                CommitRemoveStroke();
                FinalizePendingSculptStroke();
                ResetElementHandleState();
                ClearElementHover();
                nudgeStrokeActive = false;
                SceneView.RepaintAll();
                return;
            }
            if (currentMode == SculptMode.Boolean_Cut)
            {
                DrawBooleanCutPreview();
                return;
            }

            ValidateMeshState();

            bool leftMouseReleased =
                (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) &&
                e.button == 0;
            if (leftMouseReleased && removeStrokeActive)
            {
                CommitRemoveStroke();
                nudgeStrokeActive = false;
                e.Use();
                return;
            }

            // Finalize brush/element mutations before any focus, selection, or
            // target-validation early return can swallow MouseUp. This keeps a
            // gesture-owned Undo group from surviving into unrelated editor work.
            if (leftMouseReleased && sculptMeshStateCommitPending)
            {
                nudgeStrokeActive = false;
                FinalizePendingSculptStroke();
            }

            if (EditorWindow.focusedWindow != _owner && EditorWindow.focusedWindow != sceneView)
            {
                FinalizePendingSculptStroke();
                return;
            }
            if (currentSelection != Selection.activeGameObject)
            {
                FinalizePendingSculptStroke();
                return;
            }
            if (currentSelection == null || currentMeshFilter == null || workingMesh == null)
            {
                FinalizePendingSculptStroke();
                return;
            }

            EnsureSpatialBucketsCurrent();

            if (HandleBrushWheelShortcut(e))
                return;

            // --- OPTIMIZATION 1: Force Repaint on Mouse Move ---
            // This fixes the "Frozen Brush" issue where the gizmo lags behind the cursor
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                SceneView.RepaintAll();
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftBracket)
                {
                    if (brushShape == BrushShape.Circle) brushSize = Mathf.Max(0.1f, brushSize - 0.5f);
                    else { squareBrushWidth = Mathf.Max(0.1f, squareBrushWidth - 0.5f); squareBrushHeight = Mathf.Max(0.1f, squareBrushHeight - 0.5f); }
                    if (currentMeshFilter != null &&
                        workingMesh != null &&
                        (!removeStrokeActive || !removeStrokeGeometryApplied))
                    {
                        BuildSpatialBuckets();
                    }
                    e.Use();
                }
                if (e.keyCode == KeyCode.RightBracket)
                {
                    if (brushShape == BrushShape.Circle) brushSize += 0.5f;
                    else { squareBrushWidth += 0.5f; squareBrushHeight += 0.5f; }
                    if (currentMeshFilter != null &&
                        workingMesh != null &&
                        (!removeStrokeActive || !removeStrokeGeometryApplied))
                    {
                        BuildSpatialBuckets();
                    }
                    e.Use();
                }
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (currentMode == SculptMode.Select_Vertex)
            {
                DrawAndEditSelectedElementHandle();
                int elementControlID = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(elementControlID);
            }

            UnityEngine.SceneManagement.Scene selectionScene = currentSelection.scene;
            if (!selectionScene.IsValid() || !selectionScene.isLoaded)
                return;

            if (selectionScene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out RaycastHit hit))
            {
                if (!IsColliderOwnedBySelection(hit.collider, currentSelection, currentMeshFilter))
                {
                    if (currentMode == SculptMode.Select_Vertex)
                    {
                        ClearElementHover();
                        if (leftMouseReleased)
                        {
                            FinalizePendingSculptStroke();
                            ResetElementHandleState();
                        }
                    }
                    return;
                }
                lastHitRayDirection = ray.direction;

                if (currentMode == SculptMode.Select_Vertex)
                {
                    if (GUIUtility.hotControl == 0 &&
                        TryPickElementAtHit(hit, elementSelectionMode, out int[] hoverAnchors))
                    {
                        SetElementHover(hoverAnchors);
                    }
                    else if (GUIUtility.hotControl == 0)
                    {
                        ClearElementHover();
                    }

                    if (e.type == EventType.Repaint && hoveredElementAnchors != null)
                    {
                        DrawElementGizmo(
                            hoveredElementAnchors,
                            elementSelectionMode,
                            new Color(1f, 0.88f, 0.15f, 0.95f),
                            false);
                    }

                    if (e.type == EventType.MouseDown &&
                        e.button == 0 &&
                        !e.alt &&
                        GUIUtility.hotControl == 0)
                    {
                        if (hoveredElementAnchors != null)
                        {
                            SelectElement(hoveredElementAnchors, elementSelectionMode);
                            e.Use();
                        }
                    }

                    if (leftMouseReleased)
                    {
                        nudgeStrokeActive = false;
                        FinalizePendingSculptStroke();
                        ResetElementHandleState();
                    }

                    return;
                }

                UnityEngine.Rendering.CompareFunction previousBrushZTest = Handles.zTest;
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Handles.color = new Color(1, 0.5f, 0, 0.5f);
                Vector3 brushDrawPoint = hit.point + hit.normal * 0.0025f;
                if (brushShape == BrushShape.Circle)
                {
                    Handles.DrawWireDisc(brushDrawPoint, hit.normal, brushSize);
                }
                else
                {
                    Matrix4x4 oldMatrix = Handles.matrix;
                    Vector3 safeBrushNormal = hit.normal.sqrMagnitude > 0.0000001f
                        ? hit.normal.normalized
                        : Vector3.up;
                    GetRemoveBrushBasis(
                        safeBrushNormal,
                        out _,
                        out Vector3 brushVisualUp);
                    Quaternion rot = Quaternion.LookRotation(
                        safeBrushNormal,
                        brushVisualUp);
                    Handles.matrix = Matrix4x4.TRS(brushDrawPoint, rot, Vector3.one);
                    Handles.DrawWireCube(Vector3.zero, new Vector3(squareBrushWidth, squareBrushHeight, 0));
                    Handles.matrix = oldMatrix;
                }
                Handles.zTest = previousBrushZTest;

                lastHitPoint = hit.point;
                lastHitNormal = hit.normal;

                // --- OPTIMIZATION 2: Only draw gizmos during Repaint ---
                if (e.type == EventType.Repaint)
                {
                    UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                    if (showVertexGizmo) DrawVertexGizmos(hit.point);
                    if (currentMode == SculptMode.Remove && showTriangleGizmo) DrawTriangleGizmos(hit.point);
                    Handles.zTest = previousZTest;
                }

                int controlID = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(controlID);

                if ((e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && e.button == 0 && !e.alt)
                {
                    if (currentMode == SculptMode.Refine_Add)
                    {
                        if (e.type == EventType.MouseDown)
                        {
                            RefineMesh_Midpoint(hit.point, hit.normal);
                            e.Use();
                        }
                    }
                    else if (currentMode == SculptMode.Remove)
                    {
                        if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                        {
                            if (!removeStrokeActive)
                                BeginRemoveStroke();
                            AccumulateRemoveStroke(hit.point, hit.normal);
                            e.Use();
                        }
                    }
                    else if (currentMode == SculptMode.Nudge)
                    {
                        if (e.type == EventType.MouseDown)
                        {
                            nudgeStrokeActive = true;
                            nudgeLastHitPoint = hit.point;
                            e.Use();
                        }
                        else
                        {
                            if (!nudgeStrokeActive)
                            {
                                nudgeStrokeActive = true;
                                nudgeLastHitPoint = hit.point;
                            }

                            Vector3 nudgeDelta = hit.point - nudgeLastHitPoint;
                            nudgeLastHitPoint = hit.point;
                            if (nudgeDelta.sqrMagnitude > 0.0000001f)
                            {
                                SculptMesh(hit.point, hit.normal, false, nudgeDelta);
                            }
                            e.Use();
                        }
                    }
                    else
                    {
                        if (currentMode == SculptMode.Flatten_Height && e.type == EventType.MouseDown && autoSampleFlattenHeight)
                        {
                            Vector3 localHit = currentMeshFilter.transform.InverseTransformPoint(hit.point);
                            flattenHeight = GetFlattenAxisValue(localHit);
                        }

                        // --- OPTIMIZATION 3a: Dragging ---
                        // Pass 'false' to skip RecalculateNormals while dragging.
                        SculptMesh(hit.point, hit.normal, false, Vector3.zero);
                        e.Use();
                    }
                }

                // --- OPTIMIZATION 3b: Mouse Up ---
                // Only recalculate normals once the user releases the mouse.
                if (e.type == EventType.MouseUp && e.button == 0 && !e.alt)
                {
                    nudgeStrokeActive = false;
                    if (currentMode != SculptMode.Refine_Add && currentMode != SculptMode.Remove)
                    {
                        FinalizePendingSculptStroke();
                    }
                }
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                nudgeStrokeActive = false;
                FinalizePendingSculptStroke();
                if (currentMode == SculptMode.Select_Vertex)
                {
                    ResetElementHandleState();
                    ClearElementHover();
                }
            }
            else if (currentMode == SculptMode.Select_Vertex)
            {
                ClearElementHover();
                if (leftMouseReleased)
                {
                    FinalizePendingSculptStroke();
                    ResetElementHandleState();
                }
            }

            if (currentMode == SculptMode.Select_Vertex && e.type == EventType.MouseDown && e.button == 0 && !e.alt && GUIUtility.hotControl == 0)
            {
                ClearElementSelection();
                _owner?.Repaint();
            }
        }

        private bool HandleBrushWheelShortcut(Event e)
        {
            if (e == null ||
                e.type != EventType.ScrollWheel ||
                e.alt ||
                (!e.control && !e.command))
            {
                return false;
            }

            float direction = -Mathf.Sign(e.delta.y);
            if (Mathf.Approximately(direction, 0f))
                return false;

            if (e.shift)
            {
                brushStrength = Mathf.Clamp(
                    brushStrength + direction * 0.1f,
                    0.1f,
                    5f);
            }
            else
            {
                float scale = Mathf.Pow(1.1f, direction);
                if (brushShape == BrushShape.Circle)
                {
                    brushSize = Mathf.Clamp(brushSize * scale, 0.1f, 10f);
                }
                else
                {
                    squareBrushWidth = Mathf.Clamp(squareBrushWidth * scale, 0.1f, 10f);
                    squareBrushHeight = Mathf.Clamp(squareBrushHeight * scale, 0.1f, 10f);
                }
            }

            _owner?.Repaint();
            SceneView.RepaintAll();
            e.Use();
            return true;
        }

        // --- Core Logic ---

        void DrawBooleanCutPanel()
        {
            GUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Keep legacy window state normalized to the safe internal
                // policy. There is intentionally no user-facing strict toggle.
                requireClosedBooleanTarget = true;
                requireClosedBooleanCutter = true;
                showBooleanCutPanel = true;
                EnsureBooleanTargetList();
                PruneBooleanProbeCache(false);
                AutoAssignBooleanSelection();

                EditorGUILayout.LabelField(
                    "Boolean Cut",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "One cutter can cut any scene mesh in the list. RealBlend prepares every result first, so a failed target leaves all meshes unchanged.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUI.BeginChangeCheck();
                GameObject nextCutter = (GameObject)EditorGUILayout.ObjectField(
                    "Cutter Mesh",
                    booleanCutter,
                    typeof(GameObject),
                    true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (nextCutter != booleanCutter)
                        DestroyBooleanProbeCache();
                    booleanCutter = nextCutter;
                    booleanStatus = string.Empty;
                }
                using (new EditorGUI.DisabledScope(
                           !IsBooleanSceneMesh(FindMeshFilter(
                               Selection.activeGameObject))))
                {
                    if (GUILayout.Button("Use Selected as Cutter"))
                    {
                        MeshFilter selected = FindMeshFilter(
                            Selection.activeGameObject);
                        if (selected != null)
                        {
                            DestroyBooleanProbeCache();
                            booleanTargets.RemoveAll(target =>
                                FindMeshFilter(target) == selected);
                            booleanCutter = Selection.activeGameObject;
                            SyncLegacyBooleanTarget();
                            booleanStatus = string.Empty;
                        }
                    }
                }

                GUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    $"Target Meshes ({booleanTargets.Count})",
                    EditorStyles.miniBoldLabel);
                for (int targetIndex = 0; targetIndex < booleanTargets.Count; targetIndex++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GameObject nextTarget = (GameObject)EditorGUILayout.ObjectField(
                            $"Target {targetIndex + 1}",
                            booleanTargets[targetIndex],
                            typeof(GameObject),
                            true);
                        if (nextTarget != booleanTargets[targetIndex])
                        {
                            DestroyBooleanProbeCache();
                            booleanTargets[targetIndex] = nextTarget;
                            booleanStatus = string.Empty;
                        }
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
                        {
                            DestroyBooleanProbeCache();
                            booleanTargets.RemoveAt(targetIndex);
                            targetIndex--;
                            booleanStatus = string.Empty;
                        }
                    }
                }
                SyncLegacyBooleanTarget();

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(
                               !IsBooleanSceneMesh(
                                   FindMeshFilter(Selection.activeGameObject))))
                    {
                        if (GUILayout.Button("Add Selected Target"))
                            AddBooleanTarget(Selection.activeGameObject);
                    }

                    using (new EditorGUI.DisabledScope(FindMeshFilter(booleanCutter) == null))
                    {
                        if (GUILayout.Button(new GUIContent(
                                "Auto Add Intersecting Targets",
                                "Finds nearby scene meshes, verifies that the cutter actually removes solid volume, and adds only targets that can produce a usable cut.")))
                        {
                            int added = AutoAddIntersectingBooleanTargets();
                            string rejectedSuffix =
                                booleanLastAutoRejectedCandidates > 0
                                    ? $" Ignored {booleanLastAutoRejectedCandidates} bounds candidate{(booleanLastAutoRejectedCandidates == 1 ? string.Empty : "s")} that would not produce a usable cut."
                                    : string.Empty;
                            booleanStatus = added > 0
                                ? $"Added {added} intersecting target{(added == 1 ? string.Empty : "s")}.{rejectedSuffix}"
                                : "No additional intersecting scene meshes were found." + rejectedSuffix;
                            booleanStatusType = added > 0
                                ? MessageType.Info
                                : MessageType.Warning;
                        }
                    }
                }

                if (booleanCutter != null && !booleanCutter.activeSelf)
                {
                    if (GUILayout.Button("Show Hidden Cutter"))
                    {
                        Undo.RecordObject(booleanCutter, "Show Boolean Cutter");
                        booleanCutter.SetActive(true);
                        EditorUtility.SetDirty(booleanCutter);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(booleanCutter);
                        if (booleanCutter.scene.IsValid() && booleanCutter.scene.isLoaded)
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                                booleanCutter.scene);
                        }
                    }
                }

                bool nextLeaveCutOpen = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Leave Cut Open",
                        "Removes only the new cutter-derived interior faces after the Boolean is validated, leaving an intentionally open boundary."),
                    booleanLeaveCutOpen);
                if (nextLeaveCutOpen != booleanLeaveCutOpen)
                {
                    DestroyBooleanProbeCache();
                    booleanLeaveCutOpen = nextLeaveCutOpen;
                    booleanStatus = string.Empty;
                }
                using (new EditorGUI.DisabledScope(booleanLeaveCutOpen))
                {
                    bool nextSeparateInteriorSubmesh =
                        EditorGUILayout.Toggle(
                            new GUIContent(
                                "Separate Interior Submesh",
                                "Writes all cutter-generated interior faces into a dedicated submesh. Disable this to retain the legacy material-slot reuse behaviour."),
                            booleanUseSeparateInteriorSubmesh);
                    Material nextCutSurfaceMaterial =
                        (Material)EditorGUILayout.ObjectField(
                            new GUIContent(
                                "Interior Material",
                                "Optional material for the Boolean interior. With a separate submesh and no material selected, slot 0 is copied so appearance stays unchanged until you assign one."),
                            booleanCutSurfaceMaterial,
                            typeof(Material),
                            false);
                    Color nextCutSurfaceColor = EditorGUILayout.ColorField(
                        new GUIContent(
                            "Cut Vertex Color",
                            "Vertex color assigned to the newly exposed interior surface."),
                        booleanCutSurfaceColor);
                    if (nextSeparateInteriorSubmesh !=
                            booleanUseSeparateInteriorSubmesh ||
                        nextCutSurfaceMaterial != booleanCutSurfaceMaterial ||
                        nextCutSurfaceColor != booleanCutSurfaceColor)
                    {
                        DestroyBooleanProbeCache();
                        booleanUseSeparateInteriorSubmesh =
                            nextSeparateInteriorSubmesh;
                        booleanCutSurfaceMaterial = nextCutSurfaceMaterial;
                        booleanCutSurfaceColor = nextCutSurfaceColor;
                    }
                }
                hideBooleanCutterAfterApply = EditorGUILayout.Toggle(
                    "Hide Cutter After Cut",
                    hideBooleanCutterAfterApply);

                showBooleanAdvanced = EditorGUILayout.Foldout(
                    showBooleanAdvanced,
                    "Advanced",
                    true);
                if (showBooleanAdvanced)
                {
                    EditorGUI.indentLevel++;
                    float nextTolerance = Mathf.Max(
                        0.000001f,
                        EditorGUILayout.FloatField("Seam Tolerance", booleanTolerance));
                    int nextTriangleLimit = Mathf.Clamp(
                        EditorGUILayout.IntField("Per-Mesh Triangle Limit", booleanTriangleLimit),
                        1,
                        100000);
                    RealBlendBooleanResourceBudget nextResourceBudget =
                        (RealBlendBooleanResourceBudget)EditorGUILayout.EnumPopup(
                            new GUIContent(
                                "Resource Budget",
                                "Standard: original 10-second limits. Expanded: 30 seconds and 1.5x bounded working memory. High: 60 seconds and 2x. All profiles retain hard allocation and output ceilings."),
                            booleanResourceBudget);
                    if (nextTolerance != booleanTolerance ||
                        nextTriangleLimit != booleanTriangleLimit ||
                        nextResourceBudget != booleanResourceBudget)
                    {
                        DestroyBooleanProbeCache();
                        booleanTolerance = nextTolerance;
                        booleanTriangleLimit = nextTriangleLimit;
                        booleanResourceBudget = nextResourceBudget;
                    }
                    EditorGUI.indentLevel--;
                }

                string validationMessage = GetBooleanBatchValidationMessage(
                    out List<MeshFilter> targetFilters,
                    out MeshFilter cutterFilter);
                bool canApply = string.IsNullOrEmpty(validationMessage);
                if (!canApply)
                {
                    EditorGUILayout.HelpBox(validationMessage, MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Ready to cut {targetFilters.Count} target{(targetFilters.Count == 1 ? string.Empty : "s")}. Original mesh assets will not be changed.",
                        MessageType.None);

                }

                using (new EditorGUI.DisabledScope(!canApply))
                {
                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.92f, 0.48f, 0.2f);
                    if (GUILayout.Button(
                            targetFilters.Count > 1
                                ? $"Apply Boolean Cut to {targetFilters.Count} Targets"
                                : "Apply Boolean Cut",
                            GUILayout.Height(32f)))
                    {
                        ApplyBooleanCut();
                    }
                    GUI.backgroundColor = previous;
                }

                if (!string.IsNullOrEmpty(booleanStatus))
                    EditorGUILayout.HelpBox(booleanStatus, booleanStatusType);
            }
        }

        void AutoAssignBooleanSelection()
        {
            EnsureBooleanTargetList();
            GameObject selectedObject = Selection.activeGameObject;
            if (selectedObject == booleanLastAutoSelection)
                return;

            booleanLastAutoSelection = selectedObject;
            MeshFilter selectedFilter = FindMeshFilter(selectedObject);
            if (selectedFilter == null || selectedFilter.sharedMesh == null)
                return;

            if (booleanTargets.Count == 0 && IsBooleanSceneMesh(selectedFilter))
            {
                AddBooleanTarget(selectedObject);
                if (FindMeshFilter(booleanCutter) == selectedFilter)
                {
                    DestroyBooleanProbeCache();
                    booleanCutter = null;
                }
                return;
            }

            // The first selected mesh becomes the target and the next distinct
            // scene mesh becomes the cutter. Further changes are explicit so
            // selecting unrelated scene geometry cannot grow the batch.
            if (booleanTargets.Count > 0 &&
                booleanCutter == null &&
                IsBooleanSceneMesh(selectedFilter) &&
                !ContainsBooleanTargetFilter(selectedFilter) &&
                FindMeshFilter(booleanCutter) != selectedFilter)
            {
                DestroyBooleanProbeCache();
                booleanCutter = selectedObject;
                booleanStatus = string.Empty;
            }
        }

        void EnsureBooleanTargetList()
        {
            if (booleanTargets == null)
                booleanTargets = new List<GameObject>();

            if (booleanTarget != null &&
                !booleanTargets.Any(target =>
                    FindMeshFilter(target) == FindMeshFilter(booleanTarget)))
            {
                booleanTargets.Insert(0, booleanTarget);
            }

            HashSet<MeshFilter> seen = new HashSet<MeshFilter>();
            for (int i = booleanTargets.Count - 1; i >= 0; i--)
            {
                MeshFilter filter = FindMeshFilter(booleanTargets[i]);
                if (filter == null || !seen.Add(filter))
                    booleanTargets.RemoveAt(i);
            }
            SyncLegacyBooleanTarget();
        }

        void SyncLegacyBooleanTarget()
        {
            booleanTarget = booleanTargets != null && booleanTargets.Count > 0
                ? booleanTargets[0]
                : null;
        }

        bool AddBooleanTarget(GameObject candidate)
        {
            MeshFilter filter = FindMeshFilter(candidate);
            if (!IsBooleanSceneMesh(filter) ||
                filter == FindMeshFilter(booleanCutter) ||
                ContainsBooleanTargetFilter(filter))
            {
                return false;
            }

            booleanTargets.Add(candidate);
            SyncLegacyBooleanTarget();
            booleanStatus = string.Empty;
            return true;
        }

        bool ContainsBooleanTargetFilter(MeshFilter filter)
        {
            if (filter == null || booleanTargets == null)
                return false;
            for (int i = 0; i < booleanTargets.Count; i++)
            {
                if (FindMeshFilter(booleanTargets[i]) == filter)
                    return true;
            }
            return false;
        }

        int AutoAddIntersectingBooleanTargets()
        {
            EnsureBooleanTargetList();
            PruneBooleanProbeCache(true);
            booleanLastAutoRejectedCandidates = 0;
            MeshFilter cutterFilter = FindMeshFilter(booleanCutter);
            if (cutterFilter == null || cutterFilter.sharedMesh == null ||
                !TryGetWorldMeshBounds(cutterFilter, out Bounds cutterBounds) ||
                !TryGetOrientedMeshBounds(
                    cutterFilter,
                    out OrientedMeshBounds orientedCutterBounds))
            {
                return 0;
            }
            ulong cutterFingerprint;
            try
            {
                cutterFingerprint = ComputeBooleanMeshFingerprint(
                    cutterFilter.sharedMesh);
            }
            catch (System.Exception)
            {
                return 0;
            }

            int added = 0;
            MeshFilter[] candidates = Resources.FindObjectsOfTypeAll<MeshFilter>();
            for (int i = 0; i < candidates.Length; i++)
            {
                MeshFilter candidate = candidates[i];
                if (candidate == null || candidate == cutterFilter ||
                    candidate.sharedMesh == null ||
                    !candidate.gameObject.activeInHierarchy ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded ||
                    candidate.gameObject.scene != cutterFilter.gameObject.scene ||
                    !IsBooleanSceneMesh(candidate) ||
                    ContainsBooleanTargetFilter(candidate) ||
                    candidate.transform.IsChildOf(cutterFilter.transform) ||
                    cutterFilter.transform.IsChildOf(candidate.transform) ||
                    !TryGetWorldMeshBounds(candidate, out Bounds targetBounds) ||
                    !BoundsHaveVolumeOverlap(
                        cutterBounds,
                        targetBounds,
                        Mathf.Max(0.000001f, booleanTolerance)) ||
                    !TryGetOrientedMeshBounds(
                        candidate,
                        out OrientedMeshBounds orientedTargetBounds) ||
                    !OrientedBoundsOverlap(
                        orientedCutterBounds,
                        orientedTargetBounds,
                        Mathf.Max(0.000001f, booleanTolerance)))
                {
                    continue;
                }

                // AABB/OBB overlap is only a broadphase. Concave walls and
                // meshes with loose imported bounds can still surround the
                // cutter without touching it. Run the same no-mutation solid
                // subtraction used by Apply and retain a transient verified
                // result so Apply does not repeat this expensive work.
                // This also prevents one unsuitable auto-discovered mesh from
                // aborting an otherwise valid atomic batch later.
                if (!string.IsNullOrEmpty(GetBooleanTargetValidationMessage(
                        candidate,
                        cutterFilter,
                        0)) ||
                    !TryCreateCachedBooleanProbe(
                        candidate,
                        cutterFilter,
                        cutterFingerprint,
                        out CachedBooleanProbe cachedProbe))
                {
                    booleanLastAutoRejectedCandidates++;
                    continue;
                }

                booleanTargets.Add(candidate.gameObject);
                StoreBooleanProbe(cachedProbe);
                added++;
            }

            if (added > 0)
                SyncLegacyBooleanTarget();
            return added;
        }

        bool TryCreateCachedBooleanProbe(
            MeshFilter targetFilter,
            MeshFilter cutterFilter,
            ulong cutterFingerprint,
            out CachedBooleanProbe cachedProbe)
        {
            cachedProbe = null;
            if (targetFilter == null || cutterFilter == null ||
                targetFilter.sharedMesh == null ||
                cutterFilter.sharedMesh == null)
            {
                return false;
            }

            Mesh probeResult = null;
            try
            {
                ulong targetFingerprint = ComputeBooleanMeshFingerprint(
                    targetFilter.sharedMesh);
                MeshRenderer targetRenderer =
                    targetFilter.GetComponent<MeshRenderer>();
                int cutSubmesh = ResolveBooleanCutSubmesh(
                    targetRenderer,
                    targetFilter.sharedMesh,
                    booleanCutSurfaceMaterial);
                RealBlendMeshBooleanSettings settings =
                    CreateBooleanSettings(cutSubmesh);
                bool succeeded = RealBlendMeshBoolean.TrySubtract(
                    targetFilter.sharedMesh,
                    targetFilter.transform.localToWorldMatrix,
                    cutterFilter.sharedMesh,
                    cutterFilter.transform.localToWorldMatrix,
                    settings,
                    out probeResult,
                    out RealBlendMeshBooleanReport report,
                    out _);
                bool usable = succeeded &&
                              probeResult != null &&
                              !report.targetCompletelyRemoved &&
                              (booleanLeaveCutOpen ||
                               report.generatedCutSurfaceTriangles > 0) &&
                              report.outputVertices > 0 &&
                              report.outputTriangles > 0;
                if (!usable)
                    return false;

                cachedProbe = new CachedBooleanProbe
                {
                    targetFilter = targetFilter,
                    cutterFilter = cutterFilter,
                    targetMesh = targetFilter.sharedMesh,
                    cutterMesh = cutterFilter.sharedMesh,
                    targetMatrix = targetFilter.transform.localToWorldMatrix,
                    cutterMatrix = cutterFilter.transform.localToWorldMatrix,
                    targetFingerprint = targetFingerprint,
                    cutterFingerprint = cutterFingerprint,
                    tolerance = booleanTolerance,
                    triangleLimit = booleanTriangleLimit,
                    resourceBudget = booleanResourceBudget,
                    cutSubmesh = cutSubmesh,
                    cutSurfaceColor = booleanCutSurfaceColor,
                    cutSurfaceMaterial = booleanCutSurfaceMaterial,
                    separateInteriorSubmesh =
                        booleanUseSeparateInteriorSubmesh,
                    leaveCutOpen = booleanLeaveCutOpen,
                    result = probeResult,
                    report = report
                };
                probeResult = null;
                return true;
            }
            catch (System.Exception)
            {
                // Auto discovery is advisory. Apply provides detailed errors
                // for manually chosen targets; candidates that cannot be
                // proven safe are simply not injected into the batch.
                return false;
            }
            finally
            {
                if (probeResult != null)
                    Object.DestroyImmediate(probeResult);
            }
        }

        void EnsureBooleanProbeCache()
        {
            if (booleanProbeCache == null)
                booleanProbeCache = new List<CachedBooleanProbe>();
        }

        void StoreBooleanProbe(CachedBooleanProbe probe)
        {
            if (probe == null || probe.result == null)
                return;

            EnsureBooleanProbeCache();
            for (int i = booleanProbeCache.Count - 1; i >= 0; i--)
            {
                CachedBooleanProbe existing = booleanProbeCache[i];
                if (existing == null ||
                    existing.targetFilter == probe.targetFilter)
                {
                    DestroyBooleanProbeAt(i);
                }
            }
            booleanProbeCache.Add(probe);
        }

        void DestroyBooleanProbeCache()
        {
            if (booleanProbeCache == null)
                return;
            for (int i = booleanProbeCache.Count - 1; i >= 0; i--)
                DestroyBooleanProbeAt(i);
            booleanProbeCache.Clear();
        }

        void DestroyBooleanProbeAt(int index)
        {
            if (booleanProbeCache == null ||
                index < 0 || index >= booleanProbeCache.Count)
            {
                return;
            }

            CachedBooleanProbe probe = booleanProbeCache[index];
            booleanProbeCache.RemoveAt(index);
            if (probe != null && probe.result != null)
            {
                Object.DestroyImmediate(probe.result);
                probe.result = null;
            }
        }

        void PruneBooleanProbeCache(bool verifyMeshContents)
        {
            if (booleanProbeCache == null)
                return;

            MeshFilter selectedCutter = FindMeshFilter(booleanCutter);
            ulong currentCutterFingerprint = 0UL;
            bool hasCurrentCutterFingerprint = false;
            if (verifyMeshContents &&
                selectedCutter != null &&
                selectedCutter.sharedMesh != null)
            {
                try
                {
                    currentCutterFingerprint =
                        ComputeBooleanMeshFingerprint(
                            selectedCutter.sharedMesh);
                    hasCurrentCutterFingerprint = true;
                }
                catch (System.Exception)
                {
                    hasCurrentCutterFingerprint = false;
                }
            }
            for (int i = booleanProbeCache.Count - 1; i >= 0; i--)
            {
                CachedBooleanProbe probe = booleanProbeCache[i];
                bool keep = probe != null &&
                            probe.result != null &&
                            probe.targetFilter != null &&
                            probe.cutterFilter != null &&
                            probe.cutterFilter == selectedCutter &&
                            ContainsBooleanTargetFilter(probe.targetFilter);
                if (keep && verifyMeshContents)
                {
                    keep = hasCurrentCutterFingerprint &&
                           BooleanProbeMatchesCurrentState(
                               probe,
                               currentCutterFingerprint);
                }
                if (!keep)
                    DestroyBooleanProbeAt(i);
            }
        }

        bool BooleanProbeMatchesCurrentState(
            CachedBooleanProbe probe,
            ulong currentCutterFingerprint)
        {
            if (probe == null ||
                probe.result == null ||
                probe.targetFilter == null ||
                probe.cutterFilter == null ||
                probe.targetMesh == null ||
                probe.cutterMesh == null ||
                probe.targetFilter.sharedMesh != probe.targetMesh ||
                probe.cutterFilter.sharedMesh != probe.cutterMesh ||
                !MatrixExactlyMatches(
                    probe.targetMatrix,
                    probe.targetFilter.transform.localToWorldMatrix) ||
                !MatrixExactlyMatches(
                    probe.cutterMatrix,
                    probe.cutterFilter.transform.localToWorldMatrix) ||
                probe.tolerance != booleanTolerance ||
                probe.triangleLimit != booleanTriangleLimit ||
                probe.resourceBudget != booleanResourceBudget ||
                probe.separateInteriorSubmesh !=
                    booleanUseSeparateInteriorSubmesh ||
                probe.leaveCutOpen != booleanLeaveCutOpen ||
                !probe.cutSurfaceColor.Equals(booleanCutSurfaceColor) ||
                !ReferenceEquals(
                    probe.cutSurfaceMaterial,
                    booleanCutSurfaceMaterial))
            {
                return false;
            }

            MeshRenderer renderer =
                probe.targetFilter.GetComponent<MeshRenderer>();
            if (ResolveBooleanCutSubmesh(
                    renderer,
                    probe.targetMesh,
                    booleanCutSurfaceMaterial) !=
                probe.cutSubmesh)
            {
                return false;
            }

            try
            {
                return probe.targetFingerprint ==
                           ComputeBooleanMeshFingerprint(probe.targetMesh) &&
                       probe.cutterFingerprint ==
                           currentCutterFingerprint;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        bool TryTakeCachedBooleanProbe(
            MeshFilter targetFilter,
            MeshFilter cutterFilter,
            out PreparedBooleanCut prepared)
        {
            prepared = null;
            if (booleanProbeCache == null)
                return false;

            for (int i = booleanProbeCache.Count - 1; i >= 0; i--)
            {
                CachedBooleanProbe probe = booleanProbeCache[i];
                if (probe.targetFilter != targetFilter ||
                    probe.cutterFilter != cutterFilter)
                {
                    continue;
                }

                booleanProbeCache.RemoveAt(i);
                prepared = new PreparedBooleanCut
                {
                    targetFilter = targetFilter,
                    targetRenderer =
                        targetFilter.GetComponent<MeshRenderer>(),
                    previousMesh = targetFilter.sharedMesh,
                    result = probe.result,
                    cutSubmesh = probe.cutSubmesh,
                    report = probe.report
                };
                probe.result = null;
                booleanCachedProbeReuseCount++;
                return true;
            }
            return false;
        }

        static bool MatrixExactlyMatches(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }
            return true;
        }

        static ulong ComputeBooleanMeshFingerprint(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable)
                throw new System.InvalidOperationException(
                    "A readable mesh is required for Boolean cache validation.");

            ulong hash = 1469598103934665603UL;
            HashBooleanValue(ref hash, mesh.vertexCount);
            HashBooleanValue(ref hash, mesh.subMeshCount);
            HashBooleanValue(ref hash, mesh.bounds.center);
            HashBooleanValue(ref hash, mesh.bounds.extents);

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                HashBooleanValue(ref hash, vertices[i]);
            Vector3[] normals = mesh.normals;
            HashBooleanValue(ref hash, normals.Length);
            for (int i = 0; i < normals.Length; i++)
                HashBooleanValue(ref hash, normals[i]);
            Vector2[] uv0 = mesh.uv;
            HashBooleanValue(ref hash, uv0.Length);
            for (int i = 0; i < uv0.Length; i++)
                HashBooleanValue(ref hash, uv0[i]);
            List<Vector2> uv2 = new List<Vector2>();
            mesh.GetUVs(2, uv2);
            HashBooleanValue(ref hash, uv2.Count);
            for (int i = 0; i < uv2.Count; i++)
                HashBooleanValue(ref hash, uv2[i]);
            Color[] colors = mesh.colors;
            HashBooleanValue(ref hash, colors.Length);
            for (int i = 0; i < colors.Length; i++)
                HashBooleanValue(ref hash, colors[i]);

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                HashBooleanValue(ref hash, (int)mesh.GetTopology(submesh));
                int[] indices = mesh.GetTriangles(submesh, true);
                HashBooleanValue(ref hash, indices.Length);
                for (int i = 0; i < indices.Length; i++)
                    HashBooleanValue(ref hash, indices[i]);
            }
            return hash;
        }

        static void HashBooleanValue(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        static void HashBooleanValue(ref ulong hash, float value)
        {
            HashBooleanValue(ref hash, value.GetHashCode());
        }

        static void HashBooleanValue(ref ulong hash, Vector2 value)
        {
            HashBooleanValue(ref hash, value.x);
            HashBooleanValue(ref hash, value.y);
        }

        static void HashBooleanValue(ref ulong hash, Vector3 value)
        {
            HashBooleanValue(ref hash, value.x);
            HashBooleanValue(ref hash, value.y);
            HashBooleanValue(ref hash, value.z);
        }

        static void HashBooleanValue(ref ulong hash, Color value)
        {
            HashBooleanValue(ref hash, value.r);
            HashBooleanValue(ref hash, value.g);
            HashBooleanValue(ref hash, value.b);
            HashBooleanValue(ref hash, value.a);
        }

        static bool TryGetWorldMeshBounds(
            MeshFilter filter,
            out Bounds worldBounds)
        {
            worldBounds = default;
            if (filter == null || filter.sharedMesh == null)
                return false;

            worldBounds = RealBlendMeshBoolean.TransformBounds(
                filter.sharedMesh.bounds,
                filter.transform.localToWorldMatrix);
            return worldBounds.size.x > 0f &&
                   worldBounds.size.y > 0f &&
                   worldBounds.size.z > 0f;
        }

        static bool BoundsHaveVolumeOverlap(
            Bounds a,
            Bounds b,
            float tolerance)
        {
            Vector3 overlap = Vector3.Min(a.max, b.max) -
                              Vector3.Max(a.min, b.min);
            return overlap.x > tolerance &&
                   overlap.y > tolerance &&
                   overlap.z > tolerance;
        }

        static bool TryGetOrientedMeshBounds(
            MeshFilter filter,
            out OrientedMeshBounds orientedBounds)
        {
            orientedBounds = default;
            if (filter == null || filter.sharedMesh == null)
                return false;

            Bounds localBounds = filter.sharedMesh.bounds;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3 columnX = matrix.MultiplyVector(Vector3.right);
            Vector3 columnY = matrix.MultiplyVector(Vector3.up);
            Vector3 columnZ = matrix.MultiplyVector(Vector3.forward);
            float scaleX = columnX.magnitude;
            float scaleY = columnY.magnitude;
            float scaleZ = columnZ.magnitude;
            if (scaleX <= 0.0000001f ||
                scaleY <= 0.0000001f ||
                scaleZ <= 0.0000001f)
            {
                return false;
            }

            orientedBounds = new OrientedMeshBounds
            {
                center = matrix.MultiplyPoint3x4(localBounds.center),
                axisX = columnX / scaleX,
                axisY = columnY / scaleY,
                axisZ = columnZ / scaleZ,
                extents = new Vector3(
                    localBounds.extents.x * scaleX,
                    localBounds.extents.y * scaleY,
                    localBounds.extents.z * scaleZ)
            };
            return true;
        }

        static bool OrientedBoundsOverlap(
            OrientedMeshBounds a,
            OrientedMeshBounds b,
            float tolerance)
        {
            Vector3 centerDelta = b.center - a.center;
            Vector3[] axesA = { a.axisX, a.axisY, a.axisZ };
            Vector3[] axesB = { b.axisX, b.axisY, b.axisZ };

            for (int i = 0; i < 3; i++)
            {
                if (SeparatesOrientedBounds(
                        centerDelta,
                        axesA[i],
                        a,
                        b,
                        tolerance))
                    return false;
                if (SeparatesOrientedBounds(
                        centerDelta,
                        axesB[i],
                        a,
                        b,
                        tolerance))
                    return false;
            }

            for (int aAxis = 0; aAxis < 3; aAxis++)
            {
                for (int bAxis = 0; bAxis < 3; bAxis++)
                {
                    Vector3 crossAxis = Vector3.Cross(
                        axesA[aAxis],
                        axesB[bAxis]);
                    if (SeparatesOrientedBounds(
                            centerDelta,
                            crossAxis,
                            a,
                            b,
                            tolerance))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        static bool SeparatesOrientedBounds(
            Vector3 centerDelta,
            Vector3 axis,
            OrientedMeshBounds a,
            OrientedMeshBounds b,
            float tolerance)
        {
            float axisLength = axis.magnitude;
            if (axisLength <= 0.000001f)
                return false;
            axis /= axisLength;

            float radiusA =
                a.extents.x * Mathf.Abs(Vector3.Dot(axis, a.axisX)) +
                a.extents.y * Mathf.Abs(Vector3.Dot(axis, a.axisY)) +
                a.extents.z * Mathf.Abs(Vector3.Dot(axis, a.axisZ));
            float radiusB =
                b.extents.x * Mathf.Abs(Vector3.Dot(axis, b.axisX)) +
                b.extents.y * Mathf.Abs(Vector3.Dot(axis, b.axisY)) +
                b.extents.z * Mathf.Abs(Vector3.Dot(axis, b.axisZ));
            return Mathf.Abs(Vector3.Dot(centerDelta, axis)) >=
                   radiusA + radiusB - Mathf.Max(0f, tolerance);
        }

        static bool IsBooleanSceneMesh(MeshFilter filter)
        {
            if (filter == null ||
                filter.sharedMesh == null ||
                EditorUtility.IsPersistent(filter.gameObject) ||
                !filter.gameObject.scene.IsValid() ||
                !filter.gameObject.scene.isLoaded ||
                UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(
                    filter.gameObject.scene))
            {
                return false;
            }

            UnityEditor.SceneManagement.PrefabStage prefabStage =
                UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            return prefabStage == null || filter.gameObject.scene != prefabStage.scene;
        }

        string GetBooleanValidationMessage(
            out MeshFilter targetFilter,
            out MeshFilter cutterFilter)
        {
            EnsureBooleanTargetList();
            targetFilter = booleanTargets.Count > 0
                ? FindMeshFilter(booleanTargets[0])
                : null;
            cutterFilter = FindMeshFilter(booleanCutter);
            return GetBooleanTargetValidationMessage(
                targetFilter,
                cutterFilter,
                0);
        }

        string GetBooleanBatchValidationMessage(
            out List<MeshFilter> targetFilters,
            out MeshFilter cutterFilter)
        {
            EnsureBooleanTargetList();
            targetFilters = new List<MeshFilter>();
            cutterFilter = FindMeshFilter(booleanCutter);

            if (booleanTargets.Count == 0)
                return "Add at least one target scene mesh.";
            if (booleanCutter == null)
                return "Choose the rock or other mesh that will cut the target.";
            if (cutterFilter == null || cutterFilter.sharedMesh == null)
                return "The selected cutter does not contain a usable mesh.";
            if (!IsBooleanSceneMesh(cutterFilter))
                return "The cutter must be a mesh object in a loaded scene.";
            if (!booleanCutter.activeInHierarchy ||
                !cutterFilter.gameObject.activeInHierarchy)
            {
                return "The cutter is hidden. Re-enable it before applying another cut.";
            }
            if (!cutterFilter.sharedMesh.isReadable)
                return "The cutter mesh is not readable. Enable Read/Write in its import settings.";
            if (CountTriangles(cutterFilter.sharedMesh) > Mathf.Max(1, booleanTriangleLimit))
                return "The cutter exceeds the current cut budget. Raise Per-Mesh Triangle Limit under Advanced.";

            HashSet<MeshFilter> seen = new HashSet<MeshFilter>();
            for (int targetIndex = 0; targetIndex < booleanTargets.Count; targetIndex++)
            {
                MeshFilter targetFilter = FindMeshFilter(booleanTargets[targetIndex]);
                string error = GetBooleanTargetValidationMessage(
                    targetFilter,
                    cutterFilter,
                    targetIndex);
                if (!string.IsNullOrEmpty(error))
                    return error;
                if (!seen.Add(targetFilter))
                    return $"Target {targetIndex + 1} is already in the list.";
                targetFilters.Add(targetFilter);
            }

            return string.Empty;
        }

        string GetBooleanTargetValidationMessage(
            MeshFilter targetFilter,
            MeshFilter cutterFilter,
            int targetIndex)
        {
            string prefix = booleanTargets != null && booleanTargets.Count > 1
                ? $"Target {targetIndex + 1}: "
                : string.Empty;
            if (targetFilter == null || targetFilter.sharedMesh == null)
                return prefix + "The selected target does not contain a usable mesh.";
            if (!IsBooleanSceneMesh(targetFilter))
                return prefix + "The target must be a mesh object in a loaded scene.";
            if (RealBlendProBuilderColorBridge.HasProBuilderMesh(targetFilter))
            {
                return prefix +
                       "ProBuilder owns this target's topology. Finalize or remove the ProBuilder component before applying a RealBlend Boolean Cut.";
            }
            if (cutterFilter == null || cutterFilter.sharedMesh == null)
                return "Choose a usable cutter mesh.";
            if (targetFilter == cutterFilter)
                return prefix + "Target and cutter must be different mesh objects.";
            if (cutterFilter.transform.IsChildOf(targetFilter.transform) ||
                targetFilter.transform.IsChildOf(cutterFilter.transform))
                return prefix + "Target and cutter must be separate objects, not parented inside each other.";
            if (!targetFilter.sharedMesh.isReadable)
                return prefix + "The target mesh is not readable. Enable Read/Write in its import settings.";

            if (CountTriangles(targetFilter.sharedMesh) > Mathf.Max(1, booleanTriangleLimit))
                return prefix + "The target exceeds the current cut budget. Raise Per-Mesh Triangle Limit under Advanced.";

            Matrix4x4 cutterToTarget =
                targetFilter.transform.worldToLocalMatrix *
                cutterFilter.transform.localToWorldMatrix;
            Bounds targetBounds = targetFilter.sharedMesh.bounds;
            Bounds cutterBounds = RealBlendMeshBoolean.TransformBounds(
                cutterFilter.sharedMesh.bounds,
                cutterToTarget);
            Vector3 overlap = Vector3.Min(targetBounds.max, cutterBounds.max) -
                              Vector3.Max(targetBounds.min, cutterBounds.min);
            float tolerance = Mathf.Max(0.000001f, booleanTolerance);
            if (overlap.x <= tolerance || overlap.y <= tolerance || overlap.z <= tolerance)
                return prefix + "The cutter does not pass through this target mesh.";

            return string.Empty;
        }

        void ApplyBooleanCut()
        {
            booleanCachedProbeReuseCount = 0;
            string validationMessage = GetBooleanBatchValidationMessage(
                out List<MeshFilter> targetFilters,
                out MeshFilter cutterFilter);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                DestroyBooleanProbeCache();
                booleanStatus = validationMessage;
                booleanStatusType = MessageType.Error;
                return;
            }

            Mesh cutterMesh = cutterFilter.sharedMesh;
            List<PreparedBooleanCut> prepared = new List<PreparedBooleanCut>(
                targetFilters.Count);
            try
            {
                PruneBooleanProbeCache(true);
                for (int targetIndex = 0; targetIndex < targetFilters.Count; targetIndex++)
                {
                    MeshFilter targetFilter = targetFilters[targetIndex];
                    if (TryTakeCachedBooleanProbe(
                            targetFilter,
                            cutterFilter,
                            out PreparedBooleanCut cachedCut))
                    {
                        prepared.Add(cachedCut);
                        RealBlendMeshUVUtility.GenerateLightmapUVs(
                            cachedCut.result);
                        continue;
                    }

                    Mesh targetMesh = targetFilter.sharedMesh;
                    MeshRenderer targetRenderer = targetFilter.GetComponent<MeshRenderer>();
                    int cutSubmesh = ResolveBooleanCutSubmesh(
                        targetRenderer,
                        targetMesh,
                        booleanCutSurfaceMaterial);
                    RealBlendMeshBooleanSettings settings =
                        CreateBooleanSettings(cutSubmesh);

                    EditorUtility.DisplayProgressBar(
                        "RealBlend Boolean Cut",
                        $"Preparing {targetFilter.gameObject.name} ({targetIndex + 1}/{targetFilters.Count})...",
                        Mathf.Lerp(
                            0.05f,
                            0.72f,
                            (targetIndex + 0.5f) / targetFilters.Count));
                    bool succeeded = RealBlendMeshBoolean.TrySubtract(
                        targetMesh,
                        targetFilter.transform.localToWorldMatrix,
                        cutterMesh,
                        cutterFilter.transform.localToWorldMatrix,
                        settings,
                        out Mesh result,
                        out RealBlendMeshBooleanReport report,
                        out string error);
                    if (!succeeded)
                    {
                        Debug.LogError(
                            BuildBooleanFailureDiagnostic(
                                targetFilter,
                                cutterFilter,
                                settings,
                                report,
                                error),
                            targetFilter);
                        if (result != null)
                            Object.DestroyImmediate(result);
                        DestroyPreparedBooleanResults(prepared);
                        DestroyBooleanProbeCache();
                        booleanStatus = targetFilters.Count > 1
                            ? $"{targetFilter.gameObject.name}: {GetFriendlyBooleanError(error)} No targets were changed."
                            : GetFriendlyBooleanError(error);
                        booleanStatusType = MessageType.Error;
                        return;
                    }
                    if (report.targetCompletelyRemoved)
                    {
                        if (result != null)
                            Object.DestroyImmediate(result);
                        DestroyPreparedBooleanResults(prepared);
                        DestroyBooleanProbeCache();
                        booleanStatus = $"{targetFilter.gameObject.name} is completely inside the cutter. No targets were changed.";
                        booleanStatusType = MessageType.Warning;
                        return;
                    }

                    PreparedBooleanCut preparedCut = new PreparedBooleanCut
                    {
                        targetFilter = targetFilter,
                        targetRenderer = targetRenderer,
                        previousMesh = targetMesh,
                        result = result,
                        cutSubmesh = cutSubmesh,
                        report = report
                    };
                    prepared.Add(preparedCut);
                    result = null;
                    RealBlendMeshUVUtility.GenerateLightmapUVs(
                        preparedCut.result);
                }

                EditorUtility.DisplayProgressBar(
                    "RealBlend Boolean Cut",
                    $"Committing {prepared.Count} prepared cut{(prepared.Count == 1 ? string.Empty : "s")}...",
                    0.85f);
                CommitBooleanResults(prepared, cutterFilter);

                int totalCutTriangles = prepared.Sum(item =>
                    item.report.generatedCutSurfaceTriangles);
                if (booleanLeaveCutOpen)
                {
                    int componentDeletions = prepared.Count(item =>
                        item.report.generatedCutSurfaceTriangles == 0);
                    if (prepared.Count == 1)
                    {
                        booleanStatus = totalCutTriangles > 0
                            ? $"Cut complete. Removed {totalCutTriangles} generated interior triangle{(totalCutTriangles == 1 ? string.Empty : "s")}, so the new boundary is intentionally open. Undo restores the previous mesh."
                            : "Cut complete. A disconnected target component was removed; no interior fill surface was generated. Undo restores the previous mesh.";
                    }
                    else
                    {
                        booleanStatus =
                            $"Open cut complete on {prepared.Count} targets ({totalCutTriangles} generated interior triangles removed" +
                            (componentDeletions > 0
                                ? $", {componentDeletions} disconnected component deletion{(componentDeletions == 1 ? string.Empty : "s")}"
                                : string.Empty) +
                            "). One Undo restores the whole batch.";
                    }
                }
                else
                {
                    booleanStatus = prepared.Count == 1
                        ? "Cut complete. The new hole is sealed and ready to paint. Undo restores the previous mesh."
                        : $"Cut complete on {prepared.Count} targets ({totalCutTriangles} sealed cut-surface triangles). One Undo restores the whole batch.";
                }
                if (booleanCachedProbeReuseCount > 0)
                {
                    booleanStatus +=
                        $" Reused {booleanCachedProbeReuseCount} verified Auto Add result{(booleanCachedProbeReuseCount == 1 ? string.Empty : "s")}.";
                }
                booleanStatusType = MessageType.Info;
            }
            catch (System.Exception exception)
            {
                DestroyPreparedBooleanResults(prepared);
                Debug.LogException(
                    exception,
                    targetFilters.Count > 0
                        ? targetFilters[0]
                        : cutterFilter);
                booleanStatus =
                    "RealBlend hit an internal error while applying this cut. " +
                    "Nothing was changed, and the exact exception is in the Console.";
                booleanStatusType = MessageType.Error;
            }
            finally
            {
                DestroyBooleanProbeCache();
                EditorUtility.ClearProgressBar();
            }
        }

        static void DestroyPreparedBooleanResults(
            List<PreparedBooleanCut> prepared)
        {
            if (prepared == null)
                return;
            for (int i = 0; i < prepared.Count; i++)
            {
                Mesh result = prepared[i].result;
                if (result != null)
                    Object.DestroyImmediate(result);
                prepared[i].result = null;
            }
        }

        static string GetFriendlyBooleanError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return GetAutomaticBooleanRetryGuidance();

            string lower = error.ToLowerInvariant();
            string meshName = lower.StartsWith("target")
                ? "The target"
                : lower.StartsWith("cutter")
                    ? "The cutter"
                    : "One of the meshes";

            if (lower.Contains("could not produce a sealed result"))
                return GetAutomaticBooleanRetryGuidance();

            if (lower.Contains("fully contained") ||
                lower.Contains("no boundary and removed no solid volume"))
            {
                return "With Leave Cut Open, move the cutter through an outer target surface. A cutter fully enclosed inside the target would leave no visible opening.";
            }

            if (lower.Contains("closed solid") ||
                lower.Contains("open boundary") ||
                lower.Contains("non-manifold") ||
                lower.Contains("zero enclosed volume") ||
                lower.Contains("inward-facing"))
            {
                return GetAutomaticBooleanRetryGuidance();
            }

            if (lower.Contains("degenerate") ||
                lower.Contains("zero-area") ||
                lower.Contains("nan") ||
                lower.Contains("infinite") ||
                lower.Contains("invalid vertex"))
            {
                return GetAutomaticBooleanRetryGuidance();
            }

            if (lower.Contains("does not overlap") ||
                lower.Contains("do not intersect") ||
                lower.Contains("volumes do not intersect"))
            {
                return "Move the cutter farther into the target so it clearly passes through it.";
            }

            if (lower.Contains("one triangle received more intersection segments") ||
                lower.Contains("one affected triangle exceeded its finite-arrangement"))
            {
                return "One local face/cutter overlap is pathologically complex. The per-face safety ceiling is intentionally fixed; simplify or triangulate that area instead of increasing memory.";
            }

            if (lower.Contains("time budget") ||
                lower.Contains("safety budget") ||
                lower.Contains("candidate-pair budget") ||
                lower.Contains("fragment budget") ||
                lower.Contains("ray-test budget") ||
                lower.Contains("output budget") ||
                lower.Contains("processing budget"))
            {
                return "This cut reached its bounded work allowance. Choose a higher Resource Budget under Advanced; hard memory ceilings still remain in place.";
            }

            if (lower.Contains("safety limit") ||
                lower.Contains("above the safety limit"))
            {
                return "One operand exceeds the accepted input size. Raise Per-Mesh Triangle Limit under Advanced if the mesh is intentional.";
            }

            if (lower.Contains("readable"))
                return meshName + " needs Read/Write enabled in its mesh import settings.";

            if (lower.Contains("transform") || lower.Contains("non-zero scale"))
                return "The target and cutter need a non-zero scale before they can be cut.";

            return GetAutomaticBooleanRetryGuidance();
        }

        static string GetAutomaticBooleanRetryGuidance()
        {
            return "RealBlend exhausted its automatic recovery for this cut. Nothing was changed; the exact internal reason is in the Console.";
        }

        static string BuildBooleanFailureDiagnostic(
            MeshFilter targetFilter,
            MeshFilter cutterFilter,
            RealBlendMeshBooleanSettings settings,
            RealBlendMeshBooleanReport report,
            string error)
        {
            Mesh targetMesh = targetFilter != null ? targetFilter.sharedMesh : null;
            Mesh cutterMesh = cutterFilter != null ? cutterFilter.sharedMesh : null;
            return
                "RealBlend Boolean Cut exhausted automatic recovery.\n" +
                "Internal reason: " + (string.IsNullOrEmpty(error) ? "No kernel reason was returned." : error) + "\n" +
                "Target: " + DescribeBooleanOperand(targetFilter, targetMesh) + "\n" +
                "Cutter: " + DescribeBooleanOperand(cutterFilter, cutterMesh) + "\n" +
                "Settings: plane=" + settings.planeEpsilon.ToString("G9") +
                ", weld=" + settings.weldTolerance.ToString("G9") +
                ", triangle limit=" + settings.maxInputTriangles +
                ", resource budget=" + settings.resourceBudget + "\n" +
                "Leave cut open: " + settings.leaveCutOpen + "\n" +
                "Attempts: " + report.recoveryAttempts +
                ", used plane=" + report.usedPlaneEpsilon.ToString("G9") +
                ", used weld=" + report.usedWeldTolerance.ToString("G9") +
                ", used repair=" + report.usedRepairTolerance.ToString("G9") +
                ", repaired inputs=" + report.inputsAutomaticallyRepaired + "\n" +
                "Work: " + report.processingMilliseconds + " ms, " +
                report.broadphaseCandidatePairs + " broadphase pairs, " +
                report.intersectionSegments + " intersection segments\n" +
                "Output: " + report.outputVertices + " vertices, " +
                report.outputTriangles + " triangles, " +
                report.cutSurfaceTriangles + " retained cut triangles, " +
                report.generatedCutSurfaceTriangles + " generated cut triangles.";
        }

        static string DescribeBooleanOperand(MeshFilter filter, Mesh mesh)
        {
            if (filter == null)
                return "<missing MeshFilter>";
            string objectName = filter.gameObject != null
                ? filter.gameObject.name
                : "<destroyed object>";
            if (mesh == null)
                return objectName + " / <missing mesh>";
            return objectName + " / " + mesh.name + " / " +
                   mesh.vertexCount + " vertices / " +
                   CountTriangles(mesh) + " triangles / matrix " +
                   filter.transform.localToWorldMatrix;
        }

        RealBlendMeshBooleanSettings CreateBooleanSettings(int cutSubmesh)
        {
            return new RealBlendMeshBooleanSettings
            {
                planeEpsilon = Mathf.Max(0.000001f, booleanTolerance),
                weldTolerance = Mathf.Max(0.000001f, booleanTolerance),
                maxInputTriangles = Mathf.Max(1, booleanTriangleLimit),
                resourceBudget = booleanResourceBudget,
                cutSurfaceSubmesh = Mathf.Max(0, cutSubmesh),
                cutSurfaceColor = booleanCutSurfaceColor,
                leaveCutOpen = booleanLeaveCutOpen,
                requireClosedTarget = true,
                requireClosedCutter = true
            };
        }

        void CommitBooleanResult(
            MeshFilter targetFilter,
            MeshRenderer targetRenderer,
            Mesh previousMesh,
            Mesh result,
            int cutSubmesh)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("RealBlend Boolean Cut");
            try
            {
                BooleanCommitState state = CommitBooleanResultCore(
                    targetFilter,
                    targetRenderer,
                    previousMesh,
                    result,
                    cutSubmesh,
                    FindMeshFilter(booleanCutter));
                FinishBooleanCommit(
                    new List<BooleanCommitState> { state },
                    FindMeshFilter(booleanCutter));
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        void CommitBooleanResults(
            List<PreparedBooleanCut> prepared,
            MeshFilter cutterFilter)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(
                prepared.Count > 1
                    ? "RealBlend Boolean Cut Batch"
                    : "RealBlend Boolean Cut");

            List<BooleanCommitState> committed =
                new List<BooleanCommitState>(prepared.Count);
            try
            {
                for (int i = 0; i < prepared.Count; i++)
                {
                    PreparedBooleanCut item = prepared[i];
                    BooleanCommitState state = CommitBooleanResultCore(
                        item.targetFilter,
                        item.targetRenderer,
                        item.previousMesh,
                        item.result,
                        item.cutSubmesh,
                        cutterFilter);
                    committed.Add(state);
                    item.result = null;
                }

                FinishBooleanCommit(committed, cutterFilter);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        BooleanCommitState CommitBooleanResultCore(
            MeshFilter targetFilter,
            MeshRenderer targetRenderer,
            Mesh previousMesh,
            Mesh result,
            int cutSubmesh,
            MeshFilter cutterFilter)
        {
            GameObject targetObject = targetFilter.gameObject;
            GameObject targetRoot = ResolveBooleanTargetRoot(targetFilter);
            List<MeshCollider> relevantColliders = CollectMatchingMeshColliders(
                targetFilter,
                targetRoot,
                previousMesh);
            VertexPaintStorage storage =
                targetObject.GetComponent<VertexPaintStorage>();
            if (storage == null)
            {
                storage = Undo.AddComponent<VertexPaintStorage>(targetObject);
                Undo.RecordObject(storage, "RealBlend Boolean Cut Paint Data");
                storage.SetBaseTopology(previousMesh);
            }
            else
            {
                Undo.RecordObject(storage, "RealBlend Boolean Cut Paint Data");
            }
            storage.ValidateGeneratedInteriorSubmeshes(previousMesh);

            if (targetRenderer != null &&
                !booleanLeaveCutOpen &&
                (booleanUseSeparateInteriorSubmesh ||
                 booleanCutSurfaceMaterial != null))
            {
                Undo.RecordObject(targetRenderer, "RealBlend Boolean Cut Material");
                Material[] materials = targetRenderer.sharedMaterials;
                bool materialsChanged = false;
                if (cutSubmesh >= materials.Length)
                {
                    Material visualFallback = booleanCutSurfaceMaterial != null
                        ? booleanCutSurfaceMaterial
                        : materials.Length > 0
                            ? materials[0]
                            : null;
                    System.Array.Resize(ref materials, cutSubmesh + 1);
                    materials[cutSubmesh] = visualFallback;
                    materialsChanged = true;
                }
                else if (booleanCutSurfaceMaterial != null &&
                         materials[cutSubmesh] != booleanCutSurfaceMaterial)
                {
                    materials[cutSubmesh] = booleanCutSurfaceMaterial;
                    materialsChanged = true;
                }
                if (materialsChanged)
                    targetRenderer.sharedMaterials = materials;
            }

            result.name = targetObject.name + "_BooleanCut";
            Undo.RegisterCreatedObjectUndo(result, "RealBlend Boolean Cut Mesh");
            Undo.RecordObject(targetFilter, "RealBlend Boolean Cut Mesh");
            targetFilter.sharedMesh = result;
            Mesh finalMesh = result;

            for (int i = 0; i < relevantColliders.Count; i++)
            {
                MeshCollider collider = relevantColliders[i];
                if (collider == null)
                    continue;
                // RegisterCreatedObjectUndo flushes earlier diff records, so
                // capture the collider immediately before rebinding it.
                Undo.RecordObject(
                    collider,
                    "RealBlend Boolean Cut Collider");
                collider.sharedMesh = null;
                collider.sharedMesh = finalMesh;
                EditorUtility.SetDirty(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }

            // RegisterCreatedObjectUndo flushes outstanding diff-based records.
            // Record storage again afterwards so the controlled provenance
            // re-sign and final mesh snapshot participate in this same group.
            Undo.RecordObject(storage, "RealBlend Boolean Cut Paint Data");
            storage.CommitGeneratedInteriorSubmeshTopology(
                finalMesh,
                booleanUseSeparateInteriorSubmesh && !booleanLeaveCutOpen
                    ? cutSubmesh
                    : -1);
            storage.SetOwnedEditableMesh(finalMesh);
            storage.SaveCurrentState(finalMesh);
            EditorUtility.SetDirty(storage);
            PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
            EditorUtility.SetDirty(targetFilter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetFilter);
            if (targetRenderer != null)
            {
                EditorUtility.SetDirty(targetRenderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetRenderer);
            }
            EditorUtility.SetDirty(targetObject);
            if (targetObject.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    targetObject.scene);
            }

            return new BooleanCommitState
            {
                targetObject = targetObject,
                targetFilter = targetFilter,
                storage = storage,
                finalMesh = finalMesh
            };
        }

        GameObject ResolveBooleanTargetRoot(MeshFilter targetFilter)
        {
            if (targetFilter == null)
                return null;
            if (booleanTargets != null)
            {
                for (int i = 0; i < booleanTargets.Count; i++)
                {
                    GameObject candidate = booleanTargets[i];
                    if (candidate == null)
                        continue;
                    Transform candidateTransform = candidate.transform;
                    if (targetFilter.transform == candidateTransform ||
                        targetFilter.transform.IsChildOf(candidateTransform))
                    {
                        return candidate;
                    }
                }
            }
            return targetFilter.gameObject;
        }

        static List<MeshCollider> CollectMatchingMeshColliders(
            MeshFilter targetFilter,
            GameObject targetRoot,
            Mesh previousMesh)
        {
            List<MeshCollider> colliders = new List<MeshCollider>();
            if (targetFilter == null || previousMesh == null)
                return colliders;
            HashSet<MeshCollider> seen = new HashSet<MeshCollider>();

            Transform requestedRoot = targetRoot != null
                ? targetRoot.transform
                : targetFilter.transform;
            bool filterBelongsToRoot =
                targetFilter.transform == requestedRoot ||
                targetFilter.transform.IsChildOf(requestedRoot);
            Transform scanRoot = filterBelongsToRoot
                ? requestedRoot
                : targetFilter.transform;
            MeshCollider[] nested = scanRoot.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < nested.Length; i++)
            {
                MeshCollider collider = nested[i];
                MeshFilter nearestOwner = null;
                Transform ownerSearch = collider != null
                    ? collider.transform
                    : null;
                while (ownerSearch != null)
                {
                    nearestOwner = ownerSearch.GetComponent<MeshFilter>();
                    if (nearestOwner != null || ownerSearch == scanRoot)
                        break;
                    ownerSearch = ownerSearch.parent;
                }
                if (collider != null &&
                    collider.sharedMesh == previousMesh &&
                    (nearestOwner == null || nearestOwner == targetFilter) &&
                    seen.Add(collider))
                {
                    colliders.Add(collider);
                }
            }

            // A selected root can own the collider above the actual child
            // MeshFilter. Include that ancestor chain as well, while exact
            // shared-mesh matching and de-duplication keep the scan bounded.
            Transform current = targetFilter.transform;
            while (current != null)
            {
                MeshFilter ancestorOwner = current.GetComponent<MeshFilter>();
                if (current != targetFilter.transform &&
                    ancestorOwner != null &&
                    ancestorOwner != targetFilter)
                {
                    break;
                }
                MeshCollider[] currentColliders = current.GetComponents<MeshCollider>();
                for (int i = 0; i < currentColliders.Length; i++)
                {
                    MeshCollider collider = currentColliders[i];
                    if (collider != null &&
                        collider.sharedMesh == previousMesh &&
                        seen.Add(collider))
                    {
                        colliders.Add(collider);
                    }
                }
                current = current.parent;
            }
            return colliders;
        }

        void FinishBooleanCommit(
            List<BooleanCommitState> committed,
            MeshFilter cutterFilter)
        {
            if (hideBooleanCutterAfterApply &&
                booleanCutter != null &&
                booleanCutter.activeSelf)
            {
                Undo.RecordObject(booleanCutter, "Hide Boolean Cutter");
                booleanCutter.SetActive(false);
                EditorUtility.SetDirty(booleanCutter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(booleanCutter);
                if (booleanCutter.scene.IsValid() && booleanCutter.scene.isLoaded)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                        booleanCutter.scene);
                }
            }

            if (committed.Count > 0)
            {
                BooleanCommitState primary = committed[0];
                currentSelection = primary.targetObject;
                currentMeshFilter = primary.targetFilter;
                currentStorage = primary.storage;
                workingMesh = primary.finalMesh;
                currentVertices = primary.finalMesh.vertices;
                originalVertices = primary.storage.originalVertices;
                ResetElementHandleState();
                ClearElementSelection();
                meshIsDirty = true;
                Selection.activeGameObject = primary.targetObject;
                ValidateMeshState();
                BuildSpatialBuckets();
                UpdateStats();
            }

            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        int ResolveBooleanCutSubmesh(
            MeshRenderer renderer,
            Mesh targetMesh,
            Material cutMaterial)
        {
            if (booleanLeaveCutOpen)
                return 0;
            if (booleanUseSeparateInteriorSubmesh)
            {
                if (targetMesh == null)
                    return 0;
                Material[] separateMaterials = renderer != null
                    ? renderer.sharedMaterials
                    : System.Array.Empty<Material>();
                VertexPaintStorage storage = renderer != null
                    ? renderer.GetComponent<VertexPaintStorage>()
                    : null;
                int trackedCount = storage != null
                    ? storage.GeneratedInteriorSubmeshCount
                    : 0;
                for (int trackedIndex = trackedCount - 1;
                     trackedIndex >= 0;
                     trackedIndex--)
                {
                    if (!storage.TryGetGeneratedInteriorSubmeshAt(
                            targetMesh,
                            trackedIndex,
                            out int trackedSubmesh))
                    {
                        continue;
                    }
                    bool materialMatches = cutMaterial == null ||
                        trackedSubmesh < separateMaterials.Length &&
                        separateMaterials[trackedSubmesh] == cutMaterial;
                    if (materialMatches &&
                        targetMesh.GetTopology(trackedSubmesh) ==
                            UnityEngine.MeshTopology.Triangles)
                    {
                        return trackedSubmesh;
                    }
                }
                return Mathf.Max(0, targetMesh.subMeshCount);
            }
            if (cutMaterial == null)
                return 0;

            Material[] materials = renderer != null
                ? renderer.sharedMaterials
                : System.Array.Empty<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == cutMaterial)
                    return i;
            }
            int sourceSubmeshCount = targetMesh != null
                ? targetMesh.subMeshCount
                : 0;
            return Mathf.Max(materials.Length, sourceSubmeshCount);
        }

        static MeshFilter FindMeshFilter(GameObject root)
        {
            if (root == null)
                return null;
            MeshFilter direct = root.GetComponent<MeshFilter>();
            if (direct != null)
                return direct;

            MeshFilter[] childFilters =
                root.GetComponentsInChildren<MeshFilter>(true);

            // Imported environment prefabs commonly put a collision-ready,
            // low-detail mesh on the root and their render LODs on children.
            // Use the child backed by that collider mesh instead of silently
            // selecting LOD0, which is slower and often less Boolean-friendly.
            MeshCollider rootCollider = root.GetComponent<MeshCollider>();
            if (rootCollider != null && rootCollider.sharedMesh != null)
            {
                for (int filterIndex = 0;
                     filterIndex < childFilters.Length;
                     filterIndex++)
                {
                    MeshFilter filter = childFilters[filterIndex];
                    if (filter != null &&
                        filter.sharedMesh == rootCollider.sharedMesh)
                    {
                        return filter;
                    }
                }
            }

            LODGroup lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                for (int lodIndex = lods.Length - 1; lodIndex >= 0; lodIndex--)
                {
                    Renderer[] renderers = lods[lodIndex].renderers;
                    for (int rendererIndex = 0;
                         rendererIndex < renderers.Length;
                         rendererIndex++)
                    {
                        Renderer renderer = renderers[rendererIndex];
                        if (renderer == null)
                            continue;
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                            return filter;
                    }
                }
            }

            return childFilters.FirstOrDefault(
                filter => filter != null && filter.sharedMesh != null);
        }

        /// <summary>
        /// Resolves the mesh that ordinary Sculpt tools should edit. Boolean Cut
        /// deliberately prefers collision-friendly/low-detail meshes, whereas a
        /// brush must edit the mesh the artist can actually see. Keeping these
        /// policies separate prevents Remove gizmos from following a collision
        /// mesh or low LOD while another child mesh remains visibly unchanged.
        /// </summary>
        static MeshFilter FindSculptMeshFilter(GameObject root)
        {
            if (root == null)
                return null;

            MeshFilter direct = root.GetComponent<MeshFilter>();
            LODGroup lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                // LOD0 is the authored/rendered source users expect to sculpt.
                // Do not use Renderer.isVisible here: SceneView camera movement
                // can change it between layout and input events.
                LOD[] lods = lodGroup.GetLODs();
                for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    Renderer[] renderers = lods[lodIndex].renderers;
                    for (int rendererIndex = 0;
                         rendererIndex < renderers.Length;
                         rendererIndex++)
                    {
                        Renderer renderer = renderers[rendererIndex];
                        if (renderer == null ||
                            !renderer.enabled ||
                            !renderer.gameObject.activeInHierarchy)
                        {
                            continue;
                        }

                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                            return filter;
                    }
                }
            }

            if (direct != null && direct.sharedMesh != null)
            {
                Renderer directRenderer = direct.GetComponent<Renderer>();
                if (directRenderer != null &&
                    directRenderer.enabled &&
                    directRenderer.gameObject.activeInHierarchy)
                {
                    return direct;
                }
            }

            MeshFilter[] childFilters =
                root.GetComponentsInChildren<MeshFilter>(true);
            for (int filterIndex = 0;
                 filterIndex < childFilters.Length;
                 filterIndex++)
            {
                MeshFilter filter = childFilters[filterIndex];
                if (filter == null ||
                    filter.sharedMesh == null ||
                    !filter.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer != null && renderer.enabled)
                    return filter;
            }

            // A selected collision-only object may legitimately have no
            // Renderer. Use its direct mesh only after all visible children
            // have been considered.
            if (direct != null && direct.sharedMesh != null)
                return direct;

            return childFilters.FirstOrDefault(
                filter => filter != null && filter.sharedMesh != null);
        }

        static bool IsColliderOwnedBySelection(
            Collider collider,
            GameObject selection,
            MeshFilter meshFilter)
        {
            if (collider == null || selection == null || meshFilter == null)
                return false;

            Transform hitTransform = collider.transform;
            Transform selectionTransform = selection.transform;
            Transform meshTransform = meshFilter.transform;
            return hitTransform == selectionTransform ||
                   hitTransform.IsChildOf(selectionTransform) ||
                   hitTransform == meshTransform ||
                   hitTransform.IsChildOf(meshTransform);
        }

        static VertexPaintStorage FindVertexPaintStorage(
            GameObject selection,
            MeshFilter meshFilter)
        {
            if (meshFilter != null)
            {
                VertexPaintStorage meshStorage =
                    meshFilter.GetComponent<VertexPaintStorage>();
                if (meshStorage != null)
                    return meshStorage;
            }

            if (selection == null)
                return null;
            VertexPaintStorage direct = selection.GetComponent<VertexPaintStorage>();
            return direct != null
                ? direct
                : selection.GetComponentInChildren<VertexPaintStorage>(true);
        }

        static int CountTriangles(Mesh mesh)
        {
            if (mesh == null)
                return 0;
            long count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) == UnityEngine.MeshTopology.Triangles)
                    count += (long)mesh.GetIndexCount(submesh) / 3L;
            }
            return count > int.MaxValue ? int.MaxValue : (int)count;
        }

        void DrawBooleanCutPreview()
        {
            if (currentMode != SculptMode.Boolean_Cut ||
                booleanCutter == null ||
                !booleanCutter.activeInHierarchy)
            {
                return;
            }

            MeshFilter cutterFilter = FindMeshFilter(booleanCutter);
            if (cutterFilter == null ||
                cutterFilter.sharedMesh == null ||
                !cutterFilter.gameObject.activeInHierarchy)
                return;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.matrix = cutterFilter.transform.localToWorldMatrix;
            Handles.color = new Color(1f, 0.32f, 0.12f, 0.95f);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.DrawWireCube(
                cutterFilter.sharedMesh.bounds.center,
                cutterFilter.sharedMesh.bounds.size);

            EnsureBooleanTargetList();
            Handles.color = new Color(0.2f, 0.82f, 1f, 0.8f);
            for (int i = 0; i < booleanTargets.Count; i++)
            {
                MeshFilter targetFilter = FindMeshFilter(booleanTargets[i]);
                if (targetFilter == null || targetFilter.sharedMesh == null)
                    continue;
                Handles.matrix = targetFilter.transform.localToWorldMatrix;
                Handles.DrawWireCube(
                    targetFilter.sharedMesh.bounds.center,
                    targetFilter.sharedMesh.bounds.size);
            }
            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        void ValidateMeshState()
        {
            GameObject newSelection = Selection.activeGameObject;
            if (newSelection != currentSelection)
            {
                CommitRemoveStroke();
                FinalizePendingSculptStroke();
                currentSelection = newSelection;
                ResetElementHandleState();
                ClearElementSelection();
                meshIsDirty = true;
            }

            if (currentSelection == null)
            {
                ClearWorkingMeshState();
                return;
            }

            MeshFilter resolvedFilter = FindSculptMeshFilter(currentSelection);
            if (resolvedFilter == null || resolvedFilter.sharedMesh == null)
            {
                currentMeshFilter = null;
                currentStorage = null;
                ClearWorkingMeshState(false);
                return;
            }

            // Unity's destroyed-object null overload can report a destroyed
            // cached filter as equal to a real CLR null. Reference comparison
            // makes sure the stale wrapper is actually replaced.
            if (!ReferenceEquals(resolvedFilter, currentMeshFilter))
            {
                currentMeshFilter = resolvedFilter;
                meshIsDirty = true;
            }
            Mesh resolvedMesh = currentMeshFilter.sharedMesh;
            if (!resolvedMesh.isReadable)
            {
                // Imported cutters commonly disable Read/Write. Boolean mode
                // reports that through its own validation UI; ordinary sculpt
                // modes simply leave the working mesh uninitialized instead
                // of touching Mesh.vertices and throwing.
                ClearWorkingMeshState(false);
                return;
            }

            currentStorage = FindVertexPaintStorage(currentSelection, currentMeshFilter);
            if (workingMesh != resolvedMesh)
            {
                workingMesh = resolvedMesh;
                meshIsDirty = true;
            }

            if (meshIsDirty)
            {
                currentVertices = workingMesh.vertices;
                ValidateElementSelectionIndices();
                if (currentStorage != null && currentStorage.hasOriginalData && currentStorage.originalVertices != null)
                    originalVertices = currentStorage.originalVertices;
                else
                    originalVertices = currentVertices;

                BuildSpatialBuckets();
                UpdateStats();
                meshIsDirty = false;
            }
        }

        bool EnsureSculptMeshEditable(string undoName)
        {
            if (currentMeshFilter == null || currentMeshFilter.sharedMesh == null)
                return false;
            if (RealBlendProBuilderColorBridge.HasProBuilderMesh(currentMeshFilter))
                return false;

            // Ownership was already established for this active gesture.
            // Avoid the shared-mesh scene scan on every drag sample.
            if (sculptMeshStateCommitPending &&
                workingMesh == currentMeshFilter.sharedMesh)
            {
                return true;
            }

            currentStorage = FindVertexPaintStorage(currentSelection, currentMeshFilter);
            Mesh sourceMesh = currentMeshFilter.sharedMesh;
            GameObject editRoot = currentSelection != null &&
                                  (currentMeshFilter.transform == currentSelection.transform ||
                                   currentMeshFilter.transform.IsChildOf(currentSelection.transform))
                ? currentSelection
                : currentMeshFilter.gameObject;
            List<MeshCollider> sourceColliders = CollectMatchingMeshColliders(
                currentMeshFilter,
                editRoot,
                sourceMesh);
            Mesh editableMesh = RealBlendEditableMeshUtility.EnsureUniqueForEditing(
                currentMeshFilter,
                ref currentStorage,
                undoName,
                true,
                out bool cloned);
            if (editableMesh == null || !editableMesh.isReadable)
                return false;

            if (cloned)
            {
                // The shared ownership helper updates the first collider on the
                // MeshFilter object. Rebind any additional same-object or
                // matching ancestor colliders in the same Undo group as well.
                for (int i = 0; i < sourceColliders.Count; i++)
                {
                    MeshCollider sourceCollider = sourceColliders[i];
                    if (sourceCollider == null || sourceCollider.sharedMesh == editableMesh)
                        continue;
                    Undo.RecordObject(sourceCollider, undoName);
                    sourceCollider.sharedMesh = null;
                    sourceCollider.sharedMesh = editableMesh;
                    EditorUtility.SetDirty(sourceCollider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sourceCollider);
                }
            }

            if (workingMesh != editableMesh)
            {
                workingMesh = editableMesh;
                currentVertices = workingMesh.vertices;
                originalVertices = currentStorage != null &&
                                   currentStorage.hasOriginalData &&
                                   currentStorage.originalVertices != null
                    ? currentStorage.originalVertices
                    : currentVertices;
                meshIsDirty = true;
                BuildSpatialBuckets();
                UpdateStats();
                meshIsDirty = false;
            }

            return true;
        }

        void ClearCurrentMeshState()
        {
            currentSelection = null;
            currentMeshFilter = null;
            currentStorage = null;
            selectedVertexIndex = -1;
            hoveredVertexIndex = -1;
            ClearElementSelection();
            ClearWorkingMeshState();
        }

        void ClearWorkingMeshState(bool clearFilter = true)
        {
            ResetRemoveStrokeState();
            sculptMeshStateCommitPending = false;
            sculptMutationUndoGroup = -1;
            sculptOwnsUndoGroup = false;
            if (clearFilter)
            {
                currentMeshFilter = null;
                currentStorage = null;
            }
            workingMesh = null;
            currentVertices = null;
            originalVertices = null;
            vertexBuckets = null;
            triangleBuckets = null;
            triangleRecords = null;
            largeTriangleRecords = null;
            cachedSpatialMesh = null;
            cachedSpatialLocalToWorld = Matrix4x4.zero;
            cachedVertCount = 0;
            cachedTriCount = 0;
            selectedVertexIndex = -1;
            hoveredVertexIndex = -1;
            ResetElementHandleState();
            ClearElementSelection();
            meshIsDirty = true;
        }

        void BuildSpatialBuckets()
        {
            if (currentVertices == null || currentMeshFilter == null || workingMesh == null)
                return;
            vertexBuckets = new Dictionary<Vector3Int, List<int>>();
            triangleBuckets = new Dictionary<Vector3Int, List<int>>();
            triangleRecords = new List<SculptTriangleRecord>();
            largeTriangleRecords = new List<int>();
            float sizeForBucket = brushShape == BrushShape.Circle ? brushSize : Mathf.Max(squareBrushWidth, squareBrushHeight);
            bucketSize = Mathf.Clamp(sizeForBucket, 0.5f, 5.0f);

            Transform mt = currentMeshFilter.transform;
            cachedSpatialMesh = workingMesh;
            cachedSpatialLocalToWorld = mt.localToWorldMatrix;

            // Only index vertices that are still referenced by rendered mesh
            // topology. Remove intentionally changes index buffers without
            // compacting vertex streams, so this keeps orphaned vertices out of
            // Sculpt brushes and vertex gizmos without discarding UVs, tangents,
            // skinning, or any other vertex data.
            bool[] referencedVertices = new bool[currentVertices.Length];
            for (int submesh = 0; submesh < workingMesh.subMeshCount; submesh++)
            {
                int[] indices = workingMesh.GetIndices(submesh, true);
                for (int index = 0; index < indices.Length; index++)
                {
                    int vertex = indices[index];
                    if (vertex >= 0 && vertex < referencedVertices.Length)
                        referencedVertices[vertex] = true;
                }
            }

            // Build vertex buckets.
            for (int i = 0; i < currentVertices.Length; i++)
            {
                if (!referencedVertices[i])
                    continue;
                Vector3 worldPos = mt.TransformPoint(currentVertices[i]);
                Vector3Int key = GetBucketID(worldPos);
                if (!vertexBuckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    vertexBuckets[key] = list;
                }
                list.Add(i);
            }

            // Index each triangle by its complete world-space bounds rather than
            // only its centroid. Large triangles are kept in a small overflow
            // list so a fine bucket size cannot create an excessive number of
            // references.
            for (int submesh = 0; submesh < workingMesh.subMeshCount; submesh++)
            {
                if (workingMesh.GetTopology(submesh) != UnityEngine.MeshTopology.Triangles)
                    continue;

                int[] indices = workingMesh.GetTriangles(submesh, true);
                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    int a = indices[offset];
                    int b = indices[offset + 1];
                    int c = indices[offset + 2];
                    if (a < 0 || b < 0 || c < 0 ||
                        a >= currentVertices.Length ||
                        b >= currentVertices.Length ||
                        c >= currentVertices.Length)
                    {
                        continue;
                    }

                    int recordIndex = triangleRecords.Count;
                    triangleRecords.Add(new SculptTriangleRecord(
                        submesh,
                        offset / 3,
                        a,
                        b,
                        c));

                    Vector3 p1 = mt.TransformPoint(currentVertices[a]);
                    Vector3 p2 = mt.TransformPoint(currentVertices[b]);
                    Vector3 p3 = mt.TransformPoint(currentVertices[c]);
                    Vector3 min = Vector3.Min(p1, Vector3.Min(p2, p3));
                    Vector3 max = Vector3.Max(p1, Vector3.Max(p2, p3));
                    Vector3Int minID = GetBucketID(min);
                    Vector3Int maxID = GetBucketID(max);
                    long spanX = (long)maxID.x - minID.x + 1L;
                    long spanY = (long)maxID.y - minID.y + 1L;
                    long spanZ = (long)maxID.z - minID.z + 1L;
                    // Bound each factor before multiplying so malformed or
                    // extremely large mesh coordinates cannot overflow the
                    // bucket reference-count calculation.
                    if (spanX <= 0L || spanY <= 0L || spanZ <= 0L ||
                        spanX > 64L || spanY > 64L || spanZ > 64L ||
                        spanX * spanY * spanZ > 64L)
                    {
                        largeTriangleRecords.Add(recordIndex);
                        continue;
                    }

                    for (int x = minID.x; x <= maxID.x; x++)
                    {
                        for (int y = minID.y; y <= maxID.y; y++)
                        {
                            for (int z = minID.z; z <= maxID.z; z++)
                            {
                                Vector3Int key = new Vector3Int(x, y, z);
                                if (!triangleBuckets.TryGetValue(key, out List<int> list))
                                {
                                    list = new List<int>();
                                    triangleBuckets[key] = list;
                                }
                                list.Add(recordIndex);
                            }
                        }
                    }
                }
            }
        }

        void EnsureSpatialBucketsCurrent()
        {
            if (workingMesh == null || currentMeshFilter == null)
                return;

            // Live Remove indices refer to the immutable topology snapshot made
            // at MouseDown. Rebuilding from the already-reduced mesh would
            // renumber survivors and make later drag samples delete the wrong
            // original triangles. Rebuild once when the stroke commits instead.
            if (removeStrokeActive && removeStrokeGeometryApplied)
                return;

            float sizeForBucket = brushShape == BrushShape.Circle
                ? brushSize
                : Mathf.Max(squareBrushWidth, squareBrushHeight);
            float desiredBucketSize = Mathf.Clamp(sizeForBucket, 0.5f, 5.0f);
            if (vertexBuckets == null ||
                triangleBuckets == null ||
                triangleRecords == null ||
                cachedSpatialMesh != workingMesh ||
                cachedSpatialLocalToWorld != currentMeshFilter.transform.localToWorldMatrix ||
                !Mathf.Approximately(bucketSize, desiredBucketSize))
            {
                currentVertices = workingMesh.vertices;
                BuildSpatialBuckets();
            }
        }

        Vector3Int GetBucketID(Vector3 pos)
        {
            return new Vector3Int(Mathf.FloorToInt(pos.x / bucketSize), Mathf.FloorToInt(pos.y / bucketSize), Mathf.FloorToInt(pos.z / bucketSize));
        }

        float GetBrushAlphaValue(Vector3 worldPos, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (brushAlpha == null) return 1.0f;
            Vector3 brushRight = Vector3.Cross(hitNormal, Vector3.up);
            if (brushRight.sqrMagnitude < 0.001f) brushRight = Vector3.Cross(hitNormal, Vector3.right);
            brushRight.Normalize();
            Vector3 brushUp = Vector3.Cross(brushRight, hitNormal).normalized;

            Quaternion rot = Quaternion.AngleAxis(brushRotation, hitNormal);
            brushRight = rot * brushRight;
            brushUp = rot * brushUp;

            Vector3 delta = worldPos - hitPoint;
            float u = Vector3.Dot(delta, brushRight) / brushSize;
            float v = Vector3.Dot(delta, brushUp) / brushSize;
            float uvX = u * 0.5f + 0.5f;
            float uvY = v * 0.5f + 0.5f;

            if (uvX >= 0 && uvX <= 1 && uvY >= 0 && uvY <= 1)
            {
                float val = brushAlpha.GetPixelBilinear(uvX, uvY).r;
                return invertBrushAlpha ? 1.0f - val : val;
            }
            return 0f;
        }

        float GetBrushRangeWorld()
        {
            return brushShape == BrushShape.Circle ? brushSize : Mathf.Max(squareBrushWidth, squareBrushHeight) * 0.5f;
        }

        float GetSurfaceDepthToleranceWorld()
        {
            // Keep operations on the currently hovered side of the mesh.
            return Mathf.Max(0.0025f, GetBrushRangeWorld() * 0.1f);
        }

        float GetEffectiveFlattenHeight()
        {
            return flattenHeight + flattenHeightOffset;
        }

        void NormalizeFlattenAxisFromLegacyValue()
        {
            int axisValue = (int)flattenLocalAxis;
            switch (axisValue)
            {
                case 0:
                case 1:
                    flattenLocalAxis = LocalFlattenAxis.X;
                    break;
                case 2:
                case 3:
                    flattenLocalAxis = LocalFlattenAxis.Y;
                    break;
                case 4:
                case 5:
                    flattenLocalAxis = LocalFlattenAxis.Z;
                    break;
                default:
                    flattenLocalAxis = LocalFlattenAxis.Y;
                    break;
            }
        }

        float GetFlattenAxisValue(Vector3 localPos)
        {
            switch (flattenLocalAxis)
            {
                case LocalFlattenAxis.X: return localPos.x;
                case LocalFlattenAxis.Y: return localPos.y;
                case LocalFlattenAxis.Z: return localPos.z;
                default: return localPos.y;
            }
        }

        void SetFlattenAxisValue(ref Vector3 localPos, float value)
        {
            switch (flattenLocalAxis)
            {
                case LocalFlattenAxis.X:
                    localPos.x = value;
                    break;
                case LocalFlattenAxis.Y:
                    localPos.y = value;
                    break;
                case LocalFlattenAxis.Z:
                    localPos.z = value;
                    break;
                default:
                    localPos.y = value;
                    break;
            }
        }

        bool HasValidElementSelection()
        {
            if (currentVertices == null ||
                selectedElementAnchors == null ||
                selectedElementAnchors.Length == 0 ||
                selectedElementWeldedIndices == null ||
                selectedElementWeldedIndices.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selectedElementAnchors.Length; i++)
            {
                int index = selectedElementAnchors[i];
                if (index < 0 || index >= currentVertices.Length)
                    return false;
            }
            for (int i = 0; i < selectedElementWeldedIndices.Count; i++)
            {
                int index = selectedElementWeldedIndices[i];
                if (index < 0 || index >= currentVertices.Length)
                    return false;
            }
            return true;
        }

        void ValidateElementSelectionIndices()
        {
            if ((selectedElementAnchors != null || selectedElementWeldedIndices != null) &&
                !HasValidElementSelection())
            {
                ClearElementSelection();
            }
            if (hoveredElementAnchors == null || currentVertices == null)
                return;
            for (int i = 0; i < hoveredElementAnchors.Length; i++)
            {
                int index = hoveredElementAnchors[i];
                if (index < 0 || index >= currentVertices.Length)
                {
                    ClearElementHover();
                    break;
                }
            }
        }

        void ClearElementSelection()
        {
            selectedElementAnchors = null;
            selectedElementWeldedIndices = null;
            selectedVertexIndex = -1;
            ResetElementHandleState();
            ClearElementHover();
        }

        void ClearElementHover()
        {
            hoveredElementAnchors = null;
            hoveredVertexIndex = -1;
        }

        void ResetElementHandleState()
        {
            elementHandleRotation = Quaternion.identity;
            elementHandleScale = Vector3.one;
            elementHandleStartWorldPositions = null;
            elementHandleStartCenter = Vector3.zero;
        }

        void DrawVertexMarker(int vertexIndex, Color color, float sizeMultiplier)
        {
            if (currentVertices == null || vertexIndex < 0 || vertexIndex >= currentVertices.Length) return;
            Transform mt = currentMeshFilter.transform;
            Vector3 worldPos = mt.TransformPoint(currentVertices[vertexIndex]);

            Color oldColor = Handles.color;
            UnityEngine.Rendering.CompareFunction oldZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = color;
            float dotSize = HandleUtility.GetHandleSize(worldPos) * sizeMultiplier;
            Handles.SphereHandleCap(0, worldPos, Quaternion.identity, dotSize, EventType.Repaint);
            Handles.zTest = oldZ;
            Handles.color = oldColor;
        }

        bool TryPickElementAtHit(
            RaycastHit hit,
            ElementSelectionMode mode,
            out int[] anchors)
        {
            anchors = null;
            if (workingMesh == null ||
                currentVertices == null ||
                triangleRecords == null ||
                hit.triangleIndex < 0 ||
                hit.triangleIndex >= triangleRecords.Count)
            {
                return false;
            }

            MeshCollider hitMeshCollider = hit.collider as MeshCollider;
            if (hitMeshCollider == null || hitMeshCollider.sharedMesh != workingMesh)
                return false;

            SculptTriangleRecord triangle = triangleRecords[hit.triangleIndex];
            int a = triangle.a;
            int b = triangle.b;
            int c = triangle.c;
            if (a < 0 || b < 0 || c < 0 ||
                a >= currentVertices.Length ||
                b >= currentVertices.Length ||
                c >= currentVertices.Length)
            {
                return false;
            }

            Transform mt = currentMeshFilter.transform;
            Vector3 worldA = mt.TransformPoint(currentVertices[a]);
            Vector3 worldB = mt.TransformPoint(currentVertices[b]);
            Vector3 worldC = mt.TransformPoint(currentVertices[c]);
            switch (mode)
            {
                case ElementSelectionMode.Vertex:
                {
                    int best = a;
                    float bestDist = (worldA - hit.point).sqrMagnitude;
                    float distB = (worldB - hit.point).sqrMagnitude;
                    if (distB < bestDist)
                    {
                        best = b;
                        bestDist = distB;
                    }
                    float distC = (worldC - hit.point).sqrMagnitude;
                    if (distC < bestDist)
                        best = c;
                    anchors = new[] { best };
                    return true;
                }
                case ElementSelectionMode.Edge:
                {
                    float ab = DistancePointToSegmentSqr(hit.point, worldA, worldB);
                    float bc = DistancePointToSegmentSqr(hit.point, worldB, worldC);
                    float ca = DistancePointToSegmentSqr(hit.point, worldC, worldA);
                    anchors = ab <= bc && ab <= ca
                        ? new[] { a, b }
                        : bc <= ca
                            ? new[] { b, c }
                            : new[] { c, a };
                    return true;
                }
                case ElementSelectionMode.Face:
                    anchors = new[] { a, b, c };
                    return true;
                default:
                    return false;
            }
        }

        static float DistancePointToSegmentSqr(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 edge = end - start;
            float edgeSqr = edge.sqrMagnitude;
            if (edgeSqr <= 0.0000001f)
                return (point - start).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(point - start, edge) / edgeSqr);
            return (point - (start + edge * t)).sqrMagnitude;
        }

        void SetElementHover(int[] anchors)
        {
            hoveredElementAnchors = anchors != null
                ? (int[])anchors.Clone()
                : null;
            hoveredVertexIndex = elementSelectionMode == ElementSelectionMode.Vertex &&
                                 hoveredElementAnchors != null &&
                                 hoveredElementAnchors.Length > 0
                ? hoveredElementAnchors[0]
                : -1;
        }

        void SelectElement(int[] anchors, ElementSelectionMode mode)
        {
            if (anchors == null || anchors.Length == 0)
            {
                ClearElementSelection();
                return;
            }

            selectedElementAnchors = (int[])anchors.Clone();
            selectedElementMode = mode;
            selectedElementWeldedIndices = ExpandCoincidentElementIndices(anchors);
            selectedVertexIndex = mode == ElementSelectionMode.Vertex
                ? selectedElementAnchors[0]
                : -1;
            ResetElementHandleState();
            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        List<int> ExpandCoincidentElementIndices(int[] anchors)
        {
            HashSet<int> welded = new HashSet<int>();
            if (currentVertices == null || currentMeshFilter == null || anchors == null)
                return new List<int>();

            Transform mt = currentMeshFilter.transform;
            Vector3[] anchorWorldPositions = new Vector3[anchors.Length];
            for (int i = 0; i < anchors.Length; i++)
            {
                int anchor = anchors[i];
                if (anchor < 0 || anchor >= currentVertices.Length)
                    continue;
                anchorWorldPositions[i] = mt.TransformPoint(currentVertices[anchor]);
                welded.Add(anchor);
            }

            Renderer renderer = currentMeshFilter.GetComponent<Renderer>();
            float worldSize;
            if (renderer != null)
            {
                worldSize = renderer.bounds.size.magnitude;
            }
            else
            {
                Vector3 scale = mt.lossyScale;
                float maxScale = Mathf.Max(
                    Mathf.Abs(scale.x),
                    Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                worldSize = workingMesh.bounds.size.magnitude * maxScale;
            }
            float epsilon = Mathf.Max(0.000000001f, worldSize * 0.000001f);
            float epsilonSqr = epsilon * epsilon;
            for (int vertex = 0; vertex < currentVertices.Length; vertex++)
            {
                Vector3 world = mt.TransformPoint(currentVertices[vertex]);
                for (int anchor = 0; anchor < anchorWorldPositions.Length; anchor++)
                {
                    if ((world - anchorWorldPositions[anchor]).sqrMagnitude <= epsilonSqr)
                    {
                        welded.Add(vertex);
                        break;
                    }
                }
            }

            List<int> result = welded.ToList();
            result.Sort();
            return result;
        }

        Vector3 GetSelectedElementCentroidWorld()
        {
            if (selectedElementAnchors == null ||
                selectedElementAnchors.Length == 0 ||
                currentMeshFilter == null ||
                currentVertices == null)
            {
                return Vector3.zero;
            }

            Transform mt = currentMeshFilter.transform;
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < selectedElementAnchors.Length; i++)
                centroid += mt.TransformPoint(currentVertices[selectedElementAnchors[i]]);
            return centroid / selectedElementAnchors.Length;
        }

        void DrawElementGizmo(
            int[] anchors,
            ElementSelectionMode mode,
            Color color,
            bool selected)
        {
            if (anchors == null ||
                anchors.Length == 0 ||
                currentVertices == null ||
                currentMeshFilter == null)
            {
                return;
            }

            Transform mt = currentMeshFilter.transform;
            Vector3[] points = new Vector3[anchors.Length];
            for (int i = 0; i < anchors.Length; i++)
            {
                int index = anchors[i];
                if (index < 0 || index >= currentVertices.Length)
                    return;
                points[i] = mt.TransformPoint(currentVertices[index]);
            }

            Color oldColor = Handles.color;
            UnityEngine.Rendering.CompareFunction oldZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = color;
            float markerScale = selected ? 0.075f : 0.06f;
            if (mode == ElementSelectionMode.Vertex)
            {
                float size = HandleUtility.GetHandleSize(points[0]) * markerScale;
                Handles.SphereHandleCap(0, points[0], Quaternion.identity, size, EventType.Repaint);
            }
            else if (mode == ElementSelectionMode.Edge && points.Length >= 2)
            {
                Handles.DrawAAPolyLine(selected ? 6f : 4f, points[0], points[1]);
                for (int i = 0; i < 2; i++)
                {
                    float size = HandleUtility.GetHandleSize(points[i]) * markerScale * 0.65f;
                    Handles.SphereHandleCap(0, points[i], Quaternion.identity, size, EventType.Repaint);
                }
            }
            else if (mode == ElementSelectionMode.Face && points.Length >= 3)
            {
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]);
                Vector3 center = (points[0] + points[1] + points[2]) / 3f;
                float offsetAmount = HandleUtility.GetHandleSize(center) * 0.0015f;
                Vector3 offset = normal.sqrMagnitude > 0.0000001f
                    ? normal.normalized * offsetAmount
                    : Vector3.zero;
                Vector3 p0 = points[0] + offset;
                Vector3 p1 = points[1] + offset;
                Vector3 p2 = points[2] + offset;
                Color outlineColor = color;
                Color fillColor = new Color(color.r, color.g, color.b, selected ? 0.22f : 0.12f);
                Handles.color = fillColor;
                Handles.DrawAAConvexPolygon(p0, p1, p2);
                Handles.color = outlineColor;
                Handles.DrawAAPolyLine(selected ? 6f : 4f, p0, p1, p2, p0);
                for (int i = 0; i < 3; i++)
                {
                    float size = HandleUtility.GetHandleSize(points[i]) * markerScale * 0.55f;
                    Handles.SphereHandleCap(0, points[i], Quaternion.identity, size, EventType.Repaint);
                }
            }
            Handles.zTest = oldZ;
            Handles.color = oldColor;
        }

        Tool GetActiveElementTransformTool()
        {
            if (Tools.current == Tool.Rotate)
                return Tool.Rotate;
            if (Tools.current == Tool.Scale)
                return Tool.Scale;
            return Tool.Move;
        }

        string GetElementTransformUndoName(Tool tool)
        {
            string operation = tool == Tool.Rotate
                ? "Rotate"
                : tool == Tool.Scale
                    ? "Scale"
                    : "Move";
            return $"{operation} {selectedElementMode}";
        }

        void CaptureElementHandleBaseline(Vector3 center)
        {
            if (selectedElementWeldedIndices == null || currentMeshFilter == null)
                return;
            Transform mt = currentMeshFilter.transform;
            elementHandleStartCenter = center;
            elementHandleStartWorldPositions =
                new Vector3[selectedElementWeldedIndices.Count];
            for (int i = 0; i < selectedElementWeldedIndices.Count; i++)
            {
                elementHandleStartWorldPositions[i] =
                    mt.TransformPoint(currentVertices[selectedElementWeldedIndices[i]]);
            }
        }

        void DrawAndEditSelectedElementHandle()
        {
            if (!HasValidElementSelection())
                return;

            if (Event.current.type == EventType.Repaint)
            {
                DrawElementGizmo(
                    selectedElementAnchors,
                    selectedElementMode,
                    new Color(0.15f, 0.95f, 1f, 1f),
                    true);
            }

            Vector3 center = GetSelectedElementCentroidWorld();
            Tool tool = GetActiveElementTransformTool();
            Vector3 newMovePosition = center;
            Quaternion newRotation = elementHandleRotation;
            Vector3 newScale = elementHandleScale;

            EditorGUI.BeginChangeCheck();
            switch (tool)
            {
                case Tool.Rotate:
                    newRotation = Handles.RotationHandle(elementHandleRotation, center);
                    break;
                case Tool.Scale:
                    newScale = Handles.ScaleHandle(
                        elementHandleScale,
                        center,
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(center));
                    break;
                default:
                    newMovePosition = Handles.PositionHandle(center, Quaternion.identity);
                    break;
            }
            if (!EditorGUI.EndChangeCheck())
                return;

            if (elementHandleStartWorldPositions == null)
                CaptureElementHandleBaseline(center);
            if (elementHandleStartWorldPositions == null ||
                elementHandleStartWorldPositions.Length != selectedElementWeldedIndices.Count)
            {
                ResetElementHandleState();
                return;
            }

            bool hasActualChange;
            if (tool == Tool.Rotate)
            {
                hasActualChange = Quaternion.Angle(Quaternion.identity, newRotation) > 0.0001f;
            }
            else if (tool == Tool.Scale)
            {
                hasActualChange = (newScale - Vector3.one).sqrMagnitude > 0.00000001f;
            }
            else
            {
                hasActualChange = (newMovePosition - elementHandleStartCenter).sqrMagnitude > 0.0000000001f;
            }
            if (!hasActualChange)
                return;

            bool startingMutation = !sculptMeshStateCommitPending;
            string undoName = GetElementTransformUndoName(tool);
            if (startingMutation)
                BeginPendingSculptMutation(undoName);
            if (!EnsureSculptMeshEditable("Prepare Unique Mesh for Element Editing"))
            {
                if (startingMutation)
                    CancelEmptyPendingSculptMutation();
                ResetElementHandleState();
                return;
            }

            if (!sculptMeshStateCommitPending)
                Undo.RecordObject(workingMesh, undoName);

            ApplySelectedElementBaselineTransform(
                tool,
                newMovePosition,
                newRotation,
                newScale);
        }

        void ApplySelectedElementBaselineTransform(
            Tool tool,
            Vector3 movePosition,
            Quaternion rotation,
            Vector3 scale)
        {
            if (currentMeshFilter == null ||
                workingMesh == null ||
                currentVertices == null ||
                selectedElementWeldedIndices == null ||
                elementHandleStartWorldPositions == null ||
                elementHandleStartWorldPositions.Length != selectedElementWeldedIndices.Count)
            {
                return;
            }

            Transform mt = currentMeshFilter.transform;
            for (int i = 0; i < selectedElementWeldedIndices.Count; i++)
            {
                Vector3 world = elementHandleStartWorldPositions[i];
                Vector3 transformed;
                if (tool == Tool.Rotate)
                {
                    transformed = elementHandleStartCenter +
                                  rotation * (world - elementHandleStartCenter);
                }
                else if (tool == Tool.Scale)
                {
                    Vector3 delta = world - elementHandleStartCenter;
                    transformed = elementHandleStartCenter + Vector3.Scale(delta, scale);
                }
                else
                {
                    transformed = world + (movePosition - elementHandleStartCenter);
                }
                currentVertices[selectedElementWeldedIndices[i]] =
                    mt.InverseTransformPoint(transformed);
            }

            elementHandleRotation = rotation;
            elementHandleScale = scale;
            workingMesh.vertices = currentVertices;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
            sculptMeshStateCommitPending = true;
            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        bool IsVertexFacingHit(int vertexIndex, Vector3 hitNormal)
        {
            if (workingMesh == null) return true;
            Vector3[] normals = workingMesh.normals;
            if (normals == null || normals.Length <= vertexIndex) return true;

            Transform mt = currentMeshFilter.transform;
            Vector3 worldNormal = mt.TransformDirection(normals[vertexIndex]).normalized;
            return Vector3.Dot(worldNormal, hitNormal) > 0.01f;
        }

        bool IsTriangleFacingHit(Vector3 wp1, Vector3 wp2, Vector3 wp3, Vector3 hitPoint, Vector3 hitNormal, float depthTolerance)
        {
            Vector3 triNormal = Vector3.Cross(wp2 - wp1, wp3 - wp1);
            if (triNormal.sqrMagnitude < 0.0000001f) return false;
            triNormal.Normalize();

            // Skip opposite side winding on double-sided surfaces.
            if (Vector3.Dot(triNormal, hitNormal) <= 0.01f) return false;

            // The triangle must intersect the shallow surface slab around the
            // hit plane. Testing all three vertices avoids reaching through to
            // a nearby parallel surface while still accepting large triangles
            // whose centroid lies outside the brush.
            float depth1 = Vector3.Dot(wp1 - hitPoint, hitNormal);
            float depth2 = Vector3.Dot(wp2 - hitPoint, hitNormal);
            float depth3 = Vector3.Dot(wp3 - hitPoint, hitNormal);
            float minDepth = Mathf.Min(depth1, Mathf.Min(depth2, depth3));
            float maxDepth = Mathf.Max(depth1, Mathf.Max(depth2, depth3));
            if (maxDepth < -depthTolerance || minDepth > depthTolerance)
                return false;

            // Prefer faces with a similar camera-facing orientation to the hit.
            if (lastHitRayDirection.sqrMagnitude > 0.0000001f)
            {
                float faceToRay = Vector3.Dot(triNormal, -lastHitRayDirection.normalized);
                if (faceToRay <= 0.001f) return false;
            }

            return true;
        }

        void CollectTriangleCandidates(Vector3 worldCenter, HashSet<int> candidates)
        {
            if (candidates == null)
                return;
            candidates.Clear();
            if (triangleBuckets == null || triangleRecords == null || bucketSize <= 0f)
                return;

            float queryRadius = GetBrushRangeWorld() + GetSurfaceDepthToleranceWorld();
            int range = Mathf.CeilToInt(queryRadius / bucketSize) + 1;
            Vector3Int centerID = GetBucketID(worldCenter);
            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int id = centerID + new Vector3Int(x, y, z);
                        if (!triangleBuckets.TryGetValue(id, out List<int> records))
                            continue;
                        for (int i = 0; i < records.Count; i++)
                            candidates.Add(records[i]);
                    }
                }
            }

            if (largeTriangleRecords == null)
                return;
            for (int i = 0; i < largeTriangleRecords.Count; i++)
                candidates.Add(largeTriangleRecords[i]);
        }

        bool TryGetTriangleWorldPositions(
            SculptTriangleRecord triangle,
            out Vector3 wp1,
            out Vector3 wp2,
            out Vector3 wp3)
        {
            wp1 = Vector3.zero;
            wp2 = Vector3.zero;
            wp3 = Vector3.zero;
            if (currentMeshFilter == null || currentVertices == null ||
                triangle.a < 0 || triangle.a >= currentVertices.Length ||
                triangle.b < 0 || triangle.b >= currentVertices.Length ||
                triangle.c < 0 || triangle.c >= currentVertices.Length)
            {
                return false;
            }

            Transform mt = currentMeshFilter.transform;
            wp1 = mt.TransformPoint(currentVertices[triangle.a]);
            wp2 = mt.TransformPoint(currentVertices[triangle.b]);
            wp3 = mt.TransformPoint(currentVertices[triangle.c]);
            return true;
        }

        bool IsExistingVertexInsideRemoveBrush(
            int vertexIndex,
            Vector3 hitPoint,
            Vector3 hitNormal)
        {
            if (currentMeshFilter == null ||
                currentVertices == null ||
                vertexIndex < 0 ||
                vertexIndex >= currentVertices.Length)
            {
                return false;
            }

            Vector3 safeNormal = hitNormal.sqrMagnitude > 0.0000001f
                ? hitNormal.normalized
                : Vector3.up;
            Vector3 worldPosition = currentMeshFilter.transform.TransformPoint(
                currentVertices[vertexIndex]);
            Vector3 delta = worldPosition - hitPoint;

            // Remove works from vertices on the hovered surface, not vertices on
            // a nearby back wall. Unlike Boolean Cut, this test never creates an
            // intersection point between the existing vertices.
            float depth = Vector3.Dot(delta, safeNormal);
            if (Mathf.Abs(depth) > GetSurfaceDepthToleranceWorld())
                return false;

            Vector3 planarDelta = delta - safeNormal * depth;
            if (brushShape == BrushShape.Circle)
            {
                float radius = Mathf.Max(0f, brushSize);
                return planarDelta.sqrMagnitude <= radius * radius;
            }

            GetRemoveBrushBasis(safeNormal, out Vector3 brushRight, out Vector3 brushUp);
            float horizontal = Vector3.Dot(planarDelta, brushRight);
            float vertical = Vector3.Dot(planarDelta, brushUp);
            return Mathf.Abs(horizontal) <= Mathf.Max(0f, squareBrushWidth) * 0.5f &&
                   Mathf.Abs(vertical) <= Mathf.Max(0f, squareBrushHeight) * 0.5f;
        }

        static void GetRemoveBrushBasis(
            Vector3 hitNormal,
            out Vector3 brushRight,
            out Vector3 brushUp)
        {
            brushRight = Vector3.Cross(hitNormal, Vector3.up);
            if (brushRight.sqrMagnitude < 0.001f)
                brushRight = Vector3.Cross(hitNormal, Vector3.right);
            brushRight.Normalize();
            brushUp = Vector3.Cross(brushRight, hitNormal).normalized;
        }

        bool TriangleIntersectsRemoveBrush(
            SculptTriangleRecord triangle,
            Vector3 hitPoint,
            Vector3 hitNormal,
            ISet<int> selectedVertices)
        {
            if (!TryGetTriangleWorldPositions(
                    triangle,
                    out Vector3 wp1,
                    out Vector3 wp2,
                    out Vector3 wp3))
            {
                return false;
            }

            Vector3 safeNormal = hitNormal.sqrMagnitude > 0.0000001f
                ? hitNormal.normalized
                : Vector3.up;
            float depthTolerance = GetSurfaceDepthToleranceWorld();
            if (!IsTriangleFacingHit(
                    wp1,
                    wp2,
                    wp3,
                    hitPoint,
                    safeNormal,
                    depthTolerance))
            {
                return false;
            }

            // Circle Remove deliberately remains based on existing vertex
            // positions. This keeps it lightweight and distinct from Boolean
            // Cut; the closed boundary loop can be rounded after the stroke.
            if (brushShape == BrushShape.Circle)
            {
                return selectedVertices != null &&
                       (selectedVertices.Contains(triangle.a) ||
                        selectedVertices.Contains(triangle.b) ||
                        selectedVertices.Contains(triangle.c));
            }

            // A square is commonly aligned to a wall grid. Requiring a vertex
            // centre to enter the square made a visibly touched corner cell
            // easy to miss. Clip the triangle to the hovered surface slab and
            // use an inclusive 2D overlap test so every touched face is chosen.
            EnsureSculptScratchBuffers();
            if (!ClipTriangleToDepthSlab(
                    wp1,
                    wp2,
                    wp3,
                    hitPoint,
                    safeNormal,
                    depthTolerance,
                    depthClipScratchA,
                    depthClipScratchB))
            {
                return false;
            }

            GetRemoveBrushBasis(safeNormal, out Vector3 brushRight, out Vector3 brushUp);
            projectedTriangleScratch.Clear();
            for (int i = 0; i < depthClipScratchA.Count; i++)
            {
                projectedTriangleScratch.Add(ProjectToBrushPlane(
                    depthClipScratchA[i] - hitPoint,
                    brushRight,
                    brushUp));
            }
            return PolygonIntersectsRectangle(
                projectedTriangleScratch,
                Mathf.Max(0f, squareBrushWidth) * 0.5f,
                Mathf.Max(0f, squareBrushHeight) * 0.5f);
        }

        static bool ClipTriangleToDepthSlab(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 planePoint,
            Vector3 planeNormal,
            float tolerance,
            List<Vector3> scratchA,
            List<Vector3> scratchB)
        {
            scratchA.Clear();
            scratchB.Clear();
            scratchA.Add(a);
            scratchA.Add(b);
            scratchA.Add(c);
            ClipPolygonToDepthPlane(
                scratchA,
                scratchB,
                planePoint,
                planeNormal,
                -tolerance,
                true);
            if (scratchB.Count < 3)
                return false;

            scratchA.Clear();
            ClipPolygonToDepthPlane(
                scratchB,
                scratchA,
                planePoint,
                planeNormal,
                tolerance,
                false);
            return scratchA.Count >= 3;
        }

        static void ClipPolygonToDepthPlane(
            List<Vector3> input,
            List<Vector3> output,
            Vector3 planePoint,
            Vector3 planeNormal,
            float planeDepth,
            bool keepGreater)
        {
            output.Clear();
            if (input.Count == 0)
                return;

            Vector3 previous = input[input.Count - 1];
            float previousDepth = Vector3.Dot(previous - planePoint, planeNormal);
            bool previousInside = keepGreater
                ? previousDepth >= planeDepth
                : previousDepth <= planeDepth;
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 current = input[i];
                float currentDepth = Vector3.Dot(current - planePoint, planeNormal);
                bool currentInside = keepGreater
                    ? currentDepth >= planeDepth
                    : currentDepth <= planeDepth;

                if (currentInside != previousInside)
                {
                    float denominator = currentDepth - previousDepth;
                    if (Mathf.Abs(denominator) > 0.0000001f)
                    {
                        float t = Mathf.Clamp01((planeDepth - previousDepth) / denominator);
                        output.Add(Vector3.LerpUnclamped(previous, current, t));
                    }
                }
                if (currentInside)
                    output.Add(current);

                previous = current;
                previousDepth = currentDepth;
                previousInside = currentInside;
            }
        }

        static Vector2 ProjectToBrushPlane(
            Vector3 delta,
            Vector3 brushRight,
            Vector3 brushUp)
        {
            return new Vector2(
                Vector3.Dot(delta, brushRight),
                Vector3.Dot(delta, brushUp));
        }

        static bool PolygonIntersectsCircle(List<Vector2> polygon, float radius)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            float radiusSqr = radius * radius;
            for (int i = 0; i < polygon.Count; i++)
            {
                if (polygon[i].sqrMagnitude <= radiusSqr)
                    return true;
            }
            if (PointInConvexPolygon(Vector2.zero, polygon))
                return true;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 start = polygon[i];
                Vector2 end = polygon[(i + 1) % polygon.Count];
                if (DistancePointToSegmentSqr(Vector2.zero, start, end) <= radiusSqr)
                    return true;
            }
            return false;
        }

        static bool PolygonIntersectsRectangle(
            List<Vector2> polygon,
            float halfWidth,
            float halfHeight)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            for (int i = 0; i < polygon.Count; i++)
            {
                if (PointInRectangle(polygon[i], halfWidth, halfHeight))
                    return true;
            }

            if (PointInConvexPolygon(Vector2.zero, polygon) ||
                PointInConvexPolygon(new Vector2(-halfWidth, -halfHeight), polygon) ||
                PointInConvexPolygon(new Vector2(-halfWidth, halfHeight), polygon) ||
                PointInConvexPolygon(new Vector2(halfWidth, -halfHeight), polygon) ||
                PointInConvexPolygon(new Vector2(halfWidth, halfHeight), polygon))
            {
                return true;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                if (SegmentIntersectsRectangle(
                        polygon[i],
                        polygon[(i + 1) % polygon.Count],
                        halfWidth,
                        halfHeight))
                {
                    return true;
                }
            }
            return false;
        }

        static bool PointInConvexPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool hasNegative = false;
            bool hasPositive = false;
            const float epsilon = 0.000001f;
            for (int i = 0; i < polygon.Count; i++)
            {
                float cross = Cross2D(
                    polygon[(i + 1) % polygon.Count] - polygon[i],
                    point - polygon[i]);
                hasNegative |= cross < -epsilon;
                hasPositive |= cross > epsilon;
                if (hasNegative && hasPositive)
                    return false;
            }
            return true;
        }

        static bool PointInRectangle(Vector2 point, float halfWidth, float halfHeight)
        {
            return Mathf.Abs(point.x) <= halfWidth &&
                   Mathf.Abs(point.y) <= halfHeight;
        }

        static float Cross2D(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        static float DistancePointToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float edgeSqr = edge.sqrMagnitude;
            if (edgeSqr <= 0.0000001f)
                return (point - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / edgeSqr);
            return (point - (a + edge * t)).sqrMagnitude;
        }

        static bool SegmentIntersectsRectangle(
            Vector2 start,
            Vector2 end,
            float halfWidth,
            float halfHeight)
        {
            float minT = 0f;
            float maxT = 1f;
            Vector2 delta = end - start;
            return ClipSegmentAxis(start.x, delta.x, -halfWidth, halfWidth, ref minT, ref maxT) &&
                   ClipSegmentAxis(start.y, delta.y, -halfHeight, halfHeight, ref minT, ref maxT);
        }

        static bool ClipSegmentAxis(
            float start,
            float delta,
            float min,
            float max,
            ref float minT,
            ref float maxT)
        {
            if (Mathf.Abs(delta) <= 0.0000001f)
                return start >= min && start <= max;

            float inverse = 1f / delta;
            float t1 = (min - start) * inverse;
            float t2 = (max - start) * inverse;
            if (t1 > t2)
            {
                float swap = t1;
                t1 = t2;
                t2 = swap;
            }

            minT = Mathf.Max(minT, t1);
            maxT = Mathf.Min(maxT, t2);
            return minT <= maxT;
        }

        // --- Features ---

        void CollectRemoveVertexCandidates(
            Vector3 worldCenter,
            Vector3 hitNormal,
            HashSet<int> candidates)
        {
            if (candidates == null)
                return;
            candidates.Clear();
            if (vertexBuckets == null || bucketSize <= 0f)
                return;

            int range = Mathf.CeilToInt(GetBrushRangeWorld() / bucketSize) + 1;
            Vector3Int centerID = GetBucketID(worldCenter);
            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int id = centerID + new Vector3Int(x, y, z);
                        if (!vertexBuckets.TryGetValue(id, out List<int> indices))
                            continue;
                        for (int index = 0; index < indices.Count; index++)
                        {
                            int vertexIndex = indices[index];
                            if (IsExistingVertexInsideRemoveBrush(
                                    vertexIndex,
                                    worldCenter,
                                    hitNormal))
                            {
                                candidates.Add(vertexIndex);
                            }
                        }
                    }
                }
            }
        }

        void DrawVertexGizmos(Vector3 worldCenter)
        {
            if (vertexBuckets == null) return;

            if (currentMode == SculptMode.Remove)
            {
                EnsureSculptScratchBuffers();
                CollectRemoveVertexCandidates(
                    worldCenter,
                    lastHitNormal,
                    removeVertexCandidateScratch);
                CollectTriangleCandidates(worldCenter, triangleCandidateScratch);
                removeAffectedVertexScratch.Clear();
                foreach (int recordIndex in triangleCandidateScratch)
                {
                    if (recordIndex < 0 || recordIndex >= triangleRecords.Count)
                        continue;
                    SculptTriangleRecord triangle = triangleRecords[recordIndex];
                    if (!TriangleIntersectsRemoveBrush(
                            triangle,
                            worldCenter,
                            lastHitNormal,
                            removeVertexCandidateScratch))
                    {
                        continue;
                    }

                    removeAffectedVertexScratch.Add(triangle.a);
                    removeAffectedVertexScratch.Add(triangle.b);
                    removeAffectedVertexScratch.Add(triangle.c);
                }

                Handles.color = gizmoColor;
                Transform removeTransform = currentMeshFilter.transform;
                foreach (int vertexIndex in removeAffectedVertexScratch)
                {
                    Handles.DotHandleCap(
                        0,
                        removeTransform.TransformPoint(currentVertices[vertexIndex]),
                        Quaternion.identity,
                        gizmoSize,
                        EventType.Repaint);
                }

                if (removeStrokeActive && removeStrokeSelectedVertices != null)
                {
                    Handles.color = new Color(1f, 0.2f, 0.1f, 0.95f);
                    foreach (int vertexIndex in removeStrokeSelectedVertices)
                    {
                        if (vertexIndex < 0 || vertexIndex >= currentVertices.Length)
                            continue;
                        Handles.DotHandleCap(
                            0,
                            removeTransform.TransformPoint(currentVertices[vertexIndex]),
                            Quaternion.identity,
                            gizmoSize,
                            EventType.Repaint);
                    }
                }
                return;
            }

            // --- OPTIMIZATION: Local Space Math ---
            // Instead of calling TransformPoint on every vertex in range (which is slow),
            // we transform the brush center to Local Space once and compare sqrMagnitude.
            Transform mt = currentMeshFilter.transform;
            Vector3 localCenter = mt.InverseTransformPoint(worldCenter);

            // Adjust local brush size based on object scale to ensure we catch all vertices
            float maxScale = Mathf.Max(mt.lossyScale.x, Mathf.Max(mt.lossyScale.y, mt.lossyScale.z));
            float localBrushSize = brushSize / maxScale;
            float localWidth = squareBrushWidth / maxScale;
            float localHeight = squareBrushHeight / maxScale;
            float sqrLocalSize = localBrushSize * localBrushSize;

            float sizeForRange = GetBrushRangeWorld();
            float depthTolerance = GetSurfaceDepthToleranceWorld();
            int range = Mathf.CeilToInt(sizeForRange / bucketSize) + 1;
            Vector3Int centerID = GetBucketID(worldCenter);

            Handles.color = gizmoColor;

            // Basis for Square Brush (Local Space)
            Vector3 localNormal = mt.InverseTransformDirection(lastHitNormal).normalized;
            Vector3 localRight = Vector3.Cross(localNormal, Vector3.up);
            if (localRight.sqrMagnitude < 0.001f) localRight = Vector3.Cross(localNormal, Vector3.right);
            localRight.Normalize();
            Vector3 localUp = Vector3.Cross(localRight, localNormal).normalized;

            float halfW = localWidth * 0.5f;
            float halfH = localHeight * 0.5f;

            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int id = centerID + new Vector3Int(x, y, z);
                        if (vertexBuckets.TryGetValue(id, out List<int> indices))
                        {
                            foreach (int i in indices)
                            {
                                Vector3 vPos = currentVertices[i]; // Raw local position
                                bool inside = false;

                                if (brushShape == BrushShape.Circle)
                                {
                                    // Fast distance check
                                    if ((vPos - localCenter).sqrMagnitude <= sqrLocalSize) inside = true;
                                }
                                else
                                {
                                    Vector3 delta = vPos - localCenter;
                                    float u = Vector3.Dot(delta, localRight);
                                    float v = Vector3.Dot(delta, localUp);
                                    inside = (Mathf.Abs(u) <= halfW && Mathf.Abs(v) <= halfH);
                                }

                                if (inside)
                                {
                                    // Only transform to world space if we actually need to draw it
                                    Vector3 worldPos = mt.TransformPoint(vPos);
                                    if (Vector3.Dot(worldPos - worldCenter, lastHitNormal) < -depthTolerance) continue;
                                    if (!IsVertexFacingHit(i, lastHitNormal)) continue;
                                    Handles.DotHandleCap(0, worldPos, Quaternion.identity, gizmoSize, EventType.Repaint);
                                }
                            }
                        }
                    }
                }
            }
        }

        void DrawTriangleGizmos(Vector3 worldCenter)
        {
            if (triangleBuckets == null || triangleRecords == null)
                return;

            EnsureSculptScratchBuffers();
            CollectRemoveVertexCandidates(
                worldCenter,
                lastHitNormal,
                removeVertexCandidateScratch);
            CollectTriangleCandidates(worldCenter, triangleCandidateScratch);
            Handles.color = gizmoColor;
            foreach (int recordIndex in triangleCandidateScratch)
            {
                if (recordIndex < 0 || recordIndex >= triangleRecords.Count)
                    continue;
                SculptTriangleRecord triangle = triangleRecords[recordIndex];
                if (!TriangleIntersectsRemoveBrush(
                        triangle,
                        worldCenter,
                        lastHitNormal,
                        removeVertexCandidateScratch))
                    continue;
                DrawTriangleOutline(triangle);
            }

            if (!removeStrokeActive || removeStrokeTrianglesBySubmesh == null)
                return;

            Handles.color = new Color(1f, 0.2f, 0.1f, 0.95f);
            foreach (KeyValuePair<int, HashSet<int>> removed in removeStrokeTrianglesBySubmesh)
            {
                if (removeStrokeSubmeshTriangles == null ||
                    removed.Key < 0 ||
                    removed.Key >= removeStrokeSubmeshTriangles.Length)
                {
                    continue;
                }

                int[] indices = removeStrokeSubmeshTriangles[removed.Key];
                if (indices == null ||
                    removeStrokeSubmeshBaseVertices == null ||
                    removed.Key >= removeStrokeSubmeshBaseVertices.Length)
                    continue;
                int baseVertex = removeStrokeSubmeshBaseVertices[removed.Key];
                foreach (int triangleIndex in removed.Value)
                {
                    int offset = triangleIndex * 3;
                    if (offset < 0 || offset + 2 >= indices.Length)
                        continue;
                    DrawTriangleOutline(new SculptTriangleRecord(
                        removed.Key,
                        triangleIndex,
                        indices[offset] + baseVertex,
                        indices[offset + 1] + baseVertex,
                        indices[offset + 2] + baseVertex));
                }
            }
        }

        void DrawTriangleOutline(SculptTriangleRecord triangle)
        {
            if (!TryGetTriangleWorldPositions(triangle, out Vector3 wp1, out Vector3 wp2, out Vector3 wp3))
                return;
            Handles.DrawLine(wp1, wp2);
            Handles.DrawLine(wp2, wp3);
            Handles.DrawLine(wp3, wp1);
        }

        // --- OPTIMIZATION: Added recalculateNormals bool ---
        void SculptMesh(Vector3 hitPoint, Vector3 hitNormal, bool recalculateNormals, Vector3 nudgeDeltaWorld)
        {
            EnsureSpatialBucketsCurrent();
            if (vertexBuckets == null) return;

            float sizeForRange = GetBrushRangeWorld();
            int range = Mathf.CeilToInt(sizeForRange / bucketSize);
            Vector3Int centerID = GetBucketID(hitPoint);
            Transform mt = currentMeshFilter.transform;

            bool isSmoothing = currentMode == SculptMode.Smooth;
            bool isPushing = currentMode == SculptMode.Push;
            bool isReverting = currentMode == SculptMode.Revert_Pose;
            bool isPulling = currentMode == SculptMode.Move;
            bool isNudging = currentMode == SculptMode.Nudge;
            bool isFlattening = currentMode == SculptMode.Flatten_Height;

            Vector3 projectedNudge = Vector3.ProjectOnPlane(nudgeDeltaWorld, hitNormal);
            Vector3 nudgeDeltaLocal = mt.InverseTransformVector(projectedNudge);

            List<int> affectedIndices = new List<int>();
            for (int x = -range; x <= range; x++)
                for (int y = -range; y <= range; y++)
                    for (int z = -range; z <= range; z++)
                        if (vertexBuckets.TryGetValue(centerID + new Vector3Int(x, y, z), out List<int> list))
                            affectedIndices.AddRange(list);

            if (affectedIndices.Count == 0) return;

            bool startingMutation = !sculptMeshStateCommitPending;
            if (startingMutation)
                BeginPendingSculptMutation("Sculpt Mesh");
            if (!EnsureSculptMeshEditable("Prepare Unique Mesh for Sculpting"))
            {
                if (startingMutation)
                    CancelEmptyPendingSculptMutation();
                return;
            }

            Vector3 avgPos = Vector3.zero;
            if (isSmoothing)
            {
                foreach (int i in affectedIndices) avgPos += currentVertices[i];
                avgPos /= affectedIndices.Count;
            }

            Vector3 brushRight = Vector3.Cross(hitNormal, Vector3.up);
            if (brushRight.sqrMagnitude < 0.001f) brushRight = Vector3.Cross(hitNormal, Vector3.right);
            brushRight.Normalize();
            Vector3 brushUp = Vector3.Cross(brushRight, hitNormal).normalized;

            float halfW = squareBrushWidth * 0.5f;
            float halfH = squareBrushHeight * 0.5f;
            bool verticesChanged = false;

            foreach (int i in affectedIndices)
            {
                Vector3 worldPos = mt.TransformPoint(currentVertices[i]);
                float falloff = 0f;
                float alphaMod = 1f;

                if (brushShape == BrushShape.Circle)
                {
                    float dist = Vector3.Distance(hitPoint, worldPos);
                    if (dist > brushSize) continue;
                    float ratio = dist / brushSize;
                    falloff = Mathf.Clamp01(1.0f - ratio);
                    falloff *= falloff;
                    alphaMod = GetBrushAlphaValue(worldPos, hitPoint, hitNormal);
                }
                else
                {
                    Vector3 delta = worldPos - hitPoint;
                    float u = Vector3.Dot(delta, brushRight);
                    float v = Vector3.Dot(delta, brushUp);
                    if (Mathf.Abs(u) > halfW || Mathf.Abs(v) > halfH) continue;
                    float normU = Mathf.Abs(u) / halfW;
                    float normV = Mathf.Abs(v) / halfH;
                    float ratio = Mathf.Max(normU, normV);
                    falloff = Mathf.Clamp01(1.0f - ratio);
                    falloff *= falloff;

                    if (brushAlpha != null)
                    {
                        float uNorm = u / (halfW * 2f) + 0.5f;
                        float vNorm = v / (halfH * 2f) + 0.5f;
                        if (uNorm >= 0f && uNorm <= 1f && vNorm >= 0f && vNorm <= 1f)
                        {
                            float val = brushAlpha.GetPixelBilinear(uNorm, vNorm).r;
                            alphaMod = invertBrushAlpha ? (1.0f - val) : val;
                        }
                        else
                        {
                            alphaMod = 0f;
                        }
                    }
                }

                falloff *= alphaMod;
                if (falloff <= 0.001f) continue;

                verticesChanged = true;

                if (isReverting)
                {
                    if (originalVertices != null && i < originalVertices.Length)
                        currentVertices[i] = Vector3.Lerp(currentVertices[i], originalVertices[i], brushStrength * 0.1f * falloff);
                }
                else if (isSmoothing)
                {
                    Vector3 localAvg = mt.InverseTransformPoint(avgPos);
                    currentVertices[i] = Vector3.Lerp(currentVertices[i], currentVertices[i] * 0.5f + localAvg * 0.5f, brushStrength * 0.0001f * falloff);
                }
                else if (isFlattening)
                {
                    Vector3 targetLocalPos = currentVertices[i];
                    SetFlattenAxisValue(ref targetLocalPos, GetEffectiveFlattenHeight());
                    currentVertices[i] = Vector3.Lerp(currentVertices[i], targetLocalPos, brushStrength * 0.1f * falloff);
                }
                else if (isNudging)
                {
                    currentVertices[i] += nudgeDeltaLocal * brushStrength * falloff;
                }
                else if (isPulling || isPushing)
                {
                    Vector3 moveDir = isPushing ? -hitNormal : hitNormal;
                    currentVertices[i] += mt.InverseTransformVector(moveDir) * brushStrength * 0.02f * falloff;
                }
            }

            if (verticesChanged)
            {
                // A single pre-stroke mesh snapshot is sufficient for the
                // entire drag. Re-recording the full mesh for every dab is the
                // main source of Sculpt Undo stalls on dense meshes.
                if (!sculptMeshStateCommitPending)
                    Undo.RecordObject(workingMesh, "Sculpt Mesh");
                workingMesh.vertices = currentVertices;
                sculptMeshStateCommitPending = true;

                // --- OPTIMIZATION Check ---
                if (recalculateNormals)
                {
                    workingMesh.RecalculateNormals();
                    workingMesh.RecalculateBounds();
                }
            }
        }

        // =================================================================================
        // NEW: REMOVE TRIANGLES
        // =================================================================================
        void BeginRemoveStroke()
        {
            if (removeStrokeActive || workingMesh == null || currentMeshFilter == null)
                return;

            EnsureSpatialBucketsCurrent();
            removeStrokeActive = true;
            removeStrokeMesh = workingMesh;
            removeStrokeMeshFilter = currentMeshFilter;
            removeStrokeStorage = currentStorage;
            removeStrokeTrianglesBySubmesh = new Dictionary<int, HashSet<int>>();
            removeStrokeSelectedVertices = new HashSet<int>();
            removeStrokeSamples = new List<RealBlendSculptRemoveSample>(32);
            removeStrokeVertexCount = workingMesh.vertexCount;
            removeStrokeSubmeshTriangles = new int[workingMesh.subMeshCount][];
            removeStrokeSubmeshBaseVertices = new int[workingMesh.subMeshCount];
            removeStrokeKeptTriangles = new List<int>[workingMesh.subMeshCount];
            for (int submesh = 0; submesh < workingMesh.subMeshCount; submesh++)
            {
                if (workingMesh.GetTopology(submesh) == UnityEngine.MeshTopology.Triangles)
                {
                    uint baseVertex = workingMesh.GetBaseVertex(submesh);
                    if (baseVertex > int.MaxValue)
                        continue;
                    removeStrokeSubmeshTriangles[submesh] = workingMesh.GetTriangles(submesh, false);
                    removeStrokeSubmeshBaseVertices[submesh] = (int)baseVertex;
                }
            }
            removeStrokeHasLastSample = false;
            removeStrokeUndoGroup = -1;
            removeStrokeMutationPrepared = false;
            removeStrokeGeometryApplied = false;
            removeTopologyStatus = string.Empty;
            removeTopologyStatusType = MessageType.None;
        }

        void AccumulateRemoveStroke(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (!removeStrokeActive || removeStrokeMesh != workingMesh)
            {
                CommitRemoveStroke();
                BeginRemoveStroke();
            }
            if (!removeStrokeActive)
                return;

            Vector3 safeNormal = hitNormal.sqrMagnitude > 0.0000001f
                ? hitNormal.normalized
                : removeStrokeHasLastSample
                    ? removeStrokeLastNormal
                    : Vector3.up;

            bool changed = false;
            if (!removeStrokeHasLastSample)
            {
                changed |= AccumulateRemoveStrokeSample(hitPoint, safeNormal);
            }
            else
            {
                float distance = Vector3.Distance(removeStrokeLastPoint, hitPoint);
                float spacing = GetRemoveSweepSpacing();
                int samples = Mathf.Clamp(Mathf.CeilToInt(distance / spacing), 1, 512);
                for (int sample = 1; sample <= samples; sample++)
                {
                    float t = sample / (float)samples;
                    Vector3 samplePoint = Vector3.Lerp(removeStrokeLastPoint, hitPoint, t);
                    Vector3 sampleNormal = Vector3.Lerp(removeStrokeLastNormal, safeNormal, t);
                    if (sampleNormal.sqrMagnitude <= 0.0000001f)
                        sampleNormal = safeNormal;
                    changed |= AccumulateRemoveStrokeSample(samplePoint, sampleNormal.normalized);
                }
            }

            // A long cursor move can contain hundreds of interpolated brush
            // samples. Update the live index buffers once per editor event,
            // after every new triangle ordinal has been accumulated.
            if (changed)
                ApplyRemoveStrokeGeometry();

            removeStrokeHasLastSample = true;
            removeStrokeLastPoint = hitPoint;
            removeStrokeLastNormal = safeNormal;
        }

        float GetRemoveSweepSpacing()
        {
            float smallestRadius = brushShape == BrushShape.Circle
                ? brushSize
                : Mathf.Min(squareBrushWidth, squareBrushHeight) * 0.5f;
            return Mathf.Max(0.005f, smallestRadius * 0.5f);
        }

        bool AccumulateRemoveStrokeSample(Vector3 hitPoint, Vector3 hitNormal)
        {
            EnsureSculptScratchBuffers();
            RecordRemoveStrokeSample(hitPoint, hitNormal);
            CollectRemoveVertexCandidates(
                hitPoint,
                hitNormal,
                removeVertexCandidateScratch);
            if (brushShape == BrushShape.Circle &&
                removeVertexCandidateScratch.Count == 0)
                return false;

            CollectTriangleCandidates(hitPoint, triangleCandidateScratch);
            bool changed = false;
            foreach (int recordIndex in triangleCandidateScratch)
            {
                if (recordIndex < 0 || recordIndex >= triangleRecords.Count)
                    continue;
                SculptTriangleRecord triangle = triangleRecords[recordIndex];
                if (!TriangleIntersectsRemoveBrush(
                        triangle,
                        hitPoint,
                        hitNormal,
                        removeVertexCandidateScratch))
                    continue;

                removeStrokeSelectedVertices.Add(triangle.a);
                removeStrokeSelectedVertices.Add(triangle.b);
                removeStrokeSelectedVertices.Add(triangle.c);

                if (!removeStrokeTrianglesBySubmesh.TryGetValue(
                        triangle.submesh,
                        out HashSet<int> removedTriangles))
                {
                    removedTriangles = new HashSet<int>();
                    removeStrokeTrianglesBySubmesh.Add(triangle.submesh, removedTriangles);
                }
                changed |= removedTriangles.Add(triangle.triangle);
            }
            return changed;
        }

        void RecordRemoveStrokeSample(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (removeStrokeSamples == null)
                removeStrokeSamples = new List<RealBlendSculptRemoveSample>(32);

            Vector3 safeNormal = hitNormal.sqrMagnitude > 0.0000001f
                ? hitNormal.normalized
                : Vector3.up;
            RealBlendSculptRemoveSample sample = brushShape == BrushShape.Circle
                ? RealBlendSculptRemoveSample.Circle(
                    hitPoint,
                    safeNormal,
                    Mathf.Max(0.0001f, brushSize))
                : RealBlendSculptRemoveSample.Rectangle(
                    hitPoint,
                    safeNormal,
                    Mathf.Max(0.0002f, squareBrushWidth),
                    Mathf.Max(0.0002f, squareBrushHeight));

            if (removeStrokeSamples.Count > 0)
            {
                RealBlendSculptRemoveSample previous =
                    removeStrokeSamples[removeStrokeSamples.Count - 1];
                float minimumSpacing = Mathf.Max(
                    0.0001f,
                    GetRemoveSweepSpacing() * 0.45f);
                if ((previous.worldCenter - sample.worldCenter).sqrMagnitude <
                        minimumSpacing * minimumSpacing &&
                    Vector3.Dot(previous.worldNormal, sample.worldNormal) > 0.999f)
                {
                    // Keep the latest raycast position for a square wall cut,
                    // but do not flood a stationary circle stroke with samples.
                    removeStrokeSamples[removeStrokeSamples.Count - 1] = sample;
                    return;
                }
            }

            removeStrokeSamples.Add(sample);
            if (removeStrokeSamples.Count <= 2048)
                return;

            // Extremely long drags remain bounded. Preserve both ends while
            // retaining every second interior sample for the swept-circle rim.
            for (int destination = 1, source = 2;
                 source < removeStrokeSamples.Count - 1;
                 destination++, source += 2)
            {
                removeStrokeSamples[destination] = removeStrokeSamples[source];
            }
            RealBlendSculptRemoveSample last =
                removeStrokeSamples[removeStrokeSamples.Count - 1];
            int retained = (removeStrokeSamples.Count + 1) / 2;
            removeStrokeSamples.RemoveRange(
                retained,
                removeStrokeSamples.Count - retained);
            removeStrokeSamples[removeStrokeSamples.Count - 1] = last;
        }

        bool RemoveStrokeSnapshotMatches(Mesh targetMesh)
        {
            if (targetMesh == null ||
                removeStrokeSubmeshTriangles == null ||
                removeStrokeSubmeshBaseVertices == null ||
                targetMesh.vertexCount != removeStrokeVertexCount ||
                targetMesh.subMeshCount != removeStrokeSubmeshTriangles.Length)
            {
                return false;
            }

            for (int submesh = 0; submesh < removeStrokeSubmeshTriangles.Length; submesh++)
            {
                int[] source = removeStrokeSubmeshTriangles[submesh];
                if (source == null)
                    continue;
                if (targetMesh.GetTopology(submesh) != UnityEngine.MeshTopology.Triangles ||
                    targetMesh.GetIndexCount(submesh) != (uint)source.Length ||
                    targetMesh.GetBaseVertex(submesh) != removeStrokeSubmeshBaseVertices[submesh])
                {
                    return false;
                }
            }
            return true;
        }

        bool PrepareRemoveStrokeMutation()
        {
            if (removeStrokeMutationPrepared)
                return removeStrokeMesh != null;
            if (!removeStrokeActive ||
                removeStrokeMesh == null ||
                removeStrokeMeshFilter == null ||
                !RemoveStrokeSnapshotMatches(removeStrokeMesh))
            {
                return false;
            }

            Undo.IncrementCurrentGroup();
            removeStrokeUndoGroup = Undo.GetCurrentGroup();
            removeStrokeOwnsUndoGroup = removeStrokeUndoGroup > 0;
            Undo.SetCurrentGroupName("Remove Triangles");
            if (removeStrokeMeshFilter == currentMeshFilter && removeStrokeMesh == workingMesh)
            {
                if (!EnsureSculptMeshEditable("Prepare Unique Mesh for Triangle Removal"))
                {
                    CollapseOwnedUndoGroupIfCurrent(
                        removeStrokeUndoGroup,
                        removeStrokeOwnsUndoGroup);
                    removeStrokeUndoGroup = -1;
                    removeStrokeOwnsUndoGroup = false;
                    return false;
                }
                removeStrokeMesh = workingMesh;
                removeStrokeMeshFilter = currentMeshFilter;
                removeStrokeStorage = currentStorage;
            }

            // EnsureUniqueForEditing clones every stream and index buffer, so the
            // immutable stroke snapshot must still describe the editable copy.
            if (!RemoveStrokeSnapshotMatches(removeStrokeMesh))
            {
                CollapseOwnedUndoGroupIfCurrent(
                    removeStrokeUndoGroup,
                    removeStrokeOwnsUndoGroup);
                removeStrokeUndoGroup = -1;
                removeStrokeOwnsUndoGroup = false;
                return false;
            }

            // Capture the complete pre-stroke Mesh once. Undo.RecordObject is
            // deferred/diff-based and can miss index-buffer changes that span
            // MouseDown/drag frames before the circle rim is shaped on MouseUp.
            Undo.RegisterCompleteObjectUndo(
                removeStrokeMesh,
                "Remove Triangles");
            if (removeStrokeStorage != null)
            {
                Undo.RecordObject(
                    removeStrokeStorage,
                    "Remove Triangles Data");
                removeStrokeStorage.ValidateGeneratedInteriorSubmeshes(
                    removeStrokeMesh);
            }
            removeStrokeMutationPrepared = true;
            return true;
        }

        void ApplyRemoveStrokeGeometry()
        {
            if (!PrepareRemoveStrokeMutation() ||
                removeStrokeTrianglesBySubmesh == null ||
                removeStrokeSubmeshTriangles == null ||
                removeStrokeSubmeshBaseVertices == null)
            {
                return;
            }

            bool changedAnySubmesh = false;
            for (int submesh = 0; submesh < removeStrokeSubmeshTriangles.Length; submesh++)
            {
                if (!removeStrokeTrianglesBySubmesh.TryGetValue(
                        submesh,
                        out HashSet<int> removedTriangles) ||
                    removedTriangles == null ||
                    removedTriangles.Count == 0)
                {
                    continue;
                }

                int[] source = removeStrokeSubmeshTriangles[submesh];
                if (source == null || source.Length < 3)
                    continue;

                List<int> kept = removeStrokeKeptTriangles[submesh];
                int requiredCapacity = Mathf.Max(
                    0,
                    source.Length - removedTriangles.Count * 3);
                if (kept == null)
                {
                    kept = new List<int>(requiredCapacity);
                    removeStrokeKeptTriangles[submesh] = kept;
                }
                else
                {
                    kept.Clear();
                    if (kept.Capacity < requiredCapacity)
                        kept.Capacity = requiredCapacity;
                }

                for (int triangle = 0; triangle < source.Length / 3; triangle++)
                {
                    if (removedTriangles.Contains(triangle))
                        continue;
                    int offset = triangle * 3;
                    kept.Add(source[offset]);
                    kept.Add(source[offset + 1]);
                    kept.Add(source[offset + 2]);
                }

                removeStrokeMesh.SetTriangles(
                    kept,
                    submesh,
                    false,
                    removeStrokeSubmeshBaseVertices[submesh]);
                changedAnySubmesh = true;
            }

            if (!changedAnySubmesh)
                return;

            // The collider intentionally keeps the pre-stroke topology until
            // release so the brush can continue raycasting across the new hole.
            // Uploading the mutable mesh here makes the renderer reflect every
            // MouseDown/MouseDrag event immediately.
            removeStrokeMesh.MarkModified();
            removeStrokeMesh.UploadMeshData(false);
            EditorUtility.SetDirty(removeStrokeMesh);
            removeStrokeGeometryApplied = true;
            cachedTriCount = CountTriangles(removeStrokeMesh);
            EditorApplication.QueuePlayerLoopUpdate();
            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        Mesh CreateRemoveStrokeSourceSnapshot(Mesh targetMesh)
        {
            if (targetMesh == null ||
                removeStrokeSubmeshTriangles == null ||
                removeStrokeSubmeshBaseVertices == null ||
                targetMesh.subMeshCount != removeStrokeSubmeshTriangles.Length)
            {
                return null;
            }

            Mesh snapshot = UnityEngine.Object.Instantiate(targetMesh);
            snapshot.name = targetMesh.name + " (Remove Source Snapshot)";
            for (int submesh = 0; submesh < removeStrokeSubmeshTriangles.Length; submesh++)
            {
                int[] source = removeStrokeSubmeshTriangles[submesh];
                if (source == null)
                    continue;
                snapshot.SetTriangles(
                    source,
                    submesh,
                    false,
                    removeStrokeSubmeshBaseVertices[submesh]);
            }
            return snapshot;
        }

        static bool IsRealBlendSolidWall(
            MeshFilter targetFilter,
            Mesh targetMesh,
            VertexPaintStorage targetStorage)
        {
            string objectName = targetFilter != null && targetFilter.gameObject != null
                ? targetFilter.gameObject.name
                : string.Empty;
            string meshName = targetMesh != null ? targetMesh.name : string.Empty;
            return targetStorage != null && targetStorage.IsGeneratedSolidWall ||
                   objectName.StartsWith(
                       "RealBlend Solid Wall",
                       System.StringComparison.OrdinalIgnoreCase) ||
                   meshName.StartsWith(
                       "RealBlend Solid Wall",
                       System.StringComparison.OrdinalIgnoreCase);
        }

        static void CopyRemoveResultIntoMesh(
            Mesh result,
            Mesh target,
            MeshFilter targetFilter)
        {
            if (result == null || target == null)
                return;
            if (result == target)
                throw new System.InvalidOperationException(
                    "The completed Remove mesh cannot be copied onto itself.");
            ValidateRemoveResultMesh(result);

            string targetName = target.name;
            HideFlags targetHideFlags = target.hideFlags;
            target.Clear(false);
            target.indexFormat = result.indexFormat;

            List<Vector3> positions = new List<Vector3>(result.vertexCount);
            result.GetVertices(positions);
            target.SetVertices(positions);

            if (result.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Normal))
            {
                List<Vector3> normals = new List<Vector3>(result.vertexCount);
                result.GetNormals(normals);
                target.SetNormals(normals);
            }
            if (result.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Tangent))
            {
                List<Vector4> tangents = new List<Vector4>(result.vertexCount);
                result.GetTangents(tangents);
                target.SetTangents(tangents);
            }
            if (result.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Color))
            {
                UnityEngine.Rendering.VertexAttributeFormat colorFormat =
                    result.GetVertexAttributeFormat(
                        UnityEngine.Rendering.VertexAttribute.Color);
                if (colorFormat ==
                    UnityEngine.Rendering.VertexAttributeFormat.UNorm8)
                {
                    List<Color32> colors = new List<Color32>(result.vertexCount);
                    result.GetColors(colors);
                    target.SetColors(colors);
                }
                else
                {
                    List<Color> colors = new List<Color>(result.vertexCount);
                    result.GetColors(colors);
                    target.SetColors(colors);
                }
            }

            for (int channel = 0; channel < 8; channel++)
                CopyRemoveUVChannel(result, target, channel);

            target.subMeshCount = result.subMeshCount;
            for (int submesh = 0; submesh < result.subMeshCount; submesh++)
            {
                uint sourceBaseVertex = result.GetBaseVertex(submesh);
                if (sourceBaseVertex > int.MaxValue)
                {
                    throw new System.InvalidOperationException(
                        "The completed Remove mesh has an unsupported base vertex.");
                }
                target.SetIndices(
                    result.GetIndices(submesh, false),
                    result.GetTopology(submesh),
                    submesh,
                    false,
                    (int)sourceBaseVertex);
            }
            target.bounds = result.bounds;
            target.name = targetName;
            target.hideFlags = targetHideFlags;

            // Use actual geometry setters rather than CopySerialized. Unity 6
            // otherwise keeps the previous native vertex/index buffers alive
            // until a later paint/sculpt action happens to rewrite one stream.
            target.UploadMeshData(false);
            target.MarkModified();
            EditorUtility.SetDirty(target);
            if (targetFilter != null)
                EditorUtility.SetDirty(targetFilter);
        }

        static void ValidateRemoveResultMesh(Mesh mesh)
        {
            if (!mesh.isReadable)
            {
                throw new System.InvalidOperationException(
                    "The completed Remove mesh is not readable.");
            }

            int vertexCount = mesh.vertexCount;
            if (!mesh.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Position) ||
                mesh.GetVertexAttributeFormat(
                    UnityEngine.Rendering.VertexAttribute.Position) !=
                    UnityEngine.Rendering.VertexAttributeFormat.Float32 ||
                mesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.Position) != 3 ||
                mesh.vertices.Length != vertexCount)
            {
                throw new System.InvalidOperationException(
                    "The completed Remove mesh has an invalid position stream.");
            }
            if (!mesh.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Normal) ||
                mesh.GetVertexAttributeFormat(
                    UnityEngine.Rendering.VertexAttribute.Normal) !=
                    UnityEngine.Rendering.VertexAttributeFormat.Float32 ||
                mesh.GetVertexAttributeDimension(
                    UnityEngine.Rendering.VertexAttribute.Normal) != 3 ||
                mesh.normals.Length != vertexCount)
            {
                throw new System.InvalidOperationException(
                    "The completed Remove mesh has an invalid normal stream.");
            }
            if (mesh.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Tangent) &&
                (mesh.GetVertexAttributeFormat(
                     UnityEngine.Rendering.VertexAttribute.Tangent) !=
                     UnityEngine.Rendering.VertexAttributeFormat.Float32 ||
                 mesh.GetVertexAttributeDimension(
                     UnityEngine.Rendering.VertexAttribute.Tangent) != 4 ||
                 mesh.tangents.Length != vertexCount))
            {
                throw new System.InvalidOperationException(
                    "The completed Remove mesh has an invalid tangent stream.");
            }
            if (mesh.HasVertexAttribute(
                    UnityEngine.Rendering.VertexAttribute.Color))
            {
                UnityEngine.Rendering.VertexAttributeFormat format =
                    mesh.GetVertexAttributeFormat(
                        UnityEngine.Rendering.VertexAttribute.Color);
                if ((format !=
                         UnityEngine.Rendering.VertexAttributeFormat.Float32 &&
                     format !=
                         UnityEngine.Rendering.VertexAttributeFormat.UNorm8) ||
                    mesh.GetVertexAttributeDimension(
                        UnityEngine.Rendering.VertexAttribute.Color) != 4)
                {
                    throw new System.InvalidOperationException(
                        "The completed Remove mesh has an unsupported color stream.");
                }

                if (format ==
                    UnityEngine.Rendering.VertexAttributeFormat.UNorm8)
                {
                    List<Color32> colors = new List<Color32>();
                    mesh.GetColors(colors);
                    if (colors.Count != vertexCount)
                    {
                        throw new System.InvalidOperationException(
                            "The completed Remove mesh has an incomplete color stream.");
                    }
                }
                else
                {
                    List<Color> colors = new List<Color>();
                    mesh.GetColors(colors);
                    if (colors.Count != vertexCount)
                    {
                        throw new System.InvalidOperationException(
                            "The completed Remove mesh has an incomplete color stream.");
                    }
                }
            }

            for (int channel = 0; channel < 8; channel++)
            {
                UnityEngine.Rendering.VertexAttribute attribute =
                    (UnityEngine.Rendering.VertexAttribute)(
                        (int)UnityEngine.Rendering.VertexAttribute.TexCoord0 +
                        channel);
                if (!mesh.HasVertexAttribute(attribute))
                    continue;
                int dimension = mesh.GetVertexAttributeDimension(attribute);
                if (mesh.GetVertexAttributeFormat(attribute) !=
                        UnityEngine.Rendering.VertexAttributeFormat.Float32 ||
                    dimension < 2 || dimension > 4)
                {
                    throw new System.InvalidOperationException(
                        $"The completed Remove mesh has an unsupported UV{channel} stream.");
                }
                List<Vector4> values = new List<Vector4>();
                mesh.GetUVs(channel, values);
                if (values.Count != vertexCount)
                {
                    throw new System.InvalidOperationException(
                        $"The completed Remove mesh has an incomplete UV{channel} stream.");
                }
            }
        }

        static void CopyRemoveUVChannel(
            Mesh source,
            Mesh target,
            int channel)
        {
            UnityEngine.Rendering.VertexAttribute attribute =
                (UnityEngine.Rendering.VertexAttribute)(
                    (int)UnityEngine.Rendering.VertexAttribute.TexCoord0 +
                    channel);
            if (!source.HasVertexAttribute(attribute))
                return;

            int dimension = source.GetVertexAttributeDimension(attribute);
            if (dimension == 2)
            {
                List<Vector2> values = new List<Vector2>(source.vertexCount);
                source.GetUVs(channel, values);
                target.SetUVs(channel, values);
            }
            else if (dimension == 3)
            {
                List<Vector3> values = new List<Vector3>(source.vertexCount);
                source.GetUVs(channel, values);
                target.SetUVs(channel, values);
            }
            else if (dimension == 4)
            {
                List<Vector4> values = new List<Vector4>(source.vertexCount);
                source.GetUVs(channel, values);
                target.SetUVs(channel, values);
            }
        }

        void ApplyRemoveInteriorMaterial(
            MeshFilter targetFilter,
            int revealSubmesh,
            int resultSubmeshCount)
        {
            if (!removeUseSeparateInteriorSubmesh ||
                revealSubmesh < 0 ||
                targetFilter == null)
            {
                return;
            }

            MeshRenderer renderer = targetFilter.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            Material[] materials = renderer.sharedMaterials;
            Material chosen = removeInteriorMaterial;
            if (chosen == null &&
                revealSubmesh < materials.Length &&
                materials[revealSubmesh] != null)
            {
                chosen = materials[revealSubmesh];
            }
            if (chosen == null && materials.Length > 0)
                chosen = materials[0];

            int requiredSlots = Mathf.Max(
                resultSubmeshCount,
                revealSubmesh + 1);
            bool changed = materials.Length < requiredSlots;
            if (changed)
                System.Array.Resize(ref materials, requiredSlots);
            if (materials[revealSubmesh] != chosen)
            {
                materials[revealSubmesh] = chosen;
                changed = true;
            }
            if (!changed)
                return;

            Undo.RecordObject(renderer, "Remove Interior Material");
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        int ResolveRemoveRevealSubmesh(
            MeshFilter targetFilter,
            Mesh sourceMesh,
            VertexPaintStorage storage)
        {
            if (!removeUseSeparateInteriorSubmesh || sourceMesh == null)
                return -1;

            MeshRenderer renderer = targetFilter != null
                ? targetFilter.GetComponent<MeshRenderer>()
                : null;
            Material[] materials = renderer != null
                ? renderer.sharedMaterials
                : System.Array.Empty<Material>();
            int trackedCount = storage != null
                ? storage.GeneratedInteriorSubmeshCount
                : 0;
            for (int trackedIndex = trackedCount - 1;
                 trackedIndex >= 0;
                 trackedIndex--)
            {
                if (!storage.TryGetGeneratedInteriorSubmeshAt(
                        sourceMesh,
                        trackedIndex,
                        out int trackedSubmesh))
                {
                    continue;
                }
                bool materialMatches = removeInteriorMaterial == null ||
                    trackedSubmesh < materials.Length &&
                    materials[trackedSubmesh] == removeInteriorMaterial;
                if (materialMatches &&
                    sourceMesh.GetTopology(trackedSubmesh) ==
                        UnityEngine.MeshTopology.Triangles)
                {
                    return trackedSubmesh;
                }
            }
            return sourceMesh.subMeshCount;
        }

        void CommitRemoveStroke()
        {
            if (!removeStrokeActive)
                return;

            Mesh targetMesh = removeStrokeMesh;
            MeshFilter targetFilter = removeStrokeMeshFilter;
            VertexPaintStorage targetStorage = removeStrokeStorage;
            int undoGroup = removeStrokeUndoGroup;
            bool ownsUndoGroup = removeStrokeOwnsUndoGroup;
            bool geometryApplied = removeStrokeGeometryApplied;
            bool cancelWallRemoveCommit = false;
            // -1 means this was still a controlled Remove topology rewrite, so
            // preserve/re-sign verified prior interior slots without adding one.
            int generatedInteriorSubmeshToCommit = -1;

            try
            {
                if (!geometryApplied || targetMesh == null || targetFilter == null)
                    return;

                if (brushShape == BrushShape.Square &&
                    removeThroughWall &&
                    removeStrokeSamples != null &&
                    removeStrokeSamples.Count > 0 &&
                    IsRealBlendSolidWall(targetFilter, targetMesh, targetStorage))
                {
                    Mesh sourceSnapshot = CreateRemoveStrokeSourceSnapshot(targetMesh);
                    Mesh openingResult = null;
                    int revealSubmesh = ResolveRemoveRevealSubmesh(
                        targetFilter,
                        sourceSnapshot,
                        targetStorage);
                    try
                    {
                        if (sourceSnapshot != null &&
                            targetFilter.gameObject != null &&
                            !sourceSnapshot.name.StartsWith(
                                "RealBlend Solid Wall",
                                System.StringComparison.OrdinalIgnoreCase))
                        {
                            // The topology validator remains authoritative;
                            // this only carries the generator identity across a
                            // customer-renamed mesh asset when the wall object
                            // itself still has its RealBlend creation name.
                            sourceSnapshot.name = targetFilter.gameObject.name;
                        }
                        RealBlendSculptRemoveSample openingSample =
                            removeStrokeSamples[removeStrokeSamples.Count - 1];
                        RealBlendSculptRemoveReport openingReport = default;
                        string openingError =
                            "The pre-stroke wall snapshot could not be reconstructed.";
                        bool usedEditedGridRecovery = false;
                        bool openingBuilt = sourceSnapshot != null &&
                                            RealBlendWallOpeningUtility.TryCutOpening(
                                                sourceSnapshot,
                                                targetFilter.transform.localToWorldMatrix,
                                                openingSample,
                                                revealSubmesh,
                                                out openingResult,
                                                out openingReport,
                                                out openingError);
                        if (!openingBuilt &&
                            sourceSnapshot != null &&
                            targetStorage != null)
                        {
                            string strictGridError = openingError;
                            if (openingResult != null)
                            {
                                UnityEngine.Object.DestroyImmediate(openingResult);
                                openingResult = null;
                            }

                            if (RealBlendEditedWallOpeningUtility.TryCutOpening(
                                    sourceSnapshot,
                                    targetStorage,
                                    targetFilter.transform.localToWorldMatrix,
                                    openingSample,
                                    revealSubmesh,
                                    out openingResult,
                                    out openingReport,
                                    out string editedGridError))
                            {
                                openingBuilt = true;
                                usedEditedGridRecovery = true;
                            }
                            else
                            {
                                bool hasInsertedTopology =
                                    targetStorage.baseVertices != null &&
                                    sourceSnapshot.vertexCount !=
                                    targetStorage.baseVertices.Length;
                                openingError = hasInsertedTopology &&
                                               !string.IsNullOrEmpty(editedGridError)
                                    ? editedGridError
                                    : !string.IsNullOrEmpty(strictGridError)
                                        ? strictGridError
                                        : editedGridError;
                            }
                        }

                        if (openingBuilt)
                        {
                            CopyRemoveResultIntoMesh(
                                openingResult,
                                targetMesh,
                                targetFilter);
                            if (removeUseSeparateInteriorSubmesh &&
                                revealSubmesh >= 0)
                            {
                                generatedInteriorSubmeshToCommit =
                                    revealSubmesh;
                            }
                            ApplyRemoveInteriorMaterial(
                                targetFilter,
                                revealSubmesh,
                                openingResult.subMeshCount);
                            removeTopologyStatus =
                                (usedEditedGridRecovery
                                    ? "Cut through intact paired grid cells on the edited RealBlend wall and preserved its other Boolean geometry. Added "
                                    : "Cut through the aligned RealBlend wall and added ") +
                                openingReport.bridgeTriangles +
                                " reveal triangles.";
                            removeTopologyStatusType = MessageType.Info;
                            if (IsOwnedUndoGroupCurrent(
                                    undoGroup,
                                    ownsUndoGroup))
                            {
                                Undo.SetCurrentGroupName("Cut Opening Through Wall");
                            }
                        }
                        else
                        {
                            if (sourceSnapshot != null)
                            {
                                CopyRemoveResultIntoMesh(
                                    sourceSnapshot,
                                    targetMesh,
                                    targetFilter);
                                targetMesh.UploadMeshData(false);
                                cancelWallRemoveCommit = true;
                            }
                            removeTopologyStatus =
                                "The aligned wall opening was not applied" +
                                (cancelWallRemoveCommit
                                    ? ", and the pre-stroke wall was restored. "
                                    : "; the normal surface Remove result was kept because its source snapshot was unavailable. ") +
                                (string.IsNullOrEmpty(openingError)
                                    ? "The wall topology did not pass validation."
                                    : openingError) +
                                (cancelWallRemoveCommit
                                    ? " Disable Cut Through Solid Walls to use surface-only Remove intentionally."
                                    : string.Empty);
                            removeTopologyStatusType = MessageType.Warning;
                        }
                    }
                    finally
                    {
                        if (openingResult != null)
                            UnityEngine.Object.DestroyImmediate(openingResult);
                        if (sourceSnapshot != null)
                            UnityEngine.Object.DestroyImmediate(sourceSnapshot);
                    }
                }

                if (cancelWallRemoveCommit)
                    return;

                if (brushShape == BrushShape.Circle &&
                    shapeCircularRemoveBoundary &&
                    removeStrokeSamples != null &&
                    removeStrokeSamples.Count > 0)
                {
                    bool hadNormals = targetMesh.normals != null &&
                                      targetMesh.normals.Length == targetMesh.vertexCount;
                    bool hadTangents = targetMesh.tangents != null &&
                                       targetMesh.tangents.Length == targetMesh.vertexCount;
                    if (RealBlendCircleRemoveBoundaryUtility.TryShapeBoundary(
                            targetMesh,
                            targetFilter.transform.localToWorldMatrix,
                            removeStrokeSubmeshTriangles,
                            removeStrokeSubmeshBaseVertices,
                            removeStrokeTrianglesBySubmesh,
                            removeStrokeSamples,
                            out RealBlendCircleRemoveBoundaryReport circleReport,
                            out string circleError))
                    {
                        if (hadNormals)
                            targetMesh.RecalculateNormals();
                        if (hadTangents && hadNormals)
                            targetMesh.RecalculateTangents();
                        removeTopologyStatus =
                            "Rounded " + circleReport.validLoops +
                            " existing boundary loop" +
                            (circleReport.validLoops == 1 ? string.Empty : "s") +
                            " without adding topology.";
                        if (circleReport.appliedBlend < 0.999f)
                        {
                            removeTopologyStatus +=
                                " Safety limited the reshape to " +
                                Mathf.RoundToInt(circleReport.appliedBlend * 100f) +
                                "%.";
                        }
                        removeTopologyStatusType = MessageType.Info;
                        if (IsOwnedUndoGroupCurrent(
                                undoGroup,
                                ownsUndoGroup))
                        {
                            Undo.SetCurrentGroupName("Remove With Shaped Boundary");
                        }
                    }
                    else
                    {
                        removeTopologyStatus =
                            "The existing circle rim was kept because it could not be reshaped safely. " +
                            (string.IsNullOrEmpty(circleError)
                                ? "The index-only Remove result is still valid."
                                : circleError);
                        removeTopologyStatusType = MessageType.Warning;
                    }
                }

                targetMesh.RecalculateBounds();
                targetMesh.MarkModified();
                targetMesh.UploadMeshData(false);
                EditorUtility.SetDirty(targetMesh);
                CommitSculptMeshState(
                    targetFilter,
                    targetMesh,
                    targetStorage,
                    "Remove Triangles Data",
                    generatedInteriorSubmeshToCommit);

                if (targetMesh == workingMesh)
                {
                    currentVertices = workingMesh.vertices;
                    meshIsDirty = true;
                    BuildSpatialBuckets();
                    meshIsDirty = false;
                    UpdateStats();
                }
            }
            finally
            {
                ResetRemoveStrokeState();
                CollapseOwnedUndoGroupIfCurrent(undoGroup, ownsUndoGroup);
                EditorApplication.QueuePlayerLoopUpdate();
                _owner?.Repaint();
                SceneView.RepaintAll();
            }
        }

        void ResetRemoveStrokeState()
        {
            removeStrokeActive = false;
            removeStrokeMesh = null;
            removeStrokeMeshFilter = null;
            removeStrokeStorage = null;
            removeStrokeSubmeshTriangles = null;
            removeStrokeSubmeshBaseVertices = null;
            removeStrokeKeptTriangles = null;
            removeStrokeVertexCount = 0;
            removeStrokeTrianglesBySubmesh = null;
            removeStrokeSelectedVertices = null;
            removeStrokeHasLastSample = false;
            removeStrokeLastPoint = Vector3.zero;
            removeStrokeLastNormal = Vector3.up;
            removeStrokeUndoGroup = -1;
            removeStrokeOwnsUndoGroup = false;
            removeStrokeMutationPrepared = false;
            removeStrokeGeometryApplied = false;
            removeStrokeSamples = null;
        }

        static void CollapseOwnedUndoGroupIfCurrent(
            int ownedGroup,
            bool ownsUndoGroup)
        {
            // CollapseUndoOperations merges every newer group down to the
            // supplied ID. A restored default (0), missed MouseUp, or focus
            // interruption must therefore never be allowed to collapse an old
            // section of the user's global Unity Undo history.
            if (!IsOwnedUndoGroupCurrent(ownedGroup, ownsUndoGroup))
                return;

            Undo.CollapseUndoOperations(ownedGroup);
            // Explicitly close the RealBlend transaction so a later editor
            // operation cannot be appended to the just-collapsed stroke.
            Undo.IncrementCurrentGroup();
        }

        static bool IsOwnedUndoGroupCurrent(
            int ownedGroup,
            bool ownsUndoGroup)
        {
            return ownsUndoGroup &&
                   ownedGroup > 0 &&
                   !Undo.isProcessing &&
                   Undo.GetCurrentGroup() == ownedGroup;
        }

        void BeginPendingSculptMutation(string undoName)
        {
            if (sculptOwnsUndoGroup && sculptMutationUndoGroup > 0)
                return;

            sculptMutationUndoGroup = -1;
            sculptOwnsUndoGroup = false;
            Undo.IncrementCurrentGroup();
            sculptMutationUndoGroup = Undo.GetCurrentGroup();
            sculptOwnsUndoGroup = sculptMutationUndoGroup > 0;
            Undo.SetCurrentGroupName(undoName);
        }

        void CancelEmptyPendingSculptMutation()
        {
            if (!sculptMeshStateCommitPending)
            {
                sculptMutationUndoGroup = -1;
                sculptOwnsUndoGroup = false;
            }
        }

        void FinalizePendingSculptStroke()
        {
            int undoGroup = sculptMutationUndoGroup;
            bool ownsUndoGroup = sculptOwnsUndoGroup;
            // Clear ownership before doing any work. If rebuilding bounds,
            // storage, or colliders throws, no later callback can reuse this
            // transaction and collapse a now-stale range of editor history.
            sculptMutationUndoGroup = -1;
            sculptOwnsUndoGroup = false;

            if (!sculptMeshStateCommitPending)
            {
                CollapseOwnedUndoGroupIfCurrent(undoGroup, ownsUndoGroup);
                return;
            }
            if (workingMesh == null || currentMeshFilter == null)
            {
                // There is no authoritative target left to finalize. More
                // importantly, retaining this group would allow a later event
                // to collapse unrelated operations down to an obsolete ID.
                sculptMeshStateCommitPending = false;
                return;
            }

            try
            {
                bool hadTangents = workingMesh.tangents != null &&
                                   workingMesh.tangents.Length == workingMesh.vertexCount;
                workingMesh.RecalculateNormals();
                if (hadTangents)
                    workingMesh.RecalculateTangents();
                workingMesh.RecalculateBounds();
                currentVertices = workingMesh.vertices;
                meshIsDirty = true;
                BuildSpatialBuckets();
                meshIsDirty = false;
                UpdateStats();
                CommitSculptMeshState(
                    currentMeshFilter,
                    workingMesh,
                    currentStorage,
                    "Sculpt Mesh Data");
            }
            finally
            {
                sculptMeshStateCommitPending = false;
                CollapseOwnedUndoGroupIfCurrent(undoGroup, ownsUndoGroup);
                _owner?.Repaint();
                SceneView.RepaintAll();
            }
        }

        void CommitSculptMeshState(
            MeshFilter meshFilter,
            Mesh mesh,
            VertexPaintStorage storage,
            string undoName,
            int generatedInteriorSubmesh = int.MinValue,
            bool clearGeneratedInteriorSubmeshes = false)
        {
            if (meshFilter == null || mesh == null)
                return;

            if (storage != null)
            {
                Undo.RecordObject(storage, undoName);
                if (clearGeneratedInteriorSubmeshes)
                {
                    storage.ClearGeneratedInteriorSubmeshes();
                }
                else if (generatedInteriorSubmesh != int.MinValue)
                {
                    storage.CommitGeneratedInteriorSubmeshTopology(
                        mesh,
                        generatedInteriorSubmesh);
                }
                storage.SetOwnedEditableMesh(mesh);
                storage.SaveCurrentState(mesh);
                EditorUtility.SetDirty(storage);
                PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
            }

            RefreshSculptMeshColliders(meshFilter, mesh, undoName);

            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(meshFilter.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
            if (meshFilter.gameObject.scene.IsValid() && meshFilter.gameObject.scene.isLoaded)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    meshFilter.gameObject.scene);
            }

            if (mesh == workingMesh)
                sculptMeshStateCommitPending = false;
        }

        void RefreshSculptMeshColliders(
            MeshFilter meshFilter,
            Mesh mesh,
            string undoName)
        {
            if (meshFilter == null || mesh == null)
                return;

            GameObject colliderRoot = currentSelection != null &&
                                      (meshFilter.transform == currentSelection.transform ||
                                       meshFilter.transform.IsChildOf(currentSelection.transform))
                ? currentSelection
                : meshFilter.gameObject;
            List<MeshCollider> exactColliders = CollectMatchingMeshColliders(
                meshFilter,
                colliderRoot,
                mesh);
            bool hasCollisionTriangles = CountTriangles(mesh) > 0;
            for (int i = 0; i < exactColliders.Count; i++)
            {
                MeshCollider exactCollider = exactColliders[i];
                if (exactCollider == null)
                    continue;
                if (!string.IsNullOrEmpty(undoName))
                {
                    Undo.RecordObject(exactCollider, undoName);
                }
                exactCollider.sharedMesh = null;
                if (hasCollisionTriangles)
                    exactCollider.sharedMesh = mesh;
                if (!string.IsNullOrEmpty(undoName))
                {
                    EditorUtility.SetDirty(exactCollider);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(exactCollider);
                }
            }
        }

        // =================================================================================
        // NEW: REFINE (1 -> 4 Subdivision)
        // =================================================================================

        struct EdgeKey
        {
            public int v1;
            public int v2;
            public EdgeKey(int a, int b)
            {
                v1 = Mathf.Min(a, b);
                v2 = Mathf.Max(a, b);
            }
            public override bool Equals(object obj) => obj is EdgeKey k && k.v1 == v1 && k.v2 == v2;
            public override int GetHashCode() => v1 * 397 ^ v2;
        }

        void RefineMesh_Midpoint(Vector3 hitPoint, Vector3 hitNormal)
        {
            bool hasBlendShapes = workingMesh != null && workingMesh.blendShapeCount > 0;
            bool hasSkinning = workingMesh != null &&
                               ((workingMesh.bindposes != null && workingMesh.bindposes.Length > 0) ||
                                workingMesh.HasVertexAttribute(
                                    UnityEngine.Rendering.VertexAttribute.BlendWeight) ||
                                workingMesh.HasVertexAttribute(
                                    UnityEngine.Rendering.VertexAttribute.BlendIndices));
            if (hasBlendShapes || hasSkinning)
            {
                string unsupportedStreams = hasBlendShapes && hasSkinning
                    ? "blend shapes and skinning/bone-weight data"
                    : hasBlendShapes
                        ? "blend shapes"
                        : "skinning/bone-weight data";
                string message =
                    $"Refine was not applied because this mesh contains {unsupportedStreams}. " +
                    "Changing topology would discard those streams. Use a static MeshFilter mesh without them.";
                Debug.LogWarning($"RealBlend: {message}", currentMeshFilter);
                EditorUtility.DisplayDialog("Refine Not Supported", message, "OK");
                return;
            }

            Transform mt = currentMeshFilter.transform;
            Vector3[] verts = workingMesh.vertices;
            Vector3[] normals = workingMesh.normals;
            Vector4[] tangents = workingMesh.tangents;
            Color[] cols = workingMesh.colors;
            UnityEngine.Rendering.IndexFormat sourceIndexFormat = workingMesh.indexFormat;
            bool hasNormals = normals != null && normals.Length == verts.Length;
            bool hasTangents = tangents != null && tangents.Length == verts.Length;
            bool hasColors = cols != null && cols.Length == verts.Length;

            const int uvChannelCount = 8;
            List<Vector2>[] sourceUVChannels = new List<Vector2>[uvChannelCount];
            for (int channel = 0; channel < uvChannelCount; channel++)
            {
                List<Vector2> channelUVs = new List<Vector2>();
                workingMesh.GetUVs(channel, channelUVs);
                if (channelUVs.Count == verts.Length)
                    sourceUVChannels[channel] = channelUVs;
            }

            int sourceSubmeshCount = workingMesh.subMeshCount;
            UnityEngine.MeshTopology[] sourceTopologies =
                new UnityEngine.MeshTopology[sourceSubmeshCount];
            int[][] sourceSubmeshIndices = new int[sourceSubmeshCount][];
            for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
            {
                sourceTopologies[submesh] = workingMesh.GetTopology(submesh);
                sourceSubmeshIndices[submesh] = workingMesh.GetIndices(submesh, true);
            }

            HashSet<EdgeKey> edgesToSplit = new HashSet<EdgeKey>();

            Vector3 brushRight = Vector3.right;
            Vector3 brushUp = Vector3.up;
            float halfW = squareBrushWidth * 0.5f;
            float halfH = squareBrushHeight * 0.5f;
            if (brushShape == BrushShape.Square)
            {
                brushRight = Vector3.Cross(hitNormal, Vector3.up);
                if (brushRight.sqrMagnitude < 0.001f) brushRight = Vector3.Cross(hitNormal, Vector3.right);
                brushRight.Normalize();
                brushUp = Vector3.Cross(brushRight, hitNormal).normalized;
            }

            for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
            {
                if (sourceTopologies[submesh] != UnityEngine.MeshTopology.Triangles)
                    continue;
                int[] indices = sourceSubmeshIndices[submesh];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i1 = indices[i];
                    int i2 = indices[i + 1];
                    int i3 = indices[i + 2];
                    if (i1 < 0 || i2 < 0 || i3 < 0 ||
                        i1 >= verts.Length || i2 >= verts.Length || i3 >= verts.Length)
                    {
                        continue;
                    }

                    Vector3 p1 = mt.TransformPoint(verts[i1]);
                    Vector3 p2 = mt.TransformPoint(verts[i2]);
                    Vector3 p3 = mt.TransformPoint(verts[i3]);
                    Vector3 center = (p1 + p2 + p3) / 3.0f;

                    bool inside;
                    if (brushShape == BrushShape.Circle)
                    {
                        inside = Vector3.Distance(hitPoint, center) < brushSize;
                    }
                    else
                    {
                        Vector3 delta = center - hitPoint;
                        float u = Vector3.Dot(delta, brushRight);
                        float v = Vector3.Dot(delta, brushUp);
                        inside = Mathf.Abs(u) <= halfW && Mathf.Abs(v) <= halfH;
                    }

                    if (inside)
                    {
                        edgesToSplit.Add(new EdgeKey(i1, i2));
                        edgesToSplit.Add(new EdgeKey(i2, i3));
                        edgesToSplit.Add(new EdgeKey(i3, i1));
                    }
                }
            }

            if (edgesToSplit.Count == 0) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine Mesh");
            if (!EnsureSculptMeshEditable("Prepare Unique Mesh for Refinement"))
            {
                Undo.CollapseUndoOperations(undoGroup);
                return;
            }

            if (currentStorage != null)
            {
                Undo.RecordObject(currentStorage, "Refine Data");
                currentStorage.ValidateGeneratedInteriorSubmeshes(workingMesh);
            }
            Undo.RecordObject(workingMesh, "Refine Mesh");

            List<Vector3> newVerts = new List<Vector3>(verts);
            List<Vector3> newNormals = hasNormals ? new List<Vector3>(normals) : null;
            List<Vector4> newTangents = hasTangents ? new List<Vector4>(tangents) : null;
            List<Color> newColors = hasColors ? new List<Color>(cols) : null;
            List<Vector2>[] newUVChannels = new List<Vector2>[uvChannelCount];
            for (int channel = 0; channel < uvChannelCount; channel++)
            {
                if (sourceUVChannels[channel] != null)
                    newUVChannels[channel] = new List<Vector2>(sourceUVChannels[channel]);
            }

            Dictionary<EdgeKey, int> edgeMidpoints = new Dictionary<EdgeKey, int>();

            int GetMidpoint(int a, int b)
            {
                EdgeKey key = new EdgeKey(a, b);
                if (edgeMidpoints.TryGetValue(key, out int idx)) return idx;

                Vector3 midPos = (verts[a] + verts[b]) * 0.5f;
                newVerts.Add(midPos);

                if (newNormals != null)
                {
                    Vector3 midNormal = normals[a] + normals[b];
                    if (midNormal.sqrMagnitude > 0.0000001f)
                        midNormal.Normalize();
                    newNormals.Add(midNormal);
                }
                if (newTangents != null)
                {
                    Vector4 tangent = Vector4.Lerp(tangents[a], tangents[b], 0.5f);
                    Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z);
                    if (direction.sqrMagnitude > 0.0000001f)
                        direction.Normalize();
                    tangent.x = direction.x;
                    tangent.y = direction.y;
                    tangent.z = direction.z;
                    tangent.w = Mathf.Abs(tangents[a].w + tangents[b].w) > 0.000001f
                        ? Mathf.Sign(tangents[a].w + tangents[b].w)
                        : tangents[a].w;
                    newTangents.Add(tangent);
                }
                if (newColors != null)
                    newColors.Add(Color.Lerp(cols[a], cols[b], 0.5f));
                for (int channel = 0; channel < uvChannelCount; channel++)
                {
                    if (newUVChannels[channel] != null)
                    {
                        newUVChannels[channel].Add(
                            (sourceUVChannels[channel][a] + sourceUVChannels[channel][b]) * 0.5f);
                    }
                }

                int newIdx = newVerts.Count - 1;
                edgeMidpoints[key] = newIdx;
                return newIdx;
            }

            List<int>[] refinedSubmeshIndices = new List<int>[sourceSubmeshCount];
            for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
            {
                int[] indices = sourceSubmeshIndices[submesh];
                List<int> output = new List<int>(indices.Length * 2);
                refinedSubmeshIndices[submesh] = output;
                if (sourceTopologies[submesh] != UnityEngine.MeshTopology.Triangles)
                {
                    output.AddRange(indices);
                    continue;
                }

                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i1 = indices[i];
                    int i2 = indices[i + 1];
                    int i3 = indices[i + 2];
                    EdgeKey e1 = new EdgeKey(i1, i2);
                    EdgeKey e2 = new EdgeKey(i2, i3);
                    EdgeKey e3 = new EdgeKey(i3, i1);

                    bool split1 = edgesToSplit.Contains(e1);
                    bool split2 = edgesToSplit.Contains(e2);
                    bool split3 = edgesToSplit.Contains(e3);
                    int splitCount = (split1 ? 1 : 0) +
                                     (split2 ? 1 : 0) +
                                     (split3 ? 1 : 0);

                    if (splitCount == 0)
                    {
                        output.Add(i1); output.Add(i2); output.Add(i3);
                    }
                    else if (splitCount == 3)
                    {
                        int m1 = GetMidpoint(i1, i2);
                        int m2 = GetMidpoint(i2, i3);
                        int m3 = GetMidpoint(i3, i1);
                        output.Add(i1); output.Add(m1); output.Add(m3);
                        output.Add(m1); output.Add(i2); output.Add(m2);
                        output.Add(m3); output.Add(m2); output.Add(i3);
                        output.Add(m1); output.Add(m2); output.Add(m3);
                    }
                    else if (splitCount == 1)
                    {
                        int a;
                        int b;
                        int c;
                        if (split1) { a = i1; b = i2; c = i3; }
                        else if (split2) { a = i2; b = i3; c = i1; }
                        else { a = i3; b = i1; c = i2; }
                        int midpoint = GetMidpoint(a, b);
                        output.Add(c); output.Add(a); output.Add(midpoint);
                        output.Add(c); output.Add(midpoint); output.Add(b);
                    }
                    else
                    {
                        int m1 = split1 ? GetMidpoint(i1, i2) : -1;
                        int m2 = split2 ? GetMidpoint(i2, i3) : -1;
                        int m3 = split3 ? GetMidpoint(i3, i1) : -1;
                        if (!split1)
                        {
                            output.Add(i3); output.Add(m3); output.Add(m2);
                            output.Add(i1); output.Add(i2); output.Add(m2);
                            output.Add(m2); output.Add(m3); output.Add(i1);
                        }
                        else if (!split2)
                        {
                            output.Add(i1); output.Add(m1); output.Add(m3);
                            output.Add(i2); output.Add(i3); output.Add(m3);
                            output.Add(m3); output.Add(m1); output.Add(i2);
                        }
                        else
                        {
                            output.Add(i2); output.Add(m2); output.Add(m1);
                            output.Add(i3); output.Add(i1); output.Add(m1);
                            output.Add(m1); output.Add(m2); output.Add(i3);
                        }
                    }
                }
            }

            workingMesh.Clear(false);
            workingMesh.indexFormat = sourceIndexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ||
                                      newVerts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            workingMesh.vertices = newVerts.ToArray();
            if (newNormals != null && newNormals.Count == newVerts.Count)
                workingMesh.normals = newNormals.ToArray();
            if (newTangents != null && newTangents.Count == newVerts.Count)
                workingMesh.tangents = newTangents.ToArray();
            if (newColors != null && newColors.Count == newVerts.Count)
                workingMesh.colors = newColors.ToArray();
            for (int channel = 0; channel < uvChannelCount; channel++)
            {
                if (newUVChannels[channel] != null &&
                    newUVChannels[channel].Count == newVerts.Count)
                {
                    workingMesh.SetUVs(channel, newUVChannels[channel]);
                }
            }

            workingMesh.subMeshCount = sourceSubmeshCount;
            for (int submesh = 0; submesh < sourceSubmeshCount; submesh++)
            {
                workingMesh.SetIndices(
                    refinedSubmeshIndices[submesh].ToArray(),
                    sourceTopologies[submesh],
                    submesh,
                    false);
            }

            if (newNormals == null)
                workingMesh.RecalculateNormals();
            if (newTangents == null && newUVChannels[0] != null)
                workingMesh.RecalculateTangents();
            workingMesh.RecalculateBounds();

            currentVertices = workingMesh.vertices;
            CommitSculptMeshState(
                currentMeshFilter,
                workingMesh,
                currentStorage,
                "Refine Data",
                -1);
            Undo.CollapseUndoOperations(undoGroup);
            meshIsDirty = true;
            BuildSpatialBuckets();
            meshIsDirty = false;
            UpdateStats();
            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        // =================================================================================
        // NEW: REVERT TOPOLOGY + KEEP PAINT
        // =================================================================================

        static void ApplyStoredSubmeshTriangles(
            Mesh mesh,
            int[] triangles,
            int[] submeshIndexCounts)
        {
            int[] safeTriangles = triangles ?? System.Array.Empty<int>();
            bool valid = submeshIndexCounts != null && submeshIndexCounts.Length > 0;
            int total = 0;
            if (valid)
            {
                for (int submesh = 0; submesh < submeshIndexCounts.Length; submesh++)
                {
                    int count = submeshIndexCounts[submesh];
                    if (count < 0 || count % 3 != 0 || total > safeTriangles.Length - count)
                    {
                        valid = false;
                        break;
                    }
                    total += count;
                }
                valid &= total == safeTriangles.Length;
            }

            if (!valid)
            {
                mesh.triangles = safeTriangles;
                return;
            }

            mesh.subMeshCount = submeshIndexCounts.Length;
            int offset = 0;
            for (int submesh = 0; submesh < submeshIndexCounts.Length; submesh++)
            {
                int count = submeshIndexCounts[submesh];
                int[] indices = new int[count];
                if (count > 0)
                    System.Array.Copy(safeTriangles, offset, indices, 0, count);
                mesh.SetTriangles(indices, submesh, false);
                offset += count;
            }
        }

        void RevertTopologyKeepPaint()
        {
            if (currentStorage == null || !currentStorage.hasOriginalData || currentStorage.originalVertices == null)
            {
                EditorUtility.DisplayDialog("Error", "No original topology data found stored on this object.", "OK");
                return;
            }

            if (EditorUtility.DisplayDialog("Revert & Project?", "This will revert the mesh to its original shape/topology but attempt to keep your painted colors.\n\nVertex positions will be reset.", "Revert", "Cancel"))
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Revert Keep Paint");
                if (!EnsureSculptMeshEditable("Prepare Unique Mesh for Topology Revert"))
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    return;
                }
                Undo.RecordObject(workingMesh, "Revert Keep Paint");

                Vector3[] highResVerts = workingMesh.vertices;
                Color[] highResColors = workingMesh.colors;

                Vector3[] targetVerts = currentStorage.originalVertices.ToArray();
                int[] targetTris = currentStorage.originalTriangles != null
                    ? currentStorage.originalTriangles.ToArray()
                    : System.Array.Empty<int>();
                int[] targetSubmeshIndexCounts = currentStorage.baseSubmeshIndexCounts != null
                    ? currentStorage.baseSubmeshIndexCounts.ToArray()
                    : null;
                Vector2[] targetUVs = currentStorage.originalUVs != null
                    ? currentStorage.originalUVs.ToArray()
                    : null;
                Color[] newColors = new Color[targetVerts.Length];

                for (int i = 0; i < targetVerts.Length; i++)
                {
                    float closestDist = float.MaxValue;
                    int closestIndex = -1;

                    for (int j = 0; j < highResVerts.Length; j++)
                    {
                        float dist = (targetVerts[i] - highResVerts[j]).sqrMagnitude;
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestIndex = j;
                        }
                    }

                    if (closestIndex != -1 && highResColors != null && highResColors.Length > closestIndex)
                    {
                        newColors[i] = highResColors[closestIndex];
                    }
                    else
                    {
                        newColors[i] = Color.white;
                    }
                }

                workingMesh.Clear(false);
                workingMesh.indexFormat = targetVerts.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                workingMesh.vertices = targetVerts;
                ApplyStoredSubmeshTriangles(
                    workingMesh,
                    targetTris,
                    targetSubmeshIndexCounts);
                if (targetUVs != null && targetUVs.Length == targetVerts.Length)
                    workingMesh.uv = targetUVs;
                workingMesh.colors = newColors;

                workingMesh.RecalculateNormals();
                RealBlendMeshUVUtility.GenerateLightmapUVs(workingMesh);
                workingMesh.RecalculateTangents();
                workingMesh.RecalculateBounds();

                currentVertices = targetVerts;
                CommitSculptMeshState(
                    currentMeshFilter,
                    workingMesh,
                    currentStorage,
                    "Revert Keep Paint Data",
                    int.MinValue,
                    true);
                Undo.CollapseUndoOperations(undoGroup);
                meshIsDirty = true;
                BuildSpatialBuckets();
                meshIsDirty = false;
                UpdateStats();
                _owner?.Repaint();
                SceneView.RepaintAll();
            }
        }
    }
}
