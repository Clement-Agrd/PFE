using System;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>
    /// Un niveau d'un bâtiment : son coût d'amélioration (pour l'ATTEINDRE), son
    /// visuel optionnel, et sa production passive (Ferme/Mine/Scierie).
    /// </summary>
    [Serializable]
    public sealed class BuildingLevelData
    {
        [Min(1)] public int level = 1;
        public ResourceCost[] upgradeCost;

        [Tooltip("Optionnel : modèle 3D à afficher à ce niveau.")]
        public GameObject visualPrefab;

        [Header("Production (Ferme / Mine / Scierie)")]
        [Tooltip("L'item produit à ce niveau par CE producteur. Vide = ne produit rien.")]
        public ItemDefinition producedItem;
        [Tooltip("Quantité produite par cycle.")]
        [Min(0)] public int productionAmount;
        [Tooltip("Secondes entre deux productions.")]
        [Min(0.1f)] public float productionInterval = 10f;
    }

    /// <summary>
    /// Un type de bâtiment (Forge, Taverne, Hôtel de Ville...). Les niveaux sont
    /// dans l'ordre : levels[0] = niveau 1, levels[1] = niveau 2, etc.
    /// Création : Assets > Create > Village > Building Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Building", menuName = "Village/Building Definition")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private BuildingLevelData[] levels;

        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxLevel => levels != null ? levels.Length : 0;

        /// <summary>Données du niveau demandé (1-indexé), ou null si hors bornes.</summary>
        public BuildingLevelData GetLevelData(int level)
        {
            if (levels == null || level < 1 || level > levels.Length) return null;
            return levels[level - 1];
        }
    }
}