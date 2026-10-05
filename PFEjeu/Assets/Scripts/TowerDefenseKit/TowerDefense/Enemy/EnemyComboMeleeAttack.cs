using UnityEngine;
using Core.HealthSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Ennemi à deux attaques :
    /// - petite attaque rapide
    /// - grosse attaque plus lente
    ///
    /// Une petite attaque peut enchaîner directement sur la grosse.
    /// Les chances sont configurables dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyComboMeleeAttack :
        EnemyAttackModule
    {
        private enum AttackPhase
        {
            Idle,
            Windup,
            Recovery
        }

        private enum AttackStep
        {
            Light,
            Heavy
        }


        [Header("Petite attaque")]

        [SerializeField, Min(0f)]
        private float lightBaseDamage = 3f;

        [SerializeField, Min(0f)]
        private float lightPhysicDamageScaling = 0.65f;

        [SerializeField, Min(0f)]
        private float lightWindup = 0.18f;

        [SerializeField, Min(0f)]
        private float lightRecovery = 0.22f;

        [SerializeField]
        private string lightTrigger = "AttackLight";


        [Header("Grosse attaque")]

        [SerializeField, Min(0f)]
        private float heavyBaseDamage = 10f;

        [SerializeField, Min(0f)]
        private float heavyPhysicDamageScaling = 1.5f;

        [SerializeField, Min(0f)]
        private float heavyWindup = 0.45f;

        [SerializeField, Min(0f)]
        private float heavyRecovery = 0.55f;

        [SerializeField]
        private string heavyTrigger = "AttackHeavy";


        [Header("Combo")]

        [Tooltip("Chance qu'une nouvelle séquence commence par la petite attaque.")]
        [SerializeField, Range(0f, 1f)]
        private float openingLightChance = 0.75f;

        [Tooltip("Après une petite attaque, chance d'enchaîner immédiatement sur la grosse.")]
        [SerializeField, Range(0f, 1f)]
        private float chainHeavyChance = 0.8f;

        [Tooltip("Marge de distance autorisée pour commencer la grosse attaque en combo.")]
        [SerializeField, Min(0f)]
        private float chainExtraRange = 0.35f;

        [Tooltip("Cooldown après la fin complète d'une séquence.")]
        [SerializeField, Min(0.05f)]
        private float comboCooldown = 0.8f;


        private AttackPhase _phase =
            AttackPhase.Idle;

        private AttackStep _step =
            AttackStep.Light;

        private float _impactTime;

        private float _phaseEndTime;

        private float _nextSequenceTime;


        public override void Tick(
            float deltaTime,
            Health target)
        {
            if (target == null)
                return;

            switch (_phase)
            {
                case AttackPhase.Idle:

                    if (Time.time >=
                        _nextSequenceTime)
                    {
                        StartOpeningAttack();
                    }

                    break;


                case AttackPhase.Windup:

                    if (Time.time >=
                        _impactTime)
                    {
                        ApplyCurrentImpact(
                            target
                        );

                        BeginRecovery();
                    }

                    break;


                case AttackPhase.Recovery:

                    if (Time.time >=
                        _phaseEndTime)
                    {
                        FinishRecovery(
                            target
                        );
                    }

                    break;
            }
        }


        private void StartOpeningAttack()
        {
            if (Random.value <=
                openingLightChance)
            {
                StartStep(
                    AttackStep.Light
                );
            }
            else
            {
                StartStep(
                    AttackStep.Heavy
                );
            }
        }


        private void StartStep(
            AttackStep step)
        {
            _step = step;

            _phase =
                AttackPhase.Windup;

            IsBusy = true;

            float windup =
                step == AttackStep.Light
                    ? lightWindup
                    : heavyWindup;

            _impactTime =
                Time.time +
                ScaleTime(
                    windup
                );

            TriggerAnimation(
                step == AttackStep.Light
                    ? lightTrigger
                    : heavyTrigger
            );
        }


        private void ApplyCurrentImpact(
            Health target)
        {
            if (_step ==
                AttackStep.Light)
            {
                DealPhysicalDamage(
                    target,
                    lightBaseDamage,
                    lightPhysicDamageScaling
                );
            }
            else
            {
                DealPhysicalDamage(
                    target,
                    heavyBaseDamage,
                    heavyPhysicDamageScaling
                );
            }
        }


        private void BeginRecovery()
        {
            _phase =
                AttackPhase.Recovery;

            float recovery =
                _step == AttackStep.Light
                    ? lightRecovery
                    : heavyRecovery;

            _phaseEndTime =
                Time.time +
                ScaleTime(
                    recovery
                );
        }


        private void FinishRecovery(
            Health target)
        {
            bool canChainHeavy =
                _step ==
                    AttackStep.Light &&
                Random.value <=
                    chainHeavyChance &&
                CanHit(
                    target,
                    chainExtraRange
                );

            if (canChainHeavy)
            {
                StartStep(
                    AttackStep.Heavy
                );

                return;
            }

            _phase =
                AttackPhase.Idle;

            IsBusy = false;

            _nextSequenceTime =
                Time.time +
                ScaleTime(
                    comboCooldown
                );
        }


        public override void Cancel()
        {
            _phase =
                AttackPhase.Idle;

            IsBusy = false;

            _nextSequenceTime =
                Mathf.Max(
                    _nextSequenceTime,
                    Time.time +
                    ScaleTime(
                        0.15f
                    )
                );
        }


        public override void ResetRuntime()
        {
            base.ResetRuntime();

            _phase =
                AttackPhase.Idle;

            _step =
                AttackStep.Light;

            _impactTime = 0f;

            _phaseEndTime = 0f;

            _nextSequenceTime = 0f;
        }
    }
}
