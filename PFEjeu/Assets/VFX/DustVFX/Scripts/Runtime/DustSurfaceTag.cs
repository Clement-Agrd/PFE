using UnityEngine;

namespace DustVFX
{
    /// <summary>Explicit surface override for a ground object (highest priority of the resolver).</summary>
    [AddComponentMenu("Dust VFX/Dust Surface Tag")]
    public sealed class DustSurfaceTag : MonoBehaviour
    {
        [Tooltip("Surface used for every collider under this object.")]
        public DustSurface surface;
    }
}
