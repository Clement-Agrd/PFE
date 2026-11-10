using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// Rôle unique : rejouer en continu un soin sur une cible (aura + croix qui montent, impulsions régulières, fin douce, pause),
    /// pour régler le FX en direct dans la Training Room. Ne touche à aucun visuel : il appelle BeginHeal / Pulse / EndHeal sur <see cref="HealTargetFx"/>.
    /// En jeu, remplace-le par ton système de soin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealTargetLoopDemo : MonoBehaviour
    {
        private enum Step
        {
            Waiting,
            Healing,
            Resting
        }

        [SerializeField] private HealTargetFx target;

        [Header("Cycle")]
        [SerializeField] private bool loopEnabled = true;
        [SerializeField, Min(0f)] private float startDelay = 0.5f;
        [Tooltip("Durée du soin (aura visible), en secondes.")]
        [SerializeField, Min(0.5f)] private float healDuration = 5f;
        [Tooltip("Pause avant le soin suivant, en secondes (l'aura a le temps de se dissiper).")]
        [SerializeField, Min(0f)] private float restDuration = 2f;
        [Tooltip("Intervalle entre deux Pulse() pendant le soin (secondes).")]
        [SerializeField, Min(0.2f)] private float pulseInterval = 1.2f;

        private Step step = Step.Waiting;
        private float stepTimer;
        private float pulseTimer;

        /// <summary>Utilisé par l'installeur Editor / le câblage de scène.</summary>
        public void Bind(HealTargetFx healTarget)
        {
            target = healTarget;
        }

        #region Cycle de vie Unity
        private void OnEnable()
        {
            step = Step.Waiting;
            stepTimer = startDelay;
        }

        private void OnDisable()
        {
            if (target != null) target.EndHeal();
        }

        private void Update()
        {
            if (target == null || !loopEnabled) return;

            if (step == Step.Healing)
            {
                pulseTimer -= Time.deltaTime;
                if (pulseTimer <= 0f)
                {
                    pulseTimer = pulseInterval;
                    target.Pulse();
                }
            }

            stepTimer -= Time.deltaTime;
            if (stepTimer > 0f) return;

            switch (step)
            {
                case Step.Waiting:
                case Step.Resting:
                    BeginCycle();
                    break;
                case Step.Healing:
                    target.EndHeal();
                    step = Step.Resting;
                    stepTimer = restDuration;
                    break;
            }
        }
        #endregion

        /// <summary>Relance tout de suite un soin complet.</summary>
        public void Restart()
        {
            if (target != null) BeginCycle();
        }

        private void BeginCycle()
        {
            target.BeginHeal();
            target.Pulse();
            step = Step.Healing;
            stepTimer = healDuration;
            pulseTimer = pulseInterval;
        }
    }
}
