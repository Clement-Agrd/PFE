using UnityEngine;
using UnityEngine.Serialization;
using Core.LootSystem;
using Core.StatsSystem;

namespace Core.TowerDefense
{
    using StatType =
        EnumStats.StatTypes;

    /// <summary>
    /// Fiche d'un type d'ennemi (asset) : stats complètes (les mêmes que le
    /// joueur, via EntityStats), dégâts à la base, or au kill et table de drop.
    /// On l'assigne sur le prefab pour régler l'équilibrage sans ouvrir le prefab.
    /// Création : Assets > Create > Tower Defense > Enemy Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "Tower Defense/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [Tooltip("Portrait affiché dans l'aperçu de spawn (au-dessus des zones).")]
        [SerializeField] private Sprite icon;
        [Tooltip("Texte du bestiaire (fiche de l'ennemi).")]
        [TextArea(2, 5)] [SerializeField] private string description;

        [Header("Vitalité")]
        [FormerlySerializedAs("maxHealth")]
        [SerializeField, Min(1f)] private float life = 30f;
        [SerializeField, Min(0f)] private float stamina = 100f;

        [Header("Attaque")]
        [SerializeField, Min(0f)] private float physicDamage = 10f;
        [SerializeField, Min(0f)] private float magicDamage = 10f;
        [SerializeField, Min(0f)] private float attackSpeed = 1f;

        [Header("Défense")]
        [SerializeField, Min(0f)] private float defensePhysic = 0f;
        [SerializeField, Min(0f)] private float defenseMagic = 0f;

        [Header("Mobilité")]
        [SerializeField, Min(0.1f)] private float speed = 3f;

        [Header("Base")]
        [Tooltip("Dégâts infligés à la base si l'ennemi arrive au bout du chemin.")]
        [SerializeField, Min(1)] private int baseDamage = 1;

        [Header("Récompenses")]
        [SerializeField, Min(0)] private int goldReward = 5;
        [SerializeField] private LootTable lootTable;

        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public string Description => description;
        public int MaxHealth => Mathf.Max(1, Mathf.RoundToInt(life));
        public float Speed => speed;
        public int BaseDamage => baseDamage;
        public int GoldReward => goldReward;
        public LootTable LootTable => lootTable;

        /// <summary>Valeur de base d'une stat, comme sur le joueur.</summary>
        public float GetStat(StatType type)
        {
            switch (type)
            {
                case StatType.Life: return life;
                case StatType.Stamina: return stamina;
                case StatType.PhysicDamage: return physicDamage;
                case StatType.MagicDamage: return magicDamage;
                case StatType.DefensePhysic: return defensePhysic;
                case StatType.DefenseMagic: return defenseMagic;
                case StatType.Speed: return speed;
                case StatType.AttackSpeed: return attackSpeed;
                default: return 0f;
            }
        }

        /// <summary>
        /// Écrit toutes les stats de la fiche dans l'EntityStats de l'ennemi
        /// (créées si le prefab ne les a pas).
        /// </summary>
        public void ApplyTo(EntityStats stats)
        {
            if (stats == null)
                return;

            foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
                stats.SetOrAddBaseStat(type, GetStat(type));
        }
    }
}
