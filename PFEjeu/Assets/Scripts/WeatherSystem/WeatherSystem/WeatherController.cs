using System;
using System.Collections;
using UnityEngine;

namespace Core.WeatherSystem
{
    /// <summary>
    /// Gère la météo courante : change d'état en fondu (brouillard mélangé
    /// progressivement), échange les particules, expose le vent et un event.
    /// </summary>
    public sealed class WeatherController : MonoBehaviour
    {
        [SerializeField] private WeatherProfile startWeather;

        [Tooltip("Parent des particules (ex. la caméra ou le joueur, pour qu'elles suivent). Vide = ce GameObject.")]
        [SerializeField] private Transform particleParent;

        [SerializeField, Min(0f)] private float defaultTransition = 2f;

        public WeatherProfile Current { get; private set; }
        public float WindStrength => Current != null ? Current.WindStrength : 0f;

        public event Action<WeatherProfile> OnWeatherChanged;

        private GameObject _currentParticles;
        private Coroutine _transition;

        private void Start()
        {
            if (startWeather != null) SetWeather(startWeather, 0f);
        }

        /// <summary>Passe à une nouvelle météo, en fondu sur 'transitionSeconds' (-1 = défaut).</summary>
        public void SetWeather(WeatherProfile profile, float transitionSeconds = -1f)
        {
            if (profile == null || profile == Current) return;
            if (transitionSeconds < 0f) transitionSeconds = defaultTransition;

            SwapParticles(profile);

            WeatherProfile from = Current;
            Current = profile;

            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(BlendFog(from, profile, transitionSeconds));

            OnWeatherChanged?.Invoke(profile);
        }

        private IEnumerator BlendFog(WeatherProfile from, WeatherProfile to, float duration)
        {
            float fromDensity = (from != null && from.UseFog) ? from.FogDensity : 0f;
            Color fromColor = (from != null) ? from.FogColor : to.FogColor;
            float toDensity = to.UseFog ? to.FogDensity : 0f;

            // Le brouillard doit être actif pendant tout le fondu (entrée OU sortie).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            if (duration > 0f)
            {
                float t = 0f;
                while (t < duration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / duration);
                    RenderSettings.fogDensity = Mathf.Lerp(fromDensity, toDensity, k);
                    RenderSettings.fogColor = Color.Lerp(fromColor, to.FogColor, k);
                    yield return null;
                }
            }

            ApplyFinalFog(to);
            _transition = null;
        }

        private static void ApplyFinalFog(WeatherProfile p)
        {
            RenderSettings.fog = p.UseFog;
            if (p.UseFog)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = p.FogColor;
                RenderSettings.fogDensity = p.FogDensity;
            }
        }

        private void SwapParticles(WeatherProfile profile)
        {
            if (_currentParticles != null) Destroy(_currentParticles);

            if (profile.ParticlePrefab != null)
            {
                Transform parent = particleParent != null ? particleParent : transform;
                _currentParticles = Instantiate(profile.ParticlePrefab, parent);
                _currentParticles.transform.localPosition = Vector3.zero;
            }
        }
    }
}
