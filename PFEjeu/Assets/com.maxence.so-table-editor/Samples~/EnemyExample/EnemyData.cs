using System;
using UnityEngine;

namespace Maxence.SOTable.Samples
{
    public enum EnemyRank
    {
        Minion,
        Elite,
        Boss
    }

    /// <summary>Nested struct: shown in the table as "Stats / Health", "Stats / Speed"...</summary>
    [Serializable]
    public struct EnemyStats
    {
        public int health;
        public float speed;
        public int damage;
    }

    /// <summary>
    /// Sample data to try SO Table Editor: create a few assets with
    /// Create > SO Table Samples > Enemy Data, then open Tools > SO Table Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemy", menuName = "SO Table Samples/Enemy Data")]
    public sealed class EnemyData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _displayName = "Enemy";
        [SerializeField] private EnemyRank _rank = EnemyRank.Minion;
        [SerializeField] private Color _tint = Color.white;

        [Header("Balancing")]
        [SerializeField] private EnemyStats _stats = new EnemyStats { health = 100, speed = 3f, damage = 10 };
        [SerializeField, Range(0f, 1f)] private float _dropChance = 0.25f;
        [SerializeField] private bool _isFlying;

        [Header("Content")]
        [SerializeField] private GameObject _prefab;
        [SerializeField] private string[] _lootTable = Array.Empty<string>();

        public string DisplayName => _displayName;
        public EnemyRank Rank => _rank;
        public Color Tint => _tint;
        public EnemyStats Stats => _stats;
        public float DropChance => _dropChance;
        public bool IsFlying => _isFlying;
        public GameObject Prefab => _prefab;
        public string[] LootTable => _lootTable;
    }
}
