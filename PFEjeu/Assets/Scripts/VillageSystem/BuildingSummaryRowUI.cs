using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;

namespace Core.Village.UI
{
    /// <summary>
    /// Une ligne du bilan village (onglet "Village" de l'inventaire) : icône,
    /// nom, niveau et ce que le bâtiment produit actuellement, sur une seule
    /// ligne de texte. Purement informatif — pas d'amélioration possible
    /// depuis ici (voir BuildingPanelUI pour ça, dans la vue village).
    /// </summary>
    public sealed class BuildingSummaryRowUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text productionLabel;

        public void Set(Building building)
        {
            if (building == null || building.Definition == null) return;

            BuildingDefinition def = building.Definition;
            int level = building.CurrentLevel;

            if (icon != null)
            {
                icon.sprite = def.Icon;
                icon.enabled = def.Icon != null;
            }
            if (nameLabel != null) nameLabel.text = def.DisplayName;
            if (levelLabel != null) levelLabel.text = def.MaxLevel > 0 ? $"Niveau {level} / {def.MaxLevel}" : $"Niveau {level}";
            if (productionLabel != null) productionLabel.text = BuildProductionText(def.GetLevelData(level));
        }

        private static string BuildProductionText(BuildingLevelData data)
        {
            if (data == null || data.productions == null || data.productions.Length == 0) return "Aucune production";

            var order = new List<ItemDefinition>();
            var perMinute = new Dictionary<ItemDefinition, float>();
            foreach (ProductionEntry entry in data.productions)
            {
                if (entry.item == null || entry.amount <= 0 || entry.interval <= 0f) continue;
                if (!order.Contains(entry.item)) order.Add(entry.item);
                perMinute.TryGetValue(entry.item, out float value);
                perMinute[entry.item] = value + entry.amount * 60f / entry.interval;
            }

            if (order.Count == 0) return "Aucune production";

            var sb = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) sb.Append("    ");
                float rate = perMinute[order[i]];
                string formatted = Mathf.Approximately(rate, Mathf.Round(rate)) ? Mathf.RoundToInt(rate).ToString() : rate.ToString("0.#");
                sb.Append(order[i].DisplayName).Append(' ').Append(formatted).Append(" /min");
            }
            return sb.ToString();
        }
    }
}
