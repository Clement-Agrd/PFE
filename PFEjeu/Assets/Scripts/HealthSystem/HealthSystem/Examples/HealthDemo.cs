using UnityEngine;

namespace Core.HealthSystem.Examples
{
    /// <summary>
    /// Démo : inflige des dégâts (1), soigne (2), réanime (R). Logge les
    /// changements de vie et la mort. Pose ce composant à côté d'un Health.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class HealthDemo : MonoBehaviour
    {
        [SerializeField] private int hitAmount = 15;
        [SerializeField] private int healAmount = 10;

        private Health _health;

        private void Awake() => _health = GetComponent<Health>();

        private void OnEnable()
        {
            _health.OnHealthChanged += HandleChanged;
            _health.OnDamaged += HandleDamaged;
            _health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            _health.OnHealthChanged -= HandleChanged;
            _health.OnDamaged -= HandleDamaged;
            _health.OnDeath -= HandleDeath;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) _health.TakeDamage(hitAmount);
            if (Input.GetKeyDown(KeyCode.Alpha2)) _health.Heal(healAmount);
            if (Input.GetKeyDown(KeyCode.R)) _health.Revive();
        }

        private void HandleChanged(int current, int max)
            => Debug.Log($"[Health] {current}/{max} ({_health.Normalized:P0})");

        private void HandleDamaged(DamageInfo info)
        {
            string type = info.Type != null ? info.Type.DisplayName : "brut";
            Debug.Log($"[Health] Coup reçu : {info.Amount} ({type})");
        }

        private void HandleDeath() => Debug.Log("[Health] ☠ Mort !");
    }
}
