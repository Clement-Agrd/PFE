using System;
using UnityEngine;

namespace Core.PoolingSystem
{
    /// <summary>
    /// Ajouté à chaque instance issue d'un pool. Sait se rendre à SON pool.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        private IPoolable[] _poolables;
        private Action<GameObject> _returnToPool;
        private float _autoReleaseAt = -1f;
        private bool _isReleasing;

        public void Initialize(Action<GameObject> returnToPool)
        {
            _returnToPool = returnToPool;
            _poolables = GetComponentsInChildren<IPoolable>(includeInactive: true);
        }

        public void ReleaseAfter(float seconds)
            => _autoReleaseAt = seconds > 0f ? Time.time + seconds : -1f;

        public void Release()
        {
            if (_isReleasing) return;
            _isReleasing = true;

            _autoReleaseAt = -1f;

            if (_returnToPool != null)
                _returnToPool.Invoke(gameObject);
            else
                Destroy(gameObject);
        }

        public void InvokeSpawn()
        {
            _isReleasing = false;
            if (_poolables == null) return;
            for (int i = 0; i < _poolables.Length; i++) _poolables[i].OnSpawn();
        }

        public void InvokeDespawn()
        {
            if (_poolables == null) return;
            for (int i = 0; i < _poolables.Length; i++) _poolables[i].OnDespawn();
        }

        private void Update()
        {
            if (_autoReleaseAt > 0f && Time.time >= _autoReleaseAt)
                Release();
        }
    }
}