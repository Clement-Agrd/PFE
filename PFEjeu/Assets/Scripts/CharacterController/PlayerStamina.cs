using System;
using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Gestion générique de l'endurance du joueur.
    ///
    /// Utilisée actuellement pour :
    /// - Sprint
    /// - Roulade
    ///
    /// Peut ensuite être utilisée pour :
    /// - Attaques lourdes
    /// - Parade
    /// - Compétences physiques
    /// - Escalade
    /// etc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStamina : MonoBehaviour
    {
        [Header("Stamina")]
        [SerializeField, Min(1f)]
        private float maxStamina = 100f;

        [SerializeField]
        private bool startFull = true;

        [SerializeField, Min(0f)]
        private float startStamina = 100f;

        [Header("Régénération")]
        [Tooltip("Stamina récupérée par seconde.")]
        [SerializeField, Min(0f)]
        private float regenerationPerSecond = 22f;

        [Tooltip("Temps d'attente après une dépense avant la régénération.")]
        [SerializeField, Min(0f)]
        private float regenerationDelay = 1f;

        [Header("Épuisement")]
        [Tooltip(
            "Quand la stamina atteint 0, le joueur doit récupérer " +
            "au moins cette quantité avant de pouvoir sprinter à nouveau."
        )]
        [SerializeField, Min(0f)]
        private float exhaustionRecoveryThreshold = 20f;

        [Tooltip(
            "Quantité minimale nécessaire pour commencer un sprint " +
            "quand le joueur n'est pas épuisé."
        )]
        [SerializeField, Min(0f)]
        private float minimumToStartSprint = 5f;


        public float MaxStamina => maxStamina;

        public float CurrentStamina { get; private set; }

        public float Normalized =>
            maxStamina > 0f
                ? CurrentStamina / maxStamina
                : 0f;

        public bool IsExhausted { get; private set; }

        public bool IsFull =>
            CurrentStamina >= maxStamina;

        /// <summary>
        /// Peut démarrer un nouveau sprint.
        /// </summary>
        public bool CanStartSprint =>
            !IsExhausted &&
            CurrentStamina >= minimumToStartSprint;

        /// <summary>
        /// Peut continuer un sprint déjà commencé.
        /// </summary>
        public bool CanContinueSprint =>
            !IsExhausted &&
            CurrentStamina > 0f;


        private float _lastSpendTime =
            float.NegativeInfinity;


        /// <summary>
        /// current, max
        /// </summary>
        public event Action<float, float> OnStaminaChanged;

        public event Action OnExhausted;
        public event Action OnRecovered;


        private void Awake()
        {
            maxStamina =
                Mathf.Max(1f, maxStamina);

            exhaustionRecoveryThreshold =
                Mathf.Clamp(
                    exhaustionRecoveryThreshold,
                    0f,
                    maxStamina
                );

            minimumToStartSprint =
                Mathf.Clamp(
                    minimumToStartSprint,
                    0f,
                    maxStamina
                );

            CurrentStamina =
                startFull
                    ? maxStamina
                    : Mathf.Clamp(
                        startStamina,
                        0f,
                        maxStamina
                    );

            IsExhausted =
                CurrentStamina <= 0f;
        }


        private void Update()
        {
            Regenerate();
        }


        private void Regenerate()
        {
            if (CurrentStamina >= maxStamina)
                return;

            if (Time.time <
                _lastSpendTime + regenerationDelay)
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


            // Le joueur était complètement épuisé.
            // Il doit récupérer une certaine quantité
            // avant de pouvoir resprinter.
            if (IsExhausted &&
                CurrentStamina >=
                exhaustionRecoveryThreshold)
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }


        /// <summary>
        /// Dépense une quantité précise de stamina.
        ///
        /// Retourne false si le joueur
        /// n'en possède pas assez.
        ///
        /// Idéal pour :
        /// - roulade
        /// - attaque lourde
        /// - capacité
        /// </summary>
        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
                return true;

            if (CurrentStamina < amount)
                return false;


            _lastSpendTime = Time.time;

            SetCurrentStamina(
                CurrentStamina - amount
            );


            CheckExhaustion();

            return true;
        }


        /// <summary>
        /// Dépense continuellement la stamina.
        ///
        /// Contrairement à TrySpend,
        /// la valeur est simplement ramenée à zéro
        /// si la dépense dépasse la stamina restante.
        ///
        /// Idéal pour le sprint.
        /// </summary>
        public void SpendContinuous(float amount)
        {
            if (amount <= 0f)
                return;

            if (CurrentStamina <= 0f)
                return;


            _lastSpendTime = Time.time;

            SetCurrentStamina(
                CurrentStamina - amount
            );


            CheckExhaustion();
        }


        public void Restore(float amount)
        {
            if (amount <= 0f)
                return;


            SetCurrentStamina(
                CurrentStamina + amount
            );


            if (IsExhausted &&
                CurrentStamina >=
                exhaustionRecoveryThreshold)
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }


        public void RestoreFull()
        {
            SetCurrentStamina(maxStamina);

            if (IsExhausted)
            {
                IsExhausted = false;

                OnRecovered?.Invoke();
            }
        }


        public void SetMaxStamina(
            float value,
            bool restoreToFull = false)
        {
            maxStamina =
                Mathf.Max(1f, value);


            exhaustionRecoveryThreshold =
                Mathf.Clamp(
                    exhaustionRecoveryThreshold,
                    0f,
                    maxStamina
                );


            minimumToStartSprint =
                Mathf.Clamp(
                    minimumToStartSprint,
                    0f,
                    maxStamina
                );


            if (restoreToFull)
            {
                CurrentStamina =
                    maxStamina;
            }
            else
            {
                CurrentStamina =
                    Mathf.Min(
                        CurrentStamina,
                        maxStamina
                    );
            }


            OnStaminaChanged?.Invoke(
                CurrentStamina,
                maxStamina
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
                    maxStamina
                );


            if (Mathf.Approximately(
                previous,
                CurrentStamina))
            {
                return;
            }


            OnStaminaChanged?.Invoke(
                CurrentStamina,
                maxStamina
            );
        }
    }
}