using Core.HealthSystem;
using Core.StatsSystem;
using UnityEngine;
using UnityEngine.AI;

namespace Core.BarracksSystem
{
    using StatType = EnumStats.StatTypes;

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EntityStats))]
    [RequireComponent(typeof(Health))]
    public sealed class BarracksSoldier : MonoBehaviour
    {
        [Header("Camp")]
        [SerializeField, Min(0.05f)] private float stoppingDistance = 0.25f;
        [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string movementSpeedParameter = "Speed";

        [Header("Death")]
        [SerializeField, Min(0f)] private float deathDespawnDelay = 0.35f;

        private Barracks _owner;
        private NavMeshAgent _agent;
        private EntityStats _stats;
        private Health _health;
        private int _slotIndex = -1;
        private int _speedHash;
        private bool _initialized;
        private bool _suppressRespawn;

        public int SlotIndex => _slotIndex;
        public EntityStats Stats => _stats;
        public Health Health => _health;

        private void Awake()
        {
            ResolveComponents();
            if (!string.IsNullOrWhiteSpace(movementSpeedParameter))
                _speedHash = Animator.StringToHash(movementSpeedParameter);
        }

        private void OnEnable()
        {
            ResolveComponents();

            if (_health != null)
                _health.OnDeath += HandleDeath;

            if (_stats != null)
                _stats.OnStatChanged += HandleStatChanged;
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnDeath -= HandleDeath;

            if (_stats != null)
                _stats.OnStatChanged -= HandleStatChanged;
        }

        private void Update()
        {
            if (animator == null || _agent == null || _speedHash == 0) return;

            float maxSpeed = Mathf.Max(0.01f, _agent.speed);
            animator.SetFloat(_speedHash, _agent.velocity.magnitude / maxSpeed);
        }

        public void Initialize(Barracks owner, int slotIndex, Vector3 campPosition)
        {
            ResolveComponents();

            _owner = owner;
            _slotIndex = slotIndex;
            _suppressRespawn = false;
            _initialized = true;

            ApplyMovementSpeed();
            SetCampPosition(campPosition);
        }

        public void SetCampPosition(Vector3 worldPosition)
        {
            ResolveComponents();
            if (_agent == null) return;

            if (!TrySampleNavMesh(worldPosition, out Vector3 sampled))
                return;

            if (!_agent.isOnNavMesh)
            {
                if (TrySampleNavMesh(transform.position, out Vector3 current))
                    _agent.Warp(current);
                else
                    _agent.Warp(sampled);
            }

            _agent.stoppingDistance = stoppingDistance;
            _agent.isStopped = false;
            _agent.SetDestination(sampled);
        }

        public void DespawnWithoutRespawn()
        {
            _suppressRespawn = true;

            if (_health != null)
                _health.OnDeath -= HandleDeath;

            Destroy(gameObject);
        }

        private void ResolveComponents()
        {
            _agent ??= GetComponent<NavMeshAgent>();
            _stats ??= GetComponent<EntityStats>();
            _health ??= GetComponent<Health>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        private void ApplyMovementSpeed()
        {
            if (_agent == null || _stats == null) return;

            if (_stats.HasStat(StatType.Speed))
                _agent.speed = Mathf.Max(0.1f, _stats.GetStat(StatType.Speed));
        }

        private void HandleStatChanged(StatType type, float oldValue, float newValue)
        {
            if (type == StatType.Speed)
                ApplyMovementSpeed();
        }

        private void HandleDeath()
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.isStopped = true;

            if (!_suppressRespawn && _initialized && _owner != null)
                _owner.NotifySoldierDied(this, _slotIndex);

            Destroy(gameObject, deathDespawnDelay);
        }

        private bool TrySampleNavMesh(Vector3 worldPosition, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(
                    worldPosition,
                    out NavMeshHit hit,
                    navMeshSampleRadius,
                    NavMesh.AllAreas))
            {
                sampled = hit.position;
                return true;
            }

            sampled = worldPosition;
            return false;
        }
    }
}
