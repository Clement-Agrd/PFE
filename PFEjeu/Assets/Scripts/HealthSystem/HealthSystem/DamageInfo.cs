using UnityEngine;

namespace Core.HealthSystem
{
    /// <summary>
    /// Décrit un coup : combien, de quel type, et de qui. Passé à TakeDamage.
    /// 'readonly struct' → immuable, sans allocation.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        public readonly DamageType Type;   // null = dégâts « bruts » (multiplicateur 1)
        public readonly GameObject Source; // qui a infligé le coup (pour l'XP, l'aggro...)

        public DamageInfo(int amount, DamageType type = null, GameObject source = null)
        {
            Amount = amount;
            Type = type;
            Source = source;
        }
    }
}
