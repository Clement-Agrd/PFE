using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    public sealed class OptimizedMaskLayerSource
    {
        public DiscoveredMaskSet Set;
        public string MaskAssetPath;
        public bool PackedThisRun;

        /// <summary>
        /// Optional stable identity for callers that are not driven by folder discovery.
        /// Folder conversion continues to use the packed-mask GUID when this is empty.
        /// </summary>
        public string StableSliceKey;
    }

    public sealed class OptimizedMaterialResult
    {
        public string MaterialPath;
        public string AlbedoArrayPath;
        public string NormalArrayPath;
        public string MaskArrayPath;
        public string ShaderName;
        public int SliceCount;
        public int[] SelectedSliceIndices;
        public MaskMaterialWriteStatus Status;
        public readonly List<string> Warnings = new List<string>();

        public bool IsUsable => Status == MaskMaterialWriteStatus.Created ||
                                Status == MaskMaterialWriteStatus.Updated;
    }

    /// <summary>
    /// Controls the material written against a shared per-folder array library.
    /// Library membership remains independent from the three ordered material
    /// layers so any number of materials can reuse the same arrays safely.
    /// </summary>
    public sealed class OptimizedMaterialBuildOptions
    {
        public string MaterialName;
        public string LibraryStem;
        public bool CreateNewMaterial;
        public bool AllowUniqueMaterialFallback = true;
        public bool ReuseExistingArrayLibrary;
        public bool AllowFallbackSourcesForExistingSlices;
        public bool OverrideMissingMaskFallback;
        public Color MissingMaskFallback;
        public bool PreserveSourceImportSettings;
        public Material PropertySourceMaterial;
        public IReadOnlyList<OptimizedMaskLayerSource> SelectedMaterialLayers;
    }

    /// <summary>
    /// Creates a shared texture-array library for all discovered texture sets in one folder.
    /// The material exposes three slice selectors, while the arrays may contain more slices so
    /// artists can duplicate the material and select other combinations without duplicating GPU
    /// texture bindings.
    /// </summary>
    public static class RealBlendOptimizedMaterialUtility
    {
        public const string GeneratedArrayLabel = "RealBlend.MaskPacker.GeneratedTextureArray";
        private const string SliceManifestTag = "RealBlendArraySliceManifest";
        private const int SliceManifestVersion = 1;

        private const string OptimizedHdrpShaderPath =
            "Assets/RealBlend/Art/Shaders/Optimized/HDRP/RealBlendProductionHDRP.shadergraph";
        private const string OptimizedUrpShaderPath =
            "Assets/RealBlend/Art/Shaders/Optimized/URP/RealBlendProductionURP.shadergraph";
        private const string PackerShaderPath =
            "Assets/RealBlend/MaskPacking/Editor/TextureArrayPacker.shader";

        private enum ArrayRole
        {
            Albedo,
            Normal,
            Mask
        }

        private sealed class ImportState
        {
            public string AssetPath;
            public TextureImporterSettings TextureSettings;
            public TextureImporterCompression TextureCompression;
            public bool CrunchedCompression;
            public int CompressionQuality;
            public int MaxTextureSize;
            public TextureImporterNPOTScale NpotScale;
            public bool IgnoreMipmapLimit;
            public string PlatformName;
            public bool HadPlatformOverride;
            public TextureImporterPlatformSettings PlatformSettings;
        }

        [Serializable]
        private sealed class SliceManifest
        {
            public int Version = SliceManifestVersion;
            public string[] Keys = Array.Empty<string>();
        }

        private sealed class ArrayAssetTransaction
        {
            public string Path;
            public Texture2DArray Existing;
            public Texture2DArray Backup;
            public string ImporterUserData;
            public bool Created;
        }

        public static Shader ResolveOptimizedShader()
        {
            string shaderPath = ResolveOptimizedShaderPath();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"The Production RealBlend shader could not be found: '{shaderPath}'.");
            }

            if (!shader.isSupported)
            {
                throw new InvalidOperationException(
                    $"The Production RealBlend shader '{shader.name}' is not supported by the active render pipeline.");
            }

            return shader;
        }

        public static string GetOptimizedMaterialFileName(string displayName)
        {
            return MaskMapPackingUtility.SanitizeFileStem(displayName) +
                   "_RealBlendOptimized" + GetPipelineToken() + ".mat";
        }

        public static void ValidateFolderDestinations(string requestedFolder)
        {
            string folder = MaskMapPackingUtility.NormalizeAssetPath(requestedFolder);
            if (!MaskMapPackingUtility.IsWritableAssetFolder(folder) ||
                !AssetDatabase.IsValidFolder(folder))
            {
                throw new InvalidOperationException(
                    $"Production material output must be a valid folder under Assets: '{folder}'.");
            }

            Shader shader = ResolveOptimizedShader();
            string displayName = GetLibraryDisplayName(folder);
            string stem = MaskMapPackingUtility.SanitizeFileStem(displayName) + "_RealBlendOptimized";
            string[] arrayPaths =
            {
                CombineAssetPath(folder, stem + "_AlbedoArray.asset"),
                CombineAssetPath(folder, stem + "_NormalArray.asset"),
                CombineAssetPath(folder, stem + "_MaskArray.asset")
            };
            foreach (string arrayPath in arrayPaths)
                ValidateArrayDestination(arrayPath);
            Material material = ValidateMaterialDestination(
                CombineAssetPath(folder, GetOptimizedMaterialFileName(displayName)),
                shader);
            bool hasExistingArray = arrayPaths.Any(
                path => AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null);
            string sharedManifest = ReadConsistentArrayManifest(arrayPaths);
            string materialManifest = material?.GetTag(SliceManifestTag, false, string.Empty);
            if (hasExistingArray &&
                string.IsNullOrWhiteSpace(sharedManifest) &&
                string.IsNullOrWhiteSpace(materialManifest))
            {
                throw new InvalidOperationException(
                    "Canonical Production arrays already exist without a shared slice manifest. " +
                    "They were left unchanged because copied materials may depend on their indices.");
            }
        }

        public static void ValidateFolderArrayDestinations(string requestedFolder)
        {
            string folder = MaskMapPackingUtility.NormalizeAssetPath(requestedFolder);
            if (!MaskMapPackingUtility.IsWritableAssetFolder(folder) ||
                !AssetDatabase.IsValidFolder(folder))
            {
                throw new InvalidOperationException(
                    $"Production material output must be a valid folder under Assets: '{folder}'.");
            }

            ResolveOptimizedShader();
            string displayName = GetLibraryDisplayName(folder);
            string stem = MaskMapPackingUtility.SanitizeFileStem(displayName) + "_RealBlendOptimized";
            string[] arrayPaths =
            {
                CombineAssetPath(folder, stem + "_AlbedoArray.asset"),
                CombineAssetPath(folder, stem + "_NormalArray.asset"),
                CombineAssetPath(folder, stem + "_MaskArray.asset")
            };
            foreach (string arrayPath in arrayPaths)
                ValidateArrayDestination(arrayPath);

            bool hasExistingArray = arrayPaths.Any(
                path => AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null);
            string sharedManifest = ReadConsistentArrayManifest(arrayPaths);
            Material canonicalMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                CombineAssetPath(folder, GetOptimizedMaterialFileName(displayName)));
            string materialManifest = canonicalMaterial?.GetTag(
                SliceManifestTag,
                false,
                string.Empty);
            if (hasExistingArray &&
                string.IsNullOrWhiteSpace(sharedManifest) &&
                string.IsNullOrWhiteSpace(materialManifest))
            {
                throw new InvalidOperationException(
                    "Canonical Production arrays already exist without a shared slice manifest. " +
                    "They were left unchanged because existing materials may depend on their indices.");
            }
        }

        /// <summary>
        /// Returns true when a retained packed mask is already an authoritative
        /// slice in this folder's generated array manifest. This lets automatic
        /// conversion include old slices after their separate channel maps have
        /// intentionally been removed.
        /// </summary>
        public static bool IsMaskReferencedByExistingFolderLibrary(
            string requestedFolder,
            string maskAssetPath)
        {
            string folder = MaskMapPackingUtility.NormalizeAssetPath(requestedFolder);
            string maskPath = MaskMapPackingUtility.NormalizeAssetPath(maskAssetPath);
            string maskGuid = string.IsNullOrWhiteSpace(maskPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(maskPath);
            if (string.IsNullOrWhiteSpace(maskGuid) || !AssetDatabase.IsValidFolder(folder))
                return false;

            string displayName = GetLibraryDisplayName(folder);
            string stem = MaskMapPackingUtility.SanitizeFileStem(displayName) + "_RealBlendOptimized";
            try
            {
                string manifestJson = ReadConsistentArrayManifest(new[]
                {
                    CombineAssetPath(folder, stem + "_AlbedoArray.asset"),
                    CombineAssetPath(folder, stem + "_NormalArray.asset"),
                    CombineAssetPath(folder, stem + "_MaskArray.asset")
                });
                if (string.IsNullOrWhiteSpace(manifestJson))
                    return false;

                SliceManifest manifest = JsonUtility.FromJson<SliceManifest>(manifestJson);
                if (manifest == null || manifest.Version != SliceManifestVersion || manifest.Keys == null)
                    return false;
                string key = "mask:" + maskGuid.ToLowerInvariant();
                return manifest.Keys.Contains(key, StringComparer.Ordinal);
            }
            catch
            {
                // The normal folder build performs the strict validation and will
                // abort without touching existing assets if this manifest is bad.
                return false;
            }
        }

        public static OptimizedMaterialResult CreateOrUpdateFolderMaterial(
            IReadOnlyList<OptimizedMaskLayerSource> requestedLayers)
        {
            return CreateOrUpdateFolderMaterial(requestedLayers, null);
        }

        public static OptimizedMaterialResult CreateOrUpdateFolderMaterial(
            IReadOnlyList<OptimizedMaskLayerSource> requestedLayers,
            OptimizedMaterialBuildOptions options)
        {
            if (requestedLayers == null)
                throw new ArgumentNullException(nameof(requestedLayers));

            List<OptimizedMaskLayerSource> layers = requestedLayers
                .Where(layer => layer?.Set != null)
                .ToList();
            if (layers.Count == 0)
                throw new InvalidOperationException("At least one discovered texture set is required.");

            string folder = MaskMapPackingUtility.NormalizeAssetPath(layers[0].Set.Folder);
            if (layers.Any(layer => !string.Equals(
                    folder,
                    MaskMapPackingUtility.NormalizeAssetPath(layer.Set.Folder),
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Production array layers must come from the same folder. Subfolders are intentionally isolated.");
            }

            if (!MaskMapPackingUtility.IsWritableAssetFolder(folder) ||
                !AssetDatabase.IsValidFolder(folder))
            {
                throw new InvalidOperationException(
                    $"Production material output must be a valid folder under Assets: '{folder}'.");
            }

            if (layers.Count > SystemInfo.maxTextureArraySlices)
            {
                throw new InvalidOperationException(
                    $"This folder contains {layers.Count} layers, but the current graphics device supports " +
                    $"at most {SystemInfo.maxTextureArraySlices} texture-array slices.");
            }

            string displayName = GetLibraryDisplayName(folder);
            string stem = GetRequestedLibraryStem(displayName, options);
            string materialFileName = GetRequestedMaterialFileName(displayName, options);
            var result = new OptimizedMaterialResult
            {
                SliceCount = layers.Count,
                MaterialPath = CombineAssetPath(folder, materialFileName),
                AlbedoArrayPath = CombineAssetPath(folder, stem + "_AlbedoArray.asset"),
                NormalArrayPath = CombineAssetPath(folder, stem + "_NormalArray.asset"),
                MaskArrayPath = CombineAssetPath(folder, stem + "_MaskArray.asset")
            };

            Shader shader = ResolveOptimizedShader();
            result.ShaderName = shader.name;
            ValidateArrayDestination(result.AlbedoArrayPath);
            ValidateArrayDestination(result.NormalArrayPath);
            ValidateArrayDestination(result.MaskArrayPath);
            Material existingMaterial = ResolveRequestedMaterialDestination(
                ref result.MaterialPath,
                shader,
                options,
                result.Warnings);
            string[] arrayPaths =
            {
                result.AlbedoArrayPath,
                result.NormalArrayPath,
                result.MaskArrayPath
            };
            bool hasExistingArray = arrayPaths.Any(
                path => AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null);
            string existingSliceManifest = ReadConsistentArrayManifest(arrayPaths);
            layers = OrderLayersForStableSlices(
                layers,
                existingMaterial,
                existingSliceManifest,
                hasExistingArray,
                result,
                out HashSet<string> existingManifestKeys);
            result.SliceCount = layers.Count;
            int[] selectedSliceIndices = ResolveSelectedSliceIndices(
                layers,
                options?.SelectedMaterialLayers);
            result.SelectedSliceIndices = selectedSliceIndices;
            bool reuseExistingArrayLibrary = TryGetReusableExistingArrayLibrary(
                options,
                layers,
                existingSliceManifest,
                result.AlbedoArrayPath,
                result.NormalArrayPath,
                result.MaskArrayPath,
                out Texture2DArray reusableAlbedoArray,
                out Texture2DArray reusableNormalArray,
                out Texture2DArray reusableMaskArray);
            if (!reuseExistingArrayLibrary &&
                options?.AllowFallbackSourcesForExistingSlices != true)
                ValidateExistingSliceMaterialSources(layers, existingManifestKeys);

            var changedImporters = new List<ImportState>();
            var arrayTransactions = new List<ArrayAssetTransaction>();
            var createdPaths = new List<string>();
            Material materialBackup = existingMaterial != null ? new Material(existingMaterial) : null;
            string[] originalMaterialLabels = existingMaterial != null
                ? AssetDatabase.GetLabels(existingMaterial)
                : null;
            bool materialCreated = false;
            Material stagedMaterial = null;
            Texture2DArray stagedAlbedo = null;
            Texture2DArray stagedNormal = null;
            Texture2DArray stagedMask = null;

            try
            {
                Texture2DArray albedoArray = reusableAlbedoArray;
                Texture2DArray normalArray = reusableNormalArray;
                Texture2DArray maskArray = reusableMaskArray;
                if (reuseExistingArrayLibrary)
                {
                    result.Warnings.Add(
                        "Reused the existing shared texture arrays; only the requested material asset was written.");
                }
                else
                {
                    var albedos = new List<Texture2D>(layers.Count);
                    var normals = new List<Texture2D>(layers.Count);
                    var normalGreenFlips = new List<bool>(layers.Count);
                    var masks = new List<Texture2D>(layers.Count);
                    foreach (OptimizedMaskLayerSource layer in layers)
                    {
                        bool baseAmbiguous = layer.Set.AmbiguousMaterialKinds.Contains(
                            MaskTextureKind.BaseColorAnchor);
                        bool normalAmbiguous = layer.Set.AmbiguousMaterialKinds.Contains(
                            MaskTextureKind.NormalAnchor);
                        bool baseMissing = layer.Set.BaseColor == null;
                        bool normalMissing = layer.Set.Normal == null;
                        if (baseAmbiguous)
                        {
                            result.Warnings.Add(
                                $"{layer.Set.DisplayName}: Base Color is ambiguous, so this array slice uses white.");
                        }
                        else if (baseMissing)
                        {
                            result.Warnings.Add(
                                $"{layer.Set.DisplayName}: Base Color is missing, so this new array slice uses white.");
                        }

                        if (normalAmbiguous)
                        {
                            result.Warnings.Add(
                                $"{layer.Set.DisplayName}: Normal is ambiguous, so this array slice uses a flat normal.");
                        }
                        else if (normalMissing)
                        {
                            result.Warnings.Add(
                                $"{layer.Set.DisplayName}: Normal is missing, so this new array slice uses a flat normal.");
                        }

                        albedos.Add(baseAmbiguous || baseMissing
                            ? null
                            : options?.PreserveSourceImportSettings == true
                                ? LoadTextureWithoutImporterChanges(
                                    layer.Set.BaseColor.AssetPath)
                                : PrepareTexture(
                                    layer.Set.BaseColor,
                                    ArrayRole.Albedo,
                                    changedImporters,
                                    result.Warnings));
                        bool flipNormalGreen = false;
                        normals.Add(normalAmbiguous || normalMissing
                            ? null
                            : options?.PreserveSourceImportSettings == true
                                ? LoadTextureWithoutImporterChanges(
                                    layer.Set.Normal.AssetPath)
                                : PrepareTexture(
                                    layer.Set.Normal,
                                    ArrayRole.Normal,
                                    changedImporters,
                                    result.Warnings,
                                    out flipNormalGreen));
                        normalGreenFlips.Add(flipNormalGreen);

                        string maskPath = MaskMapPackingUtility.NormalizeAssetPath(layer.MaskAssetPath);
                        masks.Add(options?.PreserveSourceImportSettings == true
                            ? LoadTextureWithoutImporterChanges(maskPath)
                            : PrepareTexture(
                                maskPath,
                                ArrayRole.Mask,
                                changedImporters,
                                result.Warnings));
                    }

                    stagedAlbedo = BuildArray(albedos, ArrayRole.Albedo, Path.GetFileNameWithoutExtension(result.AlbedoArrayPath), result.Warnings);
                    stagedNormal = BuildArray(
                        normals,
                        ArrayRole.Normal,
                        Path.GetFileNameWithoutExtension(result.NormalArrayPath),
                        result.Warnings,
                        normalGreenFlips);
                    stagedMask = BuildArray(
                        masks,
                        ArrayRole.Mask,
                        Path.GetFileNameWithoutExtension(result.MaskArrayPath),
                        result.Warnings,
                        fallbackOverride: options?.OverrideMissingMaskFallback == true
                            ? options.MissingMaskFallback
                            : (Color?)null);

                    // Arrays are self-contained. Restore every source importer before touching a
                    // persistent output asset, so a restore failure cannot leave a partially updated
                    // library behind.
                    RestoreImporters(changedImporters);
                    changedImporters.Clear();

                    albedoArray = WriteArrayAsset(
                        stagedAlbedo, result.AlbedoArrayPath, arrayTransactions, createdPaths);
                    stagedAlbedo = null;
                    normalArray = WriteArrayAsset(
                        stagedNormal, result.NormalArrayPath, arrayTransactions, createdPaths);
                    stagedNormal = null;
                    maskArray = WriteArrayAsset(
                        stagedMask, result.MaskArrayPath, arrayTransactions, createdPaths);
                    stagedMask = null;
                }

                bool creatingMaterial = existingMaterial == null;
                stagedMaterial = creatingMaterial
                    ? new Material(shader)
                    : new Material(existingMaterial);
                stagedMaterial.name = Path.GetFileNameWithoutExtension(result.MaterialPath);
                if (options?.PropertySourceMaterial != null)
                    CopyCompatibleMaterialProperties(options.PropertySourceMaterial, stagedMaterial);
                SetRequiredTexture(stagedMaterial, "_AlbedoArray", albedoArray);
                SetRequiredTexture(stagedMaterial, "_NormalArray", normalArray);
                SetRequiredTexture(stagedMaterial, "_MaskArray", maskArray);

                if (selectedSliceIndices != null)
                {
                    SetRequiredFloat(stagedMaterial, "_BaseSlice", selectedSliceIndices[0]);
                    SetRequiredFloat(stagedMaterial, "_Layer1Slice", selectedSliceIndices[1]);
                    SetRequiredFloat(stagedMaterial, "_Layer2Slice", selectedSliceIndices[2]);
                    if (creatingMaterial && stagedMaterial.HasProperty("_WetnessMask"))
                        stagedMaterial.SetTexture("_WetnessMask", Texture2D.whiteTexture);
                    if (creatingMaterial)
                        stagedMaterial.DisableKeyword("_REALBLEND_TRIPLANAR");
                }
                else if (creatingMaterial)
                {
                    SetRequiredFloat(stagedMaterial, "_BaseSlice", 0f);
                    SetRequiredFloat(stagedMaterial, "_Layer1Slice", Mathf.Min(1, layers.Count - 1));
                    SetRequiredFloat(stagedMaterial, "_Layer2Slice", Mathf.Min(2, layers.Count - 1));
                    if (stagedMaterial.HasProperty("_WetnessMask"))
                        stagedMaterial.SetTexture("_WetnessMask", Texture2D.whiteTexture);
                    stagedMaterial.DisableKeyword("_REALBLEND_TRIPLANAR");
                }
                else
                {
                    ClampSlice(stagedMaterial, "_BaseSlice", layers.Count);
                    ClampSlice(stagedMaterial, "_Layer1Slice", layers.Count);
                    ClampSlice(stagedMaterial, "_Layer2Slice", layers.Count);
                }

                string sliceManifest = JsonUtility.ToJson(new SliceManifest
                {
                    Version = SliceManifestVersion,
                    Keys = layers.Select(GetStableLayerKey).ToArray()
                });
                stagedMaterial.SetOverrideTag(SliceManifestTag, sliceManifest);

                MaterialEditor.ApplyMaterialPropertyDrawers(stagedMaterial);
                if (options?.PropertySourceMaterial != null)
                    CopyMaterialState(options.PropertySourceMaterial, stagedMaterial);
                if (creatingMaterial)
                {
                    existingMaterial = stagedMaterial;
                    AssetDatabase.CreateAsset(existingMaterial, result.MaterialPath);
                    materialCreated = true;
                    createdPaths.Add(result.MaterialPath);
                    stagedMaterial = null;
                }
                else
                {
                    EditorUtility.CopySerialized(stagedMaterial, existingMaterial);
                }

                EnsureLabel(existingMaterial, MaskMaterialUtility.GeneratedMaterialLabel);
                EditorUtility.SetDirty(existingMaterial);
                AssetDatabase.SaveAssetIfDirty(existingMaterial);
                foreach (ArrayAssetTransaction transaction in arrayTransactions)
                {
                    if (transaction.Existing != null)
                        AssetDatabase.SaveAssetIfDirty(transaction.Existing);
                }
                if (!reuseExistingArrayLibrary)
                {
                    foreach (string arrayPath in arrayPaths)
                        WriteArrayManifest(arrayPath, sliceManifest);
                }

                result.Status = creatingMaterial
                    ? MaskMaterialWriteStatus.Created
                    : MaskMaterialWriteStatus.Updated;
                result.Warnings.Add(BuildSliceSummary(layers));
                return result;
            }
            catch (Exception operationException)
            {
                var failures = new List<Exception> { operationException };
                try
                {
                    RestoreImporters(changedImporters);
                }
                catch (Exception restoreException)
                {
                    failures.Add(restoreException);
                }
                try
                {
                    RollBackAssets(
                        arrayTransactions,
                        createdPaths,
                        materialCreated ? null : existingMaterial,
                        materialBackup,
                        originalMaterialLabels);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }

                if (failures.Count > 1)
                    throw new AggregateException("Production array creation failed and cleanup also reported errors.", failures);
                throw;
            }
            finally
            {
                DestroyImmediateIfPresent(stagedAlbedo);
                DestroyImmediateIfPresent(stagedNormal);
                DestroyImmediateIfPresent(stagedMask);
                DestroyImmediateIfPresent(stagedMaterial);
                DestroyImmediateIfPresent(materialBackup);
                foreach (ArrayAssetTransaction transaction in arrayTransactions)
                    DestroyImmediateIfPresent(transaction.Backup);
            }
        }

        private static Texture2DArray BuildArray(
            IReadOnlyList<Texture2D> sources,
            ArrayRole role,
            string objectName,
            List<string> warnings,
            IReadOnlyList<bool> normalGreenFlips = null,
            Color? fallbackOverride = null)
        {
            int width = sources.Where(texture => texture != null).Select(texture => texture.width).DefaultIfEmpty(4).Max();
            int height = sources.Where(texture => texture != null).Select(texture => texture.height).DefaultIfEmpty(4).Max();
            width = Mathf.Clamp(width, 1, SystemInfo.maxTextureSize);
            height = Mathf.Clamp(height, 1, SystemInfo.maxTextureSize);

            string[] resized = sources
                .Select((texture, index) => new { texture, index })
                .Where(item => item.texture != null &&
                               (item.texture.width != width || item.texture.height != height))
                .Select(item => $"slice {item.index} ({item.texture.width}x{item.texture.height})")
                .ToArray();
            if (resized.Length > 0)
            {
                warnings.Add(
                    $"{objectName}: resized {string.Join(", ", resized)} to the array size {width}x{height}.");
            }

            bool linear = role != ArrayRole.Albedo;
            var array = new Texture2DArray(
                width,
                height,
                sources.Count,
                TextureFormat.RGBA32,
                true,
                linear)
            {
                name = objectName,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4
            };

            Shader packerShader = AssetDatabase.LoadAssetAtPath<Shader>(PackerShaderPath);
            if (packerShader == null || !packerShader.isSupported)
            {
                UnityEngine.Object.DestroyImmediate(array);
                throw new InvalidOperationException(
                    $"The texture-array packing shader is missing or unsupported: '{PackerShaderPath}'.");
            }

            Material packer = new Material(packerShader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTextureReadWrite readWrite = linear
                ? RenderTextureReadWrite.Linear
                : RenderTextureReadWrite.sRGB;
            RenderTexture target = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                readWrite);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = !linear;
                for (int slice = 0; slice < sources.Count; slice++)
                {
                    Texture2D source = sources[slice];
                    packer.SetTexture("_Source", source != null ? source : Texture2D.whiteTexture);
                    packer.SetFloat("_HasSource", source != null ? 1f : 0f);
                    packer.SetFloat("_Mode", role == ArrayRole.Normal ? 1f : 0f);
                    packer.SetFloat(
                        "_FlipNormalGreen",
                        role == ArrayRole.Normal &&
                        normalGreenFlips != null &&
                        slice < normalGreenFlips.Count &&
                        normalGreenFlips[slice]
                            ? 1f
                            : 0f);
                    packer.SetColor("_Fallback", fallbackOverride ?? GetFallback(role));
                    Graphics.Blit(source != null ? source : Texture2D.whiteTexture, target, packer, 0);

                    RenderTexture.active = target;
                    Texture2D readback = new Texture2D(
                        width,
                        height,
                        TextureFormat.RGBA32,
                        false,
                        linear);
                    try
                    {
                        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                        readback.Apply(false, false);
                        array.SetPixels32(readback.GetPixels32(), slice, 0);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(readback);
                    }
                }

                array.Apply(true, false);
                return array;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(array);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(packer);
            }
        }

        private static Texture2DArray WriteArrayAsset(
            Texture2DArray staged,
            string assetPath,
            List<ArrayAssetTransaction> transactions,
            List<string> createdPaths)
        {
            Texture2DArray existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
            var transaction = new ArrayAssetTransaction
            {
                Path = assetPath,
                Existing = existing,
                Backup = existing != null ? UnityEngine.Object.Instantiate(existing) : null,
                ImporterUserData = AssetImporter.GetAtPath(assetPath)?.userData
            };
            transactions.Add(transaction);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(staged, assetPath);
                transaction.Existing = staged;
                transaction.Created = true;
                createdPaths.Add(assetPath);
                EnsureLabel(staged, GeneratedArrayLabel);
                EditorUtility.SetDirty(staged);
                return staged;
            }

            string preservedName = existing.name;
            EditorUtility.CopySerialized(staged, existing);
            existing.name = preservedName;
            EnsureLabel(existing, GeneratedArrayLabel);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(staged);
            return existing;
        }

        private static Texture2D PrepareTexture(
            DiscoveredMaskTexture source,
            ArrayRole role,
            List<ImportState> changedImporters,
            List<string> warnings)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.AssetPath))
                return null;

            return PrepareTexture(source.AssetPath, role, changedImporters, warnings, out _);
        }

        private static Texture2D LoadTextureWithoutImporterChanges(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string path = MaskMapPackingUtility.NormalizeAssetPath(assetPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Array source texture could not be loaded: '{path}'.");
            }
            return texture;
        }

        private static Texture2D PrepareTexture(
            DiscoveredMaskTexture source,
            ArrayRole role,
            List<ImportState> changedImporters,
            List<string> warnings,
            out bool flipNormalGreen)
        {
            flipNormalGreen = false;
            if (source == null || string.IsNullOrWhiteSpace(source.AssetPath))
                return null;

            return PrepareTexture(
                source.AssetPath,
                role,
                changedImporters,
                warnings,
                out flipNormalGreen);
        }

        private static Texture2D PrepareTexture(
            string assetPath,
            ArrayRole role,
            List<ImportState> changedImporters,
            List<string> warnings)
        {
            return PrepareTexture(assetPath, role, changedImporters, warnings, out _);
        }

        private static Texture2D PrepareTexture(
            string assetPath,
            ArrayRole role,
            List<ImportState> changedImporters,
            List<string> warnings,
            out bool flipNormalGreen)
        {
            flipNormalGreen = false;
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string path = MaskMapPackingUtility.NormalizeAssetPath(assetPath);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                warnings.Add($"Could not configure array source import settings for '{path}'.");
                Texture2D unprepared = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (unprepared == null)
                    throw new InvalidOperationException($"Array source texture could not be loaded: '{path}'.");
                return unprepared;
            }

            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            string platformName = GetActiveTexturePlatformName();
            TextureImporterPlatformSettings platformSettings =
                importer.GetPlatformTextureSettings(platformName);
            var state = new ImportState
            {
                AssetPath = path,
                TextureSettings = textureSettings,
                TextureCompression = importer.textureCompression,
                CrunchedCompression = importer.crunchedCompression,
                CompressionQuality = importer.compressionQuality,
                MaxTextureSize = importer.maxTextureSize,
                NpotScale = importer.npotScale,
                IgnoreMipmapLimit = importer.ignoreMipmapLimit,
                PlatformName = platformName,
                HadPlatformOverride = platformSettings.overridden,
                PlatformSettings = ClonePlatformSettings(platformSettings)
            };
            bool changed = false;
            TextureImporterType desiredType = role == ArrayRole.Normal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            bool desiredSrgb = role == ArrayRole.Albedo;
            if (importer.textureType != desiredType)
            {
                importer.textureType = desiredType;
                changed = true;
            }

            if (importer.sRGBTexture != desiredSrgb)
            {
                importer.sRGBTexture = desiredSrgb;
                changed = true;
            }

            bool ignoreGamma = role != ArrayRole.Albedo;
            if (importer.ignorePngGamma != ignoreGamma)
            {
                importer.ignorePngGamma = ignoreGamma;
                changed = true;
            }

            if (role == ArrayRole.Normal)
            {
                flipNormalGreen = GetExplicitNormalGreenFlip(path) ?? importer.flipGreenChannel;
                // Canonicalize explicitly in TextureArrayPacker.shader. Importer green flipping
                // is not consistently reflected in sampled normal-map data across backends.
                if (importer.flipGreenChannel)
                {
                    importer.flipGreenChannel = false;
                    changed = true;
                }
            }

            try
            {
                importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                int sourceMaximum = Mathf.Max(sourceWidth, sourceHeight);
                int supportedMaximum = Mathf.Max(32, SystemInfo.maxTextureSize);
                int requiredMaximum = Mathf.Clamp(
                    Mathf.NextPowerOfTwo(Mathf.Max(1, sourceMaximum)),
                    32,
                    supportedMaximum);
                if (importer.maxTextureSize < requiredMaximum)
                {
                    importer.maxTextureSize = requiredMaximum;
                    changed = true;
                }
                if (sourceMaximum > supportedMaximum)
                {
                    warnings.Add(
                        $"'{path}' is {sourceWidth}x{sourceHeight}; the Production array is limited to " +
                        $"the current device maximum of {supportedMaximum}px.");
                }
            }
            catch (Exception exception)
            {
                warnings.Add($"Could not read the source resolution for '{path}': {exception.Message}");
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }
            if (importer.crunchedCompression)
            {
                importer.crunchedCompression = false;
                changed = true;
            }
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }
            if (!importer.ignoreMipmapLimit)
            {
                importer.ignoreMipmapLimit = true;
                changed = true;
            }
            if (platformSettings.overridden)
            {
                importer.ClearPlatformTextureSettings(platformName);
                changed = true;
            }

            if (changed)
            {
                changedImporters.Add(state);
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException($"Array source texture could not be loaded: '{path}'.");
            return texture;
        }

        private static void ValidateArrayDestination(string assetPath)
        {
            UnityEngine.Object existing = LoadExistingAsset(assetPath);
            if (existing == null)
                return;
            if (!(existing is Texture2DArray array))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' is occupied by a non-Texture2DArray asset and was left unchanged.");
            }

            if (!AssetDatabase.GetLabels(array).Contains(GeneratedArrayLabel))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' is user-owned because it lacks the Mask Packer array label and was left unchanged.");
            }
        }

        private static Material ValidateMaterialDestination(string assetPath, Shader shader)
        {
            UnityEngine.Object existing = LoadExistingAsset(assetPath);
            if (existing == null)
                return null;
            if (!(existing is Material material))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' is occupied by a non-material asset and was left unchanged.");
            }

            if (!AssetDatabase.GetLabels(material).Contains(MaskMaterialUtility.GeneratedMaterialLabel))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' is user-owned because it lacks the Mask Packer material label and was left unchanged.");
            }

            if (material.shader != shader)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' uses '{material.shader?.name}', not '{shader.name}', and was left unchanged.");
            }

            return material;
        }

        private static string GetRequestedMaterialFileName(
            string libraryDisplayName,
            OptimizedMaterialBuildOptions options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.MaterialName))
                return GetOptimizedMaterialFileName(libraryDisplayName);

            string requestedName = Path.GetFileNameWithoutExtension(options.MaterialName.Trim());
            string safeName = MaskMapPackingUtility.SanitizeFileStem(requestedName);
            if (string.IsNullOrWhiteSpace(safeName))
                return GetOptimizedMaterialFileName(libraryDisplayName);
            return safeName + ".mat";
        }

        private static string GetRequestedLibraryStem(
            string libraryDisplayName,
            OptimizedMaterialBuildOptions options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.LibraryStem))
            {
                return MaskMapPackingUtility.SanitizeFileStem(libraryDisplayName) +
                       "_RealBlendOptimized";
            }

            string safeStem = MaskMapPackingUtility.SanitizeFileStem(
                Path.GetFileNameWithoutExtension(options.LibraryStem.Trim()));
            return string.IsNullOrWhiteSpace(safeStem)
                ? MaskMapPackingUtility.SanitizeFileStem(libraryDisplayName) +
                  "_RealBlendOptimized"
                : safeStem;
        }

        private static void CopyCompatibleMaterialProperties(
            Material source,
            Material destination)
        {
            if (source == null || destination == null ||
                source.shader == null || destination.shader == null)
                return;

            Shader sourceShader = source.shader;
            Shader destinationShader = destination.shader;
            int propertyCount = destinationShader.GetPropertyCount();
            for (int index = 0; index < propertyCount; index++)
            {
                string propertyName = destinationShader.GetPropertyName(index);
                int sourceIndex = sourceShader.FindPropertyIndex(propertyName);
                ShaderPropertyType propertyType = destinationShader.GetPropertyType(index);
                if (sourceIndex < 0 ||
                    sourceShader.GetPropertyType(sourceIndex) != propertyType ||
                    !source.HasProperty(propertyName))
                    continue;

                switch (propertyType)
                {
                    case ShaderPropertyType.Color:
                        destination.SetColor(propertyName, source.GetColor(propertyName));
                        break;
                    case ShaderPropertyType.Vector:
                        destination.SetVector(propertyName, source.GetVector(propertyName));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        destination.SetFloat(propertyName, source.GetFloat(propertyName));
                        break;
                    case ShaderPropertyType.Texture:
                        destination.SetTexture(propertyName, source.GetTexture(propertyName));
                        destination.SetTextureScale(propertyName, source.GetTextureScale(propertyName));
                        destination.SetTextureOffset(propertyName, source.GetTextureOffset(propertyName));
                        break;
                    case ShaderPropertyType.Int:
                        destination.SetInteger(propertyName, source.GetInteger(propertyName));
                        break;
                }
            }

            CopyMaterialState(source, destination);
        }

        private static void CopyMaterialState(Material source, Material destination)
        {
            destination.renderQueue = source.renderQueue;
            destination.enableInstancing = source.enableInstancing;
            destination.doubleSidedGI = source.doubleSidedGI;
            destination.globalIlluminationFlags = source.globalIlluminationFlags;
        }

        private static Material ResolveRequestedMaterialDestination(
            ref string materialPath,
            Shader shader,
            OptimizedMaterialBuildOptions options,
            List<string> warnings)
        {
            if (options == null)
                return ValidateMaterialDestination(materialPath, shader);

            if (options.CreateNewMaterial)
            {
                materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);
                return null;
            }

            try
            {
                return ValidateMaterialDestination(materialPath, shader);
            }
            catch (InvalidOperationException exception)
            {
                if (!options.AllowUniqueMaterialFallback)
                    throw;

                string requestedPath = materialPath;
                materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);
                if (string.Equals(requestedPath, materialPath, StringComparison.OrdinalIgnoreCase))
                    throw;
                warnings?.Add(
                    $"The requested material path was protected ({exception.Message}) " +
                    $"A new material was created at '{materialPath}' instead.");
                return null;
            }
        }

        private static void RollBackAssets(
            List<ArrayAssetTransaction> transactions,
            List<string> createdPaths,
            Material existingMaterial,
            Material materialBackup,
            string[] materialLabels)
        {
            for (int index = createdPaths.Count - 1; index >= 0; index--)
            {
                if (AssetDatabase.LoadMainAssetAtPath(createdPaths[index]) != null)
                    AssetDatabase.DeleteAsset(createdPaths[index]);
            }

            foreach (ArrayAssetTransaction transaction in transactions)
            {
                if (transaction.Created || transaction.Existing == null || transaction.Backup == null)
                    continue;
                EditorUtility.CopySerialized(transaction.Backup, transaction.Existing);
                EnsureLabel(transaction.Existing, GeneratedArrayLabel);
                EditorUtility.SetDirty(transaction.Existing);
                AssetDatabase.SaveAssetIfDirty(transaction.Existing);
                RestoreArrayManifest(transaction.Path, transaction.ImporterUserData);
            }

            if (existingMaterial != null && materialBackup != null)
            {
                EditorUtility.CopySerialized(materialBackup, existingMaterial);
                AssetDatabase.SetLabels(existingMaterial, materialLabels ?? Array.Empty<string>());
                EditorUtility.SetDirty(existingMaterial);
                AssetDatabase.SaveAssetIfDirty(existingMaterial);
            }
        }

        private static void RestoreImporters(List<ImportState> states)
        {
            var failures = new List<Exception>();
            for (int index = states.Count - 1; index >= 0; index--)
            {
                ImportState state = states[index];
                Exception lastFailure = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(state.AssetPath) as TextureImporter;
                        if (importer == null)
                            throw new InvalidOperationException(
                                $"Unity did not provide a TextureImporter for '{state.AssetPath}'.");
                        importer.SetTextureSettings(state.TextureSettings);
                        importer.textureCompression = state.TextureCompression;
                        importer.crunchedCompression = state.CrunchedCompression;
                        importer.compressionQuality = state.CompressionQuality;
                        importer.maxTextureSize = state.MaxTextureSize;
                        importer.npotScale = state.NpotScale;
                        importer.ignoreMipmapLimit = state.IgnoreMipmapLimit;
                        if (state.HadPlatformOverride)
                        {
                            TextureImporterPlatformSettings original =
                                ClonePlatformSettings(state.PlatformSettings);
                            original.name = state.PlatformName;
                            original.overridden = true;
                            importer.SetPlatformTextureSettings(original);
                        }
                        else
                        {
                            importer.ClearPlatformTextureSettings(state.PlatformName);
                        }
                        importer.SaveAndReimport();
                        lastFailure = null;
                        break;
                    }
                    catch (Exception exception)
                    {
                        lastFailure = exception;
                    }
                }

                if (lastFailure != null)
                {
                    Debug.LogError(
                        $"[Mask Packer] Could not restore '{state.AssetPath}' after two attempts: {lastFailure}");
                    failures.Add(lastFailure);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more Production array source importers could not be restored.",
                    failures);
            }
        }

        private static string ResolveOptimizedShaderPath()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                throw new InvalidOperationException(
                    "Production RealBlend texture-array shaders are provided for HDRP and URP, not the Built-in Render Pipeline.");
            }

            string type = pipeline.GetType().FullName ?? pipeline.GetType().Name;
            if (type.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0)
                return OptimizedHdrpShaderPath;
            if (type.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return OptimizedUrpShaderPath;
            throw new InvalidOperationException(
                $"The active render pipeline '{type}' is neither HDRP nor URP.");
        }

        private static string GetPipelineToken()
        {
            string path = ResolveOptimizedShaderPath();
            return string.Equals(path, OptimizedHdrpShaderPath, StringComparison.Ordinal)
                ? "HDRP"
                : "URP";
        }

        private static string ReadConsistentArrayManifest(IEnumerable<string> assetPaths)
        {
            string manifest = string.Empty;
            foreach (string assetPath in assetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath) == null)
                    continue;
                string candidate = AssetImporter.GetAtPath(assetPath)?.userData ?? string.Empty;
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (string.IsNullOrWhiteSpace(manifest))
                {
                    manifest = candidate;
                    continue;
                }
                if (!string.Equals(manifest, candidate, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The generated Production arrays disagree about their slice manifest. " +
                        "They were left unchanged to protect existing material indices.");
                }
            }
            return manifest;
        }

        private static void WriteArrayManifest(string assetPath, string manifest)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not provide an importer for the generated array '{assetPath}'.");
            }
            if (string.Equals(importer.userData, manifest, StringComparison.Ordinal))
                return;
            importer.userData = manifest;
            importer.SaveAndReimport();
        }

        private static void RestoreArrayManifest(string assetPath, string manifest)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null || string.Equals(importer.userData, manifest, StringComparison.Ordinal))
                return;
            importer.userData = manifest ?? string.Empty;
            importer.SaveAndReimport();
        }

        private static string GetLibraryDisplayName(string folder)
        {
            string name = Path.GetFileName(folder.TrimEnd('/', '\\'));
            return string.IsNullOrWhiteSpace(name) ? "RealBlendLayers" : name;
        }

        private static List<OptimizedMaskLayerSource> OrderLayersForStableSlices(
            List<OptimizedMaskLayerSource> requestedLayers,
            Material existingMaterial,
            string existingArrayManifest,
            bool hasExistingArray,
            OptimizedMaterialResult result,
            out HashSet<string> existingManifestKeys)
        {
            existingManifestKeys = new HashSet<string>(StringComparer.Ordinal);
            var byKey = new Dictionary<string, OptimizedMaskLayerSource>(StringComparer.Ordinal);
            foreach (OptimizedMaskLayerSource layer in requestedLayers)
            {
                string key = GetStableLayerKey(layer);
                if (!byKey.TryAdd(key, layer))
                {
                    throw new InvalidOperationException(
                        $"Two discovered sets resolve to the same Production-array slice identity '{key}'. " +
                        "The existing library was left unchanged.");
                }
            }

            List<OptimizedMaskLayerSource> alphabetical = requestedLayers
                .OrderBy(layer => layer.Set.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetStableLayerKey, StringComparer.Ordinal)
                .ToList();
            if (existingMaterial == null && string.IsNullOrWhiteSpace(existingArrayManifest))
            {
                if (hasExistingArray)
                {
                    throw new InvalidOperationException(
                        "Canonical Production arrays already exist but have no shared slice manifest. " +
                        "They were left unchanged because copied materials may still depend on their indices.");
                }
                return alphabetical;
            }

            string json = !string.IsNullOrWhiteSpace(existingArrayManifest)
                ? existingArrayManifest
                : existingMaterial?.GetTag(SliceManifestTag, false, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                Texture2DArray existingArray = existingMaterial?.GetTexture("_AlbedoArray") as Texture2DArray;
                if (existingArray != null && existingArray.depth != alphabetical.Count)
                {
                    throw new InvalidOperationException(
                        "The existing Production array library predates slice manifests and its depth no longer " +
                        "matches the discovered sets. It was left unchanged to protect existing slice indices.");
                }

                result.Warnings.Add(
                    "Initialized stable slice identities for an existing Production array library.");
                return alphabetical;
            }

            SliceManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<SliceManifest>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The existing Production array slice manifest is unreadable. The library was left unchanged.",
                    exception);
            }

            if (manifest == null || manifest.Version != SliceManifestVersion || manifest.Keys == null)
            {
                throw new InvalidOperationException(
                    "The existing Production array slice manifest uses an unsupported format. " +
                    "The library was left unchanged.");
            }

            string[] missing = manifest.Keys
                .Where(key => !byKey.ContainsKey(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "One or more existing array slices are no longer discoverable. The library was not shrunk " +
                    "or reordered, so materials that use its slice indices remain valid. Restore the source " +
                    "sets or create a new library intentionally.");
            }

            var ordered = new List<OptimizedMaskLayerSource>(requestedLayers.Count);
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in manifest.Keys)
            {
                if (!existingManifestKeys.Add(key) || !consumed.Add(key))
                {
                    throw new InvalidOperationException(
                        "The existing Production array slice manifest contains duplicate identities.");
                }
                ordered.Add(byKey[key]);
            }
            ordered.AddRange(alphabetical.Where(layer => consumed.Add(GetStableLayerKey(layer))));
            return ordered;
        }

        private static bool TryGetReusableExistingArrayLibrary(
            OptimizedMaterialBuildOptions options,
            IReadOnlyList<OptimizedMaskLayerSource> orderedLayers,
            string existingSliceManifest,
            string albedoArrayPath,
            string normalArrayPath,
            string maskArrayPath,
            out Texture2DArray albedoArray,
            out Texture2DArray normalArray,
            out Texture2DArray maskArray)
        {
            albedoArray = null;
            normalArray = null;
            maskArray = null;
            if (options?.ReuseExistingArrayLibrary != true ||
                string.IsNullOrWhiteSpace(existingSliceManifest) ||
                orderedLayers == null ||
                orderedLayers.Count == 0)
            {
                return false;
            }

            SliceManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<SliceManifest>(existingSliceManifest);
            }
            catch
            {
                return false;
            }
            string[] requestedKeys = orderedLayers.Select(GetStableLayerKey).ToArray();
            if (manifest == null ||
                manifest.Version != SliceManifestVersion ||
                manifest.Keys == null ||
                !manifest.Keys.SequenceEqual(requestedKeys, StringComparer.Ordinal))
            {
                return false;
            }

            albedoArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(albedoArrayPath);
            normalArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(normalArrayPath);
            maskArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(maskArrayPath);
            if (albedoArray == null || normalArray == null || maskArray == null ||
                albedoArray.depth != orderedLayers.Count ||
                normalArray.depth != orderedLayers.Count ||
                maskArray.depth != orderedLayers.Count)
            {
                albedoArray = null;
                normalArray = null;
                maskArray = null;
                return false;
            }
            return true;
        }

        private static int[] ResolveSelectedSliceIndices(
            IReadOnlyList<OptimizedMaskLayerSource> orderedLibraryLayers,
            IReadOnlyList<OptimizedMaskLayerSource> selectedMaterialLayers)
        {
            if (selectedMaterialLayers == null || selectedMaterialLayers.Count == 0)
                return null;
            if (selectedMaterialLayers.Count != 3)
            {
                throw new InvalidOperationException(
                    "A Production RealBlend material requires exactly three ordered texture sets.");
            }

            var sliceByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < orderedLibraryLayers.Count; index++)
                sliceByKey.Add(GetStableLayerKey(orderedLibraryLayers[index]), index);

            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            var indices = new int[3];
            for (int index = 0; index < selectedMaterialLayers.Count; index++)
            {
                OptimizedMaskLayerSource layer = selectedMaterialLayers[index];
                if (layer?.Set == null)
                    throw new InvalidOperationException($"Selected material layer {index + 1} is missing.");
                string key = GetStableLayerKey(layer);
                if (!selectedKeys.Add(key))
                {
                    throw new InvalidOperationException(
                        "Choose three different texture sets for the Production material.");
                }
                if (!sliceByKey.TryGetValue(key, out indices[index]))
                {
                    throw new InvalidOperationException(
                        $"Selected texture set '{layer.Set.DisplayName}' is not part of the shared array library.");
                }
            }
            return indices;
        }

        private static void ValidateExistingSliceMaterialSources(
            IEnumerable<OptimizedMaskLayerSource> layers,
            ISet<string> existingManifestKeys)
        {
            if (existingManifestKeys == null || existingManifestKeys.Count == 0)
                return;

            var issues = new List<string>();
            foreach (OptimizedMaskLayerSource layer in layers)
            {
                if (!existingManifestKeys.Contains(GetStableLayerKey(layer)))
                    continue;

                bool baseAmbiguous = layer.Set.AmbiguousMaterialKinds.Contains(
                    MaskTextureKind.BaseColorAnchor);
                bool normalAmbiguous = layer.Set.AmbiguousMaterialKinds.Contains(
                    MaskTextureKind.NormalAnchor);
                if (baseAmbiguous)
                {
                    issues.Add($"{layer.Set.DisplayName}: Base Color is ambiguous");
                }
                else if (!IsLoadableTextureSource(layer.Set.BaseColor))
                {
                    issues.Add($"{layer.Set.DisplayName}: Base Color is missing");
                }

                if (normalAmbiguous)
                {
                    issues.Add($"{layer.Set.DisplayName}: Normal is ambiguous");
                }
                else if (!IsLoadableTextureSource(layer.Set.Normal))
                {
                    issues.Add($"{layer.Set.DisplayName}: Normal is missing");
                }
            }

            if (issues.Count == 0)
                return;

            const int displayedIssueLimit = 10;
            string details = string.Join("\n", issues.Take(displayedIssueLimit).Select(issue => "- " + issue));
            if (issues.Count > displayedIssueLimit)
                details += $"\n- ...and {issues.Count - displayedIssueLimit} more issue(s)";
            throw new InvalidOperationException(
                "An existing Production slice has lost an unambiguous Base Color or Normal source. " +
                "Rebuilding would silently replace that established slice with a white or flat-normal fallback, " +
                "so the entire folder update was stopped before any assets were written. Restore or " +
                "disambiguate these sources and try again. New slices may still use documented fallbacks.\n\n" +
                details);
        }

        private static bool IsLoadableTextureSource(DiscoveredMaskTexture source)
        {
            string path = MaskMapPackingUtility.NormalizeAssetPath(source?.AssetPath);
            return !string.IsNullOrWhiteSpace(path) &&
                   AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null;
        }

        private static string GetStableLayerKey(OptimizedMaskLayerSource layer)
        {
            if (!string.IsNullOrWhiteSpace(layer?.StableSliceKey))
                return "explicit:" + layer.StableSliceKey.Trim().ToLowerInvariant();

            string maskPath = MaskMapPackingUtility.NormalizeAssetPath(layer?.MaskAssetPath);
            string guid = string.IsNullOrWhiteSpace(maskPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(maskPath);
            if (!string.IsNullOrWhiteSpace(guid))
                return "mask:" + guid.ToLowerInvariant();

            string folder = MaskMapPackingUtility.NormalizeAssetPath(layer?.Set?.Folder).ToLowerInvariant();
            string group = (layer?.Set?.GroupKey ?? layer?.Set?.DisplayName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            return "group:" + folder + "|" + group;
        }

        private static TextureImporterPlatformSettings ClonePlatformSettings(
            TextureImporterPlatformSettings source)
        {
            var clone = new TextureImporterPlatformSettings();
            source.CopyTo(clone);
            return clone;
        }

        private static string GetActiveTexturePlatformName()
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            if (group == BuildTargetGroup.iOS)
                return "iPhone";

            string platformName = NamedBuildTarget.FromBuildTargetGroup(group).TargetName;
            if (string.IsNullOrEmpty(platformName))
                throw new InvalidOperationException($"Unity did not provide a texture platform name for '{group}'.");
            return platformName;
        }

        private static string BuildSliceSummary(IReadOnlyList<OptimizedMaskLayerSource> layers)
        {
            return "Array slices: " + string.Join(
                ", ",
                layers.Select((layer, index) => $"{index}={layer.Set.DisplayName}"));
        }

        private static Color GetFallback(ArrayRole role)
        {
            switch (role)
            {
                case ArrayRole.Normal:
                    return new Color(0.5f, 0.5f, 1f, 1f);
                case ArrayRole.Mask:
                    return new Color(0f, 1f, 1f, 0.5f);
                default:
                    return Color.white;
            }
        }

        private static bool? GetExplicitNormalGreenFlip(string path)
        {
            string rawName = (Path.GetFileNameWithoutExtension(path) ?? string.Empty)
                .ToLowerInvariant();
            string compact = Regex.Replace(rawName, "[^a-z0-9]+", string.Empty);
            if (compact.Contains("directx") || compact.Contains("normaldx"))
                return true;
            if (compact.Contains("opengl") || compact.Contains("normalgl"))
                return false;

            string expanded = Regex.Replace(
                Path.GetFileNameWithoutExtension(path) ?? string.Empty,
                "(?<=[a-z])(?=[A-Z])",
                "_");
            string[] tokens = Regex.Split(expanded.ToLowerInvariant(), "[^a-z0-9]+");
            if (tokens.Contains("directx") || tokens.Contains("dx") || tokens.Contains("normaldx"))
                return true;
            if (tokens.Contains("opengl") || tokens.Contains("gl") || tokens.Contains("normalgl"))
                return false;
            return null;
        }

        private static void SetRequiredTexture(Material material, string propertyName, Texture value)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Shader '{material.shader.name}' is missing required property '{propertyName}'.");
            }
            material.SetTexture(propertyName, value);
        }

        private static void SetRequiredFloat(Material material, string propertyName, float value)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Shader '{material.shader.name}' is missing required property '{propertyName}'.");
            }
            material.SetFloat(propertyName, value);
        }

        private static void ClampSlice(Material material, string propertyName, int sliceCount)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Shader '{material.shader.name}' is missing required property '{propertyName}'.");
            }
            material.SetFloat(
                propertyName,
                Mathf.Clamp(Mathf.RoundToInt(material.GetFloat(propertyName)), 0, sliceCount - 1));
        }

        private static void EnsureLabel(UnityEngine.Object asset, string label)
        {
            List<string> labels = AssetDatabase.GetLabels(asset).ToList();
            if (labels.Contains(label))
                return;
            labels.Add(label);
            AssetDatabase.SetLabels(asset, labels.ToArray());
        }

        private static UnityEngine.Object LoadExistingAsset(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null || !File.Exists(ToAbsoluteProjectPath(assetPath)))
                return asset;
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        private static string CombineAssetPath(string folder, string fileName)
        {
            return MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(folder, fileName));
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolute = Path.GetFullPath(Path.Combine(root, assetPath));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Asset path resolved outside the Unity project.");
            return absolute;
        }

        private static void DestroyImmediateIfPresent(UnityEngine.Object value)
        {
            if (value != null && !AssetDatabase.Contains(value))
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
