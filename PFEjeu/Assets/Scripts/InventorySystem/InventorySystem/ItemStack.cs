using System;
using UnityEngine;

namespace Core.InventorySystem
{
    [Serializable]
    public sealed class ItemStack
    {
        [SerializeField] private ItemDefinition definition;
        [SerializeField] private int quantity;
        [SerializeField, Min(0)] private int upgradeLevel;

        public ItemStack(ItemDefinition definition, int quantity, int upgradeLevel = 0)
        {
            this.upgradeLevel = Mathf.Max(0, upgradeLevel);
            this.definition = definition;
            this.quantity = Mathf.Max(0, quantity);
        }

        public ItemDefinition Definition => definition;
        public int Quantity => quantity;

        /// <summary>Niveau d'amélioration Forge (donnée d'instance, 0 = non amélioré).</summary>
        public int UpgradeLevel => upgradeLevel;

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

        /// <summary>Deux piles fusionnent seulement si même objet ET même niveau d'amélioration.</summary>
        public bool CanMergeWith(ItemStack other)
            => other != null && definition == other.definition && upgradeLevel == other.upgradeLevel;

        internal void SetUpgradeLevel(int level) => upgradeLevel = Mathf.Max(0, level);
    }
}