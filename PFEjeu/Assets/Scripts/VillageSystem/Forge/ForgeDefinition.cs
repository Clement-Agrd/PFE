using System;
using UnityEngine;
using Core.InventorySystem;
using Core.StatsSystem;

namespace Core.Village
{
    using StatType = EnumStats.StatTypes;

    public enum ForgeElement { None, Fire, Ice }

    /// <summary>Bonus de stat gagné à CHAQUE niveau d'amélioration d'un objet.</summary>
    [Serializable]
    public struct UpgradeBonus
    {
        public StatType stat;
        [Tooltip("Valeur ajoutée par niveau (Flat : +2 ; PercentAdditive : 0.05 = +5 %).")]
        public float valuePerLevel;
        public StatModifierType modifierType;
    }

    /// <summary>Règles d'amélioration d'une catégorie d'objets (armes, armures, pièges...).</summary>
    [Serializable]
    public sealed class ForgeCategoryRules
    {
        public ItemCategory category = ItemCategory.Weapon;
        [Tooltip("Bonus par niveau d'amélioration.")]
        public UpgradeBonus[] bonuses;
        [Tooltip("Appliqués aux stats du joueur (armes/armures). Décocher pour les pièges : " +
                 "leur bonus est lu par le code de la tour/piège.")]
        public bool appliesToPlayerStats = true;
        [Tooltip("Ressources rendues au démontage, en plus du remboursement des améliorations.")]
        public ResourceCost[] baseRefund;
    }

    /// <summary>Coût pour atteindre un niveau d'amélioration donné.</summary>
    [Serializable]
    public sealed class ForgeTier
    {
        public ResourceCost[] cost;
    }

    /// <summary>Un article vendu par la Forge, payé avec le stockage de l'HDV.</summary>
    [Serializable]
    public struct ForgeShopEntry
    {
        public ItemDefinition item;
        public ResourceCost[] cost;
    }

    /// <summary>
    /// Table d'équilibrage de la Forge.
    /// Création : Assets > Create > Village > Forge Definition
    /// </summary>
    [CreateAssetMenu(fileName = "ForgeDefinition", menuName = "Village/Forge Definition")]
    public sealed class ForgeDefinition : ScriptableObject
    {
        [Tooltip("Niveaux d'amélioration d'objet débloqués par niveau de Forge (3 → +3 / +6 / +9).")]
        [SerializeField, Min(1)] private int itemLevelsPerForgeLevel = 3;

        [Tooltip("tiers[0] = coût pour passer de +0 à +1, tiers[1] = +1 → +2, etc.")]
        [SerializeField] private ForgeTier[] tiers;

        [SerializeField] private ForgeCategoryRules[] categories;

        [Tooltip("Part (0-1) des ressources dépensées en améliorations rendue au démontage.")]
        [SerializeField, Range(0f, 1f)] private float dismantleRefundPercent = 0.5f;

        [Tooltip("Articles que la Forge vend (armures, armes, pièges...).")]
        [SerializeField] private ForgeShopEntry[] shop;

        [Header("Équipement de base")]
        [SerializeField] private ItemDefinition sword;
        [SerializeField] private ItemDefinition bow;
        [SerializeField] private ItemDefinition staff;
        [SerializeField] private SOTower fireSpell;
        [SerializeField] private SOTower iceSpell;
        [SerializeField] private GameObject swordFinisher;
        public GameObject SwordFinisher => swordFinisher;

        public const int ExplosionLevel = 3;
        public const int ElementLevel = 6;
        public const float ExplosionRadius = 2.5f;
        private static readonly UpgradeBonus[] ChestBonuses =
        {
            new UpgradeBonus { stat = StatType.Life, valuePerLevel = 25f, modifierType = StatModifierType.Flat }
        };

        public ItemDefinition GetWeapon(ProfessionalTPS.CombatMode mode) => mode switch
        {
            ProfessionalTPS.CombatMode.Melee => sword,
            ProfessionalTPS.CombatMode.Bow => bow,
            ProfessionalTPS.CombatMode.Magic => staff,
            _ => null
        };

        public SOTower GetSpell(ForgeElement element) => element switch
        {
            ForgeElement.Fire => fireSpell,
            ForgeElement.Ice => iceSpell,
            _ => null
        };

        public bool IsStaff(ItemDefinition item) => staff != null && item == staff;
        public bool IsPlayerWeapon(ItemDefinition item) => item != null && (item == sword || item == bow || item == staff);
        public bool IsSword(ItemDefinition item) => sword != null && item == sword;
        public UpgradeBonus[] GetBonuses(ItemDefinition item) =>
            item == null || IsPlayerWeapon(item) ? Array.Empty<UpgradeBonus>() :
            item.Category == ItemCategory.Armor && item.ArmorSlot == ArmorSlotType.Chest ? ChestBonuses :
            GetRules(item.Category)?.bonuses ?? Array.Empty<UpgradeBonus>();

        public static float WeaponDamage(int level) => level * 2f;
        public static float PhysicalScaling(int level) => (level / 3) * .10f;
        public static float MagicScaling(int level) => level * .05f;
        public static float SwordReach(int level) => (level / 3) * .15f;
        public static float ArrowSpeed(int level) => 1f + (level / 3) * .15f;
        public static float WeaponCadence(int level) => 1f + (level / 3) * .10f;

        public float DismantleRefundPercent => dismantleRefundPercent;

        public System.Collections.Generic.IReadOnlyList<ForgeShopEntry> Shop
            => shop ?? Array.Empty<ForgeShopEntry>();

        /// <summary>Plus haut niveau d'amélioration possible, borné par le nombre de paliers définis.</summary>
        public int AbsoluteMaxLevel => tiers != null ? tiers.Length : 0;

        public int GetMaxItemLevel(int forgeLevel)
            => Mathf.Min(AbsoluteMaxLevel, Mathf.Max(0, forgeLevel) * itemLevelsPerForgeLevel);

        /// <summary>Forge minimale requise pour atteindre ce niveau d'objet.</summary>
        public int GetRequiredForgeLevel(int targetItemLevel)
            => Mathf.Max(1, Mathf.CeilToInt(targetItemLevel / (float)itemLevelsPerForgeLevel));

        /// <summary>Coût pour atteindre targetLevel (1-indexé). Tableau vide si hors bornes.</summary>
        public ResourceCost[] GetCost(int targetLevel)
        {
            if (tiers == null || targetLevel < 1 || targetLevel > tiers.Length) return Array.Empty<ResourceCost>();
            return tiers[targetLevel - 1].cost ?? Array.Empty<ResourceCost>();
        }

        public ForgeCategoryRules GetRules(ItemCategory category)
        {
            if (categories == null) return null;
            foreach (ForgeCategoryRules rules in categories)
                if (rules != null && rules.category == category) return rules;
            return null;
        }

        public bool IsUpgradable(ItemDefinition item)
            => item != null && GetRules(item.Category) != null;

        /// <summary>Somme des coûts payés pour atteindre le niveau donné (base du remboursement).</summary>
        public ResourceCost[] GetInvestedCost(int level)
        {
            var totals = new System.Collections.Generic.Dictionary<ItemDefinition, int>();
            for (int l = 1; l <= level; l++)
            {
                foreach (ResourceCost c in GetCost(l))
                {
                    if (c.item == null) continue;
                    totals.TryGetValue(c.item, out int v);
                    totals[c.item] = v + c.amount;
                }
            }

            var result = new ResourceCost[totals.Count];
            int i = 0;
            foreach (var pair in totals)
                result[i++] = new ResourceCost { item = pair.Key, amount = pair.Value };
            return result;
        }
    }
}
