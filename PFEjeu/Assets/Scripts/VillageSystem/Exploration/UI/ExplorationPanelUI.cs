using System.Collections.Generic;
using System.Linq;
using Core.InventorySystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Village.Exploration.UI
{
    /// <summary>
    /// Panneau du poste d'expédition : liste les missions disponibles (avec un
    /// bouton par héros dispo pour les lancer) et les missions en cours / butins
    /// en attente. Toutes les lignes sont générées par code (pas de prefab de
    /// ligne à maintenir) — voir README de VillageSystem/Exploration.
    /// Ouvrable soit en cliquant le bâtiment en vue village, soit via un trigger
    /// [E] dans le monde (<see cref="ExplorationPostTrigger"/>).
    /// </summary>
    public sealed class ExplorationPanelUI : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private ExplorationManager manager;
        [SerializeField] private VillageViewController villageView;
        [Tooltip("Optionnel : si assigné, seul un clic sur CE bâtiment ouvre ce panneau en vue village.")]
        [SerializeField] private Building explorationBuilding;

        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform availableMissionsParent;
        [SerializeField] private Transform activeMissionsParent;

        [Header("Style")]
        [SerializeField] private Color titleColor = Color.white;
        [SerializeField] private Color mutedColor = new(1f, 1f, 1f, 0.65f);
        [SerializeField] private Color availableColor = new(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color unavailableColor = new(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color failColor = new(0.95f, 0.4f, 0.35f);

        private bool _pausedByThisPanel;
        private readonly List<GameObject> _spawnedRows = new();

        public bool IsShown => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
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
        }

        private void Start() => HideImmediate();

        private void HandleBuildingClicked(Building building)
        {
            if (explorationBuilding != null && building == explorationBuilding) Show();
        }

        private void HandleStateChanged()
        {
            if (panel != null && panel.activeSelf) Refresh();
        }

        public void Show()
        {
            if (panel != null) panel.SetActive(true);

            bool alreadyInVillageView = villageView != null && villageView.IsInVillageView;
            if (!alreadyInVillageView)
            {
                _pausedByThisPanel = true;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Refresh();
        }

        public void Hide() => HideImmediate();

        private void HideImmediate()
        {
            if (panel != null) panel.SetActive(false);

            if (_pausedByThisPanel)
            {
                _pausedByThisPanel = false;
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Refresh()
        {
            foreach (GameObject row in _spawnedRows) Destroy(row);
            _spawnedRows.Clear();

            if (manager == null) return;

            foreach (ExplorationMissionDefinition mission in manager.AvailableMissions)
                BuildAvailableRow(mission);

            foreach (var (hero, mission, cyclesRemaining) in manager.GetActiveMissions())
                BuildActiveRow($"{hero.DisplayName} ({hero.Class}) → {mission.DisplayName}",
                    $"{cyclesRemaining} cycle(s) restant(s)", mutedColor);

            foreach (var (hero, mission, success) in manager.GetPendingRewards())
                BuildActiveRow($"{hero.DisplayName} ({hero.Class}) → {mission.DisplayName}",
                    success ? "De retour — butin à la prochaine défense" : "Blessé — butin perdu",
                    success ? availableColor : failColor);
        }

        private void BuildAvailableRow(ExplorationMissionDefinition mission)
        {
            if (availableMissionsParent == null) return;

            GameObject row = CreateVerticalGroup(availableMissionsParent, "Row_" + mission.DisplayName, 10f);
            _spawnedRows.Add(row);

            var background = row.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.05f);
            row.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 18, 18);

            CreateText(row.transform, $"{mission.DisplayName} — {mission.Zone} — {mission.DurationCycles} cycle(s) — Risque {mission.BaseRisk}",
                titleColor, 26f, FontStyles.Bold);
            CreateText(row.transform, "Récompenses potentielles :", new Color(0.95f, 0.78f, 0.35f), 19f, FontStyles.Bold);
            CreateText(row.transform, DescribeLoot(mission), mutedColor, 19f, FontStyles.Normal);

            GameObject heroRow = CreateHorizontalGroup(row.transform, "Heroes", 12f);

            if (manager.HeroRoster.Count == 0)
            {
                CreateText(heroRow.transform, "Aucun héros dans le roster.", mutedColor, 16f, FontStyles.Italic);
                return;
            }

            foreach (HeroDefinition hero in manager.HeroRoster)
            {
                bool available = manager.IsHeroAvailable(hero);
                Button button = CreateButton(heroRow.transform, $"{hero.DisplayName}\n({hero.Class})",
                    available ? availableColor : unavailableColor);
                button.interactable = available;
                button.onClick.AddListener(() =>
                {
                    manager.TryStartMission(mission, hero, out _);
                    Refresh();
                });
            }
        }

        private void BuildActiveRow(string title, string status, Color statusColor)
        {
            if (activeMissionsParent == null) return;

            GameObject row = CreateVerticalGroup(activeMissionsParent, "Active_" + title, 2f);
            _spawnedRows.Add(row);

            CreateText(row.transform, title, titleColor, 18f, FontStyles.Normal);
            CreateText(row.transform, status, statusColor, 15f, FontStyles.Italic);
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
            tmp.textWrappingMode = TextWrappingModes.Normal;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 900f;
            layoutElement.flexibleWidth = 1f;

            return tmp;
        }

        private static Button CreateButton(Transform parent, string label, Color tint)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(tint.r, tint.g, tint.b, 0.25f);

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
            tmp.color = tint;
            tmp.fontSize = 17f;
            tmp.alignment = TextAlignmentOptions.Center;

            return go.GetComponent<Button>();
        }

        #endregion
    }
}
