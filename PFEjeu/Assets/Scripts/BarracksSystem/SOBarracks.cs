using System;
using System.Collections.Generic;
using Core.StatsSystem;
using UnityEngine;

namespace Core.BarracksSystem
{
    using StatType = EnumStats.StatTypes;

    [CreateAssetMenu(fileName = "SOBarracks", menuName = "Scriptable Objects/SOBarracks")]
    public sealed class SOBarracks : ScriptableObject
    {
        [Serializable]
        public struct SoldierStatEntry
        {
            public StatType type;
            public float baseValue;
        }

        [Serializable]
        public sealed class BarracksLevelStats
        {
            [Min(1)] public int level = 1;

            [Header("Stats propres à la caserne")]
            [Min(1)] public int soldierCount = 3;
            [Min(0.1f)] public float respawnTime = 10f;

            [Header("EntityStats des soldats")]
            public List<SoldierStatEntry> soldierStats = new();
        }

        [Header("Base Information")]
        [SerializeField] private string barracksName = "Caserne";
        [SerializeField] private GameObject soldierPrefab;

        [Header("Flag")]
        [SerializeField] private GameObject flagPrefab;
        [SerializeField] private GameObject flagPreviewPrefab;
        [SerializeField, Min(1f)] private float flagPlacementRange = 12f;
        [SerializeField, Min(0.1f)] private float campRadius = 1.5f;
        [SerializeField] private Vector3 defaultFlagOffset = new Vector3(0f, 0f, 6f);

        [Header("Level Information")]
        [SerializeField, Min(1)] private int minLevel = 1;
        [SerializeField, Min(1)] private int maxLevel = 3;
        [SerializeField] private List<BarracksLevelStats> barracksStats = new();

        [Header("Models")]
        [Tooltip("Optionnel : un modèle par niveau, comme SOTower.")]
        public GameObject[] models;

        public string BarracksName => barracksName;
        public GameObject SoldierPrefab => soldierPrefab;
        public GameObject FlagPrefab => flagPrefab;
        public GameObject FlagPreviewPrefab => flagPreviewPrefab;
        public float FlagPlacementRange => flagPlacementRange;
        public float CampRadius => campRadius;
        public Vector3 DefaultFlagOffset => defaultFlagOffset;
        public int MinLevel => minLevel;
        public int MaxLevel => maxLevel;

        public BarracksLevelStats GetLevelStats(int level)
        {
            int index = level - minLevel;
            return index >= 0 && index < barracksStats.Count
                ? barracksStats[index]
                : null;
        }

        public void ApplySoldierStatsTo(EntityStats target, int level)
        {
            if (target == null) return;

            BarracksLevelStats levelStats = GetLevelStats(level);
            if (levelStats == null)
            {
                Debug.LogError($"[SOBarracks] Le niveau {level} n'existe pas dans {name}.", this);
                return;
            }

            for (int i = 0; i < levelStats.soldierStats.Count; i++)
            {
                SoldierStatEntry entry = levelStats.soldierStats[i];
                target.SetOrAddBaseStat(entry.type, entry.baseValue);
            }
        }

#if UNITY_EDITOR
        private void Reset() => FillAllStats();

        [ContextMenu("Remplir toutes les stats")]
        private void FillAllStats()
        {
            minLevel = Mathf.Max(1, minLevel);
            maxLevel = Mathf.Max(minLevel, maxLevel);

            int levelCount = maxLevel - minLevel + 1;

            while (barracksStats.Count < levelCount)
            {
                barracksStats.Add(new BarracksLevelStats
                {
                    level = minLevel + barracksStats.Count
                });
            }

            while (barracksStats.Count > levelCount)
                barracksStats.RemoveAt(barracksStats.Count - 1);

            Array values = Enum.GetValues(typeof(StatType));

            for (int levelIndex = 0; levelIndex < barracksStats.Count; levelIndex++)
            {
                BarracksLevelStats levelStats = barracksStats[levelIndex];
                levelStats.level = minLevel + levelIndex;

                levelStats.soldierStats ??= new List<SoldierStatEntry>();

                foreach (StatType type in values)
                {
                    bool found = false;
                    for (int i = 0; i < levelStats.soldierStats.Count; i++)
                    {
                        if (levelStats.soldierStats[i].type == type)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        levelStats.soldierStats.Add(new SoldierStatEntry
                        {
                            type = type,
                            baseValue = GetDefaultStat(type)
                        });
                    }
                }
            }
        }

        private static float GetDefaultStat(StatType type)
        {
            return type switch
            {
                StatType.Life => 50f,
                StatType.Stamina => 100f,
                StatType.PhysicDamage => 8f,
                StatType.MagicDamage => 0f,
                StatType.DefensePhysic => 3f,
                StatType.DefenseMagic => 0f,
                StatType.Speed => 3.5f,
                StatType.AttackSpeed => 1f,
                _ => 0f
            };
        }
#endif
    }
}
