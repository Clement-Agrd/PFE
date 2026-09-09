using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.WeatherSystem
{
    /// <summary>
    /// Fait évoluer la météo toute seule : choisit un nouvel état à intervalle
    /// aléatoire, par tirage pondéré (météos fréquentes / rares).
    /// </summary>
    public sealed class WeatherScheduler : MonoBehaviour
    {
        [Serializable]
        private struct WeatherOption
        {
            public WeatherProfile weather;
            [Min(0f)] public float weight;
        }

        [SerializeField] private WeatherController controller;
        [SerializeField] private List<WeatherOption> options = new();

        [Tooltip("Durée min/max (s) avant de changer de météo.")]
        [SerializeField] private Vector2 durationRange = new Vector2(20f, 60f);

        [SerializeField, Min(0f)] private float transition = 4f;

        private float _timer;

        private void Start() => _timer = NextDuration();

        private void Update()
        {
            if (controller == null || options.Count == 0) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                WeatherProfile next = PickWeighted();
                if (next != null) controller.SetWeather(next, transition);
                _timer = NextDuration();
            }
        }

        private float NextDuration() => UnityEngine.Random.Range(durationRange.x, durationRange.y);

        private WeatherProfile PickWeighted()
        {
            float total = 0f;
            for (int i = 0; i < options.Count; i++) total += Mathf.Max(0f, options[i].weight);
            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < options.Count; i++)
            {
                float w = Mathf.Max(0f, options[i].weight);
                if (roll < w) return options[i].weather;
                roll -= w;
            }
            return null;
        }
    }
}
