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
        
        [Header("Monde 3D")]
        [field: SerializeField] public GameObject WorldPrefab { get; private set; }

        public bool IsStackable => MaxStackSize > 1;
    }
}