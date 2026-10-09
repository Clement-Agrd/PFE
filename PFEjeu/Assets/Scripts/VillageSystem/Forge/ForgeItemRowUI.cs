using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;

namespace Core.Village.UI
{
    /// <summary>Une ligne du panneau Forge : icône, nom, texte à droite ("+N" ou statut), clic pour sélectionner.</summary>
    public sealed class ForgeItemRowUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;
        [Tooltip("Fond coloré selon la sélection (optionnel).")]
        [SerializeField] private Image background;
        [SerializeField] private Color normalColor = new(1f, 1f, 1f, 0.08f);
        [SerializeField] private Color selectedColor = new(0.95f, 0.78f, 0.35f, 0.35f);

        private int _index;
        private Action<int> _onClick;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(() => _onClick?.Invoke(_index));
        }

        /// <summary>Objet possédé (index = case d'inventaire).</summary>
        public void Set(int slotIndex, ItemStack stack, bool selected, Action<int> onClick)
        {
            string name = stack.Quantity > 1 ? $"{stack.Definition.DisplayName} x{stack.Quantity}" : stack.Definition.DisplayName;
            Apply(slotIndex, stack.Definition, name, stack.UpgradeLevel > 0 ? $"+{stack.UpgradeLevel}" : "", selected, onClick);
        }

        /// <summary>Article de la boutique (index = position dans la boutique).</summary>
        public void SetShop(int shopIndex, ItemDefinition item, string rightText, bool selected, Action<int> onClick)
            => Apply(shopIndex, item, item.DisplayName, rightText, selected, onClick);

        private void Apply(int index, ItemDefinition def, string name, string right, bool selected, Action<int> onClick)
        {
            _index = index;
            _onClick = onClick;

            if (icon != null)
            {
                icon.sprite = def.Icon;
                icon.enabled = def.Icon != null;
            }
            if (nameLabel != null) nameLabel.text = name;
            if (levelLabel != null) levelLabel.text = right;
            if (background != null) background.color = selected ? selectedColor : normalColor;
        }
    }
}
