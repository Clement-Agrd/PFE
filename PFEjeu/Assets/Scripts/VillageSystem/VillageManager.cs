using System;
using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>
    /// Le cerveau du village : applique la règle de gating (un bâtiment ne monte
    /// au niveau N que si l'Hôtel de Ville est DÉJÀ au niveau N), débite le
    /// stockage de l'HDV, et transfère les ressources du joueur à l'entrée.
    /// </summary>
    public sealed class VillageManager : MonoBehaviour
    {
        [SerializeField] private Building townHall;
        [Tooltip("Le stockage central de l'HDV (son propre InventoryHolder).")]
        [SerializeField] private InventoryHolder hdvStorage;
        [Tooltip("Les items considérés comme des ressources transférables à l'entrée du village.")]
        [SerializeField] private List<ItemDefinition> transferableResources = new();

        public int TownHallLevel => townHall != null ? townHall.CurrentLevel : 0;

        public event Action<Building, int> OnBuildingUpgraded; // (bâtiment, nouveau niveau)
        public event Action<Building, string> OnUpgradeFailed; // (bâtiment, raison)
        public event Action OnResourcesTransferred;

        /// <summary>Le bâtiment peut-il monter de niveau maintenant ?</summary>
        public bool CanUpgrade(Building building, out string reason)
        {
            reason = "";
            if (building == null) { reason = "Bâtiment invalide."; return false; }
            if (building.IsMaxLevel) { reason = "Niveau maximum atteint."; return false; }

            int targetLevel = building.CurrentLevel + 1;

            // L'Hôtel de Ville s'améliore librement (c'est lui qui fixe le plafond).
            // Les autres bâtiments exigent que l'HDV ait DÉJÀ atteint ce niveau.
            if (building != townHall && targetLevel > TownHallLevel)
            {
                reason = $"Nécessite l'Hôtel de Ville niveau {targetLevel}.";
                return false;
            }

            BuildingLevelData data = building.GetNextLevelData();
            if (data == null) { reason = "Données de niveau manquantes."; return false; }

            if (hdvStorage == null || hdvStorage.Inventory == null)
            {
                reason = "Aucun stockage HDV.";
                return false;
            }

            foreach (ResourceCost cost in data.upgradeCost)
            {
                if (!hdvStorage.Inventory.Contains(cost.item, cost.amount))
                {
                    reason = $"Pas assez de {cost.item.DisplayName}.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Tente d'améliorer le bâtiment. Débite le stockage HDV si réussi.</summary>
        public bool TryUpgrade(Building building)
        {
            if (!CanUpgrade(building, out string reason))
            {
                OnUpgradeFailed?.Invoke(building, reason);
                return false;
            }

            BuildingLevelData data = building.GetNextLevelData();
            foreach (ResourceCost cost in data.upgradeCost)
                hdvStorage.Inventory.Remove(cost.item, cost.amount);

            building.SetLevel(building.CurrentLevel + 1);
            OnBuildingUpgraded?.Invoke(building, building.CurrentLevel);
            return true;
        }

        /// <summary>Transfère les ressources du joueur vers le stockage de l'HDV.</summary>
        public void TransferPlayerResourcesToHDV(InventoryHolder playerInventory)
        {
            if (playerInventory == null || playerInventory.Inventory == null) return;
            if (hdvStorage == null || hdvStorage.Inventory == null) return;

            Inventory playerInv = playerInventory.Inventory;
            bool anyTransferred = false;

            foreach (ItemDefinition resource in transferableResources)
            {
                if (resource == null) continue;

                int amount = playerInv.Count(resource);
                if (amount <= 0) continue;

                int removed = playerInv.Remove(resource, amount);
                if (removed > 0)
                {
                    hdvStorage.Inventory.Add(resource, removed);
                    anyTransferred = true;
                }
            }

            if (anyTransferred) OnResourcesTransferred?.Invoke();
        }
    }
}