using System;

namespace Core.StateMachineSystem
{
    /// <summary>
    /// Une transition = un état cible + une condition à évaluer.
    /// Quand la condition renvoie true, la machine bascule vers l'état cible.
    /// </summary>
    public sealed class Transition
    {
        public IState To { get; }
        public Func<bool> Condition { get; }

        public Transition(IState to, Func<bool> condition)
        {
            To = to;
            Condition = condition;
        }
    }
}
