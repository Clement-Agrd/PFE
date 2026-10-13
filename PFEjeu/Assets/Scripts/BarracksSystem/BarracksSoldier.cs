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

        [Header("Combat")]
        [SerializeField] private LayerMask enemyMask = 1 << 3;
        [SerializeField, Min(0.5f)] private float detectionRange = 6f;
        [SerializeField, Min(0.2f)] private float attackRange = 1.6f;
        [SerializeField, Min(0.5f)] private float maxPursuitDistanceFromCamp = 7f;
        [SerializeField, Min(0.02f)] private float targetRefreshInterval = 0.15f;
        [SerializeField, Min(0f)] private float attackWindup = 0.45f;
        [SerializeField, Min(0.05f)] private float attackRecovery = 0.75f;
        [SerializeField, Min(0f)] private float combatTurnSpeed = 720f;
        [SerializeField] private DamageType attackDamageType;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string movementSpeedParameter = "Speed";
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private string attackSpeedParameter = "AttackSpeed";

        [Header("Death")]
        [SerializeField, Min(0f)] private float deathDespawnDelay = 0.35f;

        private readonly Collider[] _targetBuffer = new Collider[24];

        private Barracks _owner;
        private NavMeshAgent _agent;
        private EntityStats _stats;
        private Health _health;
        private Health _target;

        private int _slotIndex = -1;
        private int _speedHash;
        private int _attackHash;
        private int _attackSpeedHash;

        private bool _initialized;
        private bool _suppressRespawn;
        private bool _isAttacking;
        private bool _damageApplied;

        private float _nextTargetRefresh;
        private float _attackImpactAt;
        private float _attackEndsAt;

        private Vector3 _campPosition;

        public int SlotIndex => _slotIndex;
        public EntityStats Stats => _stats;
        public Health Health => _health;

        private void Awake()
        {
            ResolveComponents();

            if (!string.IsNullOrWhiteSpace(movementSpeedParameter))
                _speedHash = Animator.StringToHash(movementSpeedParameter);

            if (!string.IsNullOrWhiteSpace(attackTrigger))
                _attackHash = Animator.StringToHash(attackTrigger);

            if (!string.IsNullOrWhiteSpace(attackSpeedParameter))
                _attackSpeedHash = Animator.StringToHash(attackSpeedParameter);
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
            if (!_initialized || _health == null || _health.IsDead)
                return;

            TickCombat();
            UpdateAnimatorMovement();
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

            if (TrySampleNavMesh(worldPosition, out Vector3 sampled))
                _campPosition = sampled;
            else
                _campPosition = worldPosition;

            if (_isAttacking || _target != null)
                return;

            MoveTo(_campPosition);
        }

        public void DespawnWithoutRespawn()
        {
            _suppressRespawn = true;

            if (_health != null)
                _health.OnDeath -= HandleDeath;

            Destroy(gameObject);
        }

        private void TickCombat()
        {
            if (_isAttacking)
            {
                TickAttack();
                return;
            }

            if (!IsTargetValid(_target))
                _target = null;

            if (Time.time >= _nextTargetRefresh)
            {
                _nextTargetRefresh = Time.time + targetRefreshInterval;

                if (_target == null)
                    _target = FindNearestEnemy();
            }

            if (_target == null)
            {
                ReturnToCamp();
                return;
            }

            float distance = PlanarDistance(
                transform.position,
                _target.transform.position);

            if (distance <= attackRange)
            {
                BeginAttack();
                return;
            }

            MoveTo(_target.transform.position);
        }

        private void TickAttack()
        {
            if (!IsTargetValid(_target))
            {
                FinishAttack();
                return;
            }

            StopAgent();
            FaceTarget();

            if (!_damageApplied && Time.time >= _attackImpactAt)
            {
                _damageApplied = true;
                ApplyAttackDamage();
            }

            if (Time.time >= _attackEndsAt)
                FinishAttack();
        }

        private void BeginAttack()
        {
            if (_target == null)
                return;

            StopAgent();
            FaceTarget();

            float attackSpeed = GetAttackSpeed();

            _isAttacking = true;
            _damageApplied = false;
            _attackImpactAt = Time.time + attackWindup / attackSpeed;
            _attackEndsAt = Time.time + (attackWindup + attackRecovery) / attackSpeed;

            if (animator != null)
            {
                if (_attackSpeedHash != 0)
                    animator.SetFloat(_attackSpeedHash, attackSpeed);

                if (_attackHash != 0)
                {
                    animator.ResetTrigger(_attackHash);
                    animator.SetTrigger(_attackHash);
                }
            }
        }

        private void FinishAttack()
        {
            _isAttacking = false;
            _damageApplied = false;

            if (!IsTargetValid(_target))
                _target = null;
        }

        private void ApplyAttackDamage()
        {
            if (_target == null || _target.IsDead)
                return;

            int damage = 1;

            if (_stats != null && _stats.HasStat(StatType.PhysicDamage))
            {
                damage = Mathf.Max(
                    1,
                    Mathf.RoundToInt(_stats.GetStat(StatType.PhysicDamage)));
            }

            DamageInfo info = new DamageInfo(
                damage,
                attackDamageType,
                gameObject,
                DamageSourceKind.Sword);

            _target.TakeDamage(in info);
        }

        private Health FindNearestEnemy()
        {
            int count = Physics.OverlapSphereNonAlloc(
                _campPosition,
                detectionRange,
                _targetBuffer,
                enemyMask,
                QueryTriggerInteraction.Ignore);

            Health nearest = null;
            float nearestSqr = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = _targetBuffer[i];
                if (candidateCollider == null)
                    continue;

                Health candidate = candidateCollider.GetComponentInParent<Health>();

                if (!IsTargetValid(candidate))
                    continue;

                float sqr = PlanarSqrDistance(
                    transform.position,
                    candidate.transform.position);

                if (sqr >= nearestSqr)
                    continue;

                nearestSqr = sqr;
                nearest = candidate;
            }

            return nearest;
        }

        private bool IsTargetValid(Health candidate)
        {
            if (candidate == null || candidate.IsDead || !candidate.gameObject.activeInHierarchy)
                return false;

            int layerBit = 1 << candidate.gameObject.layer;
            if ((enemyMask.value & layerBit) == 0)
                return false;

            float maxDistance = Mathf.Max(
                detectionRange,
                maxPursuitDistanceFromCamp);

            return PlanarSqrDistance(
                       _campPosition,
                       candidate.transform.position) <=
                   maxDistance * maxDistance;
        }

        private void ReturnToCamp()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return;

            if (PlanarSqrDistance(transform.position, _campPosition) <=
                stoppingDistance * stoppingDistance)
            {
                StopAgent();
                return;
            }

            MoveTo(_campPosition);
        }

        private void MoveTo(Vector3 worldPosition)
        {
            if (_agent == null)
                return;

            if (!_agent.isOnNavMesh)
            {
                if (TrySampleNavMesh(transform.position, out Vector3 current))
                    _agent.Warp(current);
                else
                    return;
            }

            if (!TrySampleNavMesh(worldPosition, out Vector3 sampled))
                sampled = worldPosition;

            _agent.stoppingDistance = stoppingDistance;
            _agent.isStopped = false;
            _agent.SetDestination(sampled);
        }

        private void StopAgent()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return;

            _agent.isStopped = true;
            _agent.ResetPath();
        }

        private void FaceTarget()
        {
            if (_target == null)
                return;

            Vector3 direction = _target.transform.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
                return;

            Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                desired,
                combatTurnSpeed * Time.deltaTime);
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
            if (_agent == null || _stats == null)
                return;

            if (_stats.HasStat(StatType.Speed))
                _agent.speed = Mathf.Max(0.1f, _stats.GetStat(StatType.Speed));
        }

        private float GetAttackSpeed()
        {
            if (_stats == null || !_stats.HasStat(StatType.AttackSpeed))
                return 1f;

            return Mathf.Clamp(_stats.GetStat(StatType.AttackSpeed), 0.1f, 5f);
        }

        private void UpdateAnimatorMovement()
        {
            if (animator == null || _speedHash == 0)
                return;

            float normalizedSpeed = 0f;

            if (!_isAttacking && _agent != null)
            {
                float maxSpeed = Mathf.Max(0.01f, _agent.speed);
                normalizedSpeed = _agent.velocity.magnitude / maxSpeed;
            }

            animator.SetFloat(_speedHash, normalizedSpeed, 0.08f, Time.deltaTime);
        }

        private void HandleStatChanged(StatType type, float oldValue, float newValue)
        {
            if (type == StatType.Speed)
                ApplyMovementSpeed();
        }

        private void HandleDeath()
        {
            _target = null;
            _isAttacking = false;
            StopAgent();

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

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return Mathf.Sqrt(PlanarSqrDistance(a, b));
        }

        private static float PlanarSqrDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return (a - b).sqrMagnitude;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = Application.isPlaying ? _campPosition : transform.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(center, detectionRange);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
