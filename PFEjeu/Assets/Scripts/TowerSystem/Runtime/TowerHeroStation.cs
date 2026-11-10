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

    private bool CanAssign(heros hero)
        => CanManage && hero != null && hero.isActiveAndEnabled && ProfileFor(hero) != null &&
           (hero.DefenseAssignment == null || hero.AssignedTower == this) &&
           (!hero.IsOnExpedition || hero.IsExpeditionPending);

    public bool AssignHero(heros hero)
    {
        if (!CanManage) return false;
        if (hero == AssignedHero) return true;
        if (hero == null)
        {
            ClearHero();
            return true;
        }
        if (!CanAssign(hero) || !hero.TryClaimTower(this)) return false;
        var previous = AssignedHero;
        AssignedHero = hero;
        ApplyProfile(ProfileFor(hero));
        if (previous != null) previous.ReleaseTower(this);
        Refresh();
        return true;
    }

    private void ApplyProfile(SOTower profile)
    {
        _tower.SetSpecialization(profile);
        if (TryGetComponent<BuildingHighlight>(out var highlight)) highlight.RefreshModel();
    }

    internal void ClearHero()
    {
        var previous = AssignedHero;
        AssignedHero = null;
        if (_tower != null) ApplyProfile(null);
        if (previous != null) previous.ReleaseTower(this);
    }

    public override bool CanInteract(GameObject interactor) => CanManage;
    public override void Interact(GameObject interactor) => Show();

    public void Show()
    {
        if (!CanManage || (_canvas != null && _canvas.activeSelf)) return;
        _roster = FindFirstObjectByType<HeroRoster>();
        if (_canvas == null) BuildUI();
        _canvas.SetActive(true);
        if (_roster != null) _roster.Changed += Refresh;
        Refresh();
        GameFreeze.RequestPause(this);
        GameFreeze.RequestCursorUnlock(this);
    }

    private void Hide()
    {
        if (_canvas == null || !_canvas.activeSelf) return;
        _canvas.SetActive(false);
        if (_roster != null) _roster.Changed -= Refresh;
        GameFreeze.ReleasePause(this);
        GameFreeze.ReleaseCursorUnlock(this);
    }

    private void Update()
    {
        if (_canvas != null && _canvas.activeSelf && (!CanManage || Input.GetKeyDown(KeyCode.Escape))) Hide();
    }

    private void Refresh()
    {
        if (_canvas == null || !_canvas.activeSelf) return;
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
        var button = NewButton(_rows, label, () => AssignHero(hero), fontSize: 24);
        Layout(button.gameObject, height: 78);
        button.SetActive(hero == AssignedHero);
        button.GetComponent<Button>().interactable = enabled;
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
        VStack(panel.gameObject, 12, new RectOffset(24, 24, 24, 24));
        _title = NewText("Title", panel.transform, "", 28, Accent);
        Layout(_title.gameObject, height: 100);
        _rows = NewScroll(panel.transform, "Heroes", 8, new RectOffset(6, 6, 6, 6), out var scroll);
        Layout(scroll.gameObject, flexH: 1);
        if (_upgradePanel != null)
        {
            var upgrade = NewButton(panel.transform, "Améliorer la tour", () =>
            {
                Hide();
                _upgradePanel.ShowStandalone(_building);
            }, fontSize: 25);
            Layout(upgrade.gameObject, height: 55);
        }
        var close = NewButton(panel.transform, "Fermer", Hide, fontSize: 25);
        Layout(close.gameObject, height: 55);
    }
}



