using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.StatsSystem
{
    /// <summary>
    /// Une stat : une valeur de base + une liste de modificateurs. La valeur finale
    /// est calculée à la demande et mise en cache (recalcul seulement si ça change).
    /// Pur C# → testable hors Unity.
    /// </summary>
    public sealed class Stat
    {
        private float _baseValue;
        private readonly List<StatModifier> _modifiers = new();

        private bool _dirty = true;
        private float _cachedValue;

        public event Action Changed;

        public Stat(float baseValue) => _baseValue = baseValue;

        public float BaseValue
        {
            get => _baseValue;
            set
            {
                if (Mathf.Approximately(_baseValue, value)) return;
                _baseValue = value;
                MarkDirty();
            }
        }

        /// <summary>Valeur finale (base + modificateurs), recalculée seulement si nécessaire.</summary>
        public float Value
        {
            get
            {
                if (_dirty)
                {
                    _cachedValue = Calculate();
                    _dirty = false;
                }
                return _cachedValue;
            }
        }

        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        public void AddModifier(StatModifier modifier)
        {
            if (modifier == null) return;

            _modifiers.Add(modifier);
            _modifiers.Sort((a, b) => a.Order.CompareTo(b.Order)); // regroupe par type/ordre
            MarkDirty();
        }

        public bool RemoveModifier(StatModifier modifier)
        {
            if (modifier != null && _modifiers.Remove(modifier))
            {
                MarkDirty();
                return true;
            }
            return false;
        }

        /// <summary>Retire tous les modificateurs posés par une source donnée.</summary>
        public bool RemoveAllFromSource(object source)
        {
            int removed = _modifiers.RemoveAll(m => Equals(m.Source, source));
            if (removed > 0)
            {
                MarkDirty();
                return true;
            }
            return false;
        }

        private void MarkDirty()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        private float Calculate()
        {
            float finalValue = _baseValue;
            float sumPercentAdd = 0f;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier mod = _modifiers[i];

                switch (mod.Type)
                {
                    case ModifierType.Flat:
                        finalValue += mod.Value;
                        break;

                    case ModifierType.PercentAdd:
                        sumPercentAdd += mod.Value;
                        // On applique la somme des PercentAdd une fois le dernier atteint.
                        bool lastPercentAdd = i + 1 >= _modifiers.Count
                                              || _modifiers[i + 1].Type != ModifierType.PercentAdd;
                        if (lastPercentAdd)
                        {
                            finalValue *= 1f + sumPercentAdd;
                            sumPercentAdd = 0f;
                        }
                        break;

                    case ModifierType.PercentMult:
                        finalValue *= 1f + mod.Value;
                        break;
                }
            }

            // Arrondi pour éviter les erreurs flottantes accumulées.
            return (float)Math.Round(finalValue, 4);
        }
    }
}
