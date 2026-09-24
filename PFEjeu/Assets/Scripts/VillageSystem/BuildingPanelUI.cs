using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.Village;

namespace Core.Village.UI
{
    /// <summary>
    /// Panneau d'amélioration d'un bâtiment : s'ouvre au clic (vue village),
    /// affiche nom/niveau/coût du prochain niveau, et tente l'amélioration via
    /// le VillageManager. Se ferme automatiquement en sortant de la vue village.
    /// </summary>
    public sealed class BuildingPanelUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private VillageManager villageManager;

        [Header("UI — En-tête")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;

        [Header("UI — Coût (une ligne par ressource)")]
        [SerializeField] private Transform costRowsParent;
        [SerializeField] private CostRowUI costRowPrefab;

        [Header("UI — Actions")]
        [SerializeField] private Button upgradeButton;
        [SerializeField] private TMP_Text upgradeButtonLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField] private Button closeButton;

        private Building _current;
        private CostRowUI[] _costRows;

        private void Awake()
        {
            if (upgradeButton != null) upgradeButton.onClick.AddListener(HandleUpgradeClicked);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
        }

        private void OnEnable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked += Show;
                villageView.OnExitedVillageView += Hide;
            }
            if (villageManager != null)
            {
                villageManager.OnBuildingUpgraded += HandleUpgraded;
                villageManager.OnUpgradeFailed += HandleUpgradeFailed;
            }
        }

        private void OnDisable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked -= Show;
                villageView.OnExitedVillageView -= Hide;
            }
            if (villageManager != null)
            {
                villageManager.OnBuildingUpgraded -= HandleUpgraded;
                villageManager.OnUpgradeFailed -= HandleUpgradeFailed;
            }
        }

        private void Start() => Hide();

        public void Show(Building building)
        {
            _current = building;
            if (panel != null) panel.SetActive(true);
            if (feedbackLabel != null) feedbackLabel.text = "";
            Refresh();
        }

        public void Hide()
        {
            _current = null;
            if (panel != null) panel.SetActive(false);
        }

        private void HandleUpgradeClicked()
        {
            if (_current == null || villageManager == null) return;
            villageManager.TryUpgrade(_current);
        }

        private void HandleUpgraded(Building building, int newLevel)
        {
            if (building != _current) return;
            if (feedbackLabel != null) feedbackLabel.text = "";
            Refresh();
        }

        private void HandleUpgradeFailed(Building building, string reason)
        {
            if (building != _current) return;
            if (feedbackLabel != null) feedbackLabel.text = reason;
        }

        private void Refresh()
        {
            if (_current == null || _current.Definition == null) return;

            BuildingDefinition def = _current.Definition;
            int level = _current.CurrentLevel;

            if (nameLabel != null) nameLabel.text = def.DisplayName;
            if (levelLabel != null) levelLabel.text = $"Niveau {level}";

            BuildingLevelData next = _current.GetNextLevelData();

            if (next == null)
            {
                ClearCostRows();
                if (upgradeButtonLabel != null) upgradeButtonLabel.text = "Niveau max";
                if (upgradeButton != null) upgradeButton.interactable = false;
                return;
            }

            BuildCostRows(next.upgradeCost);

            string reason = "";
            bool canUpgrade = villageManager != null && villageManager.CanUpgrade(_current, out reason);

            if (upgradeButtonLabel != null) upgradeButtonLabel.text = $"Améliorer (Niv. {level + 1})";
            if (upgradeButton != null) upgradeButton.interactable = canUpgrade;
        }

        private void BuildCostRows(ResourceCost[] costs)
        {
            ClearCostRows();
            if (costs == null) return;

            _costRows = new CostRowUI[costs.Length];
            for (int i = 0; i < costs.Length; i++)
            {
                _costRows[i] = Instantiate(costRowPrefab, costRowsParent);
                _costRows[i].Set(costs[i]);
            }
        }

        private void ClearCostRows()
        {
            if (costRowsParent == null) return;
            for (int i = costRowsParent.childCount - 1; i >= 0; i--)
                Destroy(costRowsParent.GetChild(i).gameObject);
            _costRows = null;
        }
    }
}