using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.StatsSystem;

namespace Core.StatsSystem.UI
{
    using StatType = EnumStats.StatTypes;

    public sealed class CharacterPanelUI : MonoBehaviour
    {
        [System.Serializable]
        private struct StatLine
        {
            public string label;         // "Attaque", "Vie"...
            public StatType stat;        // quelle stat lire
            public string suffix;        // "", " kg"...
            public bool oneDecimal;      // Vitesse = 6.4
        }

        [Header("Données")]
        [SerializeField] private EntityStats stats;

        [Header("Identité")]
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text classLevelLabel;
        [SerializeField] private string characterName = "Chef de tribu";
        [SerializeField] private string className = "Chaman";
        [SerializeField] private int level = 4;

        [Header("Stats")]
        [SerializeField] private Transform rowsParent;   // conteneur (Vertical Layout Group)
        [SerializeField] private StatRowUI rowPrefab;
        [SerializeField] private StatLine[] lines;

        private StatRowUI[] _rows;

        private void Start()
        {
            if (nameLabel != null) nameLabel.text = characterName;
            if (classLevelLabel != null) classLevelLabel.text = $"{className} · Nv {level}";

            BuildRows();
            RefreshAll();

            if (stats != null) stats.OnStatChanged += OnStatChanged;
        }

        private void OnDestroy()
        {
            if (stats != null) stats.OnStatChanged -= OnStatChanged;
        }

        private void BuildRows()
        {
            for (int i = rowsParent.childCount - 1; i >= 0; i--)
                Destroy(rowsParent.GetChild(i).gameObject);

            _rows = new StatRowUI[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                _rows[i] = Instantiate(rowPrefab, rowsParent);
        }

        private void OnStatChanged(StatType type, float oldV, float newV) => RefreshAll();

        private void RefreshAll()
        {
            if (_rows == null || stats == null) return;
            for (int i = 0; i < lines.Length; i++)
            {
                float v = stats.GetStat(lines[i].stat);
                string text = lines[i].oneDecimal ? v.ToString("0.0") : Mathf.RoundToInt(v).ToString();
                _rows[i].Set(lines[i].label, text + lines[i].suffix);
            }
        }
    }
}