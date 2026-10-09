using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core.InventorySystem;
using Core.StatsSystem;

namespace Core.Village.UI
{
    /// <summary>
    /// Panneau de la Forge, deux onglets :
    /// - Améliorer : les objets améliorables du joueur (niveau actuel → suivant, bonus, coût, démontage).
    /// - Boutique : tout ce que la Forge vend, avec son prix.
    /// Les coûts sont toujours affichés, en rouge quand le stock de l'HDV est insuffisant.
    /// S'ouvre au clic sur la Forge en vue village, ou via le forgeron (ForgeNpcTrigger).
    /// </summary>
    public sealed class ForgePanelUI : MonoBehaviour
    {
        private enum Tab { Upgrade, Shop }

        [Header("Données")]
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private ForgeService forge;
        [Tooltip("Ouvrir automatiquement au clic sur la Forge. Sinon, appeler Open() depuis un bouton.")]
        [SerializeField] private bool openOnForgeClick = true;

        [Header("UI — Général")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text forgeLevelLabel;
        [SerializeField] private Button closeButton;

        [Header("UI — Onglets")]
        [SerializeField] private Button upgradeTabButton;
        [SerializeField] private Button shopTabButton;

        [Header("UI — Liste")]
        [SerializeField] private Transform itemRowsParent;
        [SerializeField] private ForgeItemRowUI itemRowPrefab;
        [SerializeField] private GameObject emptyListLabel;

        [Header("UI — Détail")]
        [SerializeField] private GameObject detailSection;
        [SerializeField] private Image detailIcon;
        [SerializeField] private TMP_Text detailName;
        [SerializeField] private TMP_Text detailLevel;
        [SerializeField] private TMP_Text statsPreview;
        [SerializeField] private Transform costRowsParent;
        [SerializeField] private CostRowUI costRowPrefab;
        [SerializeField] private GameObject maxLevelBanner;

        [Header("UI — Actions")]
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Image upgradeButtonImage;
        [SerializeField] private TMP_Text upgradeButtonLabel;
        [SerializeField] private Button dismantleButton;
        [SerializeField] private TMP_Text dismantleLabel;
        [SerializeField] private TMP_Text feedbackLabel;

        [Header("Couleurs")]
        [SerializeField] private Color okColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color missingColor = new(0.95f, 0.4f, 0.35f);
        [SerializeField] private Color buttonReadyColor = new(0.27f, 0.62f, 0.32f);
        [SerializeField] private Color buttonBlockedColor = new(0.3f, 0.32f, 0.36f);
        [SerializeField] private Color tabActiveColor = new(0.95f, 0.78f, 0.35f, 0.9f);
        [SerializeField] private Color tabInactiveColor = new(1f, 1f, 1f, 0.1f);

        private readonly List<ForgeItemRowUI> _itemRows = new();
        private readonly List<CostRowUI> _costRows = new();
        private Inventory _playerInv;
        private Inventory _hdvInv;
        private Tab _tab = Tab.Upgrade;
        private int _selected = -1;
        private bool _open;
        private bool _ownsFreeze;
        private string _pendingFeedback;
        private Color _pendingColor;

        public bool IsOpen => _open;

        private void Awake()
        {
            if (upgradeButton != null) upgradeButton.onClick.AddListener(HandlePrimaryAction);
            if (dismantleButton != null) dismantleButton.onClick.AddListener(HandleDismantle);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (upgradeTabButton != null) upgradeTabButton.onClick.AddListener(() => SwitchTab(Tab.Upgrade));
            if (shopTabButton != null) shopTabButton.onClick.AddListener(() => SwitchTab(Tab.Shop));
        }

        private void OnEnable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked += HandleBuildingClicked;
                villageView.OnExitedVillageView += Close;
            }
            if (forge != null)
            {
                forge.OnUpgraded += HandleUpgraded;
                forge.OnDismantled += HandleDismantled;
                forge.OnPurchased += HandlePurchased;
                forge.OnFailed += HandleFailed;
            }
        }

        private void OnDisable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked -= HandleBuildingClicked;
                villageView.OnExitedVillageView -= Close;
            }
            if (forge != null)
            {
                forge.OnUpgraded -= HandleUpgraded;
                forge.OnDismantled -= HandleDismantled;
                forge.OnPurchased -= HandlePurchased;
                forge.OnFailed -= HandleFailed;
            }
            Watch(null, null);
            ReleaseFreeze();
        }

        private void Start() { if (panel != null) panel.SetActive(false); }

        #region Ouverture / fermeture

        private void HandleBuildingClicked(Building building)
        {
            if (openOnForgeClick && forge != null && building == forge.ForgeBuilding) Open();
        }

        public void Open()
        {
            if (forge == null) return;
            _open = true;
            _tab = Tab.Upgrade;
            _selected = -1;
            _pendingFeedback = null;
            Watch(forge.PlayerInventory, forge.HdvInventory);
            if (panel != null) panel.SetActive(true);

            // Hors vue village (ex. via le forgeron), on fige le jeu et on libère le curseur.
            if (!_ownsFreeze && (villageView == null || !villageView.IsInVillageView))
            {
                _ownsFreeze = true;
                GameFreeze.RequestPause(this);
                GameFreeze.RequestCursorUnlock(this);
            }
            Refresh();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            ReleaseFreeze();
            Watch(null, null);
            if (panel != null) panel.SetActive(false);
        }

        private void ReleaseFreeze()
        {
            if (!_ownsFreeze) return;
            _ownsFreeze = false;
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }

        private void Watch(Inventory player, Inventory hdv)
        {
            if (_playerInv != null) _playerInv.OnChanged -= Refresh;
            if (_hdvInv != null) _hdvInv.OnChanged -= Refresh;
            _playerInv = player;
            _hdvInv = hdv;
            if (_playerInv != null) _playerInv.OnChanged += Refresh;
            if (_hdvInv != null) _hdvInv.OnChanged += Refresh;
        }

        private void SwitchTab(Tab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _selected = -1;
            _pendingFeedback = null;
            ApplyFeedback("", Color.white);
            Refresh();
        }

        #endregion

        #region Actions

        private void HandlePrimaryAction()
        {
            if (_selected < 0) return;
            if (_tab == Tab.Shop)
            {
                forge.TryBuy(_selected);
                return;
            }
            int newIndex = forge.TryUpgrade(_selected);
            if (newIndex >= 0) _selected = newIndex;
        }

        private void HandleDismantle()
        {
            if (_tab == Tab.Upgrade && _selected >= 0) forge.TryDismantle(_selected);
        }

        private void HandleUpgraded(ItemStack stack, int level)
            => SetFeedback($"{stack.Definition.DisplayName} amélioré en +{level} !", okColor);

        private void HandleDismantled(ItemDefinition item)
            => SetFeedback($"{item.DisplayName} démonté.", okColor);

        private void HandlePurchased(ItemDefinition item)
            => SetFeedback($"{item.DisplayName} acheté !", okColor);

        private void HandleFailed(string reason) => SetFeedback(reason, missingColor);

        // Le message survit au Refresh déclenché par le changement d'inventaire.
        private void SetFeedback(string text, Color color)
        {
            _pendingFeedback = text;
            _pendingColor = color;
            ApplyFeedback(text, color);
        }

        private void ApplyFeedback(string text, Color color)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text = text;
            feedbackLabel.color = color;
        }

        #endregion

        #region Affichage

        private void Refresh()
        {
            if (!_open || forge == null || _playerInv == null) return;

            if (titleLabel != null) titleLabel.text = "Forge";
            if (forgeLevelLabel != null)
                forgeLevelLabel.text = $"Niveau {forge.ForgeLevel} — objets jusqu'à +{forge.Definition.GetMaxItemLevel(forge.ForgeLevel)}";

            RefreshTabs();
            RefreshList();
            if (_tab == Tab.Shop) RefreshShopDetail();
            else RefreshUpgradeDetail();
        }

        private void RefreshTabs()
        {
            SetTabColor(upgradeTabButton, _tab == Tab.Upgrade);
            SetTabColor(shopTabButton, _tab == Tab.Shop);
        }

        private void SetTabColor(Button tab, bool active)
        {
            if (tab != null && tab.targetGraphic != null)
                tab.targetGraphic.color = active ? tabActiveColor : tabInactiveColor;
        }

        private void RefreshList()
        {
            var indices = new List<int>();
            if (_tab == Tab.Upgrade)
            {
                for (int i = 0; i < _playerInv.Capacity; i++)
                    if (forge.IsUpgradable(_playerInv.GetSlot(i))) indices.Add(i);
            }
            else
            {
                for (int i = 0; i < forge.ShopCount; i++)
                    if (forge.Definition.Shop[i].item != null) indices.Add(i);
            }

            if (_selected >= 0 && !indices.Contains(_selected)) _selected = -1;
            if (_selected < 0 && indices.Count > 0) _selected = indices[0];

            if (emptyListLabel != null) emptyListLabel.SetActive(indices.Count == 0);
            if (itemRowsParent == null || itemRowPrefab == null) return;

            while (_itemRows.Count < indices.Count) _itemRows.Add(Instantiate(itemRowPrefab, itemRowsParent));
            for (int i = 0; i < _itemRows.Count; i++)
            {
                bool used = i < indices.Count;
                _itemRows[i].gameObject.SetActive(used);
                if (!used) continue;

                int idx = indices[i];
                if (_tab == Tab.Upgrade)
                    _itemRows[i].Set(idx, _playerInv.GetSlot(idx), idx == _selected, Select);
                else
                    _itemRows[i].SetShop(idx, forge.Definition.Shop[idx].item, ShopStatus(idx), idx == _selected, Select);
            }
        }

        private string ShopStatus(int shopIndex)
            => forge.CanBuy(shopIndex, out _) ? "Acheter" : "—";

        private void Select(int index)
        {
            _selected = index;
            _pendingFeedback = null;
            ApplyFeedback("", Color.white);
            Refresh();
        }

        private void RefreshUpgradeDetail()
        {
            ItemStack stack = _selected >= 0 ? _playerInv.GetSlot(_selected) : null;
            bool has = forge.IsUpgradable(stack);
            if (detailSection != null) detailSection.SetActive(has);
            if (!has) { ApplyFeedback(_pendingFeedback ?? "", _pendingFeedback != null ? _pendingColor : Color.white); return; }

            ForgeDefinition def = forge.Definition;
            ItemDefinition item = stack.Definition;
            int level = stack.UpgradeLevel;
            bool isMax = forge.IsMaxed(stack);

            ShowItem(item, isMax ? $"+{level} (max)" : $"+{level} → +{level + 1}");
            if (statsPreview != null) statsPreview.text = BuildPreview(def.GetRules(item.Category), level, isMax);
            if (maxLevelBanner != null) maxLevelBanner.SetActive(isMax);

            RefreshCosts(isMax ? null : forge.GetNextCost(stack));

            bool canUpgrade = forge.CanUpgrade(_selected, out string reason);
            SetPrimaryButton(!isMax, canUpgrade, $"Améliorer → +{level + 1}");

            bool canDismantle = forge.CanDismantle(_selected, out _);
            if (dismantleButton != null)
            {
                dismantleButton.gameObject.SetActive(true);
                dismantleButton.interactable = canDismantle;
            }
            if (dismantleLabel != null) dismantleLabel.text = DismantleText(stack);

            if (_pendingFeedback != null) ApplyFeedback(_pendingFeedback, _pendingColor);
            else if (isMax) ApplyFeedback("", okColor);
            else if (canUpgrade) ApplyFeedback("Prêt à être amélioré", okColor);
            else ApplyFeedback(reason, missingColor);
        }

        private void RefreshShopDetail()
        {
            bool has = _selected >= 0 && _selected < forge.ShopCount && forge.Definition.Shop[_selected].item != null;
            if (detailSection != null) detailSection.SetActive(has);
            if (!has) { ApplyFeedback(_pendingFeedback ?? "", _pendingFeedback != null ? _pendingColor : Color.white); return; }

            ForgeShopEntry entry = forge.Definition.Shop[_selected];
            ShowItem(entry.item, "En vente");

            ForgeCategoryRules rules = forge.Definition.GetRules(entry.item.Category);
            if (statsPreview != null)
                statsPreview.text = rules != null && rules.bonuses != null && rules.bonuses.Length > 0
                    ? "Bonus par niveau d'amélioration :\n" + BuildPreview(rules, 1, true)
                    : "";
            if (maxLevelBanner != null) maxLevelBanner.SetActive(false);

            RefreshCosts(entry.cost);

            bool canBuy = forge.CanBuy(_selected, out string reason);
            SetPrimaryButton(true, canBuy, "Acheter");
            if (dismantleButton != null) dismantleButton.gameObject.SetActive(false);

            if (_pendingFeedback != null) ApplyFeedback(_pendingFeedback, _pendingColor);
            else if (canBuy) ApplyFeedback("Prêt à être acheté", okColor);
            else ApplyFeedback(reason, missingColor);
        }

        private void ShowItem(ItemDefinition item, string levelText)
        {
            if (detailIcon != null) { detailIcon.sprite = item.Icon; detailIcon.enabled = item.Icon != null; }
            if (detailName != null) detailName.text = item.DisplayName;
            if (detailLevel != null) detailLevel.text = levelText;
        }

        private void SetPrimaryButton(bool visible, bool enabled, string label)
        {
            if (upgradeButton != null)
            {
                upgradeButton.gameObject.SetActive(visible);
                upgradeButton.interactable = enabled;
            }
            if (upgradeButtonImage != null) upgradeButtonImage.color = enabled ? buttonReadyColor : buttonBlockedColor;
            if (upgradeButtonLabel != null) upgradeButtonLabel.text = label;
        }

        private string DismantleText(ItemStack stack)
        {
            List<ResourceCost> refund = forge.GetDismantleRefund(stack);
            if (refund.Count == 0) return "Démonter";
            var sb = new StringBuilder("Démonter (");
            for (int i = 0; i < refund.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(refund[i].amount).Append(' ').Append(refund[i].item.DisplayName);
            }
            return sb.Append(')').ToString();
        }

        private static string BuildPreview(ForgeCategoryRules rules, int level, bool isMax)
        {
            if (rules == null || rules.bonuses == null) return "";
            var sb = new StringBuilder();
            foreach (UpgradeBonus b in rules.bonuses)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(b.stat).Append(" : ").Append(Format(b, level));
                if (!isMax) sb.Append(" → ").Append(Format(b, level + 1));
            }
            return sb.ToString();
        }

        private static string Format(UpgradeBonus b, int level)
        {
            float total = b.valuePerLevel * level;
            return b.modifierType == StatModifierType.Flat ? $"+{total:0.##}" : $"+{total * 100f:0.#}%";
        }

        private void RefreshCosts(ResourceCost[] costs)
        {
            int count = costs != null ? costs.Length : 0;
            if (costRowsParent == null || costRowPrefab == null) return;

            while (_costRows.Count < count) _costRows.Add(Instantiate(costRowPrefab, costRowsParent));
            for (int i = 0; i < _costRows.Count; i++)
            {
                bool used = i < count;
                _costRows[i].gameObject.SetActive(used);
                if (!used) continue;
                int owned = _hdvInv != null && costs[i].item != null ? _hdvInv.Count(costs[i].item) : -1;
                _costRows[i].Set(costs[i], owned);
            }
        }

        #endregion
    }
}
