using System.Collections.Generic;
using UnityEngine;

namespace Core.PoolingSystem
{
    /// <summary>
    /// Point d'accès au pooling : un pool par prefab, créé à la demande ou
    /// préchargé via l'Inspector. Expose Spawn / Despawn typés.
    /// </summary>
    public sealed class PoolManager : MonoBehaviour
    {
        [System.Serializable]
        private struct PoolConfig
        {
            public GameObject prefab;
            [Min(0)] public int prewarm;
            public bool autoExpand;
        }

        [SerializeField] private List<PoolConfig> preloadedPools = new();

        private readonly Dictionary<GameObject, ObjectPool> _pools = new();

        private void Awake()
        {
            foreach (PoolConfig config in preloadedPools)
            {
                if (config.prefab == null) continue;
                _pools[config.prefab] = new ObjectPool(config.prefab, transform, config.prewarm, config.autoExpand);
            }
        }

        /// <summary>Sort une instance du prefab donné à la position/rotation voulues.</summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
            => GetOrCreatePool(prefab).Get(position, rotation);

        /// <summary>Comme Spawn, mais renvoie directement le composant T de l'instance.</summary>
        public T Spawn<T>(GameObject prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            GameObject go = Spawn(prefab, position, rotation);
            return go != null ? go.GetComponent<T>() : null;
        }

        /// <summary>Renvoie une instance à son pool (ou la détruit si elle n'en vient pas).</summary>
        public void Despawn(GameObject instance)
        {
            if (instance == null) return;

            if (instance.TryGetComponent(out PooledObject po)) po.Release();
            else Destroy(instance);
        }

        /// <summary>Programme le retour de l'instance à son pool après 'delay' secondes.</summary>
        public void Despawn(GameObject instance, float delay)
        {
            if (instance != null && instance.TryGetComponent(out PooledObject po))
                po.ReleaseAfter(delay);
        }

        private ObjectPool GetOrCreatePool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out ObjectPool pool))
            {
                pool = new ObjectPool(prefab, transform);
                _pools[prefab] = pool;
            }
            return pool;
        }
    }
}
