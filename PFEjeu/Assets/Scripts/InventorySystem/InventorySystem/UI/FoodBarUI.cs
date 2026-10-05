using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.StatsSystem;
using Core.HealthSystem;

namespace Core.InventorySystem.UI
{
    /// <summary>
    /// Barre de nourriture : affiche jusqu'à N objets de catégorie Consumable
    /// pris dans l'inventaire du joueur (même Inventory que la grille
    /// générale, juste une vue filtrée). Cliquer une case consomme 1 unité :
    /// soin instantané et/ou effets de stats temporaires, puis retrait de
    /// l'inventaire.
    /// </summary>
    public sealed class FoodBarUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private InventoryHolder inventoryHolder;
        [SerializeField] private EntityStats playerStats;
        [SerializeField] private Health playerHealth;

        [Header("UI")]
        [SerializeField] private Transform slotsParent;
        [SerializeField] private FoodSlotUI slotPrefab;
        [SerializeField, Min(1)] private int visibleSlots = 5;

        private Inventory _inventory;
        private FoodSlotUI[] _slots;
        private readonly List<int> _shownIndices = new();

        private void Start()
        {
            if (inventoryHolder == null || slotsParent == null || slotPrefab == null) return;

            _inventory = inventoryHolder.Inventory;
            if (_inventory == null) return;

            BuildSlots();
            _inventory.OnChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.OnChanged -= Refresh;
        }

        private void BuildSlots()
        {
            _slots = new FoodSlotUI[visibleSlots];
            for (int i = 0; i < visibleSlots; i++)
                _slots[i] = Instantiate(slotPrefab, slotsParent);
        }

        private void Refresh()
        {
            _shownIndices.Clear();
            for (int i = 0; i < _inventory.Capacity && _shownIndices.Count < visibleSlots; i++)
            {
                ItemStack stack = _inventory.GetSlot(i);
                if (stack != null && !stack.IsEmpty && stack.Definition != null && stack.Definition.Category == ItemCategory.Consumable)
                    _shownIndices.Add(i);
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (i < _shownIndices.Count)
                {
                    int realIndex = _shownIndices[i];
                    _slots[i].Set(_inventory.GetSlot(realIndex), () => Consume(realIndex));
                }
                else
                {
                    _slots[i].Clear();
                }
            }
        }

        private void Consume(int index)
        {
            ItemStack stack = _inventory.GetSlot(index);
            if (stack == null || stack.IsEmpty || stack.Definition == null) return;

            ItemDefinition definition = stack.Definition;

            if (definition.HealAmount > 0 && playerHealth != null)
                playerHealth.Heal(definition.HealAmount);

            if (playerStats != null && definition.StatEffects.Count > 0)
                StartCoroutine(RunEffects(definition));

            _inventory.RemoveAt(index, 1);
        }

        private IEnumerator RunEffects(ItemDefinition definition)
        {
            object source = new object();
            float maxDuration = 0f;

            foreach (ConsumableStatEffect effect in definition.StatEffects)
            {
                playerStats.AddModifier(effect.type, effect.value, effect.modifierType, source);
                if (effect.duration > maxDuration) maxDuration = effect.duration;
            }

            yield return new WaitForSeconds(maxDuration);

            playerStats.RemoveModifiersFromSource(source);
        }
    }
}
