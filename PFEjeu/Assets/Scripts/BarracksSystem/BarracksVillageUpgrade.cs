using Core.Village;
using Core.Village.UI;
using UnityEngine;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Barracks), typeof(Building))]
    public sealed class BarracksVillageUpgrade : MonoBehaviour
    {
        [SerializeField] private BuildingPanelUI buildingPanel;
        private Barracks _barracks;
        private Building _building;

        public bool CanOpen => isActiveAndEnabled && buildingPanel != null && _building != null && _building.Definition != null;

        private void Awake()
        {
            _barracks = GetComponent<Barracks>();
            _building = GetComponent<Building>();
        }

        private void OnEnable() => _building.OnLevelChanged += HandleLevelChanged;
        private void OnDisable() => _building.OnLevelChanged -= HandleLevelChanged;
        private void Start() => HandleLevelChanged(_building.CurrentLevel);

        private void HandleLevelChanged(int level) => _barracks.SetLevel(level);

        public void OpenPanel()
        {
            if (CanOpen) buildingPanel.ShowStandalone(_building);
        }
    }
}
