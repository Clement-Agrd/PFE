using System.Collections.Generic;
using UnityEngine;
using Core.TowerDefense;

namespace Core.TrapSystem
{
    /// <summary>
    /// Piège à pointes : ralentit les ennemis qui le traversent et leur inflige de faibles
    /// dégâts à intervalle régulier tant qu'ils sont dessus. Large zone (trigger).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpikeTrap : TrapBase
    {
        [Header("Pointes")]
        [SerializeField, Range(0.1f, 1f)] private float speedFactor = 0.5f;
        [SerializeField, Min(0.1f)] private float tickInterval = 0.5f;
        [Tooltip("Durée de vie du piège (0 = permanent).")]
        [SerializeField, Min(0f)] private float lifetime = 30f;

        private readonly HashSet<EnemyController> _inside = new();
        private readonly Dictionary<EnemyController, float> _originalSpeeds = new();
        private float _nextTick;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _nextTick = Time.time + tickInterval;
            if (lifetime > 0f) Destroy(gameObject, lifetime);
        }

        private void OnTriggerEnter(Collider other)
        {
            EnemyController e = EnemyOf(other);
            if (e == null || !_inside.Add(e)) return;
            SetAgentSpeedFactor(e, _originalSpeeds, speedFactor);
        }

        private void OnTriggerExit(Collider other)
        {
            EnemyController e = other.GetComponentInParent<EnemyController>();
            if (e == null || !_inside.Remove(e)) return;
            RestoreAgentSpeed(e, _originalSpeeds);
        }

        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick += tickInterval;

            _inside.RemoveWhere(e =>
            {
                bool gone = e == null || !e.isActiveAndEnabled || e.IsFinished || e.Health.IsDead;
                if (gone && e != null) RestoreAgentSpeed(e, _originalSpeeds);
                return gone;
            });

            foreach (EnemyController e in _inside)
                Hit(e.Health);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            foreach (EnemyController e in _inside)
                RestoreAgentSpeed(e, _originalSpeeds);
            _inside.Clear();
        }
    }
}
