using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.StatsSystem
{
    using StatType = EnumStats.StatTypes;

    public enum StatModifierType
    {
        Flat,

        // +0.20 = +20 %
        PercentAdditive,

        // +0.20 = x1.20
        PercentMultiplicative
    }

    [Serializable]
    public sealed class BaseStat
    {
        [SerializeField]
        private StatType type;

        [SerializeField, Min(0f)]
        private float baseValue;

        public StatType Type => type;
        public float BaseValue => baseValue;

        public BaseStat(
            StatType type,
            float baseValue)
        {
            this.type = type;
            this.baseValue = Mathf.Max(0f, baseValue);
        }

        internal void SetBaseValue(
            float value)
        {
            baseValue = Mathf.Max(0f, value);
        }
    }

    public readonly struct StatModifierHandle
    {
        internal readonly int Id;

        public bool IsValid => Id > 0;

        internal StatModifierHandle(
            int id)
        {
            Id = id;
        }
    }

    internal sealed class RuntimeStatModifier
    {
        public int Id;
        public float Value;
        public StatModifierType Type;
        public object Source;
    }

    /// <summary>
    /// Système universel de statistiques.
    ///
    /// Peut être installé sur :
    /// - Player
    /// - Enemy
    /// - Companion
    /// - Boss
    /// - Invocation
    ///
    /// Les buffs/modificateurs ne changent JAMAIS
    /// la valeur de base.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityStats : MonoBehaviour
    {
        [Header("Base Stats")]
        [SerializeField]
        private List<BaseStat> baseStats =
            new List<BaseStat>();

        private readonly Dictionary<
            StatType,
            BaseStat
        > _baseStats =
            new Dictionary<
                StatType,
                BaseStat
            >();

        private readonly Dictionary<
            StatType,
            List<RuntimeStatModifier>
        > _modifiers =
            new Dictionary<
                StatType,
                List<RuntimeStatModifier>
            >();

        private bool _initialized;

        private int _nextModifierId = 1;

        /// <summary>
        /// type, ancienne valeur finale, nouvelle valeur finale
        /// </summary>
        public event Action<
            StatType,
            float,
            float
        > OnStatChanged;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized)
                return;

            _baseStats.Clear();

            if (baseStats == null)
                baseStats = new List<BaseStat>();

            for (int i = 0; i < baseStats.Count; i++)
            {
                BaseStat stat = baseStats[i];

                if (stat == null)
                    continue;

                if (_baseStats.ContainsKey(stat.Type))
                {
                    Debug.LogWarning(
                        $"Stat dupliquée : {stat.Type} sur {name}.",
                        this
                    );

                    continue;
                }

                _baseStats.Add(
                    stat.Type,
                    stat
                );
            }

            _initialized = true;
        }

        // ============================================================
        // READ
        // ============================================================

        public bool HasStat(
            StatType type)
        {
            Initialize();

            return _baseStats.ContainsKey(type);
        }

        public float GetBaseStat(
            StatType type)
        {
            Initialize();

            if (_baseStats.TryGetValue(
                    type,
                    out BaseStat stat))
            {
                return stat.BaseValue;
            }

            return 0f;
        }

        /// <summary>
        /// Formule :
        ///
        /// (Base + Flat)
        /// × (1 + PercentAdditive)
        /// × tous les PercentMultiplicative
        /// </summary>
        public float GetStat(
            StatType type)
        {
            Initialize();

            float value =
                GetBaseStat(type);

            if (!_modifiers.TryGetValue(
                    type,
                    out List<RuntimeStatModifier> modifiers))
            {
                return Mathf.Max(0f, value);
            }

            float flat = 0f;
            float additivePercent = 0f;
            float multiplicativePercent = 1f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                RuntimeStatModifier modifier =
                    modifiers[i];

                switch (modifier.Type)
                {
                    case StatModifierType.Flat:

                        flat += modifier.Value;

                        break;

                    case StatModifierType.PercentAdditive:

                        additivePercent += modifier.Value;

                        break;

                    case StatModifierType.PercentMultiplicative:

                        multiplicativePercent *=
                            1f + modifier.Value;

                        break;
                }
            }

            value += flat;

            value *=
                1f + additivePercent;

            value *=
                multiplicativePercent;

            return Mathf.Max(0f, value);
        }

        // ============================================================
        // BASE STATS
        // ============================================================

        /// <summary>
        /// Changement permanent.
        ///
        /// À utiliser pour :
        /// - level up
        /// - progression
        /// - sauvegarde
        /// - respec
        ///
        /// PAS pour les buffs.
        /// </summary>
        public void SetBaseStat(
            StatType type,
            float value)
        {
            Initialize();

            if (!_baseStats.TryGetValue(
                    type,
                    out BaseStat stat))
            {
                Debug.LogWarning(
                    $"La stat {type} n'existe pas sur {name}.",
                    this
                );

                return;
            }

            float oldFinalValue =
                GetStat(type);

            stat.SetBaseValue(value);

            NotifyChanged(
                type,
                oldFinalValue,
                GetStat(type)
            );
        }

        public void AddBaseStat(
            StatType type,
            float amount)
        {
            SetBaseStat(
                type,
                GetBaseStat(type) + amount
            );
        }

        // ============================================================
        // MODIFIERS
        // ============================================================

        public StatModifierHandle AddModifier(
            StatType statType,
            float value,
            StatModifierType modifierType,
            object source = null)
        {
            Initialize();

            float oldValue =
                GetStat(statType);

            if (!_modifiers.TryGetValue(
                    statType,
                    out List<RuntimeStatModifier> list))
            {
                list =
                    new List<RuntimeStatModifier>();

                _modifiers.Add(
                    statType,
                    list
                );
            }

            int id =
                _nextModifierId++;

            RuntimeStatModifier modifier =
                new RuntimeStatModifier
                {
                    Id = id,
                    Value = value,
                    Type = modifierType,
                    Source = source
                };

            list.Add(modifier);

            NotifyChanged(
                statType,
                oldValue,
                GetStat(statType)
            );

            return new StatModifierHandle(id);
        }

        public bool RemoveModifier(
            StatModifierHandle handle)
        {
            if (!handle.IsValid)
                return false;

            foreach (
                KeyValuePair<
                    StatType,
                    List<RuntimeStatModifier>
                > pair
                in _modifiers)
            {
                List<RuntimeStatModifier> list =
                    pair.Value;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id != handle.Id)
                        continue;

                    float oldValue =
                        GetStat(pair.Key);

                    list.RemoveAt(i);

                    NotifyChanged(
                        pair.Key,
                        oldValue,
                        GetStat(pair.Key)
                    );

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Retire tous les bonus provenant d'une même source.
        ///
        /// Exemple :
        /// - une épée
        /// - une potion
        /// - un buff
        /// - une aura
        /// </summary>
        public void RemoveModifiersFromSource(
            object source)
        {
            if (source == null)
                return;

            foreach (
                KeyValuePair<
                    StatType,
                    List<RuntimeStatModifier>
                > pair
                in _modifiers)
            {
                List<RuntimeStatModifier> list =
                    pair.Value;

                float oldValue =
                    GetStat(pair.Key);

                bool changed = false;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!Equals(
                            list[i].Source,
                            source))
                    {
                        continue;
                    }

                    list.RemoveAt(i);

                    changed = true;
                }

                if (!changed)
                    continue;

                NotifyChanged(
                    pair.Key,
                    oldValue,
                    GetStat(pair.Key)
                );
            }
        }

        public void ClearAllModifiers()
        {
            if (_modifiers.Count == 0)
                return;

            StatType[] types =
                new StatType[_modifiers.Count];

            _modifiers.Keys.CopyTo(
                types,
                0
            );

            Dictionary<
                StatType,
                float
            > oldValues =
                new Dictionary<
                    StatType,
                    float
                >();

            for (int i = 0; i < types.Length; i++)
            {
                oldValues[types[i]] =
                    GetStat(types[i]);
            }

            _modifiers.Clear();

            for (int i = 0; i < types.Length; i++)
            {
                NotifyChanged(
                    types[i],
                    oldValues[types[i]],
                    GetStat(types[i])
                );
            }
        }

        // ============================================================
        // EVENT
        // ============================================================

        private void NotifyChanged(
            StatType type,
            float oldValue,
            float newValue)
        {
            if (Mathf.Approximately(
                    oldValue,
                    newValue))
            {
                return;
            }

            OnStatChanged?.Invoke(
                type,
                oldValue,
                newValue
            );
        }

#if UNITY_EDITOR

        private void Reset()
        {
            CreateDefaultStats();

            _initialized = false;
        }

        private void OnValidate()
        {
            EnsureAllStatsExist();

            _initialized = false;
        }

        private void CreateDefaultStats()
        {
            baseStats =
                new List<BaseStat>
                {
                    new BaseStat(
                        StatType.Life,
                        100f
                    ),

                    new BaseStat(
                        StatType.Stamina,
                        100f
                    ),

                    new BaseStat(
                        StatType.PhysicDamage,
                        10f
                    ),

                    new BaseStat(
                        StatType.MagicDamage,
                        10f
                    ),

                    new BaseStat(
                        StatType.DefensePhysic,
                        0f
                    ),

                    new BaseStat(
                        StatType.DefenseMagic,
                        0f
                    ),

                    new BaseStat(
                        StatType.Speed,
                        5.5f
                    ),

                    new BaseStat(
                        StatType.AttackSpeed,
                        1f
                    )
                };
        }

        private void EnsureAllStatsExist()
        {
            if (baseStats == null)
            {
                baseStats =
                    new List<BaseStat>();
            }

            Array values =
                Enum.GetValues(
                    typeof(StatType)
                );

            foreach (StatType type in values)
            {
                bool found = false;

                for (int i = 0; i < baseStats.Count; i++)
                {
                    if (baseStats[i] != null &&
                        baseStats[i].Type == type)
                    {
                        found = true;

                        break;
                    }
                }

                if (found)
                    continue;

                baseStats.Add(
                    new BaseStat(
                        type,
                        GetDefaultValue(type)
                    )
                );
            }
        }

        private static float GetDefaultValue(
            StatType type)
        {
            switch (type)
            {
                case StatType.Life:
                    return 100f;

                case StatType.Stamina:
                    return 100f;

                case StatType.PhysicDamage:
                    return 10f;

                case StatType.MagicDamage:
                    return 10f;

                case StatType.DefensePhysic:
                    return 0f;

                case StatType.DefenseMagic:
                    return 0f;

                case StatType.Speed:
                    return 5.5f;

                case StatType.AttackSpeed:
                    return 1f;

                default:
                    return 0f;
            }
        }

#endif
    }
}