using System;
using UnityEngine;
using Core.StatsSystem;

namespace Core.HealthSystem
{
    using StatType = EnumStats.StatTypes;

    public sealed class Health :
        MonoBehaviour,
        IDamageable,
        IHealable
    {
        // ============================================================
        // STATS
        // ============================================================

        [Header("Stats")]

        [SerializeField]
        private EntityStats stats;


        // ============================================================
        // LIFE
        // ============================================================

        [Header("Vie - Fallback")]

        [Tooltip(
            "Utilisé uniquement si aucun EntityStats n'est présent."
        )]
        [SerializeField, Min(1)]
        private int maxHealth = 100;

        [SerializeField]
        private bool startAtMax = true;

        [SerializeField, Min(0)]
        private int startHealth = 100;


        // ============================================================
        // DEFENSE
        // ============================================================

        [Header("Défense")]

        [Tooltip(
            "100 signifie que 100 points de défense réduisent les dégâts de moitié."
        )]
        [SerializeField, Min(1f)]
        private float defenseConstant = 100f;

        [SerializeField, Min(0)]
        private int minimumDamage = 1;


        // ============================================================
        // INVULNERABILITY
        // ============================================================

        [Header("Invulnérabilité")]

        [SerializeField, Min(0f)]
        private float invulnAfterHitDuration = 0f;


        // ============================================================
        // DAMAGE NUMBERS
        // ============================================================

        [Header("Chiffres de dégâts")]

        [SerializeField]
        private bool showDamageNumbers = true;

        [SerializeField, Min(1)]
        private int critThreshold = 30;

        [SerializeField]
        private Color normalColor =
            Color.white;

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


        // ============================================================
        // PUBLIC
        // ============================================================

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


        // ============================================================
        // PRIVATE
        // ============================================================

        private float _invulnUntil;

        private IDamageFilter[] _damageFilters =
            Array.Empty<IDamageFilter>();


        // ============================================================
        // EVENTS
        // ============================================================

        public event Action<DamageInfo>
            OnDamaged;

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

            CacheDamageFilters();

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


        // ============================================================
        // FILTERS
        // ============================================================

        private void CacheDamageFilters()
        {
            MonoBehaviour[] behaviours =
                GetComponents<MonoBehaviour>();

            int count = 0;

            for (int i = 0;
                 i < behaviours.Length;
                 i++)
            {
                if (behaviours[i]
                    is IDamageFilter)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                _damageFilters =
                    Array.Empty<IDamageFilter>();

                return;
            }

            _damageFilters =
                new IDamageFilter[count];

            int index = 0;

            for (int i = 0;
                 i < behaviours.Length;
                 i++)
            {
                if (behaviours[i]
                    is IDamageFilter filter)
                {
                    _damageFilters[index] =
                        filter;

                    index++;
                }
            }
        }


        private bool PassesDamageFilters(
            in DamageInfo info)
        {
            for (int i = 0;
                 i < _damageFilters.Length;
                 i++)
            {
                if (!_damageFilters[i]
                    .CanTakeDamage(in info))
                {
                    return false;
                }
            }

            return true;
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

            if (Time.time <
                _invulnUntil)
            {
                return;
            }

            if (!PassesDamageFilters(
                    in info))
            {
                return;
            }


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


            if (invulnAfterHitDuration >
                0f)
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
                new DamageInfo(
                    amount
                );

            TakeDamage(
                in info
            );
        }


        private float GetDamageMultiplier(
            DamageType type)
        {
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
        // STATS
        // ============================================================

        private void ResolveStats()
        {
            if (stats != null)
                return;

            stats =
                GetComponent<EntityStats>();

            if (stats == null)
            {
                stats =
                    GetComponentInParent<
                        EntityStats
                    >();
            }
        }


        private void OnStatChanged(
            StatType type,
            float oldValue,
            float newValue)
        {
            if (type !=
                StatType.Life)
            {
                return;
            }


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


            maxHealth =
                value;


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
        // DEATH
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


            if (SimpleDamageSpawner.Instance ==
                null)
            {
                return;
            }


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