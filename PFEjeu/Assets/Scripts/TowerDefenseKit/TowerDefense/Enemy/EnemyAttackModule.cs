using UnityEngine;
using Core.HealthSystem;
using Core.StatsSystem;

namespace Core.TowerDefense
{
    using StatType =
        EnumStats.StatTypes;

    /// <summary>
    /// Base commune à toutes les attaques ennemies.
    /// Le State Attack ne connaît pas le type d'attaque utilisé.
    /// </summary>
    public abstract class EnemyAttackModule :
        MonoBehaviour
    {
        [Header("Portée")]

        [SerializeField, Min(0.1f)]
        private float attackRange = 1.7f;

        [SerializeField, Min(0f)]
        private float attackRangeLeeway = 0.35f;


        [Header("Dégâts")]

        [Tooltip("Assigner normalement le DamageType Physical pour les attaques de mêlée.")]
        [SerializeField]
        private DamageType damageType;


        protected EnemyController Owner { get; private set; }


        public float AttackRange =>
            attackRange;

        public float AttackRangeLeeway =>
            attackRangeLeeway;

        public bool IsBusy { get; protected set; }


        public virtual void Initialize(
            EnemyController owner)
        {
            Owner = owner;

            ResetRuntime();
        }


        public abstract void Tick(
            float deltaTime,
            Health target);


        public virtual void Cancel()
        {
            IsBusy = false;
        }


        public virtual void ResetRuntime()
        {
            IsBusy = false;
        }


        protected bool CanHit(
            Health target,
            float extraRange = 0f)
        {
            if (Owner == null ||
                target == null ||
                target.IsDead)
            {
                return false;
            }

            float distance =
                Owner.GetPlanarDistanceTo(
                    target.transform.position
                );

            return distance <=
                attackRange +
                attackRangeLeeway +
                extraRange;
        }


        protected int CalculatePhysicalDamage(
            float baseDamage,
            float physicDamageScaling)
        {
            float physicDamage = 0f;

            if (Owner != null &&
                Owner.Stats != null &&
                Owner.Stats.HasStat(
                    StatType.PhysicDamage))
            {
                physicDamage =
                    Owner.Stats.GetStat(
                        StatType.PhysicDamage
                    );
            }

            float damage =
                baseDamage +
                physicDamage *
                physicDamageScaling;

            return Mathf.Max(
                1,
                Mathf.RoundToInt(
                    damage
                )
            );
        }


        protected void DealPhysicalDamage(
            Health target,
            float baseDamage,
            float physicDamageScaling)
        {
            if (!CanHit(target))
                return;

            int finalDamage =
                CalculatePhysicalDamage(
                    baseDamage,
                    physicDamageScaling
                );

            DamageInfo info =
                new DamageInfo(
                    finalDamage,
                    damageType,
                    Owner != null
                        ? Owner.gameObject
                        : gameObject,
                    DamageSourceKind.None
                );

            target.TakeDamage(
                in info
            );
        }


        protected float ScaleTime(
            float normalDuration)
        {
            float attackSpeed =
                Owner != null
                    ? Owner.AttackSpeed
                    : 1f;

            return normalDuration /
                   Mathf.Max(
                       0.1f,
                       attackSpeed
                   );
        }


        protected void TriggerAnimation(
            string triggerName)
        {
            if (Owner == null ||
                Owner.Animator == null ||
                string.IsNullOrWhiteSpace(
                    triggerName))
            {
                return;
            }

            Owner.Animator.SetTrigger(
                triggerName
            );
        }
    }
}
