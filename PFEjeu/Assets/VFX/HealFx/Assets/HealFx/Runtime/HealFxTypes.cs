namespace HealFx
{
    /// <summary>Niveau de qualité du FX : nombre de particules et couches du shader de sol (runes).</summary>
    public enum HealFxQuality
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>Phase du cycle de vie d'une zone : apparition (pop), boucle stable (respiration), fin (dissipation), au repos.</summary>
    public enum HealZonePhase
    {
        Idle = 0,
        Appear = 1,
        Loop = 2,
        Disperse = 3
    }
}
