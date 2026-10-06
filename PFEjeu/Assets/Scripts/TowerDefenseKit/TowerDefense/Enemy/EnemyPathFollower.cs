using UnityEngine;
using UnityEngine.AI;

namespace Core.TowerDefense
{
    /// <summary>
    /// Gère le déplacement sur EnemySplinePath et les destinations NavMesh.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyPathFollower : MonoBehaviour
    {
        [Header("Spline")]

        [SerializeField, Min(0.05f)]
        private float waypointReachDistance = 0.45f;

        [SerializeField, Min(0.1f)]
        private float endReachDistance = 1.2f;


        [Header("NavMesh")]

        [SerializeField, Min(0.02f)]
        private float repathInterval = 0.25f;

        [SerializeField, Min(0.1f)]
        private float navSampleRadius = 5f;


        [Header("Horde")]

        [Tooltip(
            "Chaque ennemi reçoit sa propre position latérale autour de la spline."
        )]
        [SerializeField]
        private bool spreadAcrossPath = true;

        [Tooltip(
            "Evite que l'offset de base soit exactement sur le bord du couloir."
        )]
        [SerializeField, Range(0.1f, 1f)]
        private float widthUsage = 0.85f;

        [Tooltip(
            "Utilise l'évitement haute qualité du NavMeshAgent pour mieux séparer les groupes."
        )]
        [SerializeField]
        private bool useHighQualityAvoidance = true;

        [SerializeField, Range(0, 99)]
        private int minAvoidancePriority = 25;

        [SerializeField, Range(0, 99)]
        private int maxAvoidancePriority = 75;


        private NavMeshAgent _agent;

        private EnemySplinePath _path;

        private int _pathIndex = -1;

        private float _repathTimer;

        private float _baseLateralOffset;

        private float _wanderSeed;


        public EnemySplinePath Path =>
            _path;

        public bool HasValidPath =>
            _path != null &&
            _path.IsValid;

        public bool ReachedEnd { get; private set; }


        private void Awake()
        {
            _agent =
                GetComponent<NavMeshAgent>();

            ApplyAvoidanceProfile();
        }


        public void SetPath(
            EnemySplinePath path)
        {
            _path =
                path;

            _pathIndex = -1;

            ReachedEnd = false;

            _repathTimer = 0f;

            RandomizeHordePosition();

            ApplyAvoidanceProfile();
        }


        public void ResumeFromClosestPoint()
        {
            ReachedEnd = false;

            if (!HasValidPath)
            {
                _pathIndex = -1;
                return;
            }

            int closest =
                _path.FindClosestPointIndex(
                    transform.position
                );

            _pathIndex =
                Mathf.Min(
                    closest + 1,
                    _path.Count - 1
                );

            // Retour au comportement de suivi de route.
            if (_agent != null &&
                _agent.isOnNavMesh)
            {
                _agent.stoppingDistance = 0f;
                _agent.isStopped = false;
            }

            // Force une nouvelle destination dès le prochain Tick.
            _repathTimer = 0f;
        }


        public void Tick(
            float deltaTime)
        {
            if (ReachedEnd ||
                !HasValidPath ||
                _agent == null ||
                !_agent.isOnNavMesh)
            {
                return;
            }

            if (_pathIndex < 0)
            {
                ResumeFromClosestPoint();
            }

            if (_pathIndex >=
                _path.Count)
            {
                MarkReachedEnd();
                return;
            }

            Vector3 waypoint =
                GetNavigationWaypoint(
                    _pathIndex
                );

            float distance =
                GetPlanarDistance(
                    transform.position,
                    waypoint
                );

            bool finalPoint =
                _pathIndex ==
                _path.Count - 1;

            if (finalPoint &&
                distance <=
                endReachDistance)
            {
                MarkReachedEnd();
                return;
            }

            if (!finalPoint &&
                distance <=
                waypointReachDistance)
            {
                _pathIndex++;

                if (_pathIndex >=
                    _path.Count)
                {
                    MarkReachedEnd();
                    return;
                }

                waypoint =
                    GetNavigationWaypoint(
                        _pathIndex
                    );

                SetDestinationSafe(
                    waypoint
                );

                return;
            }

            _repathTimer -=
                deltaTime;

            if (_repathTimer <= 0f)
            {
                _repathTimer =
                    repathInterval;

                SetDestinationSafe(
                    waypoint
                );
            }
        }


        private Vector3 GetNavigationWaypoint(
            int index)
        {
            Vector3 center =
                _path.GetPoint(
                    index
                );

            Vector3 desired =
                spreadAcrossPath
                    ? _path.GetPointWithOffset(
                        index,
                        _baseLateralOffset,
                        _wanderSeed
                    )
                    : center;

            if (NavMesh.SamplePosition(
                    desired,
                    out NavMeshHit offsetHit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                return offsetHit.position;
            }

            // Si la largeur dépasse localement le NavMesh,
            // on retombe proprement sur le centre du chemin.
            if (NavMesh.SamplePosition(
                    center,
                    out NavMeshHit centerHit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                return centerHit.position;
            }

            return center;
        }


        private void RandomizeHordePosition()
        {
            if (!spreadAcrossPath ||
                _path == null)
            {
                _baseLateralOffset = 0f;
                _wanderSeed = 0f;
                return;
            }

            float usableHalfWidth =
                _path.PathHalfWidth *
                Mathf.Clamp01(
                    widthUsage
                );

            _baseLateralOffset =
                Random.Range(
                    -usableHalfWidth,
                    usableHalfWidth
                );

            _wanderSeed =
                Random.Range(
                    0f,
                    Mathf.PI * 2f
                );
        }


        private void ApplyAvoidanceProfile()
        {
            if (_agent == null)
                return;

            if (useHighQualityAvoidance)
            {
                _agent.obstacleAvoidanceType =
                    ObstacleAvoidanceType
                        .HighQualityObstacleAvoidance;
            }

            int minPriority =
                Mathf.Min(
                    minAvoidancePriority,
                    maxAvoidancePriority
                );

            int maxPriority =
                Mathf.Max(
                    minAvoidancePriority,
                    maxAvoidancePriority
                );

            _agent.avoidancePriority =
                Random.Range(
                    minPriority,
                    maxPriority + 1
                );
        }


        public bool SetDestinationSafe(
            Vector3 destination)
        {
            if (_agent == null ||
                !_agent.isOnNavMesh)
            {
                return false;
            }

            if (!NavMesh.SamplePosition(
                    destination,
                    out NavMeshHit hit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                return false;
            }

            if (_agent.isStopped)
            {
                _agent.isStopped =
                    false;
            }

            return _agent.SetDestination(
                hit.position
            );
        }


        public void Stop()
        {
            if (_agent != null &&
                _agent.isOnNavMesh)
            {
                _agent.isStopped =
                    true;
            }
        }


        public void Resume()
        {
            if (_agent != null &&
                _agent.isOnNavMesh)
            {
                _agent.stoppingDistance = 0f;
                _agent.isStopped = false;
            }
        }


        private void MarkReachedEnd()
        {
            ReachedEnd = true;

            Stop();
        }


        private static float GetPlanarDistance(
            Vector3 a,
            Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(
                a,
                b
            );
        }
    }
}
