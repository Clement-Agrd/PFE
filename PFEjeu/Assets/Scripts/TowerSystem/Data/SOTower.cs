using System;
using Core.StatsSystem;
using System.Collections.Generic;
using Core.HealthSystem;
using Core.PoolingSystem.Examples;
using UnityEngine;
using StatType = Core.StatsSystem.EnumStats.StatTypes;

[CreateAssetMenu(fileName = "SOTower", menuName = "Scriptable Objects/SOTower")]
public class SOTower : ScriptableObject
{
    public enum TypeTower
    {
        Physical,
        Magic,
    }
    
    [Header("Base Information")]
    [SerializeField] private string towerName;
    [SerializeField] private float detectionRange;
    public float DetectionRange => detectionRange;
    public string TowerName => string.IsNullOrWhiteSpace(towerName) ? name : towerName;

    [SerializeField, Range(0, 1)] private float armorPenetration;
    public float ArmorPenetration => Mathf.Clamp01(armorPenetration);
    [SerializeField, Min(0)] private float impactRadius;
    public float ImpactRadius => impactRadius;
    [SerializeField] private GameObject impactPrefab;
    public GameObject ImpactPrefab => impactPrefab;
    [SerializeField] private string effectDescription;
    public string EffectDescription => effectDescription;
    
    [SerializeField] private TypeTower towerType;
    public TypeTower TowerType => towerType;

    [SerializeField] private SOEtat etat;
    public SOEtat ETAT => etat;
    
    [Header("IA")]
    [SerializeField] private EnumTargetPriority.TargetPriority targetCondition1;
    public EnumTargetPriority.TargetPriority TargetPriority => targetCondition1;
    
    [SerializeField] private EnumTargetPriority.TargetPriority targetCondition2;
    public EnumTargetPriority.TargetPriority TargetPriority2 => targetCondition2;
    
    [SerializeField] private EnumTargetDistance.TargetDistance targetDistance;
    public EnumTargetDistance.TargetDistance TargetDistance => targetDistance;
    
    [SerializeField] private LayerMask targetLayer;
    public LayerMask TargetLayer => targetLayer;
    
    
    [Header("Projectile")]
    [SerializeField] private EnumProjectilSetting.projectilType projectileType;
    public EnumProjectilSetting.projectilType ProjectileType => projectileType;
    
    [SerializeField] private EnumProjectilSetting.projectileMovement projectileMovement;
    public EnumProjectilSetting.projectileMovement ProjectileMovement => projectileMovement;
    
    [SerializeField] private EnumShootType.ShootMode shootMode;
    public EnumShootType.ShootMode ShootMode => shootMode;

    
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
    
    public float GetStat(int level, StatType type, int heroLevel)
    {
        int index = Mathf.Clamp(level, MinLevel, MaxLevel) - MinLevel;
        if (index < 0 || index >= towerStats.Count) return 0f;
        foreach (var entry in towerStats[index].stats)
            if (entry.type == type) return ScaleStat(entry, heroLevel);
        return 0f;
    }

    public float GetRange(int heroLevel) => detectionRange * (1f + .03f * Mathf.Clamp(heroLevel, 0, 5));

    private static float ScaleStat(TowerStatEntry entry, int heroLevel) => entry.type switch
    {
        StatType.PhysicDamage or StatType.MagicDamage =>
            Mathf.RoundToInt(entry.baseValue * (1f + .08f * Mathf.Clamp(heroLevel, 0, 5))),
        StatType.AttackSpeed => entry.baseValue * (1f + .04f * Mathf.Clamp(heroLevel, 0, 5)),
        _ => entry.baseValue
    };

    public void ApplyStatsTo(EntityStats target, int level, int heroLevel = 0)
    {
        int index = level - minLevel;

        if (index < 0 || index >= towerStats.Count)
        {
            Debug.LogError("Le niveau " + level + " n'existe pas dans le SOTower.");
            return;
        }

        foreach (TowerStatEntry entry in towerStats[index].stats)
        {
            target.SetBaseStat(entry.type, ScaleStat(entry, heroLevel));
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
