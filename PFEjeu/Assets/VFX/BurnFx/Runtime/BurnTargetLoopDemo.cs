using UnityEngine;

namespace BurnFx
{
    /// <summary>
    /// Rôle unique : rejouer en continu un état "brûle" sur une cible (allumage, combustion, extinction douce, pause, recommence)
    /// pour régler le FX en direct dans la Training Room. Ne touche à aucun visuel : il appelle BeginBurn / EndBurn sur <see cref="BurnTargetFx"/>.
    /// En jeu, remplace-le par ton système d'états (BeginBurn(SOEtat) à l'application de l'état).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BurnTargetLoopDemo : MonoBehaviour
    {
        private enum Step
        {
            Waiting,
            Burning,
            Resting
        }

        [SerializeField] private BurnTargetFx target;

        [Header("Cycle")]
        [SerializeField] private bool loopEnabled = true;
        [SerializeField, Min(0f)] private float startDelay = 0.8f;
        [Tooltip("Durée de l'état, en secondes.")]
        [SerializeField, Min(0.5f)] private float burnDuration = 6f;
        [Tooltip("Pause avant l'état suivant, en secondes (la cible a le temps de s'éteindre).")]
        [SerializeField, Min(0f)] private float restDuration = 3f;

        private Step step = Step.Waiting;
        private float timer;

        /// <summary>Utilisé par l'installeur Editor / le câblage de scène.</summary>
        public void Bind(BurnTargetFx burnTarget)
        {
            target = burnTarget;
        }

        #region Cycle de vie Unity
        private void OnEnable()
        {
            step = Step.Waiting;
            timer = startDelay;
        }

        private void OnDisable()
        {
            if (target != null) target.EndBurn();
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
                    target.BeginBurn();
                    step = Step.Burning;
                    timer = burnDuration;
                    break;
                case Step.Burning:
                    target.EndBurn();
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
            target.BeginBurn();
            step = Step.Burning;
            timer = burnDuration;
        }
    }
}
