using System;
using System.Collections.Generic;
using System.Linq;
using Core.BarracksSystem;
using Core.InventorySystem;
using Core.WaveSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace Core.Village.Exploration
{
    public sealed class ExpeditionResult
    {
        public string HeroName { get; internal set; }
        public ExplorationMissionDefinition Mission { get; internal set; }
        public ExpeditionOutcome Outcome { get; internal set; }
        public List<(ItemDefinition item, int amount)> Loot { get; } = new();
        public List<(ItemDefinition item, int amount)> PendingLoot { get; } = new();
        public string Status => Outcome == ExpeditionOutcome.CriticalFailure ? "Échec critique — héros perdu définitivement"
            : Outcome == ExpeditionOutcome.Failure ? "Échec — butin réduit" : "Réussite";
    }

    public sealed class ExplorationManager : MonoBehaviour
    {
        [SerializeField] private VillageManager villageManager;
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private HeroRoster recruitedHeroes;
        [SerializeField] private Building expeditionBuilding;
        [FormerlySerializedAs("heroRoster"), SerializeField, HideInInspector]
        private List<HeroDefinition> legacyHeroDefinitions = new();
        [SerializeField] private List<ExplorationMissionDefinition> availableMissions = new();
        [Header("Chances — niveaux du héros et de la zone")]
        [SerializeField, Range(0f, 1f)] private float equalLevelSuccess = .7f;
        [SerializeField, Range(0f, .3f)] private float successPerLevel = .1f;
        [SerializeField, Range(0f, 1f)] private float criticalShareOfFailures = .2f;
        [SerializeField, Range(0f, 1f)] private float failedLootFactor = .4f;

        private sealed class ActiveMission
        {
            public ExplorationMissionDefinition Definition;
            public heros Hero;
            public string HeroName;
            public ExpeditionOdds Odds;
            public int CyclesRemaining;
            public bool Departed;
        }
        private bool _defenseInProgress;
        private readonly List<ActiveMission> _active = new();
        private readonly List<ExpeditionResult> _results = new();
        private readonly Dictionary<heros, int> _wounded = new();
        public float FailedLootFactor => failedLootFactor;
        public int BuildingLevel => expeditionBuilding != null ? expeditionBuilding.CurrentLevel : 0;
        public Building ExpeditionBuilding => expeditionBuilding;
        public IReadOnlyList<heros> HeroRoster => recruitedHeroes != null ? recruitedHeroes.OrderedHeroes.ToArray() : Array.Empty<heros>();
        public IReadOnlyList<ExplorationMissionDefinition> AllMissions => availableMissions;
        public IReadOnlyList<ExplorationMissionDefinition> AvailableMissions => availableMissions.Where(IsMissionUnlocked).ToArray();
        public IReadOnlyList<ExpeditionResult> RecentResults => _results;
        public event Action OnStateChanged;

        private void OnEnable()
        {
            if (waveSpawner != null)
            {
                _defenseInProgress = waveSpawner.IsWaveActive;
                waveSpawner.OnWaveStarted += HandleDefenseStarted;
                waveSpawner.OnAllWavesCompleted += AdvanceCycle;
            }
            if (recruitedHeroes != null) recruitedHeroes.Changed += HandleRosterChanged;
            if (expeditionBuilding != null) expeditionBuilding.OnLevelChanged += HandleLevelChanged;
        }
        private void OnDisable()
        {
            if (waveSpawner != null)
            {
                waveSpawner.OnWaveStarted -= HandleDefenseStarted;
                waveSpawner.OnAllWavesCompleted -= AdvanceCycle;
            }
            if (recruitedHeroes != null) recruitedHeroes.Changed -= HandleRosterChanged;
            if (expeditionBuilding != null) expeditionBuilding.OnLevelChanged -= HandleLevelChanged;
        }
        private void OnDestroy()
        {
            foreach (var mission in _active.ToArray())
                if (mission.Hero != null) mission.Hero.ReleaseExpedition(this);
            _active.Clear();
        }
        private void HandleLevelChanged(int _) => OnStateChanged?.Invoke();
        private void HandleRosterChanged()
        {
            foreach (var mission in _active.ToArray())
                if (mission.Hero == null || !HeroRoster.Contains(mission.Hero)) CancelHero(mission.Hero);
            foreach (var hero in _wounded.Keys.ToArray())
                if (hero == null || !HeroRoster.Contains(hero)) _wounded.Remove(hero);
            OnStateChanged?.Invoke();
        }
        public bool IsMissionUnlocked(ExplorationMissionDefinition mission)
            => mission != null && expeditionBuilding != null && expeditionBuilding.IsAvailable &&
                mission.ZoneLevel <= BuildingLevel;
        public ExpeditionOdds GetOdds(ExplorationMissionDefinition mission, heros hero)
            => ExpeditionRules.GetOdds(hero != null ? hero.Level : 1, mission != null ? mission.ZoneLevel : 1,
                equalLevelSuccess, successPerLevel, criticalShareOfFailures);
        public bool IsHeroAvailable(heros hero) => string.IsNullOrEmpty(GetHeroUnavailableReason(hero));
        public string GetHeroUnavailableReason(heros hero)
        {
            if (hero == null || !HeroRoster.Contains(hero)) return "Choisis un héros recruté.";
            if (!hero.isActiveAndEnabled) return "Héros inactif.";
            if (hero.IsOnExpedition) return "Héros déjà en expédition.";
            if (_wounded.ContainsKey(hero)) return "Héros temporairement indisponible.";
            return "";
        }
        public heros ExhaustRandomHero(int cycles)
        {
            var candidates = HeroRoster.Where(IsHeroAvailable).ToArray();
            if (candidates.Length == 0) return null;
            var hero = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            _wounded[hero] = Mathf.Max(1, cycles);
            OnStateChanged?.Invoke();
            return hero;
        }
        public IEnumerable<(heros hero, ExplorationMissionDefinition mission, int cyclesRemaining)> GetActiveMissions()
            => _active.Where(m => m.Hero != null).Select(m => (m.Hero, m.Definition, m.CyclesRemaining));
        public bool TryStartMission(ExplorationMissionDefinition mission, heros hero, out string reason)
        {
            reason = "";
            if (mission == null || !availableMissions.Contains(mission)) reason = "Mission inconnue.";
            else if (!IsMissionUnlocked(mission)) reason = $"Centre d'expédition niveau {mission.ZoneLevel} requis.";
            else reason = GetHeroUnavailableReason(hero);
            if (reason.Length > 0) return false;
            if (villageManager == null || villageManager.HdvInventory == null)
            { reason = "Stockage de l'hôtel de ville indisponible."; return false; }
            var entry = new ActiveMission { Definition = mission, Hero = hero, HeroName = hero.HeroName,
                Odds = GetOdds(mission, hero), CyclesRemaining = Mathf.Max(1, mission.DurationCycles) };
            if (!hero.TryClaimExpedition(this, mission.DisplayName))
            { reason = "Ce héros n'est plus disponible."; return false; }
            _active.Add(entry);
            OnStateChanged?.Invoke();
            return true;
        }
        public bool CanCancelMission(heros hero) => hero != null && _active.Any(m => m.Hero == hero && !m.Departed);
        public bool TryCancelMission(heros hero)
        {
            if (!CanCancelMission(hero)) return false;
            CancelHero(hero);
            return true;
        }
        public string GetMissionStatus(heros hero) => CanCancelMission(hero)
            ? "Départ en attente · Annulable avant la prochaine défense" : "En expédition · Départ verrouillé";
        private void HandleDefenseStarted(int _)
        {
            if (_defenseInProgress) return;
            _defenseInProgress = true;
            var departing = _active.Where(m => !m.Departed).ToArray();
            // Lock the whole batch before notifying UI/roster observers.
            foreach (var entry in departing)
            {
                entry.Departed = true;
                entry.Odds = GetOdds(entry.Definition, entry.Hero);
            }
            foreach (var entry in departing)
                if (entry.Hero != null) entry.Hero.NotifyExpeditionStateChanged();
            OnStateChanged?.Invoke();
        }
        public void CancelHero(heros hero)
        {
            foreach (var entry in _active.Where(m => m.Hero == hero).ToArray()) _active.Remove(entry);
            if (hero != null) hero.ReleaseExpedition(this);
            OnStateChanged?.Invoke();
        }
        // Une défense complète fait avancer toutes les expéditions d'un cycle.
        private void AdvanceCycle()
        {
            _defenseInProgress = false;
            var storage = villageManager != null ? villageManager.HdvInventory : null;
            if (storage == null) return;
            foreach (var result in _results) Deliver(result, storage);
            foreach (var hero in _wounded.Keys.ToArray())
                if (--_wounded[hero] <= 0) _wounded.Remove(hero);
            foreach (var entry in _active.ToArray())
            {
                if (entry.Hero == null || !HeroRoster.Contains(entry.Hero)) { CancelHero(entry.Hero); continue; }
                if (!entry.Departed || --entry.CyclesRemaining > 0) continue;
                _active.Remove(entry);
                var result = Resolve(entry);
                entry.Hero.ReleaseExpedition(this);
                if (result.Outcome == ExpeditionOutcome.CriticalFailure) recruitedHeroes.RemoveHero(entry.Hero);
                _results.Insert(0, result);
                Deliver(result, storage);
            }
            // Keep undelivered loot even when the visible history is trimmed.
            for (int i = _results.Count - 1; i >= 10; i--)
                if (_results[i].PendingLoot.Count == 0) _results.RemoveAt(i);
            OnStateChanged?.Invoke();
        }
        private ExpeditionResult Resolve(ActiveMission entry)
        {
            var result = new ExpeditionResult { HeroName = entry.HeroName, Mission = entry.Definition,
                Outcome = entry.Odds.Resolve(UnityEngine.Random.value) };
            if (result.Outcome == ExpeditionOutcome.CriticalFailure) return result;
            float factor = result.Outcome == ExpeditionOutcome.Failure ? failedLootFactor : 1f;
            foreach (var loot in entry.Definition.Loot)
            {
                if (loot.item == null || loot.dropChance <= 0 ||
                    (loot.dropChance < 1 && UnityEngine.Random.value >= loot.dropChance)) continue;
                int amount = UnityEngine.Random.Range(Mathf.Max(0, loot.minAmount), Mathf.Max(loot.minAmount, loot.maxAmount) + 1);
                amount = Mathf.RoundToInt(amount * factor);
                if (amount <= 0) continue;
                result.Loot.Add((loot.item, amount)); result.PendingLoot.Add((loot.item, amount));
            }
            return result;
        }
        private static void Deliver(ExpeditionResult result, Inventory storage)
        {
            for (int i = result.PendingLoot.Count - 1; i >= 0; i--)
            {
                var loot = result.PendingLoot[i]; int remaining = storage.Add(loot.item, loot.amount);
                if (remaining <= 0) result.PendingLoot.RemoveAt(i);
                else result.PendingLoot[i] = (loot.item, remaining);
            }
        }
    }
}
