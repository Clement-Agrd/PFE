using System.IO;
using UnityEditor;
using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Fonctions partagées par les fenêtres d'édition (mesh source, bake, assets, composant).</summary>
    public static class VertexClothEditorUtility
    {
        /// <summary>Mesh source d'un renderer (SkinnedMeshRenderer ou MeshRenderer + MeshFilter), sinon null.</summary>
        public static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;

            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            return null;
        }

        public static bool IsSupported(Renderer renderer)
        {
            return GetMesh(renderer) != null;
        }

        /// <summary>Crée récursivement un dossier d'assets s'il n'existe pas.</summary>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string name = Path.GetFileName(folder);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Bake poids + contraintes dans un asset (créé à newAssetPath si existing est null).</summary>
        public static VertexClothData Bake(Mesh mesh, ClothTopology topology, float[] freedom,
            VertexClothData existing, string newAssetPath)
        {
            VertexClothData data = existing;
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<VertexClothData>();
                AssetDatabase.CreateAsset(data, AssetDatabase.GenerateUniqueAssetPath(newAssetPath));
            }

            VertexClothTopologyBuilder.BuildConstraints(topology, freedom,
                out Vector2Int[] stretchEdges, out float[] stretchRest,
                out Vector2Int[] bendPairs, out float[] bendRest);

            Undo.RecordObject(data, "Bake Vertex Cloth");
            data.sourceMesh = mesh;
            data.sourceVertexCount = mesh.vertexCount;
            data.vertexToParticle = topology.VertexToParticle;
            data.particleRestPositions = topology.ParticlePositions;
            data.particleFreedom = (float[])freedom.Clone();
            data.stretchEdges = stretchEdges;
            data.stretchRestLengths = stretchRest;
            data.bendPairs = bendPairs;
            data.bendRestLengths = bendRest;
            data.particleTriangles = topology.ParticleTriangles;
            data.meshTriangles = topology.MeshTriangles;

            EditorUtility.SetDirty(data);

            Debug.Log($"[VertexCloth] Bake OK ({mesh.name}) : {topology.ParticleCount} particules, " +
                      $"{stretchEdges.Length} contraintes d'étirement, {bendPairs.Length} de flexion.", data);
            return data;
        }

        /// <summary>Retourne current, ou crée un asset de réglages par défaut dans le dossier donné.</summary>
        public static VertexClothSettings EnsureSettings(VertexClothSettings current, string folder)
        {
            if (current != null)
                return current;

            EnsureFolder(folder);
            var settings = ScriptableObject.CreateInstance<VertexClothSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(folder + "/VertexClothSettings.asset"));
            return settings;
        }

        /// <summary>Ajoute (ou réutilise) le simulateur sur le renderer et lui assigne ses assets.</summary>
        public static VertexClothSimulator AttachSimulator(Renderer renderer, VertexClothData data, VertexClothSettings settings)
        {
            VertexClothSimulator simulator = renderer.GetComponent<VertexClothSimulator>();
            if (simulator == null)
                simulator = Undo.AddComponent<VertexClothSimulator>(renderer.gameObject);

            Undo.RecordObject(simulator, "Configure Vertex Cloth");
            simulator.Configure(data, settings);
            EditorUtility.SetDirty(simulator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(simulator);
            return simulator;
        }

        public static string FolderOf(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : "Assets";
        }
    }
}
