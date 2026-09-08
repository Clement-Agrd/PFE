using UnityEngine;

namespace Core.StateMachineSystem.Examples
{
    /// <summary>État de poursuite : se déplace vers la cible.</summary>
    public sealed class ChaseState : StateBase
    {
        private readonly EnemyContext _ctx;

        public ChaseState(EnemyContext ctx) => _ctx = ctx;

        public override void Enter()
            => Debug.Log("[Enemy] Entre en Poursuite");

        public override void Tick()
        {
            if (_ctx.Target == null) return;

            _ctx.Self.position = Vector3.MoveTowards(
                _ctx.Self.position,
                _ctx.Target.position,
                _ctx.MoveSpeed * Time.deltaTime);
        }

        public override void Exit()
            => Debug.Log("[Enemy] Quitte la Poursuite");
    }
}
