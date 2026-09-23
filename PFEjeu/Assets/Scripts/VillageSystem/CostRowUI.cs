using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.Village.UI
{
    /// <summary>Une ligne de coût dans le panneau d'amélioration : icône + "Bois x20".</summary>
    public sealed class CostRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text label;

        public void Set(ResourceCost cost)
        {
            if (cost.item == null) return;

            if (icon != null)
            {
                icon.sprite = cost.item.Icon;
                icon.enabled = cost.item.Icon != null;
            }
            if (label != null)
                label.text = $"{cost.item.DisplayName} x{cost.amount}";
        }
    }
}