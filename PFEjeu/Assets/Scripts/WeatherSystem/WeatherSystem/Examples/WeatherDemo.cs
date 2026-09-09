using UnityEngine;

namespace Core.WeatherSystem.Examples
{
    /// <summary>
    /// Démo : 1 = dégagé, 2 = pluie, 3 = brouillard. Logge les changements.
    /// Pose ce composant à côté d'un WeatherController.
    /// </summary>
    [RequireComponent(typeof(WeatherController))]
    public sealed class WeatherDemo : MonoBehaviour
    {
        [SerializeField] private WeatherProfile clear;
        [SerializeField] private WeatherProfile rain;
        [SerializeField] private WeatherProfile fog;

        private WeatherController _controller;

        private void Awake() => _controller = GetComponent<WeatherController>();

        private void OnEnable() => _controller.OnWeatherChanged += HandleChanged;
        private void OnDisable() => _controller.OnWeatherChanged -= HandleChanged;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) && clear != null) _controller.SetWeather(clear);
            if (Input.GetKeyDown(KeyCode.Alpha2) && rain != null) _controller.SetWeather(rain);
            if (Input.GetKeyDown(KeyCode.Alpha3) && fog != null) _controller.SetWeather(fog);
        }

        private void HandleChanged(WeatherProfile weather)
            => Debug.Log($"[Weather] Météo : {weather.DisplayName} (vent {weather.WindStrength}).");
    }
}
