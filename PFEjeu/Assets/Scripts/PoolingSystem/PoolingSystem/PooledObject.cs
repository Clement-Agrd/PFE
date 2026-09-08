using System;
using UnityEngine;

namespace Core.PoolingSystem
{
    /// <summary>
    /// Ajouté automatiquement à chaque instance issue d'un pool. Il sait comment
    /// se rendre à SON pool (callback), gère un retour automatique après délai, et
    /// met en cache les IPoolable de l'objet (pour éviter des GetComponent répétés).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObject : MonoBehaviour
    {
        private IPoolable[] _poolables;
        private Action<GameObject> _returnToPool;
        private float _autoReleaseAt = -1f;

        /// <summary>Appelé une seule fois par le pool, à la création de l'instance.</summary>
        public void Initialize(Action<GameObject> returnToPool)
        {
            _returnToPool = returnToPool;
            _poolables = GetComponentsInChildren<IPoolable>(includeInactive: true);
        }

        /// <summary>Programme un retour automatique au pool dans 'seconds'.</summary>
        public void ReleaseAfter(float seconds)
            => _autoReleaseAt = seconds > 0f ? Time.time + seconds : -1f;

        /// <summary>Renvoie immédiatement l'objet à son pool.</summary>
        public void Release()
        {
            _autoReleaseAt = -1f;
            _returnToPool?.Invoke(gameObject);
        }

        // Appelés par le pool au moment du spawn / despawn.
        public void InvokeSpawn()
        {
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
