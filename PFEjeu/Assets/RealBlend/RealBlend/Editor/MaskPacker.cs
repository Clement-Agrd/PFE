using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public class MaskPacker : EditorWindow
    {
        [Header("Target")]
        public MaskMapProfile profile = MaskMapProfile.RealBlend;

        [Header("Inputs (all optional)")]
        public Texture2D metallic;
        public Texture2D ambientOcclusion;
        public Texture2D roughnessOrSmoothness;
        public Texture2D heightMask;
        public Texture2D detailMask;

        [Header("Channel Options")]
        public bool useSmoothnessInput;

        [Header("Defaults When Input Is Missing")]
        [Range(0f, 1f)] public float defaultMetallic;
        [Range(0f, 1f)] public float defaultAO = 1f;
        [Range(0f, 1f)] public float defaultSmoothness = 0.5f;
        [Range(0f, 1f)] public float defaultHeightMask = 1f;
        [Range(0f, 1f)] public float defaultDetailMask = MaskMapPackingUtility.HDRPUnmaskedDetailValue;

        [Header("Output")]
        public string fileName = "MaskMap.png";
        public int overrideWidth;
        public int overrideHeight;
        public bool preserveSourceResolution = true;
        public bool compressOutput;

        [Header("Automatic Folder Conversion")]
        public bool includeSubfolders;
        public bool overwriteExistingAutoMasks;
        public bool createMaterials = true;
        public bool deleteUsedSourceTextures;

        [SerializeField] private DefaultAsset automaticFolder;
        [SerializeField] private string automaticFolderPath;
        [SerializeField] private bool followProjectFolder = true;
        [SerializeField] private bool showOtherProfiles;
        [SerializeField] private bool showManualPacking;
        [SerializeField] private Material standardRealBlendMaterial;
        [SerializeField] private bool showDeprecatedProductionConversion;
        [SerializeField] private string optimizedMaterialName;
        [SerializeField] private bool createNewOptimizedMaterial = true;
        [SerializeField] private string[] optimizedSelectedSetKeys = new string[3];
        [SerializeField] private string[] standardSelectedSetKeys = new string[5];
        [SerializeField] private int optimizedActiveLayerSlot;
        [SerializeField] private string optimizedSelectedLibraryFolder;
        [SerializeField] private bool showOptimizedTextureSetGrid = true;
        [SerializeField] private List<TextureSetOverride> standardTextureOverrides = new List<TextureSetOverride>();
        private Vector2 scrollPosition;
        private GUIStyle textureSetLabelStyle;
        private double nextProjectFolderCheck;

        [Serializable]
        private sealed class TextureSetOverride
        {
            public string Key;
            public Texture2D Albedo;
            public Texture2D Normal;
        }
        private bool IsStandardRealBlend => profile == MaskMapProfile.RealBlend || profile == MaskMapProfile.RealBlend5Way;
        private bool UsesTextureSetPicker => IsStandardRealBlend || profile == MaskMapProfile.RealBlendOptimized;
        private int SelectedLayerCount => profile == MaskMapProfile.RealBlend5Way ? 5 : 3;
        private string[] ActiveSetKeys
        {
            get => IsStandardRealBlend ? standardSelectedSetKeys : optimizedSelectedSetKeys;
            set { if (IsStandardRealBlend) standardSelectedSetKeys = value; else optimizedSelectedSetKeys = value; }
        }

        private sealed class OptimizedTextureSetEntry
        {
            public string Key;
            public DiscoveredMaskSet SourceSet;
            public DiscoveredMaskSet PackedSet;
            public Texture2D Preview;

            public DiscoveredMaskSet DisplaySet => SourceSet ?? PackedSet;
            public bool HasSourceMaps => SourceSet != null;
            public bool HasPackedMask => PackedSet?.PackedMask != null;
        }

        [NonSerialized] private List<OptimizedTextureSetEntry> optimizedTextureSets;
        [NonSerialized] private List<string> optimizedTextureSetWarnings;
        [NonSerialized] private string optimizedTextureSetFolder;
        [NonSerialized] private bool optimizedTextureSetIncludesSubfolders;
        [NonSerialized] private bool optimizedTextureSetDiscoveryDirty = true;
        [NonSerialized] private string optimizedTextureSetDiscoveryError;

        private string BlueChannelName => profile == MaskMapProfile.HDRPLit ? "Detail Mask" : "Height";
        private Texture2D SelectedManualBlueTexture => profile == MaskMapProfile.HDRPLit ? detailMask : heightMask;
        private float SelectedDefaultBlue => profile == MaskMapProfile.HDRPLit ? defaultDetailMask : defaultHeightMask;

        [MenuItem("Tools/RealBlend/Mask Packer")]
        public static void ShowWindow()
        {
            MaskPacker window = GetWindow<MaskPacker>("Mask Packer");
            window.minSize = new Vector2(420f, 520f);
            Rect bounds = window.position;
            bounds.width = Mathf.Max(bounds.width, 640f);
            bounds.height = Mathf.Max(bounds.height, 760f);
            window.position = bounds;
            window.SyncProjectFolder();
            if (Selection.activeObject is Material selectedMaterial &&
                RealBlendProductionMaterialConverter.CanConvert(selectedMaterial, out _))
            {
                window.standardRealBlendMaterial = selectedMaterial;
            }
        }

        private void OnEnable()
        {
            minSize = new Vector2(420f, 520f);
            EnsureOptimizedSelectionStorage();
            optimizedTextureSetDiscoveryDirty = true;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            Selection.selectionChanged -= OnProjectSelectionChanged;
            Selection.selectionChanged += OnProjectSelectionChanged;
            EditorApplication.update -= FollowOpenProjectFolder;
            EditorApplication.update += FollowOpenProjectFolder;
            if (followProjectFolder && SyncProjectFolder()) return;
            if (automaticFolder != null)
            {
                string serializedFolder = MaskMapPackingUtility.NormalizeAssetPath(
                    AssetDatabase.GetAssetPath(automaticFolder));
                if (IsValidAutomaticFolder(serializedFolder))
                {
                    automaticFolderPath = serializedFolder;
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(automaticFolderPath))
            {
                SetAutomaticFolder(GetActiveProjectFolder());
                return;
            }

            automaticFolderPath = MaskMapPackingUtility.NormalizeAssetPath(automaticFolderPath);
            if (IsValidAutomaticFolder(automaticFolderPath))
            {
                automaticFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(automaticFolderPath);
            }
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            Selection.selectionChanged -= OnProjectSelectionChanged;
            EditorApplication.update -= FollowOpenProjectFolder;
        }

        private void OnProjectSelectionChanged()
        {
            nextProjectFolderCheck = 0;
            SyncProjectFolder();
        }

        private void FollowOpenProjectFolder()
        {
            if (!followProjectFolder || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextProjectFolderCheck) return;
            nextProjectFolderCheck = EditorApplication.timeSinceStartup + 0.4;
            SyncProjectFolder();
        }

        private bool SyncProjectFolder()
        {
            if (!followProjectFolder) return false;
            string folder = GetOpenProjectFolder();
            if (string.IsNullOrEmpty(folder)) folder = GetSelectedProjectFolder();
            if (!IsValidAutomaticFolder(folder)) return false;
            if (!string.Equals(folder, automaticFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                SetAutomaticFolder(folder);
                Repaint();
            }
            return true;
        }

        private void OnProjectChanged()
        {
            optimizedTextureSetDiscoveryDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawProfileSection();
            EditorGUILayout.Space(8f);
            DrawAutomaticSection();
            EditorGUILayout.Space(14f);
            DrawSharedPackingSettings();
            EditorGUILayout.Space(8f);
            showManualPacking = EditorGUILayout.Foldout(showManualPacking, "Manual Mask Packing", true);
            if (showManualPacking) DrawManualSection();
            EditorGUILayout.Space(14f);
            DrawStandardMaterialConversion();
            EditorGUILayout.EndScrollView();
        }

        private void DrawProfileSection()
        {
            EditorGUILayout.LabelField("RealBlend Material", EditorStyles.boldLabel);
            int selected = GUILayout.Toolbar(IsStandardRealBlend ? (SelectedLayerCount == 5 ? 1 : 0) : -1,
                new[] { "3 Layers", "5 Layers" }, GUILayout.Height(32f));
            if (selected >= 0)
                SelectProfile(selected == 1 ? MaskMapProfile.RealBlend5Way : MaskMapProfile.RealBlend);
            EditorGUILayout.LabelField("Project Pipeline", GetProjectPipelineLabel() + " (automatic)");

            if (!IsStandardRealBlend) showOtherProfiles = true;
            showOtherProfiles = EditorGUILayout.Foldout(showOtherProfiles, "Other Mask Profiles", true);
            if (showOtherProfiles)
            {
                int other = GUILayout.Toolbar(profile == MaskMapProfile.HDRPLit ? 0 :
                    profile == MaskMapProfile.RealBlendOptimized ? 1 : -1,
                    new[] { "HDRP Lit", "Production (deprecated)" }, GUILayout.Height(26f));
                if (other >= 0)
                    SelectProfile(other == 0 ? MaskMapProfile.HDRPLit : MaskMapProfile.RealBlendOptimized);
            }

            string layout = profile == MaskMapProfile.HDRPLit
                ? "R=Metallic, G=AO, B=Detail Mask, A=Smoothness"
                : "R=Metallic, G=AO, B=Height, A=Smoothness";
            EditorGUILayout.HelpBox(layout, MessageType.Info);
        }

        private void SelectProfile(MaskMapProfile selectedProfile)
        {
            if (selectedProfile == profile) return;
            profile = selectedProfile;
            EnsureOptimizedSelectionStorage();
            optimizedTextureSetDiscoveryDirty = true;
        }

        private static string GetProjectPipelineLabel()
        {
            UnityEngine.Rendering.RenderPipelineAsset pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return "Built-in";
            string name = pipeline.GetType().FullName ?? pipeline.GetType().Name;
            if (name.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0) return "HDRP";
            if (name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0) return "URP";
            return pipeline.GetType().Name;
        }

        private void DrawStandardMaterialConversion()
        {
            if (profile != MaskMapProfile.RealBlendOptimized)
            {
                showDeprecatedProductionConversion = EditorGUILayout.Foldout(
                    showDeprecatedProductionConversion, "Production Conversion (deprecated)", true);
                if (!showDeprecatedProductionConversion) return;
            }
            EditorGUILayout.LabelField("Production Conversion (deprecated)", EditorStyles.boldLabel);
            standardRealBlendMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Standard Material",
                    "A saved three-layer RealBlend Variation material for the active HDRP or URP pipeline."),
                standardRealBlendMaterial,
                typeof(Material),
                false);
            EditorGUILayout.HelpBox(
                "Standard RealBlend is recommended for new materials. This legacy conversion creates a Production material and a dedicated three-slice texture-array library beside the source. " +
                "Layer settings and material flags are copied; the source material and textures are not changed.",
                MessageType.None);

            bool canConvert = RealBlendProductionMaterialConverter.CanConvert(
                standardRealBlendMaterial,
                out string reason);
            if (standardRealBlendMaterial != null && !canConvert)
                EditorGUILayout.HelpBox(reason, MessageType.Warning);

            using (new EditorGUI.DisabledScope(!canConvert))
            {
                if (GUILayout.Button("Convert to Production Material (deprecated)", GUILayout.Height(30f)))
                    ConvertStandardMaterial();
            }
        }

        private void ConvertStandardMaterial()
        {
            try
            {
                if (!RealBlendProductionMaterialConverter.ConfirmMemoryBudget(
                        standardRealBlendMaterial))
                    return;

                EditorUtility.DisplayProgressBar(
                    "Convert RealBlend Material",
                    standardRealBlendMaterial.name,
                    0.5f);
                OptimizedMaterialResult result =
                    RealBlendProductionMaterialConverter.Convert(standardRealBlendMaterial);
                Material output = AssetDatabase.LoadAssetAtPath<Material>(result.MaterialPath);
                if (output != null)
                {
                    Selection.activeObject = output;
                    EditorGUIUtility.PingObject(output);
                }

                string warningSummary = BuildDiagnosticList(result.Warnings, 8);
                string message =
                    $"[Mask Packer] Production material {result.Status}: {result.MaterialPath}";
                if (!string.IsNullOrEmpty(warningSummary))
                    message += "\n" + warningSummary;
                Debug.Log(message, output);
                ShowNotification(new GUIContent(
                    result.Warnings.Count == 0
                        ? "Production material ready"
                        : "Production material ready — see Console notes"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Production Material Conversion Failed",
                    exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawSharedPackingSettings()
        {
            EditorGUILayout.LabelField("Shared Packing Settings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Defaults for Missing Inputs", EditorStyles.miniBoldLabel);
            defaultMetallic = EditorGUILayout.Slider("Default Metallic", defaultMetallic, 0f, 1f);
            defaultAO = EditorGUILayout.Slider("Default AO", defaultAO, 0f, 1f);
            defaultSmoothness = EditorGUILayout.Slider("Default Smoothness", defaultSmoothness, 0f, 1f);
            if (profile == MaskMapProfile.HDRPLit)
            {
                defaultDetailMask = EditorGUILayout.Slider(
                    new GUIContent(
                        "Default Detail Mask",
                        "HDRP normally uses 1 when no Detail Mask exists. Use 0 only when you intentionally want to suppress the Detail Map."),
                    defaultDetailMask,
                    0f,
                    1f);
            }
            else
            {
                defaultHeightMask = EditorGUILayout.Slider("Default Height", defaultHeightMask, 0f, 1f);
            }

            preserveSourceResolution = EditorGUILayout.Toggle(
                new GUIContent(
                    "Preserve Source Resolution",
                    "Temporarily bypasses import size limits while packing so a 4K/8K source is not silently sampled at a lower resolution."),
                preserveSourceResolution);
            compressOutput = EditorGUILayout.Toggle(
                new GUIContent(
                    "Compress Output",
                    "Off preserves mask values exactly. Enable only when smaller GPU memory usage is more important than channel fidelity."),
                compressOutput);

            deleteUsedSourceTextures = EditorGUILayout.Toggle(
                new GUIContent(
                    "Delete Source Channel Textures",
                    "Applies to manual and automatic source-packing actions. After the packed mask and any requested material/arrays succeed, consumed Metallic, AO, Roughness or Smoothness, and Height or Detail Mask inputs are moved to the OS Trash/Recycle Bin. Base Color, Normal, Emissive, generated masks, and skipped inputs are retained."),
                deleteUsedSourceTextures);

            if (deleteUsedSourceTextures)
            {
                EditorGUILayout.HelpBox(
                    "Source cleanup is recoverable from the OS Trash/Recycle Bin, but it is not a Unity Undo operation. You will be asked to confirm before packing. Building from existing packed masks never deletes sources.",
                    MessageType.Warning);
            }

        }

        private void DrawManualSection()
        {
            EditorGUILayout.LabelField("Manual Packing", EditorStyles.boldLabel);
            metallic = (Texture2D)EditorGUILayout.ObjectField("Metallic (R)", metallic, typeof(Texture2D), false);
            ambientOcclusion = (Texture2D)EditorGUILayout.ObjectField("Ambient Occlusion (G)", ambientOcclusion, typeof(Texture2D), false);
            roughnessOrSmoothness = (Texture2D)EditorGUILayout.ObjectField(
                useSmoothnessInput ? "Smoothness (A)" : "Roughness (A = 1-R)",
                roughnessOrSmoothness,
                typeof(Texture2D),
                false);
            if (profile == MaskMapProfile.HDRPLit)
            {
                detailMask = (Texture2D)EditorGUILayout.ObjectField(
                    "Detail Mask (B)", detailMask, typeof(Texture2D), false);
            }
            else
            {
                heightMask = (Texture2D)EditorGUILayout.ObjectField(
                    "Height (B)", heightMask, typeof(Texture2D), false);
            }

            useSmoothnessInput = EditorGUILayout.Toggle(
                new GUIContent(
                    "Input Is Smoothness",
                    "Enabled: use the alpha source directly. Disabled: treat it as roughness and invert it into smoothness."),
                useSmoothnessInput);

            EditorGUILayout.Space(6f);
            overrideWidth = EditorGUILayout.IntField(new GUIContent("Output Width", "0 selects the largest assigned input."), overrideWidth);
            overrideHeight = EditorGUILayout.IntField(new GUIContent("Output Height", "0 selects the largest assigned input."), overrideHeight);
            fileName = EditorGUILayout.TextField("Output File Name", fileName);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Pack and Save PNG", GUILayout.Height(30f)))
            {
                PackManualInputs();
            }

        }

        private void DrawAutomaticSection()
        {
            EditorGUILayout.LabelField("Automatic Folder Conversion", EditorStyles.boldLabel);
            DrawAutomaticFolderSelector();
            string folder = GetAutomaticFolderPath();

            EditorGUILayout.HelpBox(
                profile == MaskMapProfile.RealBlendOptimized
                    ? "For separate source textures, choose their folder and use the first button below. If both Roughness and Gloss exist, Gloss is used directly as Smoothness and Roughness is left untouched."
                    : "Choose the folder containing the separate source textures. If both Roughness and Gloss exist, Gloss is used directly as Smoothness and Roughness is left untouched.",
                MessageType.None);

            bool selectedIncludeSubfolders = EditorGUILayout.Toggle(
                new GUIContent("Include Subfolders", "Each subfolder remains isolated so similarly named texture sets are never mixed."),
                includeSubfolders);
            if (selectedIncludeSubfolders != includeSubfolders)
            {
                includeSubfolders = selectedIncludeSubfolders;
                optimizedTextureSetDiscoveryDirty = true;
            }
            overwriteExistingAutoMasks = EditorGUILayout.Toggle(
                new GUIContent(
                    "Overwrite Existing Masks",
                    "Off skips the profile-specific <TextureSet>_RealBlendMaskMap.png or <TextureSet>_HDRPMaskMap.png instead of creating duplicates."),
                overwriteExistingAutoMasks);
            createMaterials = EditorGUILayout.Toggle(
                new GUIContent(
                    "Create Materials",
                    profile == MaskMapProfile.RealBlendOptimized
                        ? "Builds one shared Production texture-array library per folder. Every detected set receives a stable slice, while the picker chooses which three ordered slices the requested material uses. Multiple materials can safely reuse the same arrays; user-owned assets are never overwritten."
                        : IsStandardRealBlend
                            ? "Creates one RealBlend material from the ordered texture sets below. Only selected source sets are packed."
                            : "Creates one profile-specific material beside each mask and assigns the matching textures. Generated materials are updated in place; user-owned materials are never overwritten."),
                createMaterials);
            bool materialShaderReady = true;
            if (createMaterials)
            {
                try
                {
                    Shader materialShader = profile == MaskMapProfile.RealBlendOptimized
                        ? RealBlendOptimizedMaterialUtility.ResolveOptimizedShader()
                        : MaskMaterialUtility.ResolveMaterialShader(profile);
                    string assignment;
                    if (profile == MaskMapProfile.HDRPLit)
                    {
                        assignment = "Base Color, Normal, Mask Map, and separate Height/Emissive maps when found";
                    }
                    else if (profile == MaskMapProfile.RealBlendOptimized)
                    {
                        assignment = "one Albedo, Normal, and Mask texture-array library per folder; every detected set becomes a stable slice and the picker assigns three ordered material layers";
                    }
                    else
                    {
                        assignment = $"the selected Base Color, Normal, and Mask Map to each of the {SelectedLayerCount} ordered RealBlend layers";
                    }
                    EditorGUILayout.HelpBox(
                        $"Shader: {materialShader.name}\nAssigns: {assignment}.",
                        MessageType.Info);
                }
                catch (Exception exception)
                {
                    materialShaderReady = false;
                    EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                }
            }
            else if (profile == MaskMapProfile.RealBlendOptimized)
            {
                EditorGUILayout.HelpBox(
                    "Create Materials is off. Source conversion will create packed masks only; Production arrays and a material will not be built.",
                    MessageType.Warning);
            }

            bool validFolder = MaskMapPackingUtility.IsWritableAssetFolder(folder) &&
                               AssetDatabase.IsValidFolder(folder);
            bool optimizedSelectionReady = true;
            if (UsesTextureSetPicker && createMaterials)
            {
                EditorGUILayout.Space(6f);
                optimizedSelectionReady = DrawOptimizedTextureSetPicker(folder, validFolder);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(
                       !validFolder ||
                       createMaterials && !materialShaderReady ||
                       UsesTextureSetPicker &&
                       createMaterials &&
                       !optimizedSelectionReady))
            {
                string sourceActionLabel;
                if (profile == MaskMapProfile.RealBlendOptimized)
                {
                    sourceActionLabel = createMaterials
                        ? createNewOptimizedMaterial
                            ? "Pack Source Textures + Create New Production Material (deprecated)"
                            : "Pack Source Textures + Build Production Material (deprecated)"
                        : "Pack Source Textures Only";
                }
                else if (IsStandardRealBlend && createMaterials)
                {
                    sourceActionLabel = $"Pack Selected Sets + Create {SelectedLayerCount}-Layer Material";
                }
                else
                {
                    sourceActionLabel = createMaterials
                        ? "Pack Source Textures + Create Materials"
                        : "Pack Source Textures";
                }

                if (GUILayout.Button(sourceActionLabel, GUILayout.Height(34f)))
                {
                    if (IsStandardRealBlend && createMaterials)
                        BuildStandardSelectedMaterial(folder, false);
                    else
                        AutoConvertFolder(folder);
                }
            }

            if (IsStandardRealBlend)
            {
                using (new EditorGUI.DisabledScope(!validFolder || !createMaterials || !materialShaderReady || !optimizedSelectionReady))
                {
                    if (GUILayout.Button(new GUIContent("Create Material from Selected Packed Masks",
                            "Uses the chosen packed masks without repacking pixels. Texture import settings are configured for Albedo, Normal, and linear mask data."), GUILayout.Height(30f)))
                        BuildStandardSelectedMaterial(folder, true);
                }
            }
            else if (profile == MaskMapProfile.RealBlendOptimized)
            {
                using (new EditorGUI.DisabledScope(
                           !validFolder ||
                           !createMaterials ||
                           !materialShaderReady ||
                           !optimizedSelectionReady))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                createNewOptimizedMaterial
                                    ? "Create New Production Material from Packed Masks (deprecated)"
                                    : "Build Production Material from Packed Masks (deprecated)",
                                "Uses already-packed masks without repacking channels. When Create New is enabled and the shared array manifest already matches, the existing arrays are reused and only a new material is written. Explicit RealBlendMaskMap/RealBlendMask names are trusted. HDRP-labelled Mask Maps and Detail Masks are excluded; ambiguous generic MaskMap/Mask names require confirmation of the RealBlend channel layout."),
                            GUILayout.Height(30f)))
                    {
                        BuildOptimizedFromExistingMasks(folder);
                    }
                }
            }

            EditorGUILayout.HelpBox(
                profile == MaskMapProfile.RealBlendOptimized
                    ? "The first button is for raw AO/Roughness-or-Gloss/Height maps. The second is only for projects that already have packed RealBlend mask maps. Keep each Production texture library in its own folder."
                : IsStandardRealBlend
                    ? "Each texture set is discovered within its own folder. The selected sets are assigned in the displayed Base/Layer order. Existing packed masks are reused; missing masks are packed from the selected source sets."
                    : "Keep each material set consistently named. Automatic conversion skips ambiguous sets and never mixes textures from different folders.",
                MessageType.None);
        }

        private void DrawAutomaticFolderSelector()
        {
            string folder = GetAutomaticFolderPath();
            DefaultAsset displayedFolder = automaticFolder;
            if (displayedFolder == null && IsValidAutomaticFolder(folder))
            {
                displayedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            }

            EditorGUI.BeginChangeCheck();
            DefaultAsset selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Source Folder",
                displayedFolder,
                typeof(DefaultAsset),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                string selectedPath = selectedFolder == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(selectedFolder);
                if (!SetAutomaticFolder(selectedPath))
                {
                    ShowNotification(new GUIContent("Choose a project folder under Assets."));
                }
                else followProjectFolder = false;
            }

            bool follow = EditorGUILayout.Toggle(new GUIContent("Follow Project Folder",
                "Automatically use the folder currently open in the Project browser. Choosing a source folder manually turns this off."), followProjectFolder);
            if (follow != followProjectFolder)
            {
                followProjectFolder = follow;
                if (follow) SyncProjectFolder();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("Use Open Project Folder"))
            {
                followProjectFolder = true;
                if (!SyncProjectFolder())
                {
                    ShowNotification(new GUIContent("Open a folder under Assets in the Project browser first."));
                }
            }
            if (GUILayout.Button("Choose...", GUILayout.Width(82f)))
            {
                ChooseAutomaticFolder();
            }
            EditorGUILayout.EndHorizontal();

            folder = GetAutomaticFolderPath();
            if (IsValidAutomaticFolder(folder))
            {
                EditorGUILayout.LabelField("Path", folder, EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Choose a valid Source Folder under Assets before converting textures.",
                    MessageType.Error);
            }
        }

        private bool DrawOptimizedTextureSetPicker(string folder, bool validFolder)
        {
            EnsureOptimizedSelectionStorage();
            string normalizedFolder = MaskMapPackingUtility.NormalizeAssetPath(folder);
            if (!string.Equals(
                    normalizedFolder,
                    optimizedTextureSetFolder,
                    StringComparison.OrdinalIgnoreCase) ||
                optimizedTextureSetIncludesSubfolders != includeSubfolders)
            {
                optimizedTextureSetDiscoveryDirty = true;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(IsStandardRealBlend ? "RealBlend Material Texture Sets" : "Production Material Texture Sets (deprecated)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Refresh", "Rescan the selected folder for source and packed texture sets."),
                    GUILayout.Width(72f)))
            {
                optimizedTextureSetDiscoveryDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!validFolder)
            {
                EditorGUILayout.HelpBox(
                    "Choose a valid Source Folder to discover texture sets.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return false;
            }

            if (optimizedTextureSetDiscoveryDirty)
                RefreshOptimizedTextureSetDiscovery(normalizedFolder);

            if (!string.IsNullOrEmpty(optimizedTextureSetDiscoveryError))
            {
                EditorGUILayout.HelpBox(optimizedTextureSetDiscoveryError, MessageType.Error);
                EditorGUILayout.EndVertical();
                return false;
            }

            if (optimizedTextureSets == null || optimizedTextureSets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No usable source or packed RealBlend texture sets were detected. " +
                    "Check the file suffixes, Include Subfolders, then press Refresh.",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return false;
            }

            if (!IsStandardRealBlend)
                DrawOptimizedLibraryFolderSelector(normalizedFolder);
            List<OptimizedTextureSetEntry> visibleSets = GetVisibleOptimizedTextureSets();
            EnsureDefaultOptimizedSelections(visibleSets);

            EditorGUILayout.HelpBox(IsStandardRealBlend
                ? $"Detected {visibleSets.Count} texture set(s). Assign a set to each of the {SelectedLayerCount} slots, or use the arrows to change the order. " +
                  "A set can be reused in multiple slots. Only your selected sets are used; each set keeps its own source folder."
                : $"Detected {visibleSets.Count} texture set(s) in this array library. " +
                (visibleSets.Count >= 3
                    ? "Choose three different sets below; their order controls Base, Layer 1, and Layer 2. "
                    : "With fewer than three sets, the available slices are reused automatically across the three shader layers. ") +
                "All detected sets remain in the shared arrays, so changing this order never changes existing slice indices.",
                MessageType.Info);

            DrawOptimizedSelectionSlots(visibleSets);

            if (IsStandardRealBlend)
            {
                EditorGUILayout.LabelField("Albedo / Base Color Picker", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select a thumbnail to assign that set to the active layer. You can also choose or drag an Albedo or Normal texture below a set when an automatic match needs correcting.", MessageType.None);
                DrawOptimizedTextureSetGrid(visibleSets);
            }
            else
            {
                showOptimizedTextureSetGrid = EditorGUILayout.Foldout(showOptimizedTextureSetGrid,
                    "Albedo / Base Color Picker", true);
                if (showOptimizedTextureSetGrid)
                DrawOptimizedTextureSetGrid(visibleSets);
            }

            EditorGUILayout.Space(4f);
            optimizedMaterialName = EditorGUILayout.TextField(
                new GUIContent(
                    "Material Name",
                    "Leave blank to use the folder's profile-specific material name. The .mat extension is optional."),
                optimizedMaterialName ?? string.Empty);
            createNewOptimizedMaterial = EditorGUILayout.Toggle(
                new GUIContent(
                    "Always Create New Asset",
                    IsStandardRealBlend
                        ? "Creates a uniquely named material on every build. When disabled, only a generated material with the same shader can be updated; user-owned collisions are left unchanged."
                        : "Creates a uniquely named material on every build. When disabled, a generated material with the requested name is updated; a protected/user-owned collision falls back to a new unique material instead of blocking."),
                createNewOptimizedMaterial);

            string materialFolder = IsStandardRealBlend ? normalizedFolder : optimizedSelectedLibraryFolder;
            string folderName = Path.GetFileName(materialFolder.TrimEnd('/', '\\'));
            string defaultMaterialName = IsStandardRealBlend
                ? MaskMaterialUtility.GetAutomaticMaterialFileName(folderName, profile)
                : RealBlendOptimizedMaterialUtility.GetOptimizedMaterialFileName(folderName);
            string requestedMaterialName = string.IsNullOrWhiteSpace(optimizedMaterialName)
                ? defaultMaterialName
                : MaskMapPackingUtility.SanitizeFileStem(optimizedMaterialName) + ".mat";
            EditorGUILayout.LabelField(
                "Output",
                MaskMapPackingUtility.NormalizeAssetPath(
                    Path.Combine(materialFolder, requestedMaterialName)),
                EditorStyles.miniLabel);
            if (createNewOptimizedMaterial)
            {
                EditorGUILayout.LabelField(
                    "A numeric suffix is added automatically when that material name already exists.",
                    EditorStyles.miniLabel);
            }

            bool selectionReady = TryGetSelectedOptimizedTextureSets(
                visibleSets,
                out _,
                out string selectionError);
            if (!selectionReady)
                EditorGUILayout.HelpBox(selectionError, MessageType.Warning);

            string warningSummary = BuildDiagnosticList(
                optimizedTextureSetWarnings ?? Enumerable.Empty<string>(),
                4);
            if (!string.IsNullOrEmpty(warningSummary))
            {
                EditorGUILayout.HelpBox(
                    "Discovery notes:\n" + warningSummary,
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
            return selectionReady;
        }

        private void RefreshOptimizedTextureSetDiscovery(string folder)
        {
            optimizedTextureSetDiscoveryDirty = false;
            optimizedTextureSetFolder = folder;
            optimizedTextureSetIncludesSubfolders = includeSubfolders;
            optimizedTextureSetDiscoveryError = string.Empty;
            optimizedTextureSets = new List<OptimizedTextureSetEntry>();
            optimizedTextureSetWarnings = new List<string>();

            try
            {
                MaskSetDiscoveryResult sourceDiscovery =
                    MaskMapPackingUtility.DiscoverMaskSets(
                        folder,
                        includeSubfolders,
                        MaskMapProfile.RealBlendOptimized);
                MaskSetDiscoveryResult packedDiscovery =
                    MaskMapPackingUtility.DiscoverOptimizedExistingMaskSets(
                        folder,
                        includeSubfolders);

                var entriesByKey = new Dictionary<string, OptimizedTextureSetEntry>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (DiscoveredMaskSet set in sourceDiscovery.Sets)
                {
                    string key = GetOptimizedTextureSetKey(set);
                    if (!entriesByKey.TryGetValue(key, out OptimizedTextureSetEntry entry))
                    {
                        entry = new OptimizedTextureSetEntry { Key = key };
                        entriesByKey.Add(key, entry);
                    }
                    entry.SourceSet = set;
                }

                foreach (DiscoveredMaskSet set in packedDiscovery.Sets)
                {
                    string key = GetOptimizedTextureSetKey(set);
                    if (!entriesByKey.TryGetValue(key, out OptimizedTextureSetEntry entry))
                    {
                        entry = new OptimizedTextureSetEntry { Key = key };
                        entriesByKey.Add(key, entry);
                    }
                    entry.PackedSet = set;
                }

                optimizedTextureSets = entriesByKey.Values
                    .OrderBy(entry => entry.DisplaySet.Folder, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.DisplaySet.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (OptimizedTextureSetEntry entry in optimizedTextureSets)
                {
                    MergeTextureAnchors(entry.SourceSet, entry.PackedSet);
                    MergeTextureAnchors(entry.PackedSet, entry.SourceSet);
                    if (IsStandardRealBlend) ApplyTextureSetOverrides(entry);
                    DiscoveredMaskSet displaySet = entry.DisplaySet;
                    bool ambiguousAlbedo = displaySet.AmbiguousMaterialKinds.Contains(
                        MaskTextureKind.BaseColorAnchor);
                    string previewPath = ambiguousAlbedo
                        ? string.Empty
                        : displaySet.BaseColor?.AssetPath;
                    entry.Preview = string.IsNullOrWhiteSpace(previewPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Texture2D>(previewPath);
                }

                optimizedTextureSetWarnings.AddRange(
                    sourceDiscovery.Warnings
                        .Concat(sourceDiscovery.SkippedReasons)
                        .Concat(packedDiscovery.Warnings)
                        .Concat(packedDiscovery.SkippedReasons)
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Distinct());
                if (!IsStandardRealBlend)
                    DrawOptimizedLibraryFolderSelector(folder, false);
                EnsureDefaultOptimizedSelections(GetVisibleOptimizedTextureSets());
            }
            catch (Exception exception)
            {
                optimizedTextureSetDiscoveryError = exception.Message;
                optimizedTextureSets.Clear();
            }
        }

        private void DrawOptimizedLibraryFolderSelector(
            string rootFolder,
            bool drawControl = true)
        {
            string[] folders = (optimizedTextureSets ?? new List<OptimizedTextureSetEntry>())
                .Select(entry => MaskMapPackingUtility.NormalizeAssetPath(entry.DisplaySet.Folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (folders.Length == 0)
            {
                optimizedSelectedLibraryFolder = string.Empty;
                return;
            }

            int currentIndex = Array.FindIndex(
                folders,
                path => string.Equals(
                    path,
                    optimizedSelectedLibraryFolder,
                    StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = Array.FindIndex(
                    folders,
                    path => string.Equals(path, rootFolder, StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0)
                {
                    currentIndex = folders
                        .Select((path, index) => new
                        {
                            Index = index,
                            Count = optimizedTextureSets.Count(entry => string.Equals(
                                MaskMapPackingUtility.NormalizeAssetPath(entry.DisplaySet.Folder),
                                path,
                                StringComparison.OrdinalIgnoreCase))
                        })
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => folders[item.Index], StringComparer.OrdinalIgnoreCase)
                        .First().Index;
                }
                optimizedSelectedLibraryFolder = folders[currentIndex];
                ClearOptimizedSelections();
            }

            if (!drawControl || folders.Length <= 1)
                return;

            string normalizedRoot = MaskMapPackingUtility.NormalizeAssetPath(rootFolder).TrimEnd('/');
            string[] labels = folders.Select(path =>
            {
                string relative = path.StartsWith(
                    normalizedRoot + "/",
                    StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(normalizedRoot.Length + 1)
                    : path;
                int count = optimizedTextureSets.Count(entry => string.Equals(
                    MaskMapPackingUtility.NormalizeAssetPath(entry.DisplaySet.Folder),
                    path,
                    StringComparison.OrdinalIgnoreCase));
                return $"{relative} ({count})";
            }).ToArray();
            int selectedIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Array Library Folder",
                    "Texture arrays are isolated per physical folder. Pick which folder's sets to configure."),
                currentIndex,
                labels);
            if (selectedIndex != currentIndex)
            {
                optimizedSelectedLibraryFolder = folders[selectedIndex];
                ClearOptimizedSelections();
            }
        }

        private List<OptimizedTextureSetEntry> GetVisibleOptimizedTextureSets()
        {
            string selectedFolder = MaskMapPackingUtility.NormalizeAssetPath(
                optimizedSelectedLibraryFolder);
            return (optimizedTextureSets ?? new List<OptimizedTextureSetEntry>())
                .Where(entry => IsStandardRealBlend || string.Equals(
                    MaskMapPackingUtility.NormalizeAssetPath(entry.DisplaySet.Folder),
                    selectedFolder,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.DisplaySet.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void DrawOptimizedSelectionSlots(
            IReadOnlyList<OptimizedTextureSetEntry> visibleSets)
        {
            string[] popupLabels = new[] { "None" }
                .Concat(visibleSets.Select(entry => IsStandardRealBlend && includeSubfolders
                    ? entry.DisplaySet.DisplayName + " — " + entry.DisplaySet.Folder
                    : entry.DisplaySet.DisplayName))
                .ToArray();
            for (int slot = 0; slot < SelectedLayerCount; slot++)
            {
                OptimizedTextureSetEntry selected = visibleSets.FirstOrDefault(
                    entry => string.Equals(
                        entry.Key,
                        ActiveSetKeys[slot],
                        StringComparison.OrdinalIgnoreCase));
                int currentPopupIndex = selected == null
                    ? 0
                    : IndexOfOptimizedTextureSet(visibleSets, selected) + 1;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                Color previousBackground = GUI.backgroundColor;
                if (optimizedActiveLayerSlot == slot)
                    GUI.backgroundColor = new Color(0.42f, 0.72f, 1f, 1f);
                if (GUILayout.Button(
                        GetOptimizedSlotLabel(slot),
                        GUILayout.Width(68f),
                        GUILayout.Height(46f)))
                {
                    optimizedActiveLayerSlot = slot;
                }
                GUI.backgroundColor = previousBackground;

                GUIContent previewContent = selected?.Preview != null
                    ? new GUIContent(selected.Preview, BuildOptimizedTextureSetTooltip(selected))
                    : new GUIContent("?", selected == null
                        ? "No texture set selected."
                        : BuildOptimizedTextureSetTooltip(selected));
                if (GUILayout.Button(
                        previewContent,
                        GUILayout.Width(48f),
                        GUILayout.Height(46f)))
                {
                    optimizedActiveLayerSlot = slot;
                }

                int selectedPopupIndex = EditorGUILayout.Popup(
                    currentPopupIndex,
                    popupLabels,
                    GUILayout.MinWidth(72f));
                if (selectedPopupIndex != currentPopupIndex)
                {
                    AssignOptimizedTextureSet(
                        slot,
                        selectedPopupIndex == 0
                            ? null
                            : visibleSets[selectedPopupIndex - 1]);
                }

                using (new EditorGUI.DisabledScope(slot == 0))
                {
                    if (GUILayout.Button("↑", GUILayout.Width(24f), GUILayout.Height(22f)))
                        SwapOptimizedSelectionSlots(slot, slot - 1);
                }
                using (new EditorGUI.DisabledScope(slot == SelectedLayerCount - 1))
                {
                    if (GUILayout.Button("↓", GUILayout.Width(24f), GUILayout.Height(22f)))
                        SwapOptimizedSelectionSlots(slot, slot + 1);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawOptimizedTextureSetGrid(
            IReadOnlyList<OptimizedTextureSetEntry> visibleSets)
        {
            float availableWidth = Mathf.Max(118f, position.width - 54f);
            const float spacing = 5f;
            int columns = CalculateOptimizedGridColumns(position.width);
            float cardWidth = Mathf.Max(
                88f,
                (availableWidth - spacing * (columns - 1)) / columns);
            if (textureSetLabelStyle == null)
                textureSetLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true
            };

            for (int start = 0; start < visibleSets.Count; start += columns)
            {
                EditorGUILayout.BeginHorizontal();
                int rowCount = Mathf.Min(columns, visibleSets.Count - start);
                for (int column = 0; column < rowCount; column++)
                {
                    OptimizedTextureSetEntry entry = visibleSets[start + column];
                    EditorGUILayout.BeginVertical(GUILayout.Width(cardWidth));
                    string selectionBadge = GetOptimizedSelectionBadge(entry.Key);
                    Color previousBackground = GUI.backgroundColor;
                    if (!string.IsNullOrEmpty(selectionBadge))
                        GUI.backgroundColor = new Color(0.42f, 0.72f, 1f, 1f);
                    GUIContent content = entry.Preview != null
                        ? new GUIContent(entry.Preview, BuildOptimizedTextureSetTooltip(entry))
                        : new GUIContent("Albedo not matched", BuildOptimizedTextureSetTooltip(entry));
                    if (GUILayout.Button(
                            content,
                            GUILayout.Width(cardWidth),
                            GUILayout.Height(Mathf.Clamp(cardWidth * 0.72f, 88f, 144f))))
                    {
                        AssignOptimizedTextureSet(optimizedActiveLayerSlot, entry);
                    }
                    GUI.backgroundColor = previousBackground;
                    GUILayout.Label(
                        string.IsNullOrEmpty(selectionBadge)
                            ? entry.DisplaySet.DisplayName
                            : selectionBadge + " " + entry.DisplaySet.DisplayName,
                        textureSetLabelStyle,
                        GUILayout.Width(cardWidth),
                        GUILayout.MinHeight(30f));
                    if (IsStandardRealBlend) DrawTextureSetOverrides(entry, cardWidth);
                    EditorGUILayout.EndVertical();
                    if (column + 1 < rowCount)
                        GUILayout.Space(spacing);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void MergeTextureAnchors(DiscoveredMaskSet target, DiscoveredMaskSet alternate)
        {
            if (target == null || alternate == null) return;
            if (target.BaseColor == null && !target.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor) &&
                !alternate.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor))
                target.BaseColor = alternate.BaseColor;
            if (target.Normal == null && !target.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor) &&
                !alternate.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor))
                target.Normal = alternate.Normal;
        }

        private void ApplyTextureSetOverrides(OptimizedTextureSetEntry entry)
        {
            TextureSetOverride selection = standardTextureOverrides?.FirstOrDefault(item =>
                string.Equals(item.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
            if (selection == null) return;
            foreach (DiscoveredMaskSet set in new[] { entry.SourceSet, entry.PackedSet })
            {
                ApplyTextureOverride(set, selection.Albedo, MaskTextureKind.BaseColorAnchor);
                ApplyTextureOverride(set, selection.Normal, MaskTextureKind.NormalAnchor);
            }
        }

        private static void ApplyTextureOverride(DiscoveredMaskSet set, Texture2D texture, MaskTextureKind role)
        {
            if (set == null || texture == null) return;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return;
            var source = new DiscoveredMaskTexture { AssetPath = path, Kind = role, MatchScore = 1000 };
            if (role == MaskTextureKind.BaseColorAnchor) set.BaseColor = source;
            else if (role == MaskTextureKind.NormalAnchor) set.Normal = source;
            set.AmbiguousMaterialKinds.Remove(role);
        }

        private void DrawTextureSetOverrides(OptimizedTextureSetEntry entry, float cardWidth)
        {
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 46f;
            try
            {
                EditorGUI.BeginChangeCheck();
                Texture2D albedo = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Albedo",
                    "Choose a saved Albedo/Base Color texture to correct this set. Clearing an override restores automatic matching."),
                    entry.Preview, typeof(Texture2D), false, GUILayout.Width(cardWidth));
                if (EditorGUI.EndChangeCheck()) SetTextureSetOverride(entry.Key, albedo, false);
                Texture2D matchedNormal = entry.DisplaySet.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor)
                    ? null : LoadTexture(entry.DisplaySet.Normal);
                EditorGUI.BeginChangeCheck();
                Texture2D normal = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Normal",
                    "Choose the matching saved Normal texture. Clearing an override restores automatic matching."),
                    matchedNormal, typeof(Texture2D), false, GUILayout.Width(cardWidth));
                if (EditorGUI.EndChangeCheck()) SetTextureSetOverride(entry.Key, normal, true);
            }
            finally { EditorGUIUtility.labelWidth = oldLabelWidth; }

            bool overridden = standardTextureOverrides != null && standardTextureOverrides.Any(item =>
                string.Equals(item.Key, entry.Key, StringComparison.OrdinalIgnoreCase) &&
                (item.Albedo != null || item.Normal != null));
            using (new EditorGUI.DisabledScope(!overridden))
            {
                if (GUILayout.Button("Use Automatic Matches", EditorStyles.miniButton, GUILayout.Width(cardWidth)))
                {
                    standardTextureOverrides.RemoveAll(item => string.Equals(item.Key, entry.Key, StringComparison.OrdinalIgnoreCase));
                    optimizedTextureSetDiscoveryDirty = true;
                    Repaint();
                }
            }
        }

        private void SetTextureSetOverride(string key, Texture2D texture, bool normal)
        {
            if (texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
            {
                ShowNotification(new GUIContent("Choose a saved project texture."));
                return;
            }
            if (standardTextureOverrides == null) standardTextureOverrides = new List<TextureSetOverride>();
            TextureSetOverride selection = standardTextureOverrides.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (selection == null)
            {
                selection = new TextureSetOverride { Key = key };
                standardTextureOverrides.Add(selection);
            }
            if (normal) selection.Normal = texture;
            else selection.Albedo = texture;
            if (selection.Albedo == null && selection.Normal == null) standardTextureOverrides.Remove(selection);
            optimizedTextureSetDiscoveryDirty = true;
            Repaint();
        }

        private static int CalculateOptimizedGridColumns(float windowWidth)
        {
            float availableWidth = Mathf.Max(118f, windowWidth - 54f);
            float preferredWidth = windowWidth < 480f ? 150f : 170f;
            const float spacing = 5f;
            return Mathf.Max(
                1,
                Mathf.FloorToInt(
                    (availableWidth + spacing) /
                    (preferredWidth + spacing)));
        }

        private static int IndexOfOptimizedTextureSet(
            IReadOnlyList<OptimizedTextureSetEntry> entries,
            OptimizedTextureSetEntry target)
        {
            if (entries == null || target == null)
                return -1;
            for (int index = 0; index < entries.Count; index++)
            {
                if (ReferenceEquals(entries[index], target))
                    return index;
            }
            return -1;
        }

        private void EnsureOptimizedSelectionStorage()
        {
            int storageSize = IsStandardRealBlend ? 5 : 3;
            if (ActiveSetKeys == null || ActiveSetKeys.Length != storageSize)
            {
                string[] previous = ActiveSetKeys ?? Array.Empty<string>();
                ActiveSetKeys = new string[storageSize];
                Array.Copy(previous, ActiveSetKeys, Mathf.Min(previous.Length, storageSize));
            }
            optimizedActiveLayerSlot = Mathf.Clamp(optimizedActiveLayerSlot, 0, SelectedLayerCount - 1);
        }

        private void EnsureDefaultOptimizedSelections(
            IReadOnlyList<OptimizedTextureSetEntry> visibleSets)
        {
            EnsureOptimizedSelectionStorage();
            if (IsStandardRealBlend)
            {
                if (visibleSets.Count > 0 && ActiveSetKeys.Take(SelectedLayerCount).All(string.IsNullOrWhiteSpace))
                {
                    for (int slot = 0; slot < SelectedLayerCount; slot++)
                        ActiveSetKeys[slot] = visibleSets[slot % visibleSets.Count].Key;
                }
                return;
            }
            var validKeys = new HashSet<string>(
                visibleSets.Select(entry => entry.Key),
                StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int slot = 0; slot < optimizedSelectedSetKeys.Length; slot++)
            {
                string key = optimizedSelectedSetKeys[slot];
                if (string.IsNullOrWhiteSpace(key) ||
                    !validKeys.Contains(key) ||
                    !selected.Add(key))
                {
                    optimizedSelectedSetKeys[slot] = string.Empty;
                }
            }

            foreach (OptimizedTextureSetEntry entry in visibleSets)
            {
                if (selected.Contains(entry.Key))
                    continue;
                int emptySlot = Array.FindIndex(
                    optimizedSelectedSetKeys,
                    string.IsNullOrWhiteSpace);
                if (emptySlot < 0)
                    break;
                optimizedSelectedSetKeys[emptySlot] = entry.Key;
                selected.Add(entry.Key);
            }
        }

        private void ClearOptimizedSelections()
        {
            EnsureOptimizedSelectionStorage();
            Array.Clear(ActiveSetKeys, 0, ActiveSetKeys.Length);
            optimizedActiveLayerSlot = 0;
        }

        private void AssignOptimizedTextureSet(
            int slot,
            OptimizedTextureSetEntry entry)
        {
            EnsureOptimizedSelectionStorage();
            slot = Mathf.Clamp(slot, 0, SelectedLayerCount - 1);
            string nextKey = entry?.Key ?? string.Empty;
            int previousSlot = Array.FindIndex(
                ActiveSetKeys,
                key => !string.IsNullOrWhiteSpace(nextKey) &&
                       string.Equals(key, nextKey, StringComparison.OrdinalIgnoreCase));
            string replacedKey = ActiveSetKeys[slot];
            ActiveSetKeys[slot] = nextKey;
            if (!IsStandardRealBlend && previousSlot >= 0 && previousSlot != slot)
                ActiveSetKeys[previousSlot] = replacedKey;
            optimizedActiveLayerSlot = (slot + 1) % SelectedLayerCount;
            Repaint();
        }

        private void SwapOptimizedSelectionSlots(int first, int second)
        {
            EnsureOptimizedSelectionStorage();
            if (first < 0 || first >= SelectedLayerCount || second < 0 || second >= SelectedLayerCount)
                return;
            string swap = ActiveSetKeys[first];
            ActiveSetKeys[first] = ActiveSetKeys[second];
            ActiveSetKeys[second] = swap;
            optimizedActiveLayerSlot = second;
        }

        private bool TryGetSelectedOptimizedTextureSets(
            IReadOnlyList<OptimizedTextureSetEntry> visibleSets,
            out List<OptimizedTextureSetEntry> selectedSets,
            out string error)
        {
            selectedSets = new List<OptimizedTextureSetEntry>(SelectedLayerCount);
            error = string.Empty;
            if (!IsStandardRealBlend && (visibleSets == null || visibleSets.Count < 3))
            {
                if (visibleSets == null || visibleSets.Count == 0)
                {
                    error = "No usable texture sets were detected in this physical folder.";
                    return false;
                }
                selectedSets.AddRange(visibleSets);
                return true;
            }

            EnsureOptimizedSelectionStorage();
            if (visibleSets == null || visibleSets.Count == 0)
            {
                error = "No usable texture sets were detected.";
                return false;
            }
            for (int slot = 0; slot < SelectedLayerCount; slot++)
            {
                OptimizedTextureSetEntry entry = visibleSets.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Key,
                        ActiveSetKeys[slot],
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    error = $"Choose a texture set for {GetOptimizedSlotLabel(slot)}.";
                    return false;
                }
                if (!IsStandardRealBlend && selectedSets.Any(selected => string.Equals(
                        selected.Key,
                        entry.Key,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    error = "Choose three different texture sets.";
                    return false;
                }
                selectedSets.Add(entry);
            }
            return true;
        }

        private static string GetOptimizedTextureSetKey(DiscoveredMaskSet set)
        {
            string folder = MaskMapPackingUtility.NormalizeAssetPath(set?.Folder)
                .TrimEnd('/')
                .ToLowerInvariant();
            string group = (set?.GroupKey ?? set?.DisplayName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            return folder + "|" + group;
        }

        private static string GetOptimizedSlotLabel(int slot)
        {
            return slot == 0 ? "Base" : "Layer " + slot;
        }

        private string GetOptimizedSelectionBadge(string key)
        {
            EnsureOptimizedSelectionStorage();
            for (int slot = 0; slot < SelectedLayerCount; slot++)
            {
                if (string.Equals(
                        ActiveSetKeys[slot],
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return (slot + 1) + "·";
                }
            }
            return string.Empty;
        }

        private static string BuildOptimizedTextureSetTooltip(
            OptimizedTextureSetEntry entry)
        {
            DiscoveredMaskSet set = entry.DisplaySet;
            var status = new List<string>(3)
            {
                set.DisplayName,
                entry.HasSourceMaps ? "Raw source maps detected" : "No raw source set detected",
                entry.HasPackedMask ? "Packed RealBlend mask detected" : "Packed mask will be created by the source workflow"
            };
            if (set.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor))
                status.Add("Base Color is ambiguous; it cannot be assigned automatically");
            else if (set.BaseColor == null)
                status.Add("Base Color is missing; the shader uses its default color");
            else
                status.Add(set.BaseColor.AssetPath);
            return string.Join("\n", status);
        }

        private bool TryValidateOptimizedSelectionAgainstSets(
            IReadOnlyCollection<DiscoveredMaskSet> availableSets,
            out string error)
        {
            error = string.Empty;
            List<OptimizedTextureSetEntry> visibleSets = GetVisibleOptimizedTextureSets();
            if (!TryGetSelectedOptimizedTextureSets(
                    visibleSets,
                    out List<OptimizedTextureSetEntry> selectedEntries,
                    out error))
            {
                return false;
            }

            string selectedFolder = MaskMapPackingUtility.NormalizeAssetPath(
                optimizedSelectedLibraryFolder);
            var availableKeys = new HashSet<string>(
                (availableSets ?? Array.Empty<DiscoveredMaskSet>())
                    .Where(set => set != null && string.Equals(
                        MaskMapPackingUtility.NormalizeAssetPath(set.Folder),
                        selectedFolder,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(GetOptimizedTextureSetKey),
                StringComparer.OrdinalIgnoreCase);
            string[] missing = selectedEntries
                .Where(entry => !availableKeys.Contains(entry.Key))
                .Select(entry => entry.DisplaySet.DisplayName)
                .ToArray();
            if (missing.Length == 0)
                return true;

            error = "The current action cannot use the following selected texture set(s): " +
                    string.Join(", ", missing) + ".";
            return false;
        }

        private OptimizedMaterialBuildOptions CreateOptimizedMaterialBuildOptions(
            IReadOnlyList<OptimizedMaskLayerSource> folderLayers,
            bool allowExistingLibraryReuse)
        {
            if (folderLayers == null || folderLayers.Count == 0)
                throw new InvalidOperationException("No Production array layers were provided.");

            string folder = MaskMapPackingUtility.NormalizeAssetPath(
                folderLayers[0].Set.Folder);
            var options = new OptimizedMaterialBuildOptions
            {
                AllowUniqueMaterialFallback = true
            };
            if (!string.Equals(
                    folder,
                    MaskMapPackingUtility.NormalizeAssetPath(optimizedSelectedLibraryFolder),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Include Subfolders keeps a separate canonical material for every
                // other physical folder while the visible picker configures only
                // the explicitly selected library.
                return options;
            }

            options.MaterialName = optimizedMaterialName;
            options.CreateNewMaterial = createNewOptimizedMaterial;
            options.ReuseExistingArrayLibrary =
                allowExistingLibraryReuse && createNewOptimizedMaterial;
            if (folderLayers.Count < 3)
                return options;

            var layersByKey = new Dictionary<string, OptimizedMaskLayerSource>(
                StringComparer.OrdinalIgnoreCase);
            foreach (OptimizedMaskLayerSource layer in folderLayers)
            {
                if (layer?.Set == null)
                    continue;
                string key = GetOptimizedTextureSetKey(layer.Set);
                if (!layersByKey.TryAdd(key, layer))
                {
                    throw new InvalidOperationException(
                        $"The Production library contains duplicate texture-set identity '{key}'.");
                }
            }

            EnsureOptimizedSelectionStorage();
            var selectedLayers = new List<OptimizedMaskLayerSource>(3);
            for (int slot = 0; slot < 3; slot++)
            {
                string key = optimizedSelectedSetKeys[slot];
                if (!layersByKey.TryGetValue(key, out OptimizedMaskLayerSource layer))
                {
                    throw new InvalidOperationException(
                        $"The selected {GetOptimizedSlotLabel(slot)} texture set is no longer available in '{folder}'. Refresh the picker and try again.");
                }
                selectedLayers.Add(layer);
            }

            options.SelectedMaterialLayers = selectedLayers;
            return options;
        }

        private string GetAutomaticFolderPath()
        {
            if (automaticFolder != null)
            {
                string currentPath = MaskMapPackingUtility.NormalizeAssetPath(
                    AssetDatabase.GetAssetPath(automaticFolder));
                if (IsValidAutomaticFolder(currentPath))
                {
                    automaticFolderPath = currentPath;
                    return currentPath;
                }
            }

            return MaskMapPackingUtility.NormalizeAssetPath(automaticFolderPath);
        }

        private bool SetAutomaticFolder(string folder)
        {
            folder = MaskMapPackingUtility.NormalizeAssetPath(folder);
            if (!IsValidAutomaticFolder(folder))
                return false;

            if (IsStandardRealBlend && !string.Equals(folder, automaticFolderPath, StringComparison.OrdinalIgnoreCase))
                ClearOptimizedSelections();
            automaticFolderPath = folder;
            automaticFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            optimizedTextureSetDiscoveryDirty = true;
            Repaint();
            return true;
        }

        private static bool IsValidAutomaticFolder(string folder)
        {
            folder = MaskMapPackingUtility.NormalizeAssetPath(folder);
            return MaskMapPackingUtility.IsWritableAssetFolder(folder) &&
                   AssetDatabase.IsValidFolder(folder);
        }

        private void ChooseAutomaticFolder()
        {
            string currentFolder = GetAutomaticFolderPath();
            string initialFolder = IsValidAutomaticFolder(currentFolder)
                ? ToAbsoluteProjectPath(currentFolder)
                : Application.dataPath;
            string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                "Choose Source Texture Folder",
                initialFolder,
                string.Empty);
            if (string.IsNullOrWhiteSpace(selectedAbsolutePath))
                return;

            string selectedAssetPath = MaskMapPackingUtility.NormalizeAssetPath(
                FileUtil.GetProjectRelativePath(selectedAbsolutePath));
            if (!SetAutomaticFolder(selectedAssetPath))
            {
                ShowNotification(new GUIContent("The Source Folder must be inside this project's Assets folder."));
            }
            else followProjectFolder = false;
        }

        private void PackManualInputs()
        {
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddUsedPath(sourcePaths, metallic);
            AddUsedPath(sourcePaths, ambientOcclusion);
            AddUsedPath(sourcePaths, roughnessOrSmoothness);
            AddUsedPath(sourcePaths, SelectedManualBlueTexture);
            bool deleteAfterPacking = deleteUsedSourceTextures && sourcePaths.Count > 0;
            bool cleanupWasOffered = deleteAfterPacking;
            if (deleteAfterPacking)
            {
                int deletionChoice = EditorUtility.DisplayDialogComplex(
                    "Delete Source Channel Textures?",
                    $"After the mask is successfully written and imported, {sourcePaths.Count} assigned source channel texture(s) will be moved to the OS Trash/Recycle Bin.\n\nThe generated mask is retained. These textures may be referenced elsewhere, and this operation cannot be undone with Unity Undo.",
                    "Pack and Delete Sources",
                    "Cancel",
                    "Pack Without Deleting");
                if (deletionChoice == 1)
                    return;
                if (deletionChoice == 2)
                    deleteAfterPacking = false;
            }

            try
            {
                var request = CreateRequest(
                    metallic,
                    ambientOcclusion,
                    roughnessOrSmoothness,
                    SelectedManualBlueTexture,
                    useSmoothnessInput,
                    GetManualOutputFolder(),
                    fileName,
                    true,
                    true);
                MaskPackingResult result = MaskMapPackingUtility.PackAndSave(request);
                LogWarnings(result.Warnings);
                Texture2D output = AssetDatabase.LoadAssetAtPath<Texture2D>(result.OutputPath);
                int deleted = 0;
                if (deleteAfterPacking)
                {
                    var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        MaskMapPackingUtility.NormalizeAssetPath(result.OutputPath)
                    };
                    deleted = MoveSuccessfulInputsToTrash(sourcePaths, protectedPaths);
                    AssetDatabase.Refresh();
                }
                EditorGUIUtility.PingObject(output);
                string cleanupSummary = deleteAfterPacking
                    ? $"\nSource channel textures moved to Trash/Recycle Bin: {deleted}"
                    : cleanupWasOffered
                        ? "\nSource channel textures retained."
                        : string.Empty;
                EditorUtility.DisplayDialog(
                    "Mask Packer",
                    $"Saved {result.Width} x {result.Height} mask map:\n{result.OutputPath}\n\nInput textures prepared/reimported: {result.InputsReimported}{cleanupSummary}",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Mask Packer", exception.Message, "OK");
            }
        }

        private void BuildOptimizedFromExistingMasks(
            string folder,
            IReadOnlyCollection<DiscoveredMaskSet> authoritativeSets = null)
        {
            bool dedicatedMigration = authoritativeSets == null;
            MaskSetDiscoveryResult discovery;
            if (dedicatedMigration)
            {
                discovery =
                    MaskMapPackingUtility.DiscoverOptimizedExistingMaskSets(folder, includeSubfolders);
                LogWarnings(discovery.Warnings);
            }
            else
            {
                discovery = new MaskSetDiscoveryResult();
                discovery.Sets.AddRange(authoritativeSets.Where(set => set != null));
            }
            if (discovery.Sets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Packed RealBlend Masks Found",
                    BuildExistingMaskDiscoveryFailureMessage(folder, discovery),
                    "OK");
                return;
            }

            if (!TryValidateOptimizedSelectionAgainstSets(
                    discovery.Sets,
                    out string selectionError))
            {
                EditorUtility.DisplayDialog(
                    "Selected Texture Sets Are Not Ready",
                    selectionError +
                    "\n\nUse 'Pack Source Textures + Build Production Material' when a selected set does not have a packed RealBlend mask yet.",
                    "OK");
                optimizedTextureSetDiscoveryDirty = true;
                return;
            }

            List<DiscoveredMaskSet> ambiguousMaskSets = dedicatedMigration
                ? discovery.Sets
                    .Where(set => set.PackedMask.MatchScore < 300)
                    .OrderBy(set => set.PackedMask.AssetPath, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<DiscoveredMaskSet>();
            if (ambiguousMaskSets.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "Confirm Packed Mask Layout",
                    BuildAmbiguousMaskConfirmation(ambiguousMaskSets),
                    "Build Arrays",
                    "Cancel"))
            {
                return;
            }

            if (!ConfirmNewOptimizedFallbackSlices(discovery.Sets))
                return;

            if (!RunOptimizedPreflight(
                    discovery.Sets,
                    "Existing masks and generated Production assets were left unchanged."))
            {
                return;
            }

            int created = 0;
            int updated = 0;
            int skipped = 0;
            int failed = 0;
            int slices = 0;
            Material firstMaterial = null;
            var warnings = new List<string>();
            IGrouping<string, DiscoveredMaskSet>[] folders = discovery.Sets
                .GroupBy(
                    set => MaskMapPackingUtility.NormalizeAssetPath(set.Folder),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            try
            {
                for (int index = 0; index < folders.Length; index++)
                {
                    IGrouping<string, DiscoveredMaskSet> group = folders[index];
                    EditorUtility.DisplayProgressBar(
                        "Build Production RealBlend Arrays",
                        $"Building {group.Key} ({index + 1}/{folders.Length})",
                        folders.Length == 0 ? 1f : (float)index / folders.Length);
                    try
                    {
                        List<OptimizedMaskLayerSource> layers = group
                            .Select(set => new OptimizedMaskLayerSource
                            {
                                Set = set,
                                MaskAssetPath = set.PackedMask.AssetPath,
                                PackedThisRun = false
                            })
                            .ToList();
                        OptimizedMaterialResult result =
                            RealBlendOptimizedMaterialUtility.CreateOrUpdateFolderMaterial(
                                layers,
                                CreateOptimizedMaterialBuildOptions(layers, true));
                        warnings.AddRange(result.Warnings.Select(warning => group.Key + ": " + warning));
                        slices += result.SliceCount;
                        switch (result.Status)
                        {
                            case MaskMaterialWriteStatus.Created:
                                created++;
                                break;
                            case MaskMaterialWriteStatus.Updated:
                                updated++;
                                break;
                            default:
                                skipped++;
                                break;
                        }

                        if (firstMaterial == null)
                            firstMaterial = AssetDatabase.LoadAssetAtPath<Material>(result.MaterialPath);
                        Debug.Log(
                            $"[Mask Packer] Existing-mask migration {result.Status}: " +
                            $"{result.MaterialPath} ({result.SliceCount} array slices, " +
                            $"{result.ShaderName ?? "no shader"})");
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Debug.LogError(
                            $"[Mask Packer] Existing-mask Production build failed for '{group.Key}': {exception}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogWarnings(warnings);
            if (firstMaterial != null)
                EditorGUIUtility.PingObject(firstMaterial);
            EditorUtility.DisplayDialog(
                "Production RealBlend Build Complete",
                $"Packed masks used: {discovery.Sets.Count}\n" +
                $"Array slices built: {slices}\n" +
                $"Materials created: {created}\n" +
                $"Materials updated: {updated}\n" +
                $"Materials skipped/protected: {skipped}\n" +
                $"Folders failed: {failed}\n\n" +
                "Existing packed masks and their source textures were not modified.",
                "OK");
            optimizedTextureSetDiscoveryDirty = true;
            Repaint();
        }

        private static string BuildAmbiguousMaskConfirmation(
            IReadOnlyList<DiscoveredMaskSet> ambiguousMaskSets,
            bool configureImportSettings = false)
        {
            const int displayedPathLimit = 8;
            string paths = string.Join(
                "\n",
                ambiguousMaskSets
                    .Take(displayedPathLimit)
                    .Select(set => "- " + set.PackedMask.AssetPath));
            if (ambiguousMaskSets.Count > displayedPathLimit)
            {
                paths += $"\n- ...and {ambiguousMaskSets.Count - displayedPathLimit} more candidate(s)";
            }

            return $"{ambiguousMaskSets.Count} packed-mask candidate(s) use a generic " +
                   "MaskMap or Mask name. Those names do not prove whether blue contains RealBlend Height " +
                   "or another workflow's data.\n\nCandidates:\n" + paths +
                   "\n\nContinue only if every listed texture uses " +
                   "R=Metallic, G=AO, B=Height, A=Smoothness. " +
                   (configureImportSettings
                       ? "Packed pixels are not changed. Material texture import settings are configured for their roles, including linear mask data."
                       : "The source textures will not be changed.");
        }

        private string BuildSourceDiscoveryMessage(
            string folder,
            MaskSetDiscoveryResult discovery,
            bool includeAdvice)
        {
            int ignoredTextures = Mathf.Max(
                0,
                discovery.TextureCountScanned - discovery.TextureCountRecognized);
            string message =
                $"Source folder: {folder}\n" +
                $"Textures scanned: {discovery.TextureCountScanned}\n" +
                $"Recognized by name: {discovery.TextureCountRecognized}\n" +
                $"Ignored/unassigned: {ignoredTextures}\n" +
                $"Texture sets ready: {discovery.Sets.Count}\n" +
                $"Texture sets skipped: {discovery.SkippedSetCount}";

            string diagnostics = BuildDiagnosticList(
                discovery.SkippedReasons.Concat(discovery.Warnings),
                8);
            if (!string.IsNullOrEmpty(diagnostics))
            {
                message += "\n\nDetails:\n" + diagnostics;
            }

            if (!includeAdvice)
                return message;

            if (discovery.TextureCountScanned == 0)
            {
                message += "\n\nNo Texture2D assets were found. Check the Source Folder and enable Include Subfolders if the maps are nested.";
            }
            else if (discovery.TextureCountRecognized == 0)
            {
                message += "\n\nNo supported suffixes were recognized. Use names such as BaseColor, Normal, AO, Roughness or Gloss, and Height, Displacement, or Bump.";
            }
            else
            {
                message += "\n\nA raw set needs matching names and at least two packable channel maps, or one channel map plus a matching Base Color or Normal. Review the details above for rejected sets.";
            }

            if (profile == MaskMapProfile.RealBlendOptimized)
            {
                message += "\n\nIf this folder contains masks that are already packed as R=Metallic, G=AO, B=Height, A=Smoothness, use 'Build Production Material from Packed Masks' instead.";
            }

            return message;
        }

        private static string BuildExistingMaskDiscoveryFailureMessage(
            string folder,
            MaskSetDiscoveryResult discovery)
        {
            int ignoredTextures = Mathf.Max(
                0,
                discovery.TextureCountScanned - discovery.TextureCountRecognized);
            string message =
                $"Source folder: {folder}\n" +
                $"Textures scanned: {discovery.TextureCountScanned}\n" +
                $"Packed-mask/Base/Normal names recognized: {discovery.TextureCountRecognized}\n" +
                $"Ignored/unassigned: {ignoredTextures}\n" +
                $"Packed mask sets ready: {discovery.Sets.Count}";

            string diagnostics = BuildDiagnosticList(
                discovery.SkippedReasons.Concat(discovery.Warnings),
                8);
            if (!string.IsNullOrEmpty(diagnostics))
            {
                message += "\n\nDetails:\n" + diagnostics;
            }

            return message +
                   "\n\nThis action needs a matching Base Color, Normal, and an already-packed texture ending in RealBlendMaskMap, RealBlendMask, MaskMap, or Mask. " +
                   "For separate AO, Roughness/Gloss, and Height maps, use 'Pack Source Textures + Build Production Material' instead.";
        }

        private static string BuildDiagnosticList(
            IEnumerable<string> diagnostics,
            int displayedLimit)
        {
            string[] items = (diagnostics ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct()
                .ToArray();
            if (items.Length == 0)
                return string.Empty;

            string displayed = string.Join("\n", items.Take(displayedLimit).Select(item => "- " + item));
            if (items.Length > displayedLimit)
            {
                displayed += $"\n- ...and {items.Length - displayedLimit} more (see Console)";
            }

            return displayed;
        }

        private static bool ConfirmNewOptimizedFallbackSlices(
            IEnumerable<DiscoveredMaskSet> candidateSets)
        {
            const int displayedIssueLimit = 8;
            var issues = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DiscoveredMaskSet set in candidateSets ?? Enumerable.Empty<DiscoveredMaskSet>())
            {
                if (set == null)
                    continue;

                string folder = MaskMapPackingUtility.NormalizeAssetPath(set.Folder);
                string maskPath = set.PackedMask != null
                    ? MaskMapPackingUtility.NormalizeAssetPath(set.PackedMask.AssetPath)
                    : MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(
                        folder,
                        MaskMapPackingUtility.GetAutomaticOutputFileName(
                            set.DisplayName,
                            MaskMapProfile.RealBlendOptimized)));

                // Existing manifest entries are validated by the strict preflight below.
                // This confirmation is only for deliberately creating a new fallback slice.
                if (RealBlendOptimizedMaterialUtility.IsMaskReferencedByExistingFolderLibrary(
                        folder,
                        maskPath))
                {
                    continue;
                }

                var missingSources = new List<string>(2);
                if (set.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor))
                {
                    missingSources.Add("Base Color is ambiguous");
                }
                else if (!IsLoadableOptimizedSource(set.BaseColor))
                {
                    missingSources.Add("Base Color is missing or unloadable");
                }

                if (set.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor))
                {
                    missingSources.Add("Normal is ambiguous");
                }
                else if (!IsLoadableOptimizedSource(set.Normal))
                {
                    missingSources.Add("Normal is missing or unloadable");
                }

                if (missingSources.Count == 0)
                    continue;

                string identity = folder + "|" + (set.GroupKey ?? set.DisplayName ?? string.Empty) + "|" + maskPath;
                if (!seen.Add(identity))
                    continue;

                string label = string.IsNullOrWhiteSpace(set.DisplayName)
                    ? Path.GetFileNameWithoutExtension(maskPath)
                    : set.DisplayName;
                issues.Add($"- {label}: {string.Join("; ", missingSources)}\n  {maskPath}");
            }

            if (issues.Count == 0)
                return true;

            string displayedIssues = string.Join("\n", issues.Take(displayedIssueLimit));
            if (issues.Count > displayedIssueLimit)
                displayedIssues += $"\n- ...and {issues.Count - displayedIssueLimit} more new slice(s)";

            return EditorUtility.DisplayDialog(
                "Confirm Fallback Array Slices",
                $"{issues.Count} new Production slice(s) do not have an unambiguous, loadable " +
                "Base Color and Normal:\n\n" + displayedIssues +
                "\n\nThe first build will use white Base Color and/or a flat Normal for these slices. " +
                "After a fallback slice is recorded in the stable manifest, future rebuilds and appends " +
                "will stop without changing the existing arrays until matching, unambiguous Base Color " +
                "and Normal textures are supplied.",
                "Build With Fallbacks",
                "Cancel");
        }

        private static bool IsLoadableOptimizedSource(DiscoveredMaskTexture source)
        {
            string path = source == null
                ? string.Empty
                : MaskMapPackingUtility.NormalizeAssetPath(source.AssetPath);
            return !string.IsNullOrWhiteSpace(path) &&
                   AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
        }

        private void BuildStandardSelectedMaterial(string folder, bool packedMasksOnly)
        {
            try
            {
                // Rescan at execution time so the displayed order cannot bind stale or replaced sets.
                RefreshOptimizedTextureSetDiscovery(folder);
                if (!string.IsNullOrEmpty(optimizedTextureSetDiscoveryError))
                    throw new InvalidOperationException(optimizedTextureSetDiscoveryError);
                if (!TryGetSelectedOptimizedTextureSets(GetVisibleOptimizedTextureSets(),
                        out List<OptimizedTextureSetEntry> orderedEntries, out string selectionError))
                    throw new InvalidOperationException(selectionError);
                MaskMaterialUtility.ResolveMaterialShader(profile);

                List<OptimizedTextureSetEntry> uniqueEntries = orderedEntries
                    .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToList();
                foreach (OptimizedTextureSetEntry entry in uniqueEntries)
                {
                    if ((packedMasksOnly || !entry.HasSourceMaps) && !entry.HasPackedMask)
                        throw new InvalidOperationException($"'{entry.DisplaySet.DisplayName}' has no packed mask. Use Pack Selected Sets first.");
                }

                List<DiscoveredMaskSet> ambiguousMasks = uniqueEntries
                    .Where(entry => (packedMasksOnly || !entry.HasSourceMaps) &&
                                    entry.PackedSet.PackedMask.MatchScore < 300)
                    .Select(entry => entry.PackedSet).ToList();
                if (ambiguousMasks.Count > 0 && !EditorUtility.DisplayDialog(
                        "Confirm RealBlend Mask Layout", BuildAmbiguousMaskConfirmation(ambiguousMasks, true),
                        "Use These Masks", "Cancel"))
                    return;

                bool deleteAfterPacking = deleteUsedSourceTextures && !packedMasksOnly &&
                                          uniqueEntries.Any(entry => entry.HasSourceMaps);
                if (deleteAfterPacking)
                {
                    int choice = EditorUtility.DisplayDialogComplex(
                        "Delete Source Channel Textures?",
                        "Only after every selected mask and the material succeed, consumed Metallic, AO, Roughness/Smoothness, and Height maps packed during this run will move to the OS Trash/Recycle Bin. " +
                        "Base Color, Normal, Emissive, existing masks, skipped source maps, and unselected sets remain. These source maps may be used elsewhere; Unity Undo cannot restore them.",
                        "Pack and Delete Sources", "Cancel", "Pack Without Deleting");
                    if (choice == 1) return;
                    deleteAfterPacking = choice == 0;
                }

                var maskPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var materialSets = new Dictionary<string, DiscoveredMaskSet>(StringComparer.OrdinalIgnoreCase);
                var usedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var warnings = new List<string>();
                foreach (OptimizedTextureSetEntry entry in optimizedTextureSets)
                {
                    foreach (DiscoveredMaskSet set in new[] { entry.SourceSet, entry.PackedSet }.Where(set => set != null))
                    {
                        AddUsedPath(protectedPaths, set.BaseColor);
                        AddUsedPath(protectedPaths, set.Normal);
                        AddUsedPath(protectedPaths, set.Emissive);
                        AddUsedPath(protectedPaths, set.PackedMask);
                        // Unselected sets may share a channel source with a selected set.
                        if (!uniqueEntries.Any(selected => string.Equals(selected.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
                        {
                            AddUsedPath(protectedPaths, set.Metallic);
                            AddUsedPath(protectedPaths, set.AmbientOcclusion);
                            AddUsedPath(protectedPaths, set.SurfaceTexture);
                            AddUsedPath(protectedPaths, set.Height);
                        }
                    }
                }

                int packedCount = 0;
                for (int index = 0; index < uniqueEntries.Count; index++)
                {
                    OptimizedTextureSetEntry entry = uniqueEntries[index];
                    EditorUtility.DisplayProgressBar("Create RealBlend Material", entry.DisplaySet.DisplayName,
                        (float)index / uniqueEntries.Count);
                    DiscoveredMaskSet set = packedMasksOnly || !entry.HasSourceMaps ? entry.PackedSet : entry.SourceSet;
                    string maskPath;
                    if (packedMasksOnly || !entry.HasSourceMaps)
                    {
                        maskPath = set.PackedMask.AssetPath;
                    }
                    else
                    {
                        string outputName = MaskMapPackingUtility.GetAutomaticOutputFileName(set.DisplayName, profile);
                        maskPath = MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(set.Folder, outputName));
                        if (!File.Exists(ToAbsoluteProjectPath(maskPath)) || overwriteExistingAutoMasks)
                        {
                            MaskPackingResult packed = MaskMapPackingUtility.PackAndSave(CreateRequest(
                                LoadTexture(set.Metallic), LoadTexture(set.AmbientOcclusion),
                                LoadTexture(set.SurfaceTexture), LoadTexture(set.Height), set.UsesSmoothnessInput,
                                set.Folder, outputName, false, false));
                            maskPath = packed.OutputPath;
                            warnings.AddRange(packed.Warnings.Select(warning => set.DisplayName + ": " + warning));
                            packedCount++;
                            AddUsedPath(usedSourcePaths, set.Metallic);
                            AddUsedPath(usedSourcePaths, set.AmbientOcclusion);
                            AddUsedPath(usedSourcePaths, set.SurfaceTexture);
                            AddUsedPath(usedSourcePaths, set.Height);
                        }
                    }
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) == null)
                        throw new InvalidOperationException($"The mask for '{set.DisplayName}' could not be loaded: '{maskPath}'. No source textures were deleted.");
                    maskPaths.Add(entry.Key, maskPath);
                    materialSets.Add(entry.Key, set);
                    protectedPaths.Add(MaskMapPackingUtility.NormalizeAssetPath(maskPath));
                }

                MaskMaterialResult result = MaskMaterialUtility.CreateOrUpdateLayeredMaterial(
                    orderedEntries.Select(entry => materialSets[entry.Key]).ToArray(),
                    orderedEntries.Select(entry => maskPaths[entry.Key]).ToArray(), profile, folder,
                    optimizedMaterialName, createNewOptimizedMaterial);
                warnings.AddRange(result.Warnings);
                int deleted = result.IsUsable && deleteAfterPacking
                    ? MoveSuccessfulInputsToTrash(usedSourcePaths, protectedPaths) : 0;
                Material material = AssetDatabase.LoadAssetAtPath<Material>(result.MaterialPath);
                if (result.IsUsable && material != null)
                {
                    Selection.activeObject = material;
                    EditorGUIUtility.PingObject(material);
                }
                LogWarnings(warnings);
                Debug.Log($"[Mask Packer] {SelectedLayerCount}-layer material {result.Status}: {result.MaterialPath}", material);
                EditorUtility.DisplayDialog("RealBlend Material Setup",
                    $"Material: {result.Status}\n{result.MaterialPath}\n\nSelected texture sets: {uniqueEntries.Count}\nMasks packed: {packedCount}\nSource maps moved to Trash: {deleted}" +
                    (warnings.Count > 0 ? "\n\n" + BuildDiagnosticList(warnings, 6) : string.Empty), "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("RealBlend Material Setup Failed",
                    exception.Message + "\n\nSource textures were not deleted. Any masks already packed remain available.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                optimizedTextureSetDiscoveryDirty = true;
                Repaint();
            }
        }

        private void AutoConvertFolder(string folder)
        {
            MaskSetDiscoveryResult discovery = MaskMapPackingUtility.DiscoverMaskSets(folder, includeSubfolders, profile);
            LogWarnings(discovery.Warnings.Concat(discovery.SkippedReasons));
            bool optimizedProfile = createMaterials && profile == MaskMapProfile.RealBlendOptimized;
            var retainedPackedSets = new List<DiscoveredMaskSet>();
            if (optimizedProfile)
            {
                MaskSetDiscoveryResult retainedDiscovery =
                    MaskMapPackingUtility.DiscoverOptimizedExistingMaskSets(folder, includeSubfolders);
                var sourceOutputPaths = new HashSet<string>(
                    discovery.Sets.Select(set => MaskMapPackingUtility.NormalizeAssetPath(
                        Path.Combine(
                            set.Folder,
                            MaskMapPackingUtility.GetAutomaticOutputFileName(
                                set.DisplayName,
                                MaskMapProfile.RealBlendOptimized)))),
                    StringComparer.OrdinalIgnoreCase);
                retainedPackedSets.AddRange(retainedDiscovery.Sets.Where(set =>
                {
                    string maskPath = MaskMapPackingUtility.NormalizeAssetPath(set.PackedMask.AssetPath);
                    if (sourceOutputPaths.Contains(maskPath))
                        return false;

                    // Explicit RealBlendMaskMap names are outputs from this packer.
                    // Less explicit legacy names join automatic append/rebuild only
                    // after their GUID is already recorded in the stable manifest.
                    return set.PackedMask.MatchScore >= 300 ||
                           RealBlendOptimizedMaterialUtility.IsMaskReferencedByExistingFolderLibrary(
                               set.Folder,
                               maskPath);
                }));
                if (retainedPackedSets.Count > 0)
                {
                    var retainedNames = new HashSet<string>(
                        retainedPackedSets.Select(set => set.DisplayName),
                        StringComparer.OrdinalIgnoreCase);
                    LogWarnings(retainedDiscovery.Warnings.Where(warning =>
                        retainedNames.Any(name => warning.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))));
                }
            }

            if (discovery.Sets.Count == 0)
            {
                if (optimizedProfile && retainedPackedSets.Count > 0)
                {
                    BuildOptimizedFromExistingMasks(folder, retainedPackedSets);
                    return;
                }

                EditorUtility.DisplayDialog(
                    "No Source Texture Sets Ready",
                    BuildSourceDiscoveryMessage(folder, discovery, true),
                    "OK");
                return;
            }

            if (discovery.SkippedSetCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "Some Texture Sets Will Be Skipped",
                    BuildSourceDiscoveryMessage(folder, discovery, false) +
                    "\n\nOnly the ready sets will be converted. Continue?",
                    "Convert Ready Sets",
                    "Cancel"))
            {
                return;
            }

            var sourceSetKeys = new HashSet<string>(
                discovery.Sets.Select(GetOptimizedTextureSetKey),
                StringComparer.OrdinalIgnoreCase);
            List<DiscoveredMaskSet> effectiveRetainedPackedSets = optimizedProfile
                ? retainedPackedSets
                    .Where(set => !sourceSetKeys.Contains(GetOptimizedTextureSetKey(set)))
                    .ToList()
                : new List<DiscoveredMaskSet>();
            List<DiscoveredMaskSet> optimizedArraySets = optimizedProfile
                ? discovery.Sets.Concat(effectiveRetainedPackedSets).ToList()
                : discovery.Sets;
            if (optimizedProfile &&
                !TryValidateOptimizedSelectionAgainstSets(
                    optimizedArraySets,
                    out string optimizedSelectionError))
            {
                EditorUtility.DisplayDialog(
                    "Selected Texture Sets Are Not Ready",
                    optimizedSelectionError,
                    "OK");
                optimizedTextureSetDiscoveryDirty = true;
                return;
            }
            if (optimizedProfile && !ConfirmNewOptimizedFallbackSlices(optimizedArraySets))
                return;
            if (optimizedProfile &&
                !RunOptimizedPreflight(optimizedArraySets, "No masks or array assets were changed."))
                return;

            bool deleteAfterPacking = deleteUsedSourceTextures;
            if (deleteAfterPacking)
            {
                string successRequirement;
                if (createMaterials && profile == MaskMapProfile.RealBlendOptimized)
                {
                    successRequirement =
                        "After every mask in a folder and its shared Production array material are successfully created or updated";
                }
                else if (createMaterials)
                {
                    successRequirement =
                        "After each mask and its generated material are successfully created or updated";
                }
                else
                {
                    successRequirement = "After each mask is successfully written and imported";
                }
                int deletionChoice = EditorUtility.DisplayDialogComplex(
                    "Delete Source Channel Textures?",
                    $"The packer found {discovery.Sets.Count} texture set(s). {successRequirement}, its consumed Metallic, AO, Roughness/Smoothness, and {BlueChannelName} source assets will be moved to the OS Trash/Recycle Bin.\n\nBase Color, Normal, Emissive, unused Height maps in HDRP/Lit mode, generated masks/materials, skipped inputs, and other textures will remain.\n\nThese source maps may be referenced elsewhere, and this operation cannot be undone with Unity Undo.",
                    "Pack and Delete Sources",
                    "Cancel",
                    "Pack Without Deleting");
                if (deletionChoice == 1)
                {
                    return;
                }

                deleteAfterPacking = deletionChoice == 0;
            }

            int created = 0;
            int skipped = 0;
            int failed = 0;
            int reimported = 0;
            int materialsCreated = 0;
            int materialsUpdated = 0;
            int materialsSkipped = 0;
            int materialsFailed = 0;
            int optimizedSlicesBuilt = 0;
            UnityEngine.Object firstOutputAsset = null;
            Material firstGeneratedMaterial = null;
            var usedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DiscoveredMaskSet set in optimizedArraySets)
            {
                AddUsedPath(protectedSourcePaths, set.BaseColor);
                AddUsedPath(protectedSourcePaths, set.Normal);
                AddUsedPath(protectedSourcePaths, set.Emissive);
                AddUsedPath(protectedSourcePaths, set.PackedMask);
                protectedSourcePaths.Add(MaskMapPackingUtility.NormalizeAssetPath(
                    Path.Combine(
                        set.Folder,
                        MaskMapPackingUtility.GetAutomaticOutputFileName(set.DisplayName, profile))));
            }
            var summaryWarnings = new List<string>();
            var optimizedLayersByFolder = new Dictionary<string, List<OptimizedMaskLayerSource>>(
                StringComparer.OrdinalIgnoreCase);
            var optimizedFailedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<DiscoveredMaskSet>> optimizedExpectedSetsByFolder = optimizedProfile
                ? optimizedArraySets
                    .GroupBy(
                        set => MaskMapPackingUtility.NormalizeAssetPath(set.Folder),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToList(),
                     StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<DiscoveredMaskSet>>(StringComparer.OrdinalIgnoreCase);

            foreach (DiscoveredMaskSet retainedSet in effectiveRetainedPackedSets)
            {
                string retainedFolder = MaskMapPackingUtility.NormalizeAssetPath(retainedSet.Folder);
                if (!optimizedLayersByFolder.TryGetValue(
                        retainedFolder,
                        out List<OptimizedMaskLayerSource> retainedLayers))
                {
                    retainedLayers = new List<OptimizedMaskLayerSource>();
                    optimizedLayersByFolder.Add(retainedFolder, retainedLayers);
                }
                retainedLayers.Add(new OptimizedMaskLayerSource
                {
                    Set = retainedSet,
                    MaskAssetPath = retainedSet.PackedMask.AssetPath,
                    PackedThisRun = false
                });
            }

            try
            {
                for (int index = 0; index < discovery.Sets.Count; index++)
                {
                    DiscoveredMaskSet set = discovery.Sets[index];
                    EditorUtility.DisplayProgressBar(
                        "Auto Convert Mask Maps",
                        $"Packing {set.DisplayName} ({index + 1}/{discovery.Sets.Count})",
                        (float)index / discovery.Sets.Count);

                    string outputName = MaskMapPackingUtility.GetAutomaticOutputFileName(set.DisplayName, profile);
                    string outputPath = MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(set.Folder, outputName));
                    DiscoveredMaskTexture surface = set.SurfaceTexture;
                    DiscoveredMaskTexture blue = set.GetBlueTexture(profile);
                    bool packedThisRun = false;
                    bool maskReady = false;

                    try
                    {
                        bool existingMask = File.Exists(ToAbsoluteProjectPath(outputPath));
                        if (existingMask && !overwriteExistingAutoMasks)
                        {
                            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
                            if (existing == null)
                            {
                                AssetDatabase.ImportAsset(
                                    outputPath,
                                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                                existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
                            }

                            if (existing == null)
                            {
                                throw new InvalidOperationException(
                                    $"The existing mask is not a Texture2D: '{outputPath}'.");
                            }

                            skipped++;
                            maskReady = true;
                            if (firstOutputAsset == null)
                            {
                                firstOutputAsset = existing;
                            }
                        }
                        else
                        {
                            var request = CreateRequest(
                                LoadTexture(set.Metallic),
                                LoadTexture(set.AmbientOcclusion),
                                LoadTexture(surface),
                                LoadTexture(blue),
                                set.UsesSmoothnessInput,
                                set.Folder,
                                outputName,
                                false,
                                false);
                            MaskPackingResult result = MaskMapPackingUtility.PackAndSave(request);
                            reimported += result.InputsReimported;
                            summaryWarnings.AddRange(result.Warnings.Select(warning => set.DisplayName + ": " + warning));

                            Texture2D packedMaskAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(result.OutputPath);
                            if (packedMaskAsset == null)
                            {
                                throw new InvalidOperationException(
                                    $"Unity did not import the generated mask '{result.OutputPath}'.");
                            }

                            if (firstOutputAsset == null)
                            {
                                firstOutputAsset = packedMaskAsset;
                            }

                            outputPath = result.OutputPath;
                            created++;
                            packedThisRun = true;
                            maskReady = true;
                            Debug.Log(BuildSetLog(set, result.OutputPath, profile));
                        }
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        if (optimizedProfile)
                        {
                            optimizedFailedFolders.Add(
                                MaskMapPackingUtility.NormalizeAssetPath(set.Folder));
                        }
                        Debug.LogError($"[Mask Packer] Failed to pack '{set.DisplayName}': {exception}");
                        continue;
                    }

                    bool materialSucceeded = !createMaterials;
                    if (createMaterials && maskReady)
                    {
                        if (profile == MaskMapProfile.RealBlendOptimized)
                        {
                            string optimizedFolder =
                                MaskMapPackingUtility.NormalizeAssetPath(set.Folder);
                            if (!optimizedLayersByFolder.TryGetValue(
                                    optimizedFolder,
                                    out List<OptimizedMaskLayerSource> folderLayers))
                            {
                                folderLayers = new List<OptimizedMaskLayerSource>();
                                optimizedLayersByFolder.Add(optimizedFolder, folderLayers);
                            }
                            folderLayers.Add(new OptimizedMaskLayerSource
                            {
                                Set = set,
                                MaskAssetPath = outputPath,
                                PackedThisRun = packedThisRun
                            });
                            // Folder-level array material success is known only after every slice
                            // in this folder has been collected.
                            materialSucceeded = false;
                        }
                        else
                        {
                            try
                            {
                                MaskMaterialResult materialResult = MaskMaterialUtility.CreateOrUpdateMaterial(
                                    set,
                                    profile,
                                    outputPath);
                                summaryWarnings.AddRange(materialResult.Warnings.Select(
                                    warning => set.DisplayName + ": " + warning));
                                switch (materialResult.Status)
                                {
                                    case MaskMaterialWriteStatus.Created:
                                        materialsCreated++;
                                        break;
                                    case MaskMaterialWriteStatus.Updated:
                                        materialsUpdated++;
                                        break;
                                    default:
                                        materialsSkipped++;
                                        break;
                                }

                                materialSucceeded = materialResult.IsUsable;
                                if (firstGeneratedMaterial == null && materialResult.IsUsable)
                                {
                                    firstGeneratedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                                        materialResult.MaterialPath);
                                }
                                Debug.Log(
                                    $"[Mask Packer] Material {materialResult.Status}: {materialResult.MaterialPath} " +
                                    $"({materialResult.ShaderName ?? "no shader"})");
                            }
                            catch (Exception exception)
                            {
                                materialsFailed++;
                                materialSucceeded = false;
                                Debug.LogError(
                                    $"[Mask Packer] Mask was created, but material creation failed for '{set.DisplayName}': {exception}");
                            }
                        }
                    }

                    if (packedThisRun && materialSucceeded)
                    {
                        AddUsedPath(usedSourcePaths, set.Metallic);
                        AddUsedPath(usedSourcePaths, set.AmbientOcclusion);
                        AddUsedPath(usedSourcePaths, surface);
                        AddUsedPath(usedSourcePaths, blue);
                    }
                }

                if (createMaterials && profile == MaskMapProfile.RealBlendOptimized)
                {
                    KeyValuePair<string, List<DiscoveredMaskSet>>[] folderGroups =
                        optimizedExpectedSetsByFolder
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    for (int groupIndex = 0; groupIndex < folderGroups.Length; groupIndex++)
                    {
                        string groupKey = folderGroups[groupIndex].Key;
                        optimizedLayersByFolder.TryGetValue(
                            groupKey,
                            out List<OptimizedMaskLayerSource> folderLayers);
                        folderLayers = folderLayers ?? new List<OptimizedMaskLayerSource>();
                        EditorUtility.DisplayProgressBar(
                            "Build Production RealBlend Arrays",
                            $"Building {groupKey} ({groupIndex + 1}/{folderGroups.Length})",
                            folderGroups.Length == 0 ? 1f : (float)groupIndex / folderGroups.Length);
                        if (optimizedFailedFolders.Contains(groupKey) ||
                            folderLayers.Count != folderGroups[groupIndex].Value.Count)
                        {
                            materialsFailed++;
                            Debug.LogError(
                                $"[Mask Packer] Production array update for '{groupKey}' was aborted because " +
                                "at least one expected mask slice failed. Existing arrays and slice indices " +
                                "were left unchanged.");
                            continue;
                        }
                        try
                        {
                            OptimizedMaterialResult materialResult =
                                RealBlendOptimizedMaterialUtility.CreateOrUpdateFolderMaterial(
                                    folderLayers,
                                    CreateOptimizedMaterialBuildOptions(folderLayers, false));
                            optimizedSlicesBuilt += materialResult.SliceCount;
                            summaryWarnings.AddRange(materialResult.Warnings.Select(
                                warning => groupKey + ": " + warning));
                            switch (materialResult.Status)
                            {
                                case MaskMaterialWriteStatus.Created:
                                    materialsCreated++;
                                    break;
                                case MaskMaterialWriteStatus.Updated:
                                    materialsUpdated++;
                                    break;
                                default:
                                    materialsSkipped++;
                                    break;
                            }

                            if (materialResult.IsUsable)
                            {
                                if (firstGeneratedMaterial == null)
                                {
                                    firstGeneratedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                                        materialResult.MaterialPath);
                                }

                                // Only inputs consumed by a mask written in this run are safe to
                                // clean up. An existing skipped mask may be stale relative to its
                                // source maps, so those sources must remain untouched.
                                foreach (OptimizedMaskLayerSource layer in folderLayers)
                                {
                                    if (!layer.PackedThisRun)
                                        continue;
                                    AddUsedPath(usedSourcePaths, layer.Set.Metallic);
                                    AddUsedPath(usedSourcePaths, layer.Set.AmbientOcclusion);
                                    AddUsedPath(usedSourcePaths, layer.Set.SurfaceTexture);
                                    AddUsedPath(usedSourcePaths, layer.Set.GetBlueTexture(profile));
                                }
                            }

                            Debug.Log(
                                $"[Mask Packer] Production material {materialResult.Status}: " +
                                $"{materialResult.MaterialPath} ({materialResult.SliceCount} array slices, " +
                                $"{materialResult.ShaderName ?? "no shader"})");
                        }
                        catch (Exception exception)
                        {
                            materialsFailed++;
                            Debug.LogError(
                                $"[Mask Packer] Masks were created, but Production array/material creation failed for '{groupKey}': {exception}");
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogWarnings(summaryWarnings);
            int deleted = deleteAfterPacking
                ? MoveSuccessfulInputsToTrash(usedSourcePaths, protectedSourcePaths)
                : 0;
            AssetDatabase.Refresh();
            UnityEngine.Object primaryOutput = firstGeneratedMaterial != null
                ? firstGeneratedMaterial
                : firstOutputAsset;
            if (primaryOutput != null)
            {
                EditorGUIUtility.PingObject(primaryOutput);
            }

            string materialSummary = createMaterials
                ? $"\nMaterials created: {materialsCreated}\nMaterials updated: {materialsUpdated}\nMaterials skipped/protected: {materialsSkipped}\nMaterials failed: {materialsFailed}"
                : string.Empty;
            if (optimizedProfile)
            {
                materialSummary += $"\nProduction array slices built: {optimizedSlicesBuilt}";
            }

            string[] completionWarnings = discovery.Warnings
                .Concat(discovery.SkippedReasons)
                .Concat(summaryWarnings)
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct()
                .ToArray();
            string warningSummary = completionWarnings.Length == 0
                ? string.Empty
                : $"\n\nWarnings and automatic choices ({completionWarnings.Length}):\n" +
                  BuildDiagnosticList(completionWarnings, 6);
            EditorUtility.DisplayDialog(
                "Automatic Mask Packing Complete",
                $"Source folder: {folder}\n" +
                $"Textures scanned: {discovery.TextureCountScanned}\n" +
                $"Texture sets processed: {discovery.Sets.Count}\n" +
                $"Texture sets skipped: {discovery.SkippedSetCount}\n\n" +
                $"Masks created/overwritten: {created}\nMasks skipped existing: {skipped}\nMasks failed: {failed}" +
                materialSummary +
                $"\nInputs prepared/reimported: {reimported}\nSource maps moved to Trash: {deleted}" +
                warningSummary,
                "OK");
            optimizedTextureSetDiscoveryDirty = true;
            Repaint();
        }

        private MaskPackingRequest CreateRequest(
            Texture2D metallicInput,
            Texture2D aoInput,
            Texture2D surfaceInput,
            Texture2D blueInput,
            bool smoothnessInput,
            string outputFolder,
            string outputName,
            bool useManualDimensions,
            bool uniquePath)
        {
            return new MaskPackingRequest
            {
                Metallic = metallicInput,
                AmbientOcclusion = aoInput,
                RoughnessOrSmoothness = surfaceInput,
                BlueMask = blueInput,
                UseSmoothnessInput = smoothnessInput,
                DefaultMetallic = defaultMetallic,
                DefaultAmbientOcclusion = defaultAO,
                DefaultSmoothness = defaultSmoothness,
                DefaultBlue = SelectedDefaultBlue,
                OverrideWidth = useManualDimensions ? overrideWidth : 0,
                OverrideHeight = useManualDimensions ? overrideHeight : 0,
                PreserveSourceResolution = preserveSourceResolution,
                RejectMismatchedDimensions = !useManualDimensions,
                CompressOutput = compressOutput,
                OutputFolder = string.IsNullOrEmpty(outputFolder) ? "Assets" : outputFolder,
                OutputFileName = outputName,
                GenerateUniquePath = uniquePath
            };
        }

        private string GetMostCommonInputFolder()
        {
            return new[] { metallic, ambientOcclusion, roughnessOrSmoothness, SelectedManualBlueTexture }
                .Where(texture => texture != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(MaskMapPackingUtility.GetAssetFolder)
                .Where(MaskMapPackingUtility.IsWritableAssetFolder)
                .GroupBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault();
        }

        private string GetManualOutputFolder()
        {
            string inputFolder = GetMostCommonInputFolder();
            if (!string.IsNullOrEmpty(inputFolder))
            {
                return inputFolder;
            }

            string configuredFolder = GetAutomaticFolderPath();
            return IsValidAutomaticFolder(configuredFolder)
                ? configuredFolder
                : GetActiveProjectFolder();
        }

        private static string GetActiveProjectFolder()
        {
            string openFolder = GetOpenProjectFolder();
            if (!string.IsNullOrEmpty(openFolder)) return openFolder;
            string selectedFolder = GetSelectedProjectFolder();
            return !string.IsNullOrEmpty(selectedFolder) ? selectedFolder : "Assets";
        }

        private static string GetOpenProjectFolder()
        {
            try
            {
                MethodInfo method = typeof(ProjectWindowUtil).GetMethod(
                    "GetActiveFolderPath",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                string activeFolder = method?.Invoke(null, null) as string;
                activeFolder = MaskMapPackingUtility.NormalizeAssetPath(activeFolder);
                if (MaskMapPackingUtility.IsWritableAssetFolder(activeFolder) && AssetDatabase.IsValidFolder(activeFolder))
                {
                    return activeFolder;
                }
            }
            catch
            {
                // Older Editor versions can fall back to the selected asset's folder.
            }

            return string.Empty;
        }

        private static string GetSelectedProjectFolder()
        {
            foreach (UnityEngine.Object selected in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
            {
                string path = MaskMapPackingUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(selected));
                if (!AssetDatabase.IsValidFolder(path))
                {
                    path = MaskMapPackingUtility.GetAssetFolder(path);
                }

                if (MaskMapPackingUtility.IsWritableAssetFolder(path) && AssetDatabase.IsValidFolder(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static Texture2D LoadTexture(DiscoveredMaskTexture texture)
        {
            return texture == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texture.AssetPath);
        }

        private static bool RunOptimizedPreflight(
            IReadOnlyCollection<DiscoveredMaskSet> sets,
            string unchangedMessage)
        {
            try
            {
                if (!SystemInfo.supports2DArrayTextures)
                {
                    throw new InvalidOperationException(
                        "The current graphics device does not support Texture2DArray resources.");
                }

                RealBlendOptimizedMaterialUtility.ResolveOptimizedShader();
                int largestFolder = sets
                    .GroupBy(
                        set => MaskMapPackingUtility.NormalizeAssetPath(set.Folder),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Count())
                    .DefaultIfEmpty(0)
                    .Max();
                if (largestFolder > SystemInfo.maxTextureArraySlices)
                {
                    throw new InvalidOperationException(
                        $"A discovered folder needs {largestFolder} slices, but the current graphics " +
                        $"device supports at most {SystemInfo.maxTextureArraySlices}.");
                }

                foreach (string discoveredFolder in sets
                             .Select(set => MaskMapPackingUtility.NormalizeAssetPath(set.Folder))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    // Material naming is handled by the picker options and may
                    // intentionally create a unique asset. Preflight only the
                    // shared array destinations here so an existing/protected
                    // material can never block that workflow.
                    RealBlendOptimizedMaterialUtility.ValidateFolderArrayDestinations(
                        discoveredFolder);
                }

                long estimatedArrayBytes = EstimateOptimizedArrayBytes(
                    sets,
                    MaskMapProfile.RealBlendOptimized);
                const long hardArrayLimit = 2L * 1024L * 1024L * 1024L;
                if (estimatedArrayBytes > hardArrayLimit)
                {
                    throw new InvalidOperationException(
                        "The discovered folders would create approximately " +
                        $"{FormatByteCount(estimatedArrayBytes)} of mipmapped RGBA32 array data. " +
                        "Split the texture sets into smaller folders or reduce their source resolution first.");
                }

                const long largeArrayWarning = 256L * 1024L * 1024L;
                if (estimatedArrayBytes >= largeArrayWarning &&
                    !EditorUtility.DisplayDialog(
                        "Large Production Texture Arrays",
                        "The discovered folders are estimated to create about " +
                        $"{FormatByteCount(estimatedArrayBytes)} of mipmapped RGBA32 texture-array data.\n\n" +
                        "Texture arrays reduce sampler bindings, but every slice still consumes texture memory. " +
                        "Consider separating unrelated sets into folders or lowering source resolution.",
                        "Continue",
                        "Cancel"))
                {
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Production RealBlend Preflight Failed",
                    exception.Message + "\n\n" + unchangedMessage,
                    "OK");
                return false;
            }
        }

        private static long EstimateOptimizedArrayBytes(
            IEnumerable<DiscoveredMaskSet> sets,
            MaskMapProfile targetProfile)
        {
            long total = 0L;
            foreach (IGrouping<string, DiscoveredMaskSet> folder in sets.GroupBy(
                         set => MaskMapPackingUtility.NormalizeAssetPath(set.Folder),
                         StringComparer.OrdinalIgnoreCase))
            {
                int sliceCount = folder.Count();
                Vector2Int albedoSize = MaxDimensions(folder.Select(set => set.BaseColor));
                Vector2Int normalSize = MaxDimensions(folder.Select(set => set.Normal));
                Vector2Int maskSize = MaxDimensions(folder.SelectMany(set =>
                    set.PackedMask != null
                        ? new[] { set.PackedMask }
                        : new[]
                        {
                            set.Metallic,
                            set.AmbientOcclusion,
                            set.SurfaceTexture,
                            set.GetBlueTexture(targetProfile)
                        }));
                total += EstimateMipmappedRgba32Bytes(albedoSize, sliceCount);
                total += EstimateMipmappedRgba32Bytes(normalSize, sliceCount);
                total += EstimateMipmappedRgba32Bytes(maskSize, sliceCount);
            }
            return total;
        }

        private static Vector2Int MaxDimensions(IEnumerable<DiscoveredMaskTexture> textures)
        {
            int width = 4;
            int height = 4;
            foreach (DiscoveredMaskTexture texture in textures.Where(value => value != null))
            {
                string path = MaskMapPackingUtility.NormalizeAssetPath(texture.AssetPath);
                try
                {
                    if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                    {
                        importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                        width = Mathf.Max(width, sourceWidth);
                        height = Mathf.Max(height, sourceHeight);
                        continue;
                    }
                }
                catch
                {
                    // Fall through to the imported dimensions.
                }

                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (imported != null)
                {
                    width = Mathf.Max(width, imported.width);
                    height = Mathf.Max(height, imported.height);
                }
            }
            int maximum = Mathf.Max(32, SystemInfo.maxTextureSize);
            return new Vector2Int(Mathf.Min(width, maximum), Mathf.Min(height, maximum));
        }

        private static long EstimateMipmappedRgba32Bytes(Vector2Int size, int sliceCount)
        {
            // A complete mip chain is approximately 4/3 of mip zero.
            double bytes = (double)size.x * size.y * 4.0 * Math.Max(1, sliceCount) * (4.0 / 3.0);
            return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(bytes);
        }

        private static string FormatByteCount(long bytes)
        {
            const double gigabyte = 1024.0 * 1024.0 * 1024.0;
            if (bytes >= gigabyte)
                return (bytes / gigabyte).ToString("0.00") + " GiB";
            return (bytes / (1024.0 * 1024.0)).ToString("0") + " MiB";
        }

        private static void AddUsedPath(HashSet<string> paths, DiscoveredMaskTexture texture)
        {
            if (texture != null && !string.IsNullOrEmpty(texture.AssetPath))
            {
                paths.Add(MaskMapPackingUtility.NormalizeAssetPath(texture.AssetPath));
            }
        }

        private static void AddUsedPath(HashSet<string> paths, Texture2D texture)
        {
            if (texture == null)
                return;
            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path))
                paths.Add(MaskMapPackingUtility.NormalizeAssetPath(path));
        }

        private static int MoveSuccessfulInputsToTrash(
            IEnumerable<string> paths,
            ISet<string> protectedPaths)
        {
            int moved = 0;
            foreach (string path in paths.OrderBy(path => path))
            {
                string normalizedPath = MaskMapPackingUtility.NormalizeAssetPath(path);
                if (protectedPaths != null && protectedPaths.Contains(normalizedPath))
                {
                    Debug.LogWarning(
                        $"[Mask Packer] Retained protected Base Color, Normal, Emissive, or packed mask asset: {normalizedPath}");
                    continue;
                }

                bool success;
                try
                {
                    success = AssetDatabase.MoveAssetToTrash(normalizedPath);
                }
                catch (Exception exception)
                {
                    success = false;
                    Debug.LogError($"[Mask Packer] Could not remove source texture '{normalizedPath}': {exception.Message}");
                }

                if (success)
                {
                    moved++;
                }
                else
                {
                    Debug.LogWarning($"[Mask Packer] Source texture was not removed: {normalizedPath}");
                }
            }

            return moved;
        }

        private static string BuildSetLog(DiscoveredMaskSet set, string outputPath, MaskMapProfile targetProfile)
        {
            string surfaceLabel = set.UsesSmoothnessInput ? "Smoothness" : "Roughness";
            return $"[Mask Packer] {set.DisplayName}\n" +
                   $"  Metallic: {FileNameOrDefault(set.Metallic)}\n" +
                   $"  AO: {FileNameOrDefault(set.AmbientOcclusion)}\n" +
                   $"  {surfaceLabel}: {FileNameOrDefault(set.SurfaceTexture)}\n" +
                   $"  Blue: {FileNameOrDefault(set.GetBlueTexture(targetProfile))}\n" +
                   $"  Output: {outputPath}";
        }

        private static string FileNameOrDefault(DiscoveredMaskTexture texture)
        {
            return texture == null ? "<default>" : Path.GetFileName(texture.AssetPath);
        }

        private static void LogWarnings(IEnumerable<string> warnings)
        {
            foreach (string warning in warnings.Distinct())
            {
                Debug.LogWarning("[Mask Packer] " + warning);
            }
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }
}
