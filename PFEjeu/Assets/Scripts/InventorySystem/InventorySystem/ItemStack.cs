using System;
using UnityEngine;

namespace Core.InventorySystem
{
    /// <summary>
    /// Pile runtime : un type d'item (Definition) + une quantité. C'est ce qui
    /// occupe un slot d'inventaire. La mutation passe par des méthodes contrôlées
    /// (jamais de setter public sur quantity) pour garder les invariants.
    /// </summary>
    [Serializable]
    public sealed class ItemStack
    {
        [SerializeField] private ItemDefinition definition;
        [SerializeField] private int quantity;

        public ItemStack(ItemDefinition definition, int quantity)
        {
            this.definition = definition;
            this.quantity = Mathf.Max(0, quantity);
        }

        public ItemDefinition Definition => definition;
        public int Quantity => quantity;

        public bool IsEmpty => definition == null || quantity <= 0;
        public int SpaceLeft => definition == null ? 0 : definition.MaxStackSize - quantity;
        public bool IsFull => definition != null && quantity >= definition.MaxStackSize;

        /// <summary>Ajoute jusqu'à 'amount'. Renvoie ce qui n'a PAS tenu (débordement).</summary>
        public int Add(int amount)
        {
            if (definition == null || amount <= 0) return amount;

            int accepted = Mathf.Min(amount, SpaceLeft);
            quantity += accepted;
            return amount - accepted;
        }

        /// <summary>Retire jusqu'à 'amount'. Renvoie la quantité réellement retirée.</summary>
        public int Remove(int amount)
        {
            if (amount <= 0) return 0;

            int removed = Mathf.Min(amount, quantity);
            quantity -= removed;
            return removed;
        }

        public bool Matches(ItemDefinition other) => definition == other;
    }
}
