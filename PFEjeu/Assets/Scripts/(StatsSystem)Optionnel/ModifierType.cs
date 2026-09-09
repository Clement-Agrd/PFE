namespace Core.StatsSystem
{
    /// <summary>
    /// Type d'un modificateur. La valeur numérique encode aussi l'ORDRE de calcul :
    /// les Flat s'appliquent d'abord, puis les PercentAdd, puis les PercentMult.
    /// </summary>
    public enum ModifierType
    {
        /// <summary>Additif brut : +5 (ex. épée +5 force).</summary>
        Flat = 100,

        /// <summary>Pourcentage additionné aux autres PercentAdd : deux +10% = +20%.</summary>
        PercentAdd = 200,

        /// <summary>Pourcentage multiplicatif séparé : ×(1 + valeur), appliqué à part.</summary>
        PercentMult = 300
    }
}
