using UnityEngine;

namespace DustVFX
{
    // Phase 1 stub - replaced by the real pool in Phase 3.
    public static class DustPool
    {
        public static int SpawnCount;
        public static void Spawn(in DustRequest r) { SpawnCount++; }
        public static DustStep AcquireTrail() => null;
        public static void ReleaseTrail(DustStep s) { }
        public static void ClearAll() { }
    }
}
