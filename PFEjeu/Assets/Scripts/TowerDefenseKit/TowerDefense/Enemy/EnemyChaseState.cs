using UnityEngine;

namespace Core.TowerDefense
{
    public sealed class EnemyChaseState :
        IEnemyState
    {
        private readonly EnemyController _owner;


        public string Name =>
            "ChaseTarget";


        public EnemyChaseState(
            EnemyController owner)
        {
            _owner = owner;
        }


        public void Enter()
        {
            if (_owner.Agent != null &&
                _owner.Agent.isOnNavMesh)
            {
                _owner.Agent.isStopped =
                    false;

                _owner.Agent.stoppingDistance =
                    Mathf.Max(
                        0.05f,
                        _owner.AttackModule.AttackRange *
                        0.8f
                    );
            }
        }


        public void Tick(
            float deltaTime)
        {
            if (!_owner.RefreshTarget(
                    deltaTime))
            {
                _owner.ChangeToFollowPath();
                return;
            }

            if (_owner.IsTargetInAttackRange())
            {
                _owner.ChangeToAttack();
                return;
            }

            _owner.PathFollower
                .SetDestinationSafe(
                    _owner.Target
                        .transform
                        .position
                );
        }


        public void Exit()
        {
        }
    }
}
