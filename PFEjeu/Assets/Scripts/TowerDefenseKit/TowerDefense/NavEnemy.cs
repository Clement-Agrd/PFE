using UnityEngine;
using UnityEngine.AI;
using Core.WaveSystem;
using Core.PoolingSystem;
using Core.HealthSystem;
using Core.ShopSystem;
using Core.LootSystem;
using Core.InventorySystem;

namespace Core.TowerDefense
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PooledObject))]
    public sealed class NavEnemy : MonoBehaviour, IWaveEnemy
    {
        [Header("Fiche (optionnelle)")]
        [SerializeField] private EnemyDefinition definition;

        [Header("Fallback si pas de fiche")]
        [SerializeField, Min(0.1f)] private float speed = 3.5f;
        [SerializeField, Min(1)] private int baseDamage = 1;
        [SerializeField, Min(0)] private int goldReward = 5;
        [SerializeField] private LootTable lootTable;

        [Header("Objectif")]
        [Tooltip("Distance (à plat) à laquelle il considère avoir atteint la base.")]
        [SerializeField] private float reachDistance = 1.2f;
        [Tooltip("Fréquence de recalcul de la destination (s).")]
        [SerializeField] private float repathInterval = 0.5f;
        [Tooltip("Rayon de recherche pour poser l'ennemi/la cible sur le NavMesh.")]
        [SerializeField] private float navSampleRadius = 5f;

        public event System.Action<IWaveEnemy> Defeated;

        private NavMeshAgent _agent;
        private Health _health;
        private Transform _target;
        private PlayerBase _base;
        private Wallet _wallet;
        private InventoryHolder _inventory;

        private float _speed;
        private int _baseDamage;
        private int _goldReward;
        private LootTable _loot;

        private Vector3 _destination;
        private float _repathTimer;
        private bool _finished;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            TryGetComponent(out _health);
        }

        private void OnEnable()
        {
            if (_health != null) _health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (_health != null) _health.OnDeath -= HandleDeath;
        }

        public void Initialize(Transform target, PlayerBase playerBase, Wallet wallet, InventoryHolder inventory)
        {
            _target = target;
            _base = playerBase;
            _wallet = wallet;
            _inventory = inventory;

            if (definition != null)
            {
                _speed = definition.Speed;
                _baseDamage = definition.BaseDamage;
                _goldReward = definition.GoldReward;
                _loot = definition.LootTable;
                if (_health != null) _health.SetMaxHealth(definition.MaxHealth);
            }
            else
            {
                _speed = speed;
                _baseDamage = baseDamage;
                _goldReward = goldReward;
                _loot = lootTable;
            }

            if (_health != null) _health.Revive();

            _finished = false;
            _repathTimer = 0f;
            _agent.speed = _speed;

            // Pose l'agent sur le NavMesh le plus proche (corrige l'erreur + les trajets erratiques).
            if (!_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                    _agent.Warp(hit.position);
            }

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                SetDestinationSafe();
            }
        }

        private void Update()
        {
            if (_finished || _target == null) return;
            if (!_agent.isOnNavMesh) return; // évite l'erreur remainingDistance

            _repathTimer -= Time.deltaTime;
            if (_repathTimer <= 0f)
            {
                _repathTimer = repathInterval;
                SetDestinationSafe();
            }

            // Tant que le chemin se calcule, on n'évalue pas l'arrivée.
            if (_agent.pathPending) return;

            // Vraie distance à la destination réelle (plan horizontal),
            // plus fiable que remainingDistance qui peut renvoyer 0 pendant le calcul.
            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = _destination; b.y = 0f;

            if (Vector3.Distance(a, b) <= reachDistance)
                ReachBase();
        }

        // Ramène la cible sur le NavMesh avant de l'assigner (base flottante / hors zone).
        private void SetDestinationSafe()
        {
            if (_target == null) return;

            if (NavMesh.SamplePosition(_target.position, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                _destination = hit.position;
            else
                _destination = _target.position;

            _agent.SetDestination(_destination);
        }

        private void HandleDeath()
        {
            if (_finished) return;
            _finished = true;

            if (_wallet != null) _wallet.Add(_goldReward);
            RollDrops();
            Defeat();
        }

        private void ReachBase()
        {
            if (_finished) return;
            _finished = true;

            if (_base != null) _base.TakeDamage(_baseDamage);
            Defeat();
        }

        private void RollDrops()
        {
            if (_loot == null) return;

            var results = _loot.Roll();
            if (_inventory != null && _inventory.Inventory != null)
                foreach (var r in results)
                    _inventory.Inventory.Add(r.Item, r.Amount);
        }

        private void Defeat()
        {
            if (_agent.isOnNavMesh) _agent.isStopped = true;

            Defeated?.Invoke(this);

            if (TryGetComponent(out PooledObject pooled)) pooled.Release();
            else Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_destination, reachDistance);
            Gizmos.DrawLine(transform.position, _destination);
        }
    }
}