using System;
using Core;
using Core.BarracksSystem;
using Core.InteractionSystem;
using Core.TowerDefense;
using Core.Village;
using Core.Village.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

[RequireComponent(typeof(Tower), typeof(Building))]
public sealed class TowerHeroStation : InteractableBase
{
    [Serializable]
    private struct Specialization
    {
        public ClasseHeros heroClass;
        public SOTower profile;
    }

    [SerializeField] private HeroFamily family;
    [SerializeField] private Specialization[] specializations;

    private Tower _tower;
    private Building _building;
    private DefenseConstructionUI _construction;
    private BuildingPanelUI _upgradePanel;
    private HeroRoster _roster;
    private GameObject _canvas;
    private RectTransform _rows;
    private TMP_Text _title;
    private TowerStatsPreview _preview;
    private heros _candidate;
    private Button _confirm;
    private TMP_Text _feedback;
    private GameObject _confirmation;

    public heros AssignedHero { get; private set; }
    public override string Prompt => "Gérer la tour";
    private bool CanManage => isActiveAndEnabled && (_construction == null || _construction.CanBuildNow);

    private void Awake()
    {
        _tower = GetComponent<Tower>();
        _building = GetComponent<Building>();
    }

    private void OnEnable() => _building.OnLevelChanged += _tower.SetLevel;

    private void OnDisable()
    {
        _building.OnLevelChanged -= _tower.SetLevel;
        Hide();
        ClearHero();
    }

    private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }

    private void Start() => _tower.SetLevel(_building.CurrentLevel);

    public void Configure(DefenseConstructionUI construction, BuildingPanelUI upgradePanel)
    {
        _construction = construction;
        _upgradePanel = upgradePanel;
    }

    private SOTower ProfileFor(heros hero)
    {
        if (hero == null || hero.Family != family || specializations == null) return null;
        foreach (var specialization in specializations)
            if (specialization.heroClass == hero.Classe) return specialization.profile;
        return null;
    }

    public SOTower GetProfile(heros hero) => ProfileFor(hero) ?? _tower.BaseProfile;

    public int CurrentLevel => _tower.CurrentLevel;

    private bool CanAssign(heros hero)
        => CanManage && hero != null && hero.isActiveAndEnabled && ProfileFor(hero) != null &&
           hero.AssignedBarracks == null && (!hero.IsOnExpedition || hero.IsExpeditionPending);

    public bool AssignHero(heros hero) => AssignHero(hero, false);

    private bool AssignHero(heros hero, bool allowTransfer)
    {
        if (!CanManage) return false;
        if (hero == AssignedHero) return true;
        if (hero == null)
        {
            ClearHero();
            return true;
        }
        if (!CanAssign(hero) || !hero.TryClaimTower(this, allowTransfer)) return false;
        var previous = AssignedHero;
        AssignedHero = hero;
        if (previous != null)
        {
            previous.Changed -= ApplyHeroLevel;
            previous.ReleaseTower(this);
        }
        AssignedHero.Changed += ApplyHeroLevel;
        ApplyHeroLevel();
        Refresh();
        return true;
    }

    private void ApplyHeroLevel() => ApplyProfile(ProfileFor(AssignedHero), AssignedHero != null ? AssignedHero.Level : 0);

    private void ApplyProfile(SOTower profile, int heroLevel = 0)
    {
        bool changedModel = _tower.TowerData != (profile != null ? profile : _tower.BaseProfile);
        _tower.SetSpecialization(profile, heroLevel);
        if (!changedModel) return;
        if (TryGetComponent<BuildingHighlight>(out var highlight)) highlight.RefreshModel();
    }

    internal void ClearHero()
    {
        var previous = AssignedHero;
        AssignedHero = null;
        if (previous != null && _tower != null) ApplyProfile(null);
        if (previous != null)
        {
            previous.Changed -= ApplyHeroLevel;
            previous.ReleaseTower(this);
        }
        Refresh();
    }

    public override bool CanInteract(GameObject interactor) => CanManage;
    public override void Interact(GameObject interactor) => Show();

    public void Show()
    {
        if (!CanManage || (_canvas != null && _canvas.activeSelf)) return;
        _roster = FindFirstObjectByType<HeroRoster>();
        if (_canvas == null) BuildUI();
        _candidate = AssignedHero;
        _feedback.text = "";
        _canvas.SetActive(true);
        if (_roster != null) _roster.Changed += Refresh;
        Refresh();
        GameFreeze.RequestPause(this);
        GameFreeze.RequestCursorUnlock(this);
    }

    private void Hide()
    {
        if (_canvas == null || !_canvas.activeSelf) return;
        CloseConfirmation();
        _canvas.SetActive(false);
        if (_roster != null) _roster.Changed -= Refresh;
        GameFreeze.ReleasePause(this);
        GameFreeze.ReleaseCursorUnlock(this);
    }

    private void Update()
    {
        if (_canvas == null || !_canvas.activeSelf) return;
        if (!CanManage) Hide();
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_confirmation != null) CloseConfirmation();
            else Hide();
        }
    }

    private void Refresh()
    {
        if (_canvas == null || !_canvas.activeSelf) return;
        if (_candidate != null && !CanAssign(_candidate)) _candidate = AssignedHero;
        _preview.Refresh(this, _tower.CurrentLevel, _candidate, "Affectation");
        _confirm.interactable = _candidate != AssignedHero && (_candidate == null || CanAssign(_candidate));
        _title.text = $"{_building.Definition.DisplayName} · Niveau {_building.CurrentLevel}\n" +
                      (AssignedHero != null ? AssignedHero.HeroName : "Forme de base · Sans héros");
        foreach (Transform row in _rows)
        {
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }

        AddChoice("Sans héros · Forme de base", null, true);
        bool found = false;
        if (_roster != null)
        {
            foreach (var hero in _roster.OrderedHeroes)
            {
                if (ProfileFor(hero) == null) continue;
                found = true;
                string status = hero.AssignedTower == this ? "Affecté à cette tour" :
                    hero.IsExpeditionPending ? "Expédition annulable" :
                    hero.IsOnExpedition ? "En expédition" :
                    hero.DefenseAssignment != null ? hero.DefenseAssignment.name : "Disponible";
                AddChoice($"{hero.HeroName} · Niveau {hero.Level}\n{status}", hero, CanAssign(hero));
            }
        }
        if (!found)
        {
            var empty = NewText("Empty", _rows, "Aucun héros compatible recruté.", 23, TextMuted);
            Layout(empty.gameObject, height: 65);
        }
    }

    private void AddChoice(string label, heros hero, bool enabled)
    {
        var button = NewButton(_rows, label, () => { _candidate = hero; Refresh(); }, fontSize: 24);
        Layout(button.gameObject, height: 78);
        button.SetActive(hero == _candidate);
        button.GetComponent<Button>().interactable = enabled;
    }

    private void ConfirmAssignment()
    {
        var hero = _candidate;
        var previousTower = hero != null ? hero.AssignedTower : null;
        var expedition = hero != null ? hero.AssignedExpedition : null;
        bool transfer = previousTower != null && previousTower != this;
        if (!transfer && expedition == null)
        {
            if (!AssignHero(hero)) _feedback.text = "Affectation indisponible.";
            return;
        }

        string source = transfer ? previousTower.name : hero.ExpeditionName;
        ShowConfirmation($"{hero.HeroName} est déjà affecté à {source}. Veux-tu changer son affectation ?", "Transférer", () =>
        {
            if (hero == null || hero.AssignedTower != previousTower || hero.AssignedExpedition != expedition ||
                !AssignHero(hero, true))
                _feedback.text = "L’affectation a changé. Sélectionne à nouveau le héros.";
        });
    }

    private void ConfirmDemolition()
    {
        ShowConfirmation($"Démolir cette tour ? L’emplacement et le héros seront libérés.\nRemboursement · {_construction.RefundLabel(_building)}",
            "Démolir", () =>
            {
                if (!_construction.TryDemolish(_building, out var reason)) _feedback.text = reason;
            });
    }

    private void ShowConfirmation(string message, string actionLabel, Action action)
    {
        CloseConfirmation();
        var overlay = NewImage("Confirmation", _canvas.transform, Dim);
        Stretch(overlay.rectTransform);
        _confirmation = overlay.gameObject;
        var card = NewImage("Dialogue", overlay.transform, CardRaised, 16);
        Anchor(card.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(650, 275));
        VStack(card.gameObject, 14, new RectOffset(24, 24, 20, 20));
        Layout(NewText("Message", card.transform, message, 26, TextMain).gameObject, flexH: 1);
        var actions = NewRect("Actions", card.transform);
        Layout(actions.gameObject, height: 55);
        HStack(actions.gameObject, 12);
        Layout(NewButton(actions, "Annuler", CloseConfirmation, fontSize: 23).gameObject, flexW: 1);
        Layout(NewButton(actions, actionLabel, () => { CloseConfirmation(); action(); }, ButtonStyle.Primary, 23).gameObject, flexW: 1);
    }

    private void CloseConfirmation()
    {
        if (_confirmation == null) return;
        _confirmation.SetActive(false);
        Destroy(_confirmation);
        _confirmation = null;
    }

    private void BuildUI()
    {
        _canvas = new GameObject("TowerHeroCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 560;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        var backdrop = NewImage("Backdrop", _canvas.transform, Dim);
        Stretch(backdrop.rectTransform);
        var panel = NewImage("Tower", backdrop.transform, Card, 16);
        Anchor(panel.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-40, 0), new Vector2(650, 670));
        _preview = new TowerStatsPreview(backdrop.transform, "TowerAssignmentStats");
        Anchor(_preview.Root, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-722, 0), new Vector2(730, 670));
        VStack(panel.gameObject, 12, new RectOffset(24, 24, 24, 24));
        _title = NewText("Title", panel.transform, "", 28, Accent);
        Layout(_title.gameObject, height: 100);
        _rows = NewScroll(panel.transform, "Heroes", 8, new RectOffset(6, 6, 6, 6), out var scroll);
        Layout(scroll.gameObject, flexH: 1);
        var confirm = NewButton(panel.transform, "Valider l’affectation", ConfirmAssignment, ButtonStyle.Primary, 25);
        _confirm = confirm.GetComponent<Button>();
        Layout(confirm.gameObject, height: 55);
        _feedback = NewText("Feedback", panel.transform, "", 21, Danger);
        Layout(_feedback.gameObject, height: 45);
        if (_upgradePanel != null || (_construction != null && _construction.CanDemolish(_building)))
        {
            var actions = NewRect("TowerActions", panel.transform);
            Layout(actions.gameObject, height: 55);
            HStack(actions.gameObject, 12);
            if (_upgradePanel != null)
            {
                var upgrade = NewButton(actions, "Améliorer la tour", () =>
                {
                    Hide();
                    _upgradePanel.ShowStandalone(_building);
                }, fontSize: 23);
                Layout(upgrade.gameObject, flexW: 1);
            }
            if (_construction != null && _construction.CanDemolish(_building))
                Layout(NewButton(actions, "Démolir", ConfirmDemolition, ButtonStyle.Danger, 23).gameObject, flexW: 1);
        }
        var close = NewButton(panel.transform, "Fermer", Hide, fontSize: 25);
        Layout(close.gameObject, height: 55);
    }
}



