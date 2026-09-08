using System;
using UnityEngine;
using Core.InventorySystem;

namespace Core.LootSystem
{
    /// <summary>
    /// Une entrée d'une table de butin : soit un item, soit une sous-table.
    /// - guaranteed = true  → tombe toujours (avec dropChance si &lt; 1).
    /// - guaranteed = false → fait partie du pool pondéré (weight).
    /// </summary>
    [Serializable]
    public sealed class LootEntry
    {
        [Tooltip("L'item à donner. Laisse vide si tu utilises une sous-table.")]
        [SerializeField] private ItemDefinition item;

        [Tooltip("Sous-table à rouler à la place de l'item (composition). Optionnel.")]
        [SerializeField] private LootTable subTable;

        [Header("Pondéré (si non garanti)")]
        [SerializeField, Min(0f)] private float weight = 1f;

        [Header("Quantité")]
        [SerializeField, Min(1)] private int minAmount = 1;
        [SerializeField, Min(1)] private int maxAmount = 1;

        [Header("Garanti")]
        [SerializeField] private bool guaranteed;
        [Tooltip("Pour les entrées garanties : probabilité de tomber (1 = toujours).")]
        [SerializeField, Range(0f, 1f)] private float dropChance = 1f;

        public ItemDefinition Item => item;
        public LootTable SubTable => subTable;
        public float Weight => weight;
        public int MinAmount => minAmount;
        public int MaxAmount => Mathf.Max(minAmount, maxAmount);
        public bool Guaranteed => guaranteed;
        public float DropChance => dropChance;
    }
}
