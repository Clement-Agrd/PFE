using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    /// <summary>
    /// Converts a three-layer Standard RealBlend material into a deprecated Production
    /// texture-array material without modifying the source material or textures.
    /// </summary>
    public static class RealBlendProductionMaterialConverter
    {
        private const string StandardHdrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlendVariationHDRP.shadergraph";
        private const string StandardUrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlendVariationURP.shadergraph";
        private const string ProductionHdrpShaderPath =
            "Assets/RealBlend/Art/Shaders/Optimized/HDRP/RealBlendProductionHDRP.shadergraph";
        private const string ProductionUrpShaderPath =
            "Assets/RealBlend/Art/Shaders/Optimized/URP/RealBlendProductionURP.shadergraph";
        private const string SliceManifestTag = "RealBlendArraySliceManifest";
        private const long LargeArrayWarningBytes = 256L * 1024L * 1024L;
        private const long HardArrayLimitBytes = 2L * 1024L * 1024L * 1024L;

        [Serializable]
        private sealed class SliceManifest
        {
            public int Version;
            public string[] Keys;
        }

        private static readonly string[] LayerLabels = { "Base", "Layer 1", "Layer 2" };
        private static readonly string[] AlbedoProperties =
            { "_Base_Albedo", "_Layer1_Albedo", "_Layer2_Albedo" };
        private static readonly string[] NormalProperties =
            { "_Base_Normal", "_Layer1_Normal", "_Layer2_Normal" };
        private static readonly string[] MaskProperties =
            { "_Base_MaskMap", "_Layer1_MaskMap", "_Layer2_MaskMap" };

        [MenuItem("Assets/RealBlend/Convert Selected Material to Production (deprecated)", false, 2100)]
        private static void ConvertSelectedMaterials()
        {
            Material[] materials = Selection.objects
                .OfType<Material>()
                .Distinct()
                .ToArray();
            if (materials.Length == 0)
                return;

            var outputs = new List<UnityEngine.Object>();
            var failures = new List<string>();
            var warnings = new List<string>();
            int canceled = 0;
            try
            {
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    EditorUtility.DisplayProgressBar(
                        "Convert RealBlend Materials",
                        source.name,
                        (float)index / materials.Length);

                    try
                    {
                        if (!ConfirmMemoryBudget(source))
                        {
                            canceled++;
                            continue;
                        }
                        OptimizedMaterialResult result = Convert(source);
                        Material output = AssetDatabase.LoadAssetAtPath<Material>(result.MaterialPath);
                        if (output != null)
                            outputs.Add(output);
                        warnings.AddRange(result.Warnings.Select(
                            warning => source.name + ": " + warning));
                    }
                    catch (Exception exception)
                    {
                        failures.Add(source.name + ": " + exception.Message);
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            if (outputs.Count > 0)
            {
                Selection.objects = outputs.ToArray();
                EditorGUIUtility.PingObject(outputs[outputs.Count - 1]);
            }
            if (outputs.Count == 0 && failures.Count == 0 && canceled > 0)
                return;

            string message = outputs.Count == 1
                ? "Created or updated the Production material:\n" +
                  AssetDatabase.GetAssetPath(outputs[0])
                : $"Created or updated {outputs.Count} Production materials.";
            if (warnings.Count > 0)
                message += "\n\nNotes:\n" + FormatMessages(warnings, 8);
            if (failures.Count > 0)
                message += "\n\nCould not convert:\n" + FormatMessages(failures, 8);
            if (canceled > 0)
                message += $"\n\nCanceled: {canceled}.";

            EditorUtility.DisplayDialog(
                failures.Count == 0 ? "Production Conversion Complete" : "Production Conversion Finished with Errors",
                message,
                "OK");
        }

        [MenuItem("Assets/RealBlend/Convert Selected Material to Production (deprecated)", true)]
        private static bool ValidateConvertSelectedMaterials()
        {
            return Selection.objects.OfType<Material>().Any();
        }

        public static bool CanConvert(Material source, out string reason)
        {
            reason = string.Empty;
            if (source == null)
            {
                reason = "Choose a Standard RealBlend material.";
                return false;
            }

            string sourcePath = MaskMapPackingUtility.NormalizeAssetPath(
                AssetDatabase.GetAssetPath(source));
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !sourcePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The source must be a saved material asset under Assets.";
                return false;
            }

            Shader expectedStandard;
            try
            {
                Shader production = RealBlendOptimizedMaterialUtility.ResolveOptimizedShader();
                Shader hdrpProduction = AssetDatabase.LoadAssetAtPath<Shader>(ProductionHdrpShaderPath);
                Shader urpProduction = AssetDatabase.LoadAssetAtPath<Shader>(ProductionUrpShaderPath);
                string standardPath;
                if (production == hdrpProduction)
                    standardPath = StandardHdrpShaderPath;
                else if (production == urpProduction)
                    standardPath = StandardUrpShaderPath;
                else
                {
                    reason = "The active Production RealBlend shader is not a recognized HDRP or URP asset.";
                    return false;
                }
                expectedStandard = AssetDatabase.LoadAssetAtPath<Shader>(standardPath);
                if (expectedStandard == null)
                {
                    reason = "The Standard RealBlend shader could not be found: '" + standardPath + "'.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }

            if (source.shader != expectedStandard)
            {
                reason =
                    "Choose a material using the three-layer Standard RealBlend shader for the active pipeline. " +
                    "Alpha Cutout, 5-way, Built-in, custom, and existing Production materials cannot be converted " +
                    "without changing their rendering semantics.";
                return false;
            }

            return true;
        }

        public static bool ConfirmMemoryBudget(Material source)
        {
            if (!CanConvert(source, out string reason))
                throw new InvalidOperationException(reason);

            long estimatedBytes = ValidateMemoryBudget(source);
            return estimatedBytes < LargeArrayWarningBytes ||
                   EditorUtility.DisplayDialog(
                       "Large Production Texture Arrays",
                       "This conversion is estimated to create about " +
                       FormatByteCount(estimatedBytes) +
                       " of mipmapped RGBA32 texture-array data.\n\n" +
                       "Texture arrays reduce sampler bindings, but every slice still consumes texture memory. " +
                       "Consider reducing the source texture resolution before converting.",
                       "Continue",
                       "Cancel");
        }

        public static OptimizedMaterialResult Convert(Material source)
        {
            if (!CanConvert(source, out string reason))
                throw new InvalidOperationException(reason);
            ValidateMemoryBudget(source);

            string sourcePath = MaskMapPackingUtility.NormalizeAssetPath(
                AssetDatabase.GetAssetPath(source));
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrWhiteSpace(sourceGuid))
                throw new InvalidOperationException("Unity could not resolve the source material GUID.");

            string outputFolder = MaskMapPackingUtility.NormalizeAssetPath(
                Path.GetDirectoryName(sourcePath));
            Shader productionShader = RealBlendOptimizedMaterialUtility.ResolveOptimizedShader();
            string sourceStem = MaskMapPackingUtility.SanitizeFileStem(
                Path.GetFileNameWithoutExtension(sourcePath));
            string libraryStem = ResolveSafeLibraryStem(
                outputFolder,
                sourceStem + "_Production",
                productionShader,
                sourceGuid);

            var conversionWarnings = new List<string>();
            var layers = new List<OptimizedMaskLayerSource>(3);
            for (int index = 0; index < 3; index++)
            {
                string albedoPath = GetTextureAssetPath(
                    source,
                    AlbedoProperties[index],
                    LayerLabels[index] + " Albedo",
                    "white",
                    conversionWarnings);
                string normalPath = GetTextureAssetPath(
                    source,
                    NormalProperties[index],
                    LayerLabels[index] + " Normal",
                    "a flat normal",
                    conversionWarnings);
                string maskPath = GetTextureAssetPath(
                    source,
                    MaskProperties[index],
                    LayerLabels[index] + " Mask Map",
                    "white to match Standard RealBlend's unassigned-mask default",
                    conversionWarnings);

                var set = new DiscoveredMaskSet
                {
                    Folder = outputFolder,
                    GroupKey = sourceGuid + ":" + index,
                    DisplayName = source.name + " " + LayerLabels[index],
                    BaseColor = CreateTextureReference(albedoPath, MaskTextureKind.BaseColorAnchor),
                    Normal = CreateTextureReference(normalPath, MaskTextureKind.NormalAnchor),
                    PackedMask = CreateTextureReference(maskPath, MaskTextureKind.PackedRealBlendMask)
                };
                layers.Add(new OptimizedMaskLayerSource
                {
                    Set = set,
                    MaskAssetPath = maskPath,
                    StableSliceKey = "material:" + sourceGuid + ":layer:" + index
                });
            }

            Material sourceSnapshot = new Material(source);
            sourceSnapshot.name = source.name;
            sourceSnapshot.hideFlags = source.hideFlags;
            int sourceRenderQueue = source.renderQueue;
            bool sourceEnableInstancing = source.enableInstancing;
            bool sourceDoubleSidedGi = source.doubleSidedGI;
            MaterialGlobalIlluminationFlags sourceGiFlags = source.globalIlluminationFlags;
            bool sourceWasDirty = EditorUtility.IsDirty(source);
            string sourceFileSystemPath = GetSafeSourceFileSystemPath(sourcePath);
            byte[] cleanSourceBytes = sourceWasDirty
                ? null
                : File.ReadAllBytes(sourceFileSystemPath);
            string sourceJson = EditorJsonUtility.ToJson(source);
            string[] sourceLabels = AssetDatabase.GetLabels(source);
            try
            {
                var options = new OptimizedMaterialBuildOptions
                {
                    MaterialName = libraryStem,
                    LibraryStem = libraryStem,
                    CreateNewMaterial = false,
                    AllowUniqueMaterialFallback = false,
                    ReuseExistingArrayLibrary = false,
                    AllowFallbackSourcesForExistingSlices = true,
                    OverrideMissingMaskFallback = true,
                    MissingMaskFallback = Color.white,
                    PreserveSourceImportSettings = true,
                    PropertySourceMaterial = sourceSnapshot,
                    SelectedMaterialLayers = layers
                };
                OptimizedMaterialResult result =
                    RealBlendOptimizedMaterialUtility.CreateOrUpdateFolderMaterial(layers, options);
                result.Warnings.InsertRange(0, conversionWarnings);
                return result;
            }
            finally
            {
                try
                {
                    RestoreSourceMaterial(
                        sourcePath,
                        source,
                        sourceSnapshot,
                        sourceJson,
                        sourceLabels,
                        sourceWasDirty,
                        sourceFileSystemPath,
                        cleanSourceBytes,
                        sourceRenderQueue,
                        sourceEnableInstancing,
                        sourceDoubleSidedGi,
                        sourceGiFlags);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(sourceSnapshot);
                }
            }
        }

        private static string GetTextureAssetPath(
            Material source,
            string propertyName,
            string displayName,
            string fallbackDescription,
            List<string> warnings)
        {
            Texture texture = source.GetTexture(propertyName);
            if (texture == null)
            {
                warnings.Add(
                    displayName + " is unassigned; the Production array uses " +
                    fallbackDescription + ".");
                return string.Empty;
            }

            if (!(texture is Texture2D))
            {
                throw new InvalidOperationException(
                    displayName + " must be a Texture2D asset before it can be placed in a Production array.");
            }

            string path = MaskMapPackingUtility.NormalizeAssetPath(
                AssetDatabase.GetAssetPath(texture));
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    displayName + " is not a saved project texture; the Production array uses " +
                    fallbackDescription + ".");
                return string.Empty;
            }
            return path;
        }

        private static DiscoveredMaskTexture CreateTextureReference(
            string assetPath,
            MaskTextureKind kind)
        {
            return string.IsNullOrWhiteSpace(assetPath)
                ? null
                : new DiscoveredMaskTexture
                {
                    AssetPath = assetPath,
                    Kind = kind,
                    MatchScore = int.MaxValue
                };
        }

        private static string ResolveSafeLibraryStem(
            string folder,
            string requestedStem,
            Shader productionShader,
            string sourceGuid)
        {
            string safeBase = MaskMapPackingUtility.SanitizeFileStem(requestedStem);
            for (int suffix = 0; suffix < 1000; suffix++)
            {
                string stem = suffix == 0 ? safeBase : safeBase + "_" + suffix;
                if (CanUseLibraryStem(folder, stem, productionShader, sourceGuid))
                    return stem;
            }

            throw new InvalidOperationException(
                "Could not find a safe output name for the Production material and arrays.");
        }

        private static bool CanUseLibraryStem(
            string folder,
            string stem,
            Shader productionShader,
            string sourceGuid)
        {
            string[] arrayPaths =
            {
                CombineAssetPath(folder, stem + "_AlbedoArray.asset"),
                CombineAssetPath(folder, stem + "_NormalArray.asset"),
                CombineAssetPath(folder, stem + "_MaskArray.asset")
            };
            foreach (string path in arrayPaths)
            {
                UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
                if (existing == null)
                    continue;
                if (!(existing is Texture2DArray) ||
                    !AssetDatabase.GetLabels(existing).Contains(
                        RealBlendOptimizedMaterialUtility.GeneratedArrayLabel))
                    return false;
                if (!ManifestBelongsToSource(
                        AssetImporter.GetAtPath(path)?.userData,
                        sourceGuid))
                    return false;
            }

            string materialPath = CombineAssetPath(folder, stem + ".mat");
            UnityEngine.Object materialAsset = AssetDatabase.LoadMainAssetAtPath(materialPath);
            if (materialAsset != null)
            {
                if (!(materialAsset is Material material) ||
                    material.shader != productionShader ||
                    !AssetDatabase.GetLabels(material).Contains(
                        MaskMaterialUtility.GeneratedMaterialLabel) ||
                    !ManifestBelongsToSource(
                        material.GetTag(SliceManifestTag, false, string.Empty),
                        sourceGuid))
                    return false;
            }

            return true;
        }

        private static bool ManifestBelongsToSource(string json, string sourceGuid)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(sourceGuid))
                return false;

            try
            {
                SliceManifest manifest = JsonUtility.FromJson<SliceManifest>(json);
                if (manifest == null || manifest.Version != 1 || manifest.Keys == null)
                    return false;

                string normalizedGuid = sourceGuid.Trim().ToLowerInvariant();
                string[] expectedKeys = Enumerable.Range(0, 3)
                    .Select(index =>
                        "explicit:material:" + normalizedGuid + ":layer:" + index)
                    .ToArray();
                return manifest.Keys.SequenceEqual(expectedKeys, StringComparer.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static void RestoreSourceMaterial(
            string sourcePath,
            Material originalReference,
            Material snapshot,
            string originalJson,
            string[] originalLabels,
            bool wasDirty,
            string sourceFileSystemPath,
            byte[] cleanSourceBytes,
            int renderQueue,
            bool enableInstancing,
            bool doubleSidedGi,
            MaterialGlobalIlluminationFlags giFlags)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null)
                source = originalReference;
            if (source == null || snapshot == null)
                return;

            bool serializedStateChanged = !string.Equals(
                EditorJsonUtility.ToJson(source),
                originalJson,
                StringComparison.Ordinal);
            string[] currentLabels = AssetDatabase.GetLabels(source);
            bool labelsChanged = !currentLabels.SequenceEqual(
                originalLabels ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            bool materialStateChanged =
                source.renderQueue != renderQueue ||
                source.enableInstancing != enableInstancing ||
                source.doubleSidedGI != doubleSidedGi ||
                source.globalIlluminationFlags != giFlags;
            bool cleanFileChanged = !wasDirty &&
                                    cleanSourceBytes != null &&
                                    (!File.Exists(sourceFileSystemPath) ||
                                     !File.ReadAllBytes(sourceFileSystemPath)
                                         .SequenceEqual(cleanSourceBytes));
            if (!serializedStateChanged &&
                !labelsChanged &&
                !materialStateChanged &&
                !cleanFileChanged)
                return;

            if (!wasDirty && cleanSourceBytes != null)
            {
                if (cleanFileChanged)
                    File.WriteAllBytes(sourceFileSystemPath, cleanSourceBytes);
                if (cleanFileChanged || serializedStateChanged || materialStateChanged)
                {
                    AssetDatabase.ImportAsset(
                        sourcePath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath) ?? source;
                }

                if (!AssetDatabase.GetLabels(source).SequenceEqual(
                        originalLabels ?? Array.Empty<string>(),
                        StringComparer.Ordinal))
                {
                    AssetDatabase.SetLabels(
                        source,
                        originalLabels ?? Array.Empty<string>());
                }
                return;
            }

            if (serializedStateChanged)
                EditorUtility.CopySerialized(snapshot, source);
            source.renderQueue = renderQueue;
            source.enableInstancing = enableInstancing;
            source.doubleSidedGI = doubleSidedGi;
            source.globalIlluminationFlags = giFlags;
            if (serializedStateChanged || materialStateChanged)
                EditorUtility.SetDirty(source);
            if (labelsChanged)
                AssetDatabase.SetLabels(source, originalLabels ?? Array.Empty<string>());
            if (!wasDirty && (serializedStateChanged || materialStateChanged))
                AssetDatabase.SaveAssetIfDirty(source);
        }

        private static string GetSafeSourceFileSystemPath(string sourceAssetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Unity project root could not be resolved.");

            string assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                sourceAssetPath.Replace('/', Path.DirectorySeparatorChar)));
            string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The source material path resolved outside the project's Assets folder.");
            }
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "The source material file could not be found.",
                    fullPath);
            }
            return fullPath;
        }

        private static long ValidateMemoryBudget(Material source)
        {
            if (!SystemInfo.supports2DArrayTextures)
            {
                throw new InvalidOperationException(
                    "The current graphics device does not support Texture2DArray resources.");
            }
            if (SystemInfo.maxTextureArraySlices < 3)
            {
                throw new InvalidOperationException(
                    "The current graphics device cannot hold the three required Production slices.");
            }

            long estimatedBytes = 0L;
            estimatedBytes += EstimateMipmappedRgba32Bytes(
                MaxTextureDimensions(source, AlbedoProperties), 3);
            estimatedBytes += EstimateMipmappedRgba32Bytes(
                MaxTextureDimensions(source, NormalProperties), 3);
            estimatedBytes += EstimateMipmappedRgba32Bytes(
                MaxTextureDimensions(source, MaskProperties), 3);
            if (estimatedBytes > HardArrayLimitBytes)
            {
                throw new InvalidOperationException(
                    "This conversion would create approximately " +
                    FormatByteCount(estimatedBytes) +
                    " of mipmapped RGBA32 texture-array data. Reduce the source texture " +
                    "resolution before converting; the 2 GiB safety limit prevents an editor memory spike.");
            }

            return estimatedBytes;
        }

        private static Vector2Int MaxTextureDimensions(
            Material source,
            IEnumerable<string> propertyNames)
        {
            int width = 4;
            int height = 4;
            foreach (string propertyName in propertyNames)
            {
                Texture texture = source.GetTexture(propertyName);
                if (texture == null)
                    continue;

                width = Mathf.Max(width, texture.width);
                height = Mathf.Max(height, texture.height);
            }

            int maximum = Mathf.Max(32, SystemInfo.maxTextureSize);
            return new Vector2Int(
                Mathf.Min(width, maximum),
                Mathf.Min(height, maximum));
        }

        private static long EstimateMipmappedRgba32Bytes(
            Vector2Int size,
            int sliceCount)
        {
            double bytes =
                (double)size.x * size.y * 4.0 * Math.Max(1, sliceCount) * (4.0 / 3.0);
            return bytes >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Ceiling(bytes);
        }

        private static string FormatByteCount(long bytes)
        {
            const double gigabyte = 1024.0 * 1024.0 * 1024.0;
            return bytes >= gigabyte
                ? (bytes / gigabyte).ToString("0.00") + " GiB"
                : (bytes / (1024.0 * 1024.0)).ToString("0") + " MiB";
        }

        private static string CombineAssetPath(string folder, string fileName)
        {
            return MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(folder, fileName));
        }

        private static string FormatMessages(IReadOnlyList<string> messages, int maximum)
        {
            string body = string.Join("\n", messages.Take(maximum).Select(message => "- " + message));
            if (messages.Count > maximum)
                body += $"\n- ...and {messages.Count - maximum} more";
            return body;
        }
    }
}
