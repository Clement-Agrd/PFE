using System;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>Un coût en ressources : un item + une quantité.</summary>
    [Serializable]
    public struct ResourceCost
    {
        public ItemDefinition item;
        [Min(1)] public int amount;
    }
}