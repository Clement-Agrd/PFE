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
        private ItemStack _sword, _bow, _staff;
        private readonly System.Collections.Generic.List<(ItemDefinition Item, int Level, int Quantity)> _applied = new();
        private readonly System.Collections.Generic.List<(ItemDefinition Item, int Level, int Quantity)> _current = new();

        public SOTower Spell => forge != null && _staff != null && _staff.UpgradeLevel >= ForgeDefinition.ElementLevel
            ? forge.GetSpell(_staff.ForgeElement) : null;
        public GameObject SwordFinisher => forge != null ? forge.SwordFinisher : null;

        public int LevelFor(ProfessionalTPS.CombatMode mode) => mode switch
        {
            ProfessionalTPS.CombatMode.Melee => _sword?.UpgradeLevel ?? 0,
            ProfessionalTPS.CombatMode.Bow => _bow?.UpgradeLevel ?? 0,
            ProfessionalTPS.CombatMode.Magic => _staff?.UpgradeLevel ?? 0,
            _ => 0
        };

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
            _sword = _bow = _staff = null;
            _current.Clear();
            for (int i = 0; i < _inventory.Capacity; i++)
            {
                ItemStack stack = _inventory.GetSlot(i);
                if (stack == null || stack.IsEmpty) continue;
                var item = stack.Definition;
                if (item == forge.GetWeapon(ProfessionalTPS.CombatMode.Melee)) KeepBest(ref _sword, stack);
                else if (item == forge.GetWeapon(ProfessionalTPS.CombatMode.Bow)) KeepBest(ref _bow, stack);
                else if (item == forge.GetWeapon(ProfessionalTPS.CombatMode.Magic)) KeepBest(ref _staff, stack);
                else if (stack.UpgradeLevel > 0 && forge.GetRules(item.Category)?.appliesToPlayerStats == true)
                    _current.Add((item, stack.UpgradeLevel, stack.Quantity));
            }

            if (System.Linq.Enumerable.SequenceEqual(_current, _applied)) return;
            stats.RemoveModifiersFromSource(this);
            _applied.Clear();
            _applied.AddRange(_current);
            foreach (var stack in _applied)
            {
                foreach (var bonus in forge.GetBonuses(stack.Item))
                    stats.AddModifier(bonus.stat, bonus.valuePerLevel * stack.Level * stack.Quantity,
                        bonus.modifierType, this);
            }
        }

        private static void KeepBest(ref ItemStack best, ItemStack candidate)
        {
            if (best == null || candidate.UpgradeLevel > best.UpgradeLevel) best = candidate;
        }
    }
}
