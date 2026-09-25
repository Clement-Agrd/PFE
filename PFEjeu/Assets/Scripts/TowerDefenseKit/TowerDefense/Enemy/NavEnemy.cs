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


    public enum EnemyAIState
    {
        FollowSpline,
        ChaseTarget,
        AttackTarget,
        Finished
    }


    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PooledObject))]
    [RequireComponent(typeof(Health))]
    public sealed class NavEnemy :
        MonoBehaviour,
        IWaveEnemy
    {
        // ============================================================
        // DEFINITION
        // ============================================================

        [Header("Fiche (optionnelle)")]

        [SerializeField]
        private EnemyDefinition definition;


        // ============================================================
        // FALLBACK
        // ============================================================

        [Header("Fallback si pas de fiche")]

        [SerializeField, Min(0.1f)]
        private float speed = 3.5f;

        [Tooltip(
            "Dégâts infligés à la BASE lorsque l'ennemi atteint la fin."
        )]

        [SerializeField, Min(0)]
        private int goldReward = 5;

        [SerializeField]
        private LootTable lootTable;


        // ============================================================
        // SPLINE
        // ============================================================

        [Header("Spline")]

        [Tooltip(
            "Chemin suivi par l'ennemi."
        )]
        [SerializeField]
        private EnemySplinePath splinePath;

        [Tooltip(
            "Si aucune spline n'est assignée, cherche automatiquement " +
            "la première EnemySplinePath de la scène."
        )]
        [SerializeField]
        private bool autoFindSpline = true;

        [Tooltip(
            "Distance nécessaire pour considérer un point de spline atteint."
        )]
        [SerializeField, Min(0.05f)]
        private float waypointReachDistance = 0.45f;


        // ============================================================
        // OBJECTIVE
        // ============================================================

        [Header("Objectif")]

        [Tooltip(
            "Distance à laquelle la fin du chemin est considérée atteinte."
        )]
        [SerializeField, Min(0.1f)]
        private float reachDistance = 1.2f;

        [Tooltip(
            "Fréquence de recalcul de destination."
        )]
        [SerializeField, Min(0.02f)]
        private float repathInterval = 0.25f;

        [Tooltip(
            "Rayon pour replacer une destination sur le NavMesh."
        )]
        [SerializeField, Min(0.1f)]
        private float navSampleRadius = 5f;


        // ============================================================
        // DETECTION
        // ============================================================

        [Header("Détection Combat")]

        [Tooltip(
            "Layers pouvant être attaqués.\n" +
            "Mets ici Player + Soldier."
        )]
        [SerializeField]
        private LayerMask targetMask;

        [SerializeField, Min(0.1f)]
        private float detectionRange = 7f;

        [Tooltip(
            "La cible n'est abandonnée qu'au-delà de cette distance. " +
            "Doit normalement être supérieure à Detection Range."
        )]
        [SerializeField, Min(0.1f)]
        private float loseTargetRange = 10f;

        [Tooltip(
            "Fréquence de recherche des cibles."
        )]
        [SerializeField, Min(0.02f)]
        private float detectionInterval = 0.15f;


        [Header("Vision optionnelle")]

        [SerializeField]
        private bool requireLineOfSight = false;

        [Tooltip(
            "Murs / décors bloquant la vision. " +
            "Ne mets PAS Player ou Soldier ici."
        )]
        [SerializeField]
        private LayerMask sightBlockingMask;

        [SerializeField]
        private float detectionEyeHeight = 1f;


        // ============================================================
        // ATTACK
        // ============================================================

        [Header("Attaque")]

        [SerializeField, Min(0.1f)]
        private float attackRange = 1.7f;

        [Tooltip(
            "Petite marge pour accepter le coup si la cible bouge " +
            "pendant l'animation."
        )]
        [SerializeField, Min(0f)]
        private float attackRangeLeeway = 0.35f;


        [Tooltip(
            "Dégâts fixes ajoutés à PhysicDamage."
        )]
        [SerializeField, Min(0f)]
        private float attackBaseDamage = 5f;

        [Tooltip(
            "Scaling de la stat PhysicDamage."
        )]
        [SerializeField, Min(0f)]
        private float physicDamageScaling = 1f;


        [Tooltip(
            "Type Physical à assigner ici."
        )]
        [SerializeField]
        private DamageType attackDamageType;


        [Header("Timing Attaque")]

        [Tooltip(
            "Temps entre deux attaques à AttackSpeed = 1."
        )]
        [SerializeField, Min(0.05f)]
        private float attackInterval = 1.25f;

        [Tooltip(
            "Temps entre le début de l'animation et le moment du dégât."
        )]
        [SerializeField, Min(0f)]
        private float attackWindup = 0.35f;

        [SerializeField, Min(0f)]
        private float combatTurnSpeed = 720f;


        // ============================================================
        // ANIMATION
        // ============================================================

        [Header("Animation optionnelle")]

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private string movementSpeedParameter =
            "Speed";

        [SerializeField]
        private string attackTriggerParameter =
            "Attack";


        // ============================================================
        // DEBUG
        // ============================================================

        [Header("Debug")]

        [SerializeField]
        private bool drawDebugGizmos = true;


        // ============================================================
        // EVENTS
        // ============================================================

        public event System.Action<IWaveEnemy>
            Defeated;


        // ============================================================
        // COMPONENTS
        // ============================================================

        private NavMeshAgent _agent;

        private Health _health;

        private EntityStats _stats;


        // ============================================================
        // WAVE REFERENCES
        // ============================================================

        private Wallet _wallet;

        private InventoryHolder _inventory;


        // ============================================================
        // DEFINITION RUNTIME
        // ============================================================

        private float _fallbackSpeed;

        private int _goldReward;

        private LootTable _loot;


        // ============================================================
        // AI
        // ============================================================

        private EnemyAIState _state =
            EnemyAIState.FollowSpline;


        private Health _combatTarget;


        private readonly Collider[] _detectionHits =
            new Collider[32];


        private int _pathIndex;

        private float _repathTimer;

        private float _detectionTimer;


        private float _nextAttackTime;

        private bool _attackImpactPending;

        private float _attackImpactTime;


        private bool _finished;


        // ============================================================
        // ANIMATOR HASH
        // ============================================================

        private int _movementSpeedHash;

        private int _attackTriggerHash;


        // ============================================================
        // PUBLIC
        // ============================================================

        public EnemyAIState CurrentState =>
            _state;


        public Health CurrentCombatTarget =>
            _combatTarget;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            _agent =
                GetComponent<NavMeshAgent>();

            _health =
                GetComponent<Health>();

            _stats =
                GetComponent<EntityStats>();


            if (animator == null)
            {
                animator =
                    GetComponentInChildren<Animator>();
            }


            _movementSpeedHash =
                Animator.StringToHash(
                    movementSpeedParameter
                );

            _attackTriggerHash =
                Animator.StringToHash(
                    attackTriggerParameter
                );


            if (splinePath == null &&
                autoFindSpline)
            {
                splinePath =
                    FindFirstObjectByType<
                        EnemySplinePath
                    >();
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
                    OnStatChanged;
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
                    OnStatChanged;
            }
        }


        private void Update()
        {
            UpdateAnimator();


            if (_finished)
                return;


            if (!_agent.isOnNavMesh)
                return;


            UpdateTargeting();

            EvaluateStateTree();

            TickCurrentState();
        }


        // ============================================================
        // INITIALIZATION
        // ============================================================

        public void Initialize(
            Wallet wallet,
            InventoryHolder inventory)
        {
            _wallet =
                wallet;


            _inventory =
                inventory;


            // ============================================================
            // DEFINITION
            // ============================================================

            if (definition != null)
            {
                _fallbackSpeed =
                    definition.Speed;


                _goldReward =
                    definition.GoldReward;


                _loot =
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
            }
            else
            {
                _fallbackSpeed =
                    speed;


                _goldReward =
                    goldReward;


                _loot =
                    lootTable;
            }


            // ============================================================
            // HEALTH
            // ============================================================

            if (_health != null)
            {
                _health.Revive();
            }


            // ============================================================
            // RESET AI
            // ============================================================

            _combatTarget =
                null;


            _finished =
                false;


            _repathTimer =
                0f;


            _detectionTimer =
                0f;


            _nextAttackTime =
                0f;


            _attackImpactPending =
                false;


            // ============================================================
            // NAVMESH
            // ============================================================

            if (!_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(
                        transform.position,
                        out NavMeshHit hit,
                        navSampleRadius,
                        NavMesh.AllAreas))
                {
                    _agent.Warp(
                        hit.position
                    );
                }
            }


            _agent.speed =
                GetMovementSpeed();


            _agent.autoBraking =
                false;


            // ============================================================
            // PATH
            // ============================================================

            if (splinePath == null &&
                autoFindSpline)
            {
                splinePath =
                    FindFirstObjectByType<
                        EnemySplinePath
                    >();
            }


            if (splinePath == null ||
                !splinePath.IsValid)
            {
                Debug.LogError(
                    $"[{nameof(NavEnemy)}] " +
                    $"{name} n'a aucune EnemySplinePath valide.",
                    this
                );
            }


            ReacquireSpline();


            ChangeState(
                EnemyAIState.FollowSpline,
                true
            );
        }


        /// <summary>
        /// Utile si tu as plusieurs chemins.
        ///
        /// Le WaveSpawner peut appeler SetPath()
        /// avant Initialize().
        /// </summary>
        public void SetPath(
            EnemySplinePath path)
        {
            splinePath =
                path;

            ReacquireSpline();
        }


        // ============================================================
        // STATE TREE
        // ============================================================

        private void EvaluateStateTree()
        {
            // Priorité absolue :
            // une cible valide interrompt le chemin.

            if (_combatTarget != null)
            {
                float distance =
                    GetPlanarDistanceTo(
                        _combatTarget.transform.position
                    );


                if (distance <=
                    attackRange)
                {
                    ChangeState(
                        EnemyAIState.AttackTarget
                    );
                }
                else
                {
                    ChangeState(
                        EnemyAIState.ChaseTarget
                    );
                }


                return;
            }


            ChangeState(
                EnemyAIState.FollowSpline
            );
        }


        private void ChangeState(
            EnemyAIState newState,
            bool force = false)
        {
            if (!force &&
                newState == _state)
            {
                return;
            }


            // Quitte attaque.
            if (_state ==
                EnemyAIState.AttackTarget)
            {
                _attackImpactPending =
                    false;
            }


            _state =
                newState;


            switch (_state)
            {
                // ====================================================
                // FOLLOW
                // ====================================================

                case EnemyAIState.FollowSpline:

                    if (_agent.isOnNavMesh)
                    {
                        _agent.isStopped =
                            false;

                        _agent.stoppingDistance =
                            0f;
                    }


                    ReacquireSpline();

                    _repathTimer = 0f;

                    break;


                // ====================================================
                // CHASE
                // ====================================================

                case EnemyAIState.ChaseTarget:

                    if (_agent.isOnNavMesh)
                    {
                        _agent.isStopped =
                            false;

                        _agent.stoppingDistance =
                            Mathf.Max(
                                0.05f,
                                attackRange * 0.8f
                            );
                    }


                    _repathTimer = 0f;

                    break;


                // ====================================================
                // ATTACK
                // ====================================================

                case EnemyAIState.AttackTarget:

                    if (_agent.isOnNavMesh)
                    {
                        _agent.isStopped =
                            true;
                    }


                    // Permet de frapper immédiatement
                    // lorsqu'on arrive au contact.
                    _nextAttackTime =
                        Mathf.Min(
                            _nextAttackTime,
                            Time.time
                        );

                    break;


                // ====================================================
                // FINISHED
                // ====================================================

                case EnemyAIState.Finished:

                    if (_agent.isOnNavMesh)
                    {
                        _agent.isStopped =
                            true;
                    }

                    break;
            }
        }


        private void TickCurrentState()
        {
            switch (_state)
            {
                case EnemyAIState.FollowSpline:

                    TickFollowSpline();

                    break;


                case EnemyAIState.ChaseTarget:

                    TickChaseTarget();

                    break;


                case EnemyAIState.AttackTarget:

                    TickAttackTarget();

                    break;
            }
        }


        // ============================================================
        // FOLLOW SPLINE
        // ============================================================

        private void TickFollowSpline()
        {
            if (splinePath == null ||
                !splinePath.IsValid)
            {
                return;
            }


            TickSplinePath();
        }


        private void TickSplinePath()
        {
            if (_pathIndex <
                0)
            {
                ReacquireSpline();
            }


            if (_pathIndex >=
                splinePath.Count)
            {
                ReachPathEnd();

                return;
            }


            Vector3 waypoint =
                splinePath.GetPoint(
                    _pathIndex
                );


            float distance =
                GetPlanarDistanceTo(
                    waypoint
                );


            bool finalPoint =
                _pathIndex ==
                splinePath.Count - 1;


            if (finalPoint &&
                distance <=
                reachDistance)
            {
                ReachPathEnd();

                return;
            }


            if (!finalPoint &&
                distance <=
                waypointReachDistance)
            {
                _pathIndex++;


                if (_pathIndex >=
                    splinePath.Count)
                {
                    ReachPathEnd();

                    return;
                }


                waypoint =
                    splinePath.GetPoint(
                        _pathIndex
                    );


                SetDestinationSafe(
                    waypoint
                );


                return;
            }


            _repathTimer -=
                Time.deltaTime;


            if (_repathTimer <= 0f)
            {
                _repathTimer =
                    repathInterval;

                SetDestinationSafe(
                    waypoint
                );
            }
        }


        private void ReacquireSpline()
        {
            if (splinePath == null ||
                !splinePath.IsValid)
            {
                _pathIndex = -1;

                return;
            }


            int closest =
                splinePath
                    .FindClosestPointIndex(
                        transform.position
                    );


            // On vise normalement le point suivant,
            // pour éviter que l'ennemi retourne légèrement en arrière.
            _pathIndex =
                Mathf.Min(
                    closest + 1,
                    splinePath.Count - 1
                );
        }


        // ============================================================
        // DETECTION
        // ============================================================

        private void UpdateTargeting()
        {
            // Vérifie d'abord la cible actuelle.
            if (_combatTarget != null)
            {
                if (!IsCurrentTargetValid())
                {
                    _combatTarget =
                        null;
                }
            }


            // Si on en a encore une, inutile
            // de rescanner tous les colliders.
            if (_combatTarget != null)
                return;


            _detectionTimer -=
                Time.deltaTime;


            if (_detectionTimer > 0f)
                return;


            _detectionTimer =
                detectionInterval;


            FindBestTarget();
        }


        private void FindBestTarget()
        {
            int count =
                Physics.OverlapSphereNonAlloc(
                    transform.position,
                    detectionRange,
                    _detectionHits,
                    targetMask,
                    QueryTriggerInteraction.Ignore
                );


            Health bestTarget =
                null;


            float bestSqrDistance =
                float.PositiveInfinity;


            for (int i = 0;
                 i < count;
                 i++)
            {
                Collider hit =
                    _detectionHits[i];


                if (hit == null)
                    continue;


                if (hit.transform.IsChildOf(
                        transform))
                {
                    continue;
                }


                Health candidate =
                    hit.GetComponentInParent<
                        Health
                    >();


                if (candidate == null)
                    continue;


                if (candidate ==
                    _health)
                {
                    continue;
                }


                if (candidate.IsDead ||
                    !candidate.gameObject
                        .activeInHierarchy)
                {
                    continue;
                }


                if (requireLineOfSight &&
                    !HasLineOfSight(
                        candidate))
                {
                    continue;
                }


                Vector3 difference =
                    candidate.transform.position -
                    transform.position;


                difference.y = 0f;


                float sqrDistance =
                    difference.sqrMagnitude;


                if (sqrDistance >=
                    bestSqrDistance)
                {
                    continue;
                }


                bestSqrDistance =
                    sqrDistance;

                bestTarget =
                    candidate;
            }


            _combatTarget =
                bestTarget;
        }


        private bool IsCurrentTargetValid()
        {
            if (_combatTarget == null)
                return false;


            if (_combatTarget.IsDead)
                return false;


            if (!_combatTarget.gameObject
                .activeInHierarchy)
            {
                return false;
            }


            float distance =
                GetPlanarDistanceTo(
                    _combatTarget
                        .transform
                        .position
                );


            if (distance >
                loseTargetRange)
            {
                return false;
            }


            if (requireLineOfSight &&
                !HasLineOfSight(
                    _combatTarget))
            {
                return false;
            }


            return true;
        }


        private bool HasLineOfSight(
            Health targetHealth)
        {
            Vector3 origin =
                transform.position +
                Vector3.up *
                detectionEyeHeight;


            Vector3 targetPoint =
                targetHealth.transform.position +
                Vector3.up *
                detectionEyeHeight;


            bool blocked =
                Physics.Linecast(
                    origin,
                    targetPoint,
                    sightBlockingMask,
                    QueryTriggerInteraction.Ignore
                );


            return !blocked;
        }


        // ============================================================
        // CHASE
        // ============================================================

        private void TickChaseTarget()
        {
            if (_combatTarget == null)
                return;


            if (_agent.isStopped)
            {
                _agent.isStopped =
                    false;
            }


            _repathTimer -=
                Time.deltaTime;


            if (_repathTimer > 0f)
                return;


            _repathTimer =
                repathInterval;


            SetDestinationSafe(
                _combatTarget
                    .transform
                    .position
            );
        }


        // ============================================================
        // ATTACK
        // ============================================================

        private void TickAttackTarget()
        {
            if (_combatTarget == null)
                return;


            if (_agent.isOnNavMesh &&
                !_agent.isStopped)
            {
                _agent.isStopped =
                    true;
            }


            FaceCombatTarget();


            // Impact d'une attaque commencée précédemment.
            if (_attackImpactPending &&
                Time.time >=
                _attackImpactTime)
            {
                _attackImpactPending =
                    false;


                ApplyAttackDamage();
            }


            if (_attackImpactPending)
                return;


            if (Time.time <
                _nextAttackTime)
            {
                return;
            }


            StartAttack();
        }


        private void StartAttack()
        {
            float attackSpeed =
                GetAttackSpeed();


            float effectiveInterval =
                attackInterval /
                attackSpeed;


            effectiveInterval =
                Mathf.Max(
                    0.05f,
                    effectiveInterval
                );


            float effectiveWindup =
                attackWindup /
                attackSpeed;


            effectiveWindup =
                Mathf.Clamp(
                    effectiveWindup,
                    0f,
                    effectiveInterval
                );


            _nextAttackTime =
                Time.time +
                effectiveInterval;


            _attackImpactTime =
                Time.time +
                effectiveWindup;


            _attackImpactPending =
                true;


            if (animator != null)
            {
                animator.SetTrigger(
                    _attackTriggerHash
                );
            }
        }


        private void ApplyAttackDamage()
        {
            if (_combatTarget == null)
                return;


            if (_combatTarget.IsDead)
                return;


            float distance =
                GetPlanarDistanceTo(
                    _combatTarget
                        .transform
                        .position
                );


            if (distance >
                attackRange +
                attackRangeLeeway)
            {
                return;
            }


            int finalDamage =
                CalculateAttackDamage();


            if (finalDamage <= 0)
                return;


            DamageInfo info =
                new DamageInfo(
                    finalDamage,
                    attackDamageType,
                    gameObject,
                    DamageSourceKind.None
                );


            _combatTarget.TakeDamage(
                in info
            );
        }


        private int CalculateAttackDamage()
        {
            float physicDamage =
                0f;


            if (_stats != null &&
                _stats.HasStat(
                    StatType.PhysicDamage))
            {
                physicDamage =
                    _stats.GetStat(
                        StatType.PhysicDamage
                    );
            }


            float damage =
                attackBaseDamage +
                physicDamage *
                physicDamageScaling;


            return Mathf.Max(
                1,
                Mathf.RoundToInt(
                    damage
                )
            );
        }


        private float GetAttackSpeed()
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


        private void FaceCombatTarget()
        {
            if (_combatTarget == null)
                return;


            Vector3 direction =
                _combatTarget.transform.position -
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


        // ============================================================
        // MOVEMENT
        // ============================================================

        private void SetDestinationSafe(
            Vector3 destination)
        {
            if (!_agent.isOnNavMesh)
                return;


            if (NavMesh.SamplePosition(
                    destination,
                    out NavMeshHit hit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                _agent.SetDestination(
                    hit.position
                );

                return;
            }


            _agent.SetDestination(
                destination
            );
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
                _fallbackSpeed
            );
        }


        private void OnStatChanged(
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


        // ============================================================
        // BASE
        // ============================================================

        private void ReachPathEnd()
        {
            if (_finished)
                return;


            _finished =
                true;


            ChangeState(
                EnemyAIState.Finished,
                true
            );


            Debug.Log(
                $"[NavEnemy] {name} a atteint la fin de son chemin."
            );


            // Important :
            //
            // Pas de gold.
            // Pas de loot.
            //
            // L'ennemi est simplement considéré
            // comme sorti de la vague.
            Defeat();
        }


        // ============================================================
        // DEATH
        // ============================================================

        private void HandleDeath()
        {
            if (_finished)
                return;


            _finished =
                true;


            ChangeState(
                EnemyAIState.Finished,
                true
            );


            if (_wallet != null)
            {
                _wallet.Add(
                    _goldReward
                );
            }


            RollDrops();

            Defeat();
        }


        // ============================================================
        // LOOT
        // ============================================================

        private void RollDrops()
        {
            if (_loot == null)
                return;


            var results =
                _loot.Roll();


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


        // ============================================================
        // DEFEAT
        // ============================================================

        private void Defeat()
        {
            if (_agent.isOnNavMesh)
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


        // ============================================================
        // UTILS
        // ============================================================

        private float GetPlanarDistanceTo(
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


        // ============================================================
        // ANIMATION
        // ============================================================

        private void UpdateAnimator()
        {
            if (animator == null)
                return;


            float currentSpeed = 0f;


            if (_agent != null &&
                _agent.isOnNavMesh)
            {
                currentSpeed =
                    _agent.velocity.magnitude;
            }


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


        // ============================================================
        // DEBUG
        // ============================================================

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos)
                return;


            Vector3 position =
                transform.position;


            // Detection
            Gizmos.color =
                Color.yellow;

            Gizmos.DrawWireSphere(
                position,
                detectionRange
            );


            // Abandon cible
            Gizmos.color =
                new Color(
                    1f,
                    0.5f,
                    0f
                );

            Gizmos.DrawWireSphere(
                position,
                loseTargetRange
            );


            // Attack
            Gizmos.color =
                Color.red;

            Gizmos.DrawWireSphere(
                position,
                attackRange
            );


            if (_combatTarget != null)
            {
                Gizmos.DrawLine(
                    transform.position,
                    _combatTarget
                        .transform
                        .position
                );
            }
        }


#if UNITY_EDITOR

        private void OnValidate()
        {
            loseTargetRange =
                Mathf.Max(
                    loseTargetRange,
                    detectionRange
                );


            attackRange =
                Mathf.Min(
                    attackRange,
                    loseTargetRange
                );
        }

#endif
    }
}