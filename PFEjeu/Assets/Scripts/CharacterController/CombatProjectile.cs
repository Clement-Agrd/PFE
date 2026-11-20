using UnityEngine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    public sealed class CombatProjectile :
        MonoBehaviour
    {
        [Header("Collision")]

        [SerializeField, Min(0.001f)]
        private float radius = 0.08f;

        [SerializeField]
        private LayerMask collisionMask = ~0;

        [SerializeField]
        private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;


        [Header("Presentation")]

        [SerializeField]
        private GameObject impactPrefab;


        private GameObject _owner;

        private int _damage;

        private float _speed;

        private float _gravity;

        private float _lifetime;

        private DamageType _damageType;

        private DamageSourceKind _sourceKind;

        private Vector3 _velocity;

        private bool _launched;
        private float _explosionRadius;
        private SOTower _element;
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        internal static CombatProjectile CreateElemental(GameObject visual, CombatProjectile template,
            Vector3 position, Quaternion rotation)
        {
            var instance = Instantiate(visual, position, rotation);
            if (instance.TryGetComponent<TowerProjectil>(out var towerProjectile)) towerProjectile.enabled = false;
            var projectile = instance.AddComponent<CombatProjectile>();
            projectile.radius = template.radius;
            projectile.collisionMask = template.collisionMask;
            projectile.triggerInteraction = template.triggerInteraction;
            projectile.impactPrefab = template.impactPrefab;
            return projectile;
        }

        internal void SetExplosion(SOTower element)
        {
            _element = element;
            _explosionRadius = element != null && element.ImpactRadius > 0f
                ? element.ImpactRadius : Core.Village.ForgeDefinition.ExplosionRadius;
        }


        private readonly RaycastHit[] _castHits =
            new RaycastHit[12];


        public float CollisionRadius =>
            radius;

        public LayerMask CollisionMask =>
            collisionMask;

        public QueryTriggerInteraction TriggerInteraction =>
            triggerInteraction;


        public void Launch(
            Vector3 direction,
            GameObject owner,
            int damage,
            float speed,
            float gravity,
            float lifetime,
            DamageType damageType,
            DamageSourceKind sourceKind)
        {
            _owner = owner;
            // Collision is swept by this script. A solid collider can push the shooter at the muzzle.
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            _damage =
                Mathf.Max(
                    0,
                    damage
                );

            _speed =
                speed;

            _gravity =
                gravity;

            _lifetime =
                lifetime;

            _damageType =
                damageType;

            _sourceKind =
                sourceKind;


            _velocity =
                direction.normalized *
                _speed;


            _launched =
                true;


            if (_velocity.sqrMagnitude >
                0.001f)
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


            float dt =
                Time.deltaTime;


            _lifetime -=
                dt;


            if (_lifetime <= 0f)
            {
                Destroy(
                    gameObject
                );

                return;
            }


            _velocity +=
                Vector3.up *
                (_gravity * dt);


            Vector3 displacement =
                _velocity *
                dt;


            float distance =
                displacement.magnitude;


            if (distance >
                0.0001f)
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


                bool foundValidHit =
                    false;


                RaycastHit nearestHit =
                    default;


                float nearestDistance =
                    float.PositiveInfinity;


                for (int i = 0;
                     i < hitCount;
                     i++)
                {
                    RaycastHit candidate =
                        _castHits[i];


                    if (candidate.collider ==
                        null)
                    {
                        continue;
                    }


                    if (_owner != null &&
                        candidate.transform
                            .IsChildOf(
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

                        foundValidHit =
                            true;
                    }
                }


                if (foundValidHit)
                {
                    HandleImpact(
                        nearestHit
                    );

                    return;
                }
            }


            transform.position +=
                displacement;


            if (_velocity.sqrMagnitude >
                0.001f)
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
            if (_explosionRadius > 0f)
            {
                Explode(hit);
                Destroy(gameObject);
                return;
            }

            IDamageable damageable =
                FindDamageable(
                    hit.collider
                );


            if (damageable != null &&
                !damageable.IsDead)
            {
                DamageInfo info =
                    new DamageInfo(
                        _damage,
                        _damageType,
                        _owner,
                        _sourceKind
                    );


                damageable.TakeDamage(
                    in info
                );
            }


            if (impactPrefab != null)
            {
                Quaternion rotation =
                    hit.normal.sqrMagnitude >
                    0.001f
                        ? Quaternion.LookRotation(
                            hit.normal
                        )
                        : Quaternion.identity;


                Instantiate(
                    impactPrefab,
                    hit.point,
                    rotation
                );
            }


            Destroy(
                gameObject
            );
        }


        private void Explode(RaycastHit hit)
        {
            var struck = hit.collider.GetComponentInParent<Health>();
            Vector3 impact = hit.point + hit.normal * .08f;
            Vector3 floor = impact;
            if (struck != null) floor.y = hit.collider.bounds.min.y;
            var ground = TowerAreaImpact.FindGround(floor, GroundHits, out var normal);
            var mask = _element != null ? _element.TargetLayer.value : LayerMask.GetMask("Enemy");
            var hitHealths = new System.Collections.Generic.HashSet<Health>();
            if (struck != null && (mask & (1 << hit.collider.gameObject.layer)) != 0) hitHealths.Add(struck);
            foreach (var body in Physics.OverlapSphere(impact, _explosionRadius, mask, triggerInteraction))
            {
                var health = body.GetComponentInParent<Health>();
                if (health != null) hitHealths.Add(health);
            }
            foreach (var health in hitHealths)
            {
                if (health.IsDead || (_owner != null && health.transform.IsChildOf(_owner.transform))) continue;
                health.TakeDamage(new DamageInfo(_damage, _element != null ? _element.DamageType : _damageType, _owner, _sourceKind));
                if (_element != null && !health.IsDead) EtatManager.ApplyTo(health, _element.ETAT);
            }

            var effectPrefab = _element != null ? _element.ImpactPrefab : impactPrefab;
            if (effectPrefab == null) return;
            bool showGround = struck != null || hit.normal.y >= .35f;
            Vector3 effectNormal = showGround ? normal : hit.normal;
            var effect = Instantiate(effectPrefab, impact, Quaternion.FromToRotation(Vector3.forward, effectNormal));
            if (effect.TryGetComponent<TowerAreaImpact>(out var area))
                area.Play(_explosionRadius, ground, normal, showGround);
            else
            {
                foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.startSizeMultiplier *= _explosionRadius;
                }
            }
            Destroy(effect, 3f);
        }

        private static IDamageable FindDamageable(
            Collider collider)
        {
            if (collider == null)
                return null;


            MonoBehaviour[] behaviours =
                collider.GetComponentsInParent<
                    MonoBehaviour
                >(true);


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