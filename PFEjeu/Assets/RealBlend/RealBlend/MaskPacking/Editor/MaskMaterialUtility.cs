using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    public enum MaskMaterialWriteStatus
    {
        Created,
        Updated,
        Skipped
    }

    public sealed class MaskMaterialResult
    {
        public string MaterialPath;
        public string ShaderName;
        public MaskMaterialWriteStatus Status;
        public readonly List<string> Warnings = new List<string>();

        public bool IsUsable => Status == MaskMaterialWriteStatus.Created ||
                                Status == MaskMaterialWriteStatus.Updated;
    }

    public static class MaskMaterialUtility
    {
        public const string GeneratedMaterialLabel = "RealBlend.MaskPacker.GeneratedMaterial";

        private const string RealBlendUrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlendVariationURP.shadergraph";
        private const string RealBlendHdrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlendVariationHDRP.shadergraph";
        private const string RealBlend5WayUrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlend5WayBlendURP.shadergraph";
        private const string RealBlend5WayHdrpShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlend5WayBlendHDRP.shadergraph";
        private const string RealBlendBuiltInShaderPath =
            "Assets/RealBlend/Art/Shaders/RealBlendBuiltInVariation.shader";
        private const string HdrpLitShaderName = "HDRP/Lit";

        private enum MaterialTextureRole
        {
            Color,
            Normal,
            Data
        }

        private sealed class MaterialTextureImportState
        {
            public string AssetPath;
            public TextureImporterType TextureType;
            public bool SrgbTexture;
            public bool IgnorePngGamma;
            public bool FlipGreenChannel;
        }

        public static string GetAutomaticMaterialFileName(string displayName, MaskMapProfile profile)
        {
            if (profile == MaskMapProfile.RealBlendOptimized)
            {
                return RealBlendOptimizedMaterialUtility.GetOptimizedMaterialFileName(displayName);
            }

            string suffix = profile == MaskMapProfile.HDRPLit
                ? "_HDRPLit.mat"
                : "_RealBlend" + (profile == MaskMapProfile.RealBlend5Way ? "5Way" : string.Empty) +
                  GetRealBlendPipelineToken() + ".mat";
            return MaskMapPackingUtility.SanitizeFileStem(displayName) + suffix;
        }

        public static MaskMaterialResult CreateOrUpdateMaterial(
            DiscoveredMaskSet set,
            MaskMapProfile profile,
            string maskAssetPath)
        {
            return CreateMaterial(set, profile, maskAssetPath, null, null, null, null, false);
        }

        public static int GetRealBlendLayerCount(MaskMapProfile profile)
        {
            if (profile == MaskMapProfile.RealBlend5Way) return 5;
            if (profile == MaskMapProfile.RealBlend) return 3;
            throw new ArgumentException("Choose the standard RealBlend or RealBlend 5-Way profile.", nameof(profile));
        }

        /// <summary>Creates one material with texture sets assigned in Base, Layer 1, ... order.</summary>
        public static MaskMaterialResult CreateOrUpdateLayeredMaterial(
            IReadOnlyList<DiscoveredMaskSet> orderedSets,
            IReadOnlyList<string> orderedMaskPaths,
            MaskMapProfile profile,
            string outputFolder,
            string materialName = null,
            bool createUniqueAsset = true)
        {
            int layerCount = GetRealBlendLayerCount(profile);
            if (orderedSets == null || orderedMaskPaths == null ||
                orderedSets.Count != layerCount || orderedMaskPaths.Count != layerCount ||
                orderedSets.Any(set => set == null))
                throw new ArgumentException($"Assign exactly {layerCount} ordered texture sets and packed masks.");

            return CreateMaterial(orderedSets[0], profile, orderedMaskPaths[0], orderedSets,
                orderedMaskPaths, outputFolder, materialName, createUniqueAsset);
        }

        private static MaskMaterialResult CreateMaterial(
            DiscoveredMaskSet set,
            MaskMapProfile profile,
            string maskAssetPath,
            IReadOnlyList<DiscoveredMaskSet> orderedSets,
            IReadOnlyList<string> orderedMaskPaths,
            string outputFolder,
            string materialName,
            bool createUniqueAsset)
        {
            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            if (profile == MaskMapProfile.RealBlendOptimized)
            {
                throw new InvalidOperationException(
                    "Production RealBlend materials are built as one shared texture-array library per folder. " +
                    "Use the Production RealBlend texture-array material builder instead.");
            }

            string folder = MaskMapPackingUtility.NormalizeAssetPath(outputFolder ?? set.Folder);
            if (!MaskMapPackingUtility.IsWritableAssetFolder(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                throw new InvalidOperationException($"Material folder must be a valid folder under Assets: '{folder}'.");
            }

            string materialPath = MaskMapPackingUtility.NormalizeAssetPath(Path.Combine(
                folder,
                string.IsNullOrWhiteSpace(materialName)
                    ? GetAutomaticMaterialFileName(orderedSets == null ? set.DisplayName : Path.GetFileName(folder), profile)
                    : MaskMapPackingUtility.SanitizeFileStem(materialName) + ".mat"));
            if (createUniqueAsset)
                materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);
            var result = new MaskMaterialResult { MaterialPath = materialPath };

            UnityEngine.Object existingAsset = LoadExistingAsset(materialPath);
            Material material = existingAsset as Material;
            if (existingAsset != null && material == null)
            {
                result.Status = MaskMaterialWriteStatus.Skipped;
                result.Warnings.Add(
                    $"'{materialPath}' is occupied by a non-material asset. It was left unchanged.");
                return result;
            }

            Shader shader = ResolveMaterialShader(profile);
            result.ShaderName = shader.name;
            bool creating = material == null;
            if (!creating)
            {
                if (!AssetDatabase.GetLabels(material).Contains(GeneratedMaterialLabel))
                {
                    result.Status = MaskMaterialWriteStatus.Skipped;
                    result.Warnings.Add(
                        $"'{materialPath}' is user-owned because it does not have the Mask Packer label. It was left unchanged.");
                    return result;
                }

                if (material.shader != shader)
                {
                    result.Status = MaskMaterialWriteStatus.Skipped;
                    result.Warnings.Add(
                        $"'{materialPath}' uses '{material.shader?.name}', not the expected '{shader.name}'. It was left unchanged.");
                    return result;
                }
            }

            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(
                MaskMapPackingUtility.NormalizeAssetPath(maskAssetPath));
            if (mask == null)
            {
                throw new InvalidOperationException(
                    $"The generated mask could not be loaded as a Texture2D: '{maskAssetPath}'.");
            }

            bool preserveBaseColor = !creating &&
                                     set.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor);
            bool preserveNormal = !creating &&
                                  set.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor);
            bool preserveEmissive = !creating &&
                                    set.AmbiguousMaterialKinds.Contains(MaskTextureKind.EmissiveAnchor);
            DiscoveredMaskTexture baseColorSource = GetUnambiguousSource(
                set, MaskTextureKind.BaseColorAnchor, set.BaseColor, result.Warnings, "Base Color", preserveBaseColor);
            DiscoveredMaskTexture normalSource = GetUnambiguousSource(
                set, MaskTextureKind.NormalAnchor, set.Normal, result.Warnings, "Normal", preserveNormal);
            DiscoveredMaskTexture emissiveSource = GetUnambiguousSource(
                set, MaskTextureKind.EmissiveAnchor, set.Emissive, result.Warnings, "Emissive", preserveEmissive);

            var changedImporters = new List<MaterialTextureImportState>();
            bool createdAsset = false;
            bool copiedToExisting = false;
            Material workingMaterial = null;
            Material originalSnapshot = creating ? null : new Material(material);
            string[] originalLabels = creating ? null : AssetDatabase.GetLabels(material);

            try
            {
                Texture2D baseColor = orderedSets == null ? PrepareMaterialTexture(
                    baseColorSource, MaterialTextureRole.Color, result.Warnings, changedImporters)
                    : null;
                Texture2D normal = orderedSets == null ? PrepareMaterialTexture(
                    normalSource, MaterialTextureRole.Normal, result.Warnings, changedImporters) : null;
                Texture2D height = profile == MaskMapProfile.HDRPLit
                    ? PrepareMaterialTexture(set.Height, MaterialTextureRole.Data, result.Warnings, changedImporters)
                    : null;
                Texture2D emissive = profile == MaskMapProfile.HDRPLit
                    ? PrepareMaterialTexture(emissiveSource, MaterialTextureRole.Color, result.Warnings, changedImporters)
                    : null;

                workingMaterial = creating ? new Material(shader) : new Material(material);
                workingMaterial.name = creating
                    ? Path.GetFileNameWithoutExtension(materialPath)
                    : material.name;
                if (profile == MaskMapProfile.HDRPLit)
                {
                    ConfigureHdrpLitMaterial(
                        workingMaterial,
                        baseColor,
                        normal,
                        mask,
                        height,
                        emissive,
                        creating,
                        preserveBaseColor,
                        preserveNormal,
                        preserveEmissive,
                        result.Warnings);
                }
                else if (orderedSets != null)
                {
                    ConfigureLayeredRealBlendMaterial(workingMaterial, orderedSets, orderedMaskPaths,
                        creating, result.Warnings, changedImporters);
                }
                else
                {
                    ConfigureRealBlendMaterial(
                        workingMaterial, baseColor, normal, mask, preserveBaseColor, preserveNormal,
                        profile == MaskMapProfile.RealBlend5Way ? 5 : 3);
                }

                if (creating)
                {
                    material = workingMaterial;
                    AssetDatabase.CreateAsset(material, materialPath);
                    createdAsset = true;
                    workingMaterial = null;
                }
                else
                {
                    EditorUtility.CopySerialized(workingMaterial, material);
                    copiedToExisting = true;
                }

                EnsureGeneratedLabel(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.ImportAsset(
                    materialPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                result.Status = creating ? MaskMaterialWriteStatus.Created : MaskMaterialWriteStatus.Updated;
                return result;
            }
            catch
            {
                RestoreMaterialTextureImportStates(changedImporters);
                if (creating && createdAsset)
                {
                    if (!AssetDatabase.DeleteAsset(materialPath))
                    {
                        Debug.LogError(
                            $"[Mask Packer] Could not remove the incomplete generated material '{materialPath}'.");
                    }
                }
                else if (!creating && copiedToExisting && originalSnapshot != null)
                {
                    try
                    {
                        EditorUtility.CopySerialized(originalSnapshot, material);
                        AssetDatabase.SetLabels(material, originalLabels ?? Array.Empty<string>());
                        EditorUtility.SetDirty(material);
                        AssetDatabase.SaveAssetIfDirty(material);
                    }
                    catch (Exception rollbackException)
                    {
                        Debug.LogError(
                            $"[Mask Packer] Could not roll back material '{materialPath}': {rollbackException}");
                    }
                }

                throw;
            }
            finally
            {
                if (workingMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingMaterial);
                }

                if (originalSnapshot != null)
                {
                    UnityEngine.Object.DestroyImmediate(originalSnapshot);
                }
            }
        }

        public static Shader ResolveMaterialShader(MaskMapProfile profile)
        {
            if (profile == MaskMapProfile.RealBlendOptimized)
            {
                return RealBlendOptimizedMaterialUtility.ResolveOptimizedShader();
            }

            Shader shader;
            string description;
            if (profile == MaskMapProfile.HDRPLit)
            {
                shader = Shader.Find(HdrpLitShaderName);
                description = HdrpLitShaderName;
            }
            else
            {
                string shaderPath = ResolveRealBlendShaderPath(profile == MaskMapProfile.RealBlend5Way);
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                description = shaderPath;
            }

            if (shader == null)
            {
                throw new InvalidOperationException($"The material shader could not be found: '{description}'.");
            }

            if (!shader.isSupported)
            {
                throw new InvalidOperationException(
                    $"The material shader '{shader.name}' is not supported by the active render pipeline.");
            }

            return shader;
        }

        private static string ResolveRealBlendShaderPath(bool fiveWay = false)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                if (fiveWay)
                    throw new InvalidOperationException("RealBlend 5-Way requires an active HDRP or URP render pipeline.");
                return RealBlendBuiltInShaderPath;
            }

            string pipelineType = pipeline.GetType().FullName ?? pipeline.GetType().Name;
            if (pipelineType.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pipelineType.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return fiveWay ? RealBlend5WayHdrpShaderPath : RealBlendHdrpShaderPath;
            }

            if (pipelineType.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return fiveWay ? RealBlend5WayUrpShaderPath : RealBlendUrpShaderPath;
            }

            throw new InvalidOperationException(
                $"The active render pipeline '{pipelineType}' is neither Built-In, URP, nor HDRP, so a RealBlend material shader could not be selected safely.");
        }

        private static string GetRealBlendPipelineToken()
        {
            string shaderPath = ResolveRealBlendShaderPath();
            if (string.Equals(shaderPath, RealBlendHdrpShaderPath, StringComparison.Ordinal))
            {
                return "HDRP";
            }

            if (string.Equals(shaderPath, RealBlendUrpShaderPath, StringComparison.Ordinal))
            {
                return "URP";
            }

            return "BuiltIn";
        }

        private static void ConfigureLayeredRealBlendMaterial(
            Material material,
            IReadOnlyList<DiscoveredMaskSet> orderedSets,
            IReadOnlyList<string> orderedMaskPaths,
            bool creating,
            List<string> warnings,
            List<MaterialTextureImportState> changedImporters)
        {
            for (int slot = 0; slot < orderedSets.Count; slot++)
            {
                DiscoveredMaskSet layerSet = orderedSets[slot];
                string prefix = slot == 0 ? "_Base" : "_Layer" + slot;
                bool preserveAlbedo = !creating && layerSet.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor);
                bool preserveNormal = !creating && layerSet.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor);
                DiscoveredMaskTexture albedoSource = GetUnambiguousSource(layerSet,
                    MaskTextureKind.BaseColorAnchor, layerSet.BaseColor, warnings, prefix + " Albedo", preserveAlbedo);
                DiscoveredMaskTexture normalSource = GetUnambiguousSource(layerSet,
                    MaskTextureKind.NormalAnchor, layerSet.Normal, warnings, prefix + " Normal", preserveNormal);
                if (!preserveAlbedo)
                    SetRequiredTexture(material, prefix + "_Albedo", PrepareMaterialTexture(
                        albedoSource, MaterialTextureRole.Color, warnings, changedImporters));
                if (!preserveNormal)
                    SetRequiredTexture(material, prefix + "_Normal", PrepareMaterialTexture(
                        normalSource, MaterialTextureRole.Normal, warnings, changedImporters));
                Texture2D mask = PrepareMaterialTexture(
                    new DiscoveredMaskTexture { AssetPath = orderedMaskPaths[slot], Kind = MaskTextureKind.PackedRealBlendMask },
                    MaterialTextureRole.Data, warnings, changedImporters);
                if (mask == null)
                    throw new InvalidOperationException($"The packed mask for {prefix} could not be loaded: '{orderedMaskPaths[slot]}'.");
                SetRequiredTexture(material, prefix + "_MaskMap", mask);
            }
            if (material.HasProperty("_LegacyMaskLayout"))
                material.SetFloat("_LegacyMaskLayout", 0f);
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
        }

        private static void ConfigureRealBlendMaterial(
            Material material,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask,
            bool preserveBaseColor,
            bool preserveNormal,
            int layerCount)
        {
            for (int slot = 0; slot < layerCount; slot++)
            {
                string prefix = slot == 0 ? "_Base" : "_Layer" + slot;
                if (!preserveBaseColor)
                {
                    SetRequiredTexture(material, prefix + "_Albedo", baseColor);
                }

                if (!preserveNormal)
                {
                    SetRequiredTexture(material, prefix + "_Normal", normal);
                }

                SetRequiredTexture(material, prefix + "_MaskMap", mask);
            }

            if (material.HasProperty("_LegacyMaskLayout"))
            {
                material.SetFloat("_LegacyMaskLayout", 0f);
            }

            MaterialEditor.ApplyMaterialPropertyDrawers(material);
        }

        private static void ConfigureHdrpLitMaterial(
            Material material,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask,
            Texture2D height,
            Texture2D emissive,
            bool creating,
            bool preserveBaseColor,
            bool preserveNormal,
            bool preserveEmissive,
            List<string> warnings)
        {
            Texture managedBaseColor = preserveBaseColor
                ? GetRequiredTexture(material, "_BaseColorMap")
                : baseColor;
            Texture managedNormal = preserveNormal
                ? GetRequiredTexture(material, "_NormalMap")
                : normal;
            Texture managedEmissive = preserveEmissive && material.HasProperty("_EmissiveColorMap")
                ? material.GetTexture("_EmissiveColorMap")
                : emissive;
            Texture previousEmissive = material.HasProperty("_EmissiveColorMap")
                ? material.GetTexture("_EmissiveColorMap")
                : null;
            SetRequiredTexture(material, "_BaseColorMap", managedBaseColor);
            SetRequiredTexture(material, "_NormalMap", managedNormal);
            SetRequiredTexture(material, "_MaskMap", mask);
            SetOptionalTexture(material, "_HeightMap", height);
            SetOptionalTexture(material, "_EmissiveColorMap", managedEmissive);

            if (material.HasProperty("_NormalMapSpace"))
            {
                material.SetFloat("_NormalMapSpace", 0f);
            }

            if (managedEmissive != null && !preserveEmissive && (creating || previousEmissive == null))
            {
                SetColorIfPresent(material, "_EmissiveColor", Color.white);
                SetColorIfPresent(material, "_EmissiveColorLDR", Color.white);
            }

            MaterialEditor.ApplyMaterialPropertyDrawers(material);
            bool validated = TryValidateHdrpMaterial(material);

            // HDRP validation/property drawers can replace texture properties with shader defaults
            // on a brand-new transient Material. Reapply the managed slots after validation, then
            // synchronize their texture-driven keywords explicitly.
            SetRequiredTexture(material, "_BaseColorMap", managedBaseColor);
            SetRequiredTexture(material, "_NormalMap", managedNormal);
            SetRequiredTexture(material, "_MaskMap", mask);
            SetOptionalTexture(material, "_HeightMap", height);
            SetOptionalTexture(material, "_EmissiveColorMap", managedEmissive);
            SetKeyword(material, "_MASKMAP", mask != null);
            SetKeyword(material, "_NORMALMAP", managedNormal != null);
            SetKeyword(material, "_NORMALMAP_TANGENT_SPACE", true);
            SetKeyword(material, "_HEIGHTMAP", height != null);
            SetKeyword(material, "_EMISSIVE_COLOR_MAP", managedEmissive != null);
            if (!validated)
            {
                warnings.Add(
                    "HDRP's material validator was unavailable, so the required texture keywords were configured directly.");
            }
        }

        private static DiscoveredMaskTexture GetUnambiguousSource(
            DiscoveredMaskSet set,
            MaskTextureKind kind,
            DiscoveredMaskTexture source,
            List<string> warnings,
            string label,
            bool preserveExisting)
        {
            if (!set.AmbiguousMaterialKinds.Contains(kind))
            {
                return source;
            }

            warnings.Add(
                preserveExisting
                    ? $"{label} was left unchanged because equally ranked source textures were found."
                    : $"{label} was left unassigned because equally ranked source textures were found.");
            return null;
        }

        private static Texture2D PrepareMaterialTexture(
            DiscoveredMaskTexture source,
            MaterialTextureRole role,
            List<string> warnings,
            List<MaterialTextureImportState> changedImporters)
        {
            if (source == null || string.IsNullOrEmpty(source.AssetPath))
            {
                return null;
            }

            string path = MaskMapPackingUtility.NormalizeAssetPath(source.AssetPath);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                warnings.Add($"Could not configure material texture import settings for '{path}'.");
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            var originalState = new MaterialTextureImportState
            {
                AssetPath = path,
                TextureType = importer.textureType,
                SrgbTexture = importer.sRGBTexture,
                IgnorePngGamma = importer.ignorePngGamma,
                FlipGreenChannel = importer.flipGreenChannel
            };
            bool changed = false;
            TextureImporterType desiredType = role == MaterialTextureRole.Normal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            bool desiredSrgb = role == MaterialTextureRole.Color;
            bool ignorePngGamma = role != MaterialTextureRole.Color;
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

            if (importer.ignorePngGamma != ignorePngGamma)
            {
                importer.ignorePngGamma = ignorePngGamma;
                changed = true;
            }

            if (role == MaterialTextureRole.Normal)
            {
                bool? flipGreen = GetExplicitNormalGreenFlip(path);
                if (flipGreen.HasValue && importer.flipGreenChannel != flipGreen.Value)
                {
                    importer.flipGreenChannel = flipGreen.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                changedImporters.Add(originalState);
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException($"Material texture could not be loaded: '{path}'.");
            }

            return texture;
        }

        private static void RestoreMaterialTextureImportStates(
            List<MaterialTextureImportState> changedImporters)
        {
            for (int index = changedImporters.Count - 1; index >= 0; index--)
            {
                MaterialTextureImportState state = changedImporters[index];
                try
                {
                    var importer = AssetImporter.GetAtPath(state.AssetPath) as TextureImporter;
                    if (importer == null)
                    {
                        Debug.LogError(
                            $"[Mask Packer] Could not restore texture importer settings for '{state.AssetPath}'.");
                        continue;
                    }

                    importer.textureType = state.TextureType;
                    importer.sRGBTexture = state.SrgbTexture;
                    importer.ignorePngGamma = state.IgnorePngGamma;
                    importer.flipGreenChannel = state.FlipGreenChannel;
                    importer.SaveAndReimport();
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[Mask Packer] Could not restore texture importer settings for '{state.AssetPath}': {exception}");
                }
            }
        }

        private static bool? GetExplicitNormalGreenFlip(string path)
        {
            string expanded = Regex.Replace(
                Path.GetFileNameWithoutExtension(path) ?? string.Empty,
                "(?<=[a-z])(?=[A-Z])",
                "_");
            string[] tokens = Regex.Split(expanded.ToLowerInvariant(), "[^a-z0-9]+");
            if (tokens.Contains("directx") || tokens.Contains("dx") || tokens.Contains("normaldx"))
            {
                return true;
            }

            if (tokens.Contains("opengl") || tokens.Contains("gl") || tokens.Contains("normalgl"))
            {
                return false;
            }

            return null;
        }

        private static UnityEngine.Object LoadExistingAsset(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null || !File.Exists(ToAbsoluteProjectPath(assetPath)))
            {
                return asset;
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        private static void EnsureGeneratedLabel(Material material)
        {
            List<string> labels = AssetDatabase.GetLabels(material).ToList();
            if (!labels.Contains(GeneratedMaterialLabel))
            {
                labels.Add(GeneratedMaterialLabel);
                AssetDatabase.SetLabels(material, labels.ToArray());
            }
        }

        private static void SetRequiredTexture(Material material, string propertyName, Texture texture)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Shader '{material.shader.name}' does not expose the required texture property '{propertyName}'.");
            }

            material.SetTexture(propertyName, texture);
        }

        private static Texture GetRequiredTexture(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Shader '{material.shader.name}' does not expose the required texture property '{propertyName}'.");
            }

            return material.GetTexture(propertyName);
        }

        private static void SetOptionalTexture(Material material, string propertyName, Texture texture)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, texture);
            }
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static bool TryValidateHdrpMaterial(Material material)
        {
            Type type = Type.GetType(
                "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime",
                false);
            if (type == null)
            {
                type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial", false))
                    .FirstOrDefault(candidate => candidate != null);
            }

            MethodInfo method = type?.GetMethod(
                "ValidateMaterial",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            return method != null && method.Invoke(null, new object[] { material }) is bool valid && valid;
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            string rootWithSeparator = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Material path resolved outside the Unity project.");
            }

            return absolutePath;
        }
    }
}
