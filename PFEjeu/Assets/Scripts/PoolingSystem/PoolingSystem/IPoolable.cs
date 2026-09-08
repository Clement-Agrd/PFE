namespace Core.PoolingSystem
{
    /// <summary>
    /// À implémenter par tout composant qui a besoin de réagir au recyclage.
    /// OnSpawn : l'objet sort du pool (réinitialise ton état ici).
    /// OnDespawn : l'objet retourne au pool (nettoie/arrête ce qu'il faut).
    /// Peut être présent sur l'objet racine ou sur ses enfants.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
