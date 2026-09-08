using UnityEngine;

namespace Core.StateMachineSystem.Examples
{
    /// <summary>
    /// Exemple complet : câble les états Patrouille/Poursuite et leurs transitions.
    /// Ajoute ce composant sur un GameObject et assigne une cible dans l'Inspector.
    /// </summary>
    public sealed class EnemyStateMachine : StateMachineRunner
    {
        [SerializeField] private Transform target;
        [SerializeField] private float chaseRange = 5f;
        [SerializeField] private float moveSpeed = 3f;

        protected override void Build(StateMachine machine)
        {
            var ctx = new EnemyContext(transform, target, chaseRange, moveSpeed);

            var patrol = new PatrolState(ctx);
            var chase = new ChaseState(ctx);

            // Patrouille → Poursuite quand la cible entre dans le rayon.
            machine.AddTransition(patrol, chase, () => ctx.DistanceToTarget() <= chaseRange);

            // Poursuite → Patrouille quand la cible sort du rayon.
            machine.AddTransition(chase, patrol, () => ctx.DistanceToTarget() > chaseRange);

            machine.SetState(patrol); // état initial
        }
    }
}
