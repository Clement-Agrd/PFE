using UnityEngine;
using UnityEngine.AI;

using Core.WaveSystem;
using Core.PoolingSystem;
using Core.HealthSystem;
using Core.ShopSystem;
using Core.LootSystem;
using Core.InventorySystem;
using Core.StatsSystem;

namespace Core.TowerDefense
{
    using StatType =
        EnumStats.StatTypes;

    /// <summary>
    /// Contrôleur central léger d'un ennemi modulaire.
    ///
    /// Il orchestre les states et les modules, mais ne contient
    /// plus lui-même toute la détection, le pathfinding et le combat.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PooledObject))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(EnemyTargetSensor))]
    [RequireComponent(typeof(EnemyPathFollower))]
    public sealed class EnemyController :
        MonoBehaviour,
        IWaveEnemy
    {
        [Header("Fiche (optionnelle)")]
        [SerializeField]
        private EnemyDefinition definition;

        [Header("Fallback si pas de fiche")]
        [SerializeField, Min(0.1f)]
        private float fallbackSpeed = 3.5f;

        [SerializeField, Min(0)]
        private int goldReward = 5;

        [SerializeField]
        private LootTable lootTable;

        [Header("Spline")]
        [SerializeField]
        private EnemySplinePath splinePath;

        [SerializeField]
        private bool autoFindSpline = true;

        [SerializeField, Min(0.1f)]
        private float spawnNavSampleRadius = 5f;

        [Header("Modules")]
        [SerializeField]
        private EnemyTargetSensor targetSensor;

        [SerializeField]
        private EnemyPathFollower pathFollower;

        [Tooltip("EnemyMeleeAttack, EnemyComboMeleeAttack, etc.")]
        [SerializeField]
        private EnemyAttackModule attackModule;

        [Header("Animation")]
        [SerializeField]
        private Animator animator;

        [SerializeField]
        private string movementSpeedParameter =
            "Speed";

        [SerializeField, Min(0f)]
        private float combatTurnSpeed = 720f;

        [Header("Debug")]
        [SerializeField]
        private bool logStateChanges;

        public event System.Action<IWaveEnemy>
            Defeated;

        private NavMeshAgent _agent;
        private Health _health;
        private EntityStats _stats;

        private Wallet _wallet;
        private InventoryHolder _inventory;

        private float _runtimeSpeed;
        private int _runtimeGoldReward;
        private LootTable _runtimeLoot;
        private bool _finished;

        private EnemyStateMachine _stateMachine;
        private EnemyFollowPathState _followPathState;
        private EnemyChaseState _chaseState;
        private EnemyAttackState _attackState;
        private EnemyFinishedState _finishedState;

        private int _movementSpeedHash;

        public NavMeshAgent Agent => _agent;
        public Health Health => _health;
        public EntityStats Stats => _stats;
        public Animator Animator => animator;
        public EnemyTargetSensor TargetSensor => targetSensor;
        public EnemyPathFollower PathFollower => pathFollower;
        public EnemyAttackModule AttackModule => attackModule;
        public Health Target { get; private set; }
        
        public EnemyDefinition Definition =>
            definition;

        public string CurrentStateName =>
            _stateMachine != null
                ? _stateMachine.CurrentStateName
                : "None";

        public bool IsFinished =>
            _finished;

        public float AttackSpeed
        {
            get
            {
                if (_stats == null ||
                    !_stats.HasStat(
                        StatType.AttackSpeed))
                {
                    return 1f;
                }

                return Mathf.Clamp(
                    _stats.GetStat(
                        StatType.AttackSpeed
                    ),
                    0.1f,
                    5f
                );
            }
        }

        private void Awake()
        {
            ResolveComponents();
            BuildStateMachine();

            if (!string.IsNullOrWhiteSpace(
                    movementSpeedParameter))
            {
                _movementSpeedHash =
                    Animator.StringToHash(
                        movementSpeedParameter
                    );
            }

            if (splinePath == null &&
                autoFindSpline)
            {
                splinePath =
                    FindFirstObjectByType<
                        EnemySplinePath
                    >();
            }

            if (attackModule != null)
            {
                attackModule.Initialize(
                    this
                );
            }
        }

        private void OnEnable()
        {
            if (_health != null)
            {
                _health.OnDeath +=
                    HandleDeath;
            }

            if (_stats != null)
            {
                _stats.OnStatChanged +=
                    HandleStatChanged;
            }
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.OnDeath -=
                    HandleDeath;
            }

            if (_stats != null)
            {
                _stats.OnStatChanged -=
                    HandleStatChanged;
            }

            attackModule?.Cancel();

            Target = null;
        }

        private void Update()
        {
            UpdateAnimator();

            if (_finished ||
                _agent == null ||
                !_agent.isOnNavMesh)
            {
                return;
            }

            _stateMachine?.Tick(
                Time.deltaTime
            );
        }

        public void Initialize(
            Wallet wallet,
            InventoryHolder inventory)
        {
            ResolveComponents();

            if (_stateMachine == null)
            {
                BuildStateMachine();
            }

            _wallet = wallet;
            _inventory = inventory;

            ApplyDefinition();

            if (_health != null)
            {
                _health.Revive();
            }

            _finished = false;
            Target = null;

            targetSensor?.ResetRuntime();

            if (attackModule != null)
            {
                attackModule.Initialize(
                    this
                );
            }

            EnsureOnNavMesh();

            if (_agent != null)
            {
                _agent.speed =
                    GetMovementSpeed();

                _agent.autoBraking =
                    false;
            }

            if (splinePath == null &&
                autoFindSpline)
            {
                splinePath =
                    FindFirstObjectByType<
                        EnemySplinePath
                    >();
            }

            if (pathFollower != null)
            {
                pathFollower.SetPath(
                    splinePath
                );
            }

            if (splinePath == null ||
                !splinePath.IsValid)
            {
                Debug.LogError(
                    "[EnemyController] " +
                    name +
                    " n'a aucune EnemySplinePath valide.",
                    this
                );
            }

            if (attackModule == null)
            {
                Debug.LogWarning(
                    "[EnemyController] " +
                    name +
                    " n'a aucun EnemyAttackModule. " +
                    "Il suivra son chemin mais ignorera le combat.",
                    this
                );
            }

            _stateMachine.Reset();

            ChangeState(
                _followPathState,
                true
            );
        }

        public void SetPath(
            EnemySplinePath path)
        {
            splinePath = path;

            if (pathFollower != null)
            {
                pathFollower.SetPath(
                    path
                );
            }
        }

        private void ResolveComponents()
        {
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();

            if (_health == null)
                _health = GetComponent<Health>();

            if (_stats == null)
                _stats = GetComponent<EntityStats>();

            if (targetSensor == null)
                targetSensor = GetComponent<EnemyTargetSensor>();

            if (pathFollower == null)
                pathFollower = GetComponent<EnemyPathFollower>();

            if (attackModule == null)
                attackModule = GetComponent<EnemyAttackModule>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        private void BuildStateMachine()
        {
            _stateMachine =
                new EnemyStateMachine();

            _followPathState =
                new EnemyFollowPathState(
                    this
                );

            _chaseState =
                new EnemyChaseState(
                    this
                );

            _attackState =
                new EnemyAttackState(
                    this
                );

            _finishedState =
                new EnemyFinishedState(
                    this
                );
        }

        private void ApplyDefinition()
        {
            if (definition != null)
            {
                _runtimeSpeed =
                    definition.Speed;

                _runtimeGoldReward =
                    definition.GoldReward;

                _runtimeLoot =
                    definition.LootTable;

                if (_health != null)
                {
                    _health.SetMaxHealth(
                        definition.MaxHealth
                    );
                }

                if (_stats != null &&
                    _stats.HasStat(
                        StatType.Speed))
                {
                    _stats.SetBaseStat(
                        StatType.Speed,
                        definition.Speed
                    );
                }

                return;
            }

            _runtimeSpeed =
                fallbackSpeed;

            _runtimeGoldReward =
                goldReward;

            _runtimeLoot =
                lootTable;
        }

        private void EnsureOnNavMesh()
        {
            if (_agent == null ||
                _agent.isOnNavMesh)
            {
                return;
            }

            if (NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit hit,
                    spawnNavSampleRadius,
                    NavMesh.AllAreas))
            {
                _agent.Warp(
                    hit.position
                );
            }
        }

        internal bool RefreshTarget(
            float deltaTime)
        {
            if (targetSensor == null ||
                attackModule == null)
            {
                Target = null;
                return false;
            }

            Target =
                targetSensor.UpdateTarget(
                    Target,
                    transform,
                    _health,
                    deltaTime
                );

            return Target != null;
        }

        internal bool IsTargetInAttackRange()
        {
            if (Target == null ||
                attackModule == null)
            {
                return false;
            }

            return GetPlanarDistanceTo(
                       Target.transform.position
                   ) <=
                   attackModule.AttackRange;
        }

        internal void FaceTarget()
        {
            if (Target == null)
                return;

            Vector3 direction =
                Target.transform.position -
                transform.position;

            direction.y = 0f;

            if (direction.sqrMagnitude <
                0.001f)
            {
                return;
            }

            Quaternion targetRotation =
                Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up
                );

            transform.rotation =
                Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    combatTurnSpeed *
                    Time.deltaTime
                );
        }

        public float GetPlanarDistanceTo(
            Vector3 position)
        {
            Vector3 a =
                transform.position;

            Vector3 b =
                position;

            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(
                a,
                b
            );
        }

        internal void ChangeToFollowPath()
        {
            ChangeState(
                _followPathState
            );
        }

        internal void ChangeToChase()
        {
            ChangeState(
                _chaseState
            );
        }

        internal void ChangeToAttack()
        {
            ChangeState(
                _attackState
            );
        }

        private void ChangeState(
            IEnemyState state,
            bool force = false)
        {
            if (_stateMachine == null ||
                state == null)
            {
                return;
            }

            string previous =
                _stateMachine.CurrentStateName;

            _stateMachine.ChangeState(
                state,
                force
            );

            if (logStateChanges &&
                previous !=
                    _stateMachine.CurrentStateName)
            {
                Debug.Log(
                    "[EnemyController] " +
                    name +
                    " : " +
                    previous +
                    " -> " +
                    _stateMachine.CurrentStateName,
                    this
                );
            }
        }

        internal void FinishPath()
        {
            if (_finished)
                return;

            _finished = true;

            ChangeState(
                _finishedState,
                true
            );

            Defeat();
        }

        private void HandleDeath()
        {
            if (_finished)
                return;

            _finished = true;

            ChangeState(
                _finishedState,
                true
            );

            if (_wallet != null)
            {
                _wallet.Add(
                    _runtimeGoldReward
                );
            }

            RollDrops();

            Defeat();
        }

        private void RollDrops()
        {
            if (_runtimeLoot == null)
                return;

            var results =
                _runtimeLoot.Roll();

            if (_inventory == null ||
                _inventory.Inventory == null)
            {
                return;
            }

            foreach (var result in results)
            {
                if (result.Item == null ||
                    result.Amount <= 0)
                {
                    continue;
                }

                _inventory.Inventory.Add(
                    result.Item,
                    result.Amount
                );
            }
        }

        private void Defeat()
        {
            if (_agent != null &&
                _agent.isOnNavMesh)
            {
                _agent.isStopped =
                    true;
            }

            Defeated?.Invoke(
                this
            );

            if (TryGetComponent(
                    out PooledObject pooled))
            {
                pooled.Release();
            }
            else
            {
                Destroy(
                    gameObject
                );
            }
        }

        private float GetMovementSpeed()
        {
            if (_stats != null &&
                _stats.HasStat(
                    StatType.Speed))
            {
                return Mathf.Max(
                    0.1f,
                    _stats.GetStat(
                        StatType.Speed
                    )
                );
            }

            return Mathf.Max(
                0.1f,
                _runtimeSpeed
            );
        }

        private void HandleStatChanged(
            StatType type,
            float oldValue,
            float newValue)
        {
            if (type !=
                StatType.Speed)
            {
                return;
            }

            if (_agent != null)
            {
                _agent.speed =
                    GetMovementSpeed();
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null ||
                _agent == null ||
                _movementSpeedHash == 0)
            {
                return;
            }

            float currentSpeed =
                _agent.isOnNavMesh
                    ? _agent.velocity.magnitude
                    : 0f;

            float maximumSpeed =
                Mathf.Max(
                    0.01f,
                    GetMovementSpeed()
                );

            animator.SetFloat(
                _movementSpeedHash,
                currentSpeed /
                maximumSpeed
            );
        }
    }
}
