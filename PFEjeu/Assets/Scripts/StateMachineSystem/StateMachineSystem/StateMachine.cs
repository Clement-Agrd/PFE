using System;
using System.Collections.Generic;

namespace Core.StateMachineSystem
{
    /// <summary>
    /// Moteur de la machine à états. Gère l'état courant, évalue les transitions
    /// et déclenche les changements d'état. Indépendant de Unity (testable seul).
    /// </summary>
    public sealed class StateMachine
    {
        public IState CurrentState { get; private set; }

        // Transitions spécifiques à un état donné : depuis quel état → quelles sorties possibles.
        private readonly Dictionary<IState, List<Transition>> _transitions = new();

        // Transitions valables depuis N'IMPORTE quel état (ex. → Mort si PV <= 0).
        private readonly List<Transition> _anyTransitions = new();

        // Cache des transitions de l'état courant, pour éviter un lookup par frame.
        private static readonly List<Transition> EmptyTransitions = new(0);
        private List<Transition> _currentTransitions = EmptyTransitions;

        /// <summary>Émis à chaque changement d'état (utile pour l'UI, les logs, l'audio...).</summary>
        public event Action<IState> OnStateChanged;

        /// <summary>À appeler depuis Update : évalue les transitions puis fait vivre l'état.</summary>
        public void Tick()
        {
            Transition transition = GetTransition();
            if (transition != null)
                SetState(transition.To);

            CurrentState?.Tick();
        }

        /// <summary>À appeler depuis FixedUpdate pour la logique physique de l'état.</summary>
        public void FixedTick() => CurrentState?.FixedTick();

        /// <summary>Force le passage à un état (Exit de l'ancien, Enter du nouveau).</summary>
        public void SetState(IState state)
        {
            if (state == CurrentState) return;

            CurrentState?.Exit();
            CurrentState = state;

            // Recharge le cache des transitions du nouvel état.
            _transitions.TryGetValue(CurrentState, out _currentTransitions);
            _currentTransitions ??= EmptyTransitions;

            CurrentState.Enter();
            OnStateChanged?.Invoke(CurrentState);
        }

        /// <summary>Déclare une transition : depuis <paramref name="from"/> vers <paramref name="to"/> si la condition est vraie.</summary>
        public void AddTransition(IState from, IState to, Func<bool> condition)
        {
            if (!_transitions.TryGetValue(from, out List<Transition> list))
            {
                list = new List<Transition>();
                _transitions[from] = list;
            }
            list.Add(new Transition(to, condition));
        }

        /// <summary>Déclare une transition valable depuis n'importe quel état.</summary>
        public void AddAnyTransition(IState to, Func<bool> condition)
            => _anyTransitions.Add(new Transition(to, condition));

        // Les transitions "any" sont prioritaires sur celles de l'état courant.
        private Transition GetTransition()
        {
            for (int i = 0; i < _anyTransitions.Count; i++)
                if (_anyTransitions[i].Condition())
                    return _anyTransitions[i];

            for (int i = 0; i < _currentTransitions.Count; i++)
                if (_currentTransitions[i].Condition())
                    return _currentTransitions[i];

            return null;
        }
    }
}
