namespace Core.StateMachineSystem
{
    /// <summary>
    /// Base optionnelle : implémente IState avec des méthodes virtuelles vides.
    /// Hérite-la pour ne surcharger QUE ce dont ton état a besoin.
    /// </summary>
    public abstract class StateBase : IState
    {
        public virtual void Enter() { }
        public virtual void Tick() { }
        public virtual void FixedTick() { }
        public virtual void Exit() { }
    }
}
