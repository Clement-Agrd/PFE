using UnityEngine;
using Core.HealthSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Attaque classique : un coup, un windup et un cooldown.
    /// Reproduit le comportement de l'ancien NavEnemy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyMeleeAttack :
        EnemyAttackModule
    {
        [Header("Attaque classique")]

        [SerializeField, Min(0f)]
        private float baseDamage = 5f;

        [SerializeField, Min(0f)]
        private float physicDamageScaling = 1f;

        [SerializeField, Min(0.05f)]
        private float attackInterval = 1.25f;

        [SerializeField, Min(0f)]
        private float attackWindup = 0.35f;

        [SerializeField]
        private string attackTrigger = "Attack";


        private float _nextAttackTime;

        private float _impactTime;

        private bool _impactPending;


        public override void Tick(
            float deltaTime,
            Health target)
        {
            if (target == null)
                return;

            if (_impactPending &&
                Time.time >=
                _impactTime)
            {
                _impactPending = false;

                IsBusy = false;

                DealPhysicalDamage(
                    target,
                    baseDamage,
                    physicDamageScaling
                );
            }

            if (_impactPending)
                return;

            if (Time.time <
                _nextAttackTime)
            {
                return;
            }

            StartAttack();
        }


        private void StartAttack()
        {
            float interval =
                Mathf.Max(
                    0.05f,
                    ScaleTime(
                        attackInterval
                    )
                );

            float windup =
                Mathf.Clamp(
                    ScaleTime(
                        attackWindup
                    ),
                    0f,
                    interval
                );

            _nextAttackTime =
                Time.time +
                interval;

            _impactTime =
                Time.time +
                windup;

            _impactPending =
                true;

            IsBusy = true;

            TriggerAnimation(
                attackTrigger
            );
        }


        public override void Cancel()
        {
            _impactPending = false;

            IsBusy = false;
        }


        public override void ResetRuntime()
        {
            base.ResetRuntime();

            _nextAttackTime = 0f;

            _impactTime = 0f;

            _impactPending = false;
        }
    }
}
