using UnityEngine;
using Core.LootSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Fiche d'un type d'ennemi (asset) : PV, vitesse, dégâts à la base, or au kill
    /// et table de drop. On l'assigne sur le prefab (via TowerDefenseEnemy/NavEnemy)
    /// pour régler l'équilibrage sans ouvrir le prefab.
    /// Création : Assets > Create > Tower Defense > Enemy Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy", menuName = "Tower Defense/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [Tooltip("Portrait affiché dans l'aperçu de spawn (au-dessus des zones).")]
        [SerializeField] private Sprite icon;

        [Header("Combat")]
        [SerializeField, Min(1)] private int maxHealth = 30;
        [SerializeField, Min(0.1f)] private float speed = 3f;
        [SerializeField, Min(1)] private int baseDamage = 1;

        [Header("Récompenses")]
        [SerializeField, Min(0)] private int goldReward = 5;
        [SerializeField] private LootTable lootTable;

        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxHealth => maxHealth;
        public float Speed => speed;
        public int BaseDamage => baseDamage;
        public int GoldReward => goldReward;
        public LootTable LootTable => lootTable;
    }
}