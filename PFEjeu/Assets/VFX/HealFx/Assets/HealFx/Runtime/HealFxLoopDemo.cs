using UnityEngine;

namespace HealFx
{
    /// <summary>
    /// Rôle unique : rejouer en continu le cycle apparition -> boucle -> fin -> pause courte -> recommence,
    /// pour régler le FX en direct dans la Training Room. Ne touche à aucun visuel : il ne fait qu'appeler Play() / Stop() sur la zone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealFxLoopDemo : MonoBehaviour
    {
        private enum Step
        {
            Waiting,
            Playing,
            Fading
        }

        [SerializeField] private HealZoneFx zone;

        [Header("Cycle")]
        [SerializeField] private bool loopEnabled = true;
        [Tooltip("Délai avant le tout premier lancement (secondes).")]
        [SerializeField, Min(0f)] private float startDelay = 0.4f;
        [Tooltip("Durée de la boucle stable, apparition comprise (secondes).")]
        [SerializeField, Min(0.5f)] private float playDuration = 7f;
        [Tooltip("Pause entre la fin de la dissipation et le cycle suivant (secondes).")]
        [SerializeField, Min(0f)] private float pauseDuration = 1.2f;

        private Step step = Step.Waiting;
        private float timer;

        public bool LoopEnabled => loopEnabled;
        public float PlayDuration => playDuration;
        public float PauseDuration => pauseDuration;

        /// <summary>Utilisé par l'installeur Editor pour câbler le prefab.</summary>
        public void Bind(HealZoneFx healZone)
        {
            zone = healZone;
        }

        #region Cycle de vie Unity
        private void OnEnable()
        {
            step = Step.Waiting;
            timer = startDelay;
        }

        private void Update()
        {
            if (zone == null) return;

            if (!loopEnabled)
            {
                // Boucle coupée : on laisse la zone dans son état courant (le panneau peut la piloter à la main).
                return;
            }

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            switch (step)
            {
                case Step.Waiting:
                    StartCycle();
                    break;
                case Step.Playing:
                    zone.Stop();
                    step = Step.Fading;
                    timer = zone.DisperseDuration + pauseDuration;
                    break;
                case Step.Fading:
                    StartCycle();
                    break;
            }
        }
        #endregion

        #region API publique
        /// <summary>Relance tout de suite un cycle complet (raccourci Espace).</summary>
        public void Restart()
        {
            if (zone == null) return;
            StartCycle();
        }

        public void SetLoopEnabled(bool enabledLoop)
        {
            loopEnabled = enabledLoop;
            if (loopEnabled) Restart();
        }

        public void SetPlayDuration(float seconds)
        {
            playDuration = Mathf.Max(0.5f, seconds);
        }

        public void SetPauseDuration(float seconds)
        {
            pauseDuration = Mathf.Max(0f, seconds);
        }
        #endregion

        private void StartCycle()
        {
            zone.Play();
            step = Step.Playing;
            timer = playDuration;
        }
    }
}
