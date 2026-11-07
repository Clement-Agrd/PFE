using System;
using UnityEngine;

namespace Core.Vfx
{
    public enum VfxQualityLevel
    {
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// Toutes les valeurs de reglage des VFX d'ambiance : couleurs, densites, budgets, qualite.
    /// Aucune valeur de design n'est ecrite dans le code des effets.
    ///
    /// Architecture (voir aussi MistLayer) :
    ///
    ///   VfxProfile (SO) --------------------+
    ///        |                              |
    ///        v                              v
    ///   MistLayer : IVfxPlayer         (P2+) VfxService : abonne aux evenements de jeu
    ///   (brume sol / lointaine)               (WaveSpawner, Health...) -> PoolManager -> effets
    /// </summary>
    [CreateAssetMenu(fileName = "VfxProfile", menuName = "Defense/VFX Profile")]
    public sealed class VfxProfile : ScriptableObject
    {
        #region Types

        [Serializable]
        public struct QualitySettings
        {
            [Tooltip("Multiplicateur du nombre de particules d'ambiance.")]
            [Range(0.1f, 1f)] public float particleMultiplier;
            [Tooltip("Nombre de couches de brume lointaine actives.")]
            [Range(0, 3)] public int farMistLayers;
            [Tooltip("Lights reelles VFX maximum.")]
            [Min(0)] public int maxRealLights;
        }

        [Serializable]
        public struct FarMistLayer
        {
            [Tooltip("Distance min / max depuis la base (m).")]
            public Vector2 distanceRange;
            [Tooltip("Hauteur au-dessus du terrain local (m).")]
            public float heightAboveGround;
            [Min(0)] public int count;
            public Vector2 sizeRange;
            [Range(0f, 1f)] public float alpha;
            [Tooltip("Teinte relative : plus la couche est lointaine, plus elle tire vers le bleu.")]
            public Color tint;
        }

        #endregion

        #region Qualite

        [Header("Qualite")]
        [SerializeField] private VfxQualityLevel qualityLevel = VfxQualityLevel.High;
        [SerializeField] private QualitySettings low = new QualitySettings { particleMultiplier = 0.4f, farMistLayers = 1, maxRealLights = 2 };
        [SerializeField] private QualitySettings medium = new QualitySettings { particleMultiplier = 0.7f, farMistLayers = 2, maxRealLights = 4 };
        [SerializeField] private QualitySettings high = new QualitySettings { particleMultiplier = 1f, farMistLayers = 3, maxRealLights = 6 };

        #endregion

        #region Couleurs (direction artistique)

        [Header("Couleurs")]
        [SerializeField] private Color enemyColor = new Color(1f, 0.38f, 0.1f, 1f);
        [SerializeField] private Color defenseColor = new Color(0.1f, 0.85f, 0.8f, 1f);
        [SerializeField] private Color torchColor = new Color(1f, 0.6f, 0.2f, 1f);
        [SerializeField] private Color ambientColor = new Color(0.45f, 0.55f, 0.72f, 1f);

        #endregion

        #region Brume au sol

        [Header("Brume - zone (XZ monde)")]
        [SerializeField] private Vector2 mistCenter = new Vector2(150f, -10f);
        [SerializeField] private Vector2 mistExtent = new Vector2(110f, 110f);

        [Header("Brume - fond de vallee")]
        [SerializeField] private Color mistColor = new Color(0.5f, 0.6f, 0.78f, 1f);
        [SerializeField, Min(0)] private int groundMistCount = 140;
        [SerializeField] private float groundMistHeight = 2.5f;
        [SerializeField] private Vector2 groundMistSize = new Vector2(16f, 30f);
        [SerializeField, Range(0f, 1f)] private float groundMistAlpha = 0.1f;
        [SerializeField, Min(0f)] private float driftStrength = 1.2f;

        [Header("Brume - lisibilite des lanes")]
        [Tooltip("Aucune brume a moins de cette distance d'une lane (m).")]
        [SerializeField, Min(0f)] private float laneClearRadius = 12f;
        [Tooltip("Rayon de degrade au-dela du rayon libre (m).")]
        [SerializeField, Min(0f)] private float laneFadeRadius = 30f;
        [Tooltip("Pres de la base (distance a la herse), le couloir libre est elargi.")]
        [SerializeField, Min(0f)] private float baseNearDistance = 60f;
        [SerializeField, Min(1f)] private float baseNearClearMultiplier = 1.8f;

        [Header("Brume lointaine (separation des plans)")]
        [SerializeField] private FarMistLayer[] farLayers =
        {
            new FarMistLayer { distanceRange = new Vector2(140f, 240f), heightAboveGround = 5f,  count = 36, sizeRange = new Vector2(40f, 70f),   alpha = 0.09f, tint = new Color(0.55f, 0.65f, 0.85f, 1f) },
            new FarMistLayer { distanceRange = new Vector2(200f, 320f), heightAboveGround = 22f, count = 30, sizeRange = new Vector2(60f, 100f),  alpha = 0.10f, tint = new Color(0.45f, 0.56f, 0.8f, 1f) },
            new FarMistLayer { distanceRange = new Vector2(260f, 400f), heightAboveGround = 50f, count = 24, sizeRange = new Vector2(90f, 150f),  alpha = 0.12f, tint = new Color(0.38f, 0.5f, 0.78f, 1f) },
        };
        [SerializeField, Range(10f, 180f)] private float farMistSectorDegrees = 150f;

        #endregion

        #region Spawns et ennemis

        [Header("Spawns et ennemis")]
        [Tooltip("Duree du portail pendant l'apercu de vague (s). >= WaveSpawner.minPreviewDuration.")]
        [SerializeField, Min(0.5f)] private float spawnPortalDuration = 3f;
        [SerializeField, Min(0f)] private float enemyGlowHeight = 1.6f;
        [Tooltip("Une liaison de VFX par ennemi : au-dela, les nouveaux ennemis n'ont plus d'aura (budget separe).")]
        [SerializeField, Min(0)] private int maxEnemyBindings = 60;
        [SerializeField, Min(0)] private int warmupPerPrefab = 8;

        #endregion

        #region Torches

        [Header("Torches")]
        [SerializeField, Min(0f)] private float torchGlowSize = 5.5f;
        [SerializeField, Range(0f, 1f)] private float torchGlowAlpha = 0.32f;
        [Tooltip("Amplitude de la pulsation de la lueur (0 = fixe). Sinus regulier, sans bruit.")]
        [SerializeField, Range(0f, 0.5f)] private float torchPulseAmount = 0.12f;
        [SerializeField, Min(0.1f)] private float torchPulseSpeed = 2.2f;
        [Tooltip("Intervalle de recalcul des lights reelles les plus proches de la camera (s).")]
        [SerializeField, Min(0.05f)] private float torchLightRefresh = 0.25f;

        #endregion

        #region Etat nuit

        [Header("Cycle jour / nuit")]
        [Tooltip("Pas de DayNightCycle dans la scene : flag manuel, pilote par les effets nocturnes.")]
        [SerializeField] private bool isNight = true;

        #endregion

        public VfxQualityLevel QualityLevel => qualityLevel;
        public QualitySettings Quality => qualityLevel == VfxQualityLevel.Low ? low : qualityLevel == VfxQualityLevel.Medium ? medium : high;
        public Color EnemyColor => enemyColor;
        public Color DefenseColor => defenseColor;
        public Color TorchColor => torchColor;
        public Color AmbientColor => ambientColor;
        public Vector2 MistCenter => mistCenter;
        public Vector2 MistExtent => mistExtent;
        public Color MistColor => mistColor;
        public int GroundMistCount => groundMistCount;
        public float GroundMistHeight => groundMistHeight;
        public Vector2 GroundMistSize => groundMistSize;
        public float GroundMistAlpha => groundMistAlpha;
        public float DriftStrength => driftStrength;
        public float LaneClearRadius => laneClearRadius;
        public float LaneFadeRadius => laneFadeRadius;
        public float BaseNearDistance => baseNearDistance;
        public float BaseNearClearMultiplier => baseNearClearMultiplier;
        public FarMistLayer[] FarLayers => farLayers;
        public float FarMistSectorDegrees => farMistSectorDegrees;
        public bool IsNight => isNight;
        public float TorchGlowSize => torchGlowSize;
        public float TorchGlowAlpha => torchGlowAlpha;
        public float TorchPulseAmount => torchPulseAmount;
        public float TorchPulseSpeed => torchPulseSpeed;
        public float TorchLightRefresh => torchLightRefresh;
        public float SpawnPortalDuration => spawnPortalDuration;
        public float EnemyGlowHeight => enemyGlowHeight;
        public int MaxEnemyBindings => maxEnemyBindings;
        public int WarmupPerPrefab => warmupPerPrefab;

        /// <summary>Appele par l'outil d'Editor pour caler la zone de brume sur les lanes.</summary>
        public void SetMistArea(Vector2 center, Vector2 extent)
        {
            mistCenter = center;
            mistExtent = extent;
        }
    }
}
