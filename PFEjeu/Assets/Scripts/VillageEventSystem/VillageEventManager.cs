using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Village;
using Core.Village.Exploration;
using Core.TowerDefense;
using Core.HealthSystem;
using Core.WaveSystem;

namespace Core.VillageEvents
{
    /// <summary>
    /// Moteur des événements post-défense : à la fin d'une nuit, lit l'état du village,
    /// filtre les fiches par conditions, tire un dilemme pondéré, l'affiche et applique le choix.
    /// </summary>
    public sealed class VillageEventManager : MonoBehaviour
    {
        [Header("Références (retrouvées automatiquement si vides)")]
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private VillageManager villageManager;
        [SerializeField] private PlayerBase playerBase;
        [SerializeField] private ExplorationManager explorationManager;
        [SerializeField] private VillageState state;
        [SerializeField] private VillageEventUI ui;

        [Header("Catalogue")]
        [SerializeField] private List<VillageEventDefinition> catalog = new();

        [Header("Cadence & équilibrage")]
        [Tooltip("Délai (s réelles) entre la fin de la défense et l'apparition du dilemme.")]
        [SerializeField, Min(0f)] private float delayAfterDefense = 2f;
        [Tooltip("Escalade : +X par nuit de rituel sur les montants (0.1 = +10 % par nuit).")]
        [SerializeField, Min(0f)] private float escalationPerNight = 0.1f;
        [Tooltip("Anti-snowball : un choix qui donne cette ressource est bloqué si le stock la dépasse déjà.")]
        [SerializeField, Min(1)] private int resourceStockCap = 500;

        [Header("Aléatoire")]
        [Tooltip("Probabilité (%) qu'un événement survienne après une vague. 100 = à chaque fois.")]
        [SerializeField, Range(0, 100)] private int eventChancePercent = 100;
        [Tooltip("Variation aléatoire (± %) appliquée aux montants (ressources, remparts, ennemis) à chaque apparition.")]
        [SerializeField, Range(0, 50)] private int amountJitterPercent = 15;
        [Tooltip("Les événements ciblés sur la situation du village (remparts bas, stock vide, bâtiment précis…) " +
                 "sont plus probables : poids × (1 + bonus × nombre de conditions de situation).")]
        [SerializeField, Min(0f)] private float situationBonusPerCondition = 0.5f;

        private struct Scheduled { public VillageEventDefinition def; public int dueNight; }

        private readonly Dictionary<VillageEventDefinition, int> _lastSeenNight = new();
        private readonly HashSet<VillageEventDefinition> _usedUnique = new();
        private readonly List<Scheduled> _scheduled = new();
        private int _nightIndex;
        private bool _nightActive;
        private VillageEventDefinition _current;

        public float EscalationMultiplier => 1f + escalationPerNight * ((state != null ? state.RitualNight : 1) - 1);

        private void Awake()
        {
            if (waveSpawner == null) waveSpawner = FindFirstObjectByType<WaveSpawner>();
            if (villageManager == null) villageManager = FindFirstObjectByType<VillageManager>();
            if (playerBase == null) playerBase = FindFirstObjectByType<PlayerBase>();
            if (explorationManager == null) explorationManager = FindFirstObjectByType<ExplorationManager>();
            if (state == null) state = FindFirstObjectByType<VillageState>();
            if (ui == null) ui = FindFirstObjectByType<VillageEventUI>();
            if (state == null) state = gameObject.AddComponent<VillageState>();
            if (ui == null) ui = gameObject.AddComponent<VillageEventUI>();
        }

        private void OnEnable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted += HandleWaveStarted;
            // OnDefenseCompleted = "la défense est finie, retour au village" : c'est le signal utilisé par le reste
            // du jeu (exploration, production, herse…). Il part après CHAQUE vague quand le village s'intercale,
            // et à la fin de toutes les vagues sinon. (OnAllWavesCompleted ne part qu'à la toute dernière vague.)
            waveSpawner.OnDefenseCompleted += HandleNightEnded;
            waveSpawner.OnEnemySpawned += HandleEnemySpawned;
        }

        private void OnDisable()
        {
            if (waveSpawner == null) return;
            waveSpawner.OnWaveStarted -= HandleWaveStarted;
            waveSpawner.OnDefenseCompleted -= HandleNightEnded;
            waveSpawner.OnEnemySpawned -= HandleEnemySpawned;
        }

        // ------------------------------------------------------------ nuit

        private void HandleWaveStarted(int wave)
        {
            if (_nightActive) return;
            _nightActive = true;
            state.BeginNight();
        }

        private void HandleEnemySpawned(GameObject enemy, SpawnZone zone)
        {
            int bonus = state.ActiveEnemyBonusPercent;
            if (bonus != 0) StartCoroutine(BuffEnemyNextFrame(enemy, bonus));
        }

        // Le spawner initialise l'ennemi après l'événement : on attend une frame pour ne pas être écrasé.
        private IEnumerator BuffEnemyNextFrame(GameObject enemy, int percent)
        {
            yield return null;
            if (enemy == null || !enemy.TryGetComponent(out Health health)) yield break;
            health.SetMaxHealth(Mathf.Max(1, Mathf.RoundToInt(health.MaxHealth * (1f + percent / 100f))), true);
        }

        private void HandleNightEnded()
        {
            _nightActive = false;
            _nightIndex++;
            state.EndNight();
            foreach (Building b in FindObjectsByType<Building>(FindObjectsSortMode.None)) b.TickUnavailable();
            StartCoroutine(ShowEventAfterDelay());
        }

        private IEnumerator ShowEventAfterDelay()
        {
            yield return new WaitForSecondsRealtime(delayAfterDefense);
            if (ui != null && ui.IsOpen) yield break; // un dilemme est déjà à l'écran
            // Une suite différée arrivée à échéance n'est jamais perdue au jet de dé.
            if (!HasDueFollowUp() && Random.value * 100f >= eventChancePercent) yield break;
            VillageEventDefinition def = PickEvent();
            if (def != null) Present(def);
        }

        // ------------------------------------------------------------ sélection

        /// <summary>Public pour tests / debug : tire un événement selon l'état actuel.</summary>
        public VillageEventDefinition PickEvent()
        {
            // Une suite différée arrivée à échéance passe avant le tirage.
            for (int i = 0; i < _scheduled.Count; i++)
            {
                if (_scheduled[i].dueNight > _nightIndex) continue;
                VillageEventDefinition due = _scheduled[i].def;
                _scheduled.RemoveAt(i);
                if (due != null) return due;
                i--;
            }

            List<VillageEventDefinition> pool = catalog.Where(IsEligible).ToList();
            if (pool.Count == 0) return null;

            float total = pool.Sum(EffectiveWeight);
            float roll = Random.value * total;
            foreach (VillageEventDefinition e in pool)
            {
                roll -= EffectiveWeight(e);
                if (roll <= 0f) return e;
            }
            return pool[^1];
        }

        /// <summary>Poids de tirage : plus un événement colle précisément à la situation du village, plus il est probable.</summary>
        private float EffectiveWeight(VillageEventDefinition e)
            => e.Weight * (1f + situationBonusPerCondition * e.Conditions.Specificity);

        private bool HasDueFollowUp()
        {
            foreach (Scheduled s in _scheduled)
                if (s.dueNight <= _nightIndex && s.def != null) return true;
            return false;
        }

        private bool IsEligible(VillageEventDefinition e)
        {
            if (e == null) return false;
            if (e.Unique && _usedUnique.Contains(e)) return false;
            if (_lastSeenNight.TryGetValue(e, out int last) && _nightIndex - last < e.CooldownNights) return false;
            return ConditionsMet(e.Conditions);
        }

        private bool ConditionsMet(EventConditions c)
        {
            if (c.minTownHallLevel > 0 && (villageManager == null || villageManager.TownHallLevel < c.minTownHallLevel)) return false;

            if (c.wallsBelowPercent > 0)
            {
                if (playerBase == null || WallPercent() >= c.wallsBelowPercent) return false;
            }

            if (c.stockItem != null && c.stockBelow > 0)
            {
                if (villageManager == null || villageManager.HdvInventory == null) return false;
                if (villageManager.HdvInventory.Count(c.stockItem) >= c.stockBelow) return false;
            }

            if (c.requiredBuildings != null)
            {
                for (int i = 0; i < c.requiredBuildings.Length; i++)
                {
                    BuildingDefinition def = c.requiredBuildings[i];
                    if (def == null) continue;
                    Building b = FindBuilding(def);
                    if (b == null) return false;
                    if (i == 0 && c.requiredBuildingMinLevel > 0 && b.CurrentLevel < c.requiredBuildingMinLevel) return false;
                }
            }

            if (c.wallsAbovePercent > 0)
            {
                if (playerBase == null || WallPercent() <= c.wallsAbovePercent) return false;
            }

            if (c.stockAboveItem != null && c.stockAbove > 0)
            {
                if (villageManager == null || villageManager.HdvInventory == null) return false;
                if (villageManager.HdvInventory.Count(c.stockAboveItem) <= c.stockAbove) return false;
            }

            if (c.minBuildingCount > 0 && FindObjectsByType<Building>(FindObjectsSortMode.None).Length < c.minBuildingCount) return false;
            if (c.minHeroes > 0 && (explorationManager == null || explorationManager.HeroRoster.Count < c.minHeroes)) return false;
            if (c.maxCorruptionTier > 0 && state.CorruptionTier > c.maxCorruptionTier) return false;

            if (state.RitualNight < c.minNight) return false;
            if (c.maxNight > 0 && state.RitualNight > c.maxNight) return false;
            if (state.CorruptionTier < c.minCorruptionTier) return false;
            return true;
        }

        private float WallPercent() => playerBase.MaxHealth > 0 ? 100f * playerBase.Health / playerBase.MaxHealth : 100f;

        private static Building FindBuilding(BuildingDefinition def)
            => FindObjectsByType<Building>(FindObjectsSortMode.None).FirstOrDefault(b => b.Definition == def);

        // ------------------------------------------------------------ présentation

        public void Present(VillageEventDefinition def)
        {
            if (def == null || ui == null) return;
            _current = def;
            _lastSeenNight[def] = _nightIndex;
            if (def.Unique) _usedUnique.Add(def);

            // Les effets sont "résolus" une seule fois à l'apparition (bâtiment aléatoire, variation des montants) :
            // ce que le joueur lit est exactement ce qui sera appliqué.
            float baseMult = EscalationMultiplier;
            _resolved = new List<ResolvedEffect>[def.Choices.Count];
            var views = new List<VillageEventUI.ChoiceView>();
            for (int i = 0; i < def.Choices.Count; i++)
            {
                EventChoice choice = def.Choices[i];
                _resolved[i] = ResolveChoice(choice, baseMult, out string unresolvable);
                string reason = unresolvable ?? BlockReason(_resolved[i]);
                views.Add(new VillageEventUI.ChoiceView
                {
                    label = choice.label,
                    effects = _resolved[i].Select(r => (r.effect.Describe(r.mult), r.effect.IsCost)).ToList(),
                    hasFollowUp = choice.followUp != null,
                    blockedReason = reason
                });
            }

            ui.Show(def.Title, def.PickFlavor(), state.RitualNight, views, index => Resolve(def.Choices[index], index));
        }

        private struct ResolvedEffect { public EventEffect effect; public float mult; }
        private List<ResolvedEffect>[] _resolved;

        private List<ResolvedEffect> ResolveChoice(EventChoice choice, float baseMult, out string unresolvable)
        {
            unresolvable = null;
            var list = new List<ResolvedEffect>();
            if (choice.effects == null) return list;

            float j = amountJitterPercent / 100f;
            foreach (EventEffect src in choice.effects)
            {
                EventEffect e = src;

                if (e.randomBuilding && (e.type == EffectType.BuildingDowntime || e.type == EffectType.BuildingLevelUp))
                {
                    Building pick = PickRandomBuilding(e.type == EffectType.BuildingLevelUp);
                    if (pick == null) { unresolvable = "Aucun bâtiment concerné."; continue; }
                    e.building = pick.Definition;
                }

                float mult = baseMult;
                if (j > 0f && e.type is EffectType.Resource or EffectType.WallRepair or EffectType.NextNightEnemies)
                    mult *= 1f + Random.Range(-j, j);

                list.Add(new ResolvedEffect { effect = e, mult = mult });
            }
            return list;
        }

        /// <summary>Bâtiment tiré au hasard (hors hôtel de ville) : améliorable pour un gain, disponible pour un coût.</summary>
        private Building PickRandomBuilding(bool forLevelUp)
        {
            var candidates = FindObjectsByType<Building>(FindObjectsSortMode.None)
                .Where(b => b.Definition != null
                            && (villageManager == null || !villageManager.IsTownHall(b))
                            && (forLevelUp ? !b.IsMaxLevel : b.IsAvailable))
                .ToList();
            return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
        }

        private string BlockReason(List<ResolvedEffect> effects)
        {
            foreach (ResolvedEffect r in effects)
            {
                EventEffect e = r.effect;
                int amount = e.Scaled(r.mult);
                switch (e.type)
                {
                    case EffectType.Resource when e.item != null && villageManager != null && villageManager.HdvInventory != null:
                        int stock = villageManager.HdvInventory.Count(e.item);
                        if (amount < 0 && stock < -amount) return $"Pas assez de {e.item.DisplayName}.";
                        if (amount > 0 && stock >= resourceStockCap) return $"Stock de {e.item.DisplayName} déjà plein.";
                        break;
                    case EffectType.CorruptionTier when amount > 0 && state.CorruptionAtMax:
                        return "Corruption au maximum.";
                    case EffectType.BuildingDowntime or EffectType.BuildingLevelUp when e.building != null:
                        if (FindBuilding(e.building) == null) return $"{e.building.DisplayName} absent du village.";
                        break;
                }
            }
            return null;
        }

        // ------------------------------------------------------------ résolution

        private void Resolve(EventChoice choice, int index)
        {
            if (_resolved != null && index >= 0 && index < _resolved.Length)
                foreach (ResolvedEffect r in _resolved[index]) Apply(r.effect, r.mult);

            if (choice.followUp != null)
                _scheduled.Add(new Scheduled { def = choice.followUp, dueNight = _nightIndex + choice.followUpDelayNights });

            _current = null;
        }

        private void Apply(EventEffect e, float mult)
        {
            int amount = e.Scaled(mult);
            switch (e.type)
            {
                case EffectType.Resource:
                    var inv = villageManager != null ? villageManager.HdvInventory : null;
                    if (inv == null || e.item == null) break;
                    if (amount > 0) inv.Add(e.item, amount);
                    else if (amount < 0) inv.Remove(e.item, -amount);
                    break;

                case EffectType.WallRepair:
                    if (playerBase != null)
                        playerBase.Repair(Mathf.RoundToInt(playerBase.MaxHealth * amount / 100f));
                    break;

                case EffectType.BuildingDowntime:
                    FindBuilding(e.building)?.MakeUnavailable(amount);
                    break;

                case EffectType.BuildingLevelUp:
                    Building b = FindBuilding(e.building);
                    if (b != null && !b.IsMaxLevel) b.SetLevel(b.CurrentLevel + 1);
                    break;

                case EffectType.HeroUnavailable:
                    explorationManager?.ExhaustRandomHero(amount);
                    break;

                case EffectType.RitualAdvance:
                    state.AdvanceRitual(amount);
                    break;

                case EffectType.NextNightEnemies:
                    state.AddNextNightEnemyBonus(amount);
                    break;

                case EffectType.CorruptionTier:
                    state.AddCorruption(amount);
                    break;
            }
        }

        // ------------------------------------------------------------ debug

        public IReadOnlyList<VillageEventDefinition> Catalog => catalog;
        public VillageState State => state;
        public PlayerBase Base => playerBase;
        public VillageManager Village => villageManager;
        public int NightIndex => _nightIndex;

        public bool IsEventOpen => ui != null && ui.IsOpen;

        /// <summary>
        /// Présente un événement au hasard (bouton de test). Sans 'ignoreConditions', tire parmi les fiches admissibles ;
        /// s'il n'y en a aucune, retombe sur le catalogue entier pour ne jamais rester sans réaction.
        /// </summary>
        public void DebugPresentRandom(bool ignoreConditions)
        {
            if (IsEventOpen) return;
            VillageEventDefinition def = null;
            if (!ignoreConditions) def = PickEvent();
            if (def == null)
            {
                var all = catalog.Where(e => e != null).ToList();
                if (all.Count == 0) { Debug.LogWarning("[VillageEvents] Catalogue vide."); return; }
                def = all[Random.Range(0, all.Count)];
            }
            Present(def);
        }

        /// <summary>Force l'affichage d'un événement précis (ignore conditions et cooldown).</summary>
        public void DebugForce(VillageEventDefinition def) => Present(def);

        /// <summary>Simule une fin de nuit complète (tick, rituel +1, tirage normal).</summary>
        public void DebugSimulateNightEnd()
        {
            _nightActive = true;
            HandleNightEnded();
        }

        /// <summary>Oublie cooldowns, uniques et suites programmées.</summary>
        public void DebugResetHistory()
        {
            _lastSeenNight.Clear();
            _usedUnique.Clear();
            _scheduled.Clear();
        }

        public string DebugEligibilityReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (VillageEventDefinition e in catalog)
                if (e != null) sb.AppendLine($"{(IsEligible(e) ? "OK " : "-- ")}{e.Title}");
            if (_scheduled.Count > 0) sb.AppendLine($"Suites programmées : {_scheduled.Count}");
            return sb.ToString();
        }

        [ContextMenu("Debug/Présenter un événement")]
        private void DebugPresent()
        {
            VillageEventDefinition def = PickEvent() ?? (catalog.Count > 0 ? catalog[0] : null);
            if (def != null) Present(def);
        }
    }
}
