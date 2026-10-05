namespace Core.TowerDefense
{
    /// <summary>
    /// Petite machine à états réutilisable par tous les ennemis.
    /// </summary>
    public sealed class EnemyStateMachine
    {
        public IEnemyState CurrentState { get; private set; }

        public string CurrentStateName =>
            CurrentState != null
                ? CurrentState.Name
                : "None";

        public void ChangeState(
            IEnemyState nextState,
            bool force = false)
        {
            if (nextState == null)
                return;

            if (!force &&
                ReferenceEquals(
                    CurrentState,
                    nextState))
            {
                return;
            }

            CurrentState?.Exit();

            CurrentState =
                nextState;

            CurrentState.Enter();
        }

        public void Tick(
            float deltaTime)
        {
            CurrentState?.Tick(
                deltaTime
            );
        }

        public void Reset()
        {
            CurrentState?.Exit();

            CurrentState = null;
        }
    }
}
