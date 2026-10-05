namespace Core.TowerDefense
{
    public sealed class EnemyAttackState :
        IEnemyState
    {
        private readonly EnemyController _owner;


        public string Name =>
            "AttackTarget";


        public EnemyAttackState(
            EnemyController owner)
        {
            _owner = owner;
        }


        public void Enter()
        {
            _owner.PathFollower.Stop();
        }


        public void Tick(
            float deltaTime)
        {
            if (!_owner.RefreshTarget(
                    deltaTime))
            {
                _owner.AttackModule.Cancel();

                _owner.ChangeToFollowPath();
                return;
            }

            if (!_owner.IsTargetInAttackRange())
            {
                _owner.AttackModule.Cancel();

                _owner.ChangeToChase();
                return;
            }

            _owner.FaceTarget();

            _owner.AttackModule.Tick(
                deltaTime,
                _owner.Target
            );
        }


        public void Exit()
        {
            _owner.AttackModule.Cancel();
        }
    }
}
