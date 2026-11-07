using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Copies "froides" des materiaux de roche, declinees en paliers d'altitude : sombre et moussue en bas,
    /// claire et neutre en haut. Les materiaux d'origine ne sont jamais modifies (hors instancing du kit) ; les copies
    /// sont assignees aux instances de scene. Des paliers (et non des proprietes par rocher) pour conserver le batching.
    /// Les materiaux du kit (URP/Lit, _BaseColor) ont en plus quelques variantes de teinte tirees par instance.
    /// </summary>
    public static class MountainRockMaterials
    {
        private const string TintProperty = "_Color";
        private const string MossProperty = "_Coverage_Color";
        private const string KitTintProperty = "_BaseColor";
        private const string KitFolder = "Assets/Arts/";
        private const float TintReference = 0.7f;

        private const float WarmthCorrection = 0.7f;
        private static readonly Color[] KitVariantTints =
        {
            new Color(1.00f, 1.00f, 1.00f), // neutre
            new Color(0.90f, 0.96f, 1.06f), // plus froide et sombre
            new Color(1.06f, 1.03f, 0.92f), // plus chaude, poussiereuse
        };

        /// <summary>Materiaux d'origine -> copies par palier d'altitude (index 0 = le plus bas) et variante.</summary>
        public sealed class Set
        {
            public readonly Dictionary<Material, Material[]> byOriginal = new Dictionary<Material, Material[]>();
            public float valleyY;
            public float darkRel;
            public float lightRel;
            public int buckets;

            public int Bucket(float worldY)
            {
                float t = Mathf.Clamp01((worldY - valleyY - darkRel) / Mathf.Max(1f, lightRel - darkRel));
                return Mathf.Clamp(Mathf.RoundToInt(t * (buckets - 1)), 0, buckets - 1);
            }
        }

        public static Set Build(MountainRockProfile profile, MountainRockCatalog catalog)
        {
            var set = new Set
            {
                valleyY = profile.ValleyY,
                darkRel = profile.TintDarkRel,
                lightRel = profile.TintLightRel,
                buckets = profile.AltitudeBuckets,
            };
            if (!profile.UseColdMaterials) return set;

            EnsureFolder(profile.ColdMaterialFolder);
            var kitMaterials = new List<Material>();
            foreach (RockSize size in (RockSize[])System.Enum.GetValues(typeof(RockSize)))
            {
                foreach (RockAsset asset in catalog.Get(size))
                {
                    foreach (Renderer renderer in asset.Prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (Material original in renderer.sharedMaterials)
                        {
                            if (original == null || set.byOriginal.ContainsKey(original) || kitMaterials.Contains(original)) continue;
                            if (IsKit(original)) kitMaterials.Add(original);
                            else if (original.HasProperty(TintProperty)) set.byOriginal[original] = BuildBuckets(profile, original);
                        }
                    }
                }
            }

            // Les materiaux du kit sont cales sur l'albedo de la couche de roche du terrain (meme famille de shading URP).
            float[] reference = WallLuminanceByBucket(profile);
            foreach (Material original in kitMaterials) set.byOriginal[original] = BuildKitVariants(profile, original, reference);
            return set;
        }

        /// <summary>variantKey : entier stable par instance, qui choisit la teinte parmi les variantes du kit.</summary>
        public static void Apply(GameObject instance, Set set, float worldY, int variantKey)
        {
            if (set == null || set.byOriginal.Count == 0) return;
            int bucket = set.Bucket(worldY);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !set.byOriginal.TryGetValue(materials[i], out Material[] variants)) continue;
                    int perBucket = variants.Length / set.buckets;
                    int index = bucket * perBucket + (perBucket > 1 ? Mathf.Abs(variantKey) % perBucket : 0);
                    materials[i] = variants[index];
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static bool IsKit(Material material) =>
            material.HasProperty(KitTintProperty) && AssetDatabase.GetAssetPath(material).StartsWith(KitFolder, System.StringComparison.Ordinal);

        private const float DefaultWallLuminance = 0.28f;
        private const float WallLuminanceGainTop = 1.3f;

        /// <summary>Albedo moyen de la couche de roche du terrain, par palier (un peu plus pale en altitude).</summary>
        private static float[] WallLuminanceByBucket(MountainRockProfile profile)
        {
            float wall = DefaultWallLuminance;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                string layerName = MountainProfile.LoadOrCreate().RockLayerName;
                foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                {
                    if (layer != null && layer.name == layerName && layer.diffuseTexture != null)
                    {
                        wall = MountainTextureStats.Luminance(MountainTextureStats.Mean(layer.diffuseTexture));
                        break;
                    }
                }
            }
            var result = new float[profile.AltitudeBuckets];
            for (int i = 0; i < result.Length; i++)
                result[i] = wall * Mathf.Lerp(1f, WallLuminanceGainTop, result.Length > 1 ? i / (float)(result.Length - 1) : 0f);
            return result;
        }

        /// <summary>Luminosite effective (texture moyenne x teinte) moyenne des materiaux du pack, par palier d'altitude.</summary>
        private static float[] PackLuminanceByBucket(Set set)
        {
            var sum = new float[set.buckets];
            int materials = 0;
            foreach (KeyValuePair<Material, Material[]> pair in set.byOriginal)
            {
                float texture = MountainTextureStats.Luminance(MountainTextureStats.Mean(MountainTextureStats.MainTexture(pair.Key)));
                for (int i = 0; i < set.buckets && i < pair.Value.Length; i++)
                    sum[i] += texture * MountainTextureStats.Luminance(pair.Value[i].GetColor(TintProperty));
                materials++;
            }
            for (int i = 0; i < sum.Length; i++) sum[i] = materials > 0 ? sum[i] / materials : 0.5f;
            return sum;
        }

        private static Material[] BuildKitVariants(MountainRockProfile profile, Material original, float[] referenceLuminance)
        {
            Color textureMean = MountainTextureStats.Mean(MountainTextureStats.MainTexture(original));
            float textureLum = Mathf.Max(0.02f, MountainTextureStats.Luminance(textureMean));
            // Dominante chaude des atlas du kit : neutralisee en partie (exposant < 1) pour rester proche du pack froid.
            var warmth = new Color(textureMean.r / textureLum, textureMean.g / textureLum, textureMean.b / textureLum, 1f);
            var correction = new Color(Mathf.Pow(1f / Mathf.Max(0.2f, warmth.r), WarmthCorrection), Mathf.Pow(1f / Mathf.Max(0.2f, warmth.g), WarmthCorrection), Mathf.Pow(1f / Mathf.Max(0.2f, warmth.b), WarmthCorrection), 1f);

            // Le materiau du kit doit etre instancie pour rester batche (certains kits l'ont desactive).
            if (!original.enableInstancing)
            {
                original.enableInstancing = true;
                EditorUtility.SetDirty(original);
            }

            int count = profile.AltitudeBuckets;
            var result = new Material[count * KitVariantTints.Length];
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                Color cold = Normalized(Color.Lerp(profile.ColdTint, profile.TopTint, t));
                // Gain qui amene la luminosite effective du kit a celle du pack au meme palier.
                float gain = referenceLuminance[i] * profile.KitLuminanceMatch / textureLum;
                for (int v = 0; v < KitVariantTints.Length; v++)
                {
                    Color tint = Multiply(Multiply(cold, correction), KitVariantTints[v]) * gain;
                    tint.a = 1f;
                    string path = profile.ColdMaterialFolder + "/" + original.name + "_Kit_A" + i + "_V" + v + ".mat";
                    var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                    Material material = existing != null ? existing : new Material(original) { name = original.name + "_Kit_A" + i + "_V" + v };
                    material.CopyPropertiesFromMaterial(original);
                    material.enableInstancing = true;
                    material.SetColor(KitTintProperty, tint);
                    MakeMatte(material);
                    if (existing == null) AssetDatabase.CreateAsset(material, path);
                    else EditorUtility.SetDirty(material);
                    result[i * KitVariantTints.Length + v] = material;
                }
            }
            return result;
        }

        /// <summary>Roche seche : aucun reflet ni highlight speculaire (la lune ne doit pas les faire briller).</summary>
        private static void MakeMatte(Material material)
        {
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            // Normal map retiree : sa convention de tangentes donnait des bandes brillantes une fois les OBJ convertis.
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        private static Color Normalized(Color c)
        {
            float average = Mathf.Max(0.01f, (c.r + c.g + c.b) / 3f);
            return new Color(c.r / average, c.g / average, c.b / average, 1f);
        }

        private static Color Multiply(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        private static Material[] BuildBuckets(MountainRockProfile profile, Material original)
        {
            int count = profile.AltitudeBuckets;
            var result = new Material[count];
            Color baseTint = original.HasProperty(TintProperty) ? original.GetColor(TintProperty) : Color.white;
            float luminance = Mathf.Max(0.05f, (baseTint.r + baseTint.g + baseTint.b) / 3f);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float brightness = Mathf.Lerp(profile.DarkBrightness, profile.LightBrightness, t);
                Color tint = Color.Lerp(profile.ColdTint, profile.TopTint, t);
                Color rock = new Color(luminance * tint.r / TintReference, luminance * tint.g / TintReference, luminance * tint.b / TintReference, 1f) * brightness;
                rock.a = 1f;
                // La mousse s'efface avec l'altitude : sa couleur converge vers celle de la roche.
                Color moss = Color.Lerp(profile.MossColor * Mathf.Lerp(1f, 1.15f, t), rock, Mathf.Pow(t, 0.7f));
                moss.a = 1f;
                result[i] = LoadOrCreate(profile, original, i, rock, moss);
            }
            return result;
        }

        private static Material LoadOrCreate(MountainRockProfile profile, Material original, int bucket, Color rock, Color moss)
        {
            string path = profile.ColdMaterialFolder + "/" + original.name + "_Cold_A" + bucket + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material material = existing != null ? existing : new Material(original) { name = original.name + "_Cold_A" + bucket };

            if (material.HasProperty(TintProperty)) material.SetColor(TintProperty, rock);
            if (material.HasProperty(MossProperty)) material.SetColor(MossProperty, moss);

            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
