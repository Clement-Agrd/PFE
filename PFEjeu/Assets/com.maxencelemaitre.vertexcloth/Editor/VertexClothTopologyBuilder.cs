using System.Collections.Generic;
using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Topologie soudée du mesh, utilisée pendant la peinture et le bake.</summary>
    public sealed class ClothTopology
    {
        public int[] VertexToParticle;
        public Vector3[] ParticlePositions;
        public Vector3[] ParticleNormals;
        public int[] ParticleTriangles;   // triplets à plat (indices de particules)
        public int[] MeshTriangles;       // triplets à plat (indices de vertices)
        public int[] IslandOfParticle;
        public int IslandCount;

        public int ParticleCount => ParticlePositions.Length;
    }

    public static class VertexClothTopologyBuilder
    {
        private struct EdgeInfo
        {
            public int OppositeA;
            public int OppositeB;
            public int Count;
        }

        /// <summary>Soude les vertices confondus, calcule normales et îlots.</summary>
        public static ClothTopology Build(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] meshTriangles = mesh.triangles;

            float tolerance = Mathf.Max(1e-6f, mesh.bounds.size.magnitude * 1e-5f);
            float inverse = 1f / tolerance;

            var map = new Dictionary<Vector3Int, int>(vertices.Length);
            var particlePositions = new List<Vector3>(vertices.Length);
            int[] vertexToParticle = new int[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                var key = new Vector3Int(
                    Mathf.RoundToInt(v.x * inverse),
                    Mathf.RoundToInt(v.y * inverse),
                    Mathf.RoundToInt(v.z * inverse));

                if (!map.TryGetValue(key, out int particle))
                {
                    particle = particlePositions.Count;
                    map.Add(key, particle);
                    particlePositions.Add(v);
                }

                vertexToParticle[i] = particle;
            }

            var particleTriangles = new List<int>(meshTriangles.Length);
            for (int i = 0; i + 2 < meshTriangles.Length; i += 3)
            {
                int a = vertexToParticle[meshTriangles[i]];
                int b = vertexToParticle[meshTriangles[i + 1]];
                int c = vertexToParticle[meshTriangles[i + 2]];
                if (a == b || b == c || a == c)
                    continue;

                particleTriangles.Add(a);
                particleTriangles.Add(b);
                particleTriangles.Add(c);
            }

            var topology = new ClothTopology
            {
                VertexToParticle = vertexToParticle,
                ParticlePositions = particlePositions.ToArray(),
                ParticleTriangles = particleTriangles.ToArray(),
                MeshTriangles = meshTriangles
            };

            topology.ParticleNormals = ComputeNormals(topology);
            ComputeIslands(topology);
            return topology;
        }

        /// <summary>Construit contraintes d'étirement et de flexion (les paires 100 % fixes sont ignorées).</summary>
        public static void BuildConstraints(
            ClothTopology topology,
            float[] freedom,
            out Vector2Int[] stretchEdges,
            out float[] stretchRest,
            out Vector2Int[] bendPairs,
            out float[] bendRest)
        {
            var edges = new Dictionary<long, EdgeInfo>();
            int[] tris = topology.ParticleTriangles;

            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];
                AddEdge(edges, a, b, c);
                AddEdge(edges, b, c, a);
                AddEdge(edges, c, a, b);
            }

            var stretchList = new List<Vector2Int>();
            var stretchLengths = new List<float>();
            var bendList = new List<Vector2Int>();
            var bendLengths = new List<float>();
            Vector3[] pos = topology.ParticlePositions;

            foreach (KeyValuePair<long, EdgeInfo> pair in edges)
            {
                int i = (int)(pair.Key >> 32);
                int j = (int)(pair.Key & 0xFFFFFFFFL);

                if (freedom[i] > 0f || freedom[j] > 0f)
                {
                    float length = Vector3.Distance(pos[i], pos[j]);
                    if (length > 1e-7f)
                    {
                        stretchList.Add(new Vector2Int(i, j));
                        stretchLengths.Add(length);
                    }
                }

                EdgeInfo info = pair.Value;
                if (info.Count == 2 && info.OppositeA != info.OppositeB
                    && (freedom[info.OppositeA] > 0f || freedom[info.OppositeB] > 0f))
                {
                    float length = Vector3.Distance(pos[info.OppositeA], pos[info.OppositeB]);
                    if (length > 1e-7f)
                    {
                        bendList.Add(new Vector2Int(info.OppositeA, info.OppositeB));
                        bendLengths.Add(length);
                    }
                }
            }

            stretchEdges = stretchList.ToArray();
            stretchRest = stretchLengths.ToArray();
            bendPairs = bendList.ToArray();
            bendRest = bendLengths.ToArray();
        }

        private static void AddEdge(Dictionary<long, EdgeInfo> edges, int a, int b, int opposite)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            edges.TryGetValue(key, out EdgeInfo info);

            if (info.Count == 0) info.OppositeA = opposite;
            else if (info.Count == 1) info.OppositeB = opposite;

            info.Count++;
            edges[key] = info;
        }

        private static Vector3[] ComputeNormals(ClothTopology topology)
        {
            var normals = new Vector3[topology.ParticleCount];
            int[] tris = topology.ParticleTriangles;
            Vector3[] pos = topology.ParticlePositions;

            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 n = Vector3.Cross(pos[tris[i + 1]] - pos[tris[i]], pos[tris[i + 2]] - pos[tris[i]]);
                normals[tris[i]] += n;
                normals[tris[i + 1]] += n;
                normals[tris[i + 2]] += n;
            }

            for (int i = 0; i < normals.Length; i++)
                normals[i] = normals[i].sqrMagnitude > 1e-12f ? normals[i].normalized : Vector3.up;

            return normals;
        }

        private static void ComputeIslands(ClothTopology topology)
        {
            int count = topology.ParticleCount;
            int[] parent = new int[count];
            for (int i = 0; i < count; i++)
                parent[i] = i;

            int[] tris = topology.ParticleTriangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Union(parent, tris[i], tris[i + 1]);
                Union(parent, tris[i + 1], tris[i + 2]);
            }

            var rootToIsland = new Dictionary<int, int>();
            topology.IslandOfParticle = new int[count];
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (!rootToIsland.TryGetValue(root, out int island))
                {
                    island = rootToIsland.Count;
                    rootToIsland.Add(root, island);
                }

                topology.IslandOfParticle[i] = island;
            }

            topology.IslandCount = rootToIsland.Count;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
                parent[ra] = rb;
        }
    }
}
