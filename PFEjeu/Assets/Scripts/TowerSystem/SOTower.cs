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

    [Serializable]
    public class TowerLevelStats
    {
        public int level;

        public List<TowerStatEntry> stats = new List<TowerStatEntry>();
    }
    
    [Header("Tower Stats")]
    [SerializeField] private List<TowerLevelStats> towerStats = new List<TowerLevelStats>();
    
    public void ApplyStatsTo(EntityStats target, int level)
    {
        int index = level - minLevel;

        if (index < 0 || index >= towerStats.Count)
        {
            Debug.LogError("Le niveau " + level + " n'existe pas dans le SOTower.");
            return;
        }

        foreach (TowerStatEntry entry in towerStats[index].stats)
        {
            target.SetBaseStat(entry.type, entry.baseValue);
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

        int levelCount = maxLevel - minLevel + 1;
        
        if (levelCount <= 0)
        {
            Debug.LogError("Max Level doit être supérieur ou égal à Min Level.");
            return;
        }

        // Crée les niveaux manquants
        while (towerStats.Count < levelCount)
        {
            int level = minLevel + towerStats.Count;

            towerStats.Add(new TowerLevelStats
            {
                level = level
            });
        }

        // Supprime les niveaux en trop
        while (towerStats.Count > levelCount)
        {
            towerStats.RemoveAt(towerStats.Count - 1);
        }

        // Remplit chaque niveau avec toutes les stats
        for (int levelIndex = 0; levelIndex < towerStats.Count; levelIndex++)
        {
            TowerLevelStats levelStats = towerStats[levelIndex];

            // Définit le numéro du niveau
            levelStats.level = minLevel + levelIndex;

            foreach (StatType type in values)
            {
                bool found = false;

                for (int i = 0; i < levelStats.stats.Count; i++)
                {
                    if (levelStats.stats[i].type == type)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    continue;

                levelStats.stats.Add(new TowerStatEntry
                {
                    type = type,
                    baseValue = 0f
                });
            }
        }
    }
#endif

    [Header("Model")] 
    public GameObject[] models;
}
