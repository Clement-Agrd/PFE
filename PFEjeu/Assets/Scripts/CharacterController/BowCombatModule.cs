using UnityEngine;
using Core.HealthSystem;

namespace ProfessionalTPS
{
    public sealed class BowCombatModule : CombatModule
    {
        [Header("Animation")]
        [Tooltip(
            "OFF = le projectile part directement au relâchement.\n" +
            "ON = AE_AttackImpact doit être placé sur la frame où la corde est libérée."
        )]
        [SerializeField]
        private bool useAnimationEvents = false;

        [Header("Charge")]
        [Tooltip(
            "Temps nécessaire pour atteindre la puissance maximale."
        )]
        [SerializeField, Min(0.05f)]
        private float fullChargeTime = 1.1f;

        [Tooltip(
            "Courbe de puissance en fonction du temps de charge."
        )]
        [SerializeField]
        private AnimationCurve chargeCurve =
            AnimationCurve.EaseInOut(
                0f,
                0f,
                1f,
                1f
            );

        [Header("Vitesse flèche")]
        [SerializeField, Min(0.1f)]
        private float minimumProjectileSpeed = 9f;

        [SerializeField, Min(0.1f)]
        private float maximumProjectileSpeed = 38f;

        [Header("Dégâts")]
        [SerializeField, Min(0)]
        private int damage = 28;

        [SerializeField]
        private DamageType damageType;

        [Tooltip(
            "Dégâts d'un tir instantané par rapport aux dégâts maximum."
        )]
        [SerializeField, Range(0f, 1f)]
        private float minimumDamageMultiplier = 0.35f;

        [Header("Projectile")]
        [SerializeField]
        private Transform muzzle;

        [SerializeField]
        private CombatProjectile projectilePrefab;

        [SerializeField]
        private float projectileGravity = -9f;

        [SerializeField, Min(0.1f)]
        private float projectileLifetime = 8f;

        [SerializeField, Min(1f)]
        private float aimRange = 180f;

        [Header("Fallback sans projectile")]
        [SerializeField]
        private LayerMask fallbackHitMask = ~0;

        [SerializeField, Min(1f)]
        private float minimumFallbackRange = 8f;

        [Header("Recovery")]
        [SerializeField, Min(0f)]
        private float recovery = 0.3f;

        [Header("Movement")]
        [SerializeField, Range(0f, 1f)]
        private float drawingMovementMultiplier = 0.5f;

        private float _drawTimer;
        private float _recoveryTimer;

        private float _pendingCharge;

        private bool _released;

        public bool IsDrawing { get; private set; }

        public float RawCharge01 =>
            Mathf.Clamp01(
                _drawTimer /
                Mathf.Max(
                    0.01f,
                    fullChargeTime
                )
            );

        public float Charge01
        {
            get
            {
                float raw = RawCharge01;

                if (chargeCurve == null)
                    return raw;

                return Mathf.Clamp01(
                    chargeCurve.Evaluate(raw)
                );
            }
        }

        public override CombatMode Mode =>
            CombatMode.Bow;

        public override float MovementMultiplier =>
            IsBusy
                ? drawingMovementMultiplier
                : 1f;

        public override bool FaceCameraWhileBusy =>
            true;

        public override void AttackPressed()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            IsDrawing = true;

            _drawTimer = 0f;
            _recoveryTimer = 0f;

            _released = false;
            _pendingCharge = 0f;
        }

        public override void AttackReleased()
        {
            if (!IsBusy ||
                !IsDrawing)
            {
                return;
            }

            _pendingCharge = Charge01;

            IsDrawing = false;

            // Pour l'instant ce Trigger peut servir
            // d'animation de lâcher de corde.
            Owner?.Animation?.PlayBowAttack();

            if (!useAnimationEvents)
            {
                AnimationImpact();
            }
        }

        public override void Tick(
            float deltaTime)
        {
            if (!IsBusy)
                return;

            // Tant qu'on garde le bouton appuyé,
            // la corde reste tendue.
            if (IsDrawing)
            {
                _drawTimer += deltaTime;

                return;
            }

            if (useAnimationEvents)
            {
                // L'Animator appellera Impact puis Finished.
                return;
            }

            if (_released)
            {
                _recoveryTimer += deltaTime;

                if (_recoveryTimer >= recovery)
                {
                    AnimationFinished();
                }
            }
        }

        public override void AnimationImpact()
        {
            if (!IsBusy ||
                IsDrawing ||
                _released)
            {
                return;
            }

            _released = true;

            Fire(_pendingCharge);

            Owner?.Audio?.PlayBowRelease();
        }

        public override void AnimationFinished()
        {
            IsBusy = false;
            IsDrawing = false;

            _drawTimer = 0f;
            _recoveryTimer = 0f;

            _pendingCharge = 0f;
            _released = false;
        }

        public override void Cancel()
        {
            base.Cancel();

            IsDrawing = false;

            _drawTimer = 0f;
            _recoveryTimer = 0f;

            _pendingCharge = 0f;
            _released = false;
        }

        private void Fire(
            float charge)
        {
            if (Owner == null)
                return;

            charge =
                Mathf.Clamp01(charge);

            Vector3 origin =
                muzzle != null
                    ? muzzle.position
                    : Owner.transform.position +
                      Vector3.up * 1.4f +
                      Owner.transform.forward * 0.5f;

            float projectileSpeed =
                Mathf.Lerp(
                    minimumProjectileSpeed,
                    maximumProjectileSpeed,
                    charge
                );

            float damageMultiplier =
                Mathf.Lerp(
                    minimumDamageMultiplier,
                    1f,
                    charge
                );

            int finalDamage =
                Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        damage *
                        damageMultiplier
                    )
                );

            Vector3 direction =
                Owner.GetAimDirection(
                    origin,
                    aimRange
                );

            // ========================================================
            // VRAIE FLECHE
            // ========================================================

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

            // ========================================================
            // MODE TEST HITSCAN
            // ========================================================

            float testRange =
                Mathf.Lerp(
                    minimumFallbackRange,
                    aimRange,
                    charge
                );

            TryHitscan(
                origin,
                direction,
                testRange,
                finalDamage
            );
        }

        private void TryHitscan(
            Vector3 origin,
            Vector3 direction,
            float range,
            int finalDamage)
        {
            RaycastHit[] hits =
                Physics.RaycastAll(
                    origin,
                    direction,
                    range,
                    fallbackHitMask,
                    QueryTriggerInteraction.Ignore
                );

            float nearestDistance =
                float.PositiveInfinity;

            RaycastHit nearestHit =
                default;

            bool found = false;

            for (int i = 0;
                 i < hits.Length;
                 i++)
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

                if (hit.distance <
                    nearestDistance)
                {
                    nearestDistance =
                        hit.distance;

                    nearestHit = hit;

                    found = true;
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