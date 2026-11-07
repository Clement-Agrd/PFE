using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Tous les reglages du relief de la zone de defense.
    /// Aucune valeur de design n'est ecrite dans le code des outils.
    /// </summary>
    [CreateAssetMenu(fileName = "DefenseReliefConfig", menuName = "Defense/Relief Config")]
    public sealed class DefenseReliefConfig : ScriptableObject
    {
        public const string AssetPath = "Assets/Editor/DefenseRelief/DefenseReliefConfig.asset";
        private const string BackupTerrainPath =
            "Assets/Scenes/SceneJeux/Defense/SceneJeux_DefenseTerrain_backup.asset";

        #region Types

        public enum ReliefPreset
        {
            Soft,
            Current,
            /// <summary>Plaine plate vers les spawns, relief concentre vers la base (herse en hauteur).</summary>
            SoftInverted,
        }

        /// <summary>Forme du bord du plateau de la base.</summary>
        public enum PlateauEdgeMode
        {
            /// <summary>Glacis doux continu (comportement d'origine).</summary>
            Glacis,
            /// <summary>Falaise raide sur le pourtour ; glacis conserve aux passages et autour des slots.</summary>
            FalaiseAvecPassages,
        }

        /// <summary>Passage (rampe douce) du plateau vers la vallee, defini par un point cible.</summary>
        [Serializable]
        public sealed class EdgePassage
        {
            public string label;
            [Tooltip("Point XZ monde vers lequel le passage part du centre du plateau.")]
            public Vector2 center;
            [Tooltip("Demi-largeur du passage (m) ou le glacis est conserve a 100 %.")]
            public float halfWidth = 9f;
            [Tooltip("Largeur du raccord falaise/passage (m).")]
            public float blend = 8f;
        }

        /// <summary>Plateforme plane sous un batiment (topologie refaite autour de lui).</summary>
        [Serializable]
        public sealed class BuildingPad
        {
            public string label;
            public Vector2 center;
            [Tooltip("Demi-dimensions X/Z du pad plat (m).")]
            public Vector2 halfSize = new Vector2(10f, 10f);
            [Tooltip("Altitude monde du pad.")]
            public float y;
            [Tooltip("Largeur du raccord (m) autour du pad.")]
            public float blend = 10f;
        }

        /// <summary>
        /// Profil "Doux modifie" : toutes les cotes sont relatives a la vallee (valleyY).
        /// Distances mesurees le long de la route depuis la fin du spline (cote herse).
        /// </summary>
        [Serializable]
        public sealed class InvertedProfile
        {
            [Header("Pads de batiments (topologie refaite)")]
            public List<BuildingPad> pads = new List<BuildingPad>
            {
                new BuildingPad { label = "HotelDeVille", center = new Vector2(71.01f, -139.72f), halfSize = new Vector2(11f, 13f), y = 20.8f, blend = 10f },
            };

            [Header("Altitudes (m, relatives a la vallee)")]
            public float valleyY = 5f;
            public float plateauRelHeight = 16f;
            public float palierBRel = 7f;
            public float palierARel = 2f;

            [Header("Longueurs le long de la route (m, depuis la herse)")]
            [Tooltip("Plateau de la base le long de la lane (~ rayon plat - decalage fin de spline/herse).")]
            public float plateauLaneLength = 30f;
            [Tooltip("Montee finale +7 -> +16 (pente moyenne = 9 / longueur).")]
            public float climbLength = 48f;
            public float palierBLength = 10f;
            [Tooltip("Rampe +2 -> +7 (routes passant par la jonction A).")]
            public float rampBALength = 28f;
            public float palierALength = 10f;
            [Tooltip("Rampe +0 -> +2 en ease-in (routes passant par A).")]
            public float softRampLength = 30f;
            [Tooltip("Rampe +7 -> +0 pour les routes qui rejoignent B sans passer par A.")]
            public float rampBToPlainLength = 42f;
            [Tooltip("Plaine plate souhaitee apres chaque spawn.")]
            public float spawnFlatLength = 40f;
            [Tooltip("Si la route est trop courte, les paliers sont raccourcis d'abord, puis la plaine jusqu'a ce plancher.")]
            public float spawnFlatFloor = 28f;
            [Range(0f, 1f)] public float climbEase = 0.15f;
            public float maxSlopeDegrees = 12f;

            [Header("Plateau et glacis")]
            public float plateauRadius = 40f;
            public float plateauBlend = 12f;
            [Tooltip("Pente max (m/m) avec laquelle le plateau monte au-dessus d'une lane voisine plus basse (0.18 = ~10 deg).")]
            public float plateauMaxSlope = 0.18f;
            [Tooltip("Metres de glacis par metre de denivele (5.5-6 => ~11-12 deg avec smoothstep).")]
            public float flankRunPerMeter = 6f;
            public float minFlankBlend = 10f;
            [Header("Bord du plateau (mode)")]
            public PlateauEdgeMode edgeMode = PlateauEdgeMode.Glacis;
            [Tooltip("Pente de la falaise (deg). >= 47 : infranchissable pour le joueur (slopeLimit 46) et le NavMesh (45).")]
            [Range(30f, 55f)] public float cliffSlopeDegrees = 48f;
            [Tooltip("Rayon d'arrondi de l'arete haute de la falaise (m).")]
            public float cliffCrestRound = 2.5f;
            [Tooltip("Adoucissement du pied de falaise (m de hauteur).")]
            public float cliffFootSoftness = 2f;
            [Tooltip("Autour de chaque slot, le glacis est conserve (rayon plein, m).")]
            public float cliffSlotRadius = 11f;
            public float cliffSlotBlend = 8f;
            [Tooltip("Autour des splines de la muraille (non modifiee), le glacis est conserve (rayon plein, m).")]
            public float cliffWallRadius = 14f;
            public float cliffWallBlend = 10f;
            public List<EdgePassage> passages = new List<EdgePassage>
            {
                new EdgePassage { label = "Passage Est", center = new Vector2(125f, -90f), halfWidth = 9f, blend = 8f },
            };

            [Header("Glacis et reseau IDW")]
            public float idwRadius = 110f;
            [Tooltip("Exposant IDW sur (d^2+1) : 2 = tres local, 1 = tres doux (melange entre lanes voisines).")]
            public float idwExponent = 1.5f;

            [Header("Buttes")]
            [Tooltip("Amplitude max de l'ondulation, atteinte pres du plateau. 0 dans la plaine.")]
            public float undulationMax = 0.4f;
            public float undulationFullRel = 12f;
            [Tooltip("Denivele de lane a partir duquel les berges apparaissent (pas de talus en plaine).")]
            public float bankFullRel = 4f;

            [Header("Plateformes de slots")]
            public float slotRaiseNear = 1.25f;
            [Tooltip("Distance depuis le spawn sous laquelle aucune plateforme n'est sculptee.")]
            public float slotNoRaiseDistance = 50f;
            public float slotRaiseRamp = 60f;
            public float slotPlatformBlend = 4f;
            [Tooltip("Largeur (m) depuis le bord de lane sur laquelle la surelevation monte (rampe lineaire, pente de bord <= 25 deg).")]
            public float slotLaneRamp = 3f;
            [Tooltip("Remblai max d'une plateforme de slot (m).")]
            public float slotMaxFill = 1.3f;
            [Tooltip("Deblai max d'une plateforme de slot (m).")]
            public float slotMaxCut = 0.4f;
        }

        /// <summary>Amplitudes et formes du profil d'altitude d'un preset.</summary>
        [Serializable]
        public sealed class PresetProfile
        {
            [Tooltip("Courbe en S (smoothstep) au lieu de rampes lineaires.")]
            public bool sCurve = true;
            [Tooltip("Pente moyenne cible des rampes (deg). 0 = longueurs fixes ci-dessous.")]
            public float averageRampSlopeDegrees = 8f;
            public float junctionBY = 12f;
            public float junctionAY = 18f;
            [Tooltip("Altitude des spawns. <= 0 : valeur propre a chaque route.")]
            public float spawnY = 25f;
            public float junctionBRampLength = 45f;
            public float junctionARampLength = 45f;
            public float junctionBFlatLength = 18f;
            public float junctionAFlatLength = 20f;
            public float spawnFlatLength = 15f;

            public static PresetProfile CreateSoft() => new PresetProfile();

            public static PresetProfile CreateCurrent() => new PresetProfile
            {
                sCurve = false,
                averageRampSlopeDegrees = 0f,
                junctionBY = 15f,
                junctionAY = 25f,
                spawnY = 0f,
                junctionBFlatLength = 18f,
                junctionAFlatLength = 20f,
                spawnFlatLength = 8f,
            };
        }

        [Serializable]
        public sealed class RouteSettings
        {
            [Tooltip("Nom de l'objet spline sous EnemyPath.")]
            public string splineName;
            [Tooltip("Altitude (Y monde) du plateau de depart.")]
            public float spawnY = 35f;
            [Tooltip("La route traverse la jonction A (routes 01 et 04).")]
            public bool hasJunctionA;
            [Tooltip("Centre de lane XZ, capture une seule fois avant toute modification.")]
            public Vector2[] centerline = Array.Empty<Vector2>();
        }

        [Serializable]
        public sealed class ObjectOffset
        {
            public string hierarchyPath;
            [Tooltip("Y de l'objet moins hauteur du terrain d'origine, mesure avant modification.")]
            public float offset;
        }

        [Serializable]
        public sealed class NewSlot
        {
            public string slotName;
            public Vector2 position;
        }

        #endregion

        #region Terrain source

        [Header("Terrain source")]
        [SerializeField] private TerrainData baseTerrainData;
        [SerializeField] private int targetHeightmapResolution = 1025;

        #endregion

        #region Profil d'altitude

        [Header("Profil d'altitude (distance mesuree depuis la fin de la route)")]
        [SerializeField] private float herseLevelY = 5f;
        [SerializeField] private float herseFlatLength = 12f;
        [SerializeField] private ReliefPreset preset = ReliefPreset.Soft;
        [SerializeField] private PresetProfile softProfile = PresetProfile.CreateSoft();
        [SerializeField] private PresetProfile currentProfile = PresetProfile.CreateCurrent();
        [SerializeField] private InvertedProfile invertedProfile = new InvertedProfile();
        [SerializeField] private List<RouteSettings> routes = new List<RouteSettings>();

        #endregion

        #region Lanes

        [Header("Largeur de lane (demi-largeur)")]
        [SerializeField] private float narrowHalfWidth = 3f;
        [SerializeField] private float openHalfWidth = 6.5f;
        [SerializeField] private float narrowUntilDistance = 57f;
        [SerializeField] private float openFromDistance = 120f;
        [SerializeField] private float sampleSpacing = 2f;
        [SerializeField] private float splineKnotSpacing = 8f;

        [Header("Rives et fondu")]
        [SerializeField] private float bankHeight = 1.5f;
        [SerializeField] private float bankWidth = 8f;
        [SerializeField] private float blendRadius = 60f;
        [Tooltip("0 = fondu lineaire (pente constante), 1 = smoothstep (pente de pointe x1.5).")]
        [SerializeField, Range(0f, 1f)] private float flankEase = 0.35f;
        [SerializeField] private float idwRadius = 60f;
        [SerializeField] private int smoothingPasses = 3;
        [Tooltip("Ondulation basse frequence (m). 0 = desactivee. Max conseille 0.5.")]
        [SerializeField] private float undulationAmplitude = 0.4f;
        [SerializeField] private float undulationWavelength = 60f;

        #endregion

        #region Zones protegees et plateformes

        [Header("Herse / casernes")]
        [SerializeField] private float herseFlatRadius = 12f;
        [SerializeField] private float herseBlend = 4f;
        [SerializeField] private float barracksPadHalfSize = 4f;
        [SerializeField] private float barracksPadBlend = 5f;
        [SerializeField] private float barracksAccessHalfWidth = 2.5f;
        [SerializeField] private float barracksAccessReach = 2f;
        [Tooltip("Points de passage XZ du couloir plat entre les casernes et la zone de la herse (contourne la lane).")]
        [SerializeField] private List<Vector2> barracksAccessWaypoints = new List<Vector2> { new Vector2(102.8f, -73.8f), new Vector2(103.5f, -80.5f), new Vector2(110f, -92f), new Vector2(101f, -98f) };
        [SerializeField] private float barracksAccessFade = 2f;

        [Header("Slots")]
        [SerializeField] private float slotPlatformHalfSize = 3f;
        [SerializeField] private float slotPlatformBlend = 2f;
        [SerializeField] private float slotOuterBoost = 1.2f;
        [SerializeField] private float slotMaxCut = 0.8f;
        [SerializeField] private float slotMaxFill = 1.5f;
        [SerializeField] private float minBendForOuterDegrees = 10f;
        [SerializeField] private Vector3 slotTorchLocalOffset = new Vector3(-4f, 0.55f, 0f);
        [SerializeField] private List<NewSlot> newSlots = new List<NewSlot>
        {
            new NewSlot { slotName = "TowerSlot_05_01", position = new Vector2(93f, -65f) },
            new NewSlot { slotName = "TowerSlot_05_02", position = new Vector2(85f, -72f) },
            new NewSlot { slotName = "TowerSlot_05_03", position = new Vector2(99f, -87f) },
            new NewSlot { slotName = "TowerSlot_05_04", position = new Vector2(73f, -80f) },
        };

        #endregion

        #region Lisibilite

        [Header("Lisibilite")]
        [SerializeField] private string pathLayerName = "Defense_Dirt";
        [SerializeField] private float pathPaintFade = 2f;
        [SerializeField] private float clearMargin = 3f;

        #endregion

        #region Offsets captures

        [Header("Offsets d'objets (captures une fois)")]
        [SerializeField] private List<ObjectOffset> offsets = new List<ObjectOffset>();

        #endregion

        public TerrainData BaseTerrainData => baseTerrainData;
        public int TargetHeightmapResolution => targetHeightmapResolution;
        public float HerseLevelY => herseLevelY;
        public float HerseFlatLength => herseFlatLength;
        public PresetProfile ActiveProfile => preset == ReliefPreset.Current ? currentProfile : softProfile;
        public bool IsInverted => preset == ReliefPreset.SoftInverted;
        public InvertedProfile Inverted => invertedProfile;
        public float JunctionBY => ActiveProfile.junctionBY;
        public float JunctionBRampLength => ActiveProfile.junctionBRampLength;
        public float JunctionBFlatLength => ActiveProfile.junctionBFlatLength;
        public float JunctionAY => ActiveProfile.junctionAY;
        public float JunctionARampLength => ActiveProfile.junctionARampLength;
        public float JunctionAFlatLength => ActiveProfile.junctionAFlatLength;
        public float SpawnFlatLength => ActiveProfile.spawnFlatLength;
        public float UndulationAmplitude => undulationAmplitude;
        public float UndulationWavelength => undulationWavelength;
        public float SlotMaxCut => slotMaxCut;
        public float SlotMaxFill => slotMaxFill;
        public IReadOnlyList<RouteSettings> Routes => routes;
        public float NarrowHalfWidth => narrowHalfWidth;
        public float OpenHalfWidth => openHalfWidth;
        public float NarrowUntilDistance => narrowUntilDistance;
        public float OpenFromDistance => openFromDistance;
        public float SampleSpacing => sampleSpacing;
        public float SplineKnotSpacing => splineKnotSpacing;
        public float BankHeight => bankHeight;
        public float BankWidth => bankWidth;
        public float BlendRadius => blendRadius;
        public float FlankEase => flankEase;
        public float IdwRadius => idwRadius;
        public int SmoothingPasses => smoothingPasses;
        public float HerseFlatRadius => herseFlatRadius;
        public float HerseBlend => herseBlend;
        public float BarracksPadHalfSize => barracksPadHalfSize;
        public float BarracksPadBlend => barracksPadBlend;
        public float BarracksAccessHalfWidth => barracksAccessHalfWidth;
        public float BarracksAccessReach => barracksAccessReach;
        public IReadOnlyList<Vector2> BarracksAccessWaypoints => barracksAccessWaypoints;
        public float BarracksAccessFade => barracksAccessFade;
        public float SlotPlatformHalfSize => slotPlatformHalfSize;
        public float SlotPlatformBlend => slotPlatformBlend;
        public float SlotOuterBoost => slotOuterBoost;
        public float MinBendForOuterDegrees => minBendForOuterDegrees;
        public Vector3 SlotTorchLocalOffset => slotTorchLocalOffset;
        public IReadOnlyList<NewSlot> NewSlots => newSlots;
        public string PathLayerName => pathLayerName;
        public float PathPaintFade => pathPaintFade;
        public float ClearMargin => clearMargin;
        public IReadOnlyList<ObjectOffset> Offsets => offsets;

        public bool HasCapturedRoutes
        {
            get
            {
                if (routes.Count == 0) return false;
                foreach (RouteSettings route in routes)
                {
                    if (route.centerline == null || route.centerline.Length < 2) return false;
                }
                return true;
            }
        }

        #region Editor API

        public static DefenseReliefConfig LoadOrCreate()
        {
            var config = AssetDatabase.LoadAssetAtPath<DefenseReliefConfig>(AssetPath);
            if (config != null) return config;

            config = CreateInstance<DefenseReliefConfig>();
            config.baseTerrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(BackupTerrainPath);
            config.routes = new List<RouteSettings>
            {
                new RouteSettings { splineName = "Spline",     spawnY = 33f, hasJunctionA = true  },
                new RouteSettings { splineName = "Spline (1)", spawnY = 35f, hasJunctionA = false },
                new RouteSettings { splineName = "Spline (2)", spawnY = 33f, hasJunctionA = false },
                new RouteSettings { splineName = "Spline (3)", spawnY = 35f, hasJunctionA = true  },
            };
            AssetDatabase.CreateAsset(config, AssetPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        public void SetCenterline(int routeIndex, Vector2[] points)
        {
            routes[routeIndex].centerline = points;
            EditorUtility.SetDirty(this);
        }

        public void SetOffset(string path, float offset)
        {
            foreach (ObjectOffset entry in offsets)
            {
                if (entry.hierarchyPath != path) continue;
                entry.offset = offset;
                EditorUtility.SetDirty(this);
                return;
            }
            offsets.Add(new ObjectOffset { hierarchyPath = path, offset = offset });
            EditorUtility.SetDirty(this);
        }

        public bool TryGetOffset(string path, out float offset)
        {
            foreach (ObjectOffset entry in offsets)
            {
                if (entry.hierarchyPath != path) continue;
                offset = entry.offset;
                return true;
            }
            offset = 0f;
            return false;
        }

        #endregion
    }
}
