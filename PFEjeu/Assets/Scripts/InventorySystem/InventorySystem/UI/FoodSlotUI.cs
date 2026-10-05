using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.InventorySystem.UI
{
    /// <summary>Une case de la barre de nourriture : icône, quantité, clic pour consommer.</summary>
    public sealed class FoodSlotUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private Button consumeButton;

        private Action _onClicked;

        private void Awake()
        {
            if (consumeButton != null) consumeButton.onClick.AddListener(HandleClicked);
        }

        public void Set(ItemStack stack, Action onClicked)
        {
            _onClicked = onClicked;

            bool hasItem = stack != null && !stack.IsEmpty && stack.Definition != null;

            if (icon != null)
            {
                bool hasSprite = hasItem && stack.Definition.Icon != null;
                icon.sprite = hasSprite ? stack.Definition.Icon : null;
                icon.color = hasSprite ? Color.white : Color.clear;
                icon.enabled = hasSprite;
            }

            if (amountText != null)
            {
                bool showAmount = hasItem && stack.Quantity > 1;
                amountText.text = showAmount ? stack.Quantity.ToString() : "";
                amountText.enabled = showAmount;
            }

            if (consumeButton != null) consumeButton.interactable = hasItem;
        }

        public void Clear() => Set(null, null);

        private void HandleClicked() => _onClicked?.Invoke();
    }
}
