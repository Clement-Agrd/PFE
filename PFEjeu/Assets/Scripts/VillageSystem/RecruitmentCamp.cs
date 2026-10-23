using System;
using System.Collections.Generic;
using Core.BarracksSystem;
using Core.InventorySystem;
using Core.InteractionSystem;
using Core.WaveSystem;
using UnityEngine;
namespace Core.Village
{
    [RequireComponent(typeof(Building))]
    public sealed class RecruitmentCamp : InteractableBase
    {
        [SerializeField] private HeroRoster roster;
        [SerializeField] private VillageManager village;
        [SerializeField] private WaveSpawner spawner;
        [SerializeField] private RecruitmentCampUI panel;
        [SerializeField] private int[] capacities={3,6,9};
        [SerializeField] private ResourceCost[] recruitmentCost;
        private Building _building;
        public Building Building => _building != null ? _building : GetComponent<Building>();
        public HeroRoster Roster=>roster;
        public VillageManager Village=>village;
        public WaveSpawner Spawner=>spawner;
        public int Capacity=>capacities!=null&&capacities.Length>0?Mathf.Max(0,capacities[Mathf.Clamp(Building.CurrentLevel-1,0,capacities.Length-1)]):0;
        public event Action CapacityChanged;
        public override string Prompt=>"Recruter des héros";
        private void Awake()=>_building=GetComponent<Building>();
        private void OnEnable()=>Building.OnLevelChanged+=LevelChanged;
        private void OnDisable()=>Building.OnLevelChanged-=LevelChanged;
        private void LevelChanged(int _)=>CapacityChanged?.Invoke();
        public override bool CanInteract(GameObject actor)=>isActiveAndEnabled&&panel!=null&&!GameFreeze.IsPaused&&(spawner==null||!spawner.IsWaveActive&&!spawner.IsDefeated);
        public override void Interact(GameObject actor){if(CanInteract(actor))panel.Show();}
        public Dictionary<ItemDefinition,int> Costs()
        {
            var result=new Dictionary<ItemDefinition,int>();
            if(recruitmentCost!=null)foreach(var c in recruitmentCost)if(c.item!=null&&c.amount>0)result[c.item]=result.GetValueOrDefault(c.item)+c.amount;
            return result;
        }
        public bool CanRecruit(ClasseHeros type,out string reason)
        {
            reason="";
            if(!isActiveAndEnabled||!Building.IsAvailable)reason="Camp indisponible.";
            else if((int)type<0||(int)type>2)reason="Classe invalide.";
            else if(spawner!=null&&(spawner.IsWaveActive||spawner.IsDefeated))reason="Recrutement disponible entre les défenses.";
            else if(roster==null||roster.Count>=Capacity||!roster.HasRecruitmentSpace)reason=$"Capacité atteinte : {Capacity} héros. Améliore le camp.";
            else if(village==null||village.HdvInventory==null)reason="Stockage HDV indisponible.";
            else
            {
                foreach(var c in Costs())if(!village.HdvInventory.Contains(c.Key,c.Value)){reason=$"Pas assez de {c.Key.DisplayName}.";break;}
            }
            return reason.Length==0;
        }
        public bool CanDismissHero(heros hero, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || !Building.IsAvailable) reason = "Camp indisponible.";
            else if (spawner != null && (spawner.IsWaveActive || spawner.IsDefeated)) reason = "Renvoi disponible entre les défenses.";
            else if (hero == null || roster == null || !System.Linq.Enumerable.Contains(roster.Heroes, hero)) reason = "Sélectionne un héros du village.";
            else if (hero.HasAssignment) reason = "Libère d’abord ce héros de son affectation.";
            return reason.Length == 0;
        }
        public bool TryDismissHero(heros hero, out string reason)
        {
            if (!CanDismissHero(hero, out reason)) return false;
            roster.RemoveHero(hero);
            return true;
        }
        public bool TryRecruit(ClasseHeros type,out heros hero,out string reason)
        {
            hero=null;
            if(!CanRecruit(type,out reason))return false;
            hero=roster.Recruit(type,1);
            if(hero==null){reason="Capacité de héros atteinte.";return false;}
            foreach(var c in Costs())village.HdvInventory.Remove(c.Key,c.Value);
            return true;
        }
    }
}
