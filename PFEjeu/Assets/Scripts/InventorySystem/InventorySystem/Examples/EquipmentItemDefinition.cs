using UnityEngine;

namespace Core.InventorySystem.Examples
{
    /// <summary>
    /// Exemple d'item dérivé : un équipement (non empilable) porteur de stats.
    /// Mets maxStackSize à 1 sur l'asset pour qu'il occupe un slot par pièce.
    /// Création : Assets > Create > Inventory > Items > Equipment
    /// </summary>
    [CreateAssetMenu(fileName = "Equipment", menuName = "Inventory/Items/Equipment")]
    public sealed class EquipmentItemDefinition : ItemDefinition
    {
        [Header("Stats équipement")]
        [SerializeField] private int armor;
        [SerializeField] private int damage;

        public int Armor => armor;
        public int Damage => damage;

        public override void Use(GameObject user)
            => Debug.Log($"[Equipment] {DisplayName} équipé sur {user.name} (+{armor} arm, +{damage} dmg).");
    }
}
