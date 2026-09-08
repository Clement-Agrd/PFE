using UnityEngine;

namespace Core.StateMachineSystem.Examples
{
    /// <summary>
    /// Données partagées entre les états de l'ennemi (pattern composition).
    /// Les états reçoivent ce contexte au constructeur → pas d'héritage, pas de singleton.
    /// </summary>
    public sealed class EnemyContext
    {
        public readonly Transform Self;
        public readonly Transform Target;
        public readonly float ChaseRange;
        public readonly float MoveSpeed;

        public EnemyContext(Transform self, Transform target, float chaseRange, float moveSpeed)
        {
            Self = self;
            Target = target;
            ChaseRange = chaseRange;
            MoveSpeed = moveSpeed;
        }

        /// <summary>Distance à la cible (infinie si pas de cible → jamais de poursuite).</summary>
        public float DistanceToTarget()
            => Target == null ? Mathf.Infinity : Vector3.Distance(Self.position, Target.position);
    }
}
