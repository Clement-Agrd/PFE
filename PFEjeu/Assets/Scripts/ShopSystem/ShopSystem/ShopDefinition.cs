using System.Collections.Generic;
using UnityEngine;

namespace Core.ShopSystem
{
    /// <summary>
    /// Données d'une boutique (asset) : son nom, ses articles, et si la vente y est
    /// autorisée. Le stock restant est géré au runtime par le composant Shop.
    /// Création : Assets > Create > Shop > Shop Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Shop", menuName = "Shop/Shop Definition")]
    public sealed class ShopDefinition : ScriptableObject
    {
        [SerializeField] private string shopName;
        [SerializeField] private List<ShopEntry> entries = new();

        [Tooltip("Le joueur peut-il vendre des objets à ce marchand ?")]
        [SerializeField] private bool canSellHere = true;

        public string ShopName => shopName;
        public IReadOnlyList<ShopEntry> Entries => entries;
        public bool CanSellHere => canSellHere;
    }
}
