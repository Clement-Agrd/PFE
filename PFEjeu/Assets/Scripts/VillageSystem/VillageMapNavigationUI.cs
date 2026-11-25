using UnityEngine;
using static Core.PauseMenu.PauseUiKit;
namespace Core.Village
{
    public sealed class VillageMapNavigationUI : MonoBehaviour
    {
        [SerializeField] private VillageViewController view;
        [SerializeField] private Core.TowerDefense.DefenseBuildSlot[] slots;
        [SerializeField] private Core.Village.UI.BuildingPanelUI buildingPanel;
        private Core.TowerDefense.DefenseConstructionUI _construction;
        private GameObject _canvas;
        private RectTransform _root;
        private Core.PauseMenu.PauseButton[] _markers;
        private Core.PauseMenu.PauseButton _village,_defenses;
        private readonly System.Collections.Generic.Dictionary<Building,Core.PauseMenu.PauseButton> _buildingMarkers=new();
        private void Start()
        {
            _canvas=new GameObject("VillageMapNavigation",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas,gameObject.scene);_canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;_canvas.GetComponent<Canvas>().sortingOrder=190;
            var scale=_canvas.GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;
            _root=(RectTransform)_canvas.transform;
            Core.UiNavigationScope.Register(_root,()=>view.ExitVillageView(),this,()=>view!=null&&view.IsInVillageView,false);
            var tabs=NewRect("MapAreas",_root);Anchor(tabs,new Vector2(1,1),new Vector2(1,1),new Vector2(-26,-100),new Vector2(460,62));HStack(tabs.gameObject,12).childForceExpandWidth=true;
            _village=NewButton(tabs,"Village",()=>view.ShowVillageMap(),ButtonStyle.Tab,26);_village.name="VillageMapButton";
            _defenses=NewButton(tabs,"Défenses",()=>view.ShowDefenseMap(),ButtonStyle.Tab,26);_defenses.name="DefenseMapButton";
            _markers=new Core.PauseMenu.PauseButton[slots.Length];
            for(int i=0;i<slots.Length;i++)
            {
                var slot=slots[i];var b=NewButton(_root,"+",()=>Select(slot),ButtonStyle.Primary,30,10);b.name="Map_"+slot.name;b.Label.alignment=TMPro.TextAlignmentOptions.Center;Stretch(b.Label.rectTransform);Anchor((RectTransform)b.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(48,48));_markers[i]=b;
            }
            foreach(var building in FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                if(building.gameObject.scene!=gameObject.scene||building.Definition==null||building.GetComponent<Core.BarracksSystem.Barracks>()!=null||building.GetComponent<TowerHeroStation>()!=null)continue;
                var target=building;
                var button=NewButton(_root,"",()=>view.SelectBuilding(target),ButtonStyle.Tab,22);
                button.name="NavigateBuilding_"+building.GetEntityId();button.Label.raycastTarget=false;
                button.GetComponent<UnityEngine.UI.Image>().enabled=false;
                Anchor((RectTransform)button.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(56,56));
                _buildingMarkers.Add(building,button);
            }
            _construction=FindAnyObjectByType<Core.TowerDefense.DefenseConstructionUI>();
            if(_construction!=null)_construction.OnBuildingConstructed+=RememberConstructed;
            _canvas.SetActive(false);
        }
        private void Select(Core.TowerDefense.DefenseBuildSlot slot)
        {
            if (view != null) view.SelectDefenseSlot(slot);
        }
        private void LateUpdate()
        {
            if(_canvas==null||view==null)return;
            bool show=view.IsInVillageView;_canvas.SetActive(show);if(!show)return;
            _village.SetActive(!view.IsDefenseMap);_defenses.SetActive(view.IsDefenseMap);
            for(int i=0;i<_markers.Length;i++)
            {
                var marker=_markers[i];var slot=slots[i];
                bool visible=view.IsDefenseMap&&slot!=null&&slot.isActiveAndEnabled;
                if(visible)
                {
                    var point=view.MapCamera.WorldToScreenPoint(slot.transform.position+Vector3.up);
                    visible=point.z>0&&point.x>=0&&point.x<=Screen.width&&point.y>=0&&point.y<=Screen.height;
                    if(visible)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root,point,null,out var local);
                        ((RectTransform)marker.transform).anchoredPosition=local;
                        marker.Label.text=slot.IsOccupied?"":"+";
                        marker.Label.raycastTarget=false;
                        marker.GetComponent<UnityEngine.UI.Image>().enabled=!slot.IsOccupied;
                        marker.GetComponent<UnityEngine.UI.Button>().interactable=view.CanSelectMap;
                    }
                }
                marker.gameObject.SetActive(visible);
            }
            foreach(var entry in _buildingMarkers)
            {
                bool visible=!view.IsDefenseMap&&entry.Key!=null&&entry.Key.isActiveAndEnabled;
                if(visible)
                {
                    var point=view.MapCamera.WorldToScreenPoint(entry.Key.transform.position+Vector3.up);
                    visible=point.z>0&&point.x>=0&&point.x<=Screen.width&&point.y>=0&&point.y<=Screen.height;
                    if(visible){RectTransformUtility.ScreenPointToLocalPointInRectangle(_root,point,null,out var local);((RectTransform)entry.Value.transform).anchoredPosition=local;entry.Value.GetComponent<UnityEngine.UI.Button>().interactable=view.CanSelectMap;}
                }
                entry.Value.gameObject.SetActive(visible);
            }
        }
        private void RememberConstructed(Building building)
        {
            for(int i=0;i<slots.Length;i++)
                if(slots[i]!=null&&slots[i].Occupant==building.gameObject)
                {Core.UiNavigation.RememberSelection(_markers[i].gameObject);break;}
        }
        private void OnDisable(){if(_canvas!=null)_canvas.SetActive(false);}
        private void OnDestroy(){if(_construction!=null)_construction.OnBuildingConstructed-=RememberConstructed;if(_canvas!=null)Destroy(_canvas);}
    }
}
