using UnityEngine;

namespace Core.HealthSystem
{
    /// <summary>
    /// Nature précise de l'action ayant produit les dégâts.
    ///
    /// DamageType répond à :
    /// "Physique ou Magique ?"
    ///
    /// DamageSourceKind répond à :
    /// "Avec quoi les dégâts ont-ils été infligés ?"
    /// </summary>
    public enum DamageSourceKind
    {
        None,

        Sword,
        Bow,
        Magic,

        Axe,
        Pickaxe
    }

    /// <summary>
    /// Informations transportées avec un coup.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;

        public readonly DamageType Type;

        public readonly float ArmorPenetration;

        public readonly GameObject Source;

        public readonly DamageSourceKind SourceKind;

        /// <summary>Point d'impact monde (optionnel) : les chiffres de dégâts y apparaissent. Sans lui, le torse de la cible est utilisé.</summary>
        public readonly Vector3 HitPoint;

        public readonly bool HasHitPoint;


        public DamageInfo(
            int amount,
            DamageType type = null,
            GameObject source = null,
            DamageSourceKind sourceKind = DamageSourceKind.None,
            float armorPenetration = 0f,
            Vector3? hitPoint = null)
        {
            HitPoint = hitPoint ?? default;
            HasHitPoint = hitPoint.HasValue;

            Amount = amount;

            Type = type;
            ArmorPenetration = Mathf.Clamp01(armorPenetration);

            Source = source;

            SourceKind = sourceKind;
        }
    }
}