using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace RealBlend
{
    [System.Serializable]
    public class VertexPainter_PaintTab : IVertexPainterTab
    {
        [System.NonSerialized] private EditorWindow _owner;
        private const string PaletteRelativeDirectory = "Assets/RealBlend/VertexColorPalettes";
        private const string PaletteLastUsedNameKey = "RealBlend_LastVertexColorPaletteName";
        private const string VertexColorPreviewShaderName = "RealBlend/Vertex Color Preview";
        private const string VertexColorPreviewShaderAssetPath = "Assets/RealBlend/Art/Shaders/VertexColorPreview.shader";
        private const string TextureArrayPreviewShaderAssetPath = "Assets/RealBlend/Editor/TextureArrayLayerPreview.shader";
        private const float PaintTabBottomScrollPadding = 100f;
        private const int OptimizedLayerPreviewCount = 3;
        private const int OptimizedLayerPreviewMaxSize = 128;
        private const double OptimizedLayerPreviewInitialRetrySeconds = 0.5d;
        private const double OptimizedLayerPreviewMaxRetrySeconds = 8d;
        internal const string FloodFillUndoName = "RealBlend Vertex Paint Flood Fill";

        private static readonly string[] OptimizedSlicePropertyNames =
        {
            "_BaseSlice",
            "_Layer1Slice",
            "_Layer2Slice"
        };

        public enum PaintLayer { Base_Rock = 0, Layer1 = 1, Layer2 = 2, Wetness = 3, Variation = 4 }
        public enum PaintWorkflow { LayeredBlend = 0, VertexColor = 1 }
        public enum PaintTargetMode { Automatic = 0, SelectedSubmesh = 1 }
        private enum VertexColorPreviewMode { RGB = 0, Alpha = 1 }
        private enum Channel { None, R, G, B, A }
        private enum VertexColorTarget { RGBA, RGB, R, G, B, A }

        // --- Brush Settings ---
        public float brushSize = 1.0f;
        public float brushStrength = 0.5f;
        public float brushHardness = .5f;
        public float mouseSensitivity = 1.0f; // Normalized 0..1 (maps to 0..0.5 drag strength multiplier)
        public Texture2D brushAlpha;
        public float brushRotation = 0f;
        public bool invertBrushAlpha = false;
        public bool showPreview = true;
        public int paintRaycastLayerMask = Physics.DefaultRaycastLayers;
        [SerializeField] private bool showAdvancedBrushSettings;
        [SerializeField] private bool limitOpacityPerStroke;

        public PaintLayer activeLayer = PaintLayer.Layer1;
        public bool eraseMode = false;
        public bool lockSelection = false;
        [SerializeField] private PaintWorkflow paintWorkflow = PaintWorkflow.LayeredBlend;
        [SerializeField] private PaintTargetMode paintTargetMode = PaintTargetMode.Automatic;
        [SerializeField] private int selectedPaintSubmesh;
        [SerializeField] private VertexColorTarget vertexColorTarget = VertexColorTarget.RGBA;
        [SerializeField] private Color vertexPaintColor = Color.white;
        [SerializeField] private Color vertexEraseColor = Color.clear;
        [SerializeField] private Color[] vertexColorPalette = new Color[10];
        [SerializeField] private bool[] vertexColorPaletteUsed = new bool[10];
        [SerializeField] private int selectedVertexPaletteSlot = 0;
        [SerializeField] private string paletteFileName = "DefaultPalette";
        [SerializeField] private string[] savedPaletteNames = new string[0];
        [SerializeField] private int selectedSavedPaletteIndex = -1;
        [SerializeField] private bool showVertexColorOverlay = false;
        [SerializeField] private float vertexColorOverlayOpacity = 0.85f;
        [SerializeField] private bool vertexColorOverlayXRay = false;
        [SerializeField] private VertexColorPreviewMode vertexColorPreviewMode = VertexColorPreviewMode.RGB;

        // --- Internal Data --
        private GameObject currentSelection;
        private MeshFilter currentMeshFilter;
        private MeshRenderer currentRenderer;
        private Mesh workingMesh;
        private VertexPaintStorage currentStorage;

        private Vector3[] originalVertices;
        private Vector3[] originalNormals;
        private Color[] storedColors;
        private Color[] previewColors;
        private List<PaintFaceReference>[] incidentPaintFaces;
        private int[] triangleSubmeshLookup;
        private int[] flattenedPaintTriangles;
        private int hoveredPaintSubmesh = -1;
        private int hoveredTriangleWinding = 1;

        // Non-Serialized runtime cache (The source of the "disappearing" bug)
        private Dictionary<Vector3Int, List<int>> vertexBuckets;

        private float bucketSize = 1.0f;
        private bool meshIsDirty = true;
        private bool isBrushPreviewing = false;
        private bool suppressBrushPreviewUntilPointerMove;
        private bool isPaintModeEnabled = false;
        private bool paintMeshLeaseValidated;
        private Material vertexColorPreviewMaterial;
        private bool paintStrokeActive;
        private bool paintStrokeModified;
        private int paintStrokeUndoGroup = -1;
        private VertexPaintColorUndoState paintStrokeUndoState;
        private Color[] strokeStartColors;
        private float[] strokeMaxCoverage;
        private bool strokeEraseMode;

        // --- Tool State ---
        private Tool lastTool = Tool.None;

        [SerializeField] private Texture[] layerTextures = new Texture[5]; // CHANGED: Replaced individual textures with array
        [System.NonSerialized] private Texture2DArray optimizedPreviewSource;
        [System.NonSerialized] private uint optimizedPreviewSourceUpdateCount;
        [System.NonSerialized] private Texture[] optimizedLayerPreviews;
        [System.NonSerialized] private int[] optimizedLayerPreviewSlices;
        [System.NonSerialized] private int[] optimizedLayerPreviewFailedSlices;
        [System.NonSerialized] private int[] optimizedLayerPreviewFailureCounts;
        [System.NonSerialized] private double[] optimizedLayerPreviewRetryTimes;
        private GUIStyle toggleStyle;

        private Vector3 lastHitPoint;
        private Vector3 lastHitNormal;
        private int lastHitTriangleIndex = -1;
        private bool validHit = false;
        private RaycastHit[] paintRaycastHits = new RaycastHit[128];
        private Vector2 scrollPos;

        private struct PaintFaceReference
        {
            public int submesh;
            public Vector3 localNormal;

            public PaintFaceReference(int submesh, Vector3 localNormal)
            {
                this.submesh = submesh;
                this.localNormal = localNormal;
            }
        }

        // --- Noise Module ---
        [SerializeField]
        private VertexPainterNoiseModule noiseModule = new VertexPainterNoiseModule();
        public VertexPaintStorage CurrentStorage => currentStorage;
        internal Mesh CurrentWorkingMesh => workingMesh;
        internal MeshFilter CurrentMeshFilter => currentMeshFilter;
        internal Color[] CurrentStoredColors => storedColors;

        // --- NEW: Settings for layers ---
        [SerializeField] private bool showSettings = false;
        [SerializeField] private string[] layerLabels = { "Base", "Layer 1", "Layer 2", "Wetness", "Variation" };
        [SerializeField] private bool[] layerOverlay = { false, false, false, true, true };
        [SerializeField] private Channel[] layerChannels = { Channel.None, Channel.G, Channel.B, Channel.A, Channel.R };
        [SerializeField] private string[] texturePropNames = { "_Base_Albedo", "_Layer1_Albedo", "_Layer2_Albedo", "_Layer3_Albedo", "_Layer4_Albedo" };

        public VertexPainter_PaintTab(EditorWindow owner)
        {
            RebindOwner(owner);
        }

        public void RebindOwner(EditorWindow owner)
        {
            _owner = owner;
            EnsureRuntimeState();
        }

        // --- Interface Implementation ---

        public void OnEnable()
        {
            EnsureRuntimeState();
            VertexPaintUndoDispatcher.AddColorsRestoredListener(
                RefreshCurrentPaintColorsAfterUndo);
            RealBlendProBuilderColorBridge.AddMeshModifiedListener(
                OnExternalMeshModified);
            CleanupAllPreview();
            meshIsDirty = true;
            ValidateMeshState();
            UpdateMaterialTextures();
            EnsureVertexPaletteState();
            RefreshSavedPaletteNames();
        }

        public void OnDisable()
        {
            VertexPaintUndoDispatcher.RemoveColorsRestoredListener(
                RefreshCurrentPaintColorsAfterUndo);
            RealBlendProBuilderColorBridge.RemoveMeshModifiedListener(
                OnExternalMeshModified);
            EndPaintStroke();
            CleanupAllPreview();
            CleanupVertexColorPreviewMaterial();
            CleanupOptimizedLayerPreviews();
        }

        public void ForceStop()
        {
            SetPaintMode(false);
        }

        internal void ReleaseUndoCallbacks()
        {
            VertexPaintUndoDispatcher.RemoveColorsRestoredListener(
                RefreshCurrentPaintColorsAfterUndo);
            RealBlendProBuilderColorBridge.RemoveMeshModifiedListener(
                OnExternalMeshModified);
        }

        private void OnExternalMeshModified(MeshFilter meshFilter)
        {
            if (meshFilter == null || meshFilter != currentMeshFilter)
                return;
            EndPaintStroke(false);
            CleanupAllPreview();
            workingMesh = meshFilter.sharedMesh;
            storedColors = workingMesh != null ? workingMesh.colors : null;
            paintMeshLeaseValidated = currentStorage != null &&
                                      currentStorage.OwnedEditableMesh == workingMesh;
            meshIsDirty = true;
            _owner?.Repaint();
        }

        public void OnUndoRedo()
        {
            isBrushPreviewing = false;
            paintMeshLeaseValidated = false;
            noiseModule?.AbandonPreview();
            EndPaintStroke(false);
            if (currentStorage != null)
            {
                currentStorage.ApplyColors();
                if (currentMeshFilter != null)
                {
                    workingMesh = currentMeshFilter.sharedMesh;
                    storedColors = workingMesh.colors;
                    // This path handles Sculpt/Boolean/general Undo operations.
                    // Paint/Flood/Dirt take the dedicated color-only fast path.
                    meshIsDirty = true;
                }
                SceneView.RepaintAll();
            }
        }

        private void RefreshCurrentPaintColorsAfterUndo(
            VertexPaintColorUndoState restoredState)
        {
            if (workingMesh == null ||
                currentMeshFilter == null ||
                currentMeshFilter.sharedMesh != workingMesh ||
                restoredState != null && restoredState.gameObject != currentMeshFilter.gameObject)
            {
                return;
            }

            // The dispatcher calls this only for the target whose carrier was
            // restored. Unrelated targets keep their live hover previews.
            isBrushPreviewing = false;
            suppressBrushPreviewUntilPointerMove = true;
            noiseModule?.AbandonPreview();
            EndPaintStroke(false);

            Color[] restoredColors = workingMesh.colors;
            if (restoredColors == null || restoredColors.Length != workingMesh.vertexCount)
                return;

            storedColors = restoredColors;
            if (previewColors == null || previewColors.Length != restoredColors.Length)
                previewColors = new Color[restoredColors.Length];
            System.Array.Copy(restoredColors, previewColors, restoredColors.Length);

            // Restoring colors does not invalidate vertex positions, triangle
            // lookup data, or spatial buckets.
            isBrushPreviewing = false;
            SceneView.RepaintAll();
            _owner?.Repaint();
        }

        public void OnSelectionChange()
        {
            if (lockSelection && isPaintModeEnabled) return;

            if (Selection.activeGameObject != currentSelection)
            {
                EndPaintStroke();
                CleanupAllPreview();
                ValidateMeshState();
                UpdateMaterialTextures();
            }
        }

        public void OnReload()
        {
            EnsureRuntimeState();
            EndPaintStroke();
            vertexBuckets = null;
            incidentPaintFaces = null;
            triangleSubmeshLookup = null;
            flattenedPaintTriangles = null;
            meshIsDirty = true;
            validHit = false;
            lastHitTriangleIndex = -1;
            hoveredPaintSubmesh = -1;
            hoveredTriangleWinding = 1;
            isBrushPreviewing = false;
            CleanupOptimizedLayerPreviews();
            ValidateMeshState();
            UpdateMaterialTextures();
            EnsureVertexPaletteState();
            RefreshSavedPaletteNames();
            CleanupVertexColorPreviewMaterial();
        }

        void SetPaintMode(bool active)
        {
            if (isPaintModeEnabled == active) return;

            isPaintModeEnabled = active;

            if (isPaintModeEnabled)
            {
                lastTool = Tools.current;
                Tools.current = Tool.None;
                ValidateMeshState();
                if (currentStorage != null)
                    EnsurePaintMeshEditableForChange();
            }
            else
            {
                Tools.current = lastTool;
                EndPaintStroke();
                CleanupAllPreview();
            }

            SceneView.RepaintAll();
        }

        void CleanupAllPreview()
        {
            if (workingMesh && storedColors != null)
            {
                if (isBrushPreviewing)
                {
                    workingMesh.colors = storedColors;
                    workingMesh.UploadMeshData(false);
                }
            }

            isBrushPreviewing = false;
            if (workingMesh && storedColors != null)
                noiseModule?.ClearPreview(workingMesh, storedColors);
        }

        internal void PrepareForProceduralPreview()
        {
            EndPaintStroke();
            CleanupAllPreview();
        }

        private void BeginPaintStroke(bool isErasing)
        {
            paintStrokeActive = true;
            paintStrokeModified = false;
            paintStrokeUndoGroup = -1;
            paintStrokeUndoState = null;
            strokeStartColors = null;
            strokeMaxCoverage = null;
            strokeEraseMode = isErasing;

            if (limitOpacityPerStroke)
                ResetCappedStrokeSegment(isErasing);
        }

        private void EndPaintStroke(bool commitChanges = true)
        {
            int completedUndoGroup = paintStrokeUndoGroup;
            if (paintStrokeActive && paintStrokeModified && commitChanges)
            {
                if (workingMesh != null && storedColors != null)
                {
                    workingMesh.colors = storedColors;
                    workingMesh.UploadMeshData(false);
                }
                isBrushPreviewing = false;
                noiseModule?.ClearPreview(workingMesh, storedColors);
                CommitPaintStrokeColors();
            }

            // The dispatcher token is transient gesture ownership, not part of
            // the committed color payload. Clear it even when an external mesh
            // change or Undo cancels the stroke; otherwise a later reused Undo
            // group can force an obsolete color carrier back onto the mesh.
            VertexPaintUndoDispatcher.CompleteActiveStroke(completedUndoGroup);

            paintStrokeActive = false;
            paintStrokeModified = false;
            paintStrokeUndoGroup = -1;
            paintStrokeUndoState = null;
            strokeStartColors = null;
            strokeMaxCoverage = null;
            strokeEraseMode = false;
        }

        private void RegisterPaintStrokeUndo()
        {
            if (paintStrokeUndoGroup >= 0 || workingMesh == null)
                return;

            Undo.IncrementCurrentGroup();
            paintStrokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(VertexPaintUndoDispatcher.UndoName);

            paintStrokeUndoState = PrepareColorUndoState(
                currentMeshFilter,
                workingMesh,
                currentStorage,
                VertexPaintUndoDispatcher.UndoName);
            if (paintStrokeUndoState != null)
            {
                paintStrokeUndoState.SetPending(storedColors);
                EditorUtility.SetDirty(paintStrokeUndoState);
                VertexPaintUndoDispatcher.TrackActiveStroke(
                    paintStrokeUndoGroup,
                    paintStrokeUndoState);
            }
        }

        private void CommitPaintStrokeColors()
        {
            if (currentStorage == null ||
                workingMesh == null ||
                storedColors == null ||
                storedColors.Length != workingMesh.vertexCount)
            {
                return;
            }

            currentStorage.SavePaintedColors(workingMesh, storedColors);
            RealBlendProBuilderColorBridge.SyncPaintedColorsToProBuilder(
                currentMeshFilter,
                currentStorage,
                storedColors);
            if (paintStrokeUndoState != null)
            {
                paintStrokeUndoState.Commit(storedColors);
                EditorUtility.SetDirty(paintStrokeUndoState);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    paintStrokeUndoState);
            }
            EditorUtility.SetDirty(currentStorage);
            PrefabUtility.RecordPrefabInstancePropertyModifications(currentStorage);
        }

        internal static VertexPaintColorUndoState PrepareColorUndoState(
            MeshFilter filter,
            Mesh mesh,
            VertexPaintStorage storage,
            string undoName)
        {
            if (filter == null || mesh == null || string.IsNullOrEmpty(undoName))
                return null;

            VertexPaintColorUndoState state =
                filter.GetComponent<VertexPaintColorUndoState>();
            if (state == null)
            {
                state = filter.gameObject.AddComponent<VertexPaintColorUndoState>();
                state.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInBuild;
            }

            state.Initialize(
                mesh.colors,
                storage != null ? storage.PaintedColorRevision : 0);
            Undo.RegisterCompleteObjectUndo(state, undoName);
            return state;
        }

        internal static void CommitColorUndoState(
            VertexPaintColorUndoState state,
            Color[] colors)
        {
            if (state == null || colors == null)
                return;

            state.Commit(colors);
            EditorUtility.SetDirty(state);
            PrefabUtility.RecordPrefabInstancePropertyModifications(state);
        }

        private void ResetCappedStrokeSegment(bool isErasing)
        {
            strokeEraseMode = isErasing;
            if (storedColors == null || storedColors.Length == 0)
            {
                strokeStartColors = null;
                strokeMaxCoverage = null;
                return;
            }

            strokeStartColors = (Color[])storedColors.Clone();
            strokeMaxCoverage = new float[storedColors.Length];
        }

        void UpdateMaterialTextures()
        {
            EnsureRuntimeState();
            if (!currentRenderer)
            {
                ClearLayerTextures();
                CleanupOptimizedLayerPreviews();
                return;
            }

            Material[] materials = currentRenderer.sharedMaterials;
            int requestedSubmesh = GetEffectivePaintSubmesh();
            int materialIndex = materials != null && materials.Length > 0
                ? Mathf.Clamp(requestedSubmesh, 0, materials.Length - 1)
                : -1;
            Material mat = materialIndex >= 0 ? materials[materialIndex] : null;
            if (!mat)
            {
                ClearLayerTextures();
                CleanupOptimizedLayerPreviews();
                return;
            }

            if (TryUpdateOptimizedMaterialTextures(mat))
                return;

            CleanupOptimizedLayerPreviews();

            for (int i = 0; i < 5; i++)
            {
                layerTextures[i] = GetMaterialTexture(mat, texturePropNames[i]);
            }
        }

        private bool TryUpdateOptimizedMaterialTextures(Material material)
        {
            if (!HasOptimizedLayerPreviewContract(material))
                return false;

            Texture2DArray albedoArray = material.GetTexture("_AlbedoArray") as Texture2DArray;
            if (albedoArray == null || albedoArray.depth <= 0)
            {
                CleanupOptimizedLayerPreviews();
                for (int i = 0; i < OptimizedLayerPreviewCount; i++)
                    layerTextures[i] = null;
                UpdateOptimizedAuxiliaryTextures(material);
                return true;
            }

            bool sourceChanged = optimizedPreviewSource != albedoArray ||
                                 optimizedPreviewSourceUpdateCount != albedoArray.updateCount;
            if (sourceChanged)
            {
                CleanupOptimizedLayerPreviews();
                optimizedPreviewSource = albedoArray;
                optimizedPreviewSourceUpdateCount = albedoArray.updateCount;
            }

            for (int i = 0; i < OptimizedLayerPreviewCount; i++)
            {
                int slice = Mathf.Clamp(
                    Mathf.RoundToInt(material.GetFloat(OptimizedSlicePropertyNames[i])),
                    0,
                    albedoArray.depth - 1);
                EnsureOptimizedLayerPreview(albedoArray, i, slice);
                layerTextures[i] = optimizedLayerPreviews[i];
            }

            UpdateOptimizedAuxiliaryTextures(material);
            return true;
        }

        private void EnsureOptimizedLayerPreview(
            Texture2DArray source,
            int previewIndex,
            int requestedSlice)
        {
            Texture cachedPreview = optimizedLayerPreviews[previewIndex];
            bool cachedPreviewExists = cachedPreview != null;
            bool renderTextureLost = cachedPreviewExists &&
                                     cachedPreview is RenderTexture renderTexture &&
                                     !renderTexture.IsCreated();
            bool cacheIsUsable = cachedPreviewExists &&
                                 !renderTextureLost &&
                                 optimizedLayerPreviewSlices[previewIndex] == requestedSlice;
            if (cacheIsUsable)
                return;

            bool failedRequestChanged =
                optimizedLayerPreviewFailedSlices[previewIndex] != requestedSlice;
            if (failedRequestChanged || renderTextureLost)
                ResetOptimizedLayerPreviewRetry(previewIndex);

            double now = EditorApplication.timeSinceStartup;
            if (!failedRequestChanged &&
                !renderTextureLost &&
                now < optimizedLayerPreviewRetryTimes[previewIndex])
            {
                return;
            }

            DestroyOptimizedLayerPreview(previewIndex);
            Texture newPreview = CreateOptimizedLayerPreview(source, requestedSlice);
            if (newPreview != null)
            {
                optimizedLayerPreviews[previewIndex] = newPreview;
                // A slice becomes cached only after its preview was created successfully.
                optimizedLayerPreviewSlices[previewIndex] = requestedSlice;
                ResetOptimizedLayerPreviewRetry(previewIndex);
                return;
            }

            optimizedLayerPreviewFailedSlices[previewIndex] = requestedSlice;
            int failureCount = Mathf.Min(
                optimizedLayerPreviewFailureCounts[previewIndex] + 1,
                16);
            optimizedLayerPreviewFailureCounts[previewIndex] = failureCount;
            double retryDelay = System.Math.Min(
                OptimizedLayerPreviewMaxRetrySeconds,
                OptimizedLayerPreviewInitialRetrySeconds *
                System.Math.Pow(2d, failureCount - 1));
            optimizedLayerPreviewRetryTimes[previewIndex] = now + retryDelay;
        }

        private void ResetOptimizedLayerPreviewRetry(int index)
        {
            if (optimizedLayerPreviewFailedSlices != null &&
                index >= 0 &&
                index < optimizedLayerPreviewFailedSlices.Length)
            {
                optimizedLayerPreviewFailedSlices[index] = -1;
            }
            if (optimizedLayerPreviewFailureCounts != null &&
                index >= 0 &&
                index < optimizedLayerPreviewFailureCounts.Length)
            {
                optimizedLayerPreviewFailureCounts[index] = 0;
            }
            if (optimizedLayerPreviewRetryTimes != null &&
                index >= 0 &&
                index < optimizedLayerPreviewRetryTimes.Length)
            {
                optimizedLayerPreviewRetryTimes[index] = 0d;
            }
        }

        private static bool HasOptimizedLayerPreviewContract(Material material)
        {
            if (material == null || !material.HasProperty("_AlbedoArray"))
                return false;

            for (int i = 0; i < OptimizedSlicePropertyNames.Length; i++)
            {
                if (!material.HasProperty(OptimizedSlicePropertyNames[i]))
                    return false;
            }
            return true;
        }

        private void UpdateOptimizedAuxiliaryTextures(Material material)
        {
            for (int i = OptimizedLayerPreviewCount; i < layerTextures.Length; i++)
                layerTextures[i] = GetMaterialTexture(material, texturePropNames[i]);

            // Production RealBlend exposes wetness separately from its three array layers.
            if (layerTextures[(int)PaintLayer.Wetness] == null && material.HasProperty("_WetnessMask"))
                layerTextures[(int)PaintLayer.Wetness] = material.GetTexture("_WetnessMask");
        }

        private static Texture GetMaterialTexture(Material material, string propertyName)
        {
            return material != null &&
                   !string.IsNullOrEmpty(propertyName) &&
                   material.HasProperty(propertyName)
                ? material.GetTexture(propertyName)
                : null;
        }

        private static Texture CreateOptimizedLayerPreview(Texture2DArray source, int slice)
        {
            if (source == null || slice < 0 || slice >= source.depth)
                return null;

            Texture gpuPreview = CreateGpuOptimizedLayerPreview(source, slice);
            if (gpuPreview != null)
                return gpuPreview;

            // A readable fallback also keeps thumbnails available on editor graphics
            // devices that cannot render texture arrays.
            int mip = FindOptimizedLayerPreviewMip(source);
            if (mip < 0 || !source.isReadable)
                return null;

            int width = Mathf.Max(1, source.width >> mip);
            int height = Mathf.Max(1, source.height >> mip);
            Texture2D preview = null;
            try
            {
                preview = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    !source.isDataSRGB);
                preview.SetPixels32(source.GetPixels32(slice, mip));
                preview.Apply(false, true);

                preview.name = $"{source.name} Slice {slice} Preview";
                preview.hideFlags = HideFlags.HideAndDontSave;
                preview.filterMode = FilterMode.Bilinear;
                preview.wrapMode = TextureWrapMode.Clamp;
                return preview;
            }
            catch (System.Exception)
            {
                if (preview != null)
                    DestroyTransientLayerPreview(preview);
                return null;
            }
        }

        private static Texture CreateGpuOptimizedLayerPreview(Texture2DArray source, int slice)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(TextureArrayPreviewShaderAssetPath);
            if (shader == null || !shader.isSupported)
                return null;

            int width = source.width;
            int height = source.height;
            float scale = Mathf.Min(
                1f,
                OptimizedLayerPreviewMaxSize / (float)Mathf.Max(width, height));
            width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * scale));

            Material previewMaterial = null;
            RenderTexture preview = null;
            RenderTexture previousActive = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                previewMaterial.SetTexture("_SourceArray", source);
                previewMaterial.SetFloat("_Slice", slice);

                preview = new RenderTexture(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    source.isDataSRGB
                        ? RenderTextureReadWrite.sRGB
                        : RenderTextureReadWrite.Linear)
                {
                    name = $"{source.name} Slice {slice} Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!preview.Create())
                {
                    DestroyTransientLayerPreview(preview);
                    return null;
                }

                GL.sRGBWrite = source.isDataSRGB;
                Graphics.Blit(Texture2D.blackTexture, preview, previewMaterial, 0);
                return preview;
            }
            catch (System.Exception)
            {
                if (preview != null)
                    DestroyTransientLayerPreview(preview);
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgbWrite;
                if (previewMaterial != null)
                    Object.DestroyImmediate(previewMaterial);
            }
        }

        private static int FindOptimizedLayerPreviewMip(Texture2DArray source)
        {
            for (int mip = 0; mip < source.mipmapCount; mip++)
            {
                int width = Mathf.Max(1, source.width >> mip);
                int height = Mathf.Max(1, source.height >> mip);
                if (Mathf.Max(width, height) <= OptimizedLayerPreviewMaxSize)
                    return mip;
            }

            // Avoid allocating a full-size duplicate for an unusually large array without mips.
            return Mathf.Max(source.width, source.height) <= OptimizedLayerPreviewMaxSize ? 0 : -1;
        }

        private void CleanupOptimizedLayerPreviews()
        {
            if (optimizedLayerPreviews != null)
            {
                for (int i = 0; i < optimizedLayerPreviews.Length; i++)
                    DestroyOptimizedLayerPreview(i);
            }
            for (int i = 0; i < OptimizedLayerPreviewCount; i++)
                ResetOptimizedLayerPreviewRetry(i);

            optimizedPreviewSource = null;
            optimizedPreviewSourceUpdateCount = 0;
        }

        private void DestroyOptimizedLayerPreview(int index)
        {
            if (optimizedLayerPreviews == null ||
                index < 0 ||
                index >= optimizedLayerPreviews.Length)
            {
                return;
            }

            if (optimizedLayerPreviews[index] != null)
            {
                DestroyTransientLayerPreview(optimizedLayerPreviews[index]);
                optimizedLayerPreviews[index] = null;
            }
            if (optimizedLayerPreviewSlices != null && index < optimizedLayerPreviewSlices.Length)
                optimizedLayerPreviewSlices[index] = -1;
        }

        private static void DestroyTransientLayerPreview(Texture preview)
        {
            if (preview == null)
                return;

            if (preview is RenderTexture renderTexture && renderTexture.IsCreated())
            {
                if (RenderTexture.active == renderTexture)
                    RenderTexture.active = null;
                renderTexture.Release();
            }
            Object.DestroyImmediate(preview);
        }

        private void ClearLayerTextures()
        {
            if (layerTextures == null)
                return;

            for (int i = 0; i < layerTextures.Length; i++)
                layerTextures[i] = null;
        }

        int GetEffectivePaintSubmesh()
        {
            if (workingMesh == null || workingMesh.subMeshCount <= 0)
                return 0;

            int submesh = paintTargetMode == PaintTargetMode.Automatic
                ? hoveredPaintSubmesh
                : selectedPaintSubmesh;
            if (submesh < 0)
                submesh = 0;
            return Mathf.Clamp(submesh, 0, workingMesh.subMeshCount - 1);
        }

        void ValidateMeshState()
        {
            GameObject newSelection = Selection.activeGameObject;

            if (lockSelection && isPaintModeEnabled && currentSelection != null)
            {
                if (currentSelection == null)
                {
                    lockSelection = false;
                    CleanupAllPreview();
                }
                else if (newSelection != currentSelection)
                {
                    if (newSelection != null) Selection.activeGameObject = currentSelection;
                    return;
                }
            }

            bool dataLost = (vertexBuckets == null);
            bool selectionChanged = (newSelection != currentSelection);

            if (selectionChanged || dataLost)
            {
                if (selectionChanged)
                {
                    EndPaintStroke();
                    CleanupAllPreview();
                    currentSelection = newSelection;
                    workingMesh = null;
                    currentStorage = null;
                    currentMeshFilter = null;
                    currentRenderer = null;
                    paintMeshLeaseValidated = false;
                    incidentPaintFaces = null;
                    triangleSubmeshLookup = null;
                    flattenedPaintTriangles = null;
                    hoveredPaintSubmesh = -1;
                    hoveredTriangleWinding = 1;
                }

                meshIsDirty = true;
                vertexBuckets = null;
            }

            if (currentSelection == null) return;

            if (!currentMeshFilter || currentMeshFilter.gameObject != currentSelection)
            {
                currentMeshFilter = currentSelection.GetComponent<MeshFilter>();
                currentRenderer = currentSelection.GetComponent<MeshRenderer>();
            }

            UpdateMaterialTextures();

            if (!currentMeshFilter) return;

            if (!currentStorage)
                currentStorage = currentSelection.GetComponent<VertexPaintStorage>();

            Mesh actualMesh = currentMeshFilter.sharedMesh;
            if (!actualMesh)
                return;

            bool meshChanged = workingMesh != actualMesh;
            if (meshChanged || storedColors == null ||
                storedColors.Length != actualMesh.vertexCount)
            {
                workingMesh = actualMesh;
                Color[] meshColors = actualMesh.colors;
                if (currentStorage != null &&
                    currentStorage.paintedColors != null &&
                    currentStorage.paintedColors.Length == actualMesh.vertexCount)
                {
                    storedColors = (Color[])currentStorage.paintedColors.Clone();
                }
                else if (meshColors != null &&
                         meshColors.Length == actualMesh.vertexCount)
                {
                    storedColors = meshColors;
                }
                else
                {
                    storedColors = new Color[actualMesh.vertexCount];
                }
                paintMeshLeaseValidated = false;
                meshIsDirty = true;
            }
        }

        public void OnGUI()
        {
            EnsureRuntimeState();

            if (toggleStyle == null || toggleStyle.normal.background == null)
            {
                toggleStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    imagePosition = ImagePosition.ImageAbove,
                    fixedHeight = 96,
                    fixedWidth = 96,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    margin = new RectOffset(2, 2, 2, 2)
                };
            }

            ValidateMeshState();

            try
            {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

                Color defaultBg = GUI.backgroundColor;
                GUI.backgroundColor = isPaintModeEnabled ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button(isPaintModeEnabled ? "🎨 PAINTING ACTIVE" : "🔴 START PAINTING", GUILayout.Height(40)))
                {
                    SetPaintMode(!isPaintModeEnabled);
                    _owner?.Repaint();
                }
                GUI.backgroundColor = defaultBg;

                if (currentSelection != null && currentSelection.GetComponent<Collider>() == null)
                {
                    EditorGUILayout.HelpBox("⚠️ No Collider Detected! You cannot paint without a collider.", MessageType.Error);
                    if (GUILayout.Button("Fix: Add Mesh Collider"))
                    {
                        currentSelection.AddComponent<MeshCollider>();
                    }
                }

                GUILayout.Space(15);

                if (currentSelection != null && currentMeshFilter != null)
                {
                    if (currentStorage == null)
                    {
                        EditorGUILayout.HelpBox("Data not saved! Add Storage script.", MessageType.Warning);
                        if (GUILayout.Button("⚠️ Add Data Storage Script", GUILayout.Height(30)))
                        {
                            currentStorage = currentSelection.AddComponent<VertexPaintStorage>();
                            workingMesh = null;
                            ValidateMeshState();
                            if (isPaintModeEnabled)
                                EnsurePaintMeshEditableForChange();
                            _owner?.Repaint();
                        }
                    }
                    else
                    {
                        GUI.backgroundColor = Color.green;
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Label("✅ Data Storage Active", EditorStyles.boldLabel);
                        GUILayout.Label($"Stored Vertices: {currentStorage.paintedColors?.Length ?? 0}", EditorStyles.miniLabel);
                        EditorGUILayout.EndVertical();
                        GUI.backgroundColor = defaultBg;
                    }
                }

                if (paintRaycastLayerMask == 0)
                    paintRaycastLayerMask = Physics.DefaultRaycastLayers;
                paintRaycastLayerMask = EditorGUILayout.MaskField("Paint Raycast Layers", paintRaycastLayerMask, RealBlendLayerMaskUtility.LayerNames);

                GUILayout.Space(10);
                PaintWorkflow nextWorkflow = (PaintWorkflow)EditorGUILayout.EnumPopup(
                    "Paint Workflow",
                    paintWorkflow);
                if (nextWorkflow != paintWorkflow)
                {
                    EndPaintStroke();
                    CleanupAllPreview();
                    paintWorkflow = nextWorkflow;
                    SceneView.RepaintAll();
                }
                if (paintWorkflow == PaintWorkflow.LayeredBlend)
                {
                    UpdateMaterialTextures();
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                    GUILayout.Space(5);
                    GUILayout.Label("🎨 Active Layer Palette", EditorStyles.boldLabel);
                    GUILayout.Space(5);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    for (int i = 0; i < 3; i++)
                    {
                        PaintLayer pl = (PaintLayer)i;
                        GUI.backgroundColor = (activeLayer == pl) ? new Color(1f, 1f, 0.5f) : defaultBg;
                        GUIContent content = new GUIContent();
                        content.tooltip = GetTooltip(pl);
                        Texture tex = layerTextures[i];
                        string lbl = layerLabels[i];
                        if (tex != null)
                        {
                            content.text = "";
                            content.image = tex;
                        }
                        else
                        {
                            content.text = lbl.ToUpper();
                        }
                        if (GUILayout.Button(content, toggleStyle))
                        {
                            EndPaintStroke();
                            activeLayer = pl;
                            SceneView.RepaintAll();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    for (int i = 3; i < 5; i++)
                    {
                        PaintLayer pl = (PaintLayer)i;
                        GUI.backgroundColor = (activeLayer == pl) ? new Color(1f, 1f, 0.5f) : defaultBg;
                        GUIContent content = new GUIContent();
                        content.tooltip = GetTooltip(pl);
                        Texture tex = layerTextures[i];
                        string lbl = layerLabels[i];
                        if (tex != null)
                        {
                            content.text = "";
                            content.image = tex;
                        }
                        else
                        {
                            content.text = lbl.ToUpper();
                        }
                        if (GUILayout.Button(content, toggleStyle))
                        {
                            EndPaintStroke();
                            activeLayer = pl;
                            SceneView.RepaintAll();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(5);
                    }
                    EditorGUILayout.EndVertical();
                    GUI.backgroundColor = defaultBg;
                }
                else
                {
                    EnsureVertexPaletteState();
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label("Direct Vertex Color", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    vertexPaintColor = EditorGUILayout.ColorField(
                        new GUIContent(
                            "Paint RGBA",
                            "RGBA are four independent vertex data channels. The color's A value is not brush opacity."),
                        vertexPaintColor);
                    vertexEraseColor = EditorGUILayout.ColorField(
                        new GUIContent(
                            "Erase RGBA",
                            "The Shift-paint target. Its A value writes the vertex alpha channel; it is not brush opacity."),
                        vertexEraseColor);
                    vertexColorTarget = (VertexColorTarget)EditorGUILayout.EnumPopup("Channels", vertexColorTarget);
                    showVertexColorOverlay = EditorGUILayout.Toggle("Show Color Overlay", showVertexColorOverlay);
                    if (showVertexColorOverlay)
                    {
                        EditorGUI.indentLevel++;
                        vertexColorPreviewMode = (VertexColorPreviewMode)EditorGUILayout.EnumPopup("Overlay Mode", vertexColorPreviewMode);
                        vertexColorOverlayOpacity = EditorGUILayout.Slider("Overlay Opacity", vertexColorOverlayOpacity, 0.05f, 1.0f);
                        vertexColorOverlayXRay = EditorGUILayout.Toggle("X-Ray Overlay", vertexColorOverlayXRay);
                        EditorGUI.indentLevel--;
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        EndPaintStroke();
                        SceneView.RepaintAll();
                    }

                    EditorGUILayout.HelpBox(
                        limitOpacityPerStroke
                            ? "Opacity is capped for each click-and-drag stroke. Release and paint again to build farther. Paint RGBA alpha writes the A data channel and does not make RGB translucent."
                            : "Brush Opacity is applied repeatedly while you hold and drag, so the color builds toward its target. Paint RGBA alpha writes the A data channel and does not make RGB translucent.",
                        MessageType.None);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Add Color", GUILayout.Width(90)))
                    {
                        SaveVertexPaintColorToPalette();
                    }
                    if (GUILayout.Button("Clear Selected", GUILayout.Width(110)))
                    {
                        ClearVertexPaletteSlot(selectedVertexPaletteSlot);
                    }
                    GUILayout.Label($"Selected Slot: {selectedVertexPaletteSlot + 1}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    DrawVertexPaletteSwatches();
                    GUILayout.Space(6);
                    DrawVertexPaletteFileControls();
                    EditorGUILayout.HelpBox("Click a swatch to load it as Paint Color. Add Color stores the current paint color into the first free slot (or selected slot when all slots are used). Use Shift to paint the erase color.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                }

                DrawOperations();
                DrawBrushSettings();
                DrawProceduralDirtControls();
                DrawPaintTargetControls();
                DrawAdvancedBrushSettings();
                DrawAdvancedLayerSettings();

                // Leave enough scroll runway to bring the final foldouts above
                // the bottom edge of a short or docked Editor window.
                GUILayout.Space(PaintTabBottomScrollPadding);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawOperations()
        {
            GUILayout.Space(15f);
            GUILayout.Label("Operations", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            string floodLabel = paintWorkflow == PaintWorkflow.LayeredBlend
                ? "Flood Fill Layer"
                : "Flood Paint Color";
            string clearLabel = paintWorkflow == PaintWorkflow.LayeredBlend
                ? "Clear Layer"
                : "Flood Erase Color";
            if (GUILayout.Button(new GUIContent(
                    floodLabel,
                    "Fills the target directly and intentionally ignores brush Opacity.")))
            {
                FloodFill(true);
            }
            if (GUILayout.Button(new GUIContent(
                    clearLabel,
                    "Fills the erase/clear target directly and intentionally ignores brush Opacity.")))
            {
                FloodFill(false);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBrushSettings()
        {
            GUILayout.Space(15f);
            GUILayout.Label("Brush Settings", EditorStyles.boldLabel);

            bool previousShowPreview = showPreview;
            EditorGUI.BeginChangeCheck();
            lockSelection = EditorGUILayout.Toggle("Lock Selection", lockSelection);
            brushSize = EditorGUILayout.Slider(
                new GUIContent(
                    "Size",
                    "Ctrl/Cmd + mouse wheel adjusts brush size in the Scene view."),
                brushSize,
                0.1f,
                10f);
            brushStrength = EditorGUILayout.Slider(
                new GUIContent(
                    "Opacity",
                    (limitOpacityPerStroke
                        ? "Maximum blend amount for one click-and-drag stroke. Release and paint again to build farther."
                        : "Blend amount applied on each paint event. Holding and dragging repeatedly builds toward the target.") +
                    " Ctrl/Cmd + Shift + mouse wheel adjusts this value in the Scene view."),
                brushStrength,
                0.0f,
                1f);
            brushHardness = EditorGUILayout.Slider("Hardness", brushHardness, 0.0f, 1.0f);
            if (!limitOpacityPerStroke)
            {
                mouseSensitivity = EditorGUILayout.Slider(
                    new GUIContent(
                        "Mouse Sensitivity",
                        "Controls how strongly each mouse-drag event builds toward the target. Drag strength ranges from 2.5% to 50% of the Opacity setting."),
                    mouseSensitivity,
                    0.05f,
                    1.0f);
            }

            GUILayout.Space(5f);
            brushAlpha = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Brush Mask (Red)",
                    "Uses the texture's red channel as brush coverage."),
                brushAlpha,
                typeof(Texture2D),
                false);

            if (brushAlpha != null)
            {
                EditorGUI.indentLevel++;
                if (!brushAlpha.isReadable)
                {
                    EditorGUILayout.HelpBox(
                        "This brush mask is not readable, so it cannot be sampled for vertex painting.",
                        MessageType.Warning);
                    if (GUILayout.Button("Enable Read/Write for Brush Mask"))
                    {
                        EnableBrushMaskReadWrite();
                    }
                }
                brushRotation = EditorGUILayout.Slider("Rotation", brushRotation, 0f, 360f);
                invertBrushAlpha = EditorGUILayout.Toggle("Invert Texture", invertBrushAlpha);
                EditorGUI.indentLevel--;
            }

            showPreview = EditorGUILayout.Toggle("Show Preview", showPreview);
            if (EditorGUI.EndChangeCheck())
            {
                EndPaintStroke();
                if (previousShowPreview && !showPreview)
                {
                    CleanupAllPreview();
                }
                SceneView.RepaintAll();
            }
        }

        private void DrawProceduralDirtControls()
        {
            GUILayout.Space(10f);
            if (noiseModule == null ||
                !currentMeshFilter ||
                !workingMesh ||
                storedColors == null)
            {
                return;
            }

            if (originalVertices == null || originalVertices.Length != workingMesh.vertexCount)
            {
                originalVertices = workingMesh.vertices;
                originalNormals = workingMesh.normals;
            }

            noiseModule.DrawGUI(
                this,
                currentMeshFilter,
                workingMesh,
                ref storedColors,
                ref originalVertices);
        }

        private void DrawAdvancedBrushSettings()
        {
            GUILayout.Space(8f);
            showAdvancedBrushSettings = EditorGUILayout.Foldout(
                showAdvancedBrushSettings,
                "Advanced Brush Settings",
                true);
            if (!showAdvancedBrushSettings)
                return;

            EditorGUI.indentLevel++;
            bool nextLimitOpacity = EditorGUILayout.Toggle(
                new GUIContent(
                    "Limit Opacity Per Stroke",
                    "Photoshop-style opacity: each click-and-drag blends a vertex toward the target by at most the Opacity value. Release and paint again to build farther."),
                limitOpacityPerStroke);
            if (nextLimitOpacity != limitOpacityPerStroke)
            {
                EndPaintStroke();
                CleanupAllPreview();
                limitOpacityPerStroke = nextLimitOpacity;
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                limitOpacityPerStroke
                    ? "Capped: Opacity is the maximum contribution of one click-and-drag stroke. Brush falloff and masks can reduce it."
                    : "Continuous: Opacity is reapplied on every drag event, so low values can quickly approach 1 while scrubbing.",
                limitOpacityPerStroke ? MessageType.Info : MessageType.None);
            EditorGUI.indentLevel--;
        }

        private void DrawAdvancedLayerSettings()
        {
            if (paintWorkflow != PaintWorkflow.LayeredBlend)
                return;

            GUILayout.Space(8f);
            showSettings = EditorGUILayout.Foldout(showSettings, "Advanced Layer Settings");
            if (!showSettings)
                return;

            EditorGUI.indentLevel++;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Layer", GUILayout.Width(80));
            GUILayout.Label("Label", GUILayout.Width(70));
            GUILayout.Label("Overlay", GUILayout.Width(50));
            GUILayout.Label("Channel", GUILayout.Width(70));
            GUILayout.Label("Tex Prop", GUILayout.Width(90));
            GUILayout.EndHorizontal();

            for (int i = 0; i < 5; i++)
            {
                PaintLayer pl = (PaintLayer)i;
                GUILayout.BeginHorizontal();
                GUILayout.Label(pl.ToString(), GUILayout.Width(80));
                layerLabels[i] = EditorGUILayout.TextField(layerLabels[i], GUILayout.Width(70));
                layerOverlay[i] = EditorGUILayout.Toggle(layerOverlay[i], GUILayout.Width(50));
                layerChannels[i] = (Channel)EditorGUILayout.EnumPopup(
                    layerChannels[i],
                    GUILayout.Width(70));
                texturePropNames[i] = EditorGUILayout.TextField(
                    texturePropNames[i],
                    GUILayout.Width(90));
                GUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private void DrawPaintTargetControls()
        {
            GUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Paint Target", EditorStyles.boldLabel);
                PaintTargetMode nextMode = (PaintTargetMode)EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Mode",
                        "Automatic follows the submesh under the mouse. Selected Submesh locks painting and its texture palette to one material section."),
                    paintTargetMode);
                if (nextMode != paintTargetMode)
                {
                    EndPaintStroke();
                    paintTargetMode = nextMode;
                    CleanupAllPreview();
                    UpdateMaterialTextures();
                    SceneView.RepaintAll();
                }

                int submeshCount = workingMesh != null
                    ? Mathf.Max(1, workingMesh.subMeshCount)
                    : 1;
                selectedPaintSubmesh = Mathf.Clamp(
                    selectedPaintSubmesh,
                    0,
                    submeshCount - 1);

                if (paintTargetMode == PaintTargetMode.SelectedSubmesh)
                {
                    string[] choices = BuildSubmeshChoices(submeshCount);
                    int nextSubmesh = EditorGUILayout.Popup(
                        "Submesh",
                        selectedPaintSubmesh,
                        choices);
                    if (nextSubmesh != selectedPaintSubmesh)
                    {
                        EndPaintStroke();
                        selectedPaintSubmesh = nextSubmesh;
                        CleanupAllPreview();
                        UpdateMaterialTextures();
                        SceneView.RepaintAll();
                    }
                    EditorGUILayout.HelpBox(
                        "Only vertices belonging to this submesh and facing the brush are painted.",
                        MessageType.None);
                }
                else
                {
                    int displaySubmesh = hoveredPaintSubmesh;
                    string label = displaySubmesh >= 0
                        ? GetSubmeshChoiceLabel(displaySubmesh)
                        : "Move the brush over the mesh";
                    EditorGUILayout.LabelField("Under Mouse", label);
                    EditorGUILayout.HelpBox(
                        "Automatic follows the visible triangle, updates the palette from that submesh's material, and does not paint through the wall.",
                        MessageType.None);
                }
            }
        }

        private string[] BuildSubmeshChoices(int submeshCount)
        {
            string[] labels = new string[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = GetSubmeshChoiceLabel(i);
            return labels;
        }

        private string GetSubmeshChoiceLabel(int submesh)
        {
            string materialName = "No material";
            if (currentRenderer != null)
            {
                Material[] materials = currentRenderer.sharedMaterials;
                if (materials != null && materials.Length > 0)
                {
                    Material material = materials[Mathf.Clamp(
                        submesh,
                        0,
                        materials.Length - 1)];
                    if (material != null)
                        materialName = material.name;
                }
            }
            return $"Submesh {submesh + 1} - {materialName}";
        }

        private void EnsureVertexPaletteState()
        {
            const int slotCount = 10;

            if (vertexColorPalette == null || vertexColorPalette.Length != slotCount)
            {
                Color[] resized = new Color[slotCount];
                if (vertexColorPalette != null)
                {
                    int copyCount = Mathf.Min(vertexColorPalette.Length, slotCount);
                    System.Array.Copy(vertexColorPalette, resized, copyCount);
                }
                vertexColorPalette = resized;
            }

            if (vertexColorPaletteUsed == null || vertexColorPaletteUsed.Length != slotCount)
            {
                bool[] resized = new bool[slotCount];
                if (vertexColorPaletteUsed != null)
                {
                    int copyCount = Mathf.Min(vertexColorPaletteUsed.Length, slotCount);
                    System.Array.Copy(vertexColorPaletteUsed, resized, copyCount);
                }
                vertexColorPaletteUsed = resized;
            }

            selectedVertexPaletteSlot = Mathf.Clamp(selectedVertexPaletteSlot, 0, slotCount - 1);
        }

        private void EnsureRuntimeState()
        {
            const int layerCount = 5;

            if (paintRaycastHits == null || paintRaycastHits.Length != 128)
                paintRaycastHits = new RaycastHit[128];

            if (noiseModule == null)
                noiseModule = new VertexPainterNoiseModule();

            if (layerTextures == null || layerTextures.Length != layerCount)
                layerTextures = ResizeArray(layerTextures, layerCount);

            if (optimizedLayerPreviews == null ||
                optimizedLayerPreviews.Length != OptimizedLayerPreviewCount)
            {
                CleanupOptimizedLayerPreviews();
                optimizedLayerPreviews = new Texture[OptimizedLayerPreviewCount];
                optimizedLayerPreviewSlices = new int[OptimizedLayerPreviewCount];
                optimizedLayerPreviewFailedSlices = new int[OptimizedLayerPreviewCount];
                optimizedLayerPreviewFailureCounts = new int[OptimizedLayerPreviewCount];
                optimizedLayerPreviewRetryTimes = new double[OptimizedLayerPreviewCount];
                for (int i = 0; i < optimizedLayerPreviewSlices.Length; i++)
                {
                    optimizedLayerPreviewSlices[i] = -1;
                    optimizedLayerPreviewFailedSlices[i] = -1;
                }
            }
            else if (optimizedLayerPreviewSlices == null ||
                optimizedLayerPreviewSlices.Length != OptimizedLayerPreviewCount)
            {
                optimizedLayerPreviewSlices = new int[OptimizedLayerPreviewCount];
                for (int i = 0; i < optimizedLayerPreviewSlices.Length; i++)
                    optimizedLayerPreviewSlices[i] = -1;
            }

            if (optimizedLayerPreviewFailedSlices == null ||
                optimizedLayerPreviewFailedSlices.Length != OptimizedLayerPreviewCount)
            {
                optimizedLayerPreviewFailedSlices = new int[OptimizedLayerPreviewCount];
                for (int i = 0; i < optimizedLayerPreviewFailedSlices.Length; i++)
                    optimizedLayerPreviewFailedSlices[i] = -1;
            }

            if (optimizedLayerPreviewFailureCounts == null ||
                optimizedLayerPreviewFailureCounts.Length != OptimizedLayerPreviewCount)
            {
                optimizedLayerPreviewFailureCounts = new int[OptimizedLayerPreviewCount];
            }

            if (optimizedLayerPreviewRetryTimes == null ||
                optimizedLayerPreviewRetryTimes.Length != OptimizedLayerPreviewCount)
            {
                optimizedLayerPreviewRetryTimes = new double[OptimizedLayerPreviewCount];
            }

            if (layerLabels == null || layerLabels.Length != layerCount)
                layerLabels = new[] { "Base", "Layer 1", "Layer 2", "Wetness", "Variation" };

            if (layerOverlay == null || layerOverlay.Length != layerCount)
                layerOverlay = new[] { false, false, false, true, true };

            if (layerChannels == null || layerChannels.Length != layerCount)
                layerChannels = new[] { Channel.None, Channel.G, Channel.B, Channel.A, Channel.R };

            if (texturePropNames == null || texturePropNames.Length != layerCount)
                texturePropNames = new[] { "_Base_Albedo", "_Layer1_Albedo", "_Layer2_Albedo", "_Layer3_Albedo", "_Layer4_Albedo" };
        }

        private T[] ResizeArray<T>(T[] source, int size)
        {
            T[] result = new T[size];
            if (source != null)
            {
                int count = Mathf.Min(source.Length, size);
                System.Array.Copy(source, result, count);
            }
            return result;
        }

        private int FindFirstEmptyVertexPaletteSlot()
        {
            EnsureVertexPaletteState();
            for (int i = 0; i < vertexColorPaletteUsed.Length; i++)
            {
                if (!vertexColorPaletteUsed[i]) return i;
            }
            return -1;
        }

        private void SaveVertexPaintColorToPalette()
        {
            EnsureVertexPaletteState();
            int slot = FindFirstEmptyVertexPaletteSlot();
            if (slot < 0) slot = selectedVertexPaletteSlot;

            vertexColorPalette[slot] = vertexPaintColor;
            vertexColorPaletteUsed[slot] = true;
            selectedVertexPaletteSlot = slot;
        }

        private void ClearVertexPaletteSlot(int slot)
        {
            EnsureVertexPaletteState();
            slot = Mathf.Clamp(slot, 0, vertexColorPaletteUsed.Length - 1);
            vertexColorPaletteUsed[slot] = false;
            vertexColorPalette[slot] = Color.clear;
        }

        private void DrawVertexPaletteSwatches()
        {
            EnsureVertexPaletteState();
            const int slotsPerRow = 5;
            Event e = Event.current;
            GUIStyle centeredLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };

            EditorGUILayout.LabelField("Saved Colors", EditorStyles.miniBoldLabel);
            for (int row = 0; row < 2; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < slotsPerRow; col++)
                {
                    int slot = row * slotsPerRow + col;
                    bool used = vertexColorPaletteUsed[slot];
                    bool selected = selectedVertexPaletteSlot == slot;

                    Rect rect = GUILayoutUtility.GetRect(32f, 24f, GUILayout.Width(32), GUILayout.Height(24));
                    Color swatchColor = used ? vertexColorPalette[slot] : new Color(0.2f, 0.2f, 0.2f, 0.85f);
                    EditorGUI.DrawRect(rect, swatchColor);

                    if (!used)
                    {
                        Color oldContent = GUI.contentColor;
                        GUI.contentColor = selected ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
                        GUI.Label(rect, "+", centeredLabel);
                        GUI.contentColor = oldContent;
                    }

                    if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                    {
                        selectedVertexPaletteSlot = slot;
                        if (e.button == 1)
                        {
                            ClearVertexPaletteSlot(slot);
                        }
                        else if (e.button == 0 && used)
                        {
                            vertexPaintColor = vertexColorPalette[slot];
                            SceneView.RepaintAll();
                        }
                        e.Use();
                    }

                    Color border = selected ? Color.white : new Color(0f, 0f, 0f, 0.5f);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
                    EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        [System.Serializable]
        private class VertexPaletteSaveData
        {
            public Color[] colors;
            public bool[] used;
            public int selectedSlot;
            public Color paintColor;
            public Color eraseColor;
            public int colorTarget;
        }

        private void DrawVertexPaletteFileControls()
        {
            EditorGUILayout.LabelField("Palette Files", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            paletteFileName = EditorGUILayout.TextField("Name", paletteFileName);
            if (GUILayout.Button("Save Palette", GUILayout.Width(100)))
            {
                SavePaletteToDisk();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (savedPaletteNames != null && savedPaletteNames.Length > 0)
            {
                selectedSavedPaletteIndex = EditorGUILayout.Popup("Saved", selectedSavedPaletteIndex, savedPaletteNames);
                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    LoadSelectedPaletteFromDisk();
                }
            }
            else
            {
                GUILayout.Label("No saved palettes found", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshSavedPaletteNames();
            }
            EditorGUILayout.EndHorizontal();
        }

        private string GetPaletteDirectoryAbsolutePath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), PaletteRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        }

        private void EnsurePaletteDirectoryExists()
        {
            string directory = GetPaletteDirectoryAbsolutePath();
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private string SanitizePaletteName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "DefaultPalette";
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string result = rawName.Trim();
            foreach (char c in invalidChars)
            {
                result = result.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(result) ? "DefaultPalette" : result;
        }

        private string GetPaletteFilePath(string paletteName)
        {
            return Path.Combine(GetPaletteDirectoryAbsolutePath(), $"{paletteName}.json");
        }

        private void RefreshSavedPaletteNames()
        {
            EnsurePaletteDirectoryExists();
            string directory = GetPaletteDirectoryAbsolutePath();
            string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            List<string> names = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                names.Add(Path.GetFileNameWithoutExtension(files[i]));
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            savedPaletteNames = names.ToArray();

            if (savedPaletteNames.Length == 0)
            {
                selectedSavedPaletteIndex = -1;
                return;
            }

            string preferred = EditorPrefs.GetString(PaletteLastUsedNameKey, string.Empty);
            int preferredIndex = System.Array.IndexOf(savedPaletteNames, preferred);
            if (preferredIndex >= 0)
            {
                selectedSavedPaletteIndex = preferredIndex;
            }
            else if (selectedSavedPaletteIndex < 0 || selectedSavedPaletteIndex >= savedPaletteNames.Length)
            {
                selectedSavedPaletteIndex = 0;
            }
        }

        private void SavePaletteToDisk()
        {
            EnsureVertexPaletteState();
            EnsurePaletteDirectoryExists();

            string paletteName = SanitizePaletteName(paletteFileName);
            paletteFileName = paletteName;
            string filePath = GetPaletteFilePath(paletteName);

            VertexPaletteSaveData data = new VertexPaletteSaveData
            {
                colors = new Color[vertexColorPalette.Length],
                used = new bool[vertexColorPaletteUsed.Length],
                selectedSlot = selectedVertexPaletteSlot,
                paintColor = vertexPaintColor,
                eraseColor = vertexEraseColor,
                colorTarget = (int)vertexColorTarget
            };
            System.Array.Copy(vertexColorPalette, data.colors, vertexColorPalette.Length);
            System.Array.Copy(vertexColorPaletteUsed, data.used, vertexColorPaletteUsed.Length);

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(filePath, json);
            AssetDatabase.Refresh();

            RefreshSavedPaletteNames();
            int loadedIndex = System.Array.IndexOf(savedPaletteNames, paletteName);
            if (loadedIndex >= 0)
            {
                selectedSavedPaletteIndex = loadedIndex;
            }
            EditorPrefs.SetString(PaletteLastUsedNameKey, paletteName);
        }

        private void LoadSelectedPaletteFromDisk()
        {
            if (savedPaletteNames == null || savedPaletteNames.Length == 0) return;
            if (selectedSavedPaletteIndex < 0 || selectedSavedPaletteIndex >= savedPaletteNames.Length) return;

            string paletteName = savedPaletteNames[selectedSavedPaletteIndex];
            string filePath = GetPaletteFilePath(paletteName);
            if (!File.Exists(filePath)) return;

            string json = File.ReadAllText(filePath);
            VertexPaletteSaveData data = JsonUtility.FromJson<VertexPaletteSaveData>(json);
            if (data == null) return;

            EnsureVertexPaletteState();

            if (data.colors != null)
            {
                int count = Mathf.Min(data.colors.Length, vertexColorPalette.Length);
                for (int i = 0; i < count; i++) vertexColorPalette[i] = data.colors[i];
                for (int i = count; i < vertexColorPalette.Length; i++) vertexColorPalette[i] = Color.clear;
            }

            if (data.used != null)
            {
                int count = Mathf.Min(data.used.Length, vertexColorPaletteUsed.Length);
                for (int i = 0; i < count; i++) vertexColorPaletteUsed[i] = data.used[i];
                for (int i = count; i < vertexColorPaletteUsed.Length; i++) vertexColorPaletteUsed[i] = false;
            }

            selectedVertexPaletteSlot = Mathf.Clamp(data.selectedSlot, 0, vertexColorPalette.Length - 1);
            vertexPaintColor = data.paintColor;
            vertexEraseColor = data.eraseColor;
            if (System.Enum.IsDefined(typeof(VertexColorTarget), data.colorTarget))
            {
                vertexColorTarget = (VertexColorTarget)data.colorTarget;
            }
            else
            {
                vertexColorTarget = VertexColorTarget.RGBA;
            }

            paletteFileName = paletteName;
            EditorPrefs.SetString(PaletteLastUsedNameKey, paletteName);
            _owner?.Repaint();
            SceneView.RepaintAll();
        }

        private bool EnsureVertexColorPreviewMaterial()
        {
            if (vertexColorPreviewMaterial != null) return true;

            Shader shader = Shader.Find(VertexColorPreviewShaderName);
            if (shader == null)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(VertexColorPreviewShaderAssetPath);
            }

            if (shader == null) return false;

            vertexColorPreviewMaterial = new Material(shader);
            vertexColorPreviewMaterial.name = "RealBlend_VertexColorPreview_Material";
            vertexColorPreviewMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            return true;
        }

        private void CleanupVertexColorPreviewMaterial()
        {
            if (vertexColorPreviewMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(vertexColorPreviewMaterial);
                vertexColorPreviewMaterial = null;
            }
        }

        private void DrawVertexColorOverlay()
        {
            if (paintWorkflow != PaintWorkflow.VertexColor) return;
            if (!showVertexColorOverlay) return;
            if (!currentMeshFilter || !workingMesh) return;
            if (!EnsureVertexColorPreviewMaterial()) return;

            vertexColorPreviewMaterial.SetFloat("_Opacity", Mathf.Clamp01(vertexColorOverlayOpacity));
            vertexColorPreviewMaterial.SetFloat("_ShowAlpha", vertexColorPreviewMode == VertexColorPreviewMode.Alpha ? 1f : 0f);
            vertexColorPreviewMaterial.SetFloat("_ZTest", vertexColorOverlayXRay
                ? (float)UnityEngine.Rendering.CompareFunction.Always
                : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            float depthBias = vertexColorOverlayXRay ? 0f : -1f;
            vertexColorPreviewMaterial.SetFloat("_DepthBiasFactor", depthBias);
            vertexColorPreviewMaterial.SetFloat("_DepthBiasUnits", depthBias);

            if (vertexColorPreviewMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(workingMesh, currentMeshFilter.transform.localToWorldMatrix);
            }
        }

        private string GetTooltip(PaintLayer pl)
        {
            int idx = (int)pl;
            string mode = layerOverlay[idx] ? "Overlay" : "Blend";
            string ch = layerChannels[idx].ToString();
            if (ch == "None") ch = "Base (Remainder)";
            return $"{mode} Mode on {ch}";
        }

        public void OnSceneGUI(SceneView sceneView)
        {
            EnsureRuntimeState();

            Event e = Event.current;
            HandleScenePointerEvent(e.type);

            if (e.type == EventType.MouseLeaveWindow)
            {
                bool hadPreview = isBrushPreviewing;
                EndPaintStroke();
                if (hadPreview)
                    CleanupAllPreview();
                sceneView.Repaint();
                return;
            }

            bool leftMouseReleased =
                (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) &&
                e.button == 0;
            if (leftMouseReleased && isPaintModeEnabled)
            {
                bool completedStroke = paintStrokeActive;
                EndPaintStroke();
                if (completedStroke)
                    sceneView.Repaint();
            }

            if (!isPaintModeEnabled) return;

            ValidateMeshState();

            if (currentSelection == null || currentMeshFilter == null || workingMesh == null) return;
            if (currentMeshFilter.gameObject == null) return;

            if (HandleBrushWheelShortcut(e))
                return;

            if (meshIsDirty || vertexBuckets == null || storedColors == null)
            {
                BuildSpatialBuckets();
                meshIsDirty = false;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Repaint)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                PhysicsScene pScene = currentMeshFilter.gameObject.scene.GetPhysicsScene();

                if (TryGetPaintHit(ray, pScene, out RaycastHit hit))
                {
                    lastHitPoint = hit.point;
                    lastHitNormal = OrientHitNormal(hit.normal, ray.direction);
                    lastHitTriangleIndex = hit.triangleIndex;
                    int nextHoveredSubmesh =
                        triangleSubmeshLookup != null &&
                        lastHitTriangleIndex >= 0 &&
                        lastHitTriangleIndex < triangleSubmeshLookup.Length
                            ? triangleSubmeshLookup[lastHitTriangleIndex]
                            : ResolveSubmeshForTriangle(
                                workingMesh,
                                lastHitTriangleIndex);
                    hoveredTriangleWinding = ResolveHitTriangleWinding(
                        lastHitTriangleIndex,
                        lastHitNormal);
                    validHit = nextHoveredSubmesh >= 0 &&
                               (paintTargetMode == PaintTargetMode.Automatic ||
                                nextHoveredSubmesh == selectedPaintSubmesh);
                    if (nextHoveredSubmesh != hoveredPaintSubmesh)
                    {
                        hoveredPaintSubmesh = nextHoveredSubmesh;
                        UpdateMaterialTextures();
                        _owner?.Repaint();
                    }
                    if (e.type == EventType.MouseMove) sceneView.Repaint();
                }
                else
                {
                    validHit = false;
                    lastHitTriangleIndex = -1;
                    hoveredTriangleWinding = 1;
                    if (hoveredPaintSubmesh != -1)
                    {
                        hoveredPaintSubmesh = -1;
                        UpdateMaterialTextures();
                        _owner?.Repaint();
                    }
                }
            }

            if (e.type == EventType.Repaint)
            {
                DrawVertexColorOverlay();
            }

            if (!validHit)
            {
                if (isBrushPreviewing)
                {
                    CleanupAllPreview();
                    sceneView.Repaint();
                }
                return;
            }

            float visualAlpha = Mathf.Lerp(0.2f, 1.0f, brushStrength);
            UnityEngine.Rendering.CompareFunction previousHandleZTest = Handles.zTest;
            Color previousHandleColor = Handles.color;
            try
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

                Color brushColor = (brushAlpha != null && invertBrushAlpha) ? new Color(1f, 0.4f, 0.2f, visualAlpha) : new Color(0, 1, 1, visualAlpha);
                Handles.color = brushColor;

                if (brushAlpha != null)
                {
                    Vector3 brushRight = Vector3.Cross(lastHitNormal, Vector3.up);
                    if (brushRight.sqrMagnitude < 0.001f) brushRight = Vector3.Cross(lastHitNormal, Vector3.right);
                    brushRight.Normalize();
                    Vector3 brushUp = Vector3.Cross(brushRight, lastHitNormal).normalized;

                    Quaternion rot = Quaternion.AngleAxis(brushRotation, lastHitNormal);
                    brushRight = rot * brushRight;
                    brushUp = rot * brushUp;

                    Vector3 c1 = lastHitPoint - brushRight * brushSize - brushUp * brushSize;
                    Vector3 c2 = lastHitPoint + brushRight * brushSize - brushUp * brushSize;
                    Vector3 c3 = lastHitPoint + brushRight * brushSize + brushUp * brushSize;
                    Vector3 c4 = lastHitPoint - brushRight * brushSize + brushUp * brushSize;

                    Vector3 offset = lastHitNormal * 0.02f;
                    c1 += offset; c2 += offset; c3 += offset; c4 += offset;

                    Handles.DrawPolyLine(c1, c2, c3, c4, c1);

                    if (brushHardness > 0.05f && brushHardness < 0.95f)
                    {
                        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, visualAlpha * 0.5f);
                        float innerSize = brushSize * brushHardness;

                        Vector3 i1 = lastHitPoint - brushRight * innerSize - brushUp * innerSize + offset;
                        Vector3 i2 = lastHitPoint + brushRight * innerSize - brushUp * innerSize + offset;
                        Vector3 i3 = lastHitPoint + brushRight * innerSize + brushUp * innerSize + offset;
                        Vector3 i4 = lastHitPoint - brushRight * innerSize + brushUp * innerSize + offset;

                        Handles.DrawPolyLine(i1, i2, i3, i4, i1);
                    }
                }
                else
                {
                    Handles.DrawWireDisc(lastHitPoint, lastHitNormal, brushSize);

                    if (brushHardness > 0.05f && brushHardness < 0.95f)
                    {
                        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, visualAlpha * 0.5f);
                        Handles.DrawWireDisc(lastHitPoint, lastHitNormal, brushSize * brushHardness);
                    }
                }
            }
            finally
            {
                Handles.zTest = previousHandleZTest;
                Handles.color = previousHandleColor;
            }

            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            bool isClick = e.button == 0 &&
                           (e.type == EventType.MouseDown ||
                            e.type == EventType.MouseDrag && paintStrokeActive);
            eraseMode = e.shift;

            if (isClick)
            {
                if (noiseModule != null) noiseModule.ClearPreview(workingMesh, storedColors);

                if (isBrushPreviewing)
                {
                    workingMesh.colors = storedColors;
                    workingMesh.UploadMeshData(false);
                    isBrushPreviewing = false;
                }

                // Restore previews before adopting the live geometry. Geometry
                // capture deliberately ignores color, but this ordering also
                // guarantees no future storage extension can serialize a hover
                // preview as real paint.
                if (e.type == EventType.MouseDown &&
                    !EnsurePaintMeshEditableForChange())
                {
                    e.Use();
                    return;
                }

                if (e.type == EventType.MouseDown)
                {
                    BeginPaintStroke(eraseMode);
                }
                else if (limitOpacityPerStroke && eraseMode != strokeEraseMode)
                {
                    // A capped segment needs one stable target. Continue the same
                    // undoable stroke, but start a fresh cap when Shift changes it.
                    ResetCappedStrokeSegment(eraseMode);
                }

                bool useCappedStroke = limitOpacityPerStroke &&
                                       strokeStartColors != null &&
                                       strokeMaxCoverage != null;
                float strokeStrengthMultiplier = GetStrokeStrengthMultiplier(
                    e.type == EventType.MouseDrag,
                    useCappedStroke,
                    mouseSensitivity);
                bool strokeModified = ApplyBrushPaint(
                    lastHitPoint,
                    lastHitNormal,
                    lastHitTriangleIndex,
                    useCappedStroke ? strokeStartColors : storedColors,
                    storedColors,
                    useCappedStroke ? strokeEraseMode : eraseMode,
                    strokeStrengthMultiplier,
                    useCappedStroke ? strokeMaxCoverage : null);

                if (strokeModified)
                {
                    RegisterPaintStrokeUndo();
                    paintStrokeModified = true;
                    workingMesh.colors = storedColors;
                    workingMesh.UploadMeshData(false);
                    sceneView.Repaint();
                }

                e.Use();
            }
            else if (paintMeshLeaseValidated && CanGenerateBrushPreview(e.type))
            {
                if (previewColors == null || previewColors.Length != storedColors.Length)
                    previewColors = new Color[storedColors.Length];

                System.Array.Copy(storedColors, previewColors, storedColors.Length);

                bool modified = ApplyBrushPaint(lastHitPoint, lastHitNormal, lastHitTriangleIndex, previewColors, previewColors, eraseMode);
                if (modified)
                {
                    workingMesh.colors = previewColors;
                    workingMesh.UploadMeshData(false);
                    isBrushPreviewing = true;
                }
                else if (isBrushPreviewing)
                {
                    CleanupAllPreview();
                }
            }
        }

        private void HandleScenePointerEvent(EventType eventType)
        {
            if (eventType == EventType.MouseMove ||
                eventType == EventType.MouseDown)
            {
                // Undo repaints with the cursor still at its old hit point.
                // Wait for fresh Scene-view pointer input before drawing
                // another hover preview, otherwise that stale preview covers
                // the colors that Undo just restored.
                suppressBrushPreviewUntilPointerMove = false;
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
                    brushStrength + direction * 0.05f,
                    0f,
                    1f);
            }
            else
            {
                float scale = Mathf.Pow(1.1f, direction);
                brushSize = Mathf.Clamp(brushSize * scale, 0.1f, 10f);
            }

            _owner?.Repaint();
            SceneView.RepaintAll();
            e.Use();
            return true;
        }

        private bool CanGenerateBrushPreview(EventType eventType)
        {
            return showPreview &&
                   (noiseModule == null || !noiseModule.IsPreviewing) &&
                   !suppressBrushPreviewUntilPointerMove &&
                   (eventType == EventType.MouseMove ||
                    eventType == EventType.Repaint);
        }

        private void SyncMeshCollider(Mesh mesh)
        {
            if (!currentSelection || mesh == null) return;
            MeshCollider meshCollider = currentSelection.GetComponent<MeshCollider>();
            if (!meshCollider) return;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        internal bool EnsurePaintMeshEditableForChange()
        {
            if (currentMeshFilter == null || currentStorage == null ||
                currentMeshFilter.sharedMesh == null)
            {
                return false;
            }

            if (paintMeshLeaseValidated &&
                currentStorage.OwnedEditableMesh == currentMeshFilter.sharedMesh)
            {
                return true;
            }

            Mesh editable = RealBlendEditableMeshUtility.EnsureUniqueForEditing(
                currentMeshFilter,
                ref currentStorage,
                "Prepare Unique Mesh for Painting",
                false,
                out _);
            if (editable == null)
                return false;

            workingMesh = editable;
            // EnsureUniqueForEditing reconciles every successful return path,
            // including an already-owned mesh changed by another editor. Do not
            // immediately repeat the full vertex/index/UV comparison here.
            bool initializedStoredColors = false;
            Color[] desiredColors =
                currentStorage.paintedColors != null &&
                currentStorage.paintedColors.Length == editable.vertexCount
                    ? (Color[])currentStorage.paintedColors.Clone()
                    : storedColors != null &&
                      storedColors.Length == editable.vertexCount
                        ? (Color[])storedColors.Clone()
                        : editable.colors;
            if (desiredColors == null || desiredColors.Length != editable.vertexCount)
                desiredColors = new Color[editable.vertexCount];

            editable.colors = desiredColors;
            editable.UploadMeshData(false);
            storedColors = editable.colors;
            if (currentStorage.paintedColors == null ||
                currentStorage.paintedColors.Length != editable.vertexCount)
            {
                currentStorage.paintedColors = (Color[])storedColors.Clone();
                initializedStoredColors = true;
            }
            if (initializedStoredColors)
            {
                EditorUtility.SetDirty(currentStorage);
                PrefabUtility.RecordPrefabInstancePropertyModifications(currentStorage);
            }
            SyncMeshCollider(editable);
            paintMeshLeaseValidated = true;
            meshIsDirty = true;
            return true;
        }

        private bool TryGetPaintHit(Ray ray, PhysicsScene physicsScene, out RaycastHit selectedHit)
        {
            selectedHit = new RaycastHit();
            if (!physicsScene.IsValid() || currentSelection == null)
                return false;

            bool previousBackfaceSetting = Physics.queriesHitBackfaces;
            int hitCount = 0;
            try
            {
                Physics.queriesHitBackfaces = true;
                hitCount = physicsScene.Raycast(
                    ray.origin,
                    ray.direction,
                    paintRaycastHits,
                    Mathf.Infinity,
                    paintRaycastLayerMask | (1 << currentSelection.layer),
                    QueryTriggerInteraction.Ignore);
            }
            finally
            {
                Physics.queriesHitBackfaces = previousBackfaceSetting;
            }

            float bestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = paintRaycastHits[i];
                if (!hit.collider || hit.collider.gameObject != currentSelection)
                    continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    selectedHit = hit;
                    found = true;
                }
            }

            return found;
        }

        private Vector3 OrientHitNormal(Vector3 normal, Vector3 rayDirection)
        {
            if (normal.sqrMagnitude < 0.000001f)
                return -rayDirection.normalized;

            normal.Normalize();
            return Vector3.Dot(normal, rayDirection) > 0f ? -normal : normal;
        }

        private static int ResolveSubmeshForTriangle(Mesh mesh, int triangleIndex)
        {
            if (mesh == null || triangleIndex < 0)
                return -1;

            long firstTriangle = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                long triangleCount = (long)mesh.GetIndexCount(submesh) / 3L;
                if (triangleIndex < firstTriangle + triangleCount)
                    return submesh;
                firstTriangle += triangleCount;
            }
            return -1;
        }

        private int ResolveHitTriangleWinding(
            int triangleIndex,
            Vector3 orientedHitNormal)
        {
            if (currentMeshFilter == null ||
                originalVertices == null ||
                flattenedPaintTriangles == null)
            {
                return 1;
            }

            int triangleOffset = triangleIndex * 3;
            if (triangleOffset < 0 ||
                triangleOffset + 2 >= flattenedPaintTriangles.Length)
            {
                return 1;
            }

            int a = flattenedPaintTriangles[triangleOffset];
            int b = flattenedPaintTriangles[triangleOffset + 1];
            int c = flattenedPaintTriangles[triangleOffset + 2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= originalVertices.Length ||
                b >= originalVertices.Length ||
                c >= originalVertices.Length)
            {
                return 1;
            }

            Vector3 worldA = currentMeshFilter.transform.TransformPoint(originalVertices[a]);
            Vector3 worldB = currentMeshFilter.transform.TransformPoint(originalVertices[b]);
            Vector3 worldC = currentMeshFilter.transform.TransformPoint(originalVertices[c]);
            Vector3 worldFaceNormal = Vector3.Cross(worldB - worldA, worldC - worldA);
            if (worldFaceNormal.sqrMagnitude < 0.00000001f)
                return 1;

            return Vector3.Dot(worldFaceNormal, orientedHitNormal) < 0f ? -1 : 1;
        }

        void BuildSpatialBuckets()
        {
            if (workingMesh == null) return;

            originalVertices = workingMesh.vertices;
            originalNormals = workingMesh.normals;
            // Geometry caches must not promote a displayed brush/dirt preview
            // into the committed paint used by the next preview or Clear.
            if (storedColors == null || storedColors.Length != originalVertices.Length)
            {
                Color[] savedColors = currentStorage != null ? currentStorage.paintedColors : null;
                storedColors = savedColors != null && savedColors.Length == originalVertices.Length
                    ? (Color[])savedColors.Clone()
                    : workingMesh.colors;
            }
            BuildPaintTopologyCache();

            if (storedColors == null || storedColors.Length != originalVertices.Length)
            {
                Color[] newColors = new Color[originalVertices.Length];
                for (int i = 0; i < newColors.Length; i++) newColors[i] = new Color(0, 0, 0, 0);
                storedColors = newColors;
                workingMesh.colors = storedColors;
            }

            if (previewColors == null || previewColors.Length != storedColors.Length)
                previewColors = new Color[storedColors.Length];

            System.Array.Copy(storedColors, previewColors, storedColors.Length);

            vertexBuckets = new Dictionary<Vector3Int, List<int>>();
            bucketSize = Mathf.Clamp(brushSize, 0.5f, 5.0f);

            for (int i = 0; i < originalVertices.Length; i++)
            {
                if (!currentMeshFilter) break;

                Vector3 worldPos = currentMeshFilter.transform.TransformPoint(originalVertices[i]);
                Vector3Int key = GetBucketID(worldPos);
                if (!vertexBuckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    vertexBuckets[key] = list;
                }
                list.Add(i);
            }
        }

        private void BuildPaintTopologyCache()
        {
            if (workingMesh == null || originalVertices == null)
            {
                incidentPaintFaces = null;
                triangleSubmeshLookup = null;
                flattenedPaintTriangles = null;
                return;
            }

            flattenedPaintTriangles = workingMesh.triangles;
            int flattenedTriangleCount = flattenedPaintTriangles.Length / 3;
            triangleSubmeshLookup = new int[flattenedTriangleCount];
            incidentPaintFaces = new List<PaintFaceReference>[originalVertices.Length];

            int flattenedTriangle = 0;
            for (int submesh = 0; submesh < workingMesh.subMeshCount; submesh++)
            {
                // Apply baseVertex so imported meshes using SubMeshDescriptor
                // offsets produce the same effective indices as
                // Mesh.triangles and MeshCollider.triangleIndex.
                int[] submeshTriangles = workingMesh.GetTriangles(submesh, true);
                for (int offset = 0; offset + 2 < submeshTriangles.Length; offset += 3)
                {
                    if (flattenedTriangle < triangleSubmeshLookup.Length)
                        triangleSubmeshLookup[flattenedTriangle] = submesh;
                    flattenedTriangle++;

                    int a = submeshTriangles[offset];
                    int b = submeshTriangles[offset + 1];
                    int c = submeshTriangles[offset + 2];
                    if (a < 0 || b < 0 || c < 0 ||
                        a >= originalVertices.Length ||
                        b >= originalVertices.Length ||
                        c >= originalVertices.Length)
                    {
                        continue;
                    }

                    Vector3 faceNormal = Vector3.Cross(
                        originalVertices[b] - originalVertices[a],
                        originalVertices[c] - originalVertices[a]);
                    if (faceNormal.sqrMagnitude < 0.00000001f)
                        continue;
                    faceNormal.Normalize();

                    AddIncidentPaintFace(a, submesh, faceNormal);
                    AddIncidentPaintFace(b, submesh, faceNormal);
                    AddIncidentPaintFace(c, submesh, faceNormal);
                }
            }
        }

        private void AddIncidentPaintFace(
            int vertexIndex,
            int submesh,
            Vector3 localNormal)
        {
            List<PaintFaceReference> faces = incidentPaintFaces[vertexIndex];
            if (faces == null)
            {
                faces = new List<PaintFaceReference>(4);
                incidentPaintFaces[vertexIndex] = faces;
            }
            faces.Add(new PaintFaceReference(submesh, localNormal));
        }

        Vector3Int GetBucketID(Vector3 pos)
        {
            return new Vector3Int(
                Mathf.FloorToInt(pos.x / bucketSize),
                Mathf.FloorToInt(pos.y / bucketSize),
                Mathf.FloorToInt(pos.z / bucketSize)
            );
        }

        private static float GetStrokeStrengthMultiplier(
            bool isDragEvent,
            bool isOpacityCapped,
            float sensitivity)
        {
            if (!isDragEvent || isOpacityCapped)
                return 1f;

            return Mathf.Clamp01(sensitivity) * 0.5f;
        }

        bool ApplyBrushPaint(
            Vector3 hitPoint,
            Vector3 hitNormal,
            int hitTriangleIndex,
            Color[] sourceColors,
            Color[] destColors,
            bool isErasing,
            float strengthMultiplier = 1.0f,
            float[] maximumCoverage = null)
        {
            if (vertexBuckets == null ||
                sourceColors == null ||
                destColors == null ||
                originalVertices == null)
            {
                return false;
            }
            strengthMultiplier = Mathf.Clamp01(strengthMultiplier);

            int range = Mathf.CeilToInt(brushSize / bucketSize);
            Vector3Int centerID = GetBucketID(hitPoint);
            bool modified = false;

            Vector3 brushRight = Vector3.Cross(hitNormal, Vector3.up);
            if (brushRight.sqrMagnitude < 0.001f) brushRight = Vector3.Cross(hitNormal, Vector3.right);
            brushRight.Normalize();
            Vector3 brushUp = Vector3.Cross(brushRight, hitNormal).normalized;
            Quaternion rot = Quaternion.AngleAxis(brushRotation, hitNormal);
            brushRight = rot * brushRight;
            brushUp = rot * brushUp;
            Matrix4x4 normalMatrix = currentMeshFilter != null
                ? currentMeshFilter.transform.localToWorldMatrix.inverse.transpose
                : Matrix4x4.identity;

            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        Vector3Int id = centerID + new Vector3Int(x, y, z);
                        if (!vertexBuckets.TryGetValue(id, out List<int> indices)) continue;

                        foreach (int i in indices)
                        {
                            if (!currentMeshFilter) continue;

                            Vector3 vertWorldPos = currentMeshFilter.transform.TransformPoint(originalVertices[i]);
                            float dist = Vector3.Distance(hitPoint, vertWorldPos);

                            if (dist >= brushSize) continue;
                            if (!IsVertexOnHitSurface(
                                    i,
                                    vertWorldPos,
                                    hitPoint,
                                    hitNormal,
                                    normalMatrix))
                            {
                                continue;
                            }

                            float t = dist / brushSize;
                            float falloff;

                            if (brushHardness > 0.99f) falloff = 1.0f;
                            else
                            {
                                float fade = (t - brushHardness) / (1.0f - brushHardness);
                                falloff = 1.0f - Mathf.Clamp01(fade);
                                falloff = Mathf.SmoothStep(0, 1, falloff);
                            }

                            float alphaMod = SampleBrushMask(
                                vertWorldPos - hitPoint,
                                brushRight,
                                brushUp);
                            float coverage = Mathf.Clamp01(
                                falloff * alphaMod * strengthMultiplier);
                            if (coverage <= 0.000001f) continue;

                            if (!TryPromoteStrokeCoverage(
                                    i,
                                    coverage,
                                    maximumCoverage,
                                    out float promotedCoverage))
                            {
                                continue;
                            }

                            float finalStrength = Mathf.Clamp01(
                                brushStrength * promotedCoverage);
                            if (finalStrength <= 0.000001f) continue;

                            if (paintWorkflow == PaintWorkflow.VertexColor)
                            {
                                ApplyVertexColorBlend(i, finalStrength, sourceColors, destColors, isErasing, ref modified);
                            }
                            else
                            {
                                ApplyLayerBlend(i, finalStrength, sourceColors, destColors, isErasing, ref modified);
                            }
                        }
                    }
                }
            }

            return modified;
        }

        private static bool TryPromoteStrokeCoverage(
            int vertexIndex,
            float candidateCoverage,
            float[] maximumCoverage,
            out float promotedCoverage)
        {
            promotedCoverage = Mathf.Clamp01(candidateCoverage);
            if (maximumCoverage == null)
                return true;
            if (vertexIndex < 0 || vertexIndex >= maximumCoverage.Length)
                return false;
            if (promotedCoverage <= maximumCoverage[vertexIndex] + 0.000001f)
                return false;

            maximumCoverage[vertexIndex] = promotedCoverage;
            return true;
        }

        private float SampleBrushMask(
            Vector3 delta,
            Vector3 brushRight,
            Vector3 brushUp)
        {
            if (brushAlpha == null)
                return 1f;
            if (!brushAlpha.isReadable)
                return 0f;

            float u = Vector3.Dot(delta, brushRight) / brushSize;
            float v = Vector3.Dot(delta, brushUp) / brushSize;
            float uvX = u * 0.5f + 0.5f;
            float uvY = v * 0.5f + 0.5f;
            if (uvX < 0f || uvX > 1f || uvY < 0f || uvY > 1f)
                return 0f;

            float sample = brushAlpha.GetPixelBilinear(uvX, uvY).r;
            return invertBrushAlpha ? 1f - sample : sample;
        }

        private void EnableBrushMaskReadWrite()
        {
            if (brushAlpha == null || brushAlpha.isReadable)
                return;

            string assetPath = AssetDatabase.GetAssetPath(brushAlpha);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                EditorUtility.DisplayDialog(
                    "Brush Mask",
                    "This texture does not have an editable Texture Importer. Choose a project texture whose Read/Write setting can be enabled.",
                    "OK");
                return;
            }

            try
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                brushAlpha = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                SceneView.RepaintAll();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Brush Mask",
                    $"Unity could not enable Read/Write for this brush mask:\n{exception.Message}",
                    "OK");
            }
        }

        private bool IsVertexOnHitSurface(int vertexIndex, Vector3 vertexWorldPos, Vector3 hitPoint, Vector3 hitNormal)
        {
            Matrix4x4 normalMatrix = currentMeshFilter != null
                ? currentMeshFilter.transform.localToWorldMatrix.inverse.transpose
                : Matrix4x4.identity;
            return IsVertexOnHitSurface(
                vertexIndex,
                vertexWorldPos,
                hitPoint,
                hitNormal,
                normalMatrix);
        }

        private bool IsVertexOnHitSurface(
            int vertexIndex,
            Vector3 vertexWorldPos,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Matrix4x4 normalMatrix)
        {
            if (vertexIndex < 0 ||
                currentMeshFilter == null ||
                hitNormal.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            Vector3 safeHitNormal = hitNormal.normalized;
            int targetSubmesh = GetEffectivePaintSubmesh();
            bool hasUsableFaceData = incidentPaintFaces != null &&
                                     vertexIndex < incidentPaintFaces.Length &&
                                     incidentPaintFaces[vertexIndex] != null;
            if (hasUsableFaceData)
            {
                bool hasFacingFace = false;
                List<PaintFaceReference> faces = incidentPaintFaces[vertexIndex];
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    PaintFaceReference face = faces[faceIndex];
                    if (face.submesh != targetSubmesh)
                        continue;

                    Vector3 worldFaceNormal = normalMatrix.MultiplyVector(face.localNormal);
                    if (worldFaceNormal.sqrMagnitude < 0.000001f)
                        continue;
                    worldFaceNormal.Normalize();
                    worldFaceNormal *= hoveredTriangleWinding;
                    if (Vector3.Dot(worldFaceNormal, safeHitNormal) > 0.08f)
                    {
                        hasFacingFace = true;
                        break;
                    }
                }

                if (!hasFacingFace)
                    return false;
            }
            else
            {
                if (originalNormals == null || vertexIndex >= originalNormals.Length)
                    return false;
                Vector3 worldNormal = normalMatrix.MultiplyVector(
                    originalNormals[vertexIndex]);
                if (worldNormal.sqrMagnitude < 0.000001f)
                    return false;
                worldNormal.Normalize();
                worldNormal *= hoveredTriangleWinding;
                if (Vector3.Dot(worldNormal, safeHitNormal) <= 0.08f)
                    return false;
            }

            // Keep the brush in a thin shell behind the triangle under the
            // cursor. This prevents a large brush from reaching the other
            // side of a solid wall even when imported normals are smoothed.
            float signedDepth = Vector3.Dot(
                vertexWorldPos - hitPoint,
                safeHitNormal);
            float behindTolerance = Mathf.Clamp(
                brushSize * 0.025f,
                0.003f,
                0.025f);
            return signedDepth >= -behindTolerance;
        }

        private void ApplyVertexColorBlend(int index, float strength, Color[] sourceColors, Color[] destColors, bool isErasing, ref bool modified)
        {
            Color source = sourceColors[index];
            Color target = isErasing ? vertexEraseColor : vertexPaintColor;
            Color result = source;

            float t = Mathf.Clamp01(strength);
            switch (vertexColorTarget)
            {
                case VertexColorTarget.R:
                    result.r = Mathf.Lerp(source.r, target.r, t);
                    break;
                case VertexColorTarget.G:
                    result.g = Mathf.Lerp(source.g, target.g, t);
                    break;
                case VertexColorTarget.B:
                    result.b = Mathf.Lerp(source.b, target.b, t);
                    break;
                case VertexColorTarget.A:
                    result.a = Mathf.Lerp(source.a, target.a, t);
                    break;
                case VertexColorTarget.RGB:
                    result.r = Mathf.Lerp(source.r, target.r, t);
                    result.g = Mathf.Lerp(source.g, target.g, t);
                    result.b = Mathf.Lerp(source.b, target.b, t);
                    break;
                default:
                    result = Color.Lerp(source, target, t);
                    break;
            }

            if (result != source)
            {
                modified = true;
            }

            destColors[index] = result;
        }

        public void ApplyLayerBlend(int index, float strength, Color[] sourceColors, Color[] destColors, bool isErasing, ref bool modified)
        {
            Color c = sourceColors[index];
            Color original = c;

            int layerIdx = (int)activeLayer;
            bool isOverlayMode = layerOverlay[layerIdx];
            Channel layerChannel = layerChannels[layerIdx];

            // Collect weight channels: all channels used by non-overlay layers with channel != None
            List<Channel> weightChannels = new List<Channel>();
            for (int i = 0; i < 5; i++)
            {
                if (!layerOverlay[i] && layerChannels[i] != Channel.None)
                {
                    if (!weightChannels.Contains(layerChannels[i]))
                    {
                        weightChannels.Add(layerChannels[i]);
                    }
                }
            }

            // Get weights
            Dictionary<Channel, float> wDict = new Dictionary<Channel, float>();
            float wSum = 0f;
            foreach (Channel wc in weightChannels)
            {
                float val = GetChannel(c, wc);
                wDict[wc] = val;
                wSum += val;
            }
            float wBase = Mathf.Clamp01(1f - wSum);

            strength = Mathf.Clamp01(strength);

            if (isOverlayMode)
            {
                if (layerChannel == Channel.None) return; // Cannot overlay on none

                float currentVal = GetChannel(c, layerChannel);
                float target = isErasing ? 0f : 1f;
                currentVal = Mathf.Lerp(currentVal, target, strength);
                SetChannel(ref c, layerChannel, currentVal);
            }
            else // Blend mode
            {
                if (layerChannel == Channel.None) // Base layer
                {
                    if (!isErasing)
                    {
                        float available = wSum;
                        if (available > 0f)
                        {
                            float amount = strength * available;
                            foreach (Channel wc in weightChannels)
                            {
                                float prop = wDict[wc] / available;
                                float newVal = wDict[wc] - amount * prop;
                                SetChannel(ref c, wc, newVal);
                            }
                        }
                    }
                    // Erasing base does nothing
                }
                else // Layer with channel
                {
                    if (weightChannels.Contains(layerChannel))
                    {
                        float wThis = wDict[layerChannel];
                        float pool = wBase;
                        foreach (Channel wc in weightChannels)
                        {
                            if (wc != layerChannel) pool += wDict[wc];
                        }

                        if (!isErasing)
                        {
                            if (pool > 0f)
                            {
                                float amount = strength * pool;
                                float newWThis = wThis + amount;
                                SetChannel(ref c, layerChannel, newWThis);

                                float propBase = wBase / pool;
                                // wBase -= amount * propBase; (implicit since we don't store wBase)

                                foreach (Channel wc in weightChannels)
                                {
                                    if (wc != layerChannel)
                                    {
                                        float prop = wDict[wc] / pool;
                                        float newVal = wDict[wc] - amount * prop;
                                        SetChannel(ref c, wc, newVal);
                                    }
                                }
                            }
                        }
                        else
                        {
                            float amount = strength * wThis;
                            SetChannel(ref c, layerChannel, wThis - amount);
                        }
                    }
                }

                // Clamp weights
                foreach (Channel wc in weightChannels)
                {
                    float val = GetChannel(c, wc);
                    SetChannel(ref c, wc, Mathf.Clamp01(val));
                }

                // Normalize if sum > 1
                wSum = 0f;
                foreach (Channel wc in weightChannels)
                {
                    wSum += GetChannel(c, wc);
                }
                if (wSum > 1f)
                {
                    float scale = 1f / wSum;
                    foreach (Channel wc in weightChannels)
                    {
                        float val = GetChannel(c, wc);
                        SetChannel(ref c, wc, val * scale);
                    }
                }
            }

            if (c != original)
                modified = true;
            destColors[index] = c;
        }

        private float GetChannel(Color c, Channel ch)
        {
            switch (ch)
            {
                case Channel.R: return c.r;
                case Channel.G: return c.g;
                case Channel.B: return c.b;
                case Channel.A: return c.a;
                default: return 0f;
            }
        }

        private void SetChannel(ref Color c, Channel ch, float val)
        {
            switch (ch)
            {
                case Channel.R: c.r = val; break;
                case Channel.G: c.g = val; break;
                case Channel.B: c.b = val; break;
                case Channel.A: c.a = val; break;
            }
        }

        void FloodFill(bool fill)
        {
            if (workingMesh == null) return;
            noiseModule?.RestoreCommittedColors(workingMesh, storedColors);
            CleanupAllPreview();
            if (!EnsurePaintMeshEditableForChange()) return;

            Color[] colors = storedColors != null
                ? (Color[])storedColors.Clone()
                : workingMesh.colors;
            if (colors == null || colors.Length == 0) return;
            Color[] colorsBefore = (Color[])colors.Clone();
            bool[] targetMask = paintTargetMode == PaintTargetMode.SelectedSubmesh
                ? BuildSubmeshVertexMask(workingMesh, selectedPaintSubmesh)
                : null;

            if (paintWorkflow == PaintWorkflow.VertexColor)
            {
                Color target = fill ? vertexPaintColor : vertexEraseColor;
                for (int i = 0; i < colors.Length; i++)
                {
                    if (targetMask != null && !targetMask[i])
                        continue;
                    Color col = colors[i];
                    switch (vertexColorTarget)
                    {
                        case VertexColorTarget.R:
                            col.r = target.r;
                            break;
                        case VertexColorTarget.G:
                            col.g = target.g;
                            break;
                        case VertexColorTarget.B:
                            col.b = target.b;
                            break;
                        case VertexColorTarget.A:
                            col.a = target.a;
                            break;
                        case VertexColorTarget.RGB:
                            col.r = target.r;
                            col.g = target.g;
                            col.b = target.b;
                            break;
                        default:
                            col = target;
                            break;
                    }
                    colors[i] = col;
                }

                if (ColorsEqual(colorsBefore, colors))
                    return;

                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(FloodFillUndoName);
                VertexPaintColorUndoState vertexColorUndoState = PrepareColorUndoState(
                    currentMeshFilter,
                    workingMesh,
                    currentStorage,
                    FloodFillUndoName);
                storedColors = colors;
                workingMesh.colors = colors;
                workingMesh.UploadMeshData(false);

                if (currentStorage != null)
                {
                    currentStorage.SavePaintedColors(workingMesh, storedColors);
                    RealBlendProBuilderColorBridge.SyncPaintedColorsToProBuilder(
                        currentMeshFilter,
                        currentStorage,
                        storedColors);
                    EditorUtility.SetDirty(currentStorage);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        currentStorage);
                }
                CommitColorUndoState(vertexColorUndoState, storedColors);

                meshIsDirty = true;
                SceneView.RepaintAll();
                return;
            }

            int layerIdx = (int)activeLayer;
            bool isOverlayMode = layerOverlay[layerIdx];
            Channel layerChannel = layerChannels[layerIdx];

            // Collect weight channels
            List<Channel> weightChannels = new List<Channel>();
            for (int i = 0; i < 5; i++)
            {
                if (!layerOverlay[i] && layerChannels[i] != Channel.None)
                {
                    if (!weightChannels.Contains(layerChannels[i]))
                    {
                        weightChannels.Add(layerChannels[i]);
                    }
                }
            }

            for (int i = 0; i < colors.Length; i++)
            {
                if (targetMask != null && !targetMask[i])
                    continue;
                Color col = colors[i];

                if (isOverlayMode)
                {
                    if (layerChannel == Channel.None) continue;
                    float val = fill ? 1.0f : 0.0f;
                    SetChannel(ref col, layerChannel, val);
                }
                else // Blend
                {
                    if (fill)
                    {
                        if (layerChannel == Channel.None) // Fill base: clear all weights
                        {
                            foreach (Channel wc in weightChannels)
                            {
                                SetChannel(ref col, wc, 0f);
                            }
                        }
                        else // Fill layer: set own to 1, others to 0
                        {
                            SetChannel(ref col, layerChannel, 1f);
                            foreach (Channel wc in weightChannels)
                            {
                                if (wc != layerChannel) SetChannel(ref col, wc, 0f);
                            }
                        }
                    }
                    else // Clear
                    {
                        if (layerChannel == Channel.None) // Clear base: nothing
                        {
                        }
                        else
                        {
                            SetChannel(ref col, layerChannel, 0f);
                        }
                    }
                }

                colors[i] = col;
            }

            if (ColorsEqual(colorsBefore, colors))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(FloodFillUndoName);
            VertexPaintColorUndoState undoState = PrepareColorUndoState(
                currentMeshFilter,
                workingMesh,
                currentStorage,
                FloodFillUndoName);
            storedColors = colors;
            workingMesh.colors = colors;
            workingMesh.UploadMeshData(false);

            if (currentStorage != null)
            {
                currentStorage.SavePaintedColors(workingMesh, storedColors);
                RealBlendProBuilderColorBridge.SyncPaintedColorsToProBuilder(
                    currentMeshFilter,
                    currentStorage,
                    storedColors);
                EditorUtility.SetDirty(currentStorage);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    currentStorage);
            }
            CommitColorUndoState(undoState, storedColors);

            meshIsDirty = true;
            SceneView.RepaintAll();
        }

        private static bool ColorsEqual(Color[] left, Color[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static bool[] BuildSubmeshVertexMask(Mesh mesh, int submesh)
        {
            if (mesh == null)
                return null;

            bool[] mask = new bool[mesh.vertexCount];
            if (submesh < 0 || submesh >= mesh.subMeshCount)
                return mask;

            int[] triangles = mesh.GetTriangles(submesh, true);
            for (int i = 0; i < triangles.Length; i++)
            {
                int vertex = triangles[i];
                if (vertex >= 0 && vertex < mask.Length)
                    mask[vertex] = true;
            }
            return mask;
        }
    }
}
