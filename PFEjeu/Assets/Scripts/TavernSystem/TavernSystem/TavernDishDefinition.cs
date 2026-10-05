using System;
using System.Collections.Generic;
using UnityEngine;
using Core.StatsSystem;

namespace Core.TavernSystem
{
    using StatType = EnumStats.StatTypes;

    /// <summary>Un effet de stat temporaire appliqué au joueur pendant la durée du plat.</summary>
    [Serializable]
    public struct TavernStatEffect
    {
        public StatType type;
        public float value;
        public StatModifierType modifierType;
    }

    /// <summary>
    /// Un plat ou une boisson de taverne : coût en or, durée du buff, effets de
    /// stats (via EntityStats.AddModifier) et/ou régénération de vie passive
    /// pendant la durée. Création : Assets > Create > Village > Tavern Dish.
    /// </summary>
    [CreateAssetMenu(fileName = "TavernDish", menuName = "Village/Tavern Dish")]
    public sealed class TavernDishDefinition : ScriptableObject
    {
        [SerializeField] private string dishName;
        [TextArea] [SerializeField] private string description;
        [SerializeField] private Sprite icon;
        [SerializeField, Min(0)] private int cost;
        [Tooltip("Durée du buff, en secondes.")]
        [SerializeField, Min(0.1f)] private float duration = 30f;

        [Header("Effets de stats (pendant Duration)")]
        [SerializeField] private List<TavernStatEffect> statEffects = new();

        [Header("Régénération (optionnelle)")]
        [Tooltip("% des PV max soignés par seconde pendant Duration. 0 = aucune régénération.")]
        [SerializeField, Min(0f)] private float healPercentPerSecond;

        public string DishName => dishName;
        public string Description => description;
        public Sprite Icon => icon;
        public int Cost => cost;
        public float Duration => duration;
        public IReadOnlyList<TavernStatEffect> StatEffects => statEffects;
        public float HealPercentPerSecond => healPercentPerSecond;
    }
}
