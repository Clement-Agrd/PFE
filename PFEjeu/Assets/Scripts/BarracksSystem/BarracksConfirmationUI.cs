using Core.PauseMenu;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class BarracksConfirmationUI : MonoBehaviour
    {
        [SerializeField] private BarracksVillageUpgrade upgrade;
        [SerializeField] private HeroRoster heroRoster;
        private GameObject _canvas;
        private GameObject _actionsCard, _heroesCard;
        private TMP_Text _summaryLabel, _heroesSummary;
        private heros[] _displayHeroes = System.Array.Empty<heros>();
        private readonly System.Collections.Generic.List<TMP_Text> _heroLabels = new();
        private readonly System.Collections.Generic.List<UnityEngine.UI.Button> _heroButtons = new();
        private TMP_Text _standardLabel;
        private Core.PauseMenu.PauseButton _standardChoice;
        private RectTransform _heroRows;
        private TMP_Text _emptyHeroesLabel;
        private BarracksStatsPreview _heroPreview;
        private heros _previewHero;
        private bool _previewWasHero;
        private UnityEngine.UI.Button _applyHeroButton;
        private TMP_Text _applyHeroLabel;
        private CanvasGroup _applyHeroGroup;
        private UnityEngine.UI.Button _noButton;
        private GameObject _previousSelection;
        private Barracks _owner;
        private BarracksFlagPlacement _placement;
        private GameObject _interactor;

        public bool IsOpen { get; private set; }

        public void Show(Barracks owner, BarracksFlagPlacement placement, GameObject interactor)
        {
            if (IsOpen || !isActiveAndEnabled || owner == null || placement == null || interactor == null)
                return;

            if (heroRoster == null) heroRoster = interactor.GetComponent<HeroRoster>();
            if (_canvas == null) BuildUi();
            _owner = owner;
            _owner.StatsChanged += RefreshHeroUI;
            if (heroRoster != null) heroRoster.Changed += RefreshHeroUI;
            _placement = placement;
            _interactor = interactor;
            IsOpen = true;
            GameFreeze.RequestPause(this);
            GameFreeze.RequestCursorUnlock(this);
            if (EventSystem.current != null)
                _previousSelection = EventSystem.current.currentSelectedGameObject;
            _canvas.SetActive(true);
            ReturnToActions();
            RefreshHeroUI();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_noButton.gameObject);
            }
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            var owner = _owner;
            var placement = _placement;
            var interactor = _interactor;
            Cancel();
            if (owner != null && owner.isActiveAndEnabled && placement != null && placement.isActiveAndEnabled && interactor != null)
                placement.BeginPlacement(interactor);
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_canvas != null) _canvas.SetActive(false);
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_previousSelection);
            _previousSelection = null;
            if (_owner != null) _owner.StatsChanged -= RefreshHeroUI;
            if (heroRoster != null) heroRoster.Changed -= RefreshHeroUI;
            _owner = null;
            _placement = null;
            _interactor = null;
        }

        public void OpenUpgrade()
        {
            if (!IsOpen || upgrade == null || !upgrade.CanOpen) return;
            Cancel();
            upgrade.OpenPanel();
        }

        public void OpenHeroSelection()
        {
            if (!IsOpen || _heroesCard == null) return;
            _actionsCard.SetActive(false);
            _heroesCard.SetActive(true);
            _previewHero = _owner.AssignedHero;
            _previewWasHero = _previewHero != null;
            RefreshHeroUI();
            FocusPreviewSelection();
        }

        private void ChooseHero(heros hero)
        {
            if (!IsOpen || _owner == null) return;
            bool assigned = heroRoster != null ? heroRoster.AssignToBarracks(hero, _owner) : hero == null && _owner.AssignHero(null);
            if (assigned) ReturnToActions();
            RefreshHeroUI();
        }

        private void SelectHero(heros hero)
        {
            if (!IsOpen || _owner == null) return;
            _previewHero = hero;
            _previewWasHero = hero != null;
            RefreshHeroUI();
            FocusPreviewSelection();
        }

        private void FocusPreviewSelection()
        {
            if (EventSystem.current == null || _standardChoice == null) return;
            GameObject selection = _standardChoice.gameObject;
            if (heroRoster != null && _previewHero != null)
                for (int i = 0; i < _displayHeroes.Length && i < _heroButtons.Count; i++)
                    if (_displayHeroes[i] == _previewHero) { selection = _heroButtons[i].gameObject; break; }
            EventSystem.current.SetSelectedGameObject(selection);
        }

        private void ApplySelectedHero()
        {
            if (_applyHeroButton != null && _applyHeroButton.interactable) ChooseHero(_previewHero);
        }

        private void ReturnToActions()
        {
            if (_actionsCard != null) _actionsCard.SetActive(true);
            if (_heroesCard != null) _heroesCard.SetActive(false);
            if (EventSystem.current != null && _noButton != null)
                EventSystem.current.SetSelectedGameObject(_noButton.gameObject);
        }

        private string HeroStatLine(heros hero)
        {
            if (_owner == null || _owner.Data == null) return "";
            var data = _owner.Data;
            int level = _owner.CurrentLevel;
            float life = data.GetSoldierStat(level, Core.StatsSystem.EnumStats.StatTypes.Life, hero);
            float defense = data.GetSoldierStat(level, Core.StatsSystem.EnumStats.StatTypes.DefensePhysic, hero);
            float damage = data.GetSoldierStat(level, Core.StatsSystem.EnumStats.StatTypes.PhysicDamage, hero);
            return $"{data.GetSoldierCount(level, hero)} soldats · PV {life:0.#} · Défense {defense:0.#} · Dégâts {damage:0.#}";
        }

        private void RefreshHeroUI()
        {
            if (_owner == null || _owner.Data == null) return;
            var hero = _owner.AssignedHero;
            string name = hero != null ? $"{hero.HeroName} · Héros niveau {hero.Level}" : "Sans héros · Caserne standard";
            string summary = $"{name}\n{HeroStatLine(hero)}\nCaserne niveau {_owner.CurrentLevel} · Réapparition : {_owner.RespawnTime:0.#} s";
            if (_summaryLabel != null) _summaryLabel.text = summary;
            if (_heroesSummary != null) _heroesSummary.text = name;
            if (_standardLabel != null) _standardLabel.text = "Sans héros\nCaserne standard";
            _displayHeroes = heroRoster != null ? heroRoster.OrderedHeroes.ToArray() : System.Array.Empty<heros>();
            int count = _displayHeroes.Length;
            if (_emptyHeroesLabel != null) _emptyHeroesLabel.gameObject.SetActive(count == 0);
            while (_heroRows != null && _heroLabels.Count < count)
            {
                int index = _heroLabels.Count;
                var choice = NewButton(_heroRows, "Héros", () =>
                {
                    if (index < _displayHeroes.Length) SelectHero(_displayHeroes[index]);
                }, fontSize: 24);
                choice.name = "AssignHeroButton" + index;
                Layout(choice.gameObject, height: 80);
                _heroLabels.Add(choice.GetComponentInChildren<TMP_Text>());
                _heroButtons.Add(choice.GetComponent<UnityEngine.UI.Button>());
            }
            for (int i = 0; i < _heroLabels.Count; i++)
            {
                _heroButtons[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                var candidate = _displayHeroes[i];
                bool available = candidate != null && candidate.isActiveAndEnabled && (!candidate.IsOnExpedition || candidate.IsExpeditionPending) &&
                    (candidate.AssignedBarracks == null || candidate.AssignedBarracks == _owner);
                _heroButtons[i].interactable = available;
                _heroLabels[i].text = candidate != null
                    ? $"{candidate.HeroName}\n{candidate.ClasseName} · Niveau {candidate.Level}" + (candidate.IsExpeditionPending ? " · Expédition annulable" : candidate.IsOnExpedition ? " · En expédition" : candidate.AssignedBarracks != null ? " · Affecté à une caserne" : available ? "" : " · Indisponible")
                    : "Héros indisponible";
                _heroButtons[i].GetComponent<Core.PauseMenu.PauseButton>().SetActive(candidate == _previewHero);
            }
            if (_previewWasHero && _previewHero == null)
            {
                _previewHero = hero;
                _previewWasHero = hero != null;
            }
            if (_heroPreview != null)
            {
                string target = _previewHero != null ? $"{_previewHero.HeroName} · Niveau {_previewHero.Level}" : "Sans héros · Caserne standard";
                _heroPreview.Refresh(_owner, _owner.CurrentLevel, _previewHero, $"Caserne niveau {_owner.CurrentLevel}\nPrévisualisation : {target}");
                bool valid = _previewHero == null || (_previewHero.isActiveAndEnabled && (!_previewHero.IsOnExpedition || _previewHero.IsExpeditionPending) &&
                    (_previewHero.AssignedBarracks == null || _previewHero.AssignedBarracks == _owner));
                _applyHeroButton.interactable = valid && _previewHero != _owner.AssignedHero;
                _applyHeroGroup.alpha = _applyHeroButton.interactable ? 1f : .45f;
                _applyHeroLabel.text = _previewHero == _owner.AssignedHero
                    ? _previewHero == null ? "Déjà sans héros" : "Déjà affecté"
                    : _previewHero == null ? "Retirer le héros" : _previewHero.IsExpeditionPending ? "Annuler l’expédition et affecter" : "Affecter ce héros";
                if (_standardChoice != null) _standardChoice.SetActive(_previewHero == null);
            }
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_owner == null || !_owner.isActiveAndEnabled || _interactor == null)
                Cancel();
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_heroesCard != null && _heroesCard.activeSelf) ReturnToActions();
                else Cancel();
            }
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            Cancel();
            if (_canvas != null) Destroy(_canvas);
        }

        private void BuildUi()
        {
            _canvas = new GameObject("BarracksConfirmationCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var dim = NewImage("Backdrop", _canvas.transform, Dim);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var card = NewImage("Confirmation", dim.transform, Card, 20);
            _actionsCard = card.gameObject;
            bool hasUpgrade = upgrade != null;
            bool hasHeroes = heroRoster != null;
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, hasHeroes ? new Vector2(740, 460) : hasUpgrade ? new Vector2(740, 340) : new Vector2(580, 260));
            var title = NewText("Question", card.transform, hasUpgrade ? "Caserne" : "Déplacer les troupes ?", 34, TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, hasHeroes ? 185 : hasUpgrade ? 115 : 65), new Vector2(640, 60));
            var help = NewText("Description", card.transform, hasUpgrade ? "Que souhaitez-vous faire ?" : "Choisir une nouvelle position de rassemblement.", 21, TextMuted, TextAlignmentOptions.Center);
            Anchor(help.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, hasHeroes ? 135 : hasUpgrade ? 60 : 10), new Vector2(640, 50));
            if (hasHeroes)
            {
                _summaryLabel = NewText("HeroSummary", card.transform, "", 21, TextMuted, TextAlignmentOptions.Center);
                Anchor(_summaryLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(640, 90));
            }
            var row = NewRect("Choices", card.transform);
            Anchor(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, hasHeroes ? -35 : hasUpgrade ? -20 : -70), new Vector2(hasUpgrade ? 640 : 480, 60));
            var layout = HStack(row.gameObject, 20);
            layout.childForceExpandWidth = true;
            var yes = NewButton(row, hasUpgrade ? "Déplacer les troupes" : "Oui", Confirm, fontSize: hasUpgrade ? 25 : 30);
            yes.name = "YesButton";
            var no = NewButton(row, hasUpgrade ? "Améliorer la caserne" : "Non", hasUpgrade ? OpenUpgrade : Cancel, fontSize: hasUpgrade ? 25 : 30);
            no.name = "NoButton";
            _noButton = no.GetComponent<UnityEngine.UI.Button>();
            if (hasUpgrade || hasHeroes)
            {
                if (hasUpgrade)
                {
                    no.name = "UpgradeButton";
                    no.GetComponent<UnityEngine.UI.Button>().interactable = upgrade.CanOpen;
                }
                Transform footer = card.transform;
                if (hasHeroes)
                {
                    var buttons = NewRect("Footer", card.transform);
                    Anchor(buttons, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -145), new Vector2(640, 55));
                    HStack(buttons.gameObject, 20).childForceExpandWidth = true;
                    footer = buttons;
                    var assign = NewButton(footer, "Choisir un héros", OpenHeroSelection, fontSize: 25);
                    assign.name = "HeroButton";
                }
                var cancel = NewButton(footer, "Annuler", Cancel, fontSize: 25);
                cancel.name = "CancelButton";
                if (!hasHeroes) Anchor((RectTransform)cancel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -105), new Vector2(240, 55));
                _noButton = cancel.GetComponent<UnityEngine.UI.Button>();
            }
            if (hasHeroes) BuildHeroChoices(dim.transform);
            _canvas.SetActive(false);
        }

        private void BuildHeroChoices(Transform parent)
        {
            var card = NewImage("HeroSelection", parent, Card, 20);
            _heroesCard = card.gameObject;
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600, 940));
            var title = NewText("Title", card.transform, "Assigner un héros", 34, TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 415), new Vector2(1400, 60));
            _heroPreview = new BarracksStatsPreview(card.transform, "HeroStatsPreview");
            Anchor(_heroPreview.Root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-390, -10), new Vector2(730, 740));
            _heroesSummary = NewText("CurrentHero", card.transform, "", 22, TextMuted, TextAlignmentOptions.Center);
            Anchor(_heroesSummary.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(390, 330), new Vector2(650, 60));
            var list = NewRect("HeroChoices", card.transform);
            Anchor(list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(390, 15), new Vector2(650, 560));
            var viewport = NewImage("Viewport", list, new Color(1, 1, 1, 0.01f));
            Stretch(viewport.rectTransform);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            _heroRows = NewRect("Content", viewport.transform);
            _heroRows.anchorMin = new Vector2(0, 1);
            _heroRows.anchorMax = Vector2.one;
            _heroRows.pivot = new Vector2(0.5f, 1);
            _heroRows.sizeDelta = Vector2.zero;
            VStack(_heroRows.gameObject, 14);
            _heroRows.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var scroll = list.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.content = _heroRows;
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            var standard = NewButton(_heroRows, "Sans héros", () => SelectHero(null), fontSize: 24);
            standard.name = "RemoveHeroButton";
            _standardChoice = standard;
            Layout(standard.gameObject, height: 80);
            _standardLabel = standard.GetComponentInChildren<TMP_Text>();
            var apply = NewButton(card.transform, "Affecter ce héros", ApplySelectedHero, ButtonStyle.Primary, fontSize: 26);
            apply.name = "ApplyHeroButton";
            _applyHeroButton = apply.GetComponent<UnityEngine.UI.Button>();
            _applyHeroLabel = apply.Label;
            _applyHeroGroup = apply.gameObject.AddComponent<CanvasGroup>();
            Anchor((RectTransform)apply.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(390, -335), new Vector2(650, 60));
            string key = heroRoster != null ? heroRoster.DebugRecruitKey.ToString() : "F8";
            _emptyHeroesLabel = NewText("NoRecruitedHeroes", _heroRows, $"Aucun héros recruté.\n{key} : recrutement aléatoire (debug).", 24, TextMuted, TextAlignmentOptions.Center);
            Layout(_emptyHeroesLabel.gameObject, height: 110);
            var back = NewButton(card.transform, "Retour", ReturnToActions, fontSize: 25);
            back.name = "BackToBarracksButton";
            Anchor((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(390, -415), new Vector2(240, 55));
            _heroesCard.SetActive(false);
        }
    }
}
