using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;

namespace Core.Village.UI
{
    /// <summary>Une ligne compacte : icône + quantité d'une ressource (style jeu de gestion).</summary>
    public sealed class ResourceBarRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text valueLabel;

        // Si l'item n'a pas d'icône on écrit son nom, sinon on ne sait pas ce qu'on regarde.
        private string _prefix = "";

        public void SetItem(ItemDefinition item)
        {
            SetIcon(item != null ? item.Icon : null);
            _prefix = item != null && item.Icon == null ? item.DisplayName + " " : "";
        }

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        public void SetValue(int amount) => SetValue(amount, -1);

        /// <summary>Affiche "amount" ou "amount / max" si une capacité de stockage est définie.</summary>
        public void SetValue(int amount, int max)
        {
            if (valueLabel == null) return;
            valueLabel.text = _prefix + (max > 0 ? $"{amount} / {max}" : amount.ToString());
        }
    }
}
