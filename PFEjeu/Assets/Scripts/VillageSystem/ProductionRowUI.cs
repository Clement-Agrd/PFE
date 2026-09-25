using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;

namespace Core.Village.UI
{
    /// <summary>
    /// Une ligne de production dans le panneau de bâtiment : icône + nom +
    /// cadence actuelle, et la cadence au prochain niveau ("→ 48 /min") si elle change.
    /// </summary>
    public sealed class ProductionRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text currentLabel;
        [SerializeField] private TMP_Text nextLabel;
        [SerializeField] private Color gainColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color lossColor = new(0.95f, 0.4f, 0.35f);

        /// <param name="perMinute">Cadence actuelle (≤ 0 = pas encore produit).</param>
        /// <param name="nextPerMinute">Cadence au prochain niveau (&lt; 0 = pas de prochain niveau).</param>
        public void Set(ItemDefinition item, float perMinute, float nextPerMinute)
        {
            if (item == null) return;

            if (icon != null)
            {
                icon.sprite = item.Icon;
                icon.enabled = item.Icon != null;
            }
            if (nameLabel != null) nameLabel.text = item.DisplayName;
            if (currentLabel != null) currentLabel.text = perMinute > 0f ? $"{Format(perMinute)} /min" : "—";

            if (nextLabel == null) return;

            bool changes = nextPerMinute >= 0f && !Mathf.Approximately(nextPerMinute, perMinute);
            nextLabel.gameObject.SetActive(changes);
            if (!changes) return;

            nextLabel.text = $"→ {Format(nextPerMinute)} /min";
            nextLabel.color = nextPerMinute > perMinute ? gainColor : lossColor;
        }

        private static string Format(float value)
            => Mathf.Approximately(value, Mathf.Round(value)) ? Mathf.RoundToInt(value).ToString() : value.ToString("0.#");
    }
}
