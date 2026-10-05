using System;
using UnityEngine;
using Core.StatsSystem;

namespace Core.InventorySystem
{
    using StatType = EnumStats.StatTypes;

    /// <summary>Effet de stat temporaire appliqué en mangeant/buvant un objet Consommable.</summary>
    [Serializable]
    public struct ConsumableStatEffect
    {
        public StatType type;
        public float value;
        public StatModifierType modifierType;
        [Tooltip("Durée du buff, en secondes.")]
        [Min(0.1f)] public float duration;
    }
}
