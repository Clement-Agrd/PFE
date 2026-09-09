using UnityEngine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    public sealed class CombatProjectile : MonoBehaviour
    {
        [Header("Collision")]
        [SerializeField, Min(0.001f)]
        private float radius = 0.08f;

        [SerializeField]
        private LayerMask collisionMask = ~0;

        [SerializeField]
        private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;

        [Header("Présentation optionnelle")]
        [Tooltip("VFX créé au moment de l'impact.")]
        [SerializeField]
        private GameObject impactPrefab;

        private GameObject _owner;

        private int _damage;
        private float _speed;
        private float _gravity;
        private float _lifetime;

        private DamageType _damageType;

        private Vector3 _velocity;

        private bool _launched;

        // Permet de gérer plusieurs colliders rencontrés
        // sans allocation mémoire par frame.
        private readonly RaycastHit[] _castHits =
            new RaycastHit[12];

        /// <summary>
        /// Initialise le projectile.
        /// Appelé par BowCombatModule ou MagicCombatModule.
        /// </summary>
        public void Launch(
            Vector3 direction,
            GameObject owner,
            int damage,
            float speed,
            float gravity,
            float lifetime,
            DamageType damageType)
        {
            _owner = owner;

            _damage = Mathf.Max(0, damage);

            _speed = speed;
            _gravity = gravity;
            _lifetime = lifetime;

            _damageType = damageType;

            _velocity =
                direction.normalized * _speed;

            _launched = true;

            if (_velocity.sqrMagnitude > 0.001f)
            {
                transform.rotation =
                    Quaternion.LookRotation(
                        _velocity.normalized
                    );
            }
        }

        private void Update()
        {
            if (!_launched)
                return;

            float deltaTime = Time.deltaTime;

            _lifetime -= deltaTime;

            if (_lifetime <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // Gravité.
            _velocity +=
                Vector3.up *
                (_gravity * deltaTime);

            Vector3 displacement =
                _velocity * deltaTime;

            float distance =
                displacement.magnitude;

            if (distance > 0.0001f)
            {
                int hitCount =
                    Physics.SphereCastNonAlloc(
                        transform.position,
                        radius,
                        displacement.normalized,
                        _castHits,
                        distance,
                        collisionMask,
                        triggerInteraction
                    );

                bool foundValidHit = false;

                RaycastHit nearestHit = default;

                float nearestDistance =
                    float.PositiveInfinity;

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit candidate =
                        _castHits[i];

                    if (candidate.collider == null)
                        continue;

                    // Ignore complètement celui qui a lancé le projectile.
                    if (_owner != null &&
                        candidate.transform.IsChildOf(
                            _owner.transform))
                    {
                        continue;
                    }

                    if (candidate.distance <
                        nearestDistance)
                    {
                        nearestDistance =
                            candidate.distance;

                        nearestHit =
                            candidate;

                        foundValidHit = true;
                    }
                }

                if (foundValidHit)
                {
                    HandleImpact(nearestHit);
                    return;
                }
            }

            transform.position +=
                displacement;

            // Oriente le projectile dans sa trajectoire.
            if (_velocity.sqrMagnitude > 0.001f)
            {
                transform.rotation =
                    Quaternion.LookRotation(
                        _velocity.normalized
                    );
            }
        }

        private void HandleImpact(
            RaycastHit hit)
        {
            IDamageable damageable =
                FindDamageable(hit.collider);

            if (damageable != null &&
                !damageable.IsDead)
            {
                DamageInfo damageInfo =
                    new DamageInfo(
                        _damage,
                        _damageType,
                        _owner
                    );

                damageable.TakeDamage(
                    in damageInfo
                );
            }

            // VFX facultatif.
            if (impactPrefab != null)
            {
                Quaternion rotation =
                    hit.normal.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(hit.normal)
                        : Quaternion.identity;

                Instantiate(
                    impactPrefab,
                    hit.point,
                    rotation
                );
            }

            Destroy(gameObject);
        }

        private static IDamageable FindDamageable(
            Collider collider)
        {
            if (collider == null)
                return null;

            MonoBehaviour[] behaviours =
                collider.GetComponentsInParent<MonoBehaviour>(
                    true
                );

            for (int i = 0;
                 i < behaviours.Length;
                 i++)
            {
                if (behaviours[i]
                    is IDamageable damageable)
                {
                    return damageable;
                }
            }

            return null;
        }
    }
}