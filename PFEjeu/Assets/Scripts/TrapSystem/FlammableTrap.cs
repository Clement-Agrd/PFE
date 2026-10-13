using UnityEngine;

namespace Core.TrapSystem
{
    /// <summary>Piège inflammable : brûle les ennemis proches, dégâts moyens sur la durée, petite zone.</summary>
    public sealed class FlammableTrap : TrapBase
    {
        [Header("Flammes")]
        [SerializeField, Min(0.1f)] private float radius = 2.5f;
        [SerializeField, Min(0.1f)] private float tickInterval = 0.5f;
        [SerializeField, Min(0.5f)] private float lifetime = 8f;
        [SerializeField] private GameObject fireVfx;

        private float _nextTick;
        private float _endTime;

        protected override void OnEnable()
        {
            base.OnEnable();
            _nextTick = Time.time + tickInterval;
            _endTime = Time.time + lifetime;
            if (fireVfx != null)
                Instantiate(fireVfx, transform.position, Quaternion.identity, transform);
        }

        private void Update()
        {
            if (Time.time >= _endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.time < _nextTick) return;
            _nextTick += tickInterval;

            foreach (var enemy in FindEnemies(transform.position, radius))
                Hit(enemy.Health);
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.2f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
