using System;

namespace Core.WaveSystem
{
    /// <summary>
    /// Contrat que doit implémenter un ennemi spawné par une vague : il signale sa
    /// DÉFAITE (mort, sortie de l'écran...) via l'event Defeated. Le spawner s'y
    /// abonne pour savoir quand la vague est nettoyée — sans connaître ta classe.
    /// </summary>
    public interface IWaveEnemy
    {
        event Action<IWaveEnemy> Defeated;
    }
}
