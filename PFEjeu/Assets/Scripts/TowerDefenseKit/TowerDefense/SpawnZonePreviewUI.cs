using System.Collections.Generic;
using UnityEngine;
using Core.WaveSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Affiche, au-dessus d'une SpawnZone, les icônes des ennemis annoncés pour
    /// cette zone (façon Dungeon Defenders 2), avec leur compteur. Apparaît dès
    /// l'annonce (avant même le spawn), disparaît une fois l'entrée épuisée.
    /// À poser sur le MÊME GameObject que la SpawnZone concernée.
    /// </summary>
    [RequireComponent(typeof(SpawnZone))]
    public sealed class SpawnZonePreviewUI : MonoBehaviour
    {
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private Transform iconsParent;
        [SerializeField] private SpawnPreviewSlotUI slotPrefab;
        [SerializeField] private Camera worldCamera;

        private SpawnZone _zone;
        private readonly Dictionary<GameObject, SpawnPreviewSlotUI> _slots = new();

        private void Awake()
        {
            _zone = GetComponent<SpawnZone>();
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCanvas != null) worldCanvas.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnZoneSpawnStarted += HandleZoneSpawnStarted;
            waveSpawner.OnZoneSpawnFinished += HandleZoneSpawnFinished;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnZoneSpawnStarted -= HandleZoneSpawnStarted;
            waveSpawner.OnZoneSpawnFinished -= HandleZoneSpawnFinished;
        }

        private void LateUpdate()
        {
            if (worldCanvas != null && worldCanvas.gameObject.activeSelf && worldCamera != null)
                worldCanvas.transform.rotation = worldCamera.transform.rotation;
        }

        private void HandleZoneSpawnStarted(string zoneId, GameObject prefab, int totalCount)
        {
            if (_zone == null || zoneId != _zone.Id || prefab == null) return;
            if (slotPrefab == null) return;

            if (!_slots.TryGetValue(prefab, out SpawnPreviewSlotUI slot))
            {
                slot = Instantiate(slotPrefab, iconsParent);

                Sprite icon = null;
                if (prefab.TryGetComponent(out NavEnemy navEnemy) && navEnemy.Definition != null)
                    icon = navEnemy.Definition.Icon;

                slot.SetIcon(icon);
                _slots[prefab] = slot;
            }

            slot.SetCount(totalCount);

            if (worldCanvas != null) worldCanvas.gameObject.SetActive(true);
        }

        private void HandleZoneSpawnFinished(string zoneId)
        {
            if (_zone == null || zoneId != _zone.Id) return;

            foreach (SpawnPreviewSlotUI slot in _slots.Values)
                if (slot != null) Destroy(slot.gameObject);
            _slots.Clear();

            if (worldCanvas != null) worldCanvas.gameObject.SetActive(false);
        }
    }
}