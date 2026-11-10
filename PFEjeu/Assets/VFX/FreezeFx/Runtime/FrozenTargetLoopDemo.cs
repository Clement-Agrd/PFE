using UnityEngine;

namespace FreezeFx
{
    /// <summary>
    /// Rôle unique : rejouer en continu un état "glacé" sur une cible (gel, immobilité, dégel, pause, recommence)
    /// pour régler le FX en direct dans la Training Room. Ne touche à aucun visuel : il appelle BeginFreeze / EndFreeze sur <see cref="FrozenTargetFx"/>.
    /// En jeu, remplace-le par ton système d'états (BeginFreeze(SOEtat) à l'application de l'état).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrozenTargetLoopDemo : MonoBehaviour
    {
        private enum Step
        {
            Waiting,
            Frozen,
            Resting
        }

        [SerializeField] private FrozenTargetFx target;

        [Header("Cycle")]
        [SerializeField] private bool loopEnabled = true;
        [SerializeField, Min(0f)] private float startDelay = 1.2f;
        [Tooltip("Durée de l'état, en secondes.")]
        [SerializeField, Min(0.5f)] private float freezeDuration = 6f;
        [Tooltip("Pause avant l'état suivant, en secondes (la cible a le temps de dégeler).")]
        [SerializeField, Min(0f)] private float restDuration = 3.5f;

        private Step step = Step.Waiting;
        private float timer;

        /// <summary>Utilisé par l'installeur Editor / le câblage de scène.</summary>
        public void Bind(FrozenTargetFx frozenTarget)
        {
            target = frozenTarget;
        }

        #region Cycle de vie Unity
        private void OnEnable()
        {
            step = Step.Waiting;
            timer = startDelay;
        }

        private void OnDisable()
        {
            if (target != null) target.EndFreeze();
        }

        private void Update()
        {
            if (target == null || !loopEnabled) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            switch (step)
            {
                case Step.Waiting:
                case Step.Resting:
                    target.BeginFreeze();
                    step = Step.Frozen;
                    timer = freezeDuration;
                    break;
                case Step.Frozen:
                    target.EndFreeze();
                    step = Step.Resting;
                    timer = restDuration;
                    break;
            }
        }
        #endregion

        /// <summary>Relance tout de suite un cycle complet.</summary>
        public void Restart()
        {
            if (target == null) return;
            target.BeginFreeze();
            step = Step.Frozen;
            timer = freezeDuration;
        }
    }
}
