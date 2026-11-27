using UnityEngine;

namespace VertexCloth
{
    /// <summary>Données bakées par l'éditeur : poids, soudure des vertices et contraintes.</summary>
    public sealed class VertexClothData : ScriptableObject
    {
        public Mesh sourceMesh;
        public int sourceVertexCount;

        /// <summary>Indice de particule pour chaque vertex du mesh (vertices soudés).</summary>
        public int[] vertexToParticle = new int[0];

        public Vector3[] particleRestPositions = new Vector3[0];

        /// <summary>0 = fixe (suit l'objet), 1 = libre.</summary>
        public float[] particleFreedom = new float[0];

        public Vector2Int[] stretchEdges = new Vector2Int[0];
        public float[] stretchRestLengths = new float[0];
        public Vector2Int[] bendPairs = new Vector2Int[0];
        public float[] bendRestLengths = new float[0];

        /// <summary>Triangles en indices de particules (triplets à plat).</summary>
        public int[] particleTriangles = new int[0];

        /// <summary>Triangles du mesh d'origine (triplets à plat), pour recalculer les normales.</summary>
        public int[] meshTriangles = new int[0];

        public int ParticleCount => particleRestPositions != null ? particleRestPositions.Length : 0;

        public bool IsValidFor(Mesh mesh)
        {
            return mesh != null
                   && ParticleCount > 0
                   && mesh.vertexCount == sourceVertexCount
                   && vertexToParticle != null
                   && vertexToParticle.Length == sourceVertexCount
                   && particleFreedom != null
                   && particleFreedom.Length == ParticleCount;
        }
    }
}
