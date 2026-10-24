using UnityEngine;

namespace DustVFX
{
    public enum DustEventKind
    {
        Step,
        Land,
        Stop,
        Slide
    }

    public enum DustQuality
    {
        Low,
        Medium,
        High
    }

    public enum DustSurfaceKind
    {
        Earth,
        Sand,
        Stone,
        Snow,
        Grass,
        Wood,
        None
    }

    /// <summary>Everything the pooled DustStep needs to build one puff. Plain struct, no allocation.</summary>
    public struct DustRequest
    {
        public DustEventKind kind;
        public Vector3 position;
        public Vector3 normal;
        /// <summary>Unit horizontal direction of travel (the dust is thrown the opposite way).</summary>
        public Vector3 moveDir;
        /// <summary>0 = walk, 1 = sprint.</summary>
        public float speed01;
        /// <summary>0..1 landing strength (Land / Stop only).</summary>
        public float impact01;
        public DustSurface surface;
        public int foot;
    }
}
