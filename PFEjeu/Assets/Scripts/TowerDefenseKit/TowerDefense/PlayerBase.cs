using System;
using UnityEngine;

namespace Core.TowerDefense
{
    /// <summary>
    /// La base à défendre : des PV qui baissent quand un ennemi l'atteint.
    /// À 0, la partie est perdue (OnDestroyed).
    /// </summary>
    public sealed class PlayerBase : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 20;

        public int Health { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsAlive => Health > 0;

        public event Action<int, int> OnHealthChanged; // (current, max)
        public event Action OnDestroyed;

        private void Awake() => Health = maxHealth;

        /// <summary>Inflige des dégâts à la base (appelé quand un ennemi arrive au bout).</summary>
        public void TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0) return;

            Health = Mathf.Max(0, Health - amount);
            OnHealthChanged?.Invoke(Health, maxHealth);

            if (Health == 0)
                OnDestroyed?.Invoke();
        }

        public void ResetBase()
        {
            Health = maxHealth;
            OnHealthChanged?.Invoke(Health, maxHealth);
        }
    }
}
