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

        [Tooltip("Cases de la grille générale (hors objets de base).")]
        [SerializeField, Min(1)] private int capacity = 20;
        [Tooltip("Objets de base du joueur, un par case réservée (armures, armes...). Non achetables, " +
                 "non déplaçables, non jetables : uniquement améliorables à la Forge.")]
        [SerializeField] private List<ItemDefinition> baseItems = new();
        [SerializeField] private List<StartingItem> startingItems = new();

        public Inventory Inventory { get; private set; }

        private void Awake()
        {
            Inventory = new Inventory(baseItems.Count + capacity);
            Inventory.ReserveSlots(baseItems);

            foreach (StartingItem entry in startingItems)
            {
                if (entry.definition != null)
                    Inventory.Add(entry.definition, entry.amount);
            }
        }
    }
}