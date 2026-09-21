using System;
using UnityEngine;

namespace Core.Village
{
    /// <summary>
    /// Un bâtiment posé dans le village (position fixe). Connaît sa définition
    /// et son niveau actuel. Le VillageManager pilote les montées de niveau.
    /// </summary>
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] private BuildingDefinition definition;
        [SerializeField, Min(1)] private int currentLevel = 1;
        [Tooltip("Où instancier le visuel du niveau (si la définition en fournit). Vide = ce transform.")]
        [SerializeField] private Transform visualRoot;

        private GameObject _currentVisual;

        public BuildingDefinition Definition => definition;
        public int CurrentLevel => currentLevel;
        public bool IsMaxLevel => definition != null && currentLevel >= definition.MaxLevel;

        public event Action<int> OnLevelChanged; // nouveau niveau

        private void Start() => ApplyVisual();

        /// <summary>Coût pour passer au niveau suivant, ou null si déjà au max.</summary>
        public BuildingLevelData GetNextLevelData()
            => definition != null ? definition.GetLevelData(currentLevel + 1) : null;

        /// <summary>Appelé par le VillageManager après un achat réussi.</summary>
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