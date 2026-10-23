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
        private void OnEnable(){if(recruitmentCamp!=null)recruitmentCamp.CapacityChanged+=HandleHeroChanged;}
        private void OnDisable(){if(recruitmentCamp!=null)recruitmentCamp.CapacityChanged-=HandleHeroChanged;}

        [Header("Debug — recrutement")]
        [SerializeField] private bool enableDebugRecruitment = true;
        [SerializeField] private Key debugRecruitKey = Key.F8;

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
            => Recruit((ClasseHeros)UnityEngine.Random.Range(0, 3), 1);

        public heros Recruit(ClasseHeros classe, int level)
        {
            if ((int)classe < 0 || (int)classe > 2)
                throw new ArgumentOutOfRangeException(nameof(classe));
            if (!HasRecruitmentSpace) return null;
            string label = classe == ClasseHeros.Degats ? "Dégâts" : classe.ToString();
            var go = new GameObject($"Héros {label} #{_nextId++}");
            go.transform.SetParent(transform, false);
            var hero = go.AddComponent<heros>();
            hero.Initialize(go.name, classe, level);
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
