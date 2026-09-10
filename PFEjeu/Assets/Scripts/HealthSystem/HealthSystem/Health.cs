using System;
using UnityEngine;
using Core.StatsSystem;

namespace Core.HealthSystem
{
    using StatType = EnumStats.StatTypes;

    /// <summary>
    /// Vie d'une entité.
    ///
    /// Si EntityStats est présent :
    /// - Life définit les PV max
    /// - DefensePhysic réduit les dégâts physiques
    /// - DefenseMagic réduit les dégâts magiques
    ///
    /// Le composant fonctionne également sans EntityStats
    /// grâce à maxHealth comme valeur fallback.
    /// </summary>
    public sealed class Health :
        MonoBehaviour,
        IDamageable,
        IHealable
    {
        [Header("Stats")]
        [SerializeField]
        private EntityStats stats;

        [Header("Vie - Fallback")]
        [Tooltip(
            "Utilisé comme PV max uniquement lorsqu'aucun EntityStats n'est présent."
        )]
        [SerializeField, Min(1)]
        private int maxHealth = 100;

        [SerializeField]
        private bool startAtMax = true;

        [SerializeField, Min(0)]
        private int startHealth = 100;

        [Header("Défense")]
        [Tooltip(
            "Constante de la formule de réduction.\n" +
            "Avec 100 : 100 Defense = 50 % de dégâts reçus."
        )]
        [SerializeField, Min(1f)]
        private float defenseConstant = 100f;

        [Tooltip(
            "Dégâts minimum après défense."
        )]
        [SerializeField, Min(0)]
        private int minimumDamage = 1;

        [Header("Invulnérabilité")]
        [SerializeField, Min(0f)]
        private float invulnAfterHitDuration = 0f;

        [Header("Chiffres de dégâts")]
        [SerializeField]
        private bool showDamageNumbers = true;

        [SerializeField, Min(1)]
        private int critThreshold = 30;

        [SerializeField]
        private Color normalColor = Color.white;

        [SerializeField]
        private Color critColor =
            new Color(
                1f,
                0.85f,
                0.2f
            );

        [SerializeField]
        private Color healColor =
            new Color(
                0.4f,
                0.85f,
                0.4f
            );

        public int MaxHealth =>
            maxHealth;

        public int CurrentHealth
        {
            get;
            private set;
        }

        public bool IsDead
        {
            get;
            private set;
        }

        public bool IsInvulnerable
        {
            get;
            set;
        }

        public float Normalized =>
            maxHealth > 0
                ? (float)CurrentHealth /
                  maxHealth
                : 0f;

        private float _invulnUntil;

        public event Action<DamageInfo>
            OnDamaged;

        /// <summary>
        /// DamageInfo original + dégâts réellement reçus.
        /// </summary>
        public event Action<DamageInfo, int>
            OnDamageResolved;

        public event Action<int>
            OnHealed;

        public event Action<int, int>
            OnHealthChanged;

        public event Action OnDeath;

        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            ResolveStats();

            maxHealth =
                CalculateMaxHealth();

            CurrentHealth =
                startAtMax
                    ? maxHealth
                    : Mathf.Clamp(
                        startHealth,
                        0,
                        maxHealth
                    );

            IsDead =
                CurrentHealth <= 0;
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

        // ============================================================
        // DAMAGE
        // ============================================================

        public void TakeDamage(
            in DamageInfo info)
        {
            if (IsDead)
                return;

            if (info.Amount <= 0)
                return;

            if (IsInvulnerable)
                return;

            if (Time.time < _invulnUntil)
                return;

            float multiplier =
                GetDamageMultiplier(
                    info.Type
                );

            int finalDamage =
                Mathf.RoundToInt(
                    info.Amount *
                    multiplier
                );

            if (info.Amount > 0)
            {
                finalDamage =
                    Mathf.Max(
                        minimumDamage,
                        finalDamage
                    );
            }

            if (finalDamage <= 0)
                return;

            CurrentHealth =
                Mathf.Max(
                    0,
                    CurrentHealth -
                    finalDamage
                );

            OnDamaged?.Invoke(
                info
            );

            OnDamageResolved?.Invoke(
                info,
                finalDamage
            );

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );

            bool isCrit =
                finalDamage >=
                critThreshold;

            ShowNumber(
                finalDamage,
                isCrit
                    ? critColor
                    : normalColor,
                "",
                isCrit
            );

            if (invulnAfterHitDuration > 0f)
            {
                _invulnUntil =
                    Time.time +
                    invulnAfterHitDuration;
            }

            if (CurrentHealth == 0)
            {
                Die();
            }
        }

        public void TakeDamage(
            int amount)
        {
            DamageInfo info =
                new DamageInfo(amount);

            TakeDamage(in info);
        }

        /// <summary>
        /// Formule :
        ///
        /// damageMultiplier =
        /// defenseConstant /
        /// (defenseConstant + Defense)
        ///
        /// Exemple avec defenseConstant = 100 :
        ///
        /// Defense 0   => 100 % dégâts
        /// Defense 50  => 66.6 %
        /// Defense 100 => 50 %
        /// Defense 200 => 33.3 %
        /// </summary>
        private float GetDamageMultiplier(
            DamageType type)
        {
            // null = dégâts bruts
            if (type == null ||
                stats == null)
            {
                return 1f;
            }

            StatType defenseStat;

            switch (type.Category)
            {
                case DamageCategory.Physical:

                    defenseStat =
                        StatType.DefensePhysic;

                    break;

                case DamageCategory.Magical:

                    defenseStat =
                        StatType.DefenseMagic;

                    break;

                default:

                    return 1f;
            }

            float defense =
                Mathf.Max(
                    0f,
                    stats.GetStat(
                        defenseStat
                    )
                );

            return defenseConstant /
                   (
                       defenseConstant +
                       defense
                   );
        }

        // ============================================================
        // HEAL
        // ============================================================

        public void Heal(
            int amount)
        {
            if (IsDead ||
                amount <= 0)
            {
                return;
            }

            int oldHealth =
                CurrentHealth;

            CurrentHealth =
                Mathf.Min(
                    maxHealth,
                    CurrentHealth +
                    amount
                );

            int actualHeal =
                CurrentHealth -
                oldHealth;

            if (actualHeal <= 0)
                return;

            OnHealed?.Invoke(
                actualHeal
            );

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );

            ShowNumber(
                actualHeal,
                healColor,
                "+",
                false
            );
        }

        // ============================================================
        // LIFE STAT
        // ============================================================

        private void OnStatChanged(
            StatType type,
            float oldValue,
            float newValue)
        {
            if (type != StatType.Life)
                return;

            int oldMax =
                Mathf.Max(
                    1,
                    maxHealth
                );

            float normalizedHealth =
                (float)CurrentHealth /
                oldMax;

            maxHealth =
                CalculateMaxHealth();

            if (IsDead)
            {
                CurrentHealth = 0;
            }
            else
            {
                CurrentHealth =
                    Mathf.Clamp(
                        Mathf.RoundToInt(
                            normalizedHealth *
                            maxHealth
                        ),
                        0,
                        maxHealth
                    );
            }

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );
        }

        private int CalculateMaxHealth()
        {
            if (stats == null)
            {
                return Mathf.Max(
                    1,
                    maxHealth
                );
            }

            return Mathf.Max(
                1,
                Mathf.RoundToInt(
                    stats.GetStat(
                        StatType.Life
                    )
                )
            );
        }

        /// <summary>
        /// Si EntityStats est présent,
        /// cette fonction modifie la valeur BASE de Life.
        ///
        /// C'est donc un changement permanent,
        /// pas un buff temporaire.
        /// </summary>
        public void SetMaxHealth(
            int value,
            bool healToFull = false)
        {
            value =
                Mathf.Max(
                    1,
                    value
                );

            if (stats != null)
            {
                stats.SetBaseStat(
                    StatType.Life,
                    value
                );

                if (healToFull)
                {
                    CurrentHealth =
                        maxHealth;

                    IsDead = false;

                    OnHealthChanged?.Invoke(
                        CurrentHealth,
                        maxHealth
                    );
                }

                return;
            }

            maxHealth = value;

            CurrentHealth =
                healToFull
                    ? maxHealth
                    : Mathf.Min(
                        CurrentHealth,
                        maxHealth
                    );

            IsDead =
                CurrentHealth <= 0;

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );
        }

        // ============================================================
        // LIFE / DEATH
        // ============================================================

        public void Revive(
            int health = -1)
        {
            IsDead = false;

            CurrentHealth =
                health <= 0
                    ? maxHealth
                    : Mathf.Clamp(
                        health,
                        1,
                        maxHealth
                    );

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );
        }

        public void Kill()
        {
            if (IsDead)
                return;

            CurrentHealth = 0;

            OnHealthChanged?.Invoke(
                0,
                maxHealth
            );

            Die();
        }

        private void Die()
        {
            if (IsDead)
                return;

            IsDead = true;

            OnDeath?.Invoke();
        }

        // ============================================================
        // SAVE
        // ============================================================

        public void Capture(
            out int current,
            out int max)
        {
            current =
                CurrentHealth;

            max =
                maxHealth;
        }

        public void Restore(
            int current,
            int max)
        {
            // Avec EntityStats, le max vient des stats.
            //
            // Les stats de base devront idéalement être
            // sauvegardées séparément dans ton futur SaveSystem.
            if (stats != null)
            {
                maxHealth =
                    CalculateMaxHealth();
            }
            else
            {
                maxHealth =
                    Mathf.Max(
                        1,
                        max
                    );
            }

            CurrentHealth =
                Mathf.Clamp(
                    current,
                    0,
                    maxHealth
                );

            IsDead =
                CurrentHealth <= 0;

            OnHealthChanged?.Invoke(
                CurrentHealth,
                maxHealth
            );
        }

        // ============================================================
        // DAMAGE NUMBERS
        // ============================================================

        private void ShowNumber(
            int amount,
            Color color,
            string prefix = "",
            bool isCrit = false)
        {
            if (!showDamageNumbers)
                return;

            if (SimpleDamageSpawner.Instance == null)
                return;

            SimpleDamageSpawner.Instance.Show(
                transform.position,
                amount,
                prefix + amount,
                color,
                isCrit
            );
        }
    }
}