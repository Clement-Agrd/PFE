using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.InventorySystem
{
    /// <summary>
    /// Pont Unity : possède un Inventory, l'expose au reste du jeu, et le remplit
    /// avec des items de départ définis dans l'Inspector.
    /// </summary>
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

        /// <summary>Le moteur d'inventaire. Disponible dès Awake.</summary>
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
