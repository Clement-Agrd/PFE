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

        private int _stepIndex;

        private float _timer;

        private bool _impactDone;

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

            if (useAnimationEvents)
                return;

            MeleeComboStep step =
                combo[_stepIndex];

            float startup =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        step.startup
                    )
                    : step.startup;

            float recovery =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        step.recovery
                    )
                    : step.recovery;

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

            DoHit(
                combo[_stepIndex]
            );

            Owner?.Audio?.PlayMeleeSwing(
                _stepIndex
            );
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

            Owner?.Animation?.PlayMeleeAttack(
                _stepIndex
            );
        }

        private void CompleteCurrentStep()
        {
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

        private void DoHit(
            MeleeComboStep step)
        {
            if (Owner == null)
                return;

            Transform originTransform =
                hitOrigin != null
                    ? hitOrigin
                    : transform;

            Vector3 center =
                originTransform.position +
                Owner.transform.forward *
                step.forwardOffset;

            int count =
                Physics.OverlapSphereNonAlloc(
                    center,
                    step.hitRadius,
                    _hits,
                    targetMask,
                    triggerInteraction
                );

            int finalDamage =
                Owner.CalculateDamage(
                    EnumStats.StatTypes.PhysicDamage,
                    step.baseDamage,
                    step.physicDamageScaling
                );

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
                        Owner.gameObject
                    );

                damageable.TakeDamage(
                    in damageInfo
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

            MeleeComboStep step =
                combo[index];

            Transform ownerTransform =
                Owner != null
                    ? Owner.transform
                    : transform;

            Transform originTransform =
                hitOrigin != null
                    ? hitOrigin
                    : transform;

            Vector3 center =
                originTransform.position +
                ownerTransform.forward *
                step.forwardOffset;

            Gizmos.DrawWireSphere(
                center,
                step.hitRadius
            );
        }

#endif
    }
}