using UnityEngine;

namespace Core.StateMachineSystem
{
    /// <summary>
    /// Pont entre Unity et la StateMachine : pilote la machine via le cycle de vie
    /// (Update / FixedUpdate). Hérite cette classe et construis tes états dans Build().
    /// </summary>
    public abstract class StateMachineRunner : MonoBehaviour
    {
        protected StateMachine Machine { get; private set; }

        protected virtual void Awake()
        {
            Machine = new StateMachine();
            Build(Machine);
        }

        /// <summary>
        /// Crée ici les états et les transitions, puis fixe l'état initial
        /// avec Machine.SetState(...).
        /// </summary>
        protected abstract void Build(StateMachine machine);

        protected virtual void Update() => Machine.Tick();

        protected virtual void FixedUpdate() => Machine.FixedTick();
    }
}
