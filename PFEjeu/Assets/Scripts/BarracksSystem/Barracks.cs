using System;
using System.Collections.Generic;
using Core.InteractionSystem;
using Core.StatsSystem;
using UnityEngine;
using UnityEngine.AI;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class Barracks : InteractableBase
    {
        [Serializable]
        private sealed class SoldierSlot
        {
            public BarracksSoldier soldier;
            public bool waitingRespawn;
            public float respawnAt;
        }

        [Header("Data")]
        [SerializeField] private SOBarracks barracksData;

        [Header("Level")]
        [SerializeField, Min(1)] private int currentLevel = 1;

        [Header("Model")]
        [SerializeField] private Transform modelParent;
        [SerializeField] private Transform spawnPoint;

        [Header("Flag")]
        [SerializeField] private BarracksFlagPlacement flagPlacement;

        [Header("Confirmation")]
        [SerializeField] private BarracksConfirmationUI confirmationUI;

        [Header("Spawn")]
        [SerializeField, Min(0.1f)] private float spawnNavMeshSampleRadius = 3f;
        [SerializeField] private bool spawnOnStart = true;

        private readonly List<SoldierSlot> _slots = new();
        private GameObject _currentModel;
        private bool _flagSubscribed;

        public override string Prompt =>
            flagPlacement != null && flagPlacement.IsPlacing
                ? "Annuler le placement du drapeau"
                : "Déplacer le drapeau";

        public SOBarracks Data => barracksData;
        public int CurrentLevel => currentLevel;

        public int AliveSoldierCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Count; i++)
                    if (_slots[i].soldier != null) count++;
                return count;
            }
        }

        public int MaximumSoldierCount
        {
            get
            {
                SOBarracks.BarracksLevelStats stats = GetCurrentLevelStats();
                return stats != null ? stats.soldierCount : 0;
            }
        }

        public float RespawnTime
        {
            get
            {
                SOBarracks.BarracksLevelStats stats = GetCurrentLevelStats();
                return stats != null ? stats.respawnTime : 0f;
            }
        }

        public float PlacementRange =>
            barracksData != null ? barracksData.FlagPlacementRange : 0f;

        private void Awake()
        {
            ResolveReferences();

            if (barracksData != null)
            {
                currentLevel = Mathf.Clamp(
                    currentLevel,
                    barracksData.MinLevel,
                    barracksData.MaxLevel);
            }

            ConfigureFlagPlacement();
        }

        private void OnEnable() => SubscribeFlag();

        private void Start()
        {
            if (barracksData == null)
            {
                Debug.LogWarning("[Barracks] Aucun SOBarracks assigné.", this);
                return;
            }

            UpdateModel();
            ConfigureFlagPlacement();
            EnsureInitialFlag();
            EnsureSlotCount();

            if (spawnOnStart)
                SpawnAllMissingSoldiers();
        }

        private void OnDisable() => UnsubscribeFlag();

        private void OnDestroy()
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].soldier != null)
                    _slots[i].soldier.DespawnWithoutRespawn();
        }

        private void Update()
        {
            if (barracksData == null) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                SoldierSlot slot = _slots[i];
                if (!slot.waitingRespawn || Time.time < slot.respawnAt) continue;

                slot.waitingRespawn = false;
                SpawnSoldier(i);
            }
        }

        public override bool CanInteract(GameObject interactor)
        {
            return isActiveAndEnabled &&
                   barracksData != null &&
                   flagPlacement != null &&
                   (confirmationUI == null || !confirmationUI.IsOpen);
        }

        public override void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return;

            if (flagPlacement.IsPlacing)
            {
                flagPlacement.CancelPlacement();
                return;
            }

            if (confirmationUI != null)
                confirmationUI.Show(this, flagPlacement, interactor);
            else
                flagPlacement.BeginPlacement(interactor);
        }

        public void LevelUp()
        {
            if (barracksData == null) return;

            int newLevel = Mathf.Clamp(
                currentLevel + 1,
                barracksData.MinLevel,
                barracksData.MaxLevel);

            if (newLevel == currentLevel) return;

            currentLevel = newLevel;

            UpdateModel();
            EnsureSlotCount();
            ApplyStatsToAliveSoldiers();
            SpawnAllMissingSoldiers();
            RefreshCampPositions();
        }

        public void NotifySoldierDied(BarracksSoldier soldier, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            SoldierSlot slot = _slots[slotIndex];
            if (slot.soldier != soldier) return;

            slot.soldier = null;
            slot.waitingRespawn = true;
            slot.respawnAt = Time.time + Mathf.Max(0.1f, RespawnTime);
        }

        private void ResolveReferences()
        {
            modelParent ??= transform;
            flagPlacement ??= GetComponent<BarracksFlagPlacement>();
        }

        private void ConfigureFlagPlacement()
        {
            if (flagPlacement == null || barracksData == null) return;

            flagPlacement.Configure(this, barracksData);
            SubscribeFlag();
        }

        private void SubscribeFlag()
        {
            if (_flagSubscribed || flagPlacement == null) return;

            flagPlacement.FlagPlaced += HandleFlagPlaced;
            _flagSubscribed = true;
        }

        private void UnsubscribeFlag()
        {
            if (!_flagSubscribed || flagPlacement == null) return;

            flagPlacement.FlagPlaced -= HandleFlagPlaced;
            _flagSubscribed = false;
        }

        private void EnsureInitialFlag()
        {
            if (flagPlacement == null || barracksData == null || flagPlacement.HasFlag) return;

            Vector3 initial = transform.TransformPoint(barracksData.DefaultFlagOffset);
            flagPlacement.SetInitialFlagPosition(initial);
        }

        private void HandleFlagPlaced(Vector3 position) => RefreshCampPositions();

        private void EnsureSlotCount()
        {
            int targetCount = Mathf.Max(0, MaximumSoldierCount);

            while (_slots.Count < targetCount)
                _slots.Add(new SoldierSlot());

            while (_slots.Count > targetCount)
            {
                int lastIndex = _slots.Count - 1;
                SoldierSlot slot = _slots[lastIndex];

                if (slot.soldier != null)
                    slot.soldier.DespawnWithoutRespawn();

                _slots.RemoveAt(lastIndex);
            }
        }

        private void SpawnAllMissingSoldiers()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                SoldierSlot slot = _slots[i];
                if (slot.soldier != null) continue;

                slot.waitingRespawn = false;
                SpawnSoldier(i);
            }
        }

        private void SpawnSoldier(int slotIndex)
        {
            if (barracksData == null || barracksData.SoldierPrefab == null)
            {
                Debug.LogWarning("[Barracks] Aucun SoldierPrefab assigné dans le SOBarracks.", this);
                return;
            }

            if (slotIndex < 0 || slotIndex >= _slots.Count) return;

            Vector3 desiredSpawn = spawnPoint != null
                ? spawnPoint.position
                : transform.position;

            Vector3 spawnPosition = desiredSpawn;

            if (NavMesh.SamplePosition(
                    desiredSpawn,
                    out NavMeshHit spawnHit,
                    spawnNavMeshSampleRadius,
                    NavMesh.AllAreas))
            {
                spawnPosition = spawnHit.position;
            }

            GameObject soldierObject = Instantiate(
                barracksData.SoldierPrefab,
                spawnPosition,
                transform.rotation);

            BarracksSoldier soldier =
                soldierObject.GetComponent<BarracksSoldier>() ??
                soldierObject.AddComponent<BarracksSoldier>();

            EntityStats stats =
                soldierObject.GetComponent<EntityStats>() ??
                soldierObject.AddComponent<EntityStats>();

            barracksData.ApplySoldierStatsTo(stats, currentLevel);

            SoldierSlot slot = _slots[slotIndex];
            slot.soldier = soldier;
            slot.waitingRespawn = false;

            soldier.Initialize(this, slotIndex, GetCampPosition(slotIndex));
        }

        private void ApplyStatsToAliveSoldiers()
        {
            if (barracksData == null) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                BarracksSoldier soldier = _slots[i].soldier;
                if (soldier == null || soldier.Stats == null) continue;

                barracksData.ApplySoldierStatsTo(soldier.Stats, currentLevel);
            }
        }

        private void RefreshCampPositions()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                BarracksSoldier soldier = _slots[i].soldier;
                if (soldier != null)
                    soldier.SetCampPosition(GetCampPosition(i));
            }
        }

        private Vector3 GetCampPosition(int slotIndex)
        {
            Vector3 center =
                flagPlacement != null && flagPlacement.HasFlag
                    ? flagPlacement.FlagPosition
                    : transform.position;

            int count = Mathf.Max(1, _slots.Count);

            float radius = barracksData != null
                ? Mathf.Max(0.1f, barracksData.CampRadius)
                : 1.5f;

            float angle = count <= 1
                ? 0f
                : (slotIndex / (float)count) * Mathf.PI * 2f;

            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)) * radius;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;

            return candidate;
        }

        private SOBarracks.BarracksLevelStats GetCurrentLevelStats()
        {
            return barracksData != null
                ? barracksData.GetLevelStats(currentLevel)
                : null;
        }

        private void UpdateModel()
        {
            if (barracksData == null ||
                barracksData.models == null ||
                barracksData.models.Length == 0)
            {
                return;
            }

            int modelIndex = currentLevel - barracksData.MinLevel;
            if (modelIndex < 0 || modelIndex >= barracksData.models.Length) return;

            GameObject model = barracksData.models[modelIndex];
            if (model == null || modelParent == null) return;

            if (_currentModel != null)
                Destroy(_currentModel);

            _currentModel = Instantiate(model, modelParent);
            _currentModel.transform.localPosition = Vector3.zero;

            Transform foundSpawn = FindDeepChild(_currentModel.transform, "SpawnPoint");
            if (foundSpawn != null)
                spawnPoint = foundSpawn;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null) return null;
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeepChild(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (barracksData == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, barracksData.FlagPlacementRange);

            if (flagPlacement != null && flagPlacement.HasFlag)
            {
                Gizmos.DrawLine(transform.position, flagPlacement.FlagPosition);
                Gizmos.DrawWireSphere(flagPlacement.FlagPosition, barracksData.CampRadius);
            }
        }
    }
}
