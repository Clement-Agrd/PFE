using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Core.TowerDefense
{
    /// <summary>Une icône d'ennemi + son compteur, dans l'aperçu de spawn d'une zone.</summary>
    public sealed class SpawnPreviewSlotUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text countLabel;

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        public void SetCount(int count)
        {
            if (countLabel != null) countLabel.text = "x" + count;
        }
    }
}