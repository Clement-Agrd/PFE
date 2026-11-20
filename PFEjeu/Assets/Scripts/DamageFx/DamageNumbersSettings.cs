using UnityEngine;

namespace DamageFx
{
    /// <summary>Réglages globaux des chiffres de dégâts (cumul, plafond, seuils, distances, animation).</summary>
    [CreateAssetMenu(fileName = "DamageNumbersSettings", menuName = "Damage FX/Damage Numbers Settings")]
    public sealed class DamageNumbersSettings : ScriptableObject
    {
        [Header("Apparence")]
        [SerializeField, Tooltip("Icône (forme) à côté du chiffre. Désactivé par défaut.")] private bool showIcons = false;
        public bool ShowIcons => showIcons;

        [Header("Cumul par cible")]
        [SerializeField, Min(0.05f), Tooltip("Les coups reçus par une même cible dans cette fenêtre s'additionnent en UN chiffre.")]
        private float accumulationWindow = 0.4f;
        [SerializeField, Min(0.05f), Tooltip("Fenêtre plus longue pour les petits dégâts (ticks de poison / brûlure).")]
        private float smallAccumulationWindow = 0.8f;
        [SerializeField, Min(0.2f), Tooltip("Durée max pendant laquelle un chiffre accepte des coups avant de s'envoler.")]
        private float maxAccumulationTime = 1.6f;

        [Header("Plafond")]
        [SerializeField, Min(1)] private int maxActiveNumbers = 25;

        [Header("Petits dégâts")]
        [SerializeField, Min(0f)] private float smallDamageAbsolute = 5f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Sous ce % des PV max de la cible (si connu), le coup est 'petit'.")]
        private float smallDamageFraction = 0.03f;
        [SerializeField, Range(0.2f, 1f)] private float smallScale = 0.6f;
        [SerializeField, Min(1f), Tooltip("Dégâts à partir desquels un coup est 'gros' (visible de loin).")]
        private float bigHit = 50f;

        [Header("Taille")]
        [SerializeField, Min(0.001f), Tooltip("Échelle monde d'un chiffre à la distance de référence (à régler selon la taille de police du prefab).")]
        private float baseWorldScale = 0.45f;
        [SerializeField, Min(0.1f)] private float minSizeMultiplier = 0.8f;
        [SerializeField, Min(0.1f)] private float maxSizeMultiplier = 1.9f;
        [SerializeField, Min(1f), Tooltip("Dégâts pour atteindre la taille maximale.")] private float maxDamageReference = 120f;
        [SerializeField, Min(1f)] private float critScale = 1.45f;

        [Header("Distance (taille quasi constante à l'écran)")]
        [SerializeField, Min(1f)] private float referenceDistance = 12f;
        [SerializeField, Min(0.1f)] private float minDistanceFactor = 0.6f;
        [SerializeField, Min(0.1f)] private float maxDistanceFactor = 3f;
        [SerializeField, Min(1f), Tooltip("Au-delà : seuls les gros coups restent affichés.")] private float hideDistance = 45f;
        [SerializeField, Min(1f), Tooltip("Au-delà : plus rien, même les gros coups.")] private float farDistance = 110f;

        [Header("Placement")]
        [SerializeField, Min(0f)] private float spawnJitter = 0.25f;
        [SerializeField, Min(0f), Tooltip("Décalage vertical d'un nouveau chiffre repoussé hors de ceux déjà présents sur la cible.")]
        private float stackStep = 0.38f;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float life = 0.7f;
        [SerializeField, Min(0f)] private float rise = 1f;
        [SerializeField, Min(0f), Tooltip("Largeur de l'arc latéral (m).")] private float arc = 0.3f;
        [SerializeField, Min(0.02f)] private float popDuration = 0.1f;
        [SerializeField, Range(0.3f, 0.95f), Tooltip("Le fondu ne commence qu'à ce point de la vie (0,67 = dernier tiers).")]
        private float fadeStart = 0.67f;
        [SerializeField, Min(0f)] private float rollDegrees = 6f;
        [SerializeField, Min(0f)] private float critShakeAmplitude = 0.1f;
        [SerializeField, Min(0f)] private float critShakeDuration = 0.18f;

        public float AccumulationWindow => accumulationWindow;
        public float SmallAccumulationWindow => smallAccumulationWindow;
        public float MaxAccumulationTime => maxAccumulationTime;
        public int MaxActiveNumbers => maxActiveNumbers;
        public float SmallDamageAbsolute => smallDamageAbsolute;
        public float SmallDamageFraction => smallDamageFraction;
        public float SmallScale => smallScale;
        public float BigHit => bigHit;
        public float BaseWorldScale => baseWorldScale;
        public float MinSizeMultiplier => minSizeMultiplier;
        public float MaxSizeMultiplier => maxSizeMultiplier;
        public float MaxDamageReference => maxDamageReference;
        public float CritScale => critScale;
        public float ReferenceDistance => referenceDistance;
        public float MinDistanceFactor => minDistanceFactor;
        public float MaxDistanceFactor => maxDistanceFactor;
        public float HideDistance => hideDistance;
        public float FarDistance => farDistance;
        public float SpawnJitter => spawnJitter;
        public float StackStep => stackStep;
        public float Life => life;
        public float Rise => rise;
        public float Arc => arc;
        public float PopDuration => popDuration;
        public float FadeStart => fadeStart;
        public float RollDegrees => rollDegrees;
        public float CritShakeAmplitude => critShakeAmplitude;
        public float CritShakeDuration => critShakeDuration;
    }
}
