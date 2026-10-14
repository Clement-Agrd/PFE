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
        public sealed class HeroSpecialization
        {
            [Min(0.01f)] public float lifeMultiplier = 1f;
            public float lifePerHeroLevel;
            [Min(0.01f)] public float defenseMultiplier = 1f;
            public float defensePerHeroLevel;
            [Min(0.01f)] public float damageMultiplier = 1f;
            public float damagePerHeroLevel;
            [Min(0f)] public float bonusSoldierFraction;
            [Min(0f)] public float bonusSoldierFractionPerHeroLevel;
            [Min(0.1f)] public float respawnMultiplier = 1f;
            [Min(0f)] public float respawnReductionPerHeroLevel = 0.04f;

            public float StatMultiplier(StatType type, int heroLevel)
            {
                int steps = Mathf.Clamp(heroLevel, 1, 5) - 1;
                return type switch
                {
                    StatType.Life => Mathf.Max(0.01f, lifeMultiplier + steps * lifePerHeroLevel),
                    StatType.DefensePhysic or StatType.DefenseMagic => Mathf.Max(0.01f, defenseMultiplier + steps * defensePerHeroLevel),
                    StatType.PhysicDamage or StatType.MagicDamage => Mathf.Max(0.01f, damageMultiplier + steps * damagePerHeroLevel),
                    _ => 1f
                };
            }
        }

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

        [Header("Spécialisation — bonus du niveau 1, puis progression par niveau de héros")]
        [SerializeField] private HeroSpecialization tank = new()
        {
            lifeMultiplier = 1.35f, lifePerHeroLevel = 0.1f,
            defenseMultiplier = 1.35f, defensePerHeroLevel = 0.1f,
            damageMultiplier = 0.8f, damagePerHeroLevel = 0.025f
        };
        [SerializeField] private HeroSpecialization degats = new()
        {
            lifeMultiplier = 0.8f, lifePerHeroLevel = 0.025f,
            defenseMultiplier = 0.8f, defensePerHeroLevel = 0.025f,
            damageMultiplier = 1.35f, damagePerHeroLevel = 0.1f
        };
        [SerializeField] private HeroSpecialization invocateur = new()
        {
            lifeMultiplier = 0.9f, lifePerHeroLevel = 0.0125f,
            defenseMultiplier = 0.9f, defensePerHeroLevel = 0.0125f,
            damageMultiplier = 0.9f, damagePerHeroLevel = 0.0125f,
            bonusSoldierFraction = 0.25f, bonusSoldierFractionPerHeroLevel = 0.125f,
            respawnMultiplier = 0.95f
        };

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

        public HeroSpecialization GetSpecialization(ClasseHeros classe) => classe switch
        {
            ClasseHeros.Tank => tank,
            ClasseHeros.Degats => degats,
            ClasseHeros.Invocateur => invocateur,
            _ => null
        };

        public int GetSoldierCount(int level, heros hero = null)
        {
            var stats = GetLevelStats(level);
            if (stats == null) return 0;
            var specialization = hero != null ? GetSpecialization(hero.Classe) : null;
            if (specialization == null) return stats.soldierCount;
            float fraction = specialization.bonusSoldierFraction + (hero.Level - 1) * specialization.bonusSoldierFractionPerHeroLevel;
            return stats.soldierCount + Mathf.CeilToInt(stats.soldierCount * Mathf.Max(0f, fraction));
        }

        public float GetRespawnTime(int level, heros hero = null)
        {
            var stats = GetLevelStats(level);
            if (stats == null) return 0f;
            var specialization = hero != null ? GetSpecialization(hero.Classe) : null;
            if (specialization == null) return stats.respawnTime;
            return Mathf.Max(0.1f, stats.respawnTime * Mathf.Max(0.1f,
                specialization.respawnMultiplier - (hero.Level - 1) * specialization.respawnReductionPerHeroLevel));
        }

        public float GetSoldierStat(int level, StatType type, heros hero = null)
        {
            var stats = GetLevelStats(level);
            if (stats == null) return 0f;
            for (int i = 0; i < stats.soldierStats.Count; i++)
            {
                var entry = stats.soldierStats[i];
                if (entry.type == type) return entry.baseValue * GetHeroStatMultiplier(type, hero);
            }
            return 0f;
        }

        private float GetHeroStatMultiplier(StatType type, heros hero)
            => hero != null ? GetSpecialization(hero.Classe)?.StatMultiplier(type, hero.Level) ?? 1f : 1f;

        public void ApplySoldierStatsTo(EntityStats target, int level, heros hero = null)
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
                target.SetOrAddBaseStat(entry.type, entry.baseValue * GetHeroStatMultiplier(entry.type, hero));
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
