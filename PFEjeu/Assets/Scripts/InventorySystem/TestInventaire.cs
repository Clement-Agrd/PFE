using UnityEngine;
using Core.InventorySystem;

public class TestInventaire : MonoBehaviour
{
    [SerializeField] private InventoryHolder holder;
    [SerializeField] private ItemDefinition item;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (holder == null) { Debug.LogError("[Test] holder non assigné !"); return; }
            if (holder.Inventory == null) { Debug.LogError("[Test] holder.Inventory est NULL !"); return; }
            if (item == null) { Debug.LogError("[Test] item non assigné !"); return; }

            int leftover = holder.Inventory.Add(item, 3);
            Debug.Log($"[Test] Ajouté 3x {item.DisplayName}. Non rentré : {leftover}. " +
                      $"Total dans l'inventaire : {holder.Inventory.Count(item)}");
        }
    }
}