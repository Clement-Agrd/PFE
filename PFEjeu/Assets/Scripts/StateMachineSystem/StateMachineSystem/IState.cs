namespace Core.StateMachineSystem
{
    /// <summary>
    /// Contrat d'un état. Chaque état encapsule sa propre logique.
    /// La machine appelle Enter à l'entrée, Tick/FixedTick pendant, Exit à la sortie.
    /// </summary>
    public interface IState
    {
        /// <summary>Appelé une fois quand la machine entre dans cet état.</summary>
        void Enter();

        /// <summary>Appelé chaque frame (depuis Update) tant que l'état est actif.</summary>
        void Tick();

        /// <summary>Appelé chaque pas physique (depuis FixedUpdate) tant que l'état est actif.</summary>
        void FixedTick();

        /// <summary>Appelé une fois quand la machine quitte cet état.</summary>
        void Exit();
    }
}
