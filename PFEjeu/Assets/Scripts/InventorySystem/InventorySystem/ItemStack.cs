using System;
using UnityEngine;

namespace Core.InventorySystem
{
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

        public int Add(int amount)
        {
            if (definition == null || amount <= 0) return amount;

            int accepted = Mathf.Min(amount, SpaceLeft);
            quantity += accepted;
            return amount - accepted;
        }

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