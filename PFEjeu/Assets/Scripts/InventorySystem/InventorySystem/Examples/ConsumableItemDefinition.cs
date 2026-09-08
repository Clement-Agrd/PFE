using UnityEngine;

namespace Core.InventorySystem.Examples
{
    /// <summary>
    /// Exemple d'item dérivé : un consommable qui soigne à l'utilisation.
    /// Montre l'extensibilité par héritage de ScriptableObject.
    /// Création : Assets > Create > Inventory > Items > Consumable
    /// </summary>
    [CreateAssetMenu(fileName = "Consumable", menuName = "Inventory/Items/Consumable")]
    public sealed class ConsumableItemDefinition : ItemDefinition
    {
        [Header("Effet consommable")]
        [SerializeField] private int healAmount = 25;

        public int HealAmount => healAmount;

        public override void Use(GameObject user)
        {
            // Ici tu brancherais ta vraie logique, ex :
            // user.GetComponent<Health>()?.Heal(healAmount);
            Debug.Log($"[Consumable] {DisplayName} soigne {healAmount} PV sur {user.name}.");
        }
    }
}
