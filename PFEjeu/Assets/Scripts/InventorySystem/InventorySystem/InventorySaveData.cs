using System;
using System.Collections.Generic;

namespace Core.InventorySystem
{
    /// <summary>
    /// État sérialisable de l'inventaire : une entrée par slot occupé, stockée
    /// par 'id' d'item (pas par référence) → compatible JSON / Save System.
    /// </summary>
    [Serializable]
    public sealed class InventorySaveData
    {
        public List<SlotData> slots = new();

        [Serializable]
        public struct SlotData
        {
            public int index;
            public string itemId;
            public int quantity;
        }
    }
}
