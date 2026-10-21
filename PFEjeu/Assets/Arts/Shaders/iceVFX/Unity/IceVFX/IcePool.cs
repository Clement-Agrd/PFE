using System.Collections.Generic;
using UnityEngine;

namespace IceVFX
{
    /// Pool simple par prefab : Get() réactive une instance (OnEnable = reset), Release() la désactive.
    /// Aucune allocation une fois le pool chaud.
    public static class IcePool
    {
        static readonly Dictionary<GameObject, Stack<IcePooled>> pools = new Dictionary<GameObject, Stack<IcePooled>>();
        static Transform root;

        // Sans domain reload, les statics survivent au Play : on repart propre.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pools.Clear(); root = null; }

        static Transform Root()
        {
            if (!root)
            {
                var g = new GameObject("[IceVFX Pool]");
                Object.DontDestroyOnLoad(g);
                root = g.transform;
            }
            return root;
        }

        static Stack<IcePooled> StackFor(GameObject prefab)
        {
            if (!pools.TryGetValue(prefab, out var s)) { s = new Stack<IcePooled>(16); pools[prefab] = s; }
            return s;
        }

        public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (!prefab) return null;
            var stack = StackFor(prefab);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (!p) continue;                       // détruit par un changement de scène
                p.inPool = false;
                var t = p.transform;
                t.SetParent(null, false);
                t.SetPositionAndRotation(pos, rot);
                p.gameObject.SetActive(true);
                return p.gameObject;
            }
            var go = Object.Instantiate(prefab, pos, rot);
            var pooled = go.GetComponent<IcePooled>();
            if (!pooled) pooled = go.AddComponent<IcePooled>();
            pooled.prefab = prefab;
            return go;
        }

        public static void Release(GameObject go)
        {
            var p = go.GetComponent<IcePooled>();
            if (!p) { Object.Destroy(go); return; }     // instance hors pool
            if (p.inPool) return;
            p.inPool = true;
            go.SetActive(false);
            go.transform.SetParent(Root(), false);
            StackFor(p.prefab).Push(p);
        }

        /// Crée n instances à l'avance (évite le hitch du premier tir).
        public static void Prewarm(GameObject prefab, int count)
        {
            if (!prefab) return;
            var tmp = new List<GameObject>(count);
            for (int i = 0; i < count; i++) tmp.Add(Get(prefab, Vector3.zero, Quaternion.identity));
            for (int i = 0; i < tmp.Count; i++) Release(tmp[i]);
        }
    }
}
