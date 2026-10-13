using UnityEngine;
using Core.TowerDefense;

namespace Core.TrapSystem
{
    /// <summary>Piège à loup : se referme sur le premier ennemi, l'immobilise et lui inflige un gros coup. Mono-cible.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WolfTrap : TrapBase
    {
        [Header("Piège à loup")]
        [SerializeField, Min(0.1f)] private float holdSeconds = 3f;
        [SerializeField] private Animator animator;
        [SerializeField] private string closeTrigger = "Close";

        private EnemyController _victim;
        private float _releaseTime;
        private float _originalSpeed;
        private bool _armed = true;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_armed) return;
            EnemyController e = EnemyOf(other);
            if (e == null) return;

            _armed = false;
            _victim = e;
            _releaseTime = Time.time + holdSeconds;

            if (e.Agent != null)
            {
                _originalSpeed = e.Agent.speed;
                e.Agent.speed = 0f;
                if (e.Agent.isOnNavMesh) e.Agent.velocity = Vector3.zero;
            }

            Hit(e.Health);

            if (animator != null && !string.IsNullOrEmpty(closeTrigger))
                animator.SetTrigger(closeTrigger);
        }

        private void Update()
        {
            if (_armed) return;

            bool victimGone = _victim == null || !_victim.isActiveAndEnabled
                              || _victim.IsFinished || _victim.Health.IsDead;
            if (victimGone || Time.time >= _releaseTime)
            {
                Release();
                Destroy(gameObject);
            }
        }

        private void Release()
        {
            if (_victim != null && _victim.Agent != null)
                _victim.Agent.speed = _originalSpeed;
            _victim = null;
        }

        private void OnDestroy() => Release();
    }
}
