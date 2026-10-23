using System.Linq;
using Core.BarracksSystem;
using TMPro;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;
namespace Core.Village
{
    public sealed class RecruitmentCampUI : MonoBehaviour
    {
        [SerializeField] private RecruitmentCamp camp;
        [SerializeField] private VillageViewController villageView;
        [SerializeField] private Core.Village.UI.BuildingPanelUI upgradePanel;
        private GameObject _canvas;
        private TMP_Text _summary,_feedback,_heroStatus,_empty;
        private RectTransform _heroRows;
        private Core.PauseMenu.PauseButton _confirm,_dismiss;
        private ClasseHeros? _selectedClass;
        private heros _selectedHero;
        private readonly System.Collections.Generic.Dictionary<heros,Core.PauseMenu.PauseButton> _heroButtons=new();
        private readonly System.Collections.Generic.List<Core.PauseMenu.PauseButton> _buttons=new();
        public bool IsShown=>_canvas!=null&&_canvas.activeSelf;
        private void OnEnable()
        {
            if(villageView!=null){villageView.OnBuildingClicked+=Selected;villageView.OnExitedVillageView+=Hide;villageView.OnMapAreaChanged+=Hide;}
            if(camp!=null&&camp.Spawner!=null)camp.Spawner.OnWaveStarted+=DefenseStarted;
        }
        private void OnDisable()
        {
            if(villageView!=null){villageView.OnBuildingClicked-=Selected;villageView.OnExitedVillageView-=Hide;villageView.OnMapAreaChanged-=Hide;}
            if(camp!=null&&camp.Spawner!=null)camp.Spawner.OnWaveStarted-=DefenseStarted;
            Hide();
        }
        private void OnDestroy(){if(_canvas!=null)Destroy(_canvas);}
        private void Update(){if(IsShown&&Input.GetKeyDown(KeyCode.Escape))Hide();}
        private void Selected(Building building){if(camp!=null&&building==camp.Building)Show();}
        private void DefenseStarted(int _)=>Hide();
        public void Show()
        {
            if(camp==null||camp.Spawner!=null&&(camp.Spawner.IsWaveActive||camp.Spawner.IsDefeated))return;
            if(_canvas==null)BuildUI();
            if(!IsShown)
            {
                camp.Roster.Changed+=Refresh;
                if(camp.Village.HdvInventory!=null)camp.Village.HdvInventory.OnChanged+=Refresh;
            }
            _canvas.SetActive(true);Refresh();
            GameFreeze.RequestPause(this);GameFreeze.RequestCursorUnlock(this);
        }
        public void Hide()
        {
            if(IsShown&&camp!=null)
            {
                camp.Roster.Changed-=Refresh;
                if(camp.Village.HdvInventory!=null)camp.Village.HdvInventory.OnChanged-=Refresh;
            }
            if(_canvas!=null)_canvas.SetActive(false);
            GameFreeze.ReleasePause(this);GameFreeze.ReleaseCursorUnlock(this);
        }
        private void Refresh()
        {
            if(_summary==null)return;
            _summary.text=$"Niveau {camp.Building.CurrentLevel} / {camp.Building.Definition.MaxLevel} · Héros {camp.Roster.Count} / {camp.Capacity}\nChaque héros commence au niveau 1.\nCoût : "+string.Join(" · ",camp.Costs().Select(c=>$"{c.Value} {c.Key.DisplayName}"));
            _feedback.text="Sélectionne une classe, puis valide le recrutement.";
            for(int i=0;i<_buttons.Count;i++)_buttons[i].SetActive(_selectedClass==(ClasseHeros)i);
            bool canRecruit=false;
            if(_selectedClass.HasValue)
            {
                canRecruit=camp.CanRecruit(_selectedClass.Value,out var reason);
                _feedback.text=canRecruit?"Classe choisie : "+ClassName(_selectedClass.Value):reason;
            }
            SetEnabled(_confirm,canRecruit);
            foreach(var entry in _heroButtons.Where(e=>e.Key==null||!camp.Roster.Heroes.Contains(e.Key)).ToArray())
            {
                entry.Value.gameObject.SetActive(false);Destroy(entry.Value.gameObject);_heroButtons.Remove(entry.Key);
            }
            if(_selectedHero!=null&&!camp.Roster.Heroes.Contains(_selectedHero))_selectedHero=null;
            int index=0;
            foreach(var hero in camp.Roster.OrderedHeroes.Where(h=>h!=null))
            {
                if(!_heroButtons.TryGetValue(hero,out var row))
                {
                    var captured=hero;
                    row=NewButton(_heroRows,"",()=>{_selectedHero=captured;Refresh();},fontSize:24);
                    row.name="CampHero_"+hero.GetEntityId();Layout(row.gameObject,height:104);
                    row.Label.textWrappingMode=TextWrappingModes.Normal;
                    _heroButtons.Add(hero,row);
                }
                row.transform.SetSiblingIndex(index++);
                row.Label.text=$"{hero.HeroName} · {hero.ClasseName} · Niv. {hero.Level}\n{Assignment(hero)}";
                row.SetActive(_selectedHero==hero);
            }
            _empty.gameObject.SetActive(camp.Roster.Count==0);
            bool canDismiss=camp.CanDismissHero(_selectedHero,out var dismissReason);
            SetEnabled(_dismiss,canDismiss);
            _heroStatus.text=_selectedHero==null?"Sélectionne un héros pour voir son affectation.":canDismiss?$"{_selectedHero.HeroName} est disponible.\nLe renvoi ne rembourse pas le recrutement.":dismissReason;
        }
        private static string ClassName(ClasseHeros type)=>type==ClasseHeros.Degats?"Dégâts":type.ToString();
        private static string Assignment(heros hero)
        {
            if(hero.AssignedBarracks!=null)return "Caserne : "+hero.AssignedBarracks.name;
            if(hero.IsOnExpedition)return (hero.IsExpeditionPending?"Expédition prévue : ":"En expédition : ")+hero.ExpeditionName;
            return "Disponible";
        }
        private static void SetEnabled(Core.PauseMenu.PauseButton button,bool enabled)
        {
            button.GetComponent<UnityEngine.UI.Button>().interactable=enabled;
            button.GetComponent<CanvasGroup>().alpha=enabled?1:.45f;
        }
        private void Recruit()
        {
            if(!_selectedClass.HasValue)return;
            if(camp.TryRecruit(_selectedClass.Value,out var hero,out var reason))
            {
                _selectedHero=hero;Refresh();_feedback.text=$"{hero.HeroName} recruté · Niveau 1";
            }
            else{Refresh();_feedback.text=reason;}
        }
        private void Dismiss()
        {
            string name=_selectedHero!=null?_selectedHero.HeroName:"";
            if(camp.TryDismissHero(_selectedHero,out var reason))
            {
                _selectedHero=null;Refresh();_heroStatus.text=name+" a quitté le village.";
            }
            else{Refresh();_heroStatus.text=reason;}
        }
        private void Upgrade(){Hide();if(upgradePanel!=null)upgradePanel.ShowStandalone(camp.Building);}
        private void BuildUI()
        {
            _canvas=new GameObject("RecruitmentCampCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas,gameObject.scene);_canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;_canvas.GetComponent<Canvas>().sortingOrder=560;
            var scale=_canvas.GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;
            var dim=NewImage("Backdrop",_canvas.transform,Dim);Stretch(dim.rectTransform);dim.raycastTarget=true;
            var card=NewImage("Camp",dim.transform,Card,20);Anchor(card.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1640,940));
            var title=NewText("Title",card.transform,"Camp de recrutement",36,Accent,TextAlignmentOptions.Center,FontStyles.Bold);Anchor(title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-22),new Vector2(1500,65));
            var left=NewRect("Recruitment",card.transform);Anchor(left,new Vector2(0,1),new Vector2(0,1),new Vector2(38,-105),new Vector2(740,710));
            var right=NewRect("VillageHeroes",card.transform);Anchor(right,new Vector2(1,1),new Vector2(1,1),new Vector2(-38,-105),new Vector2(770,710));
            _summary=NewText("Summary",left,"",25,TextMain,TextAlignmentOptions.TopLeft);Anchor(_summary.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(710,125));
            var choices=NewRect("Classes",left);Anchor(choices,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-145),new Vector2(710,330));VStack(choices.gameObject,14);
            foreach(var type in new[]{ClasseHeros.Tank,ClasseHeros.Degats,ClasseHeros.Invocateur})
            {
                string description=type==ClasseHeros.Tank?"Plus résistants, moins de dégâts":type==ClasseHeros.Degats?"Plus de dégâts, moins résistants":"Plus de soldats, statistiques réduites";
                var choice=NewButton(choices,ClassName(type)+"\n"+description,()=>{_selectedClass=type;Refresh();},fontSize:25);
                choice.name="Recruit_"+type;Layout(choice.gameObject,height:96);_buttons.Add(choice);
            }
            _feedback=NewText("Feedback",left,"",24,TextMuted,TextAlignmentOptions.Center);Anchor(_feedback.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-490),new Vector2(710,85));
            _confirm=NewButton(left,"Valider le recrutement",Recruit,ButtonStyle.Primary,27);_confirm.name="ConfirmRecruit";_confirm.gameObject.AddComponent<CanvasGroup>();Anchor((RectTransform)_confirm.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,20),new Vector2(710,72));
            var heading=NewText("HeroesTitle",right,"Héros du village",29,Accent,TextAlignmentOptions.Left,FontStyles.Bold);Anchor(heading.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(730,55));
            _heroRows=NewScroll(right,"HeroesScroll",12,new RectOffset(12,12,8,8),out var scroll);Anchor((RectTransform)scroll.transform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-65),new Vector2(745,425));
            _empty=NewText("EmptyHeroes",_heroRows,"Aucun héros recruté.",25,TextMuted);Layout(_empty.gameObject,height:90);
            _heroStatus=NewText("HeroStatus",right,"",24,TextMuted,TextAlignmentOptions.Center);Anchor(_heroStatus.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-500),new Vector2(730,90));
            _dismiss=NewButton(right,"Renvoyer ce héros",Dismiss,ButtonStyle.Danger,26);_dismiss.name="DismissHero";_dismiss.gameObject.AddComponent<CanvasGroup>();Anchor((RectTransform)_dismiss.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,20),new Vector2(710,72));
            var upgrade=NewButton(card.transform,"Améliorer le camp",Upgrade,ButtonStyle.Secondary,25);upgrade.name="UpgradeCamp";Anchor((RectTransform)upgrade.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(-390,30),new Vector2(710,65));
            var close=NewButton(card.transform,"Fermer",Hide,fontSize:25);Anchor((RectTransform)close.transform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(395,30),new Vector2(710,65));
            _canvas.SetActive(false);
        }
    }
}
