using System.Collections.Generic;
using System.Linq;
using Core.InventorySystem;
using Core.Village;
using Core.WaveSystem;
using TMPro;
using UnityEngine;
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
        private GameObject _canvas;
        private RectTransform _rows;
        private TMP_Text _feedback;
        private DefenseBuildSlot _slot;
        private readonly List<GameObject> _choices=new();
        public bool CanBuildNow=>spawner!=null&&!spawner.IsWaveActive&&!spawner.IsDefeated;
        public bool IsOpen=>_canvas!=null&&_canvas.activeSelf;
        public IReadOnlyList<DefenseBuildingDefinition> Catalog=>catalog;
        private void OnEnable(){if(spawner!=null)spawner.OnWaveStarted+=OnDefenseStarted;if(villageView!=null){villageView.OnMapAreaChanged+=Close;villageView.OnExitedVillageView+=Close;}}
        private void OnDisable(){if(spawner!=null)spawner.OnWaveStarted-=OnDefenseStarted;if(villageView!=null){villageView.OnMapAreaChanged-=Close;villageView.OnExitedVillageView-=Close;}Close();}
        private void OnDestroy(){if(_canvas!=null)Destroy(_canvas);}
        private void Update(){if(IsOpen&&Input.GetKeyDown(KeyCode.Escape))Close();}
        private void OnDefenseStarted(int _)=>Close();
        public void Open(DefenseBuildSlot slot)
        {
            if(!CanBuildNow||slot==null||slot.IsOccupied||!slot.BelongsTo(this))return;
            if(_canvas==null)BuildUI();
            _slot=slot;
            _canvas.SetActive(true);
            Refresh();
            GameFreeze.RequestPause(this);GameFreeze.RequestCursorUnlock(this);
        }
        public void Close()
        {
            if(_canvas!=null)_canvas.SetActive(false);
            _slot=null;
            GameFreeze.ReleasePause(this);GameFreeze.ReleaseCursorUnlock(this);
        }
        private Dictionary<ItemDefinition,int> Costs(DefenseBuildingDefinition def)
        {
            var result=new Dictionary<ItemDefinition,int>();
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
            if(building.TryGetComponent<Building>(out _))
            {
                var highlight=building.GetComponent<BuildingHighlight>();
                if(highlight==null)highlight=building.AddComponent<BuildingHighlight>();
                highlight.Configure(villageView);
            }
            var obstacle=building.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if(obstacle==null)obstacle=building.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape=UnityEngine.AI.NavMeshObstacleShape.Box;obstacle.size=def.footprint;obstacle.center=Vector3.up*(def.footprint.y*.5f);obstacle.carving=true;obstacle.carveOnlyStationary=false;
            foreach(var c in Costs(def))village.HdvInventory.Remove(c.Key,c.Value);
            slot.Occupy(building);
            Close();
            return true;
        }
        private void Refresh()
        {
            foreach(var c in _choices){c.SetActive(false);Destroy(c);}_choices.Clear();
            _feedback.text="Ressources prélevées dans le stockage de l’hôtel de ville.";
            if(catalog==null)return;
            foreach(var def in catalog.Where(d=>d!=null))
            {
                bool enabled=CanBuild(_slot,def,out var reason);
                string cost=string.Join(" · ",Costs(def).Select(c=>$"{c.Value} {c.Key.DisplayName}"));
                var button=NewButton(_rows,$"{def.displayName}\n{(cost.Length>0?cost:"Gratuit")} · HDV {def.requiredTownHallLevel}"+(enabled?"":$"\n{reason}"),()=>
                {
                    if(!TryBuild(_slot,def,out var failure)){Refresh();_feedback.text=failure;}
                },fontSize:24);
                Layout(button.gameObject,height:150);
                button.Label.textWrappingMode=TextWrappingModes.Normal;
                button.name="Build_"+def.name;
                button.GetComponent<UnityEngine.UI.Button>().interactable=enabled;
                button.gameObject.AddComponent<CanvasGroup>().alpha=enabled?1:.5f;
                _choices.Add(button.gameObject);
            }
        }
        private void BuildUI()
        {
            _canvas=new GameObject("DefenseConstructionCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas,gameObject.scene);
            _canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;_canvas.GetComponent<Canvas>().sortingOrder=550;
            var scaler=_canvas.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var dim=NewImage("Backdrop",_canvas.transform,Dim);Stretch(dim.rectTransform);dim.raycastTarget=true;
            var card=NewImage("Construction",dim.transform,Card,20);Anchor(card.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1020,800));
            var title=NewText("Title",card.transform,"Construire une défense",34,Accent,TextAlignmentOptions.Center,FontStyles.Bold);Anchor(title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-24),new Vector2(920,60));
            var scroll=NewRect("BuildScroll",card.transform);Stretch(scroll,40);scroll.offsetMin=new Vector2(40,190);scroll.offsetMax=new Vector2(-40,-110);
            var view=NewImage("Viewport",scroll,new Color(1,1,1,.01f));Stretch(view.rectTransform);view.raycastTarget=true;view.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            _rows=NewRect("Content",view.transform);_rows.anchorMin=new Vector2(0,1);_rows.anchorMax=Vector2.one;_rows.pivot=new Vector2(.5f,1);_rows.sizeDelta=Vector2.zero;VStack(_rows.gameObject,14);_rows.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var sr=scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();sr.content=_rows;sr.viewport=view.rectTransform;sr.horizontal=false;sr.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;sr.scrollSensitivity=40;
            _feedback=NewText("Feedback",card.transform,"",22,TextMuted,TextAlignmentOptions.Center);Anchor(_feedback.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,115),new Vector2(920,65));
            var close=NewButton(card.transform,"Annuler",Close,fontSize:26);Anchor((RectTransform)close.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,30),new Vector2(400,60));
        }
    }
}
