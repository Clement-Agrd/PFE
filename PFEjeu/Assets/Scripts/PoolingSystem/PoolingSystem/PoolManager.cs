using System.Collections.Generic;
using UnityEngine;

namespace Core.PoolingSystem
{
    /// <summary>
    /// Gère tous les pools du jeu. Accessible partout via PoolManager.Instance,
    /// sans référence à câbler dans l'Inspector : créé automatiquement au
    /// premier accès et persiste entre les scènes (même principe que
    /// TweenManager). Pour prewarmer des prefabs au démarrage, place quand
    /// même un PoolManager dans la scène avec sa liste "Preloaded Pools"
    /// réglée : il devient l'instance utilisée par tout le monde.
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

        private static PoolManager _instance;

        private readonly Dictionary<GameObject, ObjectPool> _pools = new();

        public static PoolManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[PoolManager]");
                    _instance = go.AddComponent<PoolManager>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);

            foreach (PoolConfig config in preloadedPools)
            {
                if (config.prefab == null) continue;
                _pools[config.prefab] = new ObjectPool(config.prefab, transform, config.prewarm, config.autoExpand);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            return GetOrCreatePool(prefab).Get(position, rotation);
        }

        public T Spawn<T>(GameObject prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            GameObject go = Spawn(prefab, position, rotation);
            return go != null ? go.GetComponent<T>() : null;
        }

        public void Despawn(GameObject instance)
        {
            if (instance == null) return;
            if (instance.TryGetComponent(out PooledObject po)) po.Release();
            else Destroy(instance);
        }

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