using System.Collections.Generic;
using UnityEngine;
using Core.InventorySystem;

namespace Core.Village
{
    /// <summary>
    /// Fait produire un bâtiment (Ferme/Mine/Scierie) dans le temps. Lit TOUTES
    /// les productions du niveau actuel (une Mine peut produire pierre ET fer
    /// avec des rythmes différents) et les ajoute au stockage HDV.
    /// UN SEUL composant par bâtiment, quel que soit le nombre de ressources.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public sealed class ResourceProducer : MonoBehaviour
    {
        [Tooltip("Le stockage où déposer la production (l'inventaire de l'HDV).")]
        [SerializeField] private InventoryHolder targetStorage;

        private Building _building;

        // Un minuteur indépendant par ressource produite (index dans la liste 'productions').
        private readonly Dictionary<int, float> _timers = new();

        public event System.Action<ItemDefinition, int> OnProduced;

        private void Awake() => _building = GetComponent<Building>();

        private void OnEnable()
        {
            _timers.Clear();
            _building.OnLevelChanged += HandleLevelChanged;
        }

        private void OnDisable() => _building.OnLevelChanged -= HandleLevelChanged;

        private void Update()
        {
            BuildingLevelData data = _building.Definition != null
                ? _building.Definition.GetLevelData(_building.CurrentLevel)
                : null;

            if (data == null || data.productions == null)
            {
                Debug.Log($"[Producer] {gameObject.name} : data ou productions NULL (definition={_building.Definition}, level={_building.CurrentLevel})");
                return;
            }

            for (int i = 0; i < data.productions.Length; i++)
            {
                ProductionEntry entry = data.productions[i];
                if (entry.item == null || entry.amount <= 0)
                {
                    Debug.Log($"[Producer] {gameObject.name} entrée {i} : item ou amount invalide (item={entry.item}, amount={entry.amount})");
                    continue;
                }

                float timer = _timers.TryGetValue(i, out float t) ? t : 0f;
                timer += Time.deltaTime;

                Debug.Log($"[Producer] {gameObject.name} entrée {i} ({entry.item.DisplayName}) : timer={timer:F1}/{entry.interval}");

                if (timer >= entry.interval)
                {
                    timer -= entry.interval;
                    Produce(entry.item, entry.amount);
                }

                _timers[i] = timer;
            }
        }

        private void Produce(ItemDefinition item, int amount)
        {
            if (targetStorage == null || targetStorage.Inventory == null)
            {
                Debug.LogWarning($"[Producer] {gameObject.name} : targetStorage non assigné !");
                return;
            }

            targetStorage.Inventory.Add(item, amount);
            OnProduced?.Invoke(item, amount);
            Debug.Log($"[Producer] {gameObject.name} a produit {amount}x {item.DisplayName}");
        }

        // Remet tous les minuteurs à zéro à chaque montée de niveau.
        private void HandleLevelChanged(int newLevel)
        {
            Debug.Log($"[Producer] {gameObject.name} : OnLevelChanged déclenché (nouveau niveau={newLevel}) → timers remis à zéro");
            _timers.Clear();
        }
    }
}