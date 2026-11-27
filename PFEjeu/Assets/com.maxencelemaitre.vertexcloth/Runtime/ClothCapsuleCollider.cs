using UnityEngine;

namespace VertexCloth
{
    /// <summary>Capsule de collision (ou sphère si start == end) utilisée par VertexClothSimulator.</summary>
    [DisallowMultipleComponent]
    public sealed class ClothCapsuleCollider : MonoBehaviour
    {
        [SerializeField] private Vector3 localStart = Vector3.zero;
        [SerializeField] private Vector3 localEnd = new Vector3(0f, 0.3f, 0f);
        [SerializeField, Min(0.001f)] private float radius = 0.1f;

        public Vector3 WorldStart => transform.TransformPoint(localStart);
        public Vector3 WorldEnd => transform.TransformPoint(localEnd);

        public float WorldRadius
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

        /// <summary>Configure la capsule (coordonnées locales à ce transform, rayon local).</summary>
        public void Configure(Vector3 start, Vector3 end, float newRadius)
        {
            localStart = start;
            localEnd = end;
            radius = Mathf.Max(0.001f, newRadius);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 a = WorldStart;
            Vector3 b = WorldEnd;
            float r = WorldRadius;
            Gizmos.DrawWireSphere(a, r);
            Gizmos.DrawWireSphere(b, r);
            Gizmos.DrawLine(a, b);
        }
    }
}
