using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;
using Core.TweenSystem;

namespace Core.Village.UI
{
    /// <summary>
    /// Panneau d'un bâtiment : s'ouvre au clic (vue village) et affiche tout ce
    /// qu'il faut pour décider d'une amélioration — niveau, production actuelle
    /// et au prochain niveau, coût comparé au stock HDV, prérequis d'Hôtel de
    /// Ville, et un bouton qui dit clairement pourquoi il est bloqué.
    /// Se met à jour en direct quand le stock HDV change. Se ferme en sortant
    /// de la vue village.
    /// </summary>
    public sealed class BuildingPanelUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private VillageManager villageManager;

        [Header("UI — En-tête")]
        [SerializeField] private GameObject panel;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text levelLabel;
        [Tooltip("Parent des pastilles de niveau (une par niveau).")]
        [SerializeField] private Transform levelPipsParent;
        [Tooltip("Pastille modèle (désactivée), dupliquée pour chaque niveau.")]
        [SerializeField] private Image levelPipTemplate;

        [Header("UI — Production")]
        [SerializeField] private GameObject productionSection;
        [SerializeField] private Transform productionRowsParent;
        [SerializeField] private ProductionRowUI productionRowPrefab;

        [Header("UI — Coût (une ligne par ressource)")]
        [SerializeField] private GameObject costSection;
        [SerializeField] private Transform costRowsParent;
        [SerializeField] private CostRowUI costRowPrefab;

        [Header("UI — Prérequis / niveau max")]
        [SerializeField] private GameObject requirementRow;
        [SerializeField] private Image requirementDot;
        [SerializeField] private TMP_Text requirementLabel;
        [SerializeField] private GameObject maxLevelBanner;

        [Header("UI — Actions")]
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Image upgradeButtonImage;
        [SerializeField] private TMP_Text upgradeButtonLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField] private Button closeButton;

        [Header("Couleurs")]
        [SerializeField] private Color okColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color missingColor = new(0.95f, 0.4f, 0.35f);
        [SerializeField] private Color infoColor = new(0.95f, 0.78f, 0.35f);
        [SerializeField] private Color pipOnColor = new(0.95f, 0.78f, 0.35f);
        [SerializeField] private Color pipOffColor = new(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color buttonReadyColor = new(0.27f, 0.62f, 0.32f);
        [SerializeField] private Color buttonBlockedColor = new(0.3f, 0.32f, 0.36f);

        [Header("Animation")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.15f;
        [SerializeField, Min(0f)] private float successMessageDuration = 2f;

        private Building _current;
        private Inventory _watchedInventory;
        private float _successUntil;

        private readonly List<CostRowUI> _costRows = new();
        private readonly List<ProductionRowUI> _productionRows = new();
        private readonly List<Image> _pips = new();

        private void Awake()
        {
            if (upgradeButton != null) upgradeButton.onClick.AddListener(HandleUpgradeClicked);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (levelPipTemplate != null) levelPipTemplate.gameObject.SetActive(false);
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
            WatchInventory(null);
        }

        private void Start() => HideImmediate();

        #region Ouverture / fermeture

        public void Show(Building building)
        {
            if (building == null) return;

            bool wasOpen = _current != null;
            _current = building;
            _successUntil = 0f;
            WatchInventory(villageManager != null ? villageManager.HdvInventory : null);

            if (panel != null) panel.SetActive(true);
            Refresh();

            if (!wasOpen) PlayOpenAnimation();
        }

        public void Hide()
        {
            if (_current == null) return;
            _current = null;
            WatchInventory(null);
            CancelInvoke(nameof(Refresh));

            if (panelGroup == null || fadeDuration <= 0f)
            {
                HideImmediate();
                return;
            }

            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
            TweenManager.Kill(panelGroup);
            panelGroup.TweenFade(0f, fadeDuration)
                .SetUnscaledTime(true)
                .OnComplete(() => { if (_current == null) HideImmediate(); });
        }

        private void HideImmediate()
        {
            _current = null;
            WatchInventory(null);
            if (panel != null) panel.SetActive(false);
        }

        private void PlayOpenAnimation()
        {
            if (panelGroup != null)
            {
                TweenManager.Kill(panelGroup);
                panelGroup.alpha = 0f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
                panelGroup.TweenFade(1f, fadeDuration).SetUnscaledTime(true);
            }
            if (panel != null)
            {
                Transform tr = panel.transform;
                tr.KillTweens();
                tr.localScale = Vector3.one * 0.94f;
                tr.TweenScale(1f, fadeDuration * 1.5f).SetEase(Ease.OutBack).SetUnscaledTime(true);
            }
        }

        private void WatchInventory(Inventory inventory)
        {
            if (_watchedInventory == inventory) return;
            if (_watchedInventory != null) _watchedInventory.OnChanged -= Refresh;
            _watchedInventory = inventory;
            if (_watchedInventory != null) _watchedInventory.OnChanged += Refresh;
        }

        #endregion

        #region Événements

        private void HandleUpgradeClicked()
        {
            if (_current == null || villageManager == null) return;
            villageManager.TryUpgrade(_current);
        }

        private void HandleUpgraded(Building building, int newLevel)
        {
            if (_current == null) return;

            // Une amélioration de l'HDV change les prérequis du bâtiment affiché.
            if (building == _current)
            {
                _successUntil = Time.unscaledTime + successMessageDuration;
                SetFeedback($"Amélioré au niveau {newLevel} !", okColor);
                PulseLevel();
                CancelInvoke(nameof(Refresh));
                Invoke(nameof(Refresh), successMessageDuration);
            }
            Refresh();
        }

        private void HandleUpgradeFailed(Building building, string reason)
        {
            if (building != _current) return;
            _successUntil = 0f;
            SetFeedback(reason, missingColor);
        }

        private void PulseLevel()
        {
            if (levelLabel == null) return;
            Transform tr = levelLabel.transform;
            tr.KillTweens();
            tr.localScale = Vector3.one * 1.25f;
            tr.TweenScale(1f, 0.35f).SetEase(Ease.OutBack).SetUnscaledTime(true);
        }

        #endregion

        #region Affichage

        private void Refresh()
        {
            if (_current == null || _current.Definition == null) return;

            BuildingDefinition def = _current.Definition;
            int level = _current.CurrentLevel;
            int maxLevel = def.MaxLevel;
            BuildingLevelData currentData = def.GetLevelData(level);
            BuildingLevelData next = _current.GetNextLevelData();
            bool isMax = next == null;

            if (iconImage != null)
            {
                iconImage.sprite = def.Icon;
                iconImage.enabled = def.Icon != null;
            }
            if (nameLabel != null) nameLabel.text = def.DisplayName;
            if (levelLabel != null) levelLabel.text = maxLevel > 0 ? $"Niveau {level} / {maxLevel}" : $"Niveau {level}";
            RefreshPips(level, maxLevel);

            RefreshProduction(currentData, next);

            if (costSection != null) costSection.SetActive(!isMax);
            if (maxLevelBanner != null) maxLevelBanner.SetActive(isMax);
            if (!isMax) RefreshCosts(next.upgradeCost);

            RefreshRequirement(level + 1, isMax);

            string reason = "";
            bool canUpgrade = !isMax && villageManager != null && villageManager.CanUpgrade(_current, out reason);

            if (upgradeButton != null)
            {
                upgradeButton.gameObject.SetActive(!isMax);
                upgradeButton.interactable = canUpgrade;
            }
            if (upgradeButtonImage != null) upgradeButtonImage.color = canUpgrade ? buttonReadyColor : buttonBlockedColor;
            if (upgradeButtonLabel != null) upgradeButtonLabel.text = $"Améliorer → Niveau {level + 1}";

            if (Time.unscaledTime < _successUntil) return; // on laisse le message de réussite affiché

            if (isMax) SetFeedback("", okColor);
            else if (canUpgrade) SetFeedback("Prêt à être amélioré", okColor);
            else SetFeedback(reason, missingColor);
        }

        private void RefreshPips(int level, int maxLevel)
        {
            if (levelPipsParent == null || levelPipTemplate == null) return;

            while (_pips.Count < maxLevel)
            {
                Image pip = Instantiate(levelPipTemplate, levelPipsParent);
                _pips.Add(pip);
            }
            for (int i = 0; i < _pips.Count; i++)
            {
                bool used = i < maxLevel;
                _pips[i].gameObject.SetActive(used);
                if (used) _pips[i].color = i < level ? pipOnColor : pipOffColor;
            }
        }

        private void RefreshProduction(BuildingLevelData current, BuildingLevelData next)
        {
            // Ressource → (cadence actuelle, cadence au prochain niveau), dans l'ordre d'apparition.
            var order = new List<ItemDefinition>();
            var now = new Dictionary<ItemDefinition, float>();
            var later = new Dictionary<ItemDefinition, float>();
            Accumulate(current, now, order);
            Accumulate(next, later, order);

            if (productionSection != null) productionSection.SetActive(order.Count > 0);
            if (order.Count == 0 || productionRowsParent == null || productionRowPrefab == null) return;

            EnsureRows(_productionRows, productionRowPrefab, productionRowsParent, order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                ItemDefinition item = order[i];
                float perMinute = now.TryGetValue(item, out float a) ? a : 0f;
                float nextPerMinute = next == null ? -1f : (later.TryGetValue(item, out float b) ? b : 0f);
                _productionRows[i].Set(item, perMinute, nextPerMinute);
            }
        }

        private static void Accumulate(BuildingLevelData data, Dictionary<ItemDefinition, float> perMinute, List<ItemDefinition> order)
        {
            if (data == null || data.productions == null) return;
            foreach (ProductionEntry entry in data.productions)
            {
                if (entry.item == null || entry.amount <= 0 || entry.interval <= 0f) continue;
                if (!order.Contains(entry.item)) order.Add(entry.item);
                perMinute.TryGetValue(entry.item, out float value);
                perMinute[entry.item] = value + entry.amount * 60f / entry.interval;
            }
        }

        private void RefreshCosts(ResourceCost[] costs)
        {
            int count = costs != null ? costs.Length : 0;
            if (costRowsParent == null || costRowPrefab == null) return;

            EnsureRows(_costRows, costRowPrefab, costRowsParent, count);
            Inventory stock = villageManager != null ? villageManager.HdvInventory : null;
            for (int i = 0; i < count; i++)
            {
                int owned = stock != null && costs[i].item != null ? stock.Count(costs[i].item) : -1;
                _costRows[i].Set(costs[i], owned);
            }
        }

        private void RefreshRequirement(int targetLevel, bool isMax)
        {
            if (requirementRow == null) return;

            bool show = !isMax && villageManager != null;
            requirementRow.SetActive(show);
            if (!show) return;

            string text;
            Color color;
            if (villageManager.IsTownHall(_current))
            {
                text = $"Débloque le niveau {targetLevel} pour les autres bâtiments";
                color = infoColor;
            }
            else
            {
                int townHallLevel = villageManager.TownHallLevel;
                bool ok = townHallLevel >= targetLevel;
                text = $"Hôtel de Ville niveau {targetLevel} requis (actuel : {townHallLevel})";
                color = ok ? okColor : missingColor;
            }

            if (requirementLabel != null) requirementLabel.text = text;
            if (requirementDot != null) requirementDot.color = color;
        }

        private void SetFeedback(string text, Color color)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text = text;
            feedbackLabel.color = color;
        }

        /// <summary>Réutilise les lignes existantes, en crée si besoin et masque le surplus.</summary>
        private static void EnsureRows<T>(List<T> rows, T prefab, Transform parent, int count) where T : Component
        {
            while (rows.Count < count) rows.Add(Instantiate(prefab, parent));
            for (int i = 0; i < rows.Count; i++) rows[i].gameObject.SetActive(i < count);
        }

        #endregion
    }
}
