namespace Core.HealthSystem
{
    /// <summary>
    /// Tout ce qui peut subir des dégâts. Implémenté par Health, mais tu peux
    /// l'implémenter ailleurs (objets destructibles, boucliers...) pour que les
    /// systèmes offensifs (Abilities, Status) frappent sans connaître la classe.
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
        bool IsDead { get; }
    }

    /// <summary>Tout ce qui peut être soigné.</summary>
    public interface IHealable
    {
        void Heal(int amount);
    }
}
