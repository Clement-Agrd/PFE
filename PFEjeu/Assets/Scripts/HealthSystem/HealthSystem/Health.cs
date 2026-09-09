using System;
using UnityEngine;

namespace Core.HealthSystem
{
    public sealed class Health : MonoBehaviour, IDamageable, IHealable
    {
        [Header("Vie")]
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private bool startAtMax = true;
        [SerializeField, Min(0)] private int startHealth = 100;

        [Header("Invulnérabilité")]
        [SerializeField, Min(0f)] private float invulnAfterHitDuration = 0f;

        [Header("Résistances (par type de dégâts)")]
        [SerializeField] private Resistance[] resistances;

        [Header("Chiffres de dégâts")]
        [SerializeField] private bool showDamageNumbers = true;
        [SerializeField, Min(1)] private int critThreshold = 30;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color critColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color healColor = new Color(0.4f, 0.85f, 0.4f);

        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsInvulnerable { get; set; }
        public float Normalized => maxHealth > 0 ? (float)CurrentHealth / maxHealth : 0f;

        private float _invulnUntil;

        public event Action<DamageInfo> OnDamaged;
        public event Action<int> OnHealed;
        public event Action<int, int> OnHealthChanged;
        public event Action OnDeath;

        private void Awake()
        {
            CurrentHealth = startAtMax ? maxHealth : Mathf.Clamp(startHealth, 0, maxHealth);
            IsDead = CurrentHealth <= 0;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsDead || info.Amount <= 0) return;
            if (IsInvulnerable || Time.time < _invulnUntil) return;

            int finalDamage = Mathf.RoundToInt(info.Amount * GetMultiplier(info.Type));
            if (finalDamage <= 0) return;

            CurrentHealth = Mathf.Max(0, CurrentHealth - finalDamage);
            OnDamaged?.Invoke(info);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

            bool isCrit = finalDamage >= critThreshold;
            ShowNumber(finalDamage, isCrit ? critColor : normalColor, "", isCrit);

            if (invulnAfterHitDuration > 0f)
                _invulnUntil = Time.time + invulnAfterHitDuration;

            if (CurrentHealth == 0) Die();
        }

        public void TakeDamage(int amount) => TakeDamage(new DamageInfo(amount));

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            OnHealed?.Invoke(amount);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            ShowNumber(amount, healColor, "+", false);
        }

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

        private void ShowNumber(int amount, Color color, string prefix = "", bool isCrit = false)
        {
            if (!showDamageNumbers) return;
            if (SimpleDamageSpawner.Instance != null)
                SimpleDamageSpawner.Instance.Show(transform.position, amount, prefix + amount, color, isCrit);
        }
        

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