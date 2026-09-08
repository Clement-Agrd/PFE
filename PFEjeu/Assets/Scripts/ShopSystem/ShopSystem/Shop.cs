using System;
using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;

namespace Core.ShopSystem
{
    /// <summary>
    /// Une boutique en jeu : gère achat et vente entre un Wallet et un Inventory,
    /// à partir d'une ShopDefinition. Le stock restant vit ici (pas dans l'asset).
    /// </summary>
    public sealed class Shop : MonoBehaviour
    {
        [SerializeField] private ShopDefinition definition;
        [SerializeField] private Wallet wallet;
        [SerializeField] private InventoryHolder inventoryHolder;

        private readonly Dictionary<ItemDefinition, ShopEntry> _byItem = new();
        private readonly Dictionary<ItemDefinition, int> _stock = new(); // absent = illimité

        public event Action<ShopEntry, int> OnPurchased;        // (article, quantité)
        public event Action<ItemDefinition, int, int> OnSold;   // (item, quantité, or gagné)
        public event Action<string> OnTransactionFailed;        // raison

        private Inventory Inv => inventoryHolder != null ? inventoryHolder.Inventory : null;

        private void Awake()
        {
            if (definition == null) return;

            foreach (ShopEntry entry in definition.Entries)
            {
                if (entry.Item == null) continue;

                _byItem[entry.Item] = entry;
                if (!entry.InfiniteStock)
                    _stock[entry.Item] = entry.InitialStock; // stock fini suivi ici
            }
        }

        /// <summary>Stock restant d'un article. -1 = illimité (ou non vendu ici).</summary>
        public int GetStock(ItemDefinition item)
            => _stock.TryGetValue(item, out int s) ? s : -1;

        public int GetBuyPrice(ItemDefinition item)
            => _byItem.TryGetValue(item, out ShopEntry e) ? e.BuyPrice : 0;

        public int GetSellPrice(ItemDefinition item)
            => _byItem.TryGetValue(item, out ShopEntry e) ? e.SellPrice : 0;

        #region Achat

        public bool Buy(ItemDefinition item, int amount = 1)
        {
            if (!_byItem.TryGetValue(item, out ShopEntry entry)) { Fail("Objet non vendu ici."); return false; }
            if (amount <= 0) return false;
            if (Inv == null) { Fail("Aucun inventaire."); return false; }

            // Stock
            bool infinite = entry.InfiniteStock;
            int remaining = infinite ? int.MaxValue : (_stock.TryGetValue(item, out int s) ? s : 0);
            if (remaining < amount) { Fail("Stock insuffisant."); return false; }

            // Argent
            int cost = entry.BuyPrice * amount;
            if (wallet == null || !wallet.CanAfford(cost)) { Fail("Pas assez d'argent."); return false; }

            // Place : on ajoute, on ne facture que ce qui rentre.
            int leftover = Inv.Add(item, amount);
            int added = amount - leftover;
            if (added <= 0) { Fail("Inventaire plein."); return false; }

            wallet.Spend(entry.BuyPrice * added);
            if (!infinite) _stock[item] = remaining - added;

            OnPurchased?.Invoke(entry, added);
            return true;
        }

        #endregion

        #region Vente

        public bool Sell(ItemDefinition item, int amount = 1)
        {
            if (definition == null || !definition.CanSellHere) { Fail("La vente est désactivée ici."); return false; }
            if (!_byItem.TryGetValue(item, out ShopEntry entry)) { Fail("Ce marchand n'achète pas cet objet."); return false; }
            if (amount <= 0) return false;
            if (Inv == null || !Inv.Contains(item, amount)) { Fail("Quantité insuffisante à vendre."); return false; }

            Inv.Remove(item, amount);

            int gold = entry.SellPrice * amount;
            wallet?.Add(gold);

            // Le marchand récupère le stock (revend ce qu'on lui vend).
            if (!entry.InfiniteStock && _stock.ContainsKey(item))
                _stock[item] += amount;

            OnSold?.Invoke(item, amount, gold);
            return true;
        }

        #endregion

        private void Fail(string reason) => OnTransactionFailed?.Invoke(reason);
    }
}
