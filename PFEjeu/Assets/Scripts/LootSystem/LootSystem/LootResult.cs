using Core.InventorySystem;

namespace Core.LootSystem
{
    /// <summary>Un objet obtenu d'un roll de butin : quel item, en quelle quantité.</summary>
    public readonly struct LootResult
    {
        public readonly ItemDefinition Item;
        public readonly int Amount;

        public LootResult(ItemDefinition item, int amount)
        {
            Item = item;
            Amount = amount;
        }
    }
}
