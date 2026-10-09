using System;
using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>
    /// Logique de la Forge : vérifie, paie (stock de l'HDV) et applique l'amélioration
    /// d'un objet du joueur, ou le démonte contre des ressources.
    /// À poser sur le même GameObject que le bâtiment Forge (ou n'importe où dans le village).
    /// </summary>
    public sealed class ForgeService : MonoBehaviour
    {
        [SerializeField] private Building forgeBuilding;
        [SerializeField] private VillageManager villageManager;
        [SerializeField] private ForgeDefinition definition;
        [Tooltip("Inventaire du joueur : source des objets à améliorer.")]
        [SerializeField] private InventoryHolder playerInventory;

        public ForgeDefinition Definition => definition;
        public Building ForgeBuilding => forgeBuilding;
        public Inventory PlayerInventory => playerInventory != null ? playerInventory.Inventory : null;
        public Inventory HdvInventory => villageManager != null ? villageManager.HdvInventory : null;
        public int ForgeLevel => forgeBuilding != null ? forgeBuilding.CurrentLevel : 0;

        public event Action<ItemStack, int> OnUpgraded;      // (objet, nouveau niveau)
        public event Action<ItemDefinition> OnDismantled;
        public event Action<ItemDefinition> OnPurchased;
        public event Action<string> OnFailed;

        public bool IsUpgradable(ItemStack stack)
            => stack != null && !stack.IsEmpty && definition != null && definition.IsUpgradable(stack.Definition);

        public bool IsMaxed(ItemStack stack)
            => stack.UpgradeLevel >= definition.AbsoluteMaxLevel;

        /// <summary>Coût de la prochaine amélioration (vide si niveau max atteint).</summary>
        public ResourceCost[] GetNextCost(ItemStack stack)
            => IsUpgradable(stack) ? definition.GetCost(stack.UpgradeLevel + 1) : Array.Empty<ResourceCost>();

        public bool CanUpgrade(int slotIndex, out string reason)
        {
            reason = "";
            Inventory inv = PlayerInventory;
            Inventory hdv = HdvInventory;
            if (definition == null || forgeBuilding == null || inv == null || hdv == null)
            {
                reason = "Forge mal configurée.";
                return false;
            }

            ItemStack stack = slotIndex >= 0 && slotIndex < inv.Capacity ? inv.GetSlot(slotIndex) : null;
            if (!IsUpgradable(stack)) { reason = "Objet non améliorable."; return false; }

            int target = stack.UpgradeLevel + 1;
            if (target > definition.AbsoluteMaxLevel) { reason = "Niveau maximum atteint."; return false; }

            if (target > definition.GetMaxItemLevel(ForgeLevel))
            {
                reason = $"Nécessite la Forge niveau {definition.GetRequiredForgeLevel(target)}.";
                return false;
            }

            foreach (ResourceCost cost in definition.GetCost(target))
            {
                if (cost.item != null && !hdv.Contains(cost.item, cost.amount))
                {
                    reason = $"Pas assez de {cost.item.DisplayName}.";
                    return false;
                }
            }

            if (stack.Quantity > 1 && !inv.HasFreeSlot())
            {
                reason = "Inventaire plein.";
                return false;
            }

            return true;
        }

        /// <summary>Améliore l'objet (garanti). Retourne le nouvel index de la case, ou -1.</summary>
        public int TryUpgrade(int slotIndex)
        {
            if (!CanUpgrade(slotIndex, out string reason))
            {
                OnFailed?.Invoke(reason);
                return -1;
            }

            Inventory inv = PlayerInventory;
            ItemStack stack = inv.GetSlot(slotIndex);
            int target = stack.UpgradeLevel + 1;

            foreach (ResourceCost cost in definition.GetCost(target))
                if (cost.item != null) HdvInventory.Remove(cost.item, cost.amount);

            int newIndex = inv.SetUpgradeLevelAt(slotIndex, target);
            OnUpgraded?.Invoke(inv.GetSlot(newIndex), target);
            return newIndex;
        }

        /// <summary>Ressources obtenues en démontant cet objet (base + part des améliorations).</summary>
        public List<ResourceCost> GetDismantleRefund(ItemStack stack)
        {
            var refund = new Dictionary<ItemDefinition, int>();
            if (!IsUpgradable(stack)) return new List<ResourceCost>();

            ForgeCategoryRules rules = definition.GetRules(stack.Definition.Category);
            if (rules.baseRefund != null)
                foreach (ResourceCost c in rules.baseRefund)
                    Accumulate(refund, c.item, c.amount);

            foreach (ResourceCost c in definition.GetInvestedCost(stack.UpgradeLevel))
                Accumulate(refund, c.item, Mathf.FloorToInt(c.amount * definition.DismantleRefundPercent));

            var list = new List<ResourceCost>();
            foreach (var pair in refund)
                if (pair.Value > 0) list.Add(new ResourceCost { item = pair.Key, amount = pair.Value });
            return list;
        }

        public bool CanDismantle(int slotIndex, out string reason)
        {
            reason = "";
            Inventory inv = PlayerInventory;
            if (definition == null || inv == null || HdvInventory == null)
            {
                reason = "Forge mal configurée.";
                return false;
            }

            ItemStack stack = slotIndex >= 0 && slotIndex < inv.Capacity ? inv.GetSlot(slotIndex) : null;
            if (!IsUpgradable(stack)) { reason = "Objet non démontable."; return false; }
            if (GetDismantleRefund(stack).Count == 0) { reason = "Rien à récupérer."; return false; }
            return true;
        }

        /// <summary>Détruit UN exemplaire et verse les ressources dans le stockage de l'HDV.</summary>
        public bool TryDismantle(int slotIndex)
        {
            if (!CanDismantle(slotIndex, out string reason))
            {
                OnFailed?.Invoke(reason);
                return false;
            }

            Inventory inv = PlayerInventory;
            ItemStack stack = inv.GetSlot(slotIndex);
            ItemDefinition item = stack.Definition;
            List<ResourceCost> refund = GetDismantleRefund(stack);

            inv.RemoveAt(slotIndex, 1);
            foreach (ResourceCost c in refund)
            {
                int left = HdvInventory.Add(c.item, c.amount);
                if (left > 0) Debug.LogWarning($"[Forge] Stockage HDV plein : {left}x {c.item.DisplayName} perdu(s).", this);
            }

            OnDismantled?.Invoke(item);
            return true;
        }

        #region Boutique

        public int ShopCount => definition != null ? definition.Shop.Count : 0;

        /// <summary>Nombre d'objets du joueur que la Forge peut améliorer (quelles que soient les ressources).</summary>
        public int CountUpgradableItems()
        {
            Inventory inv = PlayerInventory;
            if (inv == null) return 0;
            int count = 0;
            for (int i = 0; i < inv.Capacity; i++)
                if (IsUpgradable(inv.GetSlot(i))) count++;
            return count;
        }

        public bool CanBuy(int shopIndex, out string reason)
        {
            reason = "";
            Inventory inv = PlayerInventory;
            Inventory hdv = HdvInventory;
            if (definition == null || inv == null || hdv == null || shopIndex < 0 || shopIndex >= definition.Shop.Count)
            {
                reason = "Article indisponible.";
                return false;
            }

            ForgeShopEntry entry = definition.Shop[shopIndex];
            if (entry.item == null) { reason = "Article indisponible."; return false; }

            if (entry.cost != null)
            {
                foreach (ResourceCost cost in entry.cost)
                {
                    if (cost.item != null && !hdv.Contains(cost.item, cost.amount))
                    {
                        reason = $"Pas assez de {cost.item.DisplayName}.";
                        return false;
                    }
                }
            }

            if (!CanFit(inv, entry.item)) { reason = "Inventaire plein."; return false; }
            return true;
        }

        /// <summary>Achète un article : paie avec le stock de l'HDV et l'ajoute à l'inventaire du joueur.</summary>
        public bool TryBuy(int shopIndex)
        {
            if (!CanBuy(shopIndex, out string reason))
            {
                OnFailed?.Invoke(reason);
                return false;
            }

            ForgeShopEntry entry = definition.Shop[shopIndex];
            if (entry.cost != null)
                foreach (ResourceCost cost in entry.cost)
                    if (cost.item != null) HdvInventory.Remove(cost.item, cost.amount);

            PlayerInventory.Add(entry.item, 1);
            OnPurchased?.Invoke(entry.item);
            return true;
        }

        private static bool CanFit(Inventory inv, ItemDefinition item)
        {
            if (inv.HasFreeSlot()) return true;
            if (!item.IsStackable) return false;
            for (int i = 0; i < inv.Capacity; i++)
            {
                ItemStack s = inv.GetSlot(i);
                if (s != null && s.Matches(item) && s.UpgradeLevel == 0 && !s.IsFull) return true;
            }
            return false;
        }

        #endregion

        private static void Accumulate(Dictionary<ItemDefinition, int> map, ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return;
            map.TryGetValue(item, out int v);
            map[item] = v + amount;
        }
    }
}
