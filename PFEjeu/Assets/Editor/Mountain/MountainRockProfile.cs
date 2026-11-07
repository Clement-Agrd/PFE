using System;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Reglages du placement des rochers (P3) : assets, densites par zone, exclusions, reperes.
    /// Les rochers generes vivent sous "Environment_Rocks" ; les rochers "hero" sous une racine separee.
    /// </summary>
    [CreateAssetMenu(fileName = "MountainRockProfile", menuName = "Defense/Mountain Rock Profile")]
    public sealed class MountainRockProfile : ScriptableObject
    {
        public const string AssetPath = "Assets/Editor/Mountain/MountainRockProfile.asset";

        [Serializable]
        public sealed class Landmark
        {
            public string label;
            [Tooltip("Position XZ indicative (a valider sur le terrain).")]
            public Vector2 position;
            [Tooltip("Decalage minimal depuis le centre de lane le plus proche (m).")]
            public float laneOffset = 10f;
        }

        #region Assets

        [Header("Assets (prefabs existants, jamais modifies)")]
        [SerializeField] private string prefabFolder = "Assets/PackagesStore/Idyllic Fantasy Nature/Prefabs/";
        [SerializeField] private string[] cliffNames = { "Cliff_01", "Cliff_02", "Cliff_03", "Cliff_04", "Cliff_05" };
        [SerializeField] private string[] bigNames = { "Rock_Big_01", "Rock_Big_02", "Rock_Big_03" };
        [SerializeField] private string[] mediumNames = { "Rock_Medium_01", "Rock_Medium_02", "Rock_Medium_03" };
        [SerializeField] private string[] smallNames = { "Rock_Small_01", "Rock_Small_02", "Rock_Small_03" };

        [Header("Kit de roches (chemins complets de prefabs, ajoutes aux categories du pack)")]
        [SerializeField] private string[] kitPanelPaths =
        {
            "Assets/Arts/3D/CliffPanKit/Assets/CliffPan/Prefabs/CliffPan_A.prefab",
            "Assets/Arts/3D/CliffPanKit/Assets/CliffPan/Prefabs/CliffPan_B.prefab",
            "Assets/Arts/3D/CliffPanKit/Assets/CliffPan/Prefabs/CliffPan_C.prefab",
        };
        [SerializeField] private string[] kitMediumPaths =
        {
            "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Prefabs/RockBlock_A.prefab",
            "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Prefabs/RockBlock_B.prefab",
            "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Prefabs/RockBlock_C.prefab",
            "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Prefabs/RockBlock_D.prefab",
            "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Prefabs/RockBlock_E.prefab",
        };
        [SerializeField] private string[] kitSmallPaths =
        {
            "Assets/Arts/3D_Rocks/SmallRocksKit/SmallRocksKit/Assets/SmallRocks/Prefabs/SmallRock_A.prefab",
            "Assets/Arts/3D_Rocks/SmallRocksKit/SmallRocksKit/Assets/SmallRocks/Prefabs/SmallRock_B.prefab",
            "Assets/Arts/3D_Rocks/SmallRocksKit/SmallRocksKit/Assets/SmallRocks/Prefabs/SmallRock_C.prefab",
            "Assets/Arts/3D_Rocks/SmallRocksKit/SmallRocksKit/Assets/SmallRocks/Prefabs/SmallRock_D.prefab",
        };
        [SerializeField] private string[] kitScreePaths =
        {
            "Assets/Arts/3D/ScreePileKit/Assets/ScreePile/Prefabs/ScreePile_A.prefab",
            "Assets/Arts/3D/ScreePileKit/Assets/ScreePile/Prefabs/ScreePile_B.prefab",
        };
        [SerializeField] private string[] kitSlabPaths =
        {
            "Assets/Arts/3D_Rocks/RockSlabKit/RockSlabKit/Assets/RockSlab/Prefabs/RockSlab_A.prefab",
            "Assets/Arts/3D_Rocks/RockSlabKit/RockSlabKit/Assets/RockSlab/Prefabs/RockSlab_B.prefab",
        };
        [SerializeField] private string[] kitLandmarkPaths =
        {
            "Assets/Arts/3D_Rocks/RockLandmarkKit/RockLandmarkKit/Assets/RockLandmark/Prefabs/RockLandmark_JunctionA.prefab",
            "Assets/Arts/3D_Rocks/RuinKit/RuinKit/Assets/Ruin/Prefabs/Ruin_Main.prefab",
            "Assets/Arts/3D_Rocks/RuinKit/RuinKit/Assets/Ruin/Prefabs/Ruin_Arch.prefab",
            "Assets/Arts/3D_Rocks/RuinKit/RuinKit/Assets/Ruin/Prefabs/Ruin_Pillar.prefab",
            "Assets/Arts/3D_Rocks/RuinKit/RuinKit/Assets/Ruin/Prefabs/Ruin_Wall.prefab",
        };
        [Header("Variation du kit")]
        [SerializeField] private float kitMediumScale = 2.2f;
        [SerializeField] private float kitSmallScale = 2.4f;
        [SerializeField] private float kitScreeScale = 1.4f;
        [SerializeField] private float kitPanelScale = 1f;
        [Tooltip("Etirement aleatoire par axe (x,z) des blocs du kit : 0,25 = de 0,75 a 1,25.")]
        [SerializeField, Range(0f, 0.5f)] private float kitStretchXZ = 0.3f;
        [SerializeField, Range(0f, 0.5f)] private float kitStretchY = 0.25f;
        [Tooltip("Luminosite du kit rapportee a celle du pack (le shader du pack eclaire plus sombre que URP/Lit).")]
        [SerializeField, Range(0.1f, 1.5f)] private float kitLuminanceMatch = 0.4f;
        [SerializeField, Range(0f, 0.7f)] private float kitScaleVariation = 0.45f;
        [Header("Variation commune (pack et kit)")]
        [SerializeField, Range(0f, 1f)] private float mirrorChance = 0.5f;
        [SerializeField, Range(0f, 20f)] private float tiltDegrees = 9f;
        [Header("Montagnes lointaines : les rochers grossissent avec la distance a la herse")]
        [SerializeField] private float farScaleStart = 200f;
        [SerializeField] private float farScaleEnd = 430f;
        [SerializeField, Range(1f, 6f)] private float farScaleMax = 3.2f;
        [Header("Part du kit dans le tirage")]
        [SerializeField, Range(0f, 1f)] private float wallPanelShare = 0.7f;
        [SerializeField, Range(0f, 1f)] private float screeAnchorShare = 0.4f;

        #endregion

        #region General

        [Header("General")]
        [SerializeField] private int seed = 20241105;
        [SerializeField] private string rootName = "Environment_Rocks";
        [SerializeField] private string heroRootName = "Environment_Rocks_Hero";
        [SerializeField] private float maxRadiusFromHerse = 230f;
        [SerializeField] private float gridSpacing = 10f;
        [SerializeField] private int maxRocks = 700;
        [SerializeField] private float densityMultiplier = 1f;

        #endregion

        #region Grappes et pose

        [Header("Grappes et pose")]
        [SerializeField, Range(0f, 1f)] private float fiveRockChance = 0.4f;
        [SerializeField, Range(0f, 1f)] private float burialMin = 0.35f;
        [SerializeField, Range(0f, 1f)] private float burialMax = 0.5f;
        [SerializeField, Range(0f, 0.5f)] private float scaleVariation = 0.25f;
        [SerializeField, Range(0f, 1f)] private float alignToNormal = 0.85f;
        [SerializeField] private float overlapPadding = 0.4f;
        [Header("Habillage des parois tres raides (formations de falaise serrees, qui masquent la texture etiree)")]
        [SerializeField] private float wallMinSlope = 55f;
        [SerializeField] private float wallMinRel = 12f;
        [SerializeField] private float wallSpacing = 8f;
        [SerializeField] private int wallMaxRocks = 260;
        [SerializeField] private float wallMaxRadius = 260f;
        [SerializeField] private float wallScaleMin = 0.9f;
        [SerializeField] private float wallScaleMax = 1.5f;
        [Tooltip("Part du rayon utilisee pour l'espacement : < 1 = les formations se recouvrent.")]
        [SerializeField, Range(0.3f, 1f)] private float wallOverlap = 0.6f;

        [Header("Teinte selon l'altitude (relative a la vallee) : sombre en bas, claire en haut")]
        [SerializeField] private float tintDarkRel = 10f;
        [SerializeField] private float tintLightRel = 130f;
        [SerializeField] private float darkBrightness = 0.55f;
        [SerializeField] private float lightBrightness = 1.25f;
        [SerializeField, Range(2, 10)] private int altitudeBuckets = 6;
        [Tooltip("Teinte de la roche au plus haut (plus neutre et pale que la teinte froide du bas).")]
        [SerializeField] private Color topTint = new Color(0.92f, 0.95f, 1f, 1f);
        [SerializeField] private float valleyY = 5f;

        [Header("Materiaux froids (copies des materiaux d'origine, appliquees aux instances de scene seulement)")]
        [SerializeField] private bool useColdMaterials = true;
        [SerializeField] private string coldMaterialFolder = "Assets/Materials/Mountain";
        [SerializeField] private Color coldTint = new Color(0.58f, 0.64f, 0.76f, 1f);
        [Tooltip("Mousse sombre et desaturee (l'original est un jaune-vert vif).")]
        [SerializeField] private Color mossColor = new Color(0.22f, 0.32f, 0.27f, 1f);


        #endregion

        #region Densites (grappes par cellule de grille)

        [Header("Densite continue (apparition progressive, sans ceinture ni palier)")]
        [Tooltip("Probabilite de grappe par cellule selon l'altitude relative a la vallee (axe X en m). Courbe continue : pas de limite nette.")]
        [SerializeField] private AnimationCurve densityByAltitude = new AnimationCurve(
            new Keyframe(10f, 0.01f), new Keyframe(25f, 0.05f), new Keyframe(40f, 0.11f),
            new Keyframe(60f, 0.17f), new Keyframe(90f, 0.2f), new Keyframe(130f, 0.16f), new Keyframe(180f, 0.12f));
        [SerializeField, Range(0f, 1f)] private float patchMin = 0.65f;
        [SerializeField, Range(1f, 2f)] private float patchMax = 1.35f;
        [SerializeField] private float nearPlateauBoost = 1.1f;
        [SerializeField] private float farPlateauFactor = 0.9f;

        [Header("Taille progressive avec l'altitude (petits en bas, formations en haut)")]
        [SerializeField] private float altScaleMin = 0.8f;
        [SerializeField] private float altScaleMax = 1.5f;
        [SerializeField] private float altScaleStartRel = 20f;
        [SerializeField] private float altScaleEndRel = 120f;
        [SerializeField] private float cliffAnchorStartRel = 40f;
        [SerializeField] private float cliffAnchorFullRel = 95f;
        [SerializeField, Range(0f, 1f)] private float cliffAnchorMax = 0.85f;
        [SerializeField] private float bigAnchorStartRel = 12f;
        [SerializeField] private float bigAnchorFullRel = 50f;
        [Tooltip("Enfoncement supplementaire sur les pentes raides (0 a 0,3) : la roche est ancree dans la pente.")]
        [SerializeField, Range(0f, 0.3f)] private float slopeBurialBonus = 0.12f;
        [SerializeField] private float cliffZoneMinSlope = 35f;
        [SerializeField] private float patchNoiseScale = 40f;

        #endregion

        #region Exclusions

        [Header("Exclusions strictes")]
        [Tooltip("Distance minimale au BORD de lane (m) ; le bord est a 3 a 6,5 m du centre : marge > 4 m du centre.")]
        [SerializeField] private float laneEdgeMin = 4f;
        [SerializeField] private float plainLaneEdgeMin = 8f;
        [SerializeField] private float slotMin = 6f;
        [Tooltip("Faux : les rochers peuvent etre sur le NavMesh (il est rebake avec leurs colliders), tant qu'ils restent hors des lanes, slots, spawns et couloirs.")]
        [SerializeField] private bool respectNavMesh;
        [SerializeField] private float navMeshPadding = 0.5f;
        [SerializeField] private float protectThreshold = 0.5f;
        [SerializeField] private float plateauKeepOut = 41.5f;
        [SerializeField] private float spawnKeepOut = 32f;
        [Tooltip("Couloir de camera : herse -> lane a cette distance, sans rocher.")]
        [SerializeField] private float corridorDistance = 110f;
        [SerializeField] private float corridorHalfWidth = 9f;

        #endregion

        #region Reperes et belvedere

        [Header("Reperes hero (racine separee)")]
        [SerializeField] private Landmark[] landmarks =
        {
            new Landmark { label = "Repere_A_GrosRocher", position = new Vector2(140f, -10f), laneOffset = 10f },
            new Landmark { label = "Repere_B_Formation", position = new Vector2(110f, -62f), laneOffset = 11f },
        };
        [SerializeField] private float belvedereTargetAzimuth = 45f;
        [SerializeField] private float belvedereMinCliffSlope = 44f;
        [SerializeField] private float belvedereGuardOffset = 3.2f;

        #endregion

        public string PrefabFolder => prefabFolder;
        public string[] CliffNames => cliffNames;
        public string[] BigNames => bigNames;
        public string[] MediumNames => mediumNames;
        public string[] SmallNames => smallNames;
        public string[] KitPanelPaths => kitPanelPaths;
        public string[] KitMediumPaths => kitMediumPaths;
        public string[] KitSmallPaths => kitSmallPaths;
        public string[] KitScreePaths => kitScreePaths;
        public string[] KitSlabPaths => kitSlabPaths;
        public string[] KitLandmarkPaths => kitLandmarkPaths;
        public float FarScaleStart => farScaleStart;
        public float FarScaleEnd => farScaleEnd;
        public float FarScaleMax => farScaleMax;
        public float KitLuminanceMatch => kitLuminanceMatch;
        public float KitScaleVariation => kitScaleVariation;
        public float MirrorChance => mirrorChance;
        public float TiltDegrees => tiltDegrees;
        public float KitStretchXZ => kitStretchXZ;
        public float KitStretchY => kitStretchY;
        public float KitScale(RockSize size)
        {
            switch (size)
            {
                case RockSize.Medium: return kitMediumScale;
                case RockSize.Small: return kitSmallScale;
                case RockSize.Scree: return kitScreeScale;
                case RockSize.Panel: return kitPanelScale;
                default: return 1f;
            }
        }
        public float WallPanelShare => wallPanelShare;
        public float ScreeAnchorShare => screeAnchorShare;
        public int Seed => seed;
        public string RootName => rootName;
        public string HeroRootName => heroRootName;
        public float MaxRadiusFromHerse => maxRadiusFromHerse;
        public float GridSpacing => gridSpacing;
        public int MaxRocks => maxRocks;
        public float DensityMultiplier => densityMultiplier;
        public float FiveRockChance => fiveRockChance;
        public float BurialMin => burialMin;
        public float BurialMax => burialMax;
        public float ScaleVariation => scaleVariation;
        public float AlignToNormal => alignToNormal;
        public float OverlapPadding => overlapPadding;
        public float WallMinSlope => wallMinSlope;
        public float WallMinRel => wallMinRel;
        public float WallSpacing => wallSpacing;
        public int WallMaxRocks => wallMaxRocks;
        public float WallMaxRadius => wallMaxRadius;
        public float WallScaleMin => wallScaleMin;
        public float WallScaleMax => wallScaleMax;
        public float WallOverlap => wallOverlap;
        public float TintDarkRel => tintDarkRel;
        public float TintLightRel => tintLightRel;
        public float DarkBrightness => darkBrightness;
        public float LightBrightness => lightBrightness;
        public int AltitudeBuckets => altitudeBuckets;
        public Color TopTint => topTint;
        public float ValleyY => valleyY;
        public bool UseColdMaterials => useColdMaterials;
        public string ColdMaterialFolder => coldMaterialFolder;
        public Color ColdTint => coldTint;
        public Color MossColor => mossColor;
        public AnimationCurve DensityByAltitude => densityByAltitude;
        public float PatchMin => patchMin;
        public float PatchMax => patchMax;
        public float AltScaleMin => altScaleMin;
        public float AltScaleMax => altScaleMax;
        public float AltScaleStartRel => altScaleStartRel;
        public float AltScaleEndRel => altScaleEndRel;
        public float CliffAnchorStartRel => cliffAnchorStartRel;
        public float CliffAnchorFullRel => cliffAnchorFullRel;
        public float CliffAnchorMax => cliffAnchorMax;
        public float BigAnchorStartRel => bigAnchorStartRel;
        public float BigAnchorFullRel => bigAnchorFullRel;
        public float SlopeBurialBonus => slopeBurialBonus;
        public float NearPlateauBoost => nearPlateauBoost;
        public float FarPlateauFactor => farPlateauFactor;
        public float CliffZoneMinSlope => cliffZoneMinSlope;
        public float PatchNoiseScale => patchNoiseScale;
        public float LaneEdgeMin => laneEdgeMin;
        public float PlainLaneEdgeMin => plainLaneEdgeMin;
        public float SlotMin => slotMin;
        public bool RespectNavMesh => respectNavMesh;
        public float NavMeshPadding => navMeshPadding;
        public float ProtectThreshold => protectThreshold;
        public float PlateauKeepOut => plateauKeepOut;
        public float SpawnKeepOut => spawnKeepOut;
        public float CorridorDistance => corridorDistance;
        public float CorridorHalfWidth => corridorHalfWidth;
        public Landmark[] Landmarks => landmarks;
        public float BelvedereTargetAzimuth => belvedereTargetAzimuth;
        public float BelvedereMinCliffSlope => belvedereMinCliffSlope;
        public float BelvedereGuardOffset => belvedereGuardOffset;

        public static MountainRockProfile LoadOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<MountainRockProfile>(AssetPath);
            if (profile != null) return profile;
            profile = CreateInstance<MountainRockProfile>();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
