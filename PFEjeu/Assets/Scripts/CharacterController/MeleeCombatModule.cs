using System;
using System.Collections.Generic;
using UnityEngine;
using Core.HealthSystem;
using Core.StatsSystem;

namespace ProfessionalTPS
{
    [Serializable]
    public sealed class MeleeComboStep
    {
        [Header("Damage")]
        [Min(0f)]
        public float baseDamage = 10f;

        [Min(0f)]
        public float physicDamageScaling = 1f;

        [Header("Timing")]
        [Min(0f)]
        public float startup = 0.18f;

        [Min(0f)]
        public float recovery = 0.28f;

        [Min(0f)]
        public float queueOpensAt = 0.16f;

        [Header("Hitbox")]
        [Min(0.05f)]
        public float hitRadius = 0.85f;

        [Min(0f)]
        public float forwardOffset = 1f;
    }

    public sealed class MeleeCombatModule :
        CombatModule
    {
        [Header("Prototype / Animation")]
        [SerializeField]
        private bool useAnimationEvents = false;

        [Header("Damage Type")]
        [Tooltip(
            "Assigne ici ton DamageType Physical."
        )]
        [SerializeField]
        private DamageType damageType;

        [Header("Hit Detection")]
        [SerializeField]
        private Transform hitOrigin;

        [SerializeField]
        private LayerMask targetMask = ~0;

        [SerializeField]
        private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;

        [Header("3 Hit Combo")]
        [SerializeField]
        private MeleeComboStep[] combo =
        {
            new MeleeComboStep
            {
                baseDamage = 10f,
                physicDamageScaling = 0.9f,

                startup = 0.16f,
                recovery = 0.24f,
                queueOpensAt = 0.12f,

                hitRadius = 0.8f,
                forwardOffset = 0.95f
            },

            new MeleeComboStep
            {
                baseDamage = 12f,
                physicDamageScaling = 1f,

                startup = 0.18f,
                recovery = 0.26f,
                queueOpensAt = 0.13f,

                hitRadius = 0.9f,
                forwardOffset = 1f
            },

            new MeleeComboStep
            {
                baseDamage = 18f,
                physicDamageScaling = 1.25f,

                startup = 0.24f,
                recovery = 0.38f,
                queueOpensAt = 0.17f,

                hitRadius = 1f,
                forwardOffset = 1.05f
            }
        };

        [Header("Movement")]
        [SerializeField, Range(0f, 1f)]
        private float attackMovementMultiplier = 0.35f;

        private readonly Collider[] _hits =
            new Collider[24];

        private readonly HashSet<IDamageable>
            _damagedThisSwing =
                new HashSet<IDamageable>();

        // Timings are in animation seconds, before the weapon's attack-speed multiplier.
        private const float SlashLeadTime = .09f;
        private const float ReturnSlashLeadTime = .035f;
        private const float FinisherFollowThrough = .065f;
        public event System.Action<int> SlashStarted;

        public readonly struct SwingGeometry
        {
            public readonly Vector3 Origin;
            public readonly Quaternion Rotation;
            public readonly float Radius;
            public readonly float DurationMultiplier;
            public readonly bool Reverse;

            public SwingGeometry(Vector3 origin, Quaternion rotation, float radius, int step, float durationMultiplier)
            {
                Origin = origin;
                Rotation = rotation * Quaternion.Euler(0f, 0f, step == 2 ? 90f : 0f);
                Radius = radius;
                DurationMultiplier = durationMultiplier;
                Reverse = step == 1;
            }

            public Vector3 Point(float t) => Origin + Rotation * Vector3.right *
                (Radius * Mathf.Lerp(Reverse ? -1f : 1f, Reverse ? 1f : -1f, t));
        }

        public SwingGeometry ActiveSwing => BuildSwing(_stepIndex, combo[_stepIndex]);
        public Transform SwingAnchor => Owner != null ? Owner.transform : transform;
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private int _stepIndex;

        private float _timer;

        private bool _impactDone;
        private bool _slashStarted;
        private bool _finisherPlayed;

        private bool _queuedNext;

        public override CombatMode Mode =>
            CombatMode.Melee;

        public override float MovementMultiplier =>
            IsBusy
                ? attackMovementMultiplier
                : 1f;

        public override void AttackPressed()
        {
            if (combo == null ||
                combo.Length == 0)
            {
                return;
            }

            if (!IsBusy)
            {
                StartStep(0);

                return;
            }

            MeleeComboStep step =
                combo[
                    Mathf.Clamp(
                        _stepIndex,
                        0,
                        combo.Length - 1
                    )
                ];

            float queueTime =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        step.queueOpensAt
                    )
                    : step.queueOpensAt;

            if (_stepIndex <
                    combo.Length - 1 &&
                _timer >= queueTime)
            {
                _queuedNext = true;
            }
        }

        public override void Tick(
            float deltaTime)
        {
            if (!IsBusy)
                return;

            _timer += deltaTime;
            MeleeComboStep step = combo[_stepIndex];
            float animationTime = _timer * (Owner != null ? Owner.AttackSpeed : 1f);
            float visualLead = _stepIndex == 1 ? ReturnSlashLeadTime : SlashLeadTime;
            if (!_slashStarted && animationTime >= Mathf.Max(0f, step.startup - visualLead))
                StartSlash();

            if (_stepIndex == 2 && _impactDone && !_finisherPlayed &&
                animationTime >= step.startup + FinisherFollowThrough)
                PlayGroundFinisher();

            if (useAnimationEvents)
                return;

            float startup =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        step.startup
                    )
                    : step.startup;

            float recovery =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        step.recovery * 1.2f
                    )
                    : step.recovery * 1.2f;

            if (!_impactDone &&
                _timer >= startup)
            {
                AnimationImpact();
            }

            if (_timer >=
                startup +
                recovery)
            {
                CompleteCurrentStep();
            }
        }

        public override void AnimationImpact()
        {
            if (!IsBusy ||
                _impactDone)
            {
                return;
            }

            _impactDone = true;

            if (!_slashStarted) StartSlash();
            DoHit(combo[_stepIndex]);
        }

        public override void AnimationFinished()
        {
            if (!IsBusy)
                return;

            CompleteCurrentStep();
        }

        public override void Cancel()
        {
            base.Cancel();

            _timer = 0f;

            _stepIndex = 0;

            _queuedNext = false;

            _impactDone = false;

            _damagedThisSwing.Clear();
        }

        private void StartStep(
            int index)
        {
            _stepIndex =
                Mathf.Clamp(
                    index,
                    0,
                    combo.Length - 1
                );

            _timer = 0f;

            _impactDone = false;

            _queuedNext = false;

            _damagedThisSwing.Clear();

            IsBusy = true;

            _slashStarted = false;
            _finisherPlayed = false;
            Owner?.Animation?.PlayMeleeAttack(_stepIndex);
        }

        private void StartSlash()
        {
            _slashStarted = true;
            Owner?.Audio?.PlayMeleeSwing(_stepIndex);
            SlashStarted?.Invoke(_stepIndex);
        }

        private void CompleteCurrentStep()
        {
            if (_stepIndex == 2 && _impactDone && !_finisherPlayed) PlayGroundFinisher();
            if (_queuedNext &&
                _stepIndex <
                combo.Length - 1)
            {
                StartStep(
                    _stepIndex + 1
                );

                return;
            }

            IsBusy = false;

            _timer = 0f;

            _stepIndex = 0;

            _queuedNext = false;

            _impactDone = false;

            _damagedThisSwing.Clear();
        }

        private void PlayGroundFinisher()
        {
            _finisherPlayed = true;
            if (Owner == null || Owner.SwordFinisher == null) return;
            var swing = ActiveSwing;
            Vector3 floor = swing.Origin + Owner.transform.forward * swing.Radius * .45f;
            var body = Owner.GetComponent<CharacterController>();
            floor.y = body != null ? body.bounds.min.y : Owner.transform.position.y;
            floor = TowerAreaImpact.FindGround(floor, _groundHits, out var normal);
            var fx = Instantiate(Owner.SwordFinisher, floor + normal * .02f,
                Quaternion.FromToRotation(Vector3.up, normal) * Owner.transform.rotation);
            fx.transform.localScale *= swing.Radius;
            Destroy(fx, 4f);
        }

        private void DoHit(
            MeleeComboStep step)
        {
            if (Owner == null)
                return;

            int level = Owner.ForgeLevel(CombatMode.Melee);
            int finalDamage = Owner.CalculateDamage(EnumStats.StatTypes.PhysicDamage,
                step.baseDamage + Core.Village.ForgeDefinition.WeaponDamage(level),
                step.physicDamageScaling + Core.Village.ForgeDefinition.PhysicalScaling(level));
            var swing = ActiveSwing;
            int count = Physics.OverlapSphereNonAlloc(swing.Origin, swing.Radius, _hits, targetMask, triggerInteraction);
            DamageHits(count, swing.Origin, finalDamage);
        }

        private SwingGeometry BuildSwing(int index, MeleeComboStep step)
        {
            var owner = Owner != null ? Owner.transform : transform;
            Vector3 origin = hitOrigin != null ? hitOrigin.position : owner.position + Vector3.up * 1.1f;
            int level = Owner != null ? Owner.ForgeLevel(CombatMode.Melee) : 0;
            float reach = Core.Village.ForgeDefinition.SwordReach(level);
            float durationMultiplier = Owner != null ? 1f / Owner.AttackSpeed : 1f;
            return new SwingGeometry(origin + owner.forward * (step.forwardOffset + reach * .5f),
                Quaternion.LookRotation(owner.forward, Vector3.up), step.hitRadius + reach, index, durationMultiplier);
        }

        private void DamageHits(int count, Vector3 center, int finalDamage)
        {
            for (int i = 0; i < count; i++)
            {
                Collider hit =
                    _hits[i];

                if (hit == null)
                    continue;

                if (hit.transform.IsChildOf(
                        Owner.transform))
                {
                    continue;
                }

                IDamageable damageable =
                    FindDamageable(
                        hit
                    );

                if (damageable == null)
                    continue;

                if (damageable.IsDead)
                    continue;

                if (!_damagedThisSwing.Add(
                        damageable))
                {
                    continue;
                }

                DamageInfo damageInfo =
                    new DamageInfo(
                        finalDamage,
                        damageType,
                        Owner.gameObject,
                        DamageSourceKind.Sword
                    );


                var health = damageable as Health;
                int before = health != null ? health.CurrentHealth : 0;
                damageable.TakeDamage(in damageInfo);

                if (health != null && !health.IsDead && health.CurrentHealth < before)
                {
                    if (health.TryGetComponent<Core.TowerDefense.EnemyController>(out var enemy) && enemy.isActiveAndEnabled)
                        enemy.ApplySwordRecoil(Owner.transform.position, _stepIndex == 2);
                    else if (health.TryGetComponent<Core.TowerDefense.NavEnemy>(out var legacy) && legacy.isActiveAndEnabled)
                        legacy.ApplySwordRecoil(Owner.transform.position, _stepIndex == 2);
                }


                // ========================================================
                // IMPACT VFX
                // ========================================================

                Vector3 impactPoint =
                    hit.ClosestPoint(
                        center
                    );


                Vector3 impactNormal =
                    center -
                    impactPoint;


                if (impactNormal.sqrMagnitude <
                    0.001f)
                {
                    impactNormal =
                        -Owner.transform.forward;
                }
                else
                {
                    impactNormal.Normalize();
                }


                Owner?.VFX?.PlayImpact(
                    impactPoint,
                    impactNormal
                );
            }
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

#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {
            if (combo == null ||
                combo.Length == 0)
            {
                return;
            }

            int index =
                Mathf.Clamp(
                    _stepIndex,
                    0,
                    combo.Length - 1
                );

            var swing = BuildSwing(index, combo[index]);
            Gizmos.DrawWireSphere(swing.Origin, swing.Radius);
        }

#endif
    }
}