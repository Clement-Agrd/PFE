using UnityEngine;

namespace Core.StatsSystem.Examples
{
    /// <summary>
    /// Démo : applique des modificateurs sur une stat au clavier et logge la
    /// valeur finale. 1 = +10 (Flat), 2 = +20% (PercentAdd), 3 = retire le buff.
    /// Pose ce composant à côté d'un StatsComponent, assigne une StatDefinition.
    /// </summary>
    [RequireComponent(typeof(StatsComponent))]
    public sealed class StatsDemo : MonoBehaviour
    {
        [SerializeField] private StatDefinition attackStat;

        private StatsComponent _stats;

        // Identité de la source « buff démo » : sert à retirer tous ses modificateurs.
        private readonly object _buffSource = new object();

        private void Awake() => _stats = GetComponent<StatsComponent>();

        private void OnEnable() => _stats.OnStatChanged += HandleChanged;
        private void OnDisable() => _stats.OnStatChanged -= HandleChanged;

        private void Start()
        {
            if (attackStat != null)
                Debug.Log($"[Stats] {attackStat.DisplayName} de base = {_stats.GetValue(attackStat):0.##}");
            else
                Debug.LogWarning("[StatsDemo] Assigne une StatDefinition dans l'Inspector.");
        }

        private void Update()
        {
            if (attackStat == null) return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
                _stats.AddModifier(attackStat, new StatModifier(10f, ModifierType.Flat, _buffSource));

            if (Input.GetKeyDown(KeyCode.Alpha2))
                _stats.AddModifier(attackStat, new StatModifier(0.2f, ModifierType.PercentAdd, _buffSource));

            if (Input.GetKeyDown(KeyCode.Alpha3))
                _stats.RemoveModifiersFromSource(attackStat, _buffSource);
        }

        private void HandleChanged(StatDefinition definition)
            => Debug.Log($"[Stats] {definition.DisplayName} = {_stats.GetValue(definition):0.##}");
    }
}
