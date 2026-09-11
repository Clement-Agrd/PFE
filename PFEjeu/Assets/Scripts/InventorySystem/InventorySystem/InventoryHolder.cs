using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.InventorySystem
{
    public sealed class InventoryHolder : MonoBehaviour
    {
        [Serializable]
        private struct StartingItem
        {
            public ItemDefinition definition;
            [Min(1)] public int amount;
        }

        [SerializeField, Min(1)] private int capacity = 20;
        [SerializeField] private List<StartingItem> startingItems = new();

        public Inventory Inventory { get; private set; }

        private void Awake()
        {
            Inventory = new Inventory(capacity);

            foreach (StartingItem entry in startingItems)
            {
                if (entry.definition != null)
                    Inventory.Add(entry.definition, entry.amount);
            }
        }
    }
}