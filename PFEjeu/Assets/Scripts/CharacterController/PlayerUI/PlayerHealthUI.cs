using System.Collections.Generic;
using UnityEngine;

using Core.HealthSystem;

namespace ProfessionalTPS.UI
{
    public sealed class PlayerHealthUI :
        MonoBehaviour
    {
        [Header("References")]

        [SerializeField]
        private Health health;

        [SerializeField]
        private Transform heartsContainer;

        [SerializeField]
        private HeartUI heartPrefab;


        [Header("Heart Settings")]

        [Tooltip(
            "Nombre de PV représenté par un cœur."
        )]
        [SerializeField, Min(1)]
        private int healthPerHeart = 25;


        private readonly List<HeartUI>
            _hearts =
                new List<HeartUI>();


        private int _lastMaxHealth = -1;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            if (health == null)
            {
                health =
                    FindFirstObjectByType<Health>();
            }
        }


        private void OnEnable()
        {
            if (health == null)
                return;


            health.OnHealthChanged +=
                OnHealthChanged;
        }


        private void OnDisable()
        {
            if (health == null)
                return;


            health.OnHealthChanged -=
                OnHealthChanged;
        }


        private void Start()
        {
            if (health == null)
                return;


            Refresh(
                health.CurrentHealth,
                health.MaxHealth
            );
        }


        // ============================================================
        // HEALTH
        // ============================================================

        private void OnHealthChanged(
            int current,
            int max)
        {
            Refresh(
                current,
                max
            );
        }


        private void Refresh(
            int currentHealth,
            int maxHealth)
        {
            if (maxHealth <= 0)
                return;


            if (_lastMaxHealth !=
                maxHealth)
            {
                RebuildHearts(
                    maxHealth
                );

                _lastMaxHealth =
                    maxHealth;
            }


            for (int i = 0;
                 i < _hearts.Count;
                 i++)
            {
                int heartStartHealth =
                    i *
                    healthPerHeart;


                // Normalement 25.
                // Mais ça permet aussi de gérer
                // un max du genre 110 HP proprement.
                int heartCapacity =
                    Mathf.Min(
                        healthPerHeart,
                        maxHealth -
                        heartStartHealth
                    );


                int healthInsideHeart =
                    Mathf.Clamp(
                        currentHealth -
                        heartStartHealth,
                        0,
                        heartCapacity
                    );


                float fill =
                    heartCapacity > 0
                        ? (float)healthInsideHeart /
                          heartCapacity
                        : 0f;


                _hearts[i].SetFill(
                    fill
                );
            }
        }


        // ============================================================
        // HEART GENERATION
        // ============================================================

        private void RebuildHearts(
            int maxHealth)
        {
            ClearHearts();


            if (heartPrefab == null ||
                heartsContainer == null)
            {
                Debug.LogWarning(
                    "[PlayerHealthUI] HeartPrefab ou HeartsContainer manquant.",
                    this
                );

                return;
            }


            int heartCount =
                Mathf.CeilToInt(
                    maxHealth /
                    (float)healthPerHeart
                );


            for (int i = 0;
                 i < heartCount;
                 i++)
            {
                HeartUI heart =
                    Instantiate(
                        heartPrefab,
                        heartsContainer
                    );


                _hearts.Add(
                    heart
                );
            }
        }


        private void ClearHearts()
        {
            for (int i = 0;
                 i < _hearts.Count;
                 i++)
            {
                if (_hearts[i] != null)
                {
                    Destroy(
                        _hearts[i].gameObject
                    );
                }
            }


            _hearts.Clear();
        }
    }
}