using System.Collections.Generic;
using UnityEngine;

namespace Core.TavernSystem
{
    /// <summary>
    /// La carte d'une taverne : la liste des plats/boissons proposés. Un asset
    /// réutilisable, comme ShopDefinition pour les boutiques.
    /// Création : Assets > Create > Village > Tavern Menu.
    /// </summary>
    [CreateAssetMenu(fileName = "TavernMenu", menuName = "Village/Tavern Menu")]
    public sealed class TavernMenuDefinition : ScriptableObject
    {
        [SerializeField] private List<TavernDishDefinition> dishes = new();

        public IReadOnlyList<TavernDishDefinition> Dishes => dishes;
    }
}
