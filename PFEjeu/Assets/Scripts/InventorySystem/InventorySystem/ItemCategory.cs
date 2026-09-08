namespace Core.InventorySystem
{
    /// <summary>
    /// Catégories d'items. Étends cet enum selon ton jeu, ou passe à des SO-tags
    /// si tu veux des catégories créables par un designer sans recompiler.
    /// </summary>
    public enum ItemCategory
    {
        Misc = 0,
        Weapon = 1,
        Armor = 2,
        Consumable = 3,
        Material = 4,
        Quest = 5
    }
}
