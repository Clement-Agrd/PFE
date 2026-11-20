using System;
using Core.HealthSystem;
using UnityEngine;

namespace DamageFx
{
    /// <summary>
    /// Fait le lien entre les assets DamageType existants du projet (Physique / Magique...) et les familles visuelles DamageFxKind.
    /// Aucun asset DamageType n'est modifié. Un DamageType absent de la liste retombe sur sa catégorie (Physique -> Physical, Magique -> Magic).
    /// </summary>
    [CreateAssetMenu(fileName = "DamageFxKindMap", menuName = "Damage FX/Kind Map")]
    public sealed class DamageFxKindMap : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public DamageType damageType;
            public DamageFxKind kind;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>Famille visuelle d'un DamageType (null = Physical).</summary>
        public DamageFxKind Resolve(DamageType type)
        {
            if (type == null) return DamageFxKind.Physical;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].damageType == type) return entries[i].kind;
            return type.Category == DamageCategory.Magical ? DamageFxKind.Magic : DamageFxKind.Physical;
        }
    }
}
