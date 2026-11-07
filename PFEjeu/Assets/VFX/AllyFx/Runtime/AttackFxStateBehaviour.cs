using UnityEngine;

namespace AllyFx
{
    /// <summary>
    /// À ajouter directement sur l'état d'attaque dans l'Animator Controller.
    /// Ouvre / ferme les fenêtres de FX selon le temps normalisé de l'état,
    /// puis délègue à un <see cref="IAttackFx"/> trouvé sur l'allié.
    /// Aucune allocation par frame.
    /// </summary>
    public sealed class AttackFxStateBehaviour : StateMachineBehaviour
    {
        #region Inspector
        [Header("Télégraphie (temps normalisé de l'état, 0 → 1)")]
        [SerializeField, Range(0f, 1f)] private float windupStart = 0f;
        [SerializeField, Range(0f, 1f)] private float windupEnd = 0.32f;

        [Header("Slash (temps normalisé de l'état, 0 → 1)")]
        [SerializeField, Range(0f, 1f)] private float slashStart = 0.33f;
        [SerializeField, Range(0f, 1f)] private float slashEnd = 0.64f;
        #endregion

        private IAttackFx attackFx;
        private bool isWindupActive;
        private bool isSlashActive;

        #region Cycle Animator
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Une seule recherche par attaque (pas par frame). Cherche sur l'Animator et ses parents.
            attackFx = animator.GetComponentInParent<IAttackFx>();
            isWindupActive = false;
            isSlashActive = false;

            if (attackFx == null)
            {
                Debug.LogWarning($"[{nameof(AttackFxStateBehaviour)}] Aucun IAttackFx (AllyAttackFx) trouvé sur '{animator.name}' ou ses parents.", animator);
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (attackFx == null) return;

            float cycleTime = stateInfo.loop
                ? Mathf.Repeat(stateInfo.normalizedTime, 1f)
                : Mathf.Clamp01(stateInfo.normalizedTime);

            UpdateWindup(cycleTime);
            UpdateSlash(cycleTime);
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Garantit qu'aucune fenêtre ne reste ouverte si l'attaque est interrompue.
            CloseAllWindows();
            attackFx = null;
        }
        #endregion

        #region Fenêtres
        private void UpdateWindup(float cycleTime)
        {
            bool shouldBeActive = cycleTime >= windupStart && cycleTime < windupEnd;

            if (shouldBeActive)
            {
                if (!isWindupActive)
                {
                    isWindupActive = true;
                    attackFx.BeginWindup();
                }
                attackFx.SetWindupProgress(Mathf.InverseLerp(windupStart, windupEnd, cycleTime));
            }
            else if (isWindupActive)
            {
                isWindupActive = false;
                attackFx.EndWindup();
            }
        }

        private void UpdateSlash(float cycleTime)
        {
            bool shouldBeActive = cycleTime >= slashStart && cycleTime < slashEnd;

            if (shouldBeActive && !isSlashActive)
            {
                isSlashActive = true;
                attackFx.BeginSlash();
            }
            else if (!shouldBeActive && isSlashActive)
            {
                isSlashActive = false;
                attackFx.EndSlash();
            }
        }

        private void CloseAllWindows()
        {
            if (attackFx == null) return;

            if (isWindupActive)
            {
                isWindupActive = false;
                attackFx.EndWindup();
            }

            if (isSlashActive)
            {
                isSlashActive = false;
                attackFx.EndSlash();
            }
        }
        #endregion

#if UNITY_EDITOR
        // Garde des fenêtres cohérentes (fin >= début) quand on édite dans l'Inspector.
        private void OnValidate()
        {
            windupEnd = Mathf.Max(windupStart, windupEnd);
            slashEnd = Mathf.Max(slashStart, slashEnd);
        }
#endif
    }
}
