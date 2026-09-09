using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.StatsSystem
{
    /// <summary>
    /// Porte les stats d'une entité. Chaque stat est créée depuis sa StatDefinition
    /// (valeur de base réglable dans l'Inspector) et gère ses modificateurs.
    /// </summary>
    public sealed class StatsComponent : MonoBehaviour
    {
        [Serializable]
        private struct StatEntry
        {
            public StatDefinition definition;
            public float baseValue;
        }

        [SerializeField] private List<StatEntry> stats = new();

        private readonly Dictionary<StatDefinition, Stat> _stats = new();

        /// <summary>Émis quand la valeur d'une stat change (base ou modificateurs).</summary>
        public event Action<StatDefinition> OnStatChanged;

        private void Awake()
        {
            foreach (StatEntry entry in stats)
                if (entry.definition != null && !_stats.ContainsKey(entry.definition))
                    Register(entry.definition, entry.baseValue);
        }

        /// <summary>Renvoie la stat (la crée avec sa valeur par défaut si absente).</summary>
        public Stat GetStat(StatDefinition definition)
        {
            if (definition == null) return null;

            if (!_stats.TryGetValue(definition, out Stat stat))
                stat = Register(definition, definition.DefaultBaseValue);

            return stat;
        }

        public float GetValue(StatDefinition definition)
            => GetStat(definition)?.Value ?? 0f;

        public void AddModifier(StatDefinition definition, StatModifier modifier)
            => GetStat(definition)?.AddModifier(modifier);

        public bool RemoveModifiersFromSource(StatDefinition definition, object source)
            => GetStat(definition)?.RemoveAllFromSource(source) ?? false;

        /// <summary>Retire les modificateurs d'une source sur TOUTES les stats (ex. déséquiper un objet).</summary>
        public void RemoveAllModifiersFromSource(object source)
        {
            foreach (Stat stat in _stats.Values)
                stat.RemoveAllFromSource(source);
        }

        private Stat Register(StatDefinition definition, float baseValue)
        {
            var stat = new Stat(baseValue);
            stat.Changed += () => OnStatChanged?.Invoke(definition);
            _stats[definition] = stat;
            return stat;
        }
    }
}
