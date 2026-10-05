using UnityEngine;
using Core.HealthSystem;

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


        [Header("Vision optionnelle")]

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
            _scanTimer -=
                deltaTime;

            if (IsTargetValid(
                    currentTarget,
                    owner))
            {
                return currentTarget;
            }

            if (_scanTimer > 0f)
                return null;

            _scanTimer =
                detectionInterval;

            return FindBestTarget(
                owner,
                selfHealth
            );
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

            float bestSqrDistance =
                float.PositiveInfinity;

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

                Vector3 difference =
                    candidate.transform.position -
                    owner.position;

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

            return bestTarget;
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
