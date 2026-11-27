using System;
using UnityEngine;

namespace Core.Village
{
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] private BuildingDefinition definition;
        [SerializeField, Min(1)] private int currentLevel = 1;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private bool fitColliderToVisual;

        [SerializeField, HideInInspector] private GameObject _currentVisual;

        public BuildingDefinition Definition => definition;
        public int CurrentLevel => currentLevel;
        public bool IsMaxLevel => definition != null && currentLevel >= definition.MaxLevel;

        /// <summary>Jours restants pendant lesquels le bâtiment est hors service (événements du village).</summary>
        public int UnavailableDays { get; private set; }
        public bool IsAvailable => UnavailableDays <= 0;

        public event Action<int> OnLevelChanged;

        public void MakeUnavailable(int days) => UnavailableDays = Mathf.Max(UnavailableDays, days);

        public void TickUnavailable()
        {
            if (UnavailableDays > 0) UnavailableDays--;
        }

        private void Start() => ApplyVisual();

        public BuildingLevelData GetNextLevelData()
            => definition != null ? definition.GetLevelData(currentLevel + 1) : null;

        public void SetLevel(int level)
        {
            currentLevel = Mathf.Max(1, level);
            ApplyVisual();
            OnLevelChanged?.Invoke(currentLevel);
        }

        private void FitVisualCollider()
        {
            var body = GetComponent<BoxCollider>();
            if (body == null || body.isTrigger || _currentVisual == null) return;
            bool initialized = false; Bounds bounds = default;
            foreach (var filter in _currentVisual.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var local = filter.sharedMesh.bounds;
                var matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = matrix.MultiplyPoint3x4(corner);
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!initialized) return;
            body.size = new Vector3(bounds.size.x * .78f, Mathf.Min(4f, bounds.size.y * .7f), bounds.size.z * .78f);
            body.center = new Vector3(bounds.center.x, bounds.min.y + body.size.y * .5f, bounds.center.z);
        }

        private void ApplyVisual()
        {
            if (definition == null) return;
            BuildingLevelData data = definition.GetLevelData(currentLevel);
            if (data == null || data.visualPrefab == null) return;

            if (_currentVisual != null)
            {
                _currentVisual.SetActive(false);
                Destroy(_currentVisual);
            }

            Transform parent = visualRoot != null ? visualRoot : transform;
            _currentVisual = Instantiate(data.visualPrefab, parent);
            if (fitColliderToVisual) FitVisualCollider();
        }
    }
}
