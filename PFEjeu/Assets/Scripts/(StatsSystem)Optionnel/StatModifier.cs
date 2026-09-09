namespace Core.StatsSystem
{
    /// <summary>
    /// Un modificateur de stat : une valeur, un type, un ordre, et une SOURCE.
    /// La source (item, buff, aura...) permet de retirer d'un coup tous les
    /// modificateurs qu'elle a posés (déséquipement, fin de buff).
    /// </summary>
    public sealed class StatModifier
    {
        public readonly float Value;
        public readonly ModifierType Type;
        public readonly int Order;
        public readonly object Source;

        public StatModifier(float value, ModifierType type, int order, object source)
        {
            Value = value;
            Type = type;
            Order = order;
            Source = source;
        }

        /// <summary>Ordre par défaut = valeur du type (Flat avant PercentAdd avant PercentMult).</summary>
        public StatModifier(float value, ModifierType type, object source = null)
            : this(value, type, (int)type, source) { }
    }
}
