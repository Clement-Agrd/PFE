using System;
using UnityEngine;

namespace Core.HealthSystem
{
    /// <summary>
    /// Points de vie d'une entité : dégâts (avec type & résistances), soin,
    /// invulnérabilité temporaire (i-frames), mort/réanimation, et events.
    /// Implémente IDamageable / IHealable → cible universelle des systèmes offensifs.
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable, IHealable
    {
        [Header("Vie")]
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private bool startAtMax = true;
        [SerializeField, Min(0)] private int startHealth = 100;

        [Header("Invulnérabilité")]
        [Tooltip("Durée d'invulnérabilité après un coup (i-frames). 0 = aucune.")]
        [SerializeField, Min(0f)] private float invulnAfterHitDuration = 0f;

        [Header("Résistances (par type de dégâts)")]
        [SerializeField] private Resistance[] resistances;

        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary>Invulnérabilité manuelle (ex. pendant un dash). Cumulée aux i-frames.</summary>
        public bool IsInvulnerable { get; set; }

        /// <summary>PV en 0 → 1, pour une barre de vie.</summary>
        public float Normalized => maxHealth > 0 ? (float)CurrentHealth / maxHealth : 0f;

        private float _invulnUntil;

        public event Action<DamageInfo> OnDamaged;
        public event Action<int> OnHealed;
        public event Action<int, int> OnHealthChanged; // (current, max)
        public event Action OnDeath;

        private void Awake()
        {
            CurrentHealth = startAtMax ? maxHealth : Mathf.Clamp(startHealth, 0, maxHealth);
            IsDead = CurrentHealth <= 0;
        }

        #region Dégâts / soin

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead || info.Amount <= 0) return;
            if (IsInvulnerable || Time.time < _invulnUntil) return;

            int finalDamage = Mathf.RoundToInt(info.Amount * GetMultiplier(info.Type));
            if (finalDamage <= 0) return; // immunisé ou absorbé

            CurrentHealth = Mathf.Max(0, CurrentHealth - finalDamage);
            OnDamaged?.Invoke(info);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (invulnAfterHitDuration > 0f)
                _invulnUntil = Time.time + invulnAfterHitDuration;

            if (CurrentHealth == 0) Die();
        }

        /// <summary>Surcharge pratique pour des dégâts bruts (sans type ni source).</summary>
        public void TakeDamage(int amount) => TakeDamage(new DamageInfo(amount));

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            OnHealed?.Invoke(amount);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        #endregion

        #region Vie / mort

        public void SetMaxHealth(int value, bool healToFull = false)
        {
            maxHealth = Mathf.Max(1, value);
            CurrentHealth = healToFull ? maxHealth : Mathf.Min(CurrentHealth, maxHealth);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void Revive(int health = -1)
        {
            IsDead = false;
            CurrentHealth = health <= 0 ? maxHealth : Mathf.Clamp(health, 1, maxHealth);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void Kill()
        {
            if (IsDead) return;
            CurrentHealth = 0;
            OnHealthChanged?.Invoke(0, maxHealth);
            Die();
        }

        private void Die()
        {
            IsDead = true;
            OnDeath?.Invoke();
        }

        #endregion

        #region Sauvegarde

        public void Capture(out int current, out int max)
        {
            current = CurrentHealth;
            max = maxHealth;
        }

        public void Restore(int current, int max)
        {
            maxHealth = Mathf.Max(1, max);
            CurrentHealth = Mathf.Clamp(current, 0, maxHealth);
            IsDead = CurrentHealth <= 0;
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        #endregion

        private float GetMultiplier(DamageType type)
        {
            if (type == null || resistances == null) return 1f;

            for (int i = 0; i < resistances.Length; i++)
                if (resistances[i].type == type)
                    return Mathf.Max(0f, resistances[i].multiplier);

            return 1f;
        }
    }
}
