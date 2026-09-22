using System.Collections.Generic;
using UnityEngine;

namespace Core.PoolingSystem
{
    public sealed class ObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly bool _autoExpand;
        private readonly Stack<GameObject> _inactive = new();

        public int CountInactive => _inactive.Count;
        public int CountActive { get; private set; }
        public int CountAll => CountInactive + CountActive;

        public ObjectPool(GameObject prefab, Transform parent = null, int prewarm = 0, bool autoExpand = true)
        {
            _prefab = prefab;
            _parent = parent;
            _autoExpand = autoExpand;
            Prewarm(prewarm);
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject go = CreateInstance();
                go.SetActive(false);
                _inactive.Push(go);
            }
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject go;
            if (_inactive.Count > 0) go = _inactive.Pop();
            else if (_autoExpand) go = CreateInstance();
            else return null;

            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            CountActive++;

            if (go.TryGetComponent(out PooledObject po)) po.InvokeSpawn();
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;

            if (go.TryGetComponent(out PooledObject po)) po.InvokeDespawn();

            go.SetActive(false);
            if (_parent != null) go.transform.SetParent(_parent, false);

            _inactive.Push(go);
            CountActive = Mathf.Max(0, CountActive - 1);
        }

        public void Clear()
        {
            while (_inactive.Count > 0)
            {
                GameObject go = _inactive.Pop();
                if (go != null) Object.Destroy(go);
            }
            CountActive = 0;
        }

        private GameObject CreateInstance()
        {
            GameObject go = Object.Instantiate(_prefab, _parent);

            PooledObject po = go.GetComponent<PooledObject>();
            if (po == null) po = go.AddComponent<PooledObject>();
            po.Initialize(Release);

            return go;
        }
    }
}