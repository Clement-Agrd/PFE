using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class HeroRoster : MonoBehaviour
    {
        [SerializeField] private Core.Village.RecruitmentCamp recruitmentCamp;
        public int MaxHeroes => recruitmentCamp != null ? recruitmentCamp.Capacity : int.MaxValue;
        public bool HasRecruitmentSpace => Count < MaxHeroes;
        [Header("Expérience de défense")]
        [SerializeField, Min(0)] private int experiencePerWaveNumber = 20;
        private Core.WaveSystem.WaveSpawner _experienceSpawner;
        private readonly Dictionary<heros, MonoBehaviour> _defenders = new();
        private int _rewardWave;
        private bool _rewardPending;
        private readonly List<string> _lastWaveExperience = new();
        public IReadOnlyList<string> LastWaveExperience => _lastWaveExperience;
        public int DefenseExperience(int waveNumber) => waveNumber <= 0 ? 0 :
            (int)Math.Min(int.MaxValue, (long)Mathf.Max(0, experiencePerWaveNumber) * waveNumber);
        private void OnEnable()
        {
            if(recruitmentCamp!=null)
            {
                recruitmentCamp.CapacityChanged+=HandleHeroChanged;
                _experienceSpawner = recruitmentCamp.Spawner;
            }
            if (_experienceSpawner != null)
            {
                _experienceSpawner.OnWaveStarted += CaptureDefenders;
                _experienceSpawner.OnWaveCompleted += RewardDefenders;
            }
        }
        private void OnDisable()
        {
            if(recruitmentCamp!=null)recruitmentCamp.CapacityChanged-=HandleHeroChanged;
            if (_experienceSpawner != null)
            {
                _experienceSpawner.OnWaveStarted -= CaptureDefenders;
                _experienceSpawner.OnWaveCompleted -= RewardDefenders;
            }
            _defenders.Clear(); _rewardPending = false;
        }
        private void CaptureDefenders(int wave)
        {
            _lastWaveExperience.Clear();
            _defenders.Clear(); _rewardWave = wave; _rewardPending = true;
            foreach (var hero in _heroes)
                if (hero != null && hero.isActiveAndEnabled && hero.DefenseAssignment != null && hero.DefenseAssignment.isActiveAndEnabled)
                    _defenders[hero] = hero.DefenseAssignment;
        }
        private void RewardDefenders(int wave)
        {
            if (!_rewardPending || wave != _rewardWave || _experienceSpawner == null || _experienceSpawner.IsDefeated) return;
            _rewardPending = false;
            foreach (var entry in _defenders.ToArray())
            {
                var hero = entry.Key;
                if (hero == null || !_heroes.Contains(hero) || !hero.isActiveAndEnabled || hero.IsOnExpedition ||
                    entry.Value == null || !entry.Value.isActiveAndEnabled || hero.DefenseAssignment != entry.Value) continue;
                int before = hero.Level;
                int gained = hero.AddExperience(DefenseExperience(wave));
                _lastWaveExperience.Add($"{hero.HeroName} · Défense : +{gained} XP · " +
                    (hero.Level > before ? $"Niveau {before} → {hero.Level}" : $"Niveau {hero.Level}") + $" · {hero.ExperienceLabel}");
            }
            _defenders.Clear();
        }

        [Header("Debug — recrutement")]
        [SerializeField] private bool enableDebugRecruitment = true;
        [SerializeField] private Key debugRecruitKey = Key.F8;

        private static readonly string[] FirstNames =
        {
            "Clément", "Maxence", "Naomi", "Chloé", "Flavio", "Brandon", "Florian", "Alexis",
            "Antonin", "Juliette", "Léna", "Magalie", "Claude", "Joe", "Adrien", "Quentin",
            "Adam", "Antoine", "Xavier", "Valentin", "Chris", "Mathieu", "Zoé", "Justine",
            "Raphaël", "Lola", "Manon", "Ines", "Ornella", "Martin", "Jules", "Héloïse",
            "Julie", "Amandine", "Marie", "Emma", "Lucie", "Léo", "Ambre", "Jade",
            "Sophia", "Aymeric", "Enzo", "Louis", "Pierre", "Louise", "Arnaud", "Jeremy",
            "Alice", "Gabriel", "Hugo", "Arthur", "Lucas", "Nathan", "Paul", "Victor",
            "Oscar", "Ethan", "Noah", "Sacha", "Théo", "Tom", "Baptiste", "Maël",
            "Malo", "Samuel", "Simon", "Romain", "Thomas", "Nicolas", "Alexandre", "Benjamin",
            "Émile", "Augustin", "Eliott", "Noé", "Camille", "Charlotte", "Sarah", "Léa",
            "Clara", "Anna", "Eva", "Mia", "Rose", "Romane", "Agathe", "Margot",
            "Jeanne", "Iris", "Élise", "Adèle", "Célia", "Anaïs", "Émilie", "Mathilde",
            "Nina", "Olivia", "Axel", "Valentine",
        };

        private readonly List<heros> _heroes = new();
        private int _nextId = 1;

        public IReadOnlyList<heros> Heroes => _heroes;
        public IEnumerable<heros> OrderedHeroes => _heroes.OrderBy(h => h == null || h.HasAssignment);
        public int Count => _heroes.Count;
        public Key DebugRecruitKey => debugRecruitKey;
        public event Action Changed;

        private void Update()
        {
            if (enableDebugRecruitment && Keyboard.current != null && Keyboard.current[debugRecruitKey].wasPressedThisFrame)
                RecruitDebugHero();
        }

        public heros RecruitDebugHero()
            => Recruit((ClasseHeros)UnityEngine.Random.Range(0, Enum.GetValues(typeof(ClasseHeros)).Length), 1);

        public heros Recruit(ClasseHeros classe, int level)
        {
            if (!Enum.IsDefined(typeof(ClasseHeros), classe))
                throw new ArgumentOutOfRangeException(nameof(classe));
            if (!HasRecruitmentSpace) return null;
            // Prefer a name not already used by a hero in this village.
            var availableNames = FirstNames.Where(name => !_heroes.Any(h => h != null && h.HeroName == name)).ToArray();
            var names = availableNames.Length > 0 ? availableNames : FirstNames;
            string firstName = names[UnityEngine.Random.Range(0, names.Length)];
            string label = heros.ClassName(classe);
            var go = new GameObject($"{firstName} · {label} #{_nextId++}");
            go.transform.SetParent(transform, false);
            var hero = go.AddComponent<heros>();
            hero.Initialize(firstName, classe, level);
            _heroes.Add(hero);
            hero.Changed += HandleHeroChanged;
            hero.Destroyed += RemoveDestroyedHero;
            Changed?.Invoke();
            return hero;
        }

        public bool AssignToBarracks(heros hero, Barracks barracks)
            => barracks != null && (hero == null || _heroes.Contains(hero)) && barracks.AssignHero(hero);

        public bool RemoveHero(heros hero)
        {
            if (hero == null || !_heroes.Remove(hero)) return false;
            hero.Changed -= HandleHeroChanged;
            hero.Destroyed -= RemoveDestroyedHero;
            if (hero.AssignedBarracks != null) hero.AssignedBarracks.AssignHero(null);
            if (hero.AssignedTower != null) hero.AssignedTower.ClearHero();
            if (hero.AssignedExpedition != null) hero.AssignedExpedition.CancelHero(hero);
            Changed?.Invoke();
            Destroy(hero.gameObject);
            return true;
        }

        private void HandleHeroChanged() => Changed?.Invoke();

        private void RemoveDestroyedHero(heros hero)
        {
            hero.Changed -= HandleHeroChanged;
            hero.Destroyed -= RemoveDestroyedHero;
            if (_heroes.Remove(hero)) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            foreach (var hero in _heroes)
            {
                if (hero == null) continue;
                hero.Changed -= HandleHeroChanged;
                hero.Destroyed -= RemoveDestroyedHero;
            }
        }
    }
}
