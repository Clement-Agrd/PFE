namespace Core.TowerDefense
{
    /// <summary>
    /// Etat logique d'un ennemi modulaire.
    /// Les states restent de simples classes C# :
    /// aucun composant supplémentaire dans l'Inspector.
    /// </summary>
    public interface IEnemyState
    {
        string Name { get; }

        void Enter();

        void Tick(float deltaTime);

        void Exit();
    }
}
