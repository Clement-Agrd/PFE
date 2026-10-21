using System;
using UnityEngine;

namespace Core.BarracksSystem
{
    public enum ClasseHeros { Tank, Degats, Invocateur }
    public enum TypeCaserne { Standard, Tank, Degats, Invocateur }

    [DisallowMultipleComponent]
    [AddComponentMenu("Caserne/Héros")]
    public sealed class heros : MonoBehaviour
    {
        [SerializeField] private string heroName = "Héros";
        [SerializeField] private ClasseHeros classe;
        [SerializeField, Range(1, 5)] private int level = 1;

        private bool _notifyPending;

        public string HeroName => string.IsNullOrWhiteSpace(heroName) ? ClasseName : heroName;
        public ClasseHeros Classe => classe;
        public int Level => Mathf.Clamp(level, 1, 5);
        public Barracks AssignedBarracks { get; private set; }
        public Core.Village.Exploration.ExplorationManager AssignedExpedition { get; private set; }
        public string ExpeditionName { get; private set; }
        public bool IsOnExpedition => AssignedExpedition != null;
        public bool IsExpeditionPending => IsOnExpedition && AssignedExpedition.CanCancelMission(this);
        public bool HasAssignment => AssignedBarracks != null || IsOnExpedition;
        internal void NotifyExpeditionStateChanged() => Changed?.Invoke();
        public string ClasseName => classe switch
        {
            ClasseHeros.Tank => "Tank",
            ClasseHeros.Degats => "Dégâts",
            ClasseHeros.Invocateur => "Invocateur",
            _ => "Héros"
        };

        public event Action Changed;
        public event Action<heros> Destroyed;

        public void Initialize(string displayName, ClasseHeros heroClass, int heroLevel)
        {
            if (AssignedBarracks != null || IsOnExpedition) throw new InvalidOperationException("Un héros affecté ne peut pas être réinitialisé.");
            heroName = displayName;
            classe = heroClass;
            level = Mathf.Clamp(heroLevel, 1, 5);
            Changed?.Invoke();
        }

        public void SetLevel(int value)
        {
            int next = Mathf.Clamp(value, 1, 5);
            if (level == next) return;
            level = next;
            Changed?.Invoke();
        }

        internal bool TryClaim(Barracks barracks)
        {
            if (barracks == null || !isActiveAndEnabled || (AssignedBarracks != null && AssignedBarracks != barracks)) return false;
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

        internal bool TryClaimExpedition(Core.Village.Exploration.ExplorationManager owner, string missionName)
        {
            if (owner == null || !isActiveAndEnabled || IsOnExpedition) return false;
            AssignedExpedition = owner;
            ExpeditionName = missionName;
            if (AssignedBarracks != null) AssignedBarracks.AssignHero(null);
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
            if (AssignedExpedition != null) AssignedExpedition.CancelHero(this);
        }

        private void OnDestroy() => Destroyed?.Invoke(this);

        private void OnValidate()
        {
            level = Mathf.Clamp(level, 1, 5);
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
