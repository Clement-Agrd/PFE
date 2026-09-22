using System;
using Core.StatsSystem;
using System.Collections.Generic;
using Core.HealthSystem;
using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    [Header("Base Information")]
    [SerializeField] private string towerName;
    [SerializeField] private float detectionRange;
    public float DetectionRange => detectionRange;
    
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    public GameObject ProjectilePrefab => projectilePrefab;
    
    [SerializeField] private float fireRate;
    public float FireRate => fireRate;
    
    [SerializeField] private DamageType damageType;
    public DamageType DamageType => damageType;
    
    
    [Header("Level Information")]
    [SerializeField] private int minLevel;
    public int MinLevel => minLevel;
    [SerializeField] private int maxLevel;
    public int MaxLevel => maxLevel;
    
    [Serializable] public struct TowerStatEntry
    {
        public StatType type;
        public float baseValue;
    }
    
    [Header("Tower Stats")]
    [SerializeField] private List<TowerStatEntry> towerStats = new List<TowerStatEntry>();
    
    public void ApplyStatsTo(EntityStats target)
    {
        foreach (TowerStatEntry entry in towerStats)
        {
            target.SetBaseStat( entry.type , entry.baseValue);
        }
    }
    
#if UNITY_EDITOR
    private void Reset()
    {
        FillAllStats();
    }
#endif
    
#if UNITY_EDITOR
    [ContextMenu("Remplir toutes les stats")]
    private void FillAllStats()
    {
        Array values = Enum.GetValues(typeof(StatType));

        foreach (StatType type in values)
        {
            bool found = false;

            for (int i = 0; i < towerStats.Count; i++)
            {
                if (towerStats[i].type == type)
                {
                    found = true;
                    break;
                }
            }

            if (found) continue;

            towerStats.Add(new TowerStatEntry { type = type, baseValue = 0f });
        }
    }
#endif

    [Header("Model")] 
    public GameObject[] models;
}
