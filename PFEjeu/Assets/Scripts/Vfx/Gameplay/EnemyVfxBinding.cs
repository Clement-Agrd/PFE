using Core.HealthSystem;
using Core.PoolingSystem;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Aura + poussiere d'un ennemi, sans toucher a son prefab : cet objet poole suit la cible
    /// (copie de position en LateUpdate), ecoute Health.OnDamaged / OnDeath et se rend au pool
    /// a la mort ou quand la cible est desactivee (ennemi rendu au pool de vagues).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyVfxBinding : MonoBehaviour, IVfxPlayer, IPoolable
    {
        [SerializeField] private ParticleSystem[] systems = System.Array.Empty<ParticleSystem>();

        private PooledObject _pooled;
        private Transform _target;
        private Health _health;
        private IEnemyVfxSink _sink;
        private Vector3 _offset;
        private System.Action<DamageInfo> _onDamaged;
        private System.Action _onDeath;
        private bool _bound;
        private System.Action _onReleased;

        private void Awake()
        {
            _pooled = GetComponent<PooledObject>();
            _onDamaged = HandleDamaged;
            _onDeath = HandleDeath;
        }

        public bool IsBound => _bound;

        public void Bind(Transform target, Health health, IEnemyVfxSink sink, float glowHeight, System.Action onReleased)
        {
            Unbind();
            _target = target;
            _health = health;
            _sink = sink;
            _offset = new Vector3(0f, glowHeight, 0f);
            _onReleased = onReleased;
            _bound = true;

            if (_health != null)
            {
                _health.OnDamaged += _onDamaged;
                _health.OnDeath += _onDeath;
            }
            transform.position = _target.position + _offset;
            Play();
        }

        public void OnSpawn() { }

        public void OnDespawn()
        {
            Unbind();
            Stop();
        }

        public void Play()
        {
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        public void Stop()
        {
            for (int i = 0; i < systems.Length; i++) systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetIntensity(float intensity)
        {
            float k = Mathf.Clamp01(intensity);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.enabled = k > 0.01f;
            }
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                Release();
                return;
            }
            transform.position = _target.position + _offset;
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (_sink != null && _target != null) _sink.PlayHit(_target.position + _offset);
        }

        private void HandleDeath()
        {
            if (_sink != null && _target != null) _sink.PlayDeath(_target.position + _offset);
            Release();
        }

        private void Release()
        {
            Unbind();
            if (_pooled != null) _pooled.Release();
        }

        private void Unbind()
        {
            if (!_bound) return;
            if (_health != null)
            {
                _health.OnDamaged -= _onDamaged;
                _health.OnDeath -= _onDeath;
            }
            _health = null;
            _target = null;
            _sink = null;
            _bound = false;
            _onReleased?.Invoke();
            _onReleased = null;
        }
    }
}
