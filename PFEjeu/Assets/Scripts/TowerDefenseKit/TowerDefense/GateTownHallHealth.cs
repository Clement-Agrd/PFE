using Core.HealthSystem;
using Core.Village;
using UnityEngine;
namespace Core.TowerDefense
{
    public sealed class GateTownHallHealth : MonoBehaviour
    {
        [SerializeField] private Building townHall;
        [SerializeField] private Health gateHealth;
        [SerializeField] private int[] maxHealthByLevel={1000,1500,2000,2500};
        private bool _initialized;
        private void OnEnable(){if(townHall!=null)townHall.OnLevelChanged+=Apply;}
        private void OnDisable(){if(townHall!=null)townHall.OnLevelChanged-=Apply;}
        private void Start()=>Apply(townHall!=null?townHall.CurrentLevel:1);
        private void Apply(int level)
        {
            if(gateHealth==null||gateHealth.IsDead||maxHealthByLevel==null||maxHealthByLevel.Length==0)return;
            int next=Mathf.Max(1,maxHealthByLevel[Mathf.Clamp(level-1,0,maxHealthByLevel.Length-1)]);
            int damage=gateHealth.MaxHealth-gateHealth.CurrentHealth;
            gateHealth.SetMaxHealth(next,false);
            // An upgrade adds the new capacity while preserving damage already suffered.
            gateHealth.Revive(_initialized?Mathf.Clamp(next-damage,1,next):next);
            _initialized=true;
        }
    }
}
