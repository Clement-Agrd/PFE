using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Village.UI
{
    /// <summary>
    /// Bilan en lecture seule du village (onglet "Village" de l'inventaire) :
    /// une ligne par bâtiment (niveau + production) sous la barre de ressources
    /// de l'HDV. Se rafraîchit à l'ouverture de l'onglet et quand un bâtiment
    /// est amélioré ailleurs (vue village). Aucune interaction d'amélioration
    /// ici, volontairement — ça reste le rôle de BuildingPanelUI.
    /// </summary>
    public sealed class VillageOverviewPanelUI : MonoBehaviour
    {
        [SerializeField] private VillageManager villageManager;
        [SerializeField] private Transform buildingRowsParent;
        [SerializeField] private BuildingSummaryRowUI rowPrefab;

        private readonly List<BuildingSummaryRowUI> _rows = new();

        private void OnEnable()
        {
            if (villageManager != null) villageManager.OnBuildingUpgraded += HandleBuildingUpgraded;
        }

        private void OnDisable()
        {
            if (villageManager != null) villageManager.OnBuildingUpgraded -= HandleBuildingUpgraded;
        }

        private void HandleBuildingUpgraded(Building building, int newLevel) => Refresh();

        /// <summary>À appeler quand l'onglet devient visible.</summary>
        public void Refresh()
        {
            if (buildingRowsParent == null || rowPrefab == null) return;

            Building[] buildings = Array.FindAll(
                FindObjectsByType<Building>(),
                b => b.Definition != null);
            Array.Sort(buildings, (a, b) => string.Compare(DisplayName(a), DisplayName(b), StringComparison.Ordinal));

            EnsureRows(buildings.Length);
            for (int i = 0; i < buildings.Length; i++)
                _rows[i].Set(buildings[i]);
        }

        private static string DisplayName(Building building)
            => building.Definition != null ? building.Definition.DisplayName : building.name;

        private void EnsureRows(int count)
        {
            while (_rows.Count < count) _rows.Add(Instantiate(rowPrefab, buildingRowsParent));
            for (int i = 0; i < _rows.Count; i++) _rows[i].gameObject.SetActive(i < count);
        }
    }
}
