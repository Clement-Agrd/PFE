using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Tous les reglages du level design vertical du village.
    /// Les etages ne sont pas des rectangles poses sur le terrain : ils SUIVENT le relief sculpte a la main.
    /// Chaque tranche d'altitude du terrain d'origine est aplanie a l'altitude de son etage, les murs
    /// apparaissent donc le long des courbes de niveau naturelles.
    /// Repere local : s = distance le long de l'axe central depuis la herse, t = ecart lateral.
    /// </summary>
    [CreateAssetMenu(fileName = "VillageLayoutProfile", menuName = "Village/Layout Profile")]
    public sealed class VillageLayoutProfile : ScriptableObject
    {
        public const string AssetPath = "Assets/Editor/Village/VillageLayoutProfile.asset";

        #region Activation

        [Header("Activation")]
        [Tooltip("Si faux, le sculpt du relief ne recoit aucune couche village.")]
        [SerializeField] private bool terracesEnabled = true;

        #endregion

        #region Repere

        [Header("Repere (XZ monde)")]
        [Tooltip("Origine de l'axe central : la herse.")]
        [SerializeField] private Vector2 axisOrigin = new Vector2(80.73f, -88.42f);
        [Tooltip("Point vise par l'axe central (sommet de la butte). Seule la direction compte.")]
        [SerializeField] private Vector2 axisTarget = new Vector2(25f, -151.75f);

        #endregion

        #region Etages

        [Header("Etages")]
        [Tooltip("Y0 : altitude monde de l'etage bas (sol devant la herse cote village).")]
        [SerializeField] private float groundFloorY = 21f;
        [Tooltip("Denivele entre deux etages (m).")]
        [SerializeField, Min(1f)] private float floorStep = 6f;
        [Tooltip("Etages sureleves, du plus bas au plus haut.")]
        [SerializeField] private List<string> upperFloorLabels = new List<string> { "Milieu", "Haut" };
        [Tooltip("Decalage (m) du seuil d'altitude naturelle de chaque mur, dans l'ordre des etages sureleves. " +
                 "0 = chaque point rejoint l'etage le plus proche (terrassement minimal). " +
                 "Negatif = le mur avance vers le bas de la pente, positif = il recule : -1 / +1 elargit l'etage du milieu.")]
        [SerializeField] private List<float> wallThresholdOffsets = new List<float> { -1f, 1f };
        [Tooltip("Rayon (m) de lissage du relief naturel avant de tracer les murs : gomme les petits accidents pour des contours amples. 0 = contours bruts.")]
        [SerializeField, Range(0f, 12f)] private float contourSmoothingRadius = 5f;

        #endregion

        #region Geologie des murs

        [Header("Geologie des murs (crete erodee, paroi rocheuse, cone d'eboulis)")]
        [Tooltip("Angle de la paroi rocheuse (deg).")]
        [SerializeField, Range(60f, 88f)] private float cliffFaceAngleDegrees = 80f;
        [Tooltip("Abaissement (m) de l'arete au bord de l'etage : crete arrondie par l'erosion.")]
        [SerializeField, Range(0f, 1.5f)] private float crestDrop = 0.35f;
        [Tooltip("Largeur (m) sur laquelle la crete s'arrondit.")]
        [SerializeField, Range(0.5f, 5f)] private float crestWidth = 1.5f;
        [Tooltip("Part minimale de la hauteur du mur occupee par l'eboulis.")]
        [SerializeField, Range(0f, 0.9f)] private float talusShareMin = 0.12f;
        [Tooltip("Part maximale de la hauteur du mur occupee par l'eboulis.")]
        [SerializeField, Range(0f, 0.9f)] private float talusShareMax = 0.5f;
        [Tooltip("Angle de repos de l'eboulis (deg). Environ 34 dans la nature.")]
        [SerializeField, Range(20f, 45f)] private float talusAngleDegrees = 34f;
        [Tooltip("1 = cone droit, > 1 = cone concave (raide sous la paroi, etale en pied).")]
        [SerializeField, Range(1f, 3f)] private float talusConcavity = 1.4f;
        [Tooltip("Longueur d'onde (m) de la variation d'eboulis le long du mur.")]
        [SerializeField, Min(2f)] private float talusNoiseWavelength = 14f;
        [Tooltip("Ondulation du trace des murs, en metres d'altitude naturelle : promontoires et rentrants.")]
        [SerializeField, Range(0f, 4f)] private float contourNoiseAmplitude = 1.2f;
        [SerializeField, Min(5f)] private float contourNoiseWavelength = 30f;
        [Tooltip("Ondulation de detail du trace des murs (m d'altitude naturelle).")]
        [SerializeField, Range(0f, 2f)] private float contourDetailAmplitude = 0.5f;
        [SerializeField, Min(2f)] private float contourDetailWavelength = 11f;
        [Tooltip("Decalage du bruit : changer pour obtenir une autre variante du meme relief.")]
        [SerializeField] private Vector2 noiseOffset = new Vector2(137.3f, 71.9f);

        #endregion

        #region Zone

        [Header("Zone terrassee (au-dela, le relief d'origine est conserve)")]
        [SerializeField] private Vector2 zoneCenter = new Vector2(22f, -148f);
        [Tooltip("Rayon (m) dans lequel les etages sont appliques a 100 %.")]
        [SerializeField, Min(5f)] private float zoneRadius = 48f;
        [Tooltip("Largeur (m) du fondu vers le relief d'origine.")]
        [SerializeField, Min(1f)] private float zoneFade = 22f;

        #endregion

        #region Etage bas

        [Header("Etage bas (plateau existant, simplement aplani)")]
        [SerializeField] private float groundFloorStart = 10f;
        [SerializeField] private float groundFloorEnd = 32f;
        [SerializeField, Min(4f)] private float groundFloorHalfWidth = 32f;
        [Tooltip("Largeur du raccord doux entre le plat et le terrain alentour (m).")]
        [SerializeField, Min(0.5f)] private float groundFloorBlend = 10f;
        [Tooltip("En deca de cette distance des splines de la muraille, le terrain n'est pas modifie (m).")]
        [SerializeField, Min(0f)] private float wallKeepRadius = 14f;
        [SerializeField, Min(0.5f)] private float wallKeepBlend = 8f;

        #endregion

        #region Rampes

        [Header("Rampes (suivent la courbe de niveau du mur, en lacet d'un etage a l'autre)")]
        [SerializeField] private bool rampsEnabled = true;
        [Tooltip("Pente de conception (deg). Longueur = denivele / tan(pente).")]
        [SerializeField, Range(3f, 12f)] private float rampSlopeDegrees = 10f;
        [SerializeField, Min(1f)] private float rampHalfWidth = 2f;
        [Tooltip("Angle des talus de part et d'autre de la rampe (deg).")]
        [SerializeField, Range(40f, 80f)] private float rampSideAngleDegrees = 60f;
        [Tooltip("La premiere rampe monte vers les t positifs (sud-est) ; les suivantes alternent.")]
        [SerializeField] private bool firstRampClimbsTowardPositiveT = true;
        [Tooltip("Pente maximale admise (deg). Sert a la validation.")]
        [SerializeField, Range(1f, 20f)] private float maxRampSlopeDegrees = 12f;

        #endregion

        #region Escaliers

        [Header("Escalier central (blocking : rampe raide a remplacer par un mesh)")]
        [SerializeField] private bool stairsEnabled = true;
        [Tooltip("Ecart lateral de l'escalier par rapport a l'axe (m).")]
        [SerializeField] private float stairLateralOffset;
        [SerializeField, Min(0.5f)] private float stairHalfWidth = 1.5f;
        [Tooltip("Pente de conception (deg). Longueur = denivele / tan(pente).")]
        [SerializeField, Range(20f, 40f)] private float stairSlopeDegrees = 32f;
        [Tooltip("Angle des joues de l'escalier (deg).")]
        [SerializeField, Range(45f, 85f)] private float stairSideAngleDegrees = 75f;
        [Tooltip("Pente maximale admise (deg) : doit rester sous le slopeLimit du joueur (46) et du NavMesh (45).")]
        [SerializeField, Range(20f, 45f)] private float maxStairSlopeDegrees = 35f;

        #endregion

        #region Validation

        [Header("Validation")]
        [Tooltip("Ecart de planeite maximal admis sur un etage (m).")]
        [SerializeField, Min(0.01f)] private float flatnessTolerance = 0.15f;

        #endregion

        public bool TerracesEnabled => terracesEnabled;
        public Vector2 AxisOrigin => axisOrigin;
        public Vector2 AxisTarget => axisTarget;
        public float GroundFloorY => groundFloorY;
        public float FloorStep => floorStep;
        public IReadOnlyList<string> UpperFloorLabels => upperFloorLabels;
        public float CliffFaceAngleDegrees => cliffFaceAngleDegrees;
        public float CrestDrop => crestDrop;
        public float CrestWidth => crestWidth;
        public float TalusShareMin => talusShareMin;
        public float TalusShareMax => talusShareMax;
        public float TalusAngleDegrees => talusAngleDegrees;
        public float TalusConcavity => talusConcavity;
        public float TalusNoiseWavelength => talusNoiseWavelength;
        public float ContourNoiseAmplitude => contourNoiseAmplitude;
        public float ContourNoiseWavelength => contourNoiseWavelength;
        public float ContourDetailAmplitude => contourDetailAmplitude;
        public float ContourDetailWavelength => contourDetailWavelength;
        public Vector2 NoiseOffset => noiseOffset;
        public float ContourSmoothingRadius => contourSmoothingRadius;
        public Vector2 ZoneCenter => zoneCenter;
        public float ZoneRadius => zoneRadius;
        public float ZoneFade => zoneFade;
        public float GroundFloorStart => groundFloorStart;
        public float GroundFloorEnd => groundFloorEnd;
        public float GroundFloorHalfWidth => groundFloorHalfWidth;
        public float GroundFloorBlend => groundFloorBlend;
        public float WallKeepRadius => wallKeepRadius;
        public float WallKeepBlend => wallKeepBlend;
        public bool RampsEnabled => rampsEnabled;
        public float RampSlopeDegrees => rampSlopeDegrees;
        public float RampHalfWidth => rampHalfWidth;
        public float RampSideAngleDegrees => rampSideAngleDegrees;
        public bool FirstRampClimbsTowardPositiveT => firstRampClimbsTowardPositiveT;
        public float MaxRampSlopeDegrees => maxRampSlopeDegrees;
        public bool StairsEnabled => stairsEnabled;
        public float StairLateralOffset => stairLateralOffset;
        public float StairHalfWidth => stairHalfWidth;
        public float StairSlopeDegrees => stairSlopeDegrees;
        public float StairSideAngleDegrees => stairSideAngleDegrees;
        public float MaxStairSlopeDegrees => maxStairSlopeDegrees;
        public float FlatnessTolerance => flatnessTolerance;

        /// <summary>Altitude de l'etage d'indice donne (0 = bas).</summary>
        public float FloorY(int index) => groundFloorY + index * floorStep;

        /// <summary>Altitude naturelle a partir de laquelle le terrain appartient a l'etage sureleve d'indice donne (1 = premier).</summary>
        public float Threshold(int upperIndex)
        {
            float offset = upperIndex - 1 < wallThresholdOffsets.Count ? wallThresholdOffsets[upperIndex - 1] : 0f;
            return groundFloorY + (upperIndex - 0.5f) * floorStep + offset;
        }

        public static VillageLayoutProfile LoadOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VillageLayoutProfile>(AssetPath);
            if (profile != null) return profile;

            profile = CreateInstance<VillageLayoutProfile>();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
