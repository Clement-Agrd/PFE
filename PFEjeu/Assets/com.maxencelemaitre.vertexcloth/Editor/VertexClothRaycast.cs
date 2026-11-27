using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Raycast Möller-Trumbore sur un mesh en espace local (double face).</summary>
    public static class VertexClothRaycast
    {
        public static bool Raycast(Vector3[] vertices, int[] triangles, Ray localRay,
            out float distance, out int triangleIndex)
        {
            distance = float.MaxValue;
            triangleIndex = -1;

            Vector3 origin = localRay.origin;
            Vector3 direction = localRay.direction;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]];
                Vector3 e1 = vertices[triangles[i + 1]] - v0;
                Vector3 e2 = vertices[triangles[i + 2]] - v0;

                Vector3 p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f)
                    continue;

                float invDet = 1f / det;
                Vector3 s = origin - v0;
                float u = Vector3.Dot(s, p) * invDet;
                if (u < 0f || u > 1f)
                    continue;

                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(direction, q) * invDet;
                if (v < 0f || u + v > 1f)
                    continue;

                float t = Vector3.Dot(e2, q) * invDet;
                if (t > 0f && t < distance)
                {
                    distance = t;
                    triangleIndex = i / 3;
                }
            }

            return triangleIndex >= 0;
        }
    }
}
