using System.Collections.Generic;
using System.Linq;
using Core.InventorySystem;
using Core.Village;
using Core.WaveSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;
namespace Core.TowerDefense
{
    public sealed class DefenseConstructionUI : MonoBehaviour
    {
        [SerializeField] private VillageManager village;
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private WaveSpawner spawner;
        [SerializeField] private DefenseBuildingDefinition[] catalog;
        [SerializeField] private Transform buildingsRoot;
        [SerializeField] private Core.Village.UI.BuildingPanelUI upgradePanel;
        [SerializeField] private bool trainingMode;
        private GameObject _canvas;
        private RectTransform _rows;
        private RectTransform _card;
        private Vector2 _defaultCardPosition;
        private TMP_Text _feedback;
        private DefenseBuildSlot _slot;
        private readonly Dictionary<Building, DefenseBuildSlot> _towerSlots = new();
        private readonly Dictionary<DefenseBuildingDefinition, Core.PauseMenu.PauseButton> _choices = new();
        private DefenseBuildingDefinition _selection;
        private Button _confirm;
        private DefenseConstructionPreview _preview;
        public bool CanBuildNow=>trainingMode||(spawner!=null&&!spawner.IsWaveActive&&!spawner.IsDefeated);
        public bool IsOpen=>_canvas!=null&&_canvas.activeSelf;
        public IReadOnlyList<DefenseBuildingDefinition> Catalog=>catalog;
        public event System.Action<Building> OnBuildingConstructed;
        private void OnEnable(){if(village!=null)village.OnBuildingUpgraded+=RecordTowerUpgrade;if(spawner!=null)spawner.OnWaveStarted+=OnDefenseStarted;if(villageView!=null){villageView.OnMapAreaChanged+=Close;villageView.OnMapSelectionChanging+=Close;villageView.OnExitedVillageView+=Close;}}
        private void OnDisable(){if(village!=null)village.OnBuildingUpgraded-=RecordTowerUpgrade;if(spawner!=null)spawner.OnWaveStarted-=OnDefenseStarted;if(villageView!=null){villageView.OnMapAreaChanged-=Close;villageView.OnMapSelectionChanging-=Close;villageView.OnExitedVillageView-=Close;}Close();}
        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas);
            _preview?.Dispose();
        }
        private void Update(){if(IsOpen&&Input.GetKeyDown(KeyCode.Escape))Close();}
        private void LateUpdate()
        {
            if (IsOpen && _slot != null) PositionCard();
        }

        private void PositionCard()
        {
            if (Core.Village.UI.MapPanelLayout.Place(villageView, _slot.BuildAnchor.position, _card)) return;
            _card.anchoredPosition = _defaultCardPosition;
            _card.localScale = Vector3.one;
        }

        private void OnDefenseStarted(int _){Core.BarracksSystem.BarracksFlagPlacement.CancelActivePlacement();Close();}
        public void Open(DefenseBuildSlot slot)
        {
            if(!CanBuildNow||slot==null||slot.IsOccupied||!slot.BelongsTo(this))return;
            Core.BarracksSystem.BarracksFlagPlacement.CancelActivePlacement();
            if(_canvas==null)BuildUI();
            if (_slot != slot) _selection = null;
            _slot = slot;
            _canvas.SetActive(true);
            Select(_selection);
            PositionCard();
            if (village != null && village.HdvInventory != null)
            {
                village.HdvInventory.OnChanged -= RefreshSelection;
                village.HdvInventory.OnChanged += RefreshSelection;
            }
            GameFreeze.RequestPause(this);GameFreeze.RequestCursorUnlock(this);
        }
        public void Close()
        {
            if(_canvas!=null)_canvas.SetActive(false);
            if (village != null && village.HdvInventory != null)
                village.HdvInventory.OnChanged -= RefreshSelection;
            _slot = null;
            _selection = null;
            _preview?.Clear();
            GameFreeze.ReleasePause(this);GameFreeze.ReleaseCursorUnlock(this);
        }
        private Dictionary<ItemDefinition,int> Costs(DefenseBuildingDefinition def)
        {
            var result=new Dictionary<ItemDefinition,int>();
            if(trainingMode)return result;
            if(def.costs!=null)foreach(var c in def.costs)
                if(c.item!=null&&c.amount>0)result[c.item]=result.GetValueOrDefault(c.item)+c.amount;
            return result;
        }
        public bool CanBuild(DefenseBuildSlot slot,DefenseBuildingDefinition def,out string reason)
        {
            reason="";
            if(!CanBuildNow)reason="Construction disponible entre les défenses.";
            else if(slot==null||!slot.isActiveAndEnabled||!slot.BelongsTo(this)||slot.IsOccupied)reason="Emplacement indisponible.";
            else if(def==null||catalog==null||!catalog.Contains(def)||def.prefab==null)reason="Défense indisponible.";
            else if(trainingMode)return true;
            else if(village==null||village.HdvInventory==null)reason="Stockage HDV indisponible.";
            else if(village.TownHallLevel<def.requiredTownHallLevel)reason=$"HDV niveau {def.requiredTownHallLevel} requis.";
            else
            {
                if(def.costs!=null&&def.costs.Any(c=>c.item==null||c.amount<=0))reason="Coût de construction invalide.";
                else foreach(var c in Costs(def))if(!village.HdvInventory.Contains(c.Key,c.Value)){reason=$"Pas assez de {c.Key.DisplayName}.";break;}
            }
            return reason.Length==0;
        }
        public bool TryBuild(DefenseBuildSlot slot,DefenseBuildingDefinition def,out string reason)
        {
            if(!CanBuild(slot,def,out reason))return false;
            var anchor=slot.BuildAnchor;
            var building=Instantiate(def.prefab,anchor.position+anchor.rotation*def.placementOffset,anchor.rotation,buildingsRoot);
            building.name=def.displayName+" · "+slot.name;
            if(building.TryGetComponent<Core.BarracksSystem.BarracksVillageUpgrade>(out var upgrade))upgrade.Configure(upgradePanel);
            if(building.TryGetComponent<TowerHeroStation>(out var tower))tower.Configure(this, upgradePanel, villageView);
            if(building.TryGetComponent<Building>(out _))
            {
                var highlight=building.GetComponent<BuildingHighlight>();
                if(highlight==null)highlight=building.AddComponent<BuildingHighlight>();
                highlight.Configure(villageView);
            }
            var obstacle=building.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if(obstacle==null)obstacle=building.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape=UnityEngine.AI.NavMeshObstacleShape.Box;obstacle.size=def.footprint;obstacle.center=Vector3.up*(def.footprint.y*.5f);obstacle.carving=true;obstacle.carveOnlyStationary=false;
            slot.Investment.Clear();
            foreach(var c in Costs(def))
                slot.Investment[c.Key] = village.HdvInventory.Remove(c.Key,c.Value);
            slot.Occupy(building);
            if(building.TryGetComponent<Building>(out var constructed))
            {
                if(tower != null) _towerSlots[constructed] = slot;
                OnBuildingConstructed?.Invoke(constructed);
            }
            Close();
            if(building.TryGetComponent<Core.BarracksSystem.BarracksFlagPlacement>(out var placement))
            {
                var actor=FindFirstObjectByType<Core.InteractionSystem.Interactor>();
                placement.BeginConstructionPlacement(actor!=null?actor.gameObject:null,villageView);
            }
            return true;
        }
        private void RecordTowerUpgrade(Building building, int level)
        {
            if (!_towerSlots.TryGetValue(building, out var slot)) return;
            var costs = building.Definition.GetLevelData(level)?.upgradeCost;
            if (costs == null) return;
            foreach (var cost in costs)
                if (cost.item != null && cost.amount > 0)
                    slot.Investment[cost.item] = slot.Investment.GetValueOrDefault(cost.item) + cost.amount;
        }

        private Dictionary<ItemDefinition, int> GetRefund(Building building)
        {
            var refund = new Dictionary<ItemDefinition, int>();
            if (!_towerSlots.TryGetValue(building, out var slot)) return refund;
            foreach (var cost in slot.Investment)
                if (cost.Value >= 2) refund[cost.Key] = cost.Value / 2;
            return refund;
        }

        public bool CanDemolish(Building building)
            => CanBuildNow && building != null && _towerSlots.TryGetValue(building, out var slot) &&
                slot.Occupant == building.gameObject;

        public string RefundLabel(Building building)
        {
            var refund = GetRefund(building);
            return refund.Count > 0
                ? string.Join(" · ", refund.Select(entry => $"{entry.Value} {entry.Key.DisplayName}"))
                : "Aucun remboursement";
        }

        public bool TryDemolish(Building building, out string reason)
        {
            reason = "Cette tour ne peut pas être démolie maintenant.";
            if (!CanDemolish(building)) return false;

            var refund = GetRefund(building);
            var inventory = village != null ? village.HdvInventory : null;
            if (refund.Count > 0 && (inventory == null || !RefundFits(inventory, refund)))
            {
                reason = "Le stockage de l’hôtel de ville manque de place pour le remboursement.";
                return false;
            }

            var slot = _towerSlots[building];
            _towerSlots.Remove(building);
            building.gameObject.SetActive(false);
            Destroy(building.gameObject);
            slot.Release();
            foreach (var entry in refund) inventory.Add(entry.Key, entry.Value);
            reason = "";
            return true;
        }

        private static bool RefundFits(Inventory inventory, Dictionary<ItemDefinition, int> refund)
        {
            int freeSlots = 0;
            int requiredSlots = 0;
            for (int i = inventory.ReservedCount; i < inventory.Capacity; i++)
                if (inventory.GetSlot(i) == null) freeSlots++;

            foreach (var entry in refund)
            {
                int remaining = entry.Value;
                if (entry.Key.IsStackable)
                    for (int i = inventory.ReservedCount; i < inventory.Capacity && remaining > 0; i++)
                    {
                        var stack = inventory.GetSlot(i);
                        if (stack != null && stack.Matches(entry.Key) && stack.UpgradeLevel == 0)
                            remaining -= stack.SpaceLeft;
                    }

                if (remaining > 0) requiredSlots += 1 + (remaining - 1) / entry.Key.MaxStackSize;
            }
            return requiredSlots <= freeSlots;
        }

        private void Select(DefenseBuildingDefinition definition)
        {
            _selection = definition;
            _preview?.Clear();
            if (definition != null && _slot != null)
            {
                _preview ??= new DefenseConstructionPreview();
                _preview.Show(_slot.BuildAnchor, definition);
            }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (var entry in _choices)
                entry.Value.SetActive(entry.Key == _selection);

            if (_selection == null)
            {
                _confirm.interactable = false;
                _feedback.text = "Choisis un bâtiment.";
                return;
            }

            _confirm.interactable = CanBuild(_slot, _selection, out var reason);
            _feedback.text = _confirm.interactable ? "Aperçu · Valide pour construire" : reason;
        }

        private void ConfirmConstruction()
        {
            if (!TryBuild(_slot, _selection, out var reason))
            {
                RefreshSelection();
                _feedback.text = reason;
            }
        }

        private void BuildUI()
        {
            _canvas = new GameObject("DefenseConstructionCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var card = NewImage("Construction", _canvas.transform, Card, 16);
            card.raycastTarget = true;
            Anchor(card.rectTransform, new Vector2(1f, .5f), new Vector2(1f, .5f),
                new Vector2(-28f, 0f), new Vector2(390f, 440f));
            _card = card.rectTransform;
            _defaultCardPosition = _card.anchoredPosition;
            VStack(card.gameObject, 9f, new RectOffset(16, 16, 15, 15));
            Layout(NewText("Title", card.transform, "Construire une défense", 26, Accent,
                TextAlignmentOptions.Left, FontStyles.Bold).gameObject, height: 39f);

            _rows = NewScroll(card.transform, "BuildChoices", 5f, new RectOffset(2, 2, 2, 2), out var scroll);
            Layout(scroll.gameObject, flexH: 1f);
            foreach (var definition in catalog ?? System.Array.Empty<DefenseBuildingDefinition>())
            {
                if (definition == null || _choices.ContainsKey(definition)) continue;
                string cost = string.Join(" · ", Costs(definition).Select(c => $"{c.Value} {c.Key.DisplayName}"));
                string requirements = trainingMode ? "Gratuit" :
                    $"{(cost.Length > 0 ? cost : "Gratuit")} · HDV {definition.requiredTownHallLevel}";
                var choice = NewButton(_rows, $"{definition.displayName}\n{requirements}", () => Select(definition), fontSize: 21);
                choice.name = "Build_" + definition.name;
                choice.Label.textWrappingMode = TextWrappingModes.Normal;
                Layout(choice.gameObject, height: 66f);
                _choices.Add(definition, choice);
            }

            _feedback = NewText("Feedback", card.transform, "", 19, TextMuted);
            Layout(_feedback.gameObject, height: 50f);
            var actions = NewRect("Actions", card.transform);
            Layout(actions.gameObject, height: 49f);
            HStack(actions.gameObject, 8f);
            var cancel = NewButton(actions, "Annuler", Close, fontSize: 20);
            Layout(cancel.gameObject, flexW: 1f);
            var confirm = NewButton(actions, "Valider", ConfirmConstruction, ButtonStyle.Primary, 20);
            confirm.name = "ConfirmDefenseConstruction";
            Layout(confirm.gameObject, flexW: 1f);
            _confirm = confirm.GetComponent<Button>();
        }
    }
}
