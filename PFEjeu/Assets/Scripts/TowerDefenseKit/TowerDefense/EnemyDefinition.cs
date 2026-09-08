using UnityEngine;
using Core.LootSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Fiche d'un type d'ennemi (asset) : PV, vitesse, dégâts à la base, or au kill
    /// et table de drop. On l'assigne sur le prefab (via TowerDefenseEnemy) pour
    /// régler l'équilibrage sans ouvrir le prefab.
    /// Création : Assets > Create > Tower Defense > Enemy Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "Tower Defense/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;

        [Header("Combat")]
        [SerializeField, Min(1)] private int maxHealth = 30;
        [SerializeField, Min(0.1f)] private float speed = 3f;
        [Tooltip("Dégâts infligés à la base s'il l'atteint.")]
        [SerializeField, Min(1)] private int baseDamage = 1;

        [Header("Récompenses")]
        [Tooltip("Or donné au joueur quand il est tué.")]
        [SerializeField, Min(0)] private int goldReward = 5;
        [Tooltip("Table de butin roulée à la mort (drops). Optionnel.")]
        [SerializeField] private LootTable lootTable;

        public string DisplayName => displayName;
        public int MaxHealth => maxHealth;
        public float Speed => speed;
        public int BaseDamage => baseDamage;
        public int GoldReward => goldReward;
        public LootTable LootTable => lootTable;
    }
}
