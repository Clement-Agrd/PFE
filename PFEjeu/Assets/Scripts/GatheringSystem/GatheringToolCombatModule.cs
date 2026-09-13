using System.Collections.Generic;
using UnityEngine;

using Core.HealthSystem;
using Core.StatsSystem;

namespace ProfessionalTPS
{
    /// <summary>
    /// Module générique pour :
    /// - Hache
    /// - Pioche
    ///
    /// Les dégâts utilisent PhysicDamage.
    /// AttackSpeed modifie startup/recovery.
    /// </summary>
    public sealed class GatheringToolCombatModule :
        CombatModule
    {
        public enum ToolMode
        {
            Axe,
            Pickaxe
        }


        [Header("Tool")]

        [SerializeField]
        private ToolMode toolMode;


        [Header("Animation")]

        [Tooltip(
            "OFF = timing prototype.\n" +
            "ON = AE_AttackImpact / AE_AttackFinished."
        )]
        [SerializeField]
        private bool useAnimationEvents = false;


        [Header("Damage")]

        [SerializeField, Min(0f)]
        private float baseDamage = 12f;

        [SerializeField, Min(0f)]
        private float physicDamageScaling = 1f;

        [Tooltip(
            "Assigne le DamageType Physical."
        )]
        [SerializeField]
        private DamageType damageType;


        [Header("Timing")]

        [SerializeField, Min(0f)]
        private float startup = 0.32f;

        [SerializeField, Min(0f)]
        private float recovery = 0.45f;


        [Header("Hit Detection")]

        [SerializeField]
        private Transform hitOrigin;

        [SerializeField, Min(0.05f)]
        private float hitRadius = 0.85f;

        [SerializeField, Min(0f)]
        private float forwardOffset = 0.9f;

        [SerializeField]
        private LayerMask targetMask = ~0;

        [SerializeField]
        private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;


        [Header("Movement")]

        [SerializeField, Range(0f, 1f)]
        private float attackMovementMultiplier = 0.25f;


        private readonly Collider[] _hits =
            new Collider[24];


        private readonly HashSet<IDamageable>
            _damagedThisSwing =
                new HashSet<IDamageable>();


        private float _timer;

        private bool _impactDone;


        // ============================================================
        // PUBLIC
        // ============================================================

        public ToolMode Tool =>
            toolMode;


        public override CombatMode Mode =>
            toolMode == ToolMode.Axe
                ? CombatMode.Axe
                : CombatMode.Pickaxe;


        public override float MovementMultiplier =>
            IsBusy
                ? attackMovementMultiplier
                : 1f;


        // ============================================================
        // ATTACK
        // ============================================================

        public override void AttackPressed()
        {
            if (IsBusy)
                return;


            IsBusy = true;

            _timer = 0f;

            _impactDone = false;

            _damagedThisSwing.Clear();


            if (toolMode == ToolMode.Axe)
            {
                Owner?.Animation?.PlayAxeAttack();
            }
            else
            {
                Owner?.Animation?.PlayPickaxeAttack();
            }
        }


        public override void Tick(
            float deltaTime)
        {
            if (!IsBusy)
                return;


            _timer +=
                deltaTime;


            if (useAnimationEvents)
                return;


            float effectiveStartup =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        startup
                    )
                    : startup;


            float effectiveRecovery =
                Owner != null
                    ? Owner.ScaleAttackTime(
                        recovery
                    )
                    : recovery;


            if (!_impactDone &&
                _timer >= effectiveStartup)
            {
                AnimationImpact();
            }


            if (_timer >=
                effectiveStartup +
                effectiveRecovery)
            {
                AnimationFinished();
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


            DoHit();


            Owner?.Audio?.PlayGatheringSwing(
                toolMode ==
                ToolMode.Axe
            );
        }


        public override void AnimationFinished()
        {
            IsBusy = false;

            _timer = 0f;

            _impactDone = false;

            _damagedThisSwing.Clear();
        }


        public override void Cancel()
        {
            base.Cancel();

            _timer = 0f;

            _impactDone = false;

            _damagedThisSwing.Clear();
        }


        // ============================================================
        // HIT
        // ============================================================

        private void DoHit()
        {
            if (Owner == null)
                return;


            Transform origin =
                hitOrigin != null
                    ? hitOrigin
                    : transform;


            Vector3 center =
                origin.position +
                Owner.transform.forward *
                forwardOffset;


            int count =
                Physics.OverlapSphereNonAlloc(
                    center,
                    hitRadius,
                    _hits,
                    targetMask,
                    triggerInteraction
                );


            int finalDamage =
                Owner.CalculateDamage(
                    EnumStats.StatTypes.PhysicDamage,
                    baseDamage,
                    physicDamageScaling
                );


            DamageSourceKind sourceKind =
                toolMode == ToolMode.Axe
                    ? DamageSourceKind.Axe
                    : DamageSourceKind.Pickaxe;


            for (int i = 0;
                 i < count;
                 i++)
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


                if (damageable == null ||
                    damageable.IsDead)
                {
                    continue;
                }


                if (!_damagedThisSwing.Add(
                        damageable))
                {
                    continue;
                }


                DamageInfo info =
                    new DamageInfo(
                        finalDamage,
                        damageType,
                        Owner.gameObject,
                        sourceKind
                    );


                damageable.TakeDamage(
                    in info
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


#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {
            Transform origin =
                hitOrigin != null
                    ? hitOrigin
                    : transform;


            Vector3 forward =
                Owner != null
                    ? Owner.transform.forward
                    : transform.forward;


            Vector3 center =
                origin.position +
                forward *
                forwardOffset;


            Gizmos.DrawWireSphere(
                center,
                hitRadius
            );
        }

#endif
    }
}