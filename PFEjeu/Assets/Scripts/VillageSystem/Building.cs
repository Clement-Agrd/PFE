using System;
using UnityEngine;

namespace Core.Village
{
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] private BuildingDefinition definition;
        [SerializeField, Min(1)] private int currentLevel = 1;
        [SerializeField] private Transform visualRoot;

        private GameObject _currentVisual;

        public BuildingDefinition Definition => definition;
        public int CurrentLevel => currentLevel;
        public bool IsMaxLevel => definition != null && currentLevel >= definition.MaxLevel;

        public event Action<int> OnLevelChanged;

        private void Start() => ApplyVisual();

        public BuildingLevelData GetNextLevelData()
            => definition != null ? definition.GetLevelData(currentLevel + 1) : null;

        public void SetLevel(int level)
        {
            currentLevel = Mathf.Max(1, level);
            ApplyVisual();
            OnLevelChanged?.Invoke(currentLevel);
        }

        private void ApplyVisual()
        {
            if (definition == null) return;
            BuildingLevelData data = definition.GetLevelData(currentLevel);
            if (data == null || data.visualPrefab == null) return;

            if (_currentVisual != null) Destroy(_currentVisual);

            Transform parent = visualRoot != null ? visualRoot : transform;
            _currentVisual = Instantiate(data.visualPrefab, parent);
        }
    }
}