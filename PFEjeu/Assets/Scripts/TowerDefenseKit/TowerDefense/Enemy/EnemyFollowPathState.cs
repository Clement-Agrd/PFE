namespace Core.TowerDefense
{
    public sealed class EnemyFollowPathState :
        IEnemyState
    {
        private readonly EnemyController _owner;


        public string Name =>
            "FollowPath";


        public EnemyFollowPathState(
            EnemyController owner)
        {
            _owner = owner;
        }


        public void Enter()
        {
            _owner.PathFollower
                .ResumeFromClosestPoint();
        }


        public void Tick(
            float deltaTime)
        {
            if (_owner.RefreshTarget(
                    deltaTime))
            {
                if (_owner.IsTargetInAttackRange())
                {
                    _owner.ChangeToAttack();
                }
                else
                {
                    _owner.ChangeToChase();
                }

                return;
            }

            _owner.PathFollower.Tick(
                deltaTime
            );

            if (_owner.PathFollower
                .ReachedEnd)
            {
                _owner.FinishPath();
            }
        }


        public void Exit()
        {
        }
    }
}
