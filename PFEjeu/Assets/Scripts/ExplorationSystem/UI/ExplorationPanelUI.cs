using System.Collections.Generic;
using System.Linq;
using Core;
using Core.BarracksSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Village.Exploration.UI
{
    public sealed class ExplorationPanelUI : MonoBehaviour
    {
        [SerializeField] private ExplorationManager manager;
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private Building explorationBuilding;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform availableMissionsParent;
        [SerializeField] private Transform activeMissionsParent;
        [SerializeField, HideInInspector] private TMP_Dropdown heroDropdown;
        [SerializeField] private Transform heroesParent;
        [SerializeField] private TMP_Text heroesSummary;
        [SerializeField] private Button forestTab;
        [SerializeField] private Button mountainTab;
        [SerializeField] private Button selectAllButton;
        [SerializeField] private Button clearSelectionButton;
        private readonly HashSet<heros> _selectedHeroes = new();
        private readonly Dictionary<heros, Core.PauseMenu.PauseButton> _heroChoices = new();
        private ExplorationZone _zone = ExplorationZone.Foret;
        private bool _sending;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Core.Village.UI.BuildingPanelUI upgradePanel;
        [SerializeField] private Color titleColor = Color.white;
        [SerializeField] private Color mutedColor = new(1f, 1f, 1f, .65f);
        [SerializeField] private Color availableColor = new(.45f, .85f, .45f);
        [SerializeField] private Color unavailableColor = new(.5f, .5f, .5f);
        [SerializeField] private Color failColor = new(.95f, .4f, .35f);
        private readonly List<GameObject> _spawnedRows = new();
        private int _openedFrame = -1;
        private string _feedback = "";
        public bool IsShown => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(OpenUpgrade);
            if (forestTab != null) forestTab.onClick.AddListener(() => SelectZone(ExplorationZone.Foret));
            if (mountainTab != null) mountainTab.onClick.AddListener(() => SelectZone(ExplorationZone.Montagne));
            if (selectAllButton != null) selectAllButton.onClick.AddListener(() =>
            {
                foreach (var hero in manager.HeroRoster.Where(manager.IsHeroAvailable)) _selectedHeroes.Add(hero);
                Refresh();
            });
            if (clearSelectionButton != null) clearSelectionButton.onClick.AddListener(() => { _selectedHeroes.Clear(); Refresh(); });
        }
        private void OnEnable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked += HandleBuildingClicked;
                villageView.OnExitedVillageView += Hide;
            }
            if (manager != null) manager.OnStateChanged += HandleStateChanged;
        }
        private void OnDisable()
        {
            if (villageView != null)
            {
                villageView.OnBuildingClicked -= HandleBuildingClicked;
                villageView.OnExitedVillageView -= Hide;
            }
            if (manager != null) manager.OnStateChanged -= HandleStateChanged;
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }
        private void Start() => HideImmediate();
        private void HandleBuildingClicked(Building building)
        { if (explorationBuilding != null && building == explorationBuilding) Show(); }
        private void HandleStateChanged() { if (IsShown && !_sending) Refresh(); }
        private void Update() { if (IsShown && UiCloseKeys.Pressed(_openedFrame)) Hide(); }
        public void Show()
        {
            if (!IsShown) _openedFrame = Time.frameCount;
            if (panel != null) panel.SetActive(true);
            if (villageView == null || !villageView.IsInVillageView)
            { GameFreeze.RequestPause(this); GameFreeze.RequestCursorUnlock(this); }
            Refresh();
        }
        public void OpenUpgrade()
        {
            if (upgradePanel == null || explorationBuilding == null) return;
            HideImmediate();
            upgradePanel.ShowStandalone(explorationBuilding);
        }
        public void Hide() => HideImmediate();
        private void HideImmediate()
        {
            if (panel != null) panel.SetActive(false);
            GameFreeze.ReleasePause(this); GameFreeze.ReleaseCursorUnlock(this);
        }
        private void SelectZone(ExplorationZone zone)
        {
            _zone = zone;
            Refresh();
            var scroll = availableMissionsParent.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }
        private void RefreshHeroes()
        {
            _selectedHeroes.RemoveWhere(h => !manager.IsHeroAvailable(h));
            var heroes = manager.HeroRoster;
            int available = heroes.Count(manager.IsHeroAvailable);
            int unassigned = heroes.Count(h => !h.HasAssignment);
            if (heroesSummary != null) heroesSummary.text = $"HÉROS · {unassigned} libre(s) · {heroes.Count - unassigned} affecté(s)\n{_selectedHeroes.Count} sélectionné(s)";
            if (selectAllButton != null) selectAllButton.interactable = available > 0;
            if (clearSelectionButton != null) clearSelectionButton.interactable = _selectedHeroes.Count > 0;
            if (heroesParent == null) return;
            foreach (var removed in _heroChoices.Keys.Where(h => h == null || !heroes.Contains(h)).ToArray())
            {
                if (_heroChoices[removed] != null) Destroy(_heroChoices[removed].gameObject);
                _heroChoices.Remove(removed);
            }
            if (heroes.Count == 0)
            {
                var empty = CreateVerticalGroup(heroesParent, "NoAvailableHeroes", 8);
                _spawnedRows.Add(empty);
                CreateText(empty.transform, "Aucun héros recruté.\nF8 : recruter un héros (debug).", mutedColor, 23, FontStyles.Normal);
            }
            int index = 0;
            foreach (var hero in heroes)
            {
                bool selectable = manager.IsHeroAvailable(hero);
                bool cancellable = manager.CanCancelMission(hero);
                if (!_heroChoices.TryGetValue(hero, out var choice) || choice == null)
                {
                    choice = Core.PauseMenu.PauseUiKit.NewButton(heroesParent, "", () => ClickHero(hero), fontSize: 22);
                    choice.name = "SelectHero_" + hero.GetEntityId();
                    Core.PauseMenu.PauseUiKit.Layout(choice.gameObject, height: 116);
                    choice.Label.textWrappingMode = TextWrappingModes.Normal;
                    choice.gameObject.AddComponent<CanvasGroup>();
                    _heroChoices[hero] = choice;
                }
                choice.transform.SetSiblingIndex(index++);
                string status = hero.IsOnExpedition
                    ? (cancellable ? $"Départ en attente : {hero.ExpeditionName} · Cliquer pour annuler" : $"En expédition : {hero.ExpeditionName}")
                    : hero.AssignedBarracks != null ? "Affecté à une caserne"
                    : selectable ? "Disponible" : manager.GetHeroUnavailableReason(hero);
                choice.Label.text = $"{hero.HeroName} · {hero.ClasseName} · Nv {hero.Level}\n{status}";
                choice.SetActive(_selectedHeroes.Contains(hero));
                choice.GetComponent<UnityEngine.UI.Button>().interactable = selectable || cancellable;
                choice.GetComponent<CanvasGroup>().alpha = selectable || cancellable ? 1f : .5f;
            }
        }
        private void ClickHero(heros hero)
        {
            if (manager.CanCancelMission(hero))
            {
                manager.TryCancelMission(hero);
                _feedback = $"Expédition de {hero.HeroName} annulée.";
            }
            else if (manager.IsHeroAvailable(hero))
            {
                if (!_selectedHeroes.Add(hero)) _selectedHeroes.Remove(hero);
                _feedback = "";
            }
            Refresh();
        }
        private void Refresh()
        {
            foreach (var row in _spawnedRows) { row.SetActive(false); Destroy(row); }
            _spawnedRows.Clear();
            if (manager == null) return;
            RefreshHeroes();
            if (forestTab != null) forestTab.GetComponent<UnityEngine.UI.Image>().color = _zone == ExplorationZone.Foret ? new Color(.43f,.35f,.14f,1) : new Color(.18f,.19f,.22f,1);
            if (mountainTab != null) mountainTab.GetComponent<UnityEngine.UI.Image>().color = _zone == ExplorationZone.Montagne ? new Color(.43f,.35f,.14f,1) : new Color(.18f,.19f,.22f,1);
            if (titleLabel != null) titleLabel.text = $"Centre d'expédition · Niveau {manager.BuildingLevel} / 3";
            if (upgradeButton != null) upgradeButton.interactable = upgradePanel != null && explorationBuilding != null;
            var warning = CreateVerticalGroup(availableMissionsParent, "DepartureWarning", 4);
            _spawnedRows.Add(warning);
            CreateText(warning.transform, "Affectations annulables jusqu’au début de la défense. Les héros partent à son lancement et reviennent à sa fin.", mutedColor, 20, FontStyles.Normal);
            CreateText(warning.transform, $"Échec : {Mathf.RoundToInt(manager.FailedLootFactor * 100)} % du butin. Échec critique : perte définitive du héros.", failColor, 20, FontStyles.Bold);
            if (_feedback.Length > 0) CreateText(warning.transform, _feedback, titleColor, 20, FontStyles.Italic);
            foreach (var mission in manager.AllMissions) if (mission != null && mission.Zone == _zone) BuildAvailableRow(mission);
            foreach (var (hero, mission, cycles) in manager.GetActiveMissions())
                BuildActiveRow($"{hero.HeroName} · Nv {hero.Level} → {mission.DisplayName}", manager.CanCancelMission(hero) ? "Départ en attente · Annulable dans la liste de droite" : $"En expédition · {cycles} défense(s) restante(s)", mutedColor);
            foreach (var result in manager.RecentResults)
            {
                string loot = result.Loot.Count > 0 ? " · " + string.Join(", ", result.Loot.Select(l => $"{l.amount} {l.item.DisplayName}")) : "";
                string pending = result.PendingLoot.Count > 0 ? " · Stockage plein : butin en attente" : "";
                BuildActiveRow($"{result.HeroName} → {result.Mission.DisplayName}", result.Status + loot + pending,
                    result.Outcome == ExpeditionOutcome.Success ? availableColor : failColor);
            }
        }
        private void BuildAvailableRow(ExplorationMissionDefinition mission)
        {
            var row = CreateVerticalGroup(availableMissionsParent, "Mission_" + mission.Zone + "_" + mission.ZoneLevel, 8);
            _spawnedRows.Add(row);
            row.AddComponent<Image>().color = new Color(1, 1, 1, .05f);
            row.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 18, 18);
            bool unlocked = manager.IsMissionUnlocked(mission);
            CreateText(row.transform, mission.DisplayName, unlocked ? titleColor : unavailableColor, 26, FontStyles.Bold);
            CreateText(row.transform, DescribeLoot(mission), mutedColor, 19, FontStyles.Normal);
            string reason = !unlocked ? $"Centre niveau {mission.ZoneLevel} requis"
                : _selectedHeroes.Count == 0 ? "Sélectionne des héros à droite" : "";
            foreach (var hero in manager.HeroRoster.Where(h => _selectedHeroes.Contains(h)))
            {
                var odds = manager.GetOdds(mission, hero);
                CreateText(row.transform, $"{hero.HeroName} · Réussite {Mathf.RoundToInt(odds.Success * 100)} % · Échec {Mathf.RoundToInt(odds.Failure * 100)} % · Perte {Mathf.RoundToInt(odds.Critical * 100)} %",
                    unlocked ? titleColor : mutedColor, 19, FontStyles.Normal);
            }
            var button = CreateButton(row.transform, reason.Length > 0 ? reason : $"Affecter {_selectedHeroes.Count} héros",
                reason.Length > 0 ? unavailableColor : availableColor);
            button.name = "Send_" + mission.Zone + "_" + mission.ZoneLevel;
            button.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 54;
            button.interactable = reason.Length == 0;
            button.onClick.AddListener(() => SendSelected(mission));
        }
        private void SendSelected(ExplorationMissionDefinition mission)
        {
            var heroes = manager.HeroRoster.Where(h => _selectedHeroes.Contains(h)).ToArray();
            int sent = 0;
            string failure = "";
            _sending = true;
            try
            {
                foreach (var hero in heroes)
                    if (manager.TryStartMission(mission, hero, out var reason)) sent++;
                    else failure = reason;
            }
            finally { _sending = false; }
            _feedback = $"{sent} héros affecté(s) · Départ à la prochaine défense." + (failure.Length > 0 ? " " + failure : "");
            Refresh();
        }

        private void BuildActiveRow(string title, string status, Color color)
        {
            var row = CreateVerticalGroup(activeMissionsParent, "ExpeditionStatus", 4);
            _spawnedRows.Add(row);
            CreateText(row.transform, title, titleColor, 21, FontStyles.Bold);
            CreateText(row.transform, status, color, 19, FontStyles.Normal);
        }

        private static string DescribeLoot(ExplorationMissionDefinition mission)
        {
            if (mission.Loot == null || mission.Loot.Count == 0) return "• Aucun butin défini.";

            var lines = mission.Loot.Where(l => l.item != null).Select(l =>
            {
                string amount = l.minAmount == l.maxAmount ? l.minAmount.ToString() : $"{l.minAmount}-{l.maxAmount}";
                string chance = l.dropChance >= 1f ? "" : $"  (chance ~{Mathf.RoundToInt(l.dropChance * 100f)}%)";
                string rare = l.isRare ? "  (rare)" : "";
                return $"•  {amount} {l.item.DisplayName}{chance}{rare}";
            });
            return string.Join("\n", lines);
        }

        #region Fabrique UI minimale (pas de prefab de ligne)

        private static GameObject CreateVerticalGroup(Transform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go;
        }

        private static GameObject CreateHorizontalGroup(Transform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go;
        }

        private static TMP_Text CreateText(Transform parent, string text, Color color, float size, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.minWidth = 0;
            layoutElement.flexibleWidth = 1f;

            return tmp;
        }

        private static Button CreateButton(Transform parent, string label, Color tint)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(tint.r * .4f, tint.g * .4f, tint.b * .4f, 1f);

            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 160f;
            layoutElement.preferredHeight = 60f;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.color = Color.white;
            tmp.fontSize = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            return go.GetComponent<Button>();
        }

        #endregion
    }
}
