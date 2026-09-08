using System;
using UnityEngine;

namespace Core.HealthSystem
{
    /// <summary>
    /// Résistance à un type de dégâts : un multiplicateur appliqué aux dégâts de
    /// ce type. 1 = normal, 0.5 = -50 %, 2 = double (faiblesse), 0 = immunisé.
    /// </summary>
    [Serializable]
    public struct Resistance
    {
        public DamageType type;

        [Tooltip("Multiplicateur : 1 = normal, 0.5 = -50 %, 2 = ×2 (faiblesse), 0 = immunisé.")]
        [Min(0f)] public float multiplier;
    }
}
