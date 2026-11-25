using System.Linq;
using Core.InteractionSystem;
using Core.PauseMenu;
using Core.TowerDefense;
using Core.Village;
using Core.Village.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class BarracksConfirmationUI : MonoBehaviour
    {
        [SerializeField] private BarracksVillageUpgrade upgrade;
        [SerializeField] private HeroRoster heroRoster;
        private DefenseConstructionUI _construction;
        private VillageViewController _view;
        private GameObject _canvas;
        private RectTransform _panel, _heroes;
        private TMP_Text _title, _feedback;
        private BarracksStatsPreview _stats;
        private Button _assign, _move, _upgrade, _demolish;
        private GameObject _confirmation;
        private Barracks _owner;
        private BarracksFlagPlacement _placement;
        private GameObject _interactor;
        private heros _candidate;
        private Building _building;

        public bool IsOpen { get; private set; }

        internal void Configure(DefenseConstructionUI construction)
        {
            _construction = construction;
        }

        public void Show(Barracks owner, BarracksFlagPlacement placement, GameObject interactor,
            VillageViewController view = null)
        {
            if (IsOpen || !isActiveAndEnabled || owner == null || owner.Data == null ||
                placement == null || (_construction != null && !_construction.CanBuildNow)) return;

            _owner = owner;
            _placement = placement;
            _interactor = interactor;
            _view = view;
            _building = owner.GetComponent<Building>();
            if (heroRoster == null && interactor != null) heroRoster = interactor.GetComponent<HeroRoster>();
            if (heroRoster == null) heroRoster = FindFirstObjectByType<HeroRoster>();
            if (_canvas == null) BuildUi();
            _candidate = owner.AssignedHero;
            _feedback.text = "";
            _upgrade.gameObject.SetActive(upgrade != null && upgrade.CanOpen);
            _demolish.gameObject.SetActive(_construction != null && _construction.CanDemolish(_building));
            _move.interactable = view != null || interactor != null;
            IsOpen = true;
            _canvas.SetActive(true);
            Core.UiNavigationScope.Register(_canvas.transform,Cancel,this,()=>IsOpen);
            owner.StatsChanged += Refresh;
            if (heroRoster != null) heroRoster.Changed += Refresh;
            if (view != null)
            {
                view.OnMapSelectionChanging += Cancel;
                view.OnMapAreaChanged += Cancel;
            }
            Refresh();
            PositionPanels();
            if (_view == null || !_view.IsInVillageView) GameFreeze.RequestPause(this);
            else GameFreeze.ReleasePause(this);
            GameFreeze.RequestCursorUnlock(this);
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            CloseConfirmation();
            if (_owner != null) _owner.StatsChanged -= Refresh;
            if (heroRoster != null) heroRoster.Changed -= Refresh;
            if (_view != null)
            {
                _view.OnMapSelectionChanging -= Cancel;
                _view.OnMapAreaChanged -= Cancel;
            }
            _owner = null;
            _view = null;
            _canvas.SetActive(false);
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            var placement = _placement;
            var interactor = _interactor;
            var view = _view;
            Cancel();
            if (placement != null && placement.isActiveAndEnabled)
                placement.BeginPlacement(interactor, view);
        }

        public void OpenUpgrade()
        {
            if (!IsOpen || upgrade == null || !upgrade.CanOpen) return;
            Cancel();
            upgrade.OpenPanel();
        }

        private bool CanChoose(heros hero)
            => hero == null || (hero.isActiveAndEnabled && (!hero.IsOnExpedition || hero.IsExpeditionPending) &&
                hero.Family == HeroFamily.Barracks);

        private void Assign()
        {
            var hero = _candidate;
            var previous = hero != null ? hero.AssignedBarracks : null;
            var expedition = hero != null ? hero.AssignedExpedition : null;
            if (hero != null && ((previous != null && previous != _owner) || expedition != null))
            {
                string source = previous != null ? previous.name : hero.ExpeditionName;
                var overlay = NewImage("Confirmation", _canvas.transform, Dim);
                Stretch(overlay.rectTransform); overlay.raycastTarget = true;
                _confirmation = overlay.gameObject;
                Core.UiNavigationScope.Register(_confirmation.transform, CloseConfirmation, this);
                var card = NewImage("Dialogue", overlay.transform, CardRaised, 16);
                Anchor(card.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(650, 295));
                VStack(card.gameObject, 14, new RectOffset(24, 24, 20, 20));
                Layout(NewText("Message", card.transform, $"{hero.HeroName} est déjà affecté à {source}. Veux-tu changer son affectation ?", 26, TextMain).gameObject, flexH: 1);
                var actions = NewRect("Actions", card.transform); Layout(actions.gameObject, height: 55); HStack(actions.gameObject, 12);
                Layout(NewButton(actions, "Annuler", CloseConfirmation, fontSize: 23).gameObject, flexW: 1);
                Layout(NewButton(actions, "Transférer", () =>
                {
                    CloseConfirmation();
                    if (hero == null || hero.AssignedBarracks != previous || hero.AssignedExpedition != expedition || !CanChoose(hero))
                    { _feedback.text = "L’affectation a changé. Sélectionne à nouveau le héros."; Refresh(); return; }
                    ApplyAssignment(hero, true);
                }, ButtonStyle.Primary, 23).gameObject, flexW: 1);
                return;
            }
            ApplyAssignment(hero, false);
        }

        private void ApplyAssignment(heros hero, bool allowTransfer)
        {
            bool success = heroRoster != null ? heroRoster.AssignToBarracks(hero, _owner, allowTransfer)
                : hero == null && _owner.AssignHero(null);
            _feedback.text = success ? "" : "Affectation indisponible.";
            Refresh();
        }

        private void Refresh()
        {
            if (!IsOpen || _owner == null) return;
            var assigned = _owner.AssignedHero;
            if (!CanChoose(_candidate)) _candidate = assigned;
            _title.text = $"Autel d’invocation · Niveau {_owner.CurrentLevel}\n" +
                (assigned != null ? assigned.HeroName : "Forme de base · Sans héros");
            var target = _candidate != null ? $"{_candidate.HeroName} · Niveau {_candidate.Level}" : "Sans héros";
            _stats.Refresh(_owner, _owner.CurrentLevel, _candidate, $"Affectation · {target}");
            _assign.interactable = _candidate != assigned;
            foreach (Transform child in _heroes)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            AddChoice("Sans héros · Forme de base", null);
            bool hasHeroes = false;
            if (heroRoster != null)
            {
                foreach (var hero in heroRoster.OrderedHeroes.Where(hero => hero != null && hero.Family == HeroFamily.Barracks))
                {
                    hasHeroes = true;
                    string status = hero == assigned ? "Affecté à cet autel" :
                        hero.IsExpeditionPending ? "Expédition annulable" :
                        hero.IsOnExpedition ? "En expédition" :
                        hero.AssignedBarracks != null ? "Transfert depuis " + hero.AssignedBarracks.name : "Disponible";
                    AddChoice($"{hero.HeroName} · {hero.ClasseName} niv. {hero.Level}\n{status}", hero);
                }
            }
            if (!hasHeroes)
                Layout(NewText("Empty", _heroes, "Aucun invocateur recruté.", 23, TextMuted).gameObject, height: 65);
        }

        private void AddChoice(string label, heros hero)
        {
            var choice = NewButton(_heroes, label, () => { _candidate = hero; Refresh(); }, fontSize: 24);
            if (hero != null) HeroClassIcons.Decorate(choice, hero.Classe);
            if (hero != null) choice.name = "AssignHero_" + hero.GetEntityId();
            choice.SetActive(hero == _candidate);
            choice.GetComponent<Button>().interactable = CanChoose(hero);
            Layout(choice.gameObject, height: 78);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_owner == null || !_owner.isActiveAndEnabled || (_construction != null && !_construction.CanBuildNow))
                Cancel();
            else if (Core.UiCloseKeys.Pressed(-1,this))
            {
                if (_confirmation != null) CloseConfirmation();
                else Cancel();
            }
        }

        private void LateUpdate()
        {
            if (IsOpen && _view != null) PositionPanels();
        }

        private void PositionPanels()
        {
            if (MapPanelLayout.Place(_view, _owner.transform.position, _panel, _stats.Root)) return;
            _panel.localScale = _stats.Root.localScale = Vector3.one;
            _panel.anchoredPosition = new Vector2(-40f, 0f);
            _stats.Root.anchoredPosition = new Vector2(-574f, 0f);
        }

        private void ConfirmDemolition()
        {
            var overlay = NewImage("Confirmation", _canvas.transform, Dim);
            Stretch(overlay.rectTransform);
            overlay.raycastTarget = true;
            _confirmation = overlay.gameObject;
            Core.UiNavigationScope.Register(_confirmation.transform,CloseConfirmation,this);
            var card = NewImage("Dialogue", overlay.transform, CardRaised, 16);
            Anchor(card.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(650, 275));
            VStack(card.gameObject, 14, new RectOffset(24, 24, 20, 20));
            var message = $"Démolir cet autel d’invocation ? L’emplacement et le héros seront libérés.\nRemboursement · {_construction.RefundLabel(_building)}";
            Layout(NewText("Message", card.transform, message, 26, TextMain).gameObject, flexH: 1);
            var actions = NewRect("Actions", card.transform);
            Layout(actions.gameObject, height: 55);
            HStack(actions.gameObject, 12);
            Layout(NewButton(actions, "Annuler", CloseConfirmation, fontSize: 23).gameObject, flexW: 1);
            Layout(NewButton(actions, "Démolir", () =>
            {
                CloseConfirmation();
                if (!_construction.TryDemolish(_building, out var reason)) _feedback.text = reason;
            }, ButtonStyle.Danger, 23).gameObject, flexW: 1);
        }

        private void CloseConfirmation()
        {
            if (_confirmation == null) return;
            _confirmation.SetActive(false);
            Destroy(_confirmation);
            _confirmation = null;
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas);
        }

        private void BuildUi()
        {
            _canvas = new GameObject("BarracksConfirmationCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 560;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var backdrop = NewImage("Backdrop", _canvas.transform, new Color(Dim.r, Dim.g, Dim.b, .15f));
            Stretch(backdrop.rectTransform);
            var panel = NewImage("BarracksManagement", backdrop.transform, Card, 16);
            panel.raycastTarget = true;
            _panel = panel.rectTransform;
            Anchor(_panel, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-40, 0), new Vector2(520, 680));
            _stats = new BarracksStatsPreview(backdrop.transform, "HeroStatsPreview");
            Anchor(_stats.Root, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-574, 0), BarracksStatsPreview.CardSize);
            VStack(panel.gameObject, 12, new RectOffset(24, 24, 24, 24));
            _title = NewText("Title", panel.transform, "", 28, Accent);
            Layout(_title.gameObject, height: 100);
            _heroes = NewScroll(panel.transform, "Heroes", 8, new RectOffset(6, 6, 6, 6), out var scroll);
            Layout(scroll.gameObject, flexH: 1);
            _assign = NewButton(panel.transform, "Valider l’affectation", Assign, ButtonStyle.Primary, 25).GetComponent<Button>();
            Layout(_assign.gameObject, height: 55);
            _move = NewButton(panel.transform, "Déplacer les esprits", Confirm, fontSize: 25).GetComponent<Button>();
            Layout(_move.gameObject, height: 55);
            var actions = NewRect("BarracksActions", panel.transform);
            Layout(actions.gameObject, height: 55);
            HStack(actions.gameObject, 12);
            _upgrade = NewButton(actions, "Améliorer l’autel", OpenUpgrade, fontSize: 23).GetComponent<Button>();
            Layout(_upgrade.gameObject, flexW: 1);
            _demolish = NewButton(actions, "Démolir", ConfirmDemolition, ButtonStyle.Danger, 23).GetComponent<Button>();
            Layout(_demolish.gameObject, flexW: 1);
            _feedback = NewText("Feedback", panel.transform, "", 21, Danger);
            Layout(_feedback.gameObject, height: 40);
            Layout(NewButton(panel.transform, "Fermer", Cancel, fontSize: 25).gameObject, height: 55);
        }
    }
}
