using UnityEngine;
using Core.InventorySystem;
using Core.StatsSystem;
using Core.Village;

namespace Core.EquipmentSystem
{
    /// <summary>
    /// Applique aux stats du joueur les bonus de Forge des objets qu'il porte.
    /// Il n'existe pas encore de système d'équipement : "porté" = présent dans l'inventaire
    /// du joueur (même logique que ArmorSlotUI). Tout est retiré puis réappliqué à chaque
    /// changement d'inventaire, avec cet objet comme unique source de modificateurs.
    /// </summary>
    public sealed class EquipmentUpgradeApplier : MonoBehaviour
    {
        [SerializeField] private InventoryHolder playerInventory;
        [SerializeField] private EntityStats stats;
        [SerializeField] private ForgeDefinition forge;

        private Inventory _inventory;

        private void Start()
        {
            if (playerInventory == null || stats == null || forge == null) return;

            _inventory = playerInventory.Inventory;
            if (_inventory == null) return;

            _inventory.OnChanged += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.OnChanged -= Rebuild;
        }

        private void Rebuild()
        {
            stats.RemoveModifiersFromSource(this);

            for (int i = 0; i < _inventory.Capacity; i++)
            {
                ItemStack stack = _inventory.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.UpgradeLevel <= 0) continue;

                ForgeCategoryRules rules = forge.GetRules(stack.Definition.Category);
                if (rules == null || !rules.appliesToPlayerStats || rules.bonuses == null) continue;

                foreach (UpgradeBonus bonus in rules.bonuses)
                    stats.AddModifier(bonus.stat, bonus.valuePerLevel * stack.UpgradeLevel * stack.Quantity,
                        bonus.modifierType, this);
            }
        }
    }
}
