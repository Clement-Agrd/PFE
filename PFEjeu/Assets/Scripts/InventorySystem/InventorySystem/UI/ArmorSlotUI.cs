using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.InventorySystem.UI
{
    /// <summary>
    /// Case d'armure : affiche en lecture seule le premier objet de catégorie
    /// Armor présent dans l'inventaire (pas de retrait/équipement, juste un
    /// aperçu qui se met à jour en direct). Se branche sur le même Inventory
    /// que la grille générale, sans dupliquer le stockage.
    /// </summary>
    public sealed class ArmorSlotUI : MonoBehaviour
    {
        [SerializeField] private InventoryHolder inventoryHolder;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text amountText;

        private Inventory _inventory;

        private void Start()
        {
            if (inventoryHolder == null) return;

            _inventory = inventoryHolder.Inventory;
            if (_inventory == null) return;

            _inventory.OnChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_inventory != null) _inventory.OnChanged -= Refresh;
        }

        private void Refresh()
        {
            ItemStack found = null;
            for (int i = 0; i < _inventory.Capacity && found == null; i++)
            {
                ItemStack stack = _inventory.GetSlot(i);
                if (stack != null && !stack.IsEmpty && stack.Definition != null && stack.Definition.Category == ItemCategory.Armor)
                    found = stack;
            }

            bool hasItem = found != null;

            if (icon != null)
            {
                bool hasSprite = hasItem && found.Definition.Icon != null;
                icon.sprite = hasSprite ? found.Definition.Icon : null;
                icon.color = hasSprite ? Color.white : Color.clear;
                icon.enabled = hasSprite;
            }

            if (amountText != null)
            {
                bool showAmount = hasItem && found.Quantity > 1;
                amountText.text = showAmount ? found.Quantity.ToString() : "";
                amountText.enabled = showAmount;
            }
        }
    }
}
