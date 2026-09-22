using System;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>Une ressource produite par un bâtiment à un niveau donné.</summary>
    [Serializable]
    public struct ProductionEntry
    {
        public ItemDefinition item;
        [Min(1)] public int amount;
        [Min(0.1f)] public float interval; // secondes entre deux productions
    }

    /// <summary>
    /// Un niveau d'un bâtiment : son coût d'amélioration, son visuel optionnel,
    /// et TOUT ce qu'il produit à ce niveau (une Mine peut produire pierre ET fer).
    /// </summary>
    [Serializable]
    public sealed class BuildingLevelData
    {
        [Min(1)] public int level = 1;
        public ResourceCost[] upgradeCost;

        [Tooltip("Optionnel : modèle 3D à afficher à ce niveau.")]
        public GameObject visualPrefab;

        [Header("Production (Ferme / Mine / Scierie)")]
        [Tooltip("Tout ce que ce bâtiment produit à ce niveau (une entrée par ressource).")]
        public ProductionEntry[] productions;
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