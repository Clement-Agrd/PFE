using UnityEngine;

namespace Core.StateMachineSystem.Examples
{
    /// <summary>État de patrouille : va-et-vient simple sur l'axe X (placeholder de démo).</summary>
    public sealed class PatrolState : StateBase
    {
        private readonly EnemyContext _ctx;

        public PatrolState(EnemyContext ctx) => _ctx = ctx;

        public override void Enter()
            => Debug.Log("[Enemy] Entre en Patrouille");

        public override void Tick()
        {
            // Placeholder : oscillation gauche/droite. Remplace par ta vraie logique
            // de patrouille (waypoints, NavMesh, etc.).
            Vector3 p = _ctx.Self.position;
            float x = Mathf.PingPong(Time.time * _ctx.MoveSpeed, 4f) - 2f;
            _ctx.Self.position = new Vector3(x, p.y, p.z);
        }

        public override void Exit()
            => Debug.Log("[Enemy] Quitte la Patrouille");
    }
}
