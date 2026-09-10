using System;
using UnityEngine;
using Core.StatsSystem;

namespace ProfessionalTPS
{
    using StatType = EnumStats.StatTypes;

    [DisallowMultipleComponent]
    public sealed class PlayerStamina :
        MonoBehaviour
    {
        [Header("Stats")]
        [SerializeField]
        private EntityStats stats;

        [Header("Fallback")]
        [Tooltip(
            "Utilisé uniquement lorsqu'aucun EntityStats n'est présent."
        )]
        [SerializeField, Min(1f)]
        private float fallbackMaxStamina = 100f;

        [Header("Starting Stamina")]
        [SerializeField]
        private bool startFull = true;

        [SerializeField, Min(0f)]
        private float startStamina = 100f;

        [Header("Regeneration")]
        [SerializeField, Min(0f)]
        private float regenerationPerSecond = 22f;

        [SerializeField, Min(0f)]
        private float regenerationDelay = 1f;

        [Header("Exhaustion")]
        [SerializeField, Min(0f)]
        private float exhaustionRecoveryThreshold = 20f;

        [SerializeField, Min(0f)]
        private float minimumToStartSprint = 5f;

        public float MaxStamina
        {
            get
            {
                if (stats == null)
                {
                    return Mathf.Max(
                        1f,
                        fallbackMaxStamina
                    );
                }

                return Mathf.Max(
                    1f,
                    stats.GetStat(
                        StatType.Stamina
                    )
                );
            }
        }

        public float CurrentStamina
        {
            get;
            private set;
        }

        public float Normalized =>
            MaxStamina > 0f
                ? CurrentStamina /
                  MaxStamina
                : 0f;

        public bool IsExhausted
        {
            get;
            private set;
        }

        public bool IsFull =>
            CurrentStamina >=
            MaxStamina;

        public bool CanStartSprint =>
            !IsExhausted &&
            CurrentStamina >=
            minimumToStartSprint;

        public bool CanContinueSprint =>
            !IsExhausted &&
            CurrentStamina > 0f;

        private float _lastSpendTime =
            float.NegativeInfinity;

        public event Action<float, float>
            OnStaminaChanged;

        public event Action OnExhausted;

        public event Action OnRecovered;

        private void Awake()
        {
            ResolveStats();

            CurrentStamina =
                startFull
                    ? MaxStamina
                    : Mathf.Clamp(
                        startStamina,
                        0f,
                        MaxStamina
                    );

            IsExhausted =
                CurrentStamina <= 0f;
        }

        private void OnEnable()
        {
            ResolveStats();

            if (stats != null)
            {
                stats.OnStatChanged +=
                    OnStatChanged;
            }
        }

        private void OnDisable()
        {
            if (stats != null)
            {
                stats.OnStatChanged -=
                    OnStatChanged;
            }
        }

        private void Update()
        {
            if (CurrentStamina >
                MaxStamina)
            {
                SetCurrentStamina(
                    MaxStamina
                );
            }

            Regenerate();
        }

        private void ResolveStats()
        {
            if (stats != null)
                return;

            stats =
                GetComponent<EntityStats>();

            if (stats == null)
            {
                stats =
                    GetComponentInParent<EntityStats>();
            }
        }

        private void OnStatChanged(
            StatType type,
            float oldValue,
            float newValue)
        {
            if (type !=
                StatType.Stamina)
            {
                return;
            }

            float oldMax =
                Mathf.Max(
                    1f,
                    oldValue
                );

            float newMax =
                Mathf.Max(
                    1f,
                    newValue
                );

            float normalized =
                CurrentStamina /
                oldMax;

            CurrentStamina =
                Mathf.Clamp(
                    normalized *
                    newMax,
                    0f,
                    newMax
                );

            if (IsExhausted &&
                CurrentStamina >=
                Mathf.Min(
                    exhaustionRecoveryThreshold,
                    newMax
                ))
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }

            OnStaminaChanged?.Invoke(
                CurrentStamina,
                newMax
            );
        }

        private void Regenerate()
        {
            if (CurrentStamina >=
                MaxStamina)
            {
                return;
            }

            if (Time.time <
                _lastSpendTime +
                regenerationDelay)
            {
                return;
            }

            if (regenerationPerSecond <= 0f)
                return;

            SetCurrentStamina(
                CurrentStamina +
                regenerationPerSecond *
                Time.deltaTime
            );

            if (IsExhausted &&
                CurrentStamina >=
                Mathf.Min(
                    exhaustionRecoveryThreshold,
                    MaxStamina
                ))
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }

        public bool TrySpend(
            float amount)
        {
            if (amount <= 0f)
                return true;

            if (CurrentStamina <
                amount)
            {
                return false;
            }

            _lastSpendTime =
                Time.time;

            SetCurrentStamina(
                CurrentStamina -
                amount
            );

            CheckExhaustion();

            return true;
        }

        public void SpendContinuous(
            float amount)
        {
            if (amount <= 0f)
                return;

            if (CurrentStamina <= 0f)
                return;

            _lastSpendTime =
                Time.time;

            SetCurrentStamina(
                CurrentStamina -
                amount
            );

            CheckExhaustion();
        }

        public void Restore(
            float amount)
        {
            if (amount <= 0f)
                return;

            SetCurrentStamina(
                CurrentStamina +
                amount
            );

            if (IsExhausted &&
                CurrentStamina >=
                Mathf.Min(
                    exhaustionRecoveryThreshold,
                    MaxStamina
                ))
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }

        public void RestoreFull()
        {
            SetCurrentStamina(
                MaxStamina
            );

            if (IsExhausted)
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }

        /// <summary>
        /// Avec EntityStats :
        /// modifie la valeur BASE de Stamina.
        /// </summary>
        public void SetMaxStamina(
            float value,
            bool restoreToFull = false)
        {
            value =
                Mathf.Max(
                    1f,
                    value
                );

            if (stats != null)
            {
                stats.SetBaseStat(
                    StatType.Stamina,
                    value
                );

                if (restoreToFull)
                {
                    RestoreFull();
                }

                return;
            }

            fallbackMaxStamina =
                value;

            CurrentStamina =
                restoreToFull
                    ? MaxStamina
                    : Mathf.Min(
                        CurrentStamina,
                        MaxStamina
                    );

            OnStaminaChanged?.Invoke(
                CurrentStamina,
                MaxStamina
            );
        }

        private void CheckExhaustion()
        {
            if (CurrentStamina > 0f)
                return;

            if (IsExhausted)
                return;

            IsExhausted = true;

            OnExhausted?.Invoke();
        }

        private void SetCurrentStamina(
            float value)
        {
            float previous =
                CurrentStamina;

            CurrentStamina =
                Mathf.Clamp(
                    value,
                    0f,
                    MaxStamina
                );

            if (Mathf.Approximately(
                    previous,
                    CurrentStamina))
            {
                return;
            }

            OnStaminaChanged?.Invoke(
                CurrentStamina,
                MaxStamina
            );
        }
    }
}