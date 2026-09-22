using UnityEngine;
using Core.InventorySystem;

public class TestProductionHDV : MonoBehaviour
{
    [SerializeField] private InventoryHolder hdvStorage;
    [SerializeField] private ItemDefinition bois;
    [SerializeField] private ItemDefinition pierre;
    [SerializeField] private ItemDefinition fer;
    [SerializeField] private ItemDefinition nourriture;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < 2f) return;
        timer = 0f;

        var inv = hdvStorage.Inventory;
        Debug.Log($"[HDV] Bois={inv.Count(bois)} Pierre={inv.Count(pierre)} Fer={inv.Count(fer)} Nourriture={inv.Count(nourriture)}");
    }
}