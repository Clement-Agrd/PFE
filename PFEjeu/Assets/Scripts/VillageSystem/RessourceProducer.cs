using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>
    /// Fait produire un bâtiment (Ferme/Mine/Scierie) dans le temps : toutes les
    /// N secondes (réglées par le niveau), ajoute des ressources au stockage HDV.
    /// Pour un bâtiment qui produit 2 ressources (ex. la Mine : pierre + fer),
    /// pose DEUX ResourceProducer sur le même bâtiment.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public sealed class ResourceProducer : MonoBehaviour
    {
        [Tooltip("Le stockage où déposer la production (l'inventaire de l'HDV).")]
        [SerializeField] private InventoryHolder targetStorage;

        private Building _building;
        private float _timer;

        public event System.Action<ItemDefinition, int> OnProduced;

        private void Awake() => _building = GetComponent<Building>();

        private void OnEnable()
        {
            _timer = 0f;
            _building.OnLevelChanged += HandleLevelChanged;
        }

        private void OnDisable() => _building.OnLevelChanged -= HandleLevelChanged;

        private void Update()
        {
            BuildingLevelData data = _building.Definition != null
                ? _building.Definition.GetLevelData(_building.CurrentLevel)
                : null;

            if (data == null || data.producedItem == null || data.productionAmount <= 0)
                return; // ce bâtiment/niveau ne produit rien

            _timer += Time.deltaTime;
            if (_timer < data.productionInterval) return;

            _timer -= data.productionInterval;
            Produce(data.producedItem, data.productionAmount);
        }

        private void Produce(ItemDefinition item, int amount)
        {
            if (targetStorage == null || targetStorage.Inventory == null) return;

            targetStorage.Inventory.Add(item, amount);
            OnProduced?.Invoke(item, amount);
        }

        // Remet le minuteur à zéro à chaque montée de niveau (évite un cycle "à cheval"
        // entre l'ancien rythme et le nouveau).
        private void HandleLevelChanged(int newLevel) => _timer = 0f;
    }
}