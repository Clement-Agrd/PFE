using System.Collections.Generic;
using UnityEngine;

namespace Core.InventorySystem
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
    public class ItemDefinition : ScriptableObject
    {
        [field: SerializeField] public string Id { get; private set; } = "item_id";
        [field: SerializeField] public string DisplayName { get; private set; } = "Nouvel Objet";
        [field: SerializeField] public Sprite Icon { get; private set; }
        [field: SerializeField] public int MaxStackSize { get; private set; } = 99;

        [Header("Catégorie")]
        [SerializeField] private ItemCategory category = ItemCategory.Misc;

        [Header("Armure (si Catégorie = Armor)")]
        [Tooltip("Emplacement de la pièce d'armure (tête, torse, jambes, pieds).")]
        [SerializeField] private ArmorSlotType armorSlot = ArmorSlotType.None;

        [Header("Monde 3D")]
        [field: SerializeField] public GameObject WorldPrefab { get; private set; }

        [SerializeField] private string interactionVerb = "Ramasser";

        [Header("Consommable (si Catégorie = Consumable)")]
        [Tooltip("Soin instantané appliqué à la consommation.")]
        [SerializeField, Min(0)] private int healAmount;
        [Tooltip("Effets de stats temporaires appliqués à la consommation (optionnel).")]
        [SerializeField] private List<ConsumableStatEffect> statEffects = new();

        public ItemCategory Category => category;
        public ArmorSlotType ArmorSlot => armorSlot;
        public string InteractionVerb => string.IsNullOrEmpty(interactionVerb) ? "Ramasser" : interactionVerb;
        public bool IsStackable => MaxStackSize > 1;
        public int HealAmount => healAmount;
        public IReadOnlyList<ConsumableStatEffect> StatEffects => statEffects;
    }
}