using UnityEngine;

namespace AllyFx
{
    /// <summary>
    /// Télégraphie d'attaque : l'émission de l'épée monte pendant l'anticipation,
    /// puis "claque" en un flash qui retombe au relâchement du coup.
    /// Utilise un MaterialPropertyBlock (pas de duplication de matériau).
    /// L'Update n'est actif que pendant la décroissance du flash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwordGlowFx : MonoBehaviour
    {
        #region Inspector
        [Header("Rendu")]
        [SerializeField] private Renderer[] swordRenderers;
        [Tooltip("Nom de la propriété d'émission du shader (URP Lit : _EmissionColor).")]
        [SerializeField] private string emissionProperty = "_EmissionColor";
        [SerializeField, ColorUsage(false, true)] private Color emissionColor = new Color(0.25f, 0.8f, 3f, 1f);

        [Header("Repos")]
        [Tooltip("Lueur permanente de l épée au repos (s ajoute à la télégraphie).")]
        [SerializeField, Min(0f)] private float idleIntensity = 0.8f;

        [Header("Télégraphie")]
        [SerializeField, Min(0f)] private float maxIntensity = 3f;
        [Tooltip("Intensité en fonction de la progression de la télégraphie (défaut : montée cubique).")]
        [SerializeField] private AnimationCurve windupCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 3f, 3f));

        [Header("Relâchement")]
        [SerializeField, Min(1f)] private float releaseFlashMultiplier = 1.8f;
        [SerializeField, Min(0.01f)] private float releaseDuration = 0.2f;

        [Header("Optionnel")]
        [SerializeField] private ParticleSystem windupParticles;
        #endregion

        private MaterialPropertyBlock propertyBlock; // créé paresseusement (un MPB natif créé dans un initialiseur de champ peut être invalide)
        private int emissionPropertyId;
        private float currentIntensity;
        private float releaseStartIntensity;
        private float releaseTimer;
        private bool isReleasing;
        private bool isInitialized;

        #region Cycle Unity
        private void Awake()
        {
            EnsureInitialized();
            ApplyIntensity(0f); // lueur de repos dès le départ

            // Update inutile au repos : réactivé uniquement pendant le flash de relâchement.
            enabled = false;
        }

        private void Update()
        {
            if (!isReleasing) return;

            releaseTimer += Time.deltaTime;
            float t = Mathf.Clamp01(releaseTimer / releaseDuration);
            float intensity = Mathf.Lerp(releaseStartIntensity, 0f, t);
            ApplyIntensity(intensity);

            if (t >= 1f)
            {
                Cancel();
            }
        }
        #endregion

        #region API
        public void BeginWindup()
        {
            isReleasing = false;
            currentIntensity = 0f;
            ApplyIntensity(0f);

            if (windupParticles != null) windupParticles.Play();
        }

        public void SetWindupProgress(float progress01)
        {
            if (isReleasing) return;

            currentIntensity = windupCurve.Evaluate(Mathf.Clamp01(progress01)) * maxIntensity;
            ApplyIntensity(currentIntensity);
        }

        public void EndWindup()
        {
            if (windupParticles != null) windupParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (currentIntensity <= 0f) return;

            isReleasing = true;
            releaseTimer = 0f;
            releaseStartIntensity = currentIntensity * releaseFlashMultiplier;
            enabled = true;
        }

        /// <summary>Arrêt immédiat : émission remise à zéro, particules coupées.</summary>
        public void Cancel()
        {
            isReleasing = false;
            currentIntensity = 0f;
            ApplyIntensity(0f);

            if (windupParticles != null) windupParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            enabled = false;
        }
        #endregion

        #region Rendu
        // Initialisation paresseuse : sûr même si Cancel() est appelé avant l'Awake (objet inactif).
        private void EnsureInitialized()
        {
            if (isInitialized) return;

            emissionPropertyId = Shader.PropertyToID(emissionProperty);
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            if (swordRenderers == null)
            {
                swordRenderers = System.Array.Empty<Renderer>();
            }

            isInitialized = true;
        }

        private void ApplyIntensity(float intensity)
        {
            EnsureInitialized();

            Color finalColor = emissionColor * (idleIntensity + intensity);

            for (int i = 0; i < swordRenderers.Length; i++)
            {
                Renderer target = swordRenderers[i];
                if (target == null) continue;

                // GetPropertyBlock d'abord : on préserve les autres propriétés déjà posées (ex : dissolve).
                target.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(emissionPropertyId, finalColor);
                target.SetPropertyBlock(propertyBlock);
            }
        }
        #endregion
    }
}
