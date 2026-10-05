using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.TavernSystem.UI
{
    /// <summary>Une ligne de la carte de la taverne : icône, nom, description, coût et bouton "Commander".</summary>
    public sealed class TavernDishRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text descriptionLabel;
        [SerializeField] private TMP_Text costLabel;
        [SerializeField] private Button orderButton;

        [Header("Couleurs du bouton")]
        [SerializeField] private Image orderButtonImage;
        [SerializeField] private Color affordableColor = new(0.27f, 0.62f, 0.32f);
        [SerializeField] private Color unaffordableColor = new(0.3f, 0.32f, 0.36f);

        private TavernDishDefinition _dish;
        private Action<TavernDishDefinition> _onOrder;

        private void Awake()
        {
            if (orderButton != null) orderButton.onClick.AddListener(HandleClicked);
        }

        public void Set(TavernDishDefinition dish, int walletBalance, Action<TavernDishDefinition> onOrder)
        {
            _dish = dish;
            _onOrder = onOrder;

            if (icon != null)
            {
                icon.sprite = dish.Icon;
                icon.enabled = dish.Icon != null;
            }
            if (nameLabel != null) nameLabel.text = dish.DishName;
            if (descriptionLabel != null) descriptionLabel.text = dish.Description;
            if (costLabel != null) costLabel.text = $"{dish.Cost} Or — {dish.Duration:0}s";

            bool affordable = walletBalance >= dish.Cost;
            if (orderButton != null) orderButton.interactable = affordable;
            if (orderButtonImage != null) orderButtonImage.color = affordable ? affordableColor : unaffordableColor;
        }

        private void HandleClicked() => _onOrder?.Invoke(_dish);
    }
}
