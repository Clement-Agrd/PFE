using UnityEngine;
using Core.HealthSystem;
using Core.BarracksSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Gère uniquement la détection et la validation des cibles.
    /// Le Controller et les States ne font aucun OverlapSphere eux-mêmes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyTargetSensor : MonoBehaviour
    {
        [Header("Détection")]

        [Tooltip("Layers pouvant être attaqués, par exemple Player + Soldier.")]
        [SerializeField]
        private LayerMask targetMask;

        [SerializeField, Min(0.1f)]
        private float detectionRange = 7f;

        [Tooltip("Distance à partir de laquelle une cible déjà acquise est abandonnée.")]
        [SerializeField, Min(0.1f)]
        private float loseTargetRange = 10f;

        [SerializeField, Min(0.02f)]
        private float detectionInterval = 0.15f;


        [Header("Vision")]
        [SerializeField, Range(1f, 180f)] private float playerFieldOfView = 110f;
        private const float DefaultPlayerVisionDot = .5735764f;
        private static int _playerVisionObstacles;
        private float PlayerVisionDot => Mathf.Cos(playerFieldOfView * .5f * Mathf.Deg2Rad);

        [SerializeField]
        private bool requireLineOfSight;

        [Tooltip("Layers bloquant la vision. Ne pas mettre Player ou Soldier ici.")]
        [SerializeField]
        private LayerMask sightBlockingMask;

        [SerializeField]
        private float eyeHeight = 1f;


        [Header("Debug")]

        [SerializeField]
        private bool drawGizmos = true;


        private readonly Collider[] _hits =
            new Collider[32];

        private float _scanTimer;


        public float DetectionRange =>
            detectionRange;

        public float LoseTargetRange =>
            loseTargetRange;


        public void ResetRuntime()
        {
            _scanTimer = 0f;
        }


        public Health UpdateTarget(
            Health currentTarget,
            Transform owner,
            Health selfHealth,
            float deltaTime)
        {
            _scanTimer -= deltaTime;
            if (!IsTargetValid(currentTarget, owner) ||
                (currentTarget.TryGetComponent<BarracksSoldier>(out _) &&
                 !BarracksSoldier.IsAssignedOpponent(selfHealth, currentTarget)))
                currentTarget = null;

            if (currentTarget != null && currentTarget.CompareTag("Player")) return currentTarget;
            if (_scanTimer > 0f) return currentTarget;
            _scanTimer = detectionInterval;

            var candidate = FindBestTarget(owner, selfHealth);
            if (candidate != null && candidate.CompareTag("Player")) return candidate;
            if (currentTarget != null) return currentTarget;
            var duelist = BarracksSoldier.FindAssignedOpponent(selfHealth, owner.position, detectionRange);
            return IsTargetValid(duelist, owner) ? duelist : candidate;
        }


        public bool IsTargetValid(
            Health target,
            Transform owner)
        {
            if (target == null ||
                owner == null)
            {
                return false;
            }

            if (target.IsDead ||
                !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            float distance =
                GetPlanarDistance(
                    owner.position,
                    target.transform.position
                );

            if (distance >
                loseTargetRange)
            {
                return false;
            }

            if (requireLineOfSight &&
                !HasLineOfSight(
                    owner,
                    target))
            {
                return false;
            }

            return true;
        }


        private Health FindBestTarget(
            Transform owner,
            Health selfHealth)
        {
            if (owner == null)
                return null;

            int count =
                Physics.OverlapSphereNonAlloc(
                    owner.position,
                    detectionRange,
                    _hits,
                    targetMask,
                    QueryTriggerInteraction.Ignore
                );

            Health bestTarget = null;

            float bestSqrDistance = float.PositiveInfinity;
            bool bestIsPlayer = false;

            for (int i = 0;
                 i < count;
                 i++)
            {
                Collider hit =
                    _hits[i];

                if (hit == null)
                    continue;

                if (hit.transform.IsChildOf(
                        owner))
                {
                    continue;
                }

                Health candidate =
                    hit.GetComponentInParent<
                        Health
                    >();

                if (candidate == null ||
                    candidate == selfHealth ||
                    candidate.IsDead ||
                    !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (candidate.TryGetComponent<BarracksSoldier>(out _)) continue;
                bool isPlayer = candidate.CompareTag("Player");
                if (bestIsPlayer && !isPlayer) continue;
                if (isPlayer && !CanSeePlayer(owner, candidate, eyeHeight, PlayerVisionDot)) continue;
                Vector3 difference = candidate.transform.position - owner.position;

                difference.y = 0f;

                if (difference.sqrMagnitude >
                    detectionRange *
                    detectionRange)
                {
                    continue;
                }

                if (requireLineOfSight &&
                    !HasLineOfSight(
                        owner,
                        candidate))
                {
                    continue;
                }

                float sqrDistance =
                    difference.sqrMagnitude;

                if (isPlayer == bestIsPlayer && sqrDistance >= bestSqrDistance)
                {
                    continue;
                }

                bestSqrDistance = sqrDistance;
                bestIsPlayer = isPlayer;

                bestTarget =
                    candidate;
            }

            return bestTarget;
        }


        internal static bool CanSeePlayer(Transform owner, Health player, float height, float minimumDot = DefaultPlayerVisionDot)
        {
            Vector3 direction = Vector3.ProjectOnPlane(player.transform.position - owner.position, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude > .01f && Vector3.Dot(forward, direction.normalized) < minimumDot)
                return false;

            if (_playerVisionObstacles == 0) _playerVisionObstacles = LayerMask.GetMask("Default", "Ground");
            Vector3 eyes = Vector3.up * height;
            return !Physics.Linecast(owner.position + eyes, player.transform.position + eyes,
                _playerVisionObstacles, QueryTriggerInteraction.Ignore);
        }

        private bool HasLineOfSight(
            Transform owner,
            Health target)
        {
            Vector3 origin =
                owner.position +
                Vector3.up *
                eyeHeight;

            Vector3 targetPoint =
                target.transform.position +
                Vector3.up *
                eyeHeight;

            return !Physics.Linecast(
                origin,
                targetPoint,
                sightBlockingMask,
                QueryTriggerInteraction.Ignore
            );
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


#if UNITY_EDITOR

        private void OnValidate()
        {
            loseTargetRange =
                Mathf.Max(
                    loseTargetRange,
                    detectionRange
                );
        }


        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Gizmos.color =
                Color.yellow;

            Gizmos.DrawWireSphere(
                transform.position,
                detectionRange
            );

            Gizmos.color =
                new Color(
                    1f,
                    0.5f,
                    0f
                );

            Gizmos.DrawWireSphere(
                transform.position,
                loseTargetRange
            );
        }

#endif
    }
}
