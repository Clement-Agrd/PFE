using UnityEngine;

namespace StudioFx
{
    /// <summary>
    /// Poignée de contrôle d'un FX d'ambiance (un prefab = un FX).
    ///   SetIntensity(0..2) : densité des particules + intensité des lumières (ex. pluie légère -> averse).
    ///   SetEmitting(bool)   : démarre / arrête proprement (les particules en vol finissent leur vie).
    /// Les FX à rafales (forge) ne changent pas de densité : seul leur éclairage suit l'intensité.
    ///
    /// FX_xxx (prefab)
    /// ├── Flame / Sparks / Smoke ...   ParticleSystem
    /// └── Light                        Light + StudioLightFlicker (optionnel)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StudioFxController : MonoBehaviour
    {
        [Header("Références (remplies par l'installeur)")]
        [SerializeField] private ParticleSystem[] systems;
        [SerializeField] private StudioLightFlicker[] lights;

        [Header("Réglage")]
        [SerializeField, Range(0f, 2f)] private float intensity = 1f;

        private float[] baseRates;

        #region Cycle de vie Unity
        private void Awake()
        {
            CacheBaseRates();
            ApplyIntensity();
        }

        private void OnValidate()
        {
            if (baseRates != null && isActiveAndEnabled) ApplyIntensity();
        }
        #endregion

        #region API publique
        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp(value, 0f, 2f);
            if (baseRates == null) CacheBaseRates();
            ApplyIntensity();
        }

        public void SetEmitting(bool emitting)
        {
            foreach (ParticleSystem system in systems)
            {
                if (system == null) continue;
                if (emitting) system.Play(false);
                else system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void SetReferences(ParticleSystem[] particleSystems, StudioLightFlicker[] flickers)
        {
            systems = particleSystems;
            lights = flickers;
        }
        #endregion

        private void CacheBaseRates()
        {
            baseRates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
                baseRates[i] = systems[i] != null ? systems[i].emission.rateOverTimeMultiplier : 0f;
        }

        private void ApplyIntensity()
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = baseRates[i] * intensity;
            }
            foreach (StudioLightFlicker flicker in lights)
                if (flicker != null) flicker.SetIntensityScale(intensity);
        }
    }
}
