namespace Core.TowerDefense
{
    public sealed class EnemyFinishedState :
        IEnemyState
    {
        private readonly EnemyController _owner;

        public string Name =>
            "Finished";

        public EnemyFinishedState(
            EnemyController owner)
        {
            _owner = owner;
        }

        public void Enter()
        {
            _owner.PathFollower.Stop();

            if (_owner.AttackModule != null)
            {
                _owner.AttackModule.Cancel();
            }
        }

        public void Tick(
            float deltaTime)
        {
        }

        public void Exit()
        {
        }
    }
}
