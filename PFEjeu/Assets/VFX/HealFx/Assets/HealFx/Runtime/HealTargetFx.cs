using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// FX posé sur une cible soignée (allié ou ennemi) : croix qui montent + fines lueurs qui s'élèvent en spirale autour du corps,
    /// avec en option l'anneau au sol et les étincelles de la v1.
    /// À instancier en enfant de l'unité (pivot aux pieds). Deux usages :
    ///   - soin ponctuel : le gameplay appelle <see cref="Pulse"/> à chaque soin reçu (anneau + gerbe de croix) ;
    ///   - soin continu  : <see cref="BeginHeal"/> / <see cref="EndHeal"/> font monter puis retomber les lueurs et les croix tant que le soin dure.
    ///
    /// HealPulse (prefab)
    /// ├── Ring        ParticleSystem (T_HealRing, horizontal)
    /// ├── Crosses     ParticleSystem (croix qui montent)
    /// ├── Sparkles    ParticleSystem (burst d'étincelles)
    /// └── Motes       ParticleSystem (lueurs en spirale autour du corps)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealTargetFx : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Références (remplies par l'installeur)")]
        [SerializeField] private HealFxPalette palette;
        [SerializeField] private ParticleSystem ringParticles;
        [SerializeField] private ParticleSystem crossParticles;
        [SerializeField] private ParticleSystem sparkleParticles;
        [SerializeField] private ParticleSystem moteParticles;

        [Header("Réglages")]
        [SerializeField, Min(0f)] private float intensity = 0.9f;
        [SerializeField, Min(0)] private int crossBurst = 5;
        [SerializeField, Min(0)] private int sparkleBurst = 4;

        [Header("Couches de Pulse()")]
        [Tooltip("Anneau qui s'élargit au sol à chaque Pulse().")]
        [SerializeField] private bool useRing = true;
        [Tooltip("Étincelles à chaque Pulse().")]
        [SerializeField] private bool useSparkles = true;

        [Header("Soin continu")]
        [SerializeField, Min(0.05f)] private float fadeInTime = 0.5f;
        [SerializeField, Min(0.05f)] private float fadeOutTime = 0.9f;
        [Tooltip("Lueurs en spirale émises par seconde tant que le soin dure (0 = aucune).")]
        [SerializeField, Min(0f)] private float moteRate = 26f;
        [Tooltip("Croix émises par seconde tant que le soin dure (0 = seulement à chaque Pulse()).")]
        [SerializeField, Min(0f)] private float healingCrossRate = 4f;

        private MaterialPropertyBlock block;
        private bool healing;
        private float healFade;   // 0 = repos, 1 = soin à pleine intensité

        public bool IsHealing => healing;

        #region Cycle de vie Unity
        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ApplyPalette();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) ApplyPalette();
        }

        private void Update()
        {
            float target = healing ? 1f : 0f;
            float duration = healing ? fadeInTime : fadeOutTime;
            healFade = Mathf.MoveTowards(healFade, target, Time.deltaTime / duration);

            SetRate(crossParticles, healingCrossRate * healFade);
            SetRate(moteParticles, moteRate * healFade);
        }
        #endregion

        #region API publique
        /// <summary>Un soin vient d'être reçu.</summary>
        public void Pulse()
        {
            if (useRing && ringParticles != null) ringParticles.Emit(1);
            if (crossParticles != null) crossParticles.Emit(crossBurst);
            if (useSparkles && sparkleParticles != null) sparkleParticles.Emit(sparkleBurst);
        }

        /// <summary>Début d'un soin continu : lueurs et croix montent progressivement.</summary>
        public void BeginHeal()
        {
            healing = true;
        }

        /// <summary>Fin du soin continu : l'émission s'éteint en douceur (les particules déjà nées finissent leur vie).</summary>
        public void EndHeal()
        {
            healing = false;
        }

        public void SetPalette(HealFxPalette newPalette)
        {
            palette = newPalette;
            ApplyPalette();
        }

        /// <summary>Intensité HDR des particules de la cible (réglage en direct).</summary>
        public void SetIntensity(float newIntensity)
        {
            intensity = Mathf.Max(0f, newIntensity);
            ApplyPalette();
        }

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void SetReferences(HealFxPalette targetPalette, ParticleSystem ring, ParticleSystem crosses, ParticleSystem sparkles)
        {
            palette = targetPalette;
            ringParticles = ring;
            crossParticles = crosses;
            sparkleParticles = sparkles;
        }

        /// <summary>Utilisé par l'installeur Editor : câble les lueurs en spirale.</summary>
        public void SetMoteReference(ParticleSystem motes)
        {
            moteParticles = motes;
        }
        #endregion

        #region Application des réglages
        private void ApplyPalette()
        {
            if (block == null) block = new MaterialPropertyBlock();
            Tint(ringParticles, palette != null ? palette.Primary : Color.white);
            Tint(crossParticles, palette != null ? palette.Primary : Color.white);
            Tint(sparkleParticles, palette != null ? palette.Secondary : Color.white);
            Tint(moteParticles, palette != null ? palette.Primary : Color.white);
        }

        private void Tint(ParticleSystem system, Color color)
        {
            if (system == null) return;

            ParticleSystem.MainModule main = system.main;
            main.startColor = color;

            var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null) return;
            particleRenderer.GetPropertyBlock(block);
            block.SetFloat(IntensityId, intensity);
            particleRenderer.SetPropertyBlock(block);
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = rate;
        }
        #endregion
    }
}
