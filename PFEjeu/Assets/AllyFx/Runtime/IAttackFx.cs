namespace AllyFx
{
    /// <summary>
    /// Contrat entre la logique d'attaque (Animator / state script) et les effets visuels.
    /// Permet de changer l'implémentation des FX sans toucher au code d'animation.
    /// </summary>
    public interface IAttackFx
    {
        /// <summary>Début de la télégraphie (anticipation avant le coup).</summary>
        void BeginWindup();

        /// <param name="progress01">Progression de la télégraphie, de 0 à 1.</param>
        void SetWindupProgress(float progress01);

        /// <summary>Fin de la télégraphie (déclenche le flash de relâchement).</summary>
        void EndWindup();

        /// <summary>Début de la fenêtre où la lame est "active" (trail visible).</summary>
        void BeginSlash();

        /// <summary>Fin du slash : le ruban cesse d'être émis et s'estompe.</summary>
        void EndSlash();
    }
}
