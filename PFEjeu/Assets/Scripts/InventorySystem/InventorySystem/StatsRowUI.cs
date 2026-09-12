using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.StatsSystem.UI
{
    public sealed class StatRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text valueLabel;

        public void Set(string statName, string value, Sprite iconSprite = null)
        {
            if (nameLabel != null) nameLabel.text = statName;
            if (valueLabel != null) valueLabel.text = value;
            if (icon != null) { icon.enabled = iconSprite != null; icon.sprite = iconSprite; }
        }
    }
}