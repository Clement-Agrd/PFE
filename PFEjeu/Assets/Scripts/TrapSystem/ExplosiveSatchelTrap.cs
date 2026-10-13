using UnityEngine;

namespace Core.TrapSystem
{
    /// <summary>Sacoche explosive : explose après un délai, gros dégâts directs sur une large zone.</summary>
    public sealed class ExplosiveSatchelTrap : TrapBase
    {
        [Header("Explosion")]
        [SerializeField, Min(0.1f)] private float fuseSeconds = 3f;
        [SerializeField, Min(0.5f)] private float radius = 6f;
        [Tooltip("Part des dégâts reçue au bord de la zone (1 = pas d'atténuation).")]
        [SerializeField, Range(0f, 1f)] private float edgeDamageFactor = 0.5f;
        [SerializeField] private GameObject explosionVfx;

        private float _timer;

        protected override void OnEnable()
        {
            base.OnEnable();
            _timer = fuseSeconds;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) Explode();
        }

        private void Explode()
        {
            enabled = false;

            foreach (var enemy in FindEnemies(transform.position, radius))
            {
                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                float falloff = Mathf.Lerp(1f, edgeDamageFactor, Mathf.Clamp01(dist / radius));
                int amount = Mathf.Max(1, Mathf.RoundToInt(Damage * falloff));
                enemy.Health.TakeDamage(new Core.HealthSystem.DamageInfo(
                    amount, damageType, owner != null ? owner : gameObject));
            }

            if (explosionVfx != null)
                Destroy(Instantiate(explosionVfx, transform.position, Quaternion.identity), 5f);

            Destroy(gameObject);
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
