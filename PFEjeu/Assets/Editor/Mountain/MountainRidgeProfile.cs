using System;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Cretes lointaines (P4) : lignes de crete decoupees dans la zone exterieure (> minDistance de la herse).
    /// Plus la crete est loin, plus elle est haute. Inactif tant que le menu P4 ne l'active pas.
    /// </summary>
    [CreateAssetMenu(fileName = "MountainRidgeProfile", menuName = "Defense/Mountain Ridge Profile")]
    public sealed class MountainRidgeProfile : ScriptableObject
    {
        public const string AssetPath = "Assets/Editor/Mountain/MountainRidgeProfile.asset";

        [Serializable]
        public sealed class Ridge
        {
            [Tooltip("Distance de la crete a la herse (m).")]
            public float radius = 320f;
            [Tooltip("Demi-largeur de la crete (m).")]
            public float halfWidth = 70f;
            [Tooltip("Hauteur maximale au-dessus du fond de vallee (m).")]
            public float height = 55f;
            [Tooltip("Ondulation du rayon de la crete (m) : silhouette irreguliere.")]
            public float wobble = 28f;
        }

        [Header("General")]
        [SerializeField] private bool enabled;
        [SerializeField] private int seed = 4242;
        [SerializeField] private float minDistanceFromHerse = 250f;
        [SerializeField] private float rampInWidth = 30f;
        [SerializeField] private float borderFadeWidth = 40f;

        [Header("Cretes (de la plus proche a la plus lointaine)")]
        [SerializeField] private Ridge[] ridges =
        {
            new Ridge { radius = 320f, halfWidth = 70f, height = 55f, wobble = 28f },
            new Ridge { radius = 385f, halfWidth = 75f, height = 80f, wobble = 34f },
            new Ridge { radius = 440f, halfWidth = 80f, height = 110f, wobble = 40f },
        };

        [Header("Silhouette")]
        [SerializeField] private float angularFrequency = 2.2f;
        [SerializeField] private float peakFrequency = 7.5f;
        [SerializeField, Range(0f, 1f)] private float peakContrast = 0.45f;
        [SerializeField] private float detailScaleMeters = 40f;
        [SerializeField] private float crestSharpness = 1.4f;

        [Header("Relief de crete (aretes, ravines, cols)")]
        [Tooltip("Echelles (m) du bruit d'aretes : grandes formes puis detail fin.")]
        [SerializeField] private float ridgedScaleBig = 70f;
        [SerializeField] private float ridgedScaleSmall = 26f;
        [SerializeField, Range(0f, 1f)] private float ridgedStrength = 0.55f;
        [SerializeField] private float gullyScale = 38f;
        [SerializeField, Range(0f, 0.6f)] private float gullyStrength = 0.3f;
        [Tooltip("Cols : zones ou la crete s'abaisse, ce qui ouvre l'anneau et laisse voir les cretes plus lointaines.")]
        [SerializeField] private float passFrequency = 1.4f;
        [SerializeField, Range(0f, 1f)] private float passLow = 0.28f;
        [SerializeField, Range(0f, 1f)] private float passHigh = 0.48f;
        [SerializeField, Range(0f, 1f)] private float passMinHeight = 0.45f;
        [SerializeField] private float maxHeightFactor = 1.6f;

        [Header("Relief des montagnes existantes (aretes, ravines, strates) au-dessus de la zone jouable")]
        [SerializeField] private bool massifDetailEnabled = true;
        [Tooltip("Altitude relative a la vallee (m) sous laquelle le terrain n'est jamais modifie (volume NavMesh : +37 m).")]
        [SerializeField] private float massifStartRel = 45f;
        [SerializeField] private float massifFullRel = 65f;
        [SerializeField] private float massifBigScale = 70f;
        [SerializeField] private float massifBigAmplitude = 14f;
        [SerializeField] private float massifMidScale = 28f;
        [SerializeField] private float massifMidAmplitude = 6f;
        [SerializeField] private float massifFineScale = 11f;
        [SerializeField] private float massifFineAmplitude = 1.8f;
        [Tooltip("Deformation du domaine (m) : evite les motifs reguliers.")]
        [SerializeField] private float massifWarp = 18f;
        [SerializeField] private float strataHeight = 3.5f;
        [SerializeField, Range(0f, 1f)] private float strataStrength = 0.22f;
        [SerializeField] private float strataStartRel = 55f;
        [SerializeField] private float strataFullRel = 85f;

        [Header("Crags : surfaces rocheuses brisees (passe sur la heightmap, selon la pente)")]
        [SerializeField] private bool cragEnabled = true;
        [Tooltip("Meme garde-fou NavMesh : rien n'est modifie sous cette altitude relative (m).")]
        [SerializeField] private float cragStartRel = 45f;
        [Tooltip("Sous cragStartRel, seules les parois TRES raides (infranchissables) sont brisees : pas de risque pour le NavMesh.")]
        [SerializeField] private float cragSteepMinRel = 10f;
        [Tooltip("Grandes formes de paroi (contreforts, dalles, encoches) sur les parois tres raides.")]
        [SerializeField] private float cragWallScale = 32f;
        [SerializeField] private float cragWallAmplitude = 6f;
        [SerializeField, Range(0f, 1f)] private float ledgeStrengthOnWalls = 0.85f;
        [SerializeField] private float cragSteepSlopeLow = 52f;
        [SerializeField] private float cragSteepSlopeHigh = 62f;
        [SerializeField] private float cragFullRel = 62f;
        [SerializeField] private float cragSlopeLow = 14f;
        [SerializeField] private float cragSlopeHigh = 38f;
        [SerializeField] private float cragLargeScale = 13f;
        [SerializeField] private float cragLargeAmplitude = 3.4f;
        [SerializeField] private float cragFineScale = 5f;
        [SerializeField] private float cragFineAmplitude = 1.5f;
        [SerializeField] private float cragWarp = 7f;
        [SerializeField] private float ledgeHeight = 2.4f;
        [SerializeField, Range(0f, 1f)] private float ledgeStrength = 0.5f;
        [Tooltip("Part minimale de l'effet sur pente douce (0 = crags seulement sur les pentes raides).")]
        [SerializeField, Range(0f, 1f)] private float cragGentleShare = 0.25f;

        [Header("Prolongement du massif vers Spawn_Route_04")]
        [Tooltip("Azimut (0 = nord, 90 = est) depuis la herse : direction de Spawn_Route_04.")]
        [SerializeField] private float sectorAzimuth = 33f;
        [SerializeField] private float sectorWidth = 45f;
        [SerializeField] private float sectorBoost = 1.25f;

        public bool Enabled => enabled;
        public int Seed => seed;
        public float MinDistanceFromHerse => minDistanceFromHerse;
        public float RampInWidth => rampInWidth;
        public float BorderFadeWidth => borderFadeWidth;
        public Ridge[] Ridges => ridges;
        public float AngularFrequency => angularFrequency;
        public float PeakFrequency => peakFrequency;
        public float PeakContrast => peakContrast;
        public float DetailScaleMeters => detailScaleMeters;
        public float CrestSharpness => crestSharpness;
        public bool CragEnabled => cragEnabled;
        public float CragStartRel => cragStartRel;
        public float CragSteepMinRel => cragSteepMinRel;
        public float CragWallScale => cragWallScale;
        public float CragWallAmplitude => cragWallAmplitude;
        public float LedgeStrengthOnWalls => ledgeStrengthOnWalls;
        public float CragSteepSlopeLow => cragSteepSlopeLow;
        public float CragSteepSlopeHigh => cragSteepSlopeHigh;
        public float CragFullRel => cragFullRel;
        public float CragSlopeLow => cragSlopeLow;
        public float CragSlopeHigh => cragSlopeHigh;
        public float CragLargeScale => cragLargeScale;
        public float CragLargeAmplitude => cragLargeAmplitude;
        public float CragFineScale => cragFineScale;
        public float CragFineAmplitude => cragFineAmplitude;
        public float CragWarp => cragWarp;
        public float LedgeHeight => ledgeHeight;
        public float LedgeStrength => ledgeStrength;
        public float CragGentleShare => cragGentleShare;
        public bool MassifDetailEnabled => massifDetailEnabled;
        public float MassifStartRel => massifStartRel;
        public float MassifFullRel => massifFullRel;
        public float MassifBigScale => massifBigScale;
        public float MassifBigAmplitude => massifBigAmplitude;
        public float MassifMidScale => massifMidScale;
        public float MassifMidAmplitude => massifMidAmplitude;
        public float MassifFineScale => massifFineScale;
        public float MassifFineAmplitude => massifFineAmplitude;
        public float MassifWarp => massifWarp;
        public float StrataHeight => strataHeight;
        public float StrataStrength => strataStrength;
        public float StrataStartRel => strataStartRel;
        public float StrataFullRel => strataFullRel;
        public float RidgedScaleBig => ridgedScaleBig;
        public float RidgedScaleSmall => ridgedScaleSmall;
        public float RidgedStrength => ridgedStrength;
        public float GullyScale => gullyScale;
        public float GullyStrength => gullyStrength;
        public float PassFrequency => passFrequency;
        public float PassLow => passLow;
        public float PassHigh => passHigh;
        public float PassMinHeight => passMinHeight;
        public float MaxHeightFactor => maxHeightFactor;
        public float SectorAzimuth => sectorAzimuth;
        public float SectorWidth => sectorWidth;
        public float SectorBoost => sectorBoost;

        public static MountainRidgeProfile LoadOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MountainRidgeProfile>(AssetPath);
            if (profile != null) return profile;
            profile = CreateInstance<MountainRidgeProfile>();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
            EditorUtility.SetDirty(this);
        }
    }
}
