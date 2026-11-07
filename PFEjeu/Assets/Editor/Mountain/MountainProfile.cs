using System;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    public enum ThresholdMode
    {
        /// <summary>Seuils en metres relatifs au fond de vallee.</summary>
        Absolute,
        /// <summary>Seuils en percentiles de la hauteur des flancs mesuree.</summary>
        Percentile,
    }

    /// <summary>Seuil d'altitude exprimable en metres ou en percentile de la hauteur des flancs.</summary>
    [Serializable]
    public sealed class AltitudeThreshold
    {
        [SerializeField] private float meters;
        [SerializeField, Range(0f, 1f)] private float percentile;

        public AltitudeThreshold(float meters, float percentile)
        {
            this.meters = meters;
            this.percentile = percentile;
        }

        public float Resolve(ThresholdMode mode, float[] sortedFlankHeights)
        {
            if (mode == ThresholdMode.Absolute || sortedFlankHeights == null || sortedFlankHeights.Length == 0) return meters;
            int index = Mathf.Clamp(Mathf.RoundToInt(percentile * (sortedFlankHeights.Length - 1)), 0, sortedFlankHeights.Length - 1);
            return sortedFlankHeights[index];
        }
    }

    /// <summary>
    /// Reglages de la montagne (zonage par altitude, pente, protections, vegetation).
    /// Aucune valeur de design n'est ecrite dans le code des outils.
    /// </summary>
    [CreateAssetMenu(fileName = "MountainProfile", menuName = "Defense/Mountain Profile")]
    public sealed class MountainProfile : ScriptableObject
    {
        public const string AssetPath = "Assets/Editor/Mountain/MountainProfile.asset";
        public const string SnapshotPath = "Assets/Scenes/SceneJeux/Defense/SceneJeux_DefenseTerrain_P1base.asset";

        #region General

        [Header("General")]
        [SerializeField] private int seed = 1337;
        [SerializeField] private float valleyY = 5f;
        [SerializeField] private TerrainData baseSnapshot;
        [SerializeField] private float noiseScaleMeters = 45f;

        #endregion

        #region Altitudes (relatives a la vallee)

        [Header("Altitudes (m relatifs a la vallee, ou percentiles des flancs)")]
        [SerializeField] private ThresholdMode thresholdMode = ThresholdMode.Absolute;
        [Tooltip("Seules les cellules plus hautes que cette valeur comptent comme 'flancs' pour les percentiles.")]
        [SerializeField] private float flankMinRelHeight = 10f;
        [SerializeField] private float percentileRadiusFromHerse = 250f;
        [SerializeField] private AltitudeThreshold treeThinStart = new AltitudeThreshold(10f, 0f);
        [SerializeField] private AltitudeThreshold treeNone = new AltitudeThreshold(40f, 0.62f);
        [SerializeField] private AltitudeThreshold screeStart = new AltitudeThreshold(25f, 0.52f);
        [SerializeField] private AltitudeThreshold cliffStart = new AltitudeThreshold(45f, 0.72f);
        [SerializeField] private AltitudeThreshold summitStart = new AltitudeThreshold(70f, 0.88f);
        [SerializeField] private AltitudeThreshold frostStart = new AltitudeThreshold(90f, 0.97f);

        #endregion

        #region Pentes

        [Header("Pentes (degres)")]
        [SerializeField] private float rockSlope = 30f;
        [SerializeField] private float rockSlopeBlend = 4f;
        [SerializeField] private float slopeNoise = 3f;
        [SerializeField] private float screeSlopeMin = 18f;
        [SerializeField] private float screeSlopeMax = 30f;
        [Tooltip("Les sommets en altitude ne deviennent roche que si la pente depasse cette valeur (sommets plats = herbe).")]
        [SerializeField] private float summitRockMinSlope = 10f;

        #endregion

        #region Peinture

        [Header("Peinture")]
        [SerializeField] private float screeFootRadius = 14f;
        [SerializeField, Range(0f, 1f)] private float mossMaxShare = 0.35f;
        [SerializeField] private float mossAltitudeFalloff = 25f;
        [SerializeField, Range(0f, 1f)] private float frostPatchThreshold = 0.58f;
        [SerializeField] private float frostMaxSlope = 50f;
        [SerializeField, Range(0f, 1f)] private float frostMaxShare = 0.9f;
        [Header("Roche peinte : sombre en bas, pale en haut (selon l'altitude relative a la vallee)")]
        [SerializeField] private float altPaleStartRel = 50f;
        [SerializeField] private float altPaleEndRel = 140f;
        [SerializeField, Range(0f, 1f)] private float altPaleMax = 0.4f;

        [Header("Perspective atmospherique (roche plus pale au loin, via la peinture seulement)")]
        [SerializeField] private float farPaleStart = 250f;
        [SerializeField] private float farPaleEnd = 450f;
        [SerializeField, Range(0f, 1f)] private float farPaleMax = 0.3f;
        [SerializeField] private string rockLayerName = "Rock_Layer";
        [SerializeField] private string screeLayerName = "Rock_Layer 1";
        [SerializeField] private string frostLayerName = "Neige";
        [SerializeField] private string mossLayerName = "Forest_Layer";

        #endregion

        #region Vegetation

        [Header("Vegetation")]
        [SerializeField] private float treeThinRamp = 12f;
        [SerializeField, Range(0f, 1f)] private float treeSparseKeep = 0.35f;
        [SerializeField] private float treeFadeWidth = 10f;
        [SerializeField, Range(0f, 1f)] private float grassKeepHigh = 0.35f;

        [Header("Optimisation : arbres lointains (hors zone de jeu, au-dela de la herse)")]
        [Tooltip("Au-dela de cette distance (m), une partie seulement des arbres est conservee.")]
        [SerializeField] private float outerTreeThinStart = 250f;
        [SerializeField, Range(0f, 1f)] private float outerTreeKeep = 0.6f;
        [Tooltip("Entre ces distances (m) les arbres disparaissent : ils sont derriere les cretes.")]
        [SerializeField] private float outerTreeCullStart = 330f;
        [SerializeField] private float outerTreeCullEnd = 400f;

        [Header("Optimisation : rendu du terrain (composant Terrain de la scene)")]
        [Tooltip("Distance maximale d'affichage des arbres (m). La scene ne depasse pas ~560 m.")]
        [SerializeField] private float terrainTreeDistance = 450f;
        [SerializeField] private bool terrainDrawInstanced = true;

        #endregion

        #region Protections

        [Header("Protections (aucun changement dans ces zones)")]
        [SerializeField] private float laneMargin = 6f;
        [SerializeField] private float laneFade = 6f;
        [SerializeField] private float slotRadius = 9f;
        [SerializeField] private float slotFade = 4f;
        [SerializeField] private float habitedRadius = 22f;
        [SerializeField] private float habitedFade = 8f;
        [Tooltip("Dessus du plateau seulement : la face de falaise (au-dela du rayon plat) doit pouvoir etre peinte.")]
        [SerializeField] private float plateauRadius = 38f;
        [SerializeField] private float plateauFade = 3f;
        [SerializeField] private string[] habitedObjectNames = { "herse", "Prefabs_Barracks", "NPC_Capitaine", "Player" };
        [SerializeField] private string habitedChildrenRoot = "VillageBuildings";

        #endregion

        public int Seed => seed;
        public float ValleyY => valleyY;
        public TerrainData BaseSnapshot => baseSnapshot;
        public float NoiseScaleMeters => noiseScaleMeters;
        public ThresholdMode Mode => thresholdMode;
        public float FlankMinRelHeight => flankMinRelHeight;
        public float PercentileRadiusFromHerse => percentileRadiusFromHerse;
        public AltitudeThreshold TreeThinStart => treeThinStart;
        public AltitudeThreshold TreeNone => treeNone;
        public AltitudeThreshold ScreeStart => screeStart;
        public AltitudeThreshold CliffStart => cliffStart;
        public AltitudeThreshold SummitStart => summitStart;
        public AltitudeThreshold FrostStart => frostStart;
        public float RockSlope => rockSlope;
        public float RockSlopeBlend => rockSlopeBlend;
        public float SlopeNoise => slopeNoise;
        public float ScreeSlopeMin => screeSlopeMin;
        public float ScreeSlopeMax => screeSlopeMax;
        public float SummitRockMinSlope => summitRockMinSlope;
        public float ScreeFootRadius => screeFootRadius;
        public float MossMaxShare => mossMaxShare;
        public float MossAltitudeFalloff => mossAltitudeFalloff;
        public float FrostPatchThreshold => frostPatchThreshold;
        public float FrostMaxSlope => frostMaxSlope;
        public float FrostMaxShare => frostMaxShare;
        public float AltPaleStartRel => altPaleStartRel;
        public float AltPaleEndRel => altPaleEndRel;
        public float AltPaleMax => altPaleMax;
        public float FarPaleStart => farPaleStart;
        public float FarPaleEnd => farPaleEnd;
        public float FarPaleMax => farPaleMax;
        public string RockLayerName => rockLayerName;
        public string ScreeLayerName => screeLayerName;
        public string FrostLayerName => frostLayerName;
        public string MossLayerName => mossLayerName;
        public float TreeThinRamp => treeThinRamp;
        public float TreeSparseKeep => treeSparseKeep;
        public float TreeFadeWidth => treeFadeWidth;
        public float GrassKeepHigh => grassKeepHigh;
        public float OuterTreeThinStart => outerTreeThinStart;
        public float OuterTreeKeep => outerTreeKeep;
        public float OuterTreeCullStart => outerTreeCullStart;
        public float OuterTreeCullEnd => outerTreeCullEnd;
        public float TerrainTreeDistance => terrainTreeDistance;
        public bool TerrainDrawInstanced => terrainDrawInstanced;
        public float LaneMargin => laneMargin;
        public float LaneFade => laneFade;
        public float SlotRadius => slotRadius;
        public float SlotFade => slotFade;
        public float HabitedRadius => habitedRadius;
        public float HabitedFade => habitedFade;
        public float PlateauRadius => plateauRadius;
        public float PlateauFade => plateauFade;
        public string[] HabitedObjectNames => habitedObjectNames;
        public string HabitedChildrenRoot => habitedChildrenRoot;

        public static MountainProfile LoadOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MountainProfile>(AssetPath);
            if (profile != null) return profile;
            profile = CreateInstance<MountainProfile>();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public void SetBaseSnapshot(TerrainData snapshot)
        {
            baseSnapshot = snapshot;
            EditorUtility.SetDirty(this);
        }
    }
}
