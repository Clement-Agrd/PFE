using UnityEngine;
using Core.HealthSystem;
using Core.StatsSystem;

namespace ProfessionalTPS
{
    public sealed class BowCombatModule :
        CombatModule
    {
        [Header("Animation")]
        [SerializeField]
        private bool useAnimationEvents = false;

        [Header("Charge")]
        [Tooltip(
            "Temps de charge maximale à AttackSpeed = 1."
        )]
        [SerializeField, Min(0.05f)]
        private float fullChargeTime = 1.1f;

        [SerializeField]
        private AnimationCurve chargeCurve =
            AnimationCurve.EaseInOut(
                0f,
                0f,
                1f,
                1f
            );

        [Header("Damage")]
        [SerializeField, Min(0f)]
        private float baseDamage = 8f;

        [SerializeField, Min(0f)]
        private float physicDamageScaling = 1f;

        [Tooltip(
            "Dégâts d'un tir sans charge par rapport au maximum."
        )]
        [SerializeField, Range(0f, 1f)]
        private float minimumDamageMultiplier = 0.35f;

        [Tooltip(
            "Assigne ici ton DamageType Physical."
        )]
        [SerializeField]
        private DamageType damageType;

        [Header("Projectile Speed")]
        [SerializeField, Min(0.1f)]
        private float minimumProjectileSpeed = 9f;

        [SerializeField, Min(0.1f)]
        private float maximumProjectileSpeed = 38f;

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

        [Header("Fallback Hitscan")]
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

        [Header("Trajectory Preview")]
        [SerializeField]
        private bool showTrajectoryPreview = true;

        [SerializeField]
        private LineRenderer trajectoryLine;

        [SerializeField, Min(4)]
        private int trajectoryPointCount = 28;

        [SerializeField, Min(0.1f)]
        private float trajectoryPreviewDuration = 2f;

        [SerializeField, Min(0.001f)]
        private float trajectoryWidth = 0.025f;

        [SerializeField]
        private Color trajectoryColor =
            new Color(
                1f,
                1f,
                1f,
                0.8f
            );

        private float _drawTimer;

        private float _recoveryTimer;

        private float _pendingCharge;

        private bool _released;

        private readonly RaycastHit[] _previewHits =
            new RaycastHit[16];

        private Material _trajectoryMaterial;

        public bool IsDrawing
        {
            get;
            private set;
        }

        private float EffectiveFullChargeTime
        {
            get
            {
                if (Owner == null)
                    return fullChargeTime;

                return Owner.ScaleAttackTime(
                    fullChargeTime
                );
            }
        }

        public float RawCharge01 =>
            Mathf.Clamp01(
                _drawTimer /
                Mathf.Max(
                    0.01f,
                    EffectiveFullChargeTime
                )
            );

        public float Charge01
        {
            get
            {
                float raw =
                    RawCharge01;

                if (chargeCurve == null)
                    return raw;

                return Mathf.Clamp01(
                    chargeCurve.Evaluate(
                        raw
                    )
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

        public override void Initialize(
            PlayerCombatController owner)
        {
            base.Initialize(
                owner
            );

            EnsureTrajectoryRenderer();
        }

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

            _pendingCharge =
                Charge01;

            IsDrawing = false;

            Owner?.Animation?.PlayBowAttack();

            if (!useAnimationEvents)
            {
                AnimationImpact();
            }
        }

        public override void Tick(
            float deltaTime)
        {
            UpdateTrajectoryPreview();

            if (!IsBusy)
                return;

            if (IsDrawing)
            {
                _drawTimer +=
                    deltaTime;

                return;
            }

            if (useAnimationEvents)
                return;

            if (!_released)
                return;

            _recoveryTimer +=
                deltaTime;

            float effectiveRecovery =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        recovery
                    )
                    : recovery;

            if (_recoveryTimer >=
                effectiveRecovery)
            {
                AnimationFinished();
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

            Fire(
                _pendingCharge
            );

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

            SetTrajectoryVisible(
                false
            );
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

            Vector3 direction =
                Owner.GetAimDirection(
                    origin,
                    aimRange
                );

            float projectileSpeed =
                Mathf.Lerp(
                    minimumProjectileSpeed,
                    maximumProjectileSpeed,
                    charge
                );

            int maximumDamage =
                Owner.CalculateDamage(
                    EnumStats.StatTypes.PhysicDamage,
                    baseDamage,
                    physicDamageScaling
                );

            float chargeDamageMultiplier =
                Mathf.Lerp(
                    minimumDamageMultiplier,
                    1f,
                    charge
                );

            int finalDamage =
                Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        maximumDamage *
                        chargeDamageMultiplier
                    )
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
                    damageType,
                    DamageSourceKind.Bow
                );

                return;
            }

            float fallbackRange =
                Mathf.Lerp(
                    minimumFallbackRange,
                    aimRange,
                    charge
                );

            TryHitscan(
                origin,
                direction,
                fallbackRange,
                finalDamage
            );
        }

        private void UpdateTrajectoryPreview()
        {
            bool shouldShow =
                showTrajectoryPreview &&
                Owner != null &&
                Owner.CurrentMode ==
                    CombatMode.Bow &&
                Owner.IsAiming &&
                !_released;

            if (!shouldShow)
            {
                SetTrajectoryVisible(
                    false
                );

                return;
            }

            EnsureTrajectoryRenderer();

            if (trajectoryLine == null)
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

            float previewCharge =
                IsDrawing
                    ? Charge01
                    : 0f;

            float speed =
                Mathf.Lerp(
                    minimumProjectileSpeed,
                    maximumProjectileSpeed,
                    previewCharge
                );

            int pointCount =
                Mathf.Max(
                    4,
                    trajectoryPointCount
                );

            float duration =
                Mathf.Min(
                    Mathf.Max(
                        0.1f,
                        trajectoryPreviewDuration
                    ),
                    Mathf.Max(
                        0.1f,
                        projectileLifetime
                    )
                );

            float stepTime =
                duration /
                (pointCount - 1);

            float collisionRadius =
                projectilePrefab != null
                    ? projectilePrefab
                        .CollisionRadius
                    : 0.01f;

            LayerMask collisionMask =
                projectilePrefab != null
                    ? projectilePrefab
                        .CollisionMask
                    : fallbackHitMask;

            QueryTriggerInteraction triggerMode =
                projectilePrefab != null
                    ? projectilePrefab
                        .TriggerInteraction
                    : QueryTriggerInteraction.Ignore;

            trajectoryLine.positionCount =
                1;

            trajectoryLine.SetPosition(
                0,
                origin
            );

            Vector3 position =
                origin;

            Vector3 velocity =
                direction.normalized *
                speed;

            int writtenPoints =
                1;

            for (int i = 1;
                 i < pointCount;
                 i++)
            {
                velocity +=
                    Vector3.up *
                    (projectileGravity *
                     stepTime);

                Vector3 nextPosition =
                    position +
                    velocity *
                    stepTime;

                Vector3 segment =
                    nextPosition -
                    position;

                float distance =
                    segment.magnitude;

                if (distance > 0.0001f &&
                    TryGetPreviewHit(
                        position,
                        segment /
                        distance,
                        distance,
                        collisionRadius,
                        collisionMask,
                        triggerMode,
                        out RaycastHit hit))
                {
                    writtenPoints++;

                    trajectoryLine.positionCount =
                        writtenPoints;

                    trajectoryLine.SetPosition(
                        writtenPoints - 1,
                        hit.point
                    );

                    break;
                }

                writtenPoints++;

                trajectoryLine.positionCount =
                    writtenPoints;

                trajectoryLine.SetPosition(
                    writtenPoints - 1,
                    nextPosition
                );

                position =
                    nextPosition;
            }

            SetTrajectoryVisible(
                true
            );
        }


        private bool TryGetPreviewHit(
            Vector3 origin,
            Vector3 direction,
            float distance,
            float radius,
            LayerMask mask,
            QueryTriggerInteraction triggerMode,
            out RaycastHit nearestHit)
        {
            int hitCount =
                Physics.SphereCastNonAlloc(
                    origin,
                    Mathf.Max(
                        0.001f,
                        radius
                    ),
                    direction,
                    _previewHits,
                    distance,
                    mask,
                    triggerMode
                );

            float nearestDistance =
                float.PositiveInfinity;

            nearestHit =
                default;

            bool found =
                false;

            for (int i = 0;
                 i < hitCount;
                 i++)
            {
                RaycastHit candidate =
                    _previewHits[i];

                if (candidate.collider ==
                    null)
                {
                    continue;
                }

                if (Owner != null &&
                    candidate.transform
                        .IsChildOf(
                            Owner.transform))
                {
                    continue;
                }

                if (candidate.distance >=
                    nearestDistance)
                {
                    continue;
                }

                nearestDistance =
                    candidate.distance;

                nearestHit =
                    candidate;

                found =
                    true;
            }

            return found;
        }


        private void EnsureTrajectoryRenderer()
        {
            if (trajectoryLine == null)
            {
                GameObject previewObject =
                    new GameObject(
                        "BowTrajectoryPreview"
                    );

                previewObject.transform.SetParent(
                    transform,
                    false
                );

                trajectoryLine =
                    previewObject.AddComponent<
                        LineRenderer
                    >();
            }

            trajectoryLine.useWorldSpace =
                true;

            trajectoryLine.loop =
                false;

            trajectoryLine.startWidth =
                trajectoryWidth;

            trajectoryLine.endWidth =
                trajectoryWidth;

            trajectoryLine.startColor =
                trajectoryColor;

            trajectoryLine.endColor =
                trajectoryColor;

            trajectoryLine.numCapVertices =
                2;

            trajectoryLine.numCornerVertices =
                2;

            if (trajectoryLine.sharedMaterial ==
                null)
            {
                Shader shader =
                    Shader.Find(
                        "Sprites/Default"
                    );

                if (shader == null)
                {
                    shader =
                        Shader.Find(
                            "Universal Render Pipeline/Unlit"
                        );
                }

                if (shader != null)
                {
                    _trajectoryMaterial =
                        new Material(
                            shader
                        )
                        {
                            name =
                                "Bow Trajectory Preview",
                            hideFlags =
                                HideFlags.DontSave
                        };

                    trajectoryLine.material =
                        _trajectoryMaterial;
                }
            }

            SetTrajectoryVisible(
                false
            );
        }


        private void SetTrajectoryVisible(
            bool visible)
        {
            if (trajectoryLine == null)
                return;

            trajectoryLine.enabled =
                visible;

            if (!visible)
            {
                trajectoryLine.positionCount =
                    0;
            }
        }


        private void OnDisable()
        {
            SetTrajectoryVisible(
                false
            );
        }


        private void OnDestroy()
        {
            if (_trajectoryMaterial ==
                null)
            {
                return;
            }

            Destroy(
                _trajectoryMaterial
            );

            _trajectoryMaterial =
                null;
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
                    Owner.gameObject,
                    DamageSourceKind.Bow
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