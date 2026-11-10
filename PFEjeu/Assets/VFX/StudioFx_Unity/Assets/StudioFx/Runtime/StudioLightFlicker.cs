using UnityEngine;

namespace StudioFx
{
    /// <summary>Fait vaciller une lumière (torche, feu, forge) avec du bruit de Perlin. Aucune allocation par frame.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class StudioLightFlicker : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float amplitude = 0.35f;
        [SerializeField, Min(0f)] private float speed = 7f;

        private Light targetLight;
        private float baseIntensity;
        private float intensityScale = 1f;
        private float seed;

        private void Awake()
        {
            targetLight = GetComponent<Light>();
            baseIntensity = targetLight.intensity;
            seed = Random.value * 100f;   // chaque torche vacille différemment
        }

        private void Update()
        {
            float noise = Mathf.PerlinNoise(Time.time * speed, seed);      // 0..1
            targetLight.intensity = baseIntensity * intensityScale * (1f + amplitude * (noise - 0.5f) * 2f);
        }

        /// <summary>Utilisé par l'installeur Editor.</summary>
        public void Configure(float flickerAmplitude, float flickerSpeed)
        {
            amplitude = flickerAmplitude;
            speed = flickerSpeed;
        }

        /// <summary>Multiplie l'intensité de base (ex. 0 = éteint). Appelé par StudioFxController.</summary>
        public void SetIntensityScale(float scale)
        {
            intensityScale = Mathf.Max(0f, scale);
        }
    }
}
