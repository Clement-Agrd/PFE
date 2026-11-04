using System;
using UnityEngine;

namespace AllyFx
{
    /// <summary>
    /// Façade des FX d'attaque de l'allié. Point d'entrée unique appelé par l'Animator
    /// (voir <see cref="AttackFxStateBehaviour"/>) et relais vers les modules de FX.
    ///
    /// Allié (racine)
    /// │
    /// ├── Animator ── AttackFxStateBehaviour (sur l'état d'attaque)
    /// ├── AllyAttackFx            (ce script, implémente IAttackFx)
    /// └── ...Squelette/Main/Epée
    ///     ├── SwordGlowFx         (lueur de télégraphie)
    ///     └── SlashRibbonTrail    (ruban de slash base → pointe)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AllyAttackFx : MonoBehaviour, IAttackFx
    {
        #region Inspector
        [Header("Modules (optionnels : un module vide est simplement ignoré)")]
        [SerializeField] private SwordGlowFx swordGlow;
        [SerializeField] private SlashRibbonTrail slashTrail;
        #endregion

        #region Évènements (audio, screen shake, hit-stop... à brancher plus tard)
        public event Action SlashStarted;
        public event Action SlashEnded;
        #endregion

        #region Cycle Unity
        // Auto-remplissage quand on ajoute le composant dans l'Inspector.
        private void Reset()
        {
            swordGlow = GetComponentInChildren<SwordGlowFx>();
            slashTrail = GetComponentInChildren<SlashRibbonTrail>();
        }

        // Si l'allié est désactivé / recyclé en plein coup, on nettoie tout.
        private void OnDisable()
        {
            CancelAll();
        }
        #endregion

        #region IAttackFx
        public void BeginWindup()
        {
            if (swordGlow != null) swordGlow.BeginWindup();
        }

        public void SetWindupProgress(float progress01)
        {
            if (swordGlow != null) swordGlow.SetWindupProgress(progress01);
        }

        public void EndWindup()
        {
            if (swordGlow != null) swordGlow.EndWindup();
        }

        public void BeginSlash()
        {
            if (slashTrail != null) slashTrail.SetEmitting(true);
            SlashStarted?.Invoke();
        }

        public void EndSlash()
        {
            if (slashTrail != null) slashTrail.SetEmitting(false);
            SlashEnded?.Invoke();
        }
        #endregion

        /// <summary>Coupe immédiatement tous les FX (mort, pooling, changement d'état brutal).</summary>
        public void CancelAll()
        {
            if (swordGlow != null) swordGlow.Cancel();
            if (slashTrail != null) slashTrail.Cancel();
        }
    }
}
