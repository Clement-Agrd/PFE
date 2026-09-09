using UnityEngine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    public sealed class BowCombatModule : CombatModule
    {
        [Header("Prototype / Animation")]
        [Tooltip(
            "OFF = tir géré par timer.\n" +
            "ON = l'animation doit appeler AE_AttackImpact et AE_AttackFinished."
        )]
        [SerializeField]
        private bool useAnimationEvents = false;

        [SerializeField, Min(0f)]
        private float releaseDelay = 0.22f;

        [SerializeField, Min(0f)]
        private float recovery = 0.35f;

        [Header("Dégâts")]
        [SerializeField, Min(0)]
        private int damage = 28;

        [Tooltip(
            "Type de dégâts de la flèche. " +
            "Exemple : Piercing."
        )]
        [SerializeField]
        private DamageType damageType;

        [Header("Projectile")]
        [SerializeField]
        private Transform muzzle;

        [Tooltip(
            "Prefab de flèche contenant CombatProjectile. " +
            "Peut rester vide pendant le prototype."
        )]
        [SerializeField]
        private CombatProjectile projectilePrefab;

        [SerializeField, Min(0.1f)]
        private float projectileSpeed = 38f;

        [SerializeField]
        private float projectileGravity = -9f;

        [SerializeField, Min(0.1f)]
        private float projectileLifetime = 8f;

        [SerializeField, Min(1f)]
        private float aimRange = 180f;

        [Header("Fallback sans prefab")]
        [Tooltip(
            "Si Projectile Prefab est vide, " +
            "un Raycast est utilisé pour tester l'arc."
        )]
        [SerializeField]
        private LayerMask fallbackHitMask = ~0;

        [Header("Déplacement pendant l'attaque")]
        [SerializeField, Range(0f, 1f)]
        private float attackMovementMultiplier = 0.5f;

        private float _timer;
        private bool _released;

        public override CombatMode Mode =>
            CombatMode.Bow;

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

            Owner?.Animation?.PlayBowAttack();
        }

        public override void Tick(
            float deltaTime)
        {
            if (!IsBusy)
                return;

            _timer += deltaTime;

            if (useAnimationEvents)
                return;

            if (!_released &&
                _timer >= releaseDelay)
            {
                AnimationImpact();
            }

            if (_timer >=
                releaseDelay + recovery)
            {
                AnimationFinished();
            }
        }

        public override void AnimationImpact()
        {
            if (!IsBusy || _released)
                return;

            _released = true;

            Fire();

            Owner?.Audio?.PlayBowRelease();
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

        private void Fire()
        {
            if (Owner == null)
                return;

            Vector3 origin =
                muzzle != null
                    ? muzzle.position
                    : Owner.transform.position +
                      Vector3.up * 1.4f +
                      Owner.transform.forward * 0.5f;

            Vector3 direction =
                Owner.GetAimDirection(
                    origin,
                    aimRange
                );

            // -----------------------------------
            // Vraie flèche
            // -----------------------------------

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
                    damage,
                    projectileSpeed,
                    projectileGravity,
                    projectileLifetime,
                    damageType
                );

                return;
            }

            // -----------------------------------
            // Prototype sans prefab
            // -----------------------------------

            TryHitscan(
                origin,
                direction
            );
        }

        private void TryHitscan(
            Vector3 origin,
            Vector3 direction)
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

            // RaycastAll ne garantit pas l'ordre.
            // On cherche donc manuellement
            // le premier impact valide.
            float nearestDistance =
                float.PositiveInfinity;

            RaycastHit nearestHit =
                default;

            bool found =
                false;

            for (int i = 0;
                 i < hits.Length;
                 i++)
            {
                RaycastHit hit =
                    hits[i];

                if (hit.collider == null)
                    continue;

                // Ignore le joueur.
                if (hit.transform.IsChildOf(
                    Owner.transform))
                {
                    continue;
                }

                if (hit.distance <
                    nearestDistance)
                {
                    nearestDistance =
                        hit.distance;

                    nearestHit =
                        hit;

                    found =
                        true;
                }
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

            DamageInfo damageInfo =
                new DamageInfo(
                    damage,
                    damageType,
                    Owner.gameObject
                );

            damageable.TakeDamage(
                in damageInfo
            );
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