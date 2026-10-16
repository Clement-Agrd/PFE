using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace RealBlend
{
    public enum MaskMapProfile
    {
        RealBlend = 0,
        HDRPLit = 1,
        [InspectorName("Production RealBlend (deprecated)")]
        RealBlendOptimized = 2,
        [InspectorName("RealBlend 5-Way (HDRP / URP)")]
        RealBlend5Way = 3
    }

    public enum MaskTextureKind
    {
        Unknown,
        Metallic,
        AmbientOcclusion,
        Roughness,
        Smoothness,
        Height,
        DetailMask,
        BaseColorAnchor,
        NormalAnchor,
        EmissiveAnchor,
        PackedRealBlendMask
    }

    public sealed class ParsedMaskTextureName
    {
        public MaskTextureKind Kind;
        public string GroupKey;
        public string GroupDisplayName;
        public int MatchScore;
    }

    public sealed class DiscoveredMaskTexture
    {
        public string AssetPath;
        public MaskTextureKind Kind;
        public int MatchScore;
    }

    public sealed class DiscoveredMaskSet
    {
        public string Folder;
        public string GroupKey;
        public string DisplayName;
        public DiscoveredMaskTexture Metallic;
        public DiscoveredMaskTexture AmbientOcclusion;
        public DiscoveredMaskTexture Roughness;
        public DiscoveredMaskTexture Smoothness;
        public DiscoveredMaskTexture Height;
        public DiscoveredMaskTexture DetailMask;
        public DiscoveredMaskTexture BaseColor;
        public DiscoveredMaskTexture Normal;
        public DiscoveredMaskTexture Emissive;
        public DiscoveredMaskTexture PackedMask;
        public bool HasConflict;
        public readonly HashSet<MaskTextureKind> AmbiguousMaterialKinds = new HashSet<MaskTextureKind>();
        public bool HasMaterialConflict => AmbiguousMaterialKinds.Count > 0;

        public DiscoveredMaskTexture SurfaceTexture => Smoothness ?? Roughness;
        public bool UsesSmoothnessInput => Smoothness != null;
        public bool HasBaseColorAnchor => BaseColor != null;
        public bool HasNormalAnchor => Normal != null;

        public bool IsEligibleForOptimizedExistingMask()
        {
            return !HasConflict && PackedMask != null;
        }

        public DiscoveredMaskTexture GetBlueTexture(MaskMapProfile profile)
        {
            return profile == MaskMapProfile.HDRPLit ? DetailMask : Height;
        }

        public bool IsEligibleForAutomaticPacking(MaskMapProfile profile)
        {
            int inputCount = 0;
            if (Metallic != null) inputCount++;
            if (AmbientOcclusion != null) inputCount++;
            if (SurfaceTexture != null) inputCount++;
            if (GetBlueTexture(profile) != null) inputCount++;
            bool hasAnchor = HasBaseColorAnchor || HasNormalAnchor;
            return !HasConflict && (inputCount >= 2 || inputCount == 1 && hasAnchor);
        }

        internal void Add(DiscoveredMaskTexture candidate, List<string> warnings)
        {
            switch (candidate.Kind)
            {
                case MaskTextureKind.Metallic:
                    Metallic = SelectBest(Metallic, candidate, warnings);
                    break;
                case MaskTextureKind.AmbientOcclusion:
                    AmbientOcclusion = SelectBest(AmbientOcclusion, candidate, warnings);
                    break;
                case MaskTextureKind.Roughness:
                    Roughness = SelectBest(Roughness, candidate, warnings);
                    break;
                case MaskTextureKind.Smoothness:
                    Smoothness = SelectBest(Smoothness, candidate, warnings);
                    break;
                case MaskTextureKind.Height:
                    Height = SelectBest(Height, candidate, warnings);
                    break;
                case MaskTextureKind.DetailMask:
                    DetailMask = SelectBest(DetailMask, candidate, warnings);
                    break;
                case MaskTextureKind.BaseColorAnchor:
                    BaseColor = SelectBest(BaseColor, candidate, warnings, false);
                    break;
                case MaskTextureKind.NormalAnchor:
                    Normal = SelectBest(Normal, candidate, warnings, false);
                    break;
                case MaskTextureKind.EmissiveAnchor:
                    Emissive = SelectBest(Emissive, candidate, warnings, false);
                    break;
                case MaskTextureKind.PackedRealBlendMask:
                    PackedMask = SelectBest(PackedMask, candidate, warnings);
                    break;
            }
        }

        private DiscoveredMaskTexture SelectBest(
            DiscoveredMaskTexture current,
            DiscoveredMaskTexture candidate,
            List<string> warnings,
            bool affectsPacking = true)
        {
            if (current == null)
            {
                return candidate;
            }

            DiscoveredMaskTexture selected = candidate.MatchScore > current.MatchScore
                ? candidate
                : current;
            DiscoveredMaskTexture ignored = selected == candidate ? current : candidate;
            if (candidate.MatchScore == current.MatchScore)
            {
                if (affectsPacking)
                {
                    HasConflict = true;
                }
                else
                {
                    AmbiguousMaterialKinds.Add(candidate.Kind);
                }

                string consequence = affectsPacking
                    ? "The set will be skipped."
                    : "Mask packing can continue; a new material leaves that slot unassigned, and an existing generated material keeps its current assignment.";
                warnings.Add(
                    $"{DisplayName}: ambiguous duplicate {candidate.Kind} maps '{Path.GetFileName(current.AssetPath)}' and '{Path.GetFileName(candidate.AssetPath)}'. {consequence}");
                return current;
            }

            warnings.Add(
                $"{DisplayName}: found multiple {candidate.Kind} maps. Using '{Path.GetFileName(selected.AssetPath)}' and ignoring '{Path.GetFileName(ignored.AssetPath)}'.");
            return selected;
        }
    }

    public sealed class MaskSetDiscoveryResult
    {
        public readonly List<DiscoveredMaskSet> Sets = new List<DiscoveredMaskSet>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> SkippedReasons = new List<string>();
        public int TextureCountScanned;
        public int TextureCountRecognized;
        public int CandidateSetCount;
        public int SkippedSetCount;
    }

    public sealed class MaskPackingRequest
    {
        public Texture2D Metallic;
        public Texture2D AmbientOcclusion;
        public Texture2D RoughnessOrSmoothness;
        public Texture2D BlueMask;
        public bool UseSmoothnessInput;
        public float DefaultMetallic;
        public float DefaultAmbientOcclusion = 1f;
        public float DefaultSmoothness = 0.5f;
        public float DefaultBlue = 1f;
        public int OverrideWidth;
        public int OverrideHeight;
        public bool PreserveSourceResolution = true;
        public bool RejectMismatchedDimensions;
        public bool CompressOutput;
        public string OutputFolder = "Assets";
        public string OutputFileName = "MaskMap.png";
        public bool GenerateUniquePath = true;
    }

    public sealed class MaskPackingResult
    {
        public string OutputPath;
        public int Width;
        public int Height;
        public int InputsReimported;
        public readonly List<string> Warnings = new List<string>();
    }

    public static class MaskTextureNameParser
    {
        private sealed class Alias
        {
            public readonly MaskTextureKind Kind;
            public readonly string[] Tokens;
            public readonly int Score;
            public readonly bool RequiresUppercase;

            public Alias(MaskTextureKind kind, int score, string alias, bool requiresUppercase = false)
            {
                Kind = kind;
                Score = score;
                Tokens = alias.Split(' ');
                RequiresUppercase = requiresUppercase;
            }
        }

        private sealed class NameToken
        {
            public string Original;
            public string Normalized;
        }

        private static readonly Regex CamelBoundary = new Regex(
            @"(?<=[a-z])(?=[A-Z])|(?<=[0-9])(?=[A-Z][a-z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled);

        private static readonly Regex ResolutionOrVersion = new Regex(
            @"^(?:\d+(?:k|m)?|v\d+|lod\d+|udim\d*|1\d{3})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> TrailingQualifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "map", "texture", "tex", "linear", "raw", "gray", "grey", "grayscale", "greyscale",
            "gl", "dx", "opengl", "directx", "rgb", "rgba", "srgb", "8bit", "16bit", "32bit"
        };

        private static readonly HashSet<string> NonIdentityTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "t", "map", "texture", "tex", "linear", "raw", "srgb", "gray", "grey", "grayscale", "greyscale",
            "8bit", "16bit", "32bit"
        };

        private static readonly HashSet<string> LeadingWrappers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "t", "tex", "texture", "map"
        };

        private static readonly string[][] ExcludedPackedAliases =
        {
            new[] { "mask", "map" },
            new[] { "maskmap" },
            new[] { "hdrp", "mask" },
            new[] { "hdrpmask" },
            new[] { "real", "blend", "mask" },
            new[] { "realblendmask" },
            new[] { "packed" },
            new[] { "channel", "packed" },
            new[] { "orm" },
            new[] { "arm" },
            new[] { "rma" },
            new[] { "mra" },
            new[] { "rmao" },
            new[] { "mrao" },
            new[] { "aorm" },
            new[] { "metallic", "roughness" },
            new[] { "metallicroughness" },
            new[] { "metal", "roughness" },
            new[] { "metalroughness" },
            new[] { "metallic", "smoothness" },
            new[] { "metallicsmoothness" },
            new[] { "metal", "smoothness" },
            new[] { "metalsmoothness" },
            new[] { "occlusionroughnessmetallic" },
            new[] { "ambientroughnessmetallic" },
            new[] { "specular", "glossiness" }
        };

        private static readonly List<Alias> Aliases = BuildAliases()
            .OrderByDescending(alias => alias.Tokens.Length)
            .ThenByDescending(alias => alias.Score)
            .ToList();

        private static readonly string[][] PackedMaskSuffixes =
        {
            new[] { "real", "blend", "mask", "map" },
            new[] { "realblend", "mask", "map" },
            new[] { "realblendmaskmap" },
            new[] { "real", "blend", "mask" },
            new[] { "realblend", "mask" },
            new[] { "realblendmask" },
            new[] { "mask", "map" },
            new[] { "maskmap" },
            new[] { "mask" }
        };

        public static bool TryParse(string fileNameWithoutExtension, out ParsedMaskTextureName parsed)
        {
            parsed = null;
            List<NameToken> tokens = Tokenize(fileNameWithoutExtension);
            if (tokens.Count < 2)
            {
                return false;
            }

            if (ContainsExcludedPackedSemantic(tokens) ||
                TryParsePackedRealBlendMask(fileNameWithoutExtension, out _))
            {
                return false;
            }

            Alias bestAlias = null;
            int bestIndex = -1;
            int bestScore = int.MinValue;
            bool bestIsSuffix = false;

            foreach (Alias alias in Aliases)
            {
                for (int index = 0; index <= tokens.Count - alias.Tokens.Length; index++)
                {
                    if (!Matches(tokens, index, alias))
                    {
                        continue;
                    }

                    int afterIndex = index + alias.Tokens.Length;
                    bool prefix = index == 0 || AllLeadingTokensAreWrappers(tokens, index);
                    bool exactSuffix = afterIndex == tokens.Count;
                    bool suffixBeforeQualifiers = index > 0 && AllTrailingTokensAreQualifiers(tokens, afterIndex);
                    if (!prefix && !exactSuffix && !suffixBeforeQualifiers)
                    {
                        continue;
                    }

                    int positionBonus = exactSuffix ? 35 : prefix ? 30 : 25;
                    int score = alias.Score + positionBonus + alias.Tokens.Length * 4;
                    bool isSuffix = exactSuffix || suffixBeforeQualifiers;
                    // A trailing map role outranks a material-name adjective such as
                    // 'rough' in rough_plaster_brick_04_diff_2k.
                    if (bestAlias == null ||
                        isSuffix && !bestIsSuffix ||
                        isSuffix == bestIsSuffix &&
                        (alias.Tokens.Length > bestAlias.Tokens.Length ||
                         alias.Tokens.Length == bestAlias.Tokens.Length && score > bestScore))
                    {
                        bestAlias = alias;
                        bestIndex = index;
                        bestScore = score;
                        bestIsSuffix = isSuffix;
                    }
                }
            }

            if (bestAlias == null)
            {
                return false;
            }

            var identityTokens = new List<NameToken>();
            for (int i = 0; i < tokens.Count; i++)
            {
                bool belongsToAlias = i >= bestIndex && i < bestIndex + bestAlias.Tokens.Length;
                if (!belongsToAlias && !NonIdentityTokens.Contains(tokens[i].Normalized))
                {
                    identityTokens.Add(tokens[i]);
                }
            }

            if (identityTokens.Count == 0)
            {
                return false;
            }

            string displayName = string.Join("_", identityTokens.Select(token => token.Original));
            string groupKey = string.Join("_", identityTokens.Select(token => token.Normalized));
            parsed = new ParsedMaskTextureName
            {
                Kind = bestAlias.Kind,
                GroupKey = groupKey,
                GroupDisplayName = displayName,
                MatchScore = bestScore
            };
            return true;
        }

        /// <summary>
        /// Parses a texture that is already packed in RealBlend's
        /// R=Metallic, G=AO, B=Height, A=Smoothness layout. This is kept
        /// separate from TryParse so packed outputs never become channel
        /// inputs during normal automatic packing.
        /// </summary>
        public static bool TryParsePackedRealBlendMask(
            string fileNameWithoutExtension,
            out ParsedMaskTextureName parsed)
        {
            parsed = null;
            List<NameToken> tokens = Tokenize(fileNameWithoutExtension);
            if (tokens.Count < 2)
                return false;
            if (IsDedicatedMaskRole(fileNameWithoutExtension)) return false;

            string[] bestSuffix = null;
            int bestIndex = -1;
            foreach (string[] suffix in PackedMaskSuffixes.OrderByDescending(item => item.Length))
            {
                for (int index = 1; index <= tokens.Count - suffix.Length; index++)
                {
                    bool matches = true;
                    for (int offset = 0; offset < suffix.Length; offset++)
                    {
                        if (!string.Equals(
                                tokens[index + offset].Normalized,
                                suffix[offset],
                                StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                        continue;

                    string precedingToken = tokens[index - 1].Normalized;
                    if (string.Equals(precedingToken, "detail", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(precedingToken, "hdrp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int afterIndex = index + suffix.Length;
                    if (afterIndex < tokens.Count && !AllTrailingTokensAreQualifiers(tokens, afterIndex))
                        continue;

                    bestSuffix = suffix;
                    bestIndex = index;
                    break;
                }

                if (bestSuffix != null)
                    break;
            }

            if (bestSuffix == null || bestIndex <= 0)
                return false;

            List<NameToken> identityTokens = tokens
                .Take(bestIndex)
                // Keep explicit resolutions/variants after Mask, just as TryParse does.
                // Stone_Mask_2k and Stone_Mask_4k must not collapse into one set.
                .Concat(tokens.Skip(bestIndex + bestSuffix.Length)
                    .Where(token => ResolutionOrVersion.IsMatch(token.Normalized)))
                .Where(token => !NonIdentityTokens.Contains(token.Normalized))
                .ToList();
            if (identityTokens.Count == 0)
                return false;

            bool explicitRealBlend = bestSuffix.Any(token =>
                string.Equals(token, "real", StringComparison.OrdinalIgnoreCase) ||
                token.IndexOf("realblend", StringComparison.OrdinalIgnoreCase) >= 0);
            bool explicitMaskMap = bestSuffix.Any(token =>
                string.Equals(token, "map", StringComparison.OrdinalIgnoreCase) ||
                token.IndexOf("maskmap", StringComparison.OrdinalIgnoreCase) >= 0);
            parsed = new ParsedMaskTextureName
            {
                Kind = MaskTextureKind.PackedRealBlendMask,
                GroupKey = string.Join("_", identityTokens.Select(token => token.Normalized)),
                GroupDisplayName = string.Join("_", identityTokens.Select(token => token.Original)),
                MatchScore = explicitRealBlend ? 300 : explicitMaskMap ? 200 : 100
            };
            return true;
        }

        internal static bool IsDedicatedMaskRole(string name)
        {
            // These explicit map roles have a different purpose/layout. A material
            // named 'wetness_rock' remains valid; its adjective alone is not a role.
            List<NameToken> tokens = Tokenize(name);
            for (int index = 0; index < tokens.Count; index++)
            {
                string token = tokens[index].Normalized;
                bool dedicated = token == "wetnessmask" || token == "hdrpmask" || token == "detailmask" ||
                                 token == "detailblendmask" ||
                                 token == "mask" && index > 0 &&
                                 (tokens[index - 1].Normalized == "wetness" || tokens[index - 1].Normalized == "hdrp" ||
                                  tokens[index - 1].Normalized == "detail" || index > 1 &&
                                  tokens[index - 1].Normalized == "blend" && tokens[index - 2].Normalized == "detail");
                if (dedicated && AllTrailingTokensAreQualifiers(tokens, index + 1)) return true;
            }
            return false;
        }

        private static bool Matches(List<NameToken> tokens, int index, Alias alias)
        {
            for (int i = 0; i < alias.Tokens.Length; i++)
            {
                NameToken token = tokens[index + i];
                if (!string.Equals(token.Normalized, alias.Tokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (alias.RequiresUppercase && !string.Equals(token.Original, token.Original.ToUpperInvariant(), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllTrailingTokensAreQualifiers(List<NameToken> tokens, int startIndex)
        {
            if (startIndex >= tokens.Count)
            {
                return true;
            }

            for (int i = startIndex; i < tokens.Count; i++)
            {
                string token = tokens[i].Normalized;
                if (!TrailingQualifiers.Contains(token) && !ResolutionOrVersion.IsMatch(token))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllLeadingTokensAreWrappers(List<NameToken> tokens, int endIndex)
        {
            if (endIndex <= 0)
            {
                return true;
            }

            for (int i = 0; i < endIndex; i++)
            {
                if (!LeadingWrappers.Contains(tokens[i].Normalized))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsExcludedPackedSemantic(List<NameToken> tokens)
        {
            foreach (string[] alias in ExcludedPackedAliases.OrderByDescending(item => item.Length))
            {
                for (int index = 0; index <= tokens.Count - alias.Length; index++)
                {
                    bool matches = true;
                    for (int offset = 0; offset < alias.Length; offset++)
                    {
                        if (!string.Equals(tokens[index + offset].Normalized, alias[offset], StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                    {
                        continue;
                    }

                    if (alias.Length == 2 && alias[0] == "mask" && alias[1] == "map" &&
                        index > 0 && tokens[index - 1].Normalized == "detail")
                    {
                        continue;
                    }

                    int afterIndex = index + alias.Length;
                    if (index == 0 || afterIndex == tokens.Count || index > 0 && AllTrailingTokensAreQualifiers(tokens, afterIndex))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<NameToken> Tokenize(string value)
        {
            // Preserve the conventional sRGB qualifier as one token instead of s + RGB.
            string normalized = Regex.Replace(value ?? string.Empty,
                @"(?<![A-Za-z0-9])srgb(?![A-Za-z0-9])", "srgb", RegexOptions.IgnoreCase);
            string expanded = CamelBoundary.Replace(normalized, "_");
            return Regex.Split(expanded, @"[^A-Za-z0-9]+")
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => new NameToken
                {
                    Original = token,
                    Normalized = token.ToLowerInvariant()
                })
                .ToList();
        }

        private static IEnumerable<Alias> BuildAliases()
        {
            // Metallic
            yield return new Alias(MaskTextureKind.Metallic, 120, "metallic map");
            yield return new Alias(MaskTextureKind.Metallic, 120, "metalness map");
            yield return new Alias(MaskTextureKind.Metallic, 115, "metallicmap");
            yield return new Alias(MaskTextureKind.Metallic, 115, "metalnessmap");
            yield return new Alias(MaskTextureKind.Metallic, 110, "metallic");
            yield return new Alias(MaskTextureKind.Metallic, 110, "metalness");
            yield return new Alias(MaskTextureKind.Metallic, 100, "metalic");
            yield return new Alias(MaskTextureKind.Metallic, 95, "metal");
            yield return new Alias(MaskTextureKind.Metallic, 90, "mtl");
            yield return new Alias(MaskTextureKind.Metallic, 90, "met");

            // Ambient occlusion
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 130, "ambient occlusion map");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 125, "ambient occlusion");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 120, "ambientocclusionmap");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 120, "ambientocclusion");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 115, "occlusion map");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 110, "ao map");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 105, "occlusion");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 105, "ao");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 95, "ambientocc");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 95, "ambocc");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 90, "occl");
            yield return new Alias(MaskTextureKind.AmbientOcclusion, 85, "occ");

            // Roughness
            yield return new Alias(MaskTextureKind.Roughness, 120, "roughness map");
            yield return new Alias(MaskTextureKind.Roughness, 115, "roughnessmap");
            yield return new Alias(MaskTextureKind.Roughness, 110, "roughness");
            yield return new Alias(MaskTextureKind.Roughness, 100, "rough");
            yield return new Alias(MaskTextureKind.Roughness, 90, "rgh");
            yield return new Alias(MaskTextureKind.Roughness, 85, "rghns");
            yield return new Alias(MaskTextureKind.Roughness, 80, "rog");

            // Smoothness / gloss
            yield return new Alias(MaskTextureKind.Smoothness, 120, "smoothness map");
            yield return new Alias(MaskTextureKind.Smoothness, 120, "glossiness map");
            yield return new Alias(MaskTextureKind.Smoothness, 115, "smoothnessmap");
            yield return new Alias(MaskTextureKind.Smoothness, 115, "glossinessmap");
            yield return new Alias(MaskTextureKind.Smoothness, 110, "smoothness");
            yield return new Alias(MaskTextureKind.Smoothness, 110, "glossiness");
            yield return new Alias(MaskTextureKind.Smoothness, 100, "smooth");
            yield return new Alias(MaskTextureKind.Smoothness, 100, "gloss");
            yield return new Alias(MaskTextureKind.Smoothness, 85, "glss");
            yield return new Alias(MaskTextureKind.Smoothness, 85, "smth");
            yield return new Alias(MaskTextureKind.Smoothness, 80, "glo");
            yield return new Alias(MaskTextureKind.Smoothness, 80, "smo");

            // Height / displacement
            yield return new Alias(MaskTextureKind.Height, 125, "displacement map");
            yield return new Alias(MaskTextureKind.Height, 120, "height map");
            yield return new Alias(MaskTextureKind.Height, 120, "bump map");
            yield return new Alias(MaskTextureKind.Height, 115, "displacementmap");
            yield return new Alias(MaskTextureKind.Height, 115, "heightmap");
            yield return new Alias(MaskTextureKind.Height, 115, "bumpmap");
            yield return new Alias(MaskTextureKind.Height, 110, "displacement");
            yield return new Alias(MaskTextureKind.Height, 110, "height");
            yield return new Alias(MaskTextureKind.Height, 105, "bump");
            yield return new Alias(MaskTextureKind.Height, 100, "disp");
            yield return new Alias(MaskTextureKind.Height, 95, "displace");
            yield return new Alias(MaskTextureKind.Height, 90, "depth");
            yield return new Alias(MaskTextureKind.Height, 90, "parallax");
            yield return new Alias(MaskTextureKind.Height, 85, "hmap");
            yield return new Alias(MaskTextureKind.Height, 85, "hgt");
            yield return new Alias(MaskTextureKind.Height, 85, "bmap");
            yield return new Alias(MaskTextureKind.Height, 80, "zmap");

            // HDRP Lit detail mask
            yield return new Alias(MaskTextureKind.DetailMask, 135, "detail blend mask");
            yield return new Alias(MaskTextureKind.DetailMask, 130, "detail mask map");
            yield return new Alias(MaskTextureKind.DetailMask, 125, "detail mask");
            yield return new Alias(MaskTextureKind.DetailMask, 120, "detailmaskmap");
            yield return new Alias(MaskTextureKind.DetailMask, 120, "detailmask");
            yield return new Alias(MaskTextureKind.DetailMask, 110, "detailblend");
            yield return new Alias(MaskTextureKind.DetailMask, 105, "dmask");

            // Non-packing anchors. These make a one-channel set safe enough for unattended conversion.
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 130, "base color map");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 130, "base colour map");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 125, "base color");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 125, "base colour");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 125, "basecolormap");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 125, "basecolourmap");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 120, "basecolor");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 120, "basecolour");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 120, "albedomap");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 115, "diffusemap");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 115, "albedo");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 110, "diffuse");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 100, "diff");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 90, "color");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 85, "colour");
            yield return new Alias(MaskTextureKind.BaseColorAnchor, 80, "col");

            yield return new Alias(MaskTextureKind.EmissiveAnchor, 125, "emissive map");
            yield return new Alias(MaskTextureKind.EmissiveAnchor, 120, "emissive");
            yield return new Alias(MaskTextureKind.EmissiveAnchor, 115, "emission map");
            yield return new Alias(MaskTextureKind.EmissiveAnchor, 110, "emission");
            yield return new Alias(MaskTextureKind.EmissiveAnchor, 90, "emit");

            yield return new Alias(MaskTextureKind.NormalAnchor, 150, "normal opengl");
            yield return new Alias(MaskTextureKind.NormalAnchor, 145, "normal gl");
            yield return new Alias(MaskTextureKind.NormalAnchor, 140, "nor gl");
            yield return new Alias(MaskTextureKind.NormalAnchor, 135, "normal map");
            yield return new Alias(MaskTextureKind.NormalAnchor, 130, "normalmap");
            yield return new Alias(MaskTextureKind.NormalAnchor, 125, "normal");
            yield return new Alias(MaskTextureKind.NormalAnchor, 120, "nrm");
            yield return new Alias(MaskTextureKind.NormalAnchor, 115, "norm");
            yield return new Alias(MaskTextureKind.NormalAnchor, 110, "nor");
            yield return new Alias(MaskTextureKind.NormalAnchor, 105, "nmap");
            yield return new Alias(MaskTextureKind.NormalAnchor, 100, "normal directx");
            yield return new Alias(MaskTextureKind.NormalAnchor, 95, "normal dx");
            yield return new Alias(MaskTextureKind.NormalAnchor, 90, "nor dx");
        }
    }

    public static class MaskMapPackingUtility
    {
        public const float HDRPUnmaskedDetailValue = 1f;

        private const string PackingShaderName = "Hidden/RealBlend/MaskPacker";
        private const string PackingShaderPath = "Assets/RealBlend/MaskPacking/Editor/MaskPacker.shader";
        private const int MaximumImporterSize = 16384;

        private sealed class PreparedTexture
        {
            public Texture2D Texture;
            public string AssetPath;
            public bool Changed;
            public bool HasTemporaryImportSettings;
            public string PlatformName;
            public TextureImporterCompression OriginalCompression;
            public bool OriginalCrunchedCompression;
            public int OriginalCompressionQuality;
            public int OriginalMaxTextureSize;
            public TextureImporterNPOTScale OriginalNpotScale;
            public bool OriginalIgnoreMipmapLimit;
            public bool HadPlatformOverride;
            public TextureImporterPlatformSettings OriginalPlatformSettings;
        }

        public static MaskSetDiscoveryResult DiscoverMaskSets(string folder, bool includeSubfolders, MaskMapProfile profile)
        {
            var result = new MaskSetDiscoveryResult();
            folder = NormalizeAssetPath(folder);
            if (!IsWritableAssetFolder(folder))
            {
                result.Warnings.Add($"'{folder}' is not a writable folder under Assets.");
                return result;
            }

            var setsByKey = new Dictionary<string, DiscoveredMaskSet>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (string guid in guids)
            {
                string path = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!includeSubfolders && !string.Equals(GetAssetFolder(path), folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.TextureCountScanned++;
                string stem = Path.GetFileNameWithoutExtension(path);
                if (!MaskTextureNameParser.TryParse(stem, out ParsedMaskTextureName parsed))
                {
                    continue;
                }

                result.TextureCountRecognized++;
                string assetFolder = GetAssetFolder(path);
                string compositeKey = assetFolder.ToLowerInvariant() + "|" + parsed.GroupKey;
                if (!setsByKey.TryGetValue(compositeKey, out DiscoveredMaskSet set))
                {
                    set = new DiscoveredMaskSet
                    {
                        Folder = assetFolder,
                        GroupKey = parsed.GroupKey,
                        DisplayName = SanitizeFileStem(parsed.GroupDisplayName)
                    };
                    setsByKey.Add(compositeKey, set);
                }

                int extensionScore = GetExtensionScore(Path.GetExtension(path));
                set.Add(new DiscoveredMaskTexture
                {
                    AssetPath = path,
                    Kind = parsed.Kind,
                    MatchScore = parsed.MatchScore + extensionScore
                }, result.Warnings);
            }

            foreach (DiscoveredMaskSet set in setsByKey.Values.OrderBy(set => set.Folder).ThenBy(set => set.DisplayName))
            {
                result.CandidateSetCount++;
                if (set.Roughness != null && set.Smoothness != null)
                {
                    result.Warnings.Add(
                        $"{set.DisplayName}: both roughness and smoothness/gloss were found. " +
                        $"Using '{Path.GetFileName(set.Smoothness.AssetPath)}' directly as smoothness and " +
                        $"leaving '{Path.GetFileName(set.Roughness.AssetPath)}' unused.");
                }

                if (!set.IsEligibleForAutomaticPacking(profile))
                {
                    result.SkippedSetCount++;
                    result.SkippedReasons.Add(DescribeAutomaticPackingRejection(set, profile));
                    continue;
                }

                result.Sets.Add(set);
            }

            return result;
        }

        private static string DescribeAutomaticPackingRejection(
            DiscoveredMaskSet set,
            MaskMapProfile profile)
        {
            if (set.HasConflict)
            {
                return $"{set.DisplayName}: ambiguous duplicate channel maps; see the matching warning for the conflicting files.";
            }

            int inputCount = 0;
            if (set.Metallic != null) inputCount++;
            if (set.AmbientOcclusion != null) inputCount++;
            if (set.SurfaceTexture != null) inputCount++;
            if (set.GetBlueTexture(profile) != null) inputCount++;

            if (inputCount == 0)
            {
                return $"{set.DisplayName}: Base Color/Normal textures were found, but no packable Metallic, AO, Roughness/Gloss, or " +
                       (profile == MaskMapProfile.HDRPLit ? "Detail Mask" : "Height") + " texture was found.";
            }

            if (inputCount == 1 && !set.HasBaseColorAnchor && !set.HasNormalAnchor)
            {
                return $"{set.DisplayName}: only one channel map was found and there is no matching Base Color or Normal to confirm the texture-set name.";
            }

            return $"{set.DisplayName}: the recognized maps did not form a safe automatic texture set.";
        }

        /// <summary>
        /// Discovers already-packed RealBlend masks for migration to the optimized
        /// texture-array shader. Unlike DiscoverMaskSets, this never treats the
        /// packed texture as a channel source and never rewrites it.
        /// </summary>
        public static MaskSetDiscoveryResult DiscoverOptimizedExistingMaskSets(
            string folder,
            bool includeSubfolders)
        {
            var result = new MaskSetDiscoveryResult();
            folder = NormalizeAssetPath(folder);
            if (!IsWritableAssetFolder(folder))
            {
                result.Warnings.Add($"'{folder}' is not a writable folder under Assets.");
                return result;
            }

            var setsByKey = new Dictionary<string, DiscoveredMaskSet>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (string guid in guids)
            {
                string path = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!includeSubfolders &&
                    !string.Equals(GetAssetFolder(path), folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.TextureCountScanned++;
                string stem = Path.GetFileNameWithoutExtension(path);
                ParsedMaskTextureName parsed;
                if (!MaskTextureNameParser.TryParsePackedRealBlendMask(stem, out parsed))
                {
                    if (!MaskTextureNameParser.TryParse(stem, out parsed) ||
                        parsed.Kind != MaskTextureKind.BaseColorAnchor &&
                        parsed.Kind != MaskTextureKind.NormalAnchor)
                    {
                        continue;
                    }
                }

                result.TextureCountRecognized++;
                string assetFolder = GetAssetFolder(path);
                string compositeKey = assetFolder.ToLowerInvariant() + "|" + parsed.GroupKey;
                if (!setsByKey.TryGetValue(compositeKey, out DiscoveredMaskSet set))
                {
                    set = new DiscoveredMaskSet
                    {
                        Folder = assetFolder,
                        GroupKey = parsed.GroupKey,
                        DisplayName = SanitizeFileStem(parsed.GroupDisplayName)
                    };
                    setsByKey.Add(compositeKey, set);
                }

                set.Add(new DiscoveredMaskTexture
                {
                    AssetPath = path,
                    Kind = parsed.Kind,
                    MatchScore = parsed.MatchScore + GetExtensionScore(Path.GetExtension(path))
                }, result.Warnings);
            }

            AddExistingMaterialBindings(setsByKey, folder, includeSubfolders, result.Warnings);
            MatchResolutionQualifiedAnchors(setsByKey.Values.ToList(), result.Warnings);

            foreach (DiscoveredMaskSet set in setsByKey.Values
                         .OrderBy(set => set.Folder)
                         .ThenBy(set => set.DisplayName))
            {
                result.CandidateSetCount++;
                if (!set.IsEligibleForOptimizedExistingMask())
                {
                    result.SkippedSetCount++;
                    result.SkippedReasons.Add(
                        set.HasConflict
                            ? $"{set.DisplayName}: multiple equally matched packed masks were found."
                            : $"{set.DisplayName}: matching Base Color/Normal textures were found, but no packed RealBlend mask was found.");
                    continue;
                }

                if (set.BaseColor == null)
                {
                    result.Warnings.Add(
                        $"{set.DisplayName}: no unambiguous Base Color was found. Assign one explicitly before creating the material.");
                }
                if (set.Normal == null)
                {
                    result.Warnings.Add(
                        $"{set.DisplayName}: no unambiguous Normal was found. Assign one explicitly or use the shader's flat-normal default.");
                }
                result.Sets.Add(set);
            }

            return result;
        }

        private sealed class MaterialLayerBinding
        {
            public string MaskPath;
            public string AlbedoPath;
            public string NormalPath;
        }

        private static void AddExistingMaterialBindings(
            IDictionary<string, DiscoveredMaskSet> setsByKey,
            string folder,
            bool includeSubfolders,
            List<string> warnings)
        {
            var bindings = new List<MaterialLayerBinding>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                string shaderName = material?.shader?.name;
                if (shaderName != "Shader Graphs/RealBlendVariationHDRP" &&
                    shaderName != "Shader Graphs/RealBlendVariationURP" &&
                    shaderName != "Shader Graphs/RealBlend5WayBlendHDRP" &&
                    shaderName != "Shader Graphs/RealBlend5WayBlendURP" &&
                    shaderName != "RealBlend/RealBlendBuiltInVariation")
                    continue;

                for (int slot = 0; slot < 5; slot++)
                {
                    string prefix = slot == 0 ? "_Base" : "_Layer" + slot;
                    if (!material.HasProperty(prefix + "_MaskMap") ||
                        !material.HasProperty(prefix + "_Albedo") || !material.HasProperty(prefix + "_Normal"))
                        continue;
                    Texture2D mask = material.GetTexture(prefix + "_MaskMap") as Texture2D;
                    Texture2D albedo = material.GetTexture(prefix + "_Albedo") as Texture2D;
                    Texture2D normal = material.GetTexture(prefix + "_Normal") as Texture2D;
                    if (mask == null || albedo == null || normal == null) continue;
                    string maskPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(mask));
                    string albedoPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(albedo));
                    string normalPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(normal));
                    string maskFolder = GetAssetFolder(maskPath);
                    bool inScope = string.Equals(maskFolder, folder, StringComparison.OrdinalIgnoreCase) ||
                                   includeSubfolders && maskFolder.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
                    // A saved layer is evidence of association only when all three assets
                    // share the same physical folder. Never infer from the first texture found.
                    if (!inScope || string.IsNullOrEmpty(maskPath) || string.IsNullOrEmpty(albedoPath) ||
                        string.IsNullOrEmpty(normalPath) ||
                        !string.Equals(maskFolder, GetAssetFolder(albedoPath), StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(maskFolder, GetAssetFolder(normalPath), StringComparison.OrdinalIgnoreCase))
                        continue;
                    bindings.Add(new MaterialLayerBinding { MaskPath = maskPath, AlbedoPath = albedoPath, NormalPath = normalPath });
                }
            }

            foreach (var maskBindings in bindings.GroupBy(binding => binding.MaskPath, StringComparer.OrdinalIgnoreCase))
            {
                string maskPath = maskBindings.Key;
                DiscoveredMaskSet set = setsByKey.Values.FirstOrDefault(candidate => string.Equals(
                    candidate.PackedMask?.AssetPath, maskPath, StringComparison.OrdinalIgnoreCase));
                if (set == null)
                {
                    string stem = Path.GetFileNameWithoutExtension(maskPath);
                    if (MaskTextureNameParser.IsDedicatedMaskRole(stem)) continue;
                    if (!MaskTextureNameParser.TryParsePackedRealBlendMask(stem, out ParsedMaskTextureName parsed))
                    {
                        // A mask without a role suffix is supported only because a RealBlend
                        // material explicitly binds it. Its channels still need confirmation.
                        if (!MaskTextureNameParser.TryParsePackedRealBlendMask(stem + "_Mask", out parsed))
                            continue;
                    }
                    string maskFolder = GetAssetFolder(maskPath);
                    string key = maskFolder.ToLowerInvariant() + "|" + parsed.GroupKey;
                    if (!setsByKey.TryGetValue(key, out set))
                    {
                        set = new DiscoveredMaskSet { Folder = maskFolder, GroupKey = parsed.GroupKey,
                            DisplayName = SanitizeFileStem(parsed.GroupDisplayName) };
                        setsByKey.Add(key, set);
                    }
                    set.Add(new DiscoveredMaskTexture { AssetPath = maskPath, Kind = MaskTextureKind.PackedRealBlendMask,
                        MatchScore = parsed.MatchScore + GetExtensionScore(Path.GetExtension(maskPath)) }, warnings);
                }
                ApplyMaterialBinding(set, maskBindings.Select(binding => binding.AlbedoPath), MaskTextureKind.BaseColorAnchor, warnings);
                ApplyMaterialBinding(set, maskBindings.Select(binding => binding.NormalPath), MaskTextureKind.NormalAnchor, warnings);
            }
        }

        private static void ApplyMaterialBinding(
            DiscoveredMaskSet set, IEnumerable<string> paths, MaskTextureKind role, List<string> warnings)
        {
            string[] distinctPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            DiscoveredMaskTexture texture = null;
            if (distinctPaths.Length == 1)
            {
                texture = new DiscoveredMaskTexture { AssetPath = distinctPaths[0], Kind = role, MatchScore = 10000 };
                set.AmbiguousMaterialKinds.Remove(role);
            }
            else
            {
                set.AmbiguousMaterialKinds.Add(role);
                warnings.Add($"{set.DisplayName}: existing RealBlend materials bind this mask to different {role} textures " +
                             $"({string.Join(", ", distinctPaths.Select(Path.GetFileName))}). Choose the intended texture explicitly.");
            }
            if (role == MaskTextureKind.BaseColorAnchor) set.BaseColor = texture;
            else if (role == MaskTextureKind.NormalAnchor) set.Normal = texture;
        }

        private static void MatchResolutionQualifiedAnchors(IReadOnlyList<DiscoveredMaskSet> sets, List<string> warnings)
        {
            foreach (DiscoveredMaskSet packed in sets.Where(set => set.PackedMask != null && !set.HasConflict))
            {
                if ((packed.BaseColor != null || packed.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor)) &&
                    (packed.Normal != null || packed.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor)))
                    continue;
                string stem = ResolutionIndependentKey(packed.GroupKey);
                // An explicit resolution on the mask must match by the original
                // full group key. Reconciliation is only for untagged mask names.
                if (!string.Equals(stem, packed.GroupKey, StringComparison.OrdinalIgnoreCase)) continue;
                var candidates = sets.Where(candidate => candidate.PackedMask == null && !candidate.HasConflict &&
                    !candidate.HasMaterialConflict && (candidate.BaseColor != null || candidate.Normal != null) &&
                    string.Equals(candidate.Folder, packed.Folder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ResolutionIndependentKey(candidate.GroupKey), stem, StringComparison.OrdinalIgnoreCase) &&
                    MatchingSourceDimensions(packed.PackedMask, candidate.BaseColor) &&
                    MatchingSourceDimensions(packed.PackedMask, candidate.Normal)).ToList();
                if (candidates.Count == 1)
                {
                    if (packed.BaseColor == null && !packed.AmbiguousMaterialKinds.Contains(MaskTextureKind.BaseColorAnchor))
                        packed.BaseColor = candidates[0].BaseColor;
                    if (packed.Normal == null && !packed.AmbiguousMaterialKinds.Contains(MaskTextureKind.NormalAnchor))
                        packed.Normal = candidates[0].Normal;
                }
                else if (candidates.Count > 1)
                {
                    warnings.Add($"{packed.DisplayName}: multiple texture-set variants match this packed mask. " +
                                 "No resolution-qualified texture was guessed; choose its Albedo and Normal explicitly.");
                }
            }
        }

        private static string ResolutionIndependentKey(string key)
        {
            // Keep material variant numbers (02, 04), versions and UDIMs. Only explicit
            // resolution labels may differ between a renamed packed mask and its anchors.
            return string.Join("_", (key ?? string.Empty).Split('_').Where(token =>
                !Regex.IsMatch(token, @"^(?:[1-9]\d*[km]|128|256|512|1024|2048|4096|8192|16384)$", RegexOptions.IgnoreCase)));
        }

        private static bool MatchingSourceDimensions(DiscoveredMaskTexture mask, DiscoveredMaskTexture anchor)
        {
            if (anchor == null) return true;
            var maskImporter = AssetImporter.GetAtPath(mask.AssetPath) as TextureImporter;
            var anchorImporter = AssetImporter.GetAtPath(anchor.AssetPath) as TextureImporter;
            if (maskImporter == null || anchorImporter == null) return false;
            try
            {
                maskImporter.GetSourceTextureWidthAndHeight(out int maskWidth, out int maskHeight);
                anchorImporter.GetSourceTextureWidthAndHeight(out int anchorWidth, out int anchorHeight);
                return maskWidth > 0 && maskHeight > 0 && maskWidth == anchorWidth && maskHeight == anchorHeight;
            }
            catch { return false; }
        }

        public static MaskPackingResult PackAndSave(MaskPackingRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new MaskPackingResult();
            string outputFolder = NormalizeAssetPath(request.OutputFolder);
            if (!IsWritableAssetFolder(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
            {
                throw new InvalidOperationException($"Output folder must be a valid writable folder under Assets: '{outputFolder}'.");
            }

            string outputName = EnsurePngFileName(request.OutputFileName);
            string outputPath = NormalizeAssetPath(Path.Combine(outputFolder, outputName));
            if (request.GenerateUniquePath)
            {
                outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
            }

            ValidateOutputDoesNotReplaceInput(outputPath, new[]
            {
                request.Metallic,
                request.AmbientOcclusion,
                request.RoughnessOrSmoothness,
                request.BlueMask
            });
            Shader packingShader = FindPackingShader();
            var preparedByPath = new Dictionary<string, PreparedTexture>(StringComparer.OrdinalIgnoreCase);
            Texture2D packedTexture = null;
            Exception packingFailure = null;
            try
            {
                PreparedTexture metallic = PrepareDataTexture(
                    request.Metallic, request.PreserveSourceResolution, result.Warnings, preparedByPath);
                PreparedTexture ao = PrepareDataTexture(
                    request.AmbientOcclusion, request.PreserveSourceResolution, result.Warnings, preparedByPath);
                PreparedTexture surface = PrepareDataTexture(
                    request.RoughnessOrSmoothness, request.PreserveSourceResolution, result.Warnings, preparedByPath);
                PreparedTexture blue = PrepareDataTexture(
                    request.BlueMask, request.PreserveSourceResolution, result.Warnings, preparedByPath);
                result.InputsReimported = preparedByPath.Values.Count(input => input.Changed);

                var inputTextures = new[] { metallic.Texture, ao.Texture, surface.Texture, blue.Texture };
                ResolveOutputSize(
                    inputTextures,
                    request.OverrideWidth,
                    request.OverrideHeight,
                    out int width,
                    out int height);
                ValidateOutputSize(width, height);
                if (request.RejectMismatchedDimensions)
                {
                    ValidateMatchingInputDimensions(inputTextures);
                }

                result.Width = width;
                result.Height = height;
                AddDimensionWarnings(inputTextures, width, height, result.Warnings);

                packedTexture = CreatePackedTexture(
                    packingShader,
                    metallic.Texture,
                    ao.Texture,
                    surface.Texture,
                    blue.Texture,
                    request.UseSmoothnessInput,
                    request.DefaultMetallic,
                    request.DefaultAmbientOcclusion,
                    request.DefaultSmoothness,
                    request.DefaultBlue,
                    width,
                    height);

                byte[] png = packedTexture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException("Unity failed to encode the packed texture as PNG.");
                }

                string absolutePath = ToAbsoluteProjectPath(outputPath);
                File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ConfigureOutputImporter(outputPath, width, height, request.CompressOutput);
                result.OutputPath = outputPath;
                return result;
            }
            catch (Exception exception)
            {
                packingFailure = exception;
                throw;
            }
            finally
            {
                if (packedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(packedTexture);
                }

                try
                {
                    RestoreTemporaryImportSettings(preparedByPath.Values, result.Warnings);
                }
                catch (Exception restorationFailure)
                {
                    if (packingFailure != null)
                    {
                        throw new AggregateException(
                            "Mask packing failed and one or more temporary input import settings also could not be restored.",
                            packingFailure,
                            restorationFailure);
                    }

                    throw;
                }
            }
        }

        public static Texture2D CreatePackedTexture(
            Texture2D metallic,
            Texture2D ambientOcclusion,
            Texture2D roughnessOrSmoothness,
            Texture2D blueMask,
            bool useSmoothnessInput,
            float defaultMetallic,
            float defaultAmbientOcclusion,
            float defaultSmoothness,
            float defaultBlue,
            int width,
            int height)
        {
            return CreatePackedTexture(
                FindPackingShader(),
                metallic,
                ambientOcclusion,
                roughnessOrSmoothness,
                blueMask,
                useSmoothnessInput,
                defaultMetallic,
                defaultAmbientOcclusion,
                defaultSmoothness,
                defaultBlue,
                width,
                height);
        }

        private static Texture2D CreatePackedTexture(
            Shader shader,
            Texture2D metallic,
            Texture2D ambientOcclusion,
            Texture2D roughnessOrSmoothness,
            Texture2D blueMask,
            bool useSmoothnessInput,
            float defaultMetallic,
            float defaultAmbientOcclusion,
            float defaultSmoothness,
            float defaultBlue,
            int width,
            int height)
        {
            ValidateOutputSize(width, height);

            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };

            RenderTexture renderTexture = null;
            Material material = null;
            Texture2D output = null;
            RenderTexture previousActive = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            bool succeeded = false;
            try
            {
                renderTexture = RenderTexture.GetTemporary(descriptor);
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_MetallicTex", metallic != null ? metallic : Texture2D.blackTexture);
                material.SetTexture("_AOTex", ambientOcclusion != null ? ambientOcclusion : Texture2D.whiteTexture);
                material.SetTexture("_SurfaceTex", roughnessOrSmoothness != null ? roughnessOrSmoothness : Texture2D.grayTexture);
                material.SetTexture("_BlueTex", blueMask != null ? blueMask : Texture2D.whiteTexture);
                material.SetVector("_Defaults", new Vector4(
                    Mathf.Clamp01(defaultMetallic),
                    Mathf.Clamp01(defaultAmbientOcclusion),
                    Mathf.Clamp01(defaultBlue),
                    Mathf.Clamp01(defaultSmoothness)));
                material.SetVector("_HasTextures", new Vector4(
                    metallic != null ? 1f : 0f,
                    ambientOcclusion != null ? 1f : 0f,
                    blueMask != null ? 1f : 0f,
                    roughnessOrSmoothness != null ? 1f : 0f));
                material.SetFloat("_InvertSurface", useSmoothnessInput ? 0f : 1f);

                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.blackTexture, renderTexture, material, 0);
                RenderTexture.active = renderTexture;
                output = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = "Packed Mask Map"
                };
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                output.Apply(false, false);
                succeeded = true;
                return output;
            }
            finally
            {
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgbWrite;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }

                if (!succeeded && output != null)
                {
                    UnityEngine.Object.DestroyImmediate(output);
                }
            }
        }

        public static string GetAssetFolder(string assetPath)
        {
            string folder = Path.GetDirectoryName(NormalizeAssetPath(assetPath));
            return NormalizeAssetPath(string.IsNullOrEmpty(folder) ? "Assets" : folder);
        }

        public static bool IsWritableAssetFolder(string folder)
        {
            folder = NormalizeAssetPath(folder);
            return string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }

        public static string SanitizeFileStem(string value)
        {
            string stem = Path.GetFileNameWithoutExtension(value ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                stem = stem.Replace(invalid, '_');
            }

            stem = Regex.Replace(stem, @"\s+", "_");
            stem = Regex.Replace(stem, @"_+", "_").Trim('_', '.', ' ');
            return string.IsNullOrEmpty(stem) ? "MaskMap" : stem;
        }

        public static string GetAutomaticOutputFileName(string displayName, MaskMapProfile profile)
        {
            string suffix = profile == MaskMapProfile.HDRPLit
                ? "_HDRPMaskMap.png"
                : "_RealBlendMaskMap.png";
            return SanitizeFileStem(displayName) + suffix;
        }

        private static PreparedTexture PrepareDataTexture(
            Texture2D input,
            bool preserveSourceResolution,
            List<string> warnings,
            IDictionary<string, PreparedTexture> preparedByPath)
        {
            var result = new PreparedTexture { Texture = input };
            if (input == null)
            {
                return result;
            }

            string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(input));
            if (string.IsNullOrEmpty(path))
            {
                return result;
            }

            if (preparedByPath.TryGetValue(path, out PreparedTexture existing))
            {
                return existing;
            }

            result.AssetPath = path;
            preparedByPath.Add(path, result);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                warnings.Add($"Could not inspect import settings for '{path}'.");
                return result;
            }

            string platformName = GetActiveTexturePlatformName();
            TextureImporterPlatformSettings platformSettings = importer.GetPlatformTextureSettings(platformName);
            result.PlatformName = platformName;
            result.OriginalCompression = importer.textureCompression;
            result.OriginalCrunchedCompression = importer.crunchedCompression;
            result.OriginalCompressionQuality = importer.compressionQuality;
            result.OriginalMaxTextureSize = importer.maxTextureSize;
            result.OriginalNpotScale = importer.npotScale;
            result.OriginalIgnoreMipmapLimit = importer.ignoreMipmapLimit;
            result.HadPlatformOverride = platformSettings.overridden;
            result.OriginalPlatformSettings = ClonePlatformSettings(platformSettings);

            bool permanentChange = false;
            bool temporaryChange = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                permanentChange = true;
            }

            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                permanentChange = true;
            }

            if (!importer.ignorePngGamma)
            {
                importer.ignorePngGamma = true;
                permanentChange = true;
            }

            if (preserveSourceResolution)
            {
                try
                {
                    importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                    int sourceMaximum = Mathf.Max(sourceWidth, sourceHeight);
                    int requiredMaxSize = RequiredImporterMaxSize(sourceMaximum);
                    if (sourceMaximum > MaximumImporterSize)
                    {
                        warnings.Add(
                            $"'{path}' is {sourceWidth} x {sourceHeight}; this packer supports imported textures up to {MaximumImporterSize}px.");
                    }

                    if (importer.maxTextureSize < requiredMaxSize)
                    {
                        importer.maxTextureSize = requiredMaxSize;
                        temporaryChange = true;
                    }
                }
                catch (Exception exception)
                {
                    warnings.Add($"Could not read the source resolution for '{path}': {exception.Message}");
                }
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                temporaryChange = true;
            }

            if (importer.crunchedCompression)
            {
                importer.crunchedCompression = false;
                temporaryChange = true;
            }

            if (preserveSourceResolution && importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                temporaryChange = true;
            }

            if (preserveSourceResolution && !importer.ignoreMipmapLimit)
            {
                importer.ignoreMipmapLimit = true;
                temporaryChange = true;
            }

            if (platformSettings.overridden)
            {
                importer.ClearPlatformTextureSettings(platformName);
                temporaryChange = true;
            }

            result.HasTemporaryImportSettings = temporaryChange;
            if (!permanentChange && !temporaryChange)
            {
                return result;
            }

            importer.SaveAndReimport();
            result.Changed = true;
            result.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result.Texture == null)
            {
                throw new InvalidOperationException($"Unity did not reload the prepared data texture '{path}'.");
            }

            return result;
        }

        private static void RestoreTemporaryImportSettings(
            IEnumerable<PreparedTexture> preparedTextures,
            List<string> warnings)
        {
            var failures = new List<Exception>();
            foreach (PreparedTexture prepared in preparedTextures.Where(item => item != null && item.HasTemporaryImportSettings))
            {
                Exception lastFailure = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var importer = AssetImporter.GetAtPath(prepared.AssetPath) as TextureImporter;
                        if (importer == null)
                        {
                            throw new InvalidOperationException($"Unity did not provide a TextureImporter for '{prepared.AssetPath}'.");
                        }

                        importer.textureCompression = prepared.OriginalCompression;
                        importer.crunchedCompression = prepared.OriginalCrunchedCompression;
                        importer.compressionQuality = prepared.OriginalCompressionQuality;
                        importer.maxTextureSize = prepared.OriginalMaxTextureSize;
                        importer.npotScale = prepared.OriginalNpotScale;
                        importer.ignoreMipmapLimit = prepared.OriginalIgnoreMipmapLimit;
                        if (prepared.HadPlatformOverride)
                        {
                            TextureImporterPlatformSettings original = ClonePlatformSettings(prepared.OriginalPlatformSettings);
                            original.name = prepared.PlatformName;
                            original.overridden = true;
                            importer.SetPlatformTextureSettings(original);
                        }
                        else
                        {
                            importer.ClearPlatformTextureSettings(prepared.PlatformName);
                        }

                        importer.SaveAndReimport();
                        prepared.HasTemporaryImportSettings = false;
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
                    string message =
                        $"Could not restore temporary import settings for '{prepared.AssetPath}' after two attempts: {lastFailure.Message}";
                    warnings.Add(message);
                    failures.Add(new InvalidOperationException(message, lastFailure));
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more source textures were packed, but their temporary import settings could not be restored. Source textures were not deleted.",
                    failures);
            }
        }

        private static TextureImporterPlatformSettings ClonePlatformSettings(TextureImporterPlatformSettings source)
        {
            var clone = new TextureImporterPlatformSettings();
            source.CopyTo(clone);
            return clone;
        }

        private static string GetActiveTexturePlatformName()
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (group == BuildTargetGroup.iOS)
            {
                return "iPhone";
            }

            string platformName = NamedBuildTarget.FromBuildTargetGroup(group).TargetName;
            if (string.IsNullOrEmpty(platformName))
            {
                throw new InvalidOperationException($"Unity did not provide a texture platform name for '{group}'.");
            }

            return platformName;
        }

        private static void ResolveOutputSize(
            IEnumerable<Texture2D> inputs,
            int overrideWidth,
            int overrideHeight,
            out int width,
            out int height)
        {
            Texture2D reference = inputs
                .Where(texture => texture != null)
                .OrderByDescending(texture => (long)texture.width * texture.height)
                .FirstOrDefault();
            width = overrideWidth > 0 ? overrideWidth : reference != null ? reference.width : 1024;
            height = overrideHeight > 0 ? overrideHeight : reference != null ? reference.height : 1024;
        }

        private static void ValidateMatchingInputDimensions(IEnumerable<Texture2D> inputs)
        {
            List<Texture2D> assigned = inputs.Where(texture => texture != null).ToList();
            if (assigned.Count <= 1)
            {
                return;
            }

            int width = assigned[0].width;
            int height = assigned[0].height;
            if (assigned.Skip(1).Any(texture => texture.width != width || texture.height != height))
            {
                string dimensions = string.Join(", ", assigned
                    .Select(texture => $"{texture.width}x{texture.height}")
                    .Distinct());
                throw new InvalidOperationException(
                    $"Automatic packing skipped this set because its input dimensions differ ({dimensions}). Resize the source maps to a matching size or pack the set manually.");
            }
        }

        private static void ValidateOutputDoesNotReplaceInput(string outputPath, IEnumerable<Texture2D> inputs)
        {
            foreach (Texture2D input in inputs.Where(texture => texture != null))
            {
                string inputPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(input));
                if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The output path '{outputPath}' is also one of the source textures. Choose a different output name.");
                }
            }
        }

        private static void ValidateOutputSize(int width, int height)
        {
            int maximum = Mathf.Min(MaximumImporterSize, Mathf.Max(1, SystemInfo.maxTextureSize));
            if (width <= 0 || height <= 0 || width > maximum || height > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    $"Output dimensions must be between 1 and {maximum}. Received {width} x {height}.");
            }
        }

        private static void AddDimensionWarnings(
            IEnumerable<Texture2D> inputs,
            int outputWidth,
            int outputHeight,
            List<string> warnings)
        {
            List<Texture2D> assigned = inputs.Where(texture => texture != null).ToList();
            if (assigned.Count <= 1)
            {
                return;
            }

            bool dimensionsDiffer = assigned.Any(texture => texture.width != outputWidth || texture.height != outputHeight);
            if (dimensionsDiffer)
            {
                warnings.Add(
                    $"Input dimensions differ. Maps were resampled to {outputWidth} x {outputHeight} using their current filter settings.");
            }

            float referenceAspect = (float)assigned[0].width / assigned[0].height;
            if (assigned.Skip(1).Any(texture => Mathf.Abs((float)texture.width / texture.height - referenceAspect) > 0.001f))
            {
                warnings.Add("Input aspect ratios differ; resampling may stretch one or more channels.");
            }
        }

        private static void ConfigureOutputImporter(string path, int width, int height, bool compressOutput)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Unity did not create a TextureImporter for '{path}'.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.ignorePngGamma = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.ignoreMipmapLimit = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = RequiredImporterMaxSize(Mathf.Max(width, height));
            importer.textureCompression = compressOutput
                ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.ClearPlatformTextureSettings(GetActiveTexturePlatformName());
            importer.SaveAndReimport();
        }

        private static Shader FindPackingShader()
        {
            Shader shader = Shader.Find(PackingShaderName) ?? AssetDatabase.LoadAssetAtPath<Shader>(PackingShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Mask packing shader was not found. Expected '{PackingShaderName}' at '{PackingShaderPath}'.");
            }

            return shader;
        }

        private static int RequiredImporterMaxSize(int sourceSize)
        {
            return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(32, sourceSize)), 32, MaximumImporterSize);
        }

        private static string EnsurePngFileName(string fileName)
        {
            string safeStem = SanitizeFileStem(fileName);
            return safeStem + ".png";
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Output path resolved outside the Unity project.");
            }

            return absolutePath;
        }

        private static int GetExtensionScore(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".exr": return 30;
                case ".tif":
                case ".tiff": return 25;
                case ".png":
                case ".tga": return 20;
                case ".psd": return 15;
                case ".jpg":
                case ".jpeg": return 5;
                default: return 0;
            }
        }
    }
}
