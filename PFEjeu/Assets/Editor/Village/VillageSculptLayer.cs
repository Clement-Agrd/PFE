using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Couche "village" de l'outil de sculpt du relief (qui reste l'unique ecrivain de la heightmap) :
    /// 1. restitue le terrain sculpte a la main (ecart enregistre), 2. applique les etages du village.
    /// Sans ecart enregistre et avec les etages desactives, le sculpt est strictement celui d'origine.
    /// </summary>
    [InitializeOnLoad]
    internal static class VillageSculptLayer
    {
        /// <summary>Variation minimale (m) pour qu'un echantillon soit reecrit.</summary>
        private const float MinChange = 0.0005f;

        static VillageSculptLayer()
        {
            DefenseReliefSculptTool.LateLayers.Remove(Apply);
            DefenseReliefSculptTool.LateLayers.Add(Apply);
        }

        private static void Apply(float[,] heights, Vector3 origin, Vector3 size)
        {
            bool restored = VillageManualDelta.Apply(heights);

            var profile = AssetDatabase.LoadAssetAtPath<VillageLayoutProfile>(VillageLayoutProfile.AssetPath);
            if (profile == null || !profile.TerracesEnabled) return;
            if (!restored)
            {
                Debug.LogError("[VillageLayout] Ecart manuel absent : etages du village NON appliques. " +
                               "Lancer d'abord Tools/Defense/Relief/Village/0 - Capturer l'ecart manuel.");
                return;
            }

            // Copie : les murs et rampes sont traces sur le terrain d'avant les etages, que la boucle va reecrire.
            var underlying = new VillageHeightSampler((float[,])heights.Clone(), origin, size);
            var field = new VillageTerraceField(profile, DefenseReliefScene.WallAnchors(), underlying);
            ApplyField(field, heights, origin, size);
        }

        private static void ApplyField(VillageTerraceField field, float[,] heights, Vector3 origin, Vector3 size)
        {
            int res = heights.GetLength(0);
            float stepX = size.x / (res - 1);
            float stepZ = size.z / (res - 1);
            Rect bounds = field.WorldBounds;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((bounds.xMin - origin.x) / stepX));
            int i1 = Mathf.Min(res - 1, Mathf.CeilToInt((bounds.xMax - origin.x) / stepX));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((bounds.yMin - origin.z) / stepZ));
            int j1 = Mathf.Min(res - 1, Mathf.CeilToInt((bounds.yMax - origin.z) / stepZ));

            for (int j = j0; j <= j1; j++)
            {
                float z = origin.z + j * stepZ;
                for (int i = i0; i <= i1; i++)
                {
                    float x = origin.x + i * stepX;
                    float underlying = heights[j, i] * size.y + origin.y;
                    float y = field.Evaluate(x, z, underlying);
                    // Echantillon inchange : on ne reecrit pas, pour rester identique au bit pres hors du village.
                    if (Mathf.Abs(y - underlying) < MinChange) continue;
                    heights[j, i] = Mathf.Clamp01((y - origin.y) / size.y);
                }
            }
        }
    }
}
