using UnityEngine;
using Core.HealthSystem;
using Core.StatsSystem;

namespace ProfessionalTPS
{
    public sealed class MagicCombatModule :
        CombatModule
    {
        [Header("Prototype / Animation")]
        [SerializeField]
        private bool useAnimationEvents = false;

        [SerializeField, Min(0f)]
        private float castDelay = 0.3f;

        [SerializeField, Min(0f)]
        private float recovery = 0.4f;

        [Header("Damage")]
        [SerializeField, Min(0f)]
        private float baseDamage = 12f;

        [SerializeField, Min(0f)]
        private float magicDamageScaling = 1f;

        [Tooltip(
            "Assigne ici ton DamageType Magic."
        )]
        [SerializeField]
        private DamageType damageType;

        [Header("Projectile")]
        [SerializeField]
        private Transform castOrigin;

        [SerializeField]
        private CombatProjectile projectilePrefab;

        [SerializeField, Min(0.1f)]
        private float projectileSpeed = 24f;

        [SerializeField]
        private float projectileGravity = 0f;

        [SerializeField, Min(0.1f)]
        private float projectileLifetime = 7f;

        [SerializeField, Min(1f)]
        private float aimRange = 150f;

        [Header("Fallback Hitscan")]
        [SerializeField]
        private LayerMask fallbackHitMask = ~0;

        [Header("Movement")]
        [SerializeField, Range(0f, 1f)]
        private float attackMovementMultiplier = 0.4f;

        private float _timer;

        private bool _released;

        public override CombatMode Mode =>
            CombatMode.Magic;

        public override float MovementMultiplier =>
            IsBusy
                ? attackMovementMultiplier
                : 1f;

        public override bool FaceCameraWhileBusy =>
            true;

        public override void AttackPressed()
        {
            if (IsBusy)
                return;

            IsBusy = true;

            _timer = 0f;

            _released = false;

            Owner?.Animation?.PlayMagicAttack();
        }

        public override void Tick(
            float deltaTime)
        {
            if (!IsBusy)
                return;

            _timer += deltaTime;

            if (useAnimationEvents)
                return;

            float effectiveCastDelay =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        castDelay
                    )
                    : castDelay;

            float effectiveRecovery =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        recovery
                    )
                    : recovery;

            if (!_released &&
                _timer >= effectiveCastDelay)
            {
                AnimationImpact();
            }

            if (_timer >=
                effectiveCastDelay +
                effectiveRecovery)
            {
                AnimationFinished();
            }
        }

        public override void AnimationImpact()
        {
            if (!IsBusy ||
                _released)
            {
                return;
            }

            _released = true;

            Cast();

            Owner?.Audio?.PlayMagicCast();
        }

        public override void AnimationFinished()
        {
            IsBusy = false;

            _timer = 0f;

            _released = false;
        }

        public override void Cancel()
        {
            base.Cancel();

            _timer = 0f;

            _released = false;
        }

        private void Cast()
        {
            if (Owner == null)
                return;

            Vector3 origin =
                castOrigin != null
                    ? castOrigin.position
                    : Owner.transform.position +
                      Vector3.up * 1.35f +
                      Owner.transform.forward * 0.55f;

            Vector3 direction =
                Owner.GetAimDirection(
                    origin,
                    aimRange
                );

            int finalDamage =
                Owner.CalculateDamage(
                    EnumStats.StatTypes.MagicDamage,
                    baseDamage,
                    magicDamageScaling
                );

            if (projectilePrefab != null)
            {
                CombatProjectile projectile =
                    Instantiate(
                        projectilePrefab,
                        origin,
                        Quaternion.LookRotation(
                            direction
                        )
                    );

                projectile.Launch(
                    direction,
                    Owner.gameObject,
                    finalDamage,
                    projectileSpeed,
                    projectileGravity,
                    projectileLifetime,
                    damageType
                );

                return;
            }

            TryHitscan(
                origin,
                direction,
                finalDamage
            );
        }

        private void TryHitscan(
            Vector3 origin,
            Vector3 direction,
            int finalDamage)
        {
            RaycastHit[] hits =
                Physics.RaycastAll(
                    origin,
                    direction,
                    aimRange,
                    fallbackHitMask,
                    QueryTriggerInteraction.Ignore
                );

            if (hits.Length == 0)
                return;

            float nearestDistance =
                float.PositiveInfinity;

            RaycastHit nearestHit =
                default;

            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit =
                    hits[i];

                if (hit.collider == null)
                    continue;

                if (hit.transform.IsChildOf(
                        Owner.transform))
                {
                    continue;
                }

                if (hit.distance >=
                    nearestDistance)
                {
                    continue;
                }

                nearestDistance =
                    hit.distance;

                nearestHit =
                    hit;

                found = true;
            }

            if (!found)
                return;

            IDamageable damageable =
                FindDamageable(
                    nearestHit.collider
                );

            if (damageable == null ||
                damageable.IsDead)
            {
                return;
            }

            DamageInfo info =
                new DamageInfo(
                    finalDamage,
                    damageType,
                    Owner.gameObject
                );

            damageable.TakeDamage(
                in info
            );
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

            for (int i = 0; i < behaviours.Length; i++)
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