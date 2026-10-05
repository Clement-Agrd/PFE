using System;
using System.Collections.Generic;
using System.Linq;
using Core.InventorySystem;
using Core.WaveSystem;
using UnityEngine;

namespace Core.Village.Exploration
{
    /// <summary>
    /// Le poste d'expédition (GDD §3) : assigne un héros du roster à une mission
    /// dans une zone, fait avancer les missions en cours à chaque défense
    /// survécue, résout succès/échec, et livre le butin à l'HDV — mais pas avant
    /// la fin de la PROCHAINE défense après le retour du héros (§3.1).
    /// Ne simule aucun gameplay de héros (pas de combat, pas de stats) : le
    /// roster n'est qu'une liste de <see cref="HeroDefinition"/> assignables.
    /// </summary>
    public sealed class ExplorationManager : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] private VillageManager villageManager;
        [Tooltip("Fait avancer les missions d'un cycle à chaque nuit survécue.")]
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private List<HeroDefinition> heroRoster = new();
        [SerializeField] private List<ExplorationMissionDefinition> availableMissions = new();

        [Header("Résolution (risque)")]
        [Tooltip("Chance d'échec de base par niveau de risque, avant bonus/malus de classe.")]
        [SerializeField] private float failChanceFaible = 0.10f;
        [SerializeField] private float failChanceMoyen = 0.25f;
        [SerializeField] private float failChanceEleve = 0.45f;
        [Tooltip("Largeur de la bande \"succès partiel\" juste au-dessus du seuil d'échec.")]
        [SerializeField, Range(0f, 1f)] private float partialBand = 0.20f;
        [Tooltip("Multiplicateur de butin en cas de succès partiel.")]
        [SerializeField, Range(0f, 1f)] private float partialLootFactor = 0.5f;

        [Header("Bonus de classe (GDD §3.2)")]
        [SerializeField, Range(0f, 1f)] private float archerForetBonus = 0.25f;
        [SerializeField, Range(0f, 1f)] private float infanterieRiskReduction = 0.30f;
        [SerializeField, Range(0f, 1f)] private float mageRareBonus = 0.25f;

        private sealed class ActiveMission
        {
            public ExplorationMissionDefinition Definition;
            public HeroDefinition Hero;
            public int CyclesRemaining;
        }

        private sealed class PendingReward
        {
            public ExplorationMissionDefinition Definition;
            public HeroDefinition Hero;
            public bool Success;
            public List<(ItemDefinition item, int amount)> Loot;
        }

        private readonly List<ActiveMission> _active = new();
        private readonly List<PendingReward> _pendingRewards = new();
        private readonly Dictionary<HeroDefinition, int> _woundedTicksRemaining = new();

        /// <summary>Missions que le poste peut proposer.</summary>
        public IReadOnlyList<ExplorationMissionDefinition> AvailableMissions => availableMissions;

        /// <summary>Tous les héros du roster (disponibles ou non).</summary>
        public IReadOnlyList<HeroDefinition> HeroRoster => heroRoster;

        /// <summary>Émis chaque fois qu'une mission démarre, avance ou se résout — pour rafraîchir l'UI.</summary>
        public event Action OnStateChanged;

        private void OnEnable()
        {
            if (waveSpawner != null) waveSpawner.OnAllWavesCompleted += AdvanceCycle;
        }

        private void OnDisable()
        {
            if (waveSpawner != null) waveSpawner.OnAllWavesCompleted -= AdvanceCycle;
        }

        public bool IsHeroAvailable(HeroDefinition hero)
        {
            if (hero == null) return false;
            if (_woundedTicksRemaining.ContainsKey(hero)) return false;
            return !_active.Any(m => m.Hero == hero);
        }

        /// <summary>Vue en lecture pour l'UI : missions en cours (héros, mission, cycles restants).</summary>
        public IEnumerable<(HeroDefinition hero, ExplorationMissionDefinition mission, int cyclesRemaining)> GetActiveMissions()
            => _active.Select(m => (m.Hero, m.Definition, m.CyclesRemaining));

        /// <summary>Vue en lecture pour l'UI : butins en attente de la prochaine défense.</summary>
        public IEnumerable<(HeroDefinition hero, ExplorationMissionDefinition mission, bool success)> GetPendingRewards()
            => _pendingRewards.Select(r => (r.Hero, r.Definition, r.Success));

        public bool TryStartMission(ExplorationMissionDefinition mission, HeroDefinition hero, out string reason)
        {
            reason = "";
            if (mission == null || hero == null) { reason = "Mission ou héros invalide."; return false; }
            if (!availableMissions.Contains(mission)) { reason = "Mission inconnue."; return false; }
            if (!heroRoster.Contains(hero)) { reason = "Héros inconnu."; return false; }
            if (!IsHeroAvailable(hero)) { reason = $"{hero.DisplayName} n'est pas disponible."; return false; }

            _active.Add(new ActiveMission
            {
                Definition = mission,
                Hero = hero,
                CyclesRemaining = mission.DurationCycles
            });

            OnStateChanged?.Invoke();
            return true;
        }

        /// <summary>Appelé à chaque défense survécue (une nuit = un cycle).</summary>
        private void AdvanceCycle()
        {
            // 1) Les butins mis de côté LA défense précédente arrivent à l'HDV maintenant.
            Inventory storage = villageManager != null ? villageManager.HdvInventory : null;
            if (storage != null)
            {
                foreach (PendingReward reward in _pendingRewards)
                    foreach (var (item, amount) in reward.Loot)
                        if (item != null && amount > 0) storage.Add(item, amount);
            }
            _pendingRewards.Clear();

            // 2) Les héros blessés récupèrent un cycle de plus avant d'être redisponibles.
            foreach (HeroDefinition hero in _woundedTicksRemaining.Keys.ToList())
            {
                if (--_woundedTicksRemaining[hero] <= 0)
                    _woundedTicksRemaining.Remove(hero);
            }

            // 3) Les missions en cours avancent d'un cycle ; celles qui se terminent
            //    sont résolues et leur butin part dans la file d'attente ci-dessus
            //    (récupéré à la défense SUIVANTE, pas celle-ci).
            foreach (ActiveMission mission in _active.ToList())
            {
                mission.CyclesRemaining--;
                if (mission.CyclesRemaining > 0) continue;

                _pendingRewards.Add(Resolve(mission));
                _active.Remove(mission);
            }

            OnStateChanged?.Invoke();
        }

        private PendingReward Resolve(ActiveMission mission)
        {
            MissionRisk risk = mission.Definition.BaseRisk;
            float baseFail = risk switch
            {
                MissionRisk.Faible => failChanceFaible,
                MissionRisk.Moyen => failChanceMoyen,
                MissionRisk.Eleve => failChanceEleve,
                _ => failChanceFaible
            };

            ExplorationZone zone = mission.Definition.Zone;
            HeroClass cls = mission.Hero.Class;

            // Infanterie : -30% de risque d'échec en Montagne/Grotte (§3.2).
            bool infanterieBonusZone = zone is ExplorationZone.Montagne or ExplorationZone.Grotte;
            float failChance = baseFail * (cls == HeroClass.Infanterie && infanterieBonusZone ? 1f - infanterieRiskReduction : 1f);

            float roll = UnityEngine.Random.value;
            var reward = new PendingReward { Definition = mission.Definition, Hero = mission.Hero, Loot = new List<(ItemDefinition, int)>() };

            if (roll < failChance)
            {
                // Échec : héros blessé (indisponible un cycle de plus), butin nul.
                reward.Success = false;
                _woundedTicksRemaining[mission.Hero] = 1;
                return reward;
            }

            bool partial = roll < failChance + partialBand;
            reward.Success = true;
            float lootFactor = partial ? partialLootFactor : 1f;

            foreach (LootEntry entry in mission.Definition.Loot)
            {
                if (entry.item == null) continue;
                if (UnityEngine.Random.value > entry.dropChance) continue;

                int amount = UnityEngine.Random.Range(entry.minAmount, entry.maxAmount + 1);

                // Archer : +25% de ressources en Forêt. Mage : +25% sur les ressources rares en Grotte.
                if (cls == HeroClass.Archer && zone == ExplorationZone.Foret)
                    amount = Mathf.RoundToInt(amount * (1f + archerForetBonus));
                if (cls == HeroClass.Mage && zone == ExplorationZone.Grotte && entry.isRare)
                    amount = Mathf.RoundToInt(amount * (1f + mageRareBonus));

                amount = Mathf.RoundToInt(amount * lootFactor);
                if (amount > 0) reward.Loot.Add((entry.item, amount));
            }

            return reward;
        }
    }
}
