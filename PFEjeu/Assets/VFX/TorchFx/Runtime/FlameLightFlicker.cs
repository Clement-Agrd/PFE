using UnityEngine;

namespace TorchFx
{
    /// <summary>
    /// Module de lumière : une Light réelle pulse avec la flamme (même bruit), change légèrement de teinte
    /// et bouge de quelques centimètres. C'est elle qui "colle" la flamme stylisée à une torche réaliste :
    /// le métal, le bois et les murs autour réagissent à la lumière.
    ///
    /// Attention : ce script déplace la Light en espace monde autour de la base de la flamme.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlameLightFlicker : MonoBehaviour, IFlameModule
    {
        #region Inspector
        [Header("Lumière")]
        [SerializeField] private Light flameLight;
        [SerializeField, Min(0f)] private float baseIntensity = 4f;
        [SerializeField, Min(0.1f)] private float baseRange = 7f;
        [Tooltip("Part de la variation d'intensité due au scintillement (0 = fixe).")]
        [SerializeField, Range(0f, 1f)] private float flickerAmplitude = 0.3f;
        [Tooltip("Teinte quand la flamme est à son maximum.")]
        [SerializeField, ColorUsage(false)] private Color warmColor = new Color(1f, 0.62f, 0.22f);
        [Tooltip("Teinte quand la flamme est à son minimum.")]
        [SerializeField, ColorUsage(false)] private Color coolColor = new Color(1f, 0.38f, 0.1f);

        [Header("Mouvement")]
        [Tooltip("Hauteur de la lumière au-dessus de la base de la flamme (centre visuel de la flamme).")]
        [SerializeField] private float heightOffset = 0.12f;
        [SerializeField, Min(0f)] private float positionJitter = 0.03f;
        [SerializeField, Min(0f)] private float jitterFrequency = 2.4f;

        [Header("Ombres")]
        [Tooltip("Les ombres portées d'une lumière qui bouge coûtent cher : à activer seulement sur les torches proches du joueur.")]
        [SerializeField] private LightShadows shadows = LightShadows.None;
        #endregion

        private float noiseSeed;
        private Transform lightTransform;

        #region Cycle Unity
        private void Reset()
        {
            flameLight = GetComponentInChildren<Light>();
        }

        private void Awake()
        {
            noiseSeed = Random.value * 100f;

            if (flameLight == null) return;

            lightTransform = flameLight.transform;
            flameLight.shadows = shadows;
        }
        #endregion

        #region IFlameModule
        public void Tick(in FlameState state)
        {
            if (flameLight == null) return;

            bool isOn = state.Intensity01 > 0.001f;
            if (flameLight.enabled != isOn) flameLight.enabled = isOn;
            if (!isOn) return;

            flameLight.intensity = baseIntensity * state.Intensity01 * (1f + state.Flicker * flickerAmplitude);
            flameLight.range = baseRange * Mathf.Lerp(0.6f, 1f, state.Intensity01);
            flameLight.color = Color.Lerp(coolColor, warmColor, 0.5f + 0.5f * state.Flicker);

            float t = state.Time * jitterFrequency;
            Vector3 jitter = new Vector3(
                Mathf.PerlinNoise(noiseSeed, t) - 0.5f,
                Mathf.PerlinNoise(noiseSeed + 9.1f, t) - 0.5f,
                Mathf.PerlinNoise(noiseSeed + 21.7f, t) - 0.5f) * (2f * positionJitter);

            lightTransform.position = state.Position + Vector3.up * heightOffset + jitter;
        }
        #endregion
    }
}
