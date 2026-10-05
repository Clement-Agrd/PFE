using System;
using Core.InventorySystem;
using UnityEngine;

namespace Core.Village.Exploration
{
    /// <summary>
    /// Une entrée de butin potentiel : "amount" (min-max) de "item", avec une
    /// chance de drop (1 = garanti, sinon tiré indépendamment des autres entrées).
    /// </summary>
    [Serializable]
    public sealed class LootEntry
    {
        public ItemDefinition item;
        [Min(0)] public int minAmount = 1;
        [Min(0)] public int maxAmount = 1;
        [Range(0f, 1f)] public float dropChance = 1f;
        [Tooltip("Bénéficie du bonus \"ressources rares\" du Mage en Grotte.")]
        public bool isRare;
    }
}
