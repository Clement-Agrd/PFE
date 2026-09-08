using System;
using UnityEngine;
using Core.InventorySystem;

namespace Core.ShopSystem
{
    /// <summary>
    /// Un article de boutique : l'item, son prix d'achat, son prix de vente (rachat
    /// par le marchand), et son stock initial (-1 = illimité).
    /// </summary>
    [Serializable]
    public sealed class ShopEntry
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(0)] private int buyPrice = 10;
        [SerializeField, Min(0)] private int sellPrice = 5;

        [Tooltip("Stock initial du marchand. -1 = illimité.")]
        [SerializeField] private int initialStock = -1;

        public ItemDefinition Item => item;
        public int BuyPrice => buyPrice;
        public int SellPrice => sellPrice;
        public int InitialStock => initialStock;
        public bool InfiniteStock => initialStock < 0;
    }
}
