using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.Village.UI
{
    /// <summary>
    /// Une ligne de coût dans le panneau d'amélioration : icône + nom + "possédé / requis",
    /// colorée en vert si le stock HDV suffit, en rouge sinon.
    /// </summary>
    public sealed class CostRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text label;

        [Header("Optionnel — affichage possédé / requis")]
        [SerializeField] private TMP_Text amountLabel;
        [SerializeField] private Image statusDot;
        [SerializeField] private Color enoughColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color missingColor = new(0.95f, 0.4f, 0.35f);

        /// <summary>Affichage simple sans connaître le stock ("Bois x20").</summary>
        public void Set(ResourceCost cost) => Set(cost, -1);

        /// <summary>Affiche le coût et, si owned >= 0, compare avec le stock possédé.</summary>
        public void Set(ResourceCost cost, int owned)
        {
            if (cost.item == null) return;

            if (icon != null)
            {
                icon.sprite = cost.item.Icon;
                icon.enabled = cost.item.Icon != null;
            }

            bool hasStock = owned >= 0;
            bool enough = hasStock && owned >= cost.amount;
            Color stateColor = enough ? enoughColor : missingColor;

            if (amountLabel != null)
            {
                if (label != null) label.text = cost.item.DisplayName;
                amountLabel.text = hasStock ? $"{owned} / {cost.amount}" : $"x{cost.amount}";
                amountLabel.color = hasStock ? stateColor : Color.white;
            }
            else if (label != null)
            {
                label.text = hasStock
                    ? $"{cost.item.DisplayName} {owned}/{cost.amount}"
                    : $"{cost.item.DisplayName} x{cost.amount}";
            }

            if (statusDot != null)
            {
                statusDot.enabled = hasStock;
                statusDot.color = stateColor;
            }
        }
    }
}
