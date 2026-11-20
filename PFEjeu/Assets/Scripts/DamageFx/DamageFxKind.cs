namespace DamageFx
{
    /// <summary>
    /// Famille visuelle d'un chiffre de dégâts. (Distinct de Core.HealthSystem.DamageType, un ScriptableObject
    /// qui ne connaît que Physique/Magique : voir DamageFxKindMap pour le passage de l'un à l'autre.)
    /// L'ordre est utilisé comme index de tableau : n'insère pas de valeur au milieu.
    /// </summary>
    public enum DamageFxKind
    {
        Physical = 0,
        Fire = 1,
        Ice = 2,
        Poison = 3,
        Magic = 4,
        Heal = 5,
        TowerHit = 6     // dégâts reçus par MA tour / mes murs
    }

    /// <summary>Constantes liées à l'enum (nombre de valeurs, pour dimensionner les tableaux).</summary>
    public static class DamageFxKinds
    {
        public const int Count = 7;
    }

    /// <summary>Rôle de la cible, pour la priorité d'affichage (posé via DamageFxTag).</summary>
    public enum DamageFxRole
    {
        Enemy = 0,
        Tower = 1,
        Wall = 2,
        Boss = 3
    }
}
