using UnityEngine;

namespace ZoneFx
{
    /// <summary>
    /// Cylindre ouvert (sans capuchon) de rayon 1 et de hauteur 1, pied en y = 0 : la "paroi" du voile.
    /// UV : x = tour complet (0..1), y = hauteur (0 = pied, 1 = sommet). Le contrôleur le met à l'échelle (rayon, hauteur, rayon).
    /// </summary>
    public static class ZoneVeilMesh
    {
        public const int DefaultSegments = 96;

        public static Mesh Build(int segments = DefaultSegments)
        {
            segments = Mathf.Max(8, segments);
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float angle = u * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = ring;                                   // pied
                vertices[i * 2 + 1] = ring + Vector3.up;                  // sommet
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "ZoneVeil" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            return mesh;
        }
    }
}
