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


        private NavMeshAgent _agent;

        private EnemySplinePath _path;

        private int _pathIndex = -1;

        private float _repathTimer;


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
        }


        public void SetPath(
            EnemySplinePath path)
        {
            _path =
                path;

            _pathIndex = -1;

            ReachedEnd = false;

            _repathTimer = 0f;
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

            Resume();
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
                _path.GetPoint(
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
                    _path.GetPoint(
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
                _agent.isStopped =
                    false;
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
