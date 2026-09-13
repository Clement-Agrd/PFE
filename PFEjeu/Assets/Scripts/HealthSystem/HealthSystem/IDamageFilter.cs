namespace Core.HealthSystem
{
    /// <summary>
    /// Filtre optionnel placé à côté d'un Health.
    ///
    /// Permet par exemple :
    /// - arbre uniquement sensible à la hache
    /// - rocher uniquement sensible à la pioche
    /// - bouclier immunisé à certains coups
    /// - boss protégé pendant une phase
    /// </summary>
    public interface IDamageFilter
    {
        bool CanTakeDamage(
            in DamageInfo info
        );
    }
}