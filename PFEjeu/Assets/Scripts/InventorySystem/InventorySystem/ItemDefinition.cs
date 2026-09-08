using UnityEngine;

namespace Core.InventorySystem
{
    /// <summary>
    /// Données STATIQUES d'un type d'item (template partagé). Un asset = un type.
    /// Le runtime ne duplique jamais ces données : les ItemStack pointent vers cet asset.
    /// Hérite cette classe pour des items spécialisés (voir Examples/).
    /// Création : Assets > Create > Inventory > Item Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Item", menuName = "Inventory/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identité")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [TextArea] [SerializeField] private string description;

        [Header("Présentation")]
        [SerializeField] private Sprite icon;
        [SerializeField] private ItemCategory category = ItemCategory.Misc;

        [Header("Stacking")]
        [Tooltip("1 = non empilable (1 par slot). >1 = empilable jusqu'à cette valeur.")]
        [SerializeField, Min(1)] private int maxStackSize = 99;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public ItemCategory Category => category;
        public int MaxStackSize => maxStackSize;
        public bool IsStackable => maxStackSize > 1;

        /// <summary>
        /// Comportement à l'utilisation. Base : aucun effet. Surcharge dans les
        /// types dérivés (consommable, équipement...) pour définir l'effet.
        /// </summary>
        public virtual void Use(GameObject user)
        {
            Debug.Log($"[Item] {displayName} utilisé (aucun effet défini).");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Id par défaut = nom de l'asset → garantit une clé stable pour la save.
            if (string.IsNullOrEmpty(id))
                id = name;
        }
#endif
    }
}
