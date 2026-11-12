using System;
using UnityEngine;

namespace Core.BarracksSystem
{
    public enum ClasseHeros { Tank, Degats, Invocateur, ArcLong, ArcCourt, Arbalete, ChamanFeu, ChamanGlace, ChamanVie }
    public enum HeroFamily { Barracks, Archer, Chaman }
    public enum TypeCaserne { Standard, Tank, Degats, Invocateur, ArcLong, ArcCourt, Arbalete, ChamanFeu, ChamanGlace, ChamanVie }

    [DisallowMultipleComponent]
    [AddComponentMenu("Caserne/Héros")]
    public sealed class heros : MonoBehaviour
    {
        [SerializeField] private string heroName = "Héros";
        [SerializeField] private ClasseHeros classe;
        [SerializeField, Range(1, 5)] private int level = 1;

        [SerializeField, Min(0)] private int experience;
        [SerializeField, Min(1)] private int baseExperiencePerLevel = 100;
        public bool IsMaxLevel => Level >= 5;
        public int Experience => IsMaxLevel ? 0 : Mathf.Max(0, experience);
        public int ExperienceToNextLevel => IsMaxLevel ? 0 : Mathf.Max(1, baseExperiencePerLevel) * Level;
        public string ExperienceLabel => IsMaxLevel ? "Niveau maximum" : $"XP {Experience} / {ExperienceToNextLevel}";
        private bool _notifyPending;

        public string HeroName => string.IsNullOrWhiteSpace(heroName) ? ClasseName : heroName;
        public ClasseHeros Classe => classe;
        public int Level => Mathf.Clamp(level, 1, 5);
        public static HeroFamily FamilyOf(ClasseHeros type) => type switch
        {
            ClasseHeros.ArcLong or ClasseHeros.ArcCourt or ClasseHeros.Arbalete => HeroFamily.Archer,
            ClasseHeros.ChamanFeu or ClasseHeros.ChamanGlace or ClasseHeros.ChamanVie => HeroFamily.Chaman,
            _ => HeroFamily.Barracks
        };

        public HeroFamily Family => FamilyOf(classe);
        public TowerHeroStation AssignedTower { get; private set; }
        public MonoBehaviour DefenseAssignment => AssignedTower != null ? AssignedTower : AssignedBarracks;
        public Barracks AssignedBarracks { get; private set; }
        public Core.Village.Exploration.ExplorationManager AssignedExpedition { get; private set; }
        public string ExpeditionName { get; private set; }
        public bool IsOnExpedition => AssignedExpedition != null;
        public bool IsExpeditionPending => IsOnExpedition && AssignedExpedition.CanCancelMission(this);
        public bool HasAssignment => DefenseAssignment != null || IsOnExpedition;
        internal void NotifyExpeditionStateChanged() => Changed?.Invoke();
        public string ClasseName => ClassName(classe);
        public static string ClassName(ClasseHeros type) => type switch
        {
            ClasseHeros.Tank => "Tank",
            ClasseHeros.Degats => "Dégâts",
            ClasseHeros.Invocateur => "Invocateur",
            ClasseHeros.ArcLong => "Arc long",
            ClasseHeros.ArcCourt => "Arc court",
            ClasseHeros.Arbalete => "Arbalète",
            ClasseHeros.ChamanFeu => "Feu",
            ClasseHeros.ChamanGlace => "Glace",
            ClasseHeros.ChamanVie => "Vie",
            _ => "Héros"
        };

        public event Action Changed;
        public event Action<heros> Destroyed;

        public void Initialize(string displayName, ClasseHeros heroClass, int heroLevel)
        {
            if (DefenseAssignment != null || IsOnExpedition) throw new InvalidOperationException("Un héros affecté ne peut pas être réinitialisé.");
            heroName = displayName;
            classe = heroClass;
            level = Mathf.Clamp(heroLevel, 1, 5);
            experience = 0;
            Changed?.Invoke();
        }

        public void SetLevel(int value)
        {
            int next = Mathf.Clamp(value, 1, 5);
            if (level == next) return;
            level = next;
            experience = 0;
            Changed?.Invoke();
        }

        public int AddExperience(int amount)
        {
            if (amount <= 0 || IsMaxLevel) return 0;
            long remaining = 0;
            for (int next = Level; next < 5; next++) remaining += (long)Mathf.Max(1, baseExperiencePerLevel) * next;
            remaining -= Experience;
            int gained = (int)Math.Min(amount, Math.Max(0, remaining));
            long total = (long)Experience + gained;
            while (!IsMaxLevel && total >= ExperienceToNextLevel)
            {
                total -= ExperienceToNextLevel;
                level = Level + 1;
            }
            experience = IsMaxLevel ? 0 : (int)total;
            if (gained > 0) Changed?.Invoke();
            return gained;
        }

        internal bool TryClaim(Barracks barracks)
        {
            if (barracks == null || Family != HeroFamily.Barracks || AssignedTower != null || !isActiveAndEnabled || (AssignedBarracks != null && AssignedBarracks != barracks)) return false;
            if (IsOnExpedition && !AssignedExpedition.TryCancelMission(this)) return false;
            if (AssignedBarracks != barracks)
            {
                AssignedBarracks = barracks;
                Changed?.Invoke();
            }
            return true;
        }

        internal void Release(Barracks barracks)
        {
            if (AssignedBarracks == barracks)
            {
                AssignedBarracks = null;
                Changed?.Invoke();
            }
        }

        internal bool TryClaimTower(TowerHeroStation tower, bool allowTransfer)
        {
            if (tower == null || AssignedBarracks != null || !isActiveAndEnabled) return false;
            var previousTower = AssignedTower != tower ? AssignedTower : null;
            if (previousTower != null && !allowTransfer) return false;
            if (IsOnExpedition && !AssignedExpedition.TryCancelMission(this)) return false;
            if (previousTower != null) previousTower.ClearHero();
            AssignedTower = tower;
            Changed?.Invoke();
            return true;
        }

        internal void ReleaseTower(TowerHeroStation tower)
        {
            if (AssignedTower != tower) return;
            AssignedTower = null;
            Changed?.Invoke();
        }

        internal bool TryClaimExpedition(Core.Village.Exploration.ExplorationManager owner, string missionName)
        {
            if (owner == null || !isActiveAndEnabled || IsOnExpedition) return false;
            AssignedExpedition = owner;
            ExpeditionName = missionName;
            if (AssignedBarracks != null) AssignedBarracks.AssignHero(null);
            if (AssignedTower != null) AssignedTower.ClearHero();
            Changed?.Invoke();
            return true;
        }

        internal void ReleaseExpedition(Core.Village.Exploration.ExplorationManager owner)
        {
            if (AssignedExpedition != owner) return;
            AssignedExpedition = null;
            ExpeditionName = null;
            Changed?.Invoke();
        }

        private void OnDisable()
        {
            if (AssignedBarracks != null) AssignedBarracks.AssignHero(null);
            if (AssignedTower != null) AssignedTower.ClearHero();
            if (AssignedExpedition != null) AssignedExpedition.CancelHero(this);
        }

        private void OnDestroy() => Destroyed?.Invoke(this);

        private void OnValidate()
        {
            level = Mathf.Clamp(level, 1, 5);
            baseExperiencePerLevel = Mathf.Clamp(baseExperiencePerLevel, 1, 1000000);
            experience = IsMaxLevel ? 0 : Mathf.Clamp(experience, 0, ExperienceToNextLevel - 1);
            // Inspector edits are applied on the main thread in the next Update.
            _notifyPending = true;
        }

        private void Update()
        {
            if (!_notifyPending) return;
            _notifyPending = false;
            Changed?.Invoke();
        }
    }
}
