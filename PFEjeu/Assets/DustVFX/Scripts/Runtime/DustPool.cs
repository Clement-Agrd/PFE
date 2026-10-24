using System.Collections.Generic;
using UnityEngine;

namespace DustVFX
{
    /// <summary>
    /// Pool of DustStep prefabs. Allocation-free after warm-up. DustSettings.maxActiveSteps limits simultaneous
    /// steps: the oldest one is sped up to fade out (never popped) and a spare instance takes over.
    /// </summary>
    public static class DustPool
    {
        sealed class Driver : MonoBehaviour
        {
            float next;
            void Update()
            {
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.15f;
                Recycle();
            }
        }

        const int Spare = 4;

        static readonly List<DustStep> free = new List<DustStep>(16);
        static readonly List<DustStep> active = new List<DustStep>(16);
        static Transform root;
        static bool warnedMissing;
        static int created;

        public static int SpawnCount { get; private set; }
        public static int ActiveCount => active.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            free.Clear();
            active.Clear();
            root = null;
            created = 0;
            SpawnCount = 0;
            warnedMissing = false;
        }

        static bool Init()
        {
            if (root != null) return true;
            if (!Application.isPlaying) return false;
            DustSettings st = DustSettings.Instance;
            if (st.stepPrefab == null)
            {
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning("[DustVFX] DustSettings.stepPrefab is missing - no dust will be spawned. Run Tools/Dust VFX/Rebuild Everything.");
                }
                return false;
            }

            var go = new GameObject("[DustPool]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
            root = go.transform;
            free.Clear();
            active.Clear();
            created = 0;
            for (int i = 0; i < st.maxActiveSteps; i++) Create();
            return true;
        }

        static DustStep Create()
        {
            DustStep s = Object.Instantiate(DustSettings.Instance.stepPrefab, root);
            s.name = "DustStep_" + created++;
            s.ResetForPool();
            free.Add(s);
            return s;
        }

        static DustStep Take()
        {
            int limit = Mathf.Max(1, DustSettings.Instance.maxActiveSteps);

            int live = 0;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].Dying) live++;

            // global limit: hurry the oldest live step so it fades out quickly
            while (live >= limit)
            {
                DustStep oldest = null;
                for (int i = 0; i < active.Count; i++)
                    if (!active[i].Dying && !active[i].Trailing && (oldest == null || active[i].StartTime < oldest.StartTime))
                        oldest = active[i];
                if (oldest == null) break;
                oldest.Hurry();
                live--;
            }

            if (free.Count == 0)
            {
                if (created < limit + Spare)
                {
                    Create();
                }
                else
                {
                    // out of spares: recycle the oldest dying step (its particles are almost gone)
                    DustStep oldest = null;
                    for (int i = 0; i < active.Count; i++)
                        if (!active[i].Trailing && (oldest == null || active[i].StartTime < oldest.StartTime))
                            oldest = active[i];
                    if (oldest == null) return null;
                    active.Remove(oldest);
                    oldest.ResetForPool();
                    free.Add(oldest);
                }
            }

            int last = free.Count - 1;
            DustStep s = free[last];
            free.RemoveAt(last);
            s.ResetForPool();
            active.Add(s);
            return s;
        }

        public static void Spawn(in DustRequest r)
        {
            if (r.surface == null || !r.surface.EmitsDust) return;
            if (DustSettings.Instance.intensity <= 0.01f) return;
            if (!Init()) return;
            DustStep s = Take();
            if (s == null) return;
            SpawnCount++;
            s.Play(r);
        }

        public static DustStep AcquireTrail()
        {
            if (!Init()) return null;
            DustStep s = Take();
            if (s != null) s.Trailing = true;
            return s;
        }

        public static void ReleaseTrail(DustStep s)
        {
            if (s != null) s.Trailing = false;
        }

        static void Recycle()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                DustStep s = active[i];
                if (s == null)
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (s.Trailing || Time.time - s.StartTime < 0.1f) continue;
                if (s.IsFinished())
                {
                    active.RemoveAt(i);
                    s.ResetForPool();
                    free.Add(s);
                }
            }
        }

        public static void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                DustStep s = active[i];
                active.RemoveAt(i);
                if (s == null) continue;
                s.ResetForPool();
                free.Add(s);
            }
        }
    }
}
