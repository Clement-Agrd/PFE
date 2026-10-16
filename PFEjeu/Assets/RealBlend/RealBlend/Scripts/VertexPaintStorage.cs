using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RealBlend
{
    [System.Serializable]
    public sealed class RealBlendGeneratedInteriorSubmeshRecord
    {
        [HideInInspector] public int submesh = -1;
        [HideInInspector] public long indexSignature;
    }

    [System.Serializable]
    public sealed class RealBlendStoredIndexBuffer
    {
        [HideInInspector] public MeshTopology topology = MeshTopology.Triangles;
        [HideInInspector] public int baseVertex;
        [HideInInspector] public int[] indices;
    }

    [System.Serializable]
    public sealed class RealBlendStoredUVChannel
    {
        [HideInInspector] public int dimension = 2;
        [HideInInspector] public Vector4[] values;
    }

    internal static class RealBlendStoredMeshDataUtility
    {
        internal static RealBlendStoredIndexBuffer[] CaptureSubmeshes(Mesh mesh)
        {
            if (mesh == null || mesh.subMeshCount <= 0)
                return null;
            RealBlendStoredIndexBuffer[] result =
                new RealBlendStoredIndexBuffer[mesh.subMeshCount];
            for (int submesh = 0; submesh < result.Length; submesh++)
            {
                uint sourceBaseVertex = mesh.GetBaseVertex(submesh);
                bool canStoreBaseVertex = sourceBaseVertex <= int.MaxValue;
                result[submesh] = new RealBlendStoredIndexBuffer
                {
                    topology = mesh.GetTopology(submesh),
                    baseVertex = canStoreBaseVertex ? (int)sourceBaseVertex : 0,
                    indices = mesh.GetIndices(submesh, !canStoreBaseVertex)
                };
            }
            return result;
        }

        internal static bool HasCompleteSubmeshes(
            RealBlendStoredIndexBuffer[] submeshes)
        {
            if (submeshes == null || submeshes.Length == 0)
                return false;
            for (int i = 0; i < submeshes.Length; i++)
            {
                if (submeshes[i] == null || submeshes[i].indices == null)
                    return false;
            }
            return true;
        }

        internal static void ApplySubmeshes(
            Mesh mesh,
            RealBlendStoredIndexBuffer[] submeshes)
        {
            mesh.subMeshCount = submeshes.Length;
            for (int submesh = 0; submesh < submeshes.Length; submesh++)
            {
                RealBlendStoredIndexBuffer stored = submeshes[submesh];
                mesh.SetIndices(
                    stored.indices ?? System.Array.Empty<int>(),
                    stored.topology,
                    submesh,
                    false,
                    stored.baseVertex);
            }
        }

        internal static RealBlendStoredUVChannel[] CaptureUVChannels(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return null;
            RealBlendStoredUVChannel[] result = new RealBlendStoredUVChannel[8];
            for (int channel = 0; channel < result.Length; channel++)
            {
                VertexAttribute attribute =
                    (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!mesh.HasVertexAttribute(attribute))
                    continue;
                List<Vector4> values = new List<Vector4>();
                mesh.GetUVs(channel, values);
                if (values.Count != mesh.vertexCount)
                    continue;
                result[channel] = new RealBlendStoredUVChannel
                {
                    dimension = Mathf.Clamp(
                        mesh.GetVertexAttributeDimension(attribute),
                        2,
                        4),
                    values = values.ToArray()
                };
            }
            return result;
        }

        internal static bool ApplyUVChannels(
            Mesh mesh,
            RealBlendStoredUVChannel[] channels,
            int vertexCount)
        {
            if (channels == null || channels.Length == 0)
                return false;
            bool appliedAny = false;
            for (int channel = 0; channel < Mathf.Min(8, channels.Length); channel++)
            {
                RealBlendStoredUVChannel stored = channels[channel];
                if (stored?.values == null || stored.values.Length != vertexCount)
                    continue;
                int dimension = Mathf.Clamp(stored.dimension, 2, 4);
                if (dimension == 2)
                {
                    List<Vector2> values = new List<Vector2>(vertexCount);
                    for (int i = 0; i < vertexCount; i++)
                        values.Add(new Vector2(stored.values[i].x, stored.values[i].y));
                    mesh.SetUVs(channel, values);
                }
                else if (dimension == 3)
                {
                    List<Vector3> values = new List<Vector3>(vertexCount);
                    for (int i = 0; i < vertexCount; i++)
                        values.Add(new Vector3(
                            stored.values[i].x,
                            stored.values[i].y,
                            stored.values[i].z));
                    mesh.SetUVs(channel, values);
                }
                else
                {
                    mesh.SetUVs(channel, new List<Vector4>(stored.values));
                }
                appliedAny = true;
            }
            return appliedAny;
        }

        internal static bool HasUVChannel(
            RealBlendStoredUVChannel[] channels,
            int channel,
            int vertexCount)
        {
            return channels != null &&
                   channel >= 0 &&
                   channel < channels.Length &&
                   channels[channel]?.values != null &&
                   channels[channel].values.Length == vertexCount;
        }

        internal static int[] CaptureLegacyTriangles(Mesh mesh)
        {
            if (mesh == null)
                return null;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    return System.Array.Empty<int>();
            }
            return mesh.triangles;
        }
    }

    [AddComponentMenu("Mesh/Vertex Paint Storage")]
    [ExecuteAlways]
    public class VertexPaintStorage : MonoBehaviour
    {
        [HideInInspector] public Color[] paintedColors;
        [HideInInspector, SerializeField] private int paintedColorRevision;
        [HideInInspector] public Vector3[] currentVertices;
        [HideInInspector] public int[] currentTriangles;
        [HideInInspector] public int[] currentSubmeshIndexCounts;
        [HideInInspector] public Vector2[] currentUVs;
        [HideInInspector] public RealBlendStoredIndexBuffer[] currentSubmeshesV2;
        [HideInInspector] public Vector3[] currentNormals;
        [HideInInspector] public Vector4[] currentTangents;
        [HideInInspector] public RealBlendStoredUVChannel[] currentUVChannels;

        [HideInInspector] public Vector3[] baseVertices;
        [HideInInspector] public int[] baseTriangles;
        [HideInInspector] public int[] baseSubmeshIndexCounts;
        [HideInInspector] public Vector2[] baseUVs;
        [HideInInspector] public RealBlendStoredIndexBuffer[] baseSubmeshesV2;
        [HideInInspector] public Vector3[] baseNormals;
        [HideInInspector] public Vector4[] baseTangents;
        [HideInInspector] public RealBlendStoredUVChannel[] baseUVChannels;
        [HideInInspector] public bool hasBaseData = false;
        [HideInInspector, SerializeField] private Mesh ownedEditableMesh;
        [HideInInspector, SerializeField] private bool generatedSolidWall;
        [HideInInspector, SerializeField]
        private RealBlendGeneratedInteriorSubmeshRecord[]
            generatedInteriorSubmeshes;

        public bool hasOriginalData => hasBaseData;
        public int PaintedColorRevision => paintedColorRevision;
        public Mesh OwnedEditableMesh => ownedEditableMesh;
        public bool IsProBuilderOwnedMesh => HasProBuilderMeshComponent();
        public bool IsGeneratedSolidWall => generatedSolidWall;
        public int GeneratedInteriorSubmeshCount =>
            generatedInteriorSubmeshes != null
                ? generatedInteriorSubmeshes.Length
                : 0;
        public Vector3[] originalVertices { get => baseVertices; set => baseVertices = value; }
        public int[] originalTriangles
        {
            get => baseTriangles;
            set
            {
                baseTriangles = value;
                baseSubmeshIndexCounts = null;
                baseSubmeshesV2 = null;
            }
        }
        public Vector2[] originalUVs
        {
            get => baseUVs;
            set
            {
                baseUVs = value;
                baseUVChannels = null;
            }
        }

        private Mesh _runtimeMesh;
#if !UNITY_EDITOR
        private Mesh _runtimeSourceMesh;
#endif
        private bool _ownsRuntimeMesh;
        [System.NonSerialized] private bool warnedAboutMissingSubmeshData;

#if UNITY_EDITOR
        private const string GlobalBakePathKey = "VertexPaintStorage_GlobalBakePath";
        public static string GlobalBakePath =>
            EditorPrefs.GetString(GlobalBakePathKey, "Assets/BakedVertexPaintMeshes");

        public static void SetGlobalBakePath(string value)
        {
            EditorPrefs.SetString(GlobalBakePathKey, value);
        }
#endif

        void OnEnable()
        {
            // ProBuilder's component is authoritative for both its serialized
            // editable data and the compiled MeshFilter output. Reconstructing
            // or recoloring that output from this companion component during a
            // reload creates an ownership race. The optional editor bridge
            // performs a direction-aware synchronization after ProBuilder has
            // initialized instead.
            if (HasProBuilderMeshComponent())
                return;

            if (!HasValidMesh()) RebuildMesh();

#if UNITY_EDITOR
            EditorApplication.delayCall += ApplyColors;
#else
        ApplyColors();
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall -= ApplyColors;
#endif
        }

        void OnDestroy()
        {
#if !UNITY_EDITOR
            ReleaseOwnedRuntimeMesh(true);
#endif
        }

        private bool HasProBuilderMeshComponent()
        {
            Component[] components = GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null &&
                    component.GetType().FullName ==
                    "UnityEngine.ProBuilder.ProBuilderMesh")
                {
                    return true;
                }
            }
            return false;
        }

        bool HasValidMesh()
        {
            var mf = GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null)
                return false;

            SelectStoredTopology(
                out Vector3[] storedVertices,
                out int[] storedTriangles,
                out int[] storedSubmeshCounts,
                out bool useCurrent);

            // A storage component with no captured topology has nothing it can
            // use to validate or rebuild the MeshFilter yet.
            if (storedVertices == null || storedVertices.Length == 0)
                return true;

            RealBlendStoredIndexBuffer[] storedSubmeshesV2 = useCurrent
                ? currentSubmeshesV2
                : baseSubmeshesV2;
            if (RealBlendStoredMeshDataUtility.HasCompleteSubmeshes(storedSubmeshesV2))
            {
                if (mesh.vertexCount != storedVertices.Length)
                    return true;
                bool exact = mesh.subMeshCount == storedSubmeshesV2.Length;
                long storedTotal = 0;
                long liveTotal = 0;
                for (int submesh = 0; submesh < storedSubmeshesV2.Length; submesh++)
                    storedTotal += storedSubmeshesV2[submesh].indices.Length;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    liveTotal += (long)mesh.GetIndexCount(submesh);
                if (exact && mesh.isReadable)
                {
                    for (int submesh = 0; submesh < storedSubmeshesV2.Length; submesh++)
                    {
                        RealBlendStoredIndexBuffer stored = storedSubmeshesV2[submesh];
                        uint liveBase = mesh.GetBaseVertex(submesh);
                        int[] live = mesh.GetIndices(submesh, false);
                        if (stored.topology != mesh.GetTopology(submesh) ||
                            liveBase > int.MaxValue ||
                            stored.baseVertex != (int)liveBase ||
                            live.Length != stored.indices.Length)
                        {
                            exact = false;
                            break;
                        }
                        for (int index = 0; index < live.Length; index++)
                        {
                            if (live[index] == stored.indices[index])
                                continue;
                            exact = false;
                            break;
                        }
                        if (!exact)
                            break;
                    }
                }
                if (exact)
                    return true;
                // Preserve the existing safety rule: auto-repair only Unity's
                // collapsed-submesh signature, not arbitrary external edits.
                if (liveTotal != storedTotal ||
                    mesh.subMeshCount >= storedSubmeshesV2.Length)
                {
                    return true;
                }
                if (!LiveVerticesMatch(mesh, storedVertices))
                    return true;
                return !FlattenedTriangleIndicesMatch(
                    mesh,
                    storedSubmeshesV2,
                    storedTotal);
            }

            int storedIndexCount = storedTriangles?.Length ?? 0;
            if (!HasValidSubmeshCounts(
                    storedSubmeshCounts,
                    storedIndexCount))
            {
                // Upgrade older storage data while its original multi-submesh
                // mesh is still intact. Once Unity has already collapsed that
                // mesh, the material-to-face partition cannot be inferred from
                // a flattened triangle array alone.
                if (!TryCaptureLegacySubmeshCounts(
                        mesh,
                        storedTriangles,
                        useCurrent,
                        out storedSubmeshCounts))
                {
                    WarnAboutMissingSubmeshData(mesh);
                    return true;
                }
            }

            // Do not silently replace an intentionally reassigned or externally
            // edited mesh merely because this component still contains older
            // storage. Automatic repair is limited to Unity's prefab-collapse
            // signature: the same geometry and flattened indices survived, but
            // fewer submeshes did not.
            if (mesh.vertexCount != storedVertices.Length)
                return true;

            bool submeshCountsMatch =
                mesh.subMeshCount == storedSubmeshCounts.Length;
            long liveIndexCount = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                long count = (long)mesh.GetIndexCount(submesh);
                liveIndexCount += count;
                if (submeshCountsMatch &&
                    count != storedSubmeshCounts[submesh])
                {
                    submeshCountsMatch = false;
                }
            }
            if (liveIndexCount != storedIndexCount)
                return true;

            // Counts catch the common prefab failure. Comparing the flattened
            // indices as well protects material ordering when two submeshes
            // happen to contain the same number of indices.
            if (storedTriangles == null || !mesh.isReadable)
                return true;

            int[] liveTriangles;
            try
            {
                liveTriangles = mesh.triangles;
            }
            catch (UnityException)
            {
                return true;
            }

            if (liveTriangles.Length != storedTriangles.Length)
                return true;
            for (int i = 0; i < liveTriangles.Length; i++)
            {
                if (liveTriangles[i] != storedTriangles[i])
                    return true;
            }

            warnedAboutMissingSubmeshData = false;
            if (submeshCountsMatch)
                return true;

            if (mesh.subMeshCount >= storedSubmeshCounts.Length)
                return true;
            return !LiveVerticesMatch(mesh, storedVertices);
        }

        private static bool LiveVerticesMatch(Mesh mesh, Vector3[] storedVertices)
        {
            if (mesh == null || !mesh.isReadable ||
                storedVertices == null || mesh.vertexCount != storedVertices.Length)
            {
                return false;
            }
            Vector3[] live;
            try
            {
                live = mesh.vertices;
            }
            catch (UnityException)
            {
                return false;
            }
            for (int vertex = 0; vertex < live.Length; vertex++)
                if (live[vertex] != storedVertices[vertex])
                    return false;
            return true;
        }

        private static bool FlattenedTriangleIndicesMatch(
            Mesh mesh,
            RealBlendStoredIndexBuffer[] storedSubmeshes,
            long expectedCount)
        {
            if (mesh == null || !mesh.isReadable ||
                expectedCount < 0 || expectedCount > int.MaxValue)
            {
                return false;
            }
            List<int> stored = new List<int>((int)expectedCount);
            for (int submesh = 0; submesh < storedSubmeshes.Length; submesh++)
            {
                RealBlendStoredIndexBuffer buffer = storedSubmeshes[submesh];
                if (buffer == null ||
                    buffer.topology != MeshTopology.Triangles ||
                    buffer.indices == null)
                {
                    return false;
                }
                for (int index = 0; index < buffer.indices.Length; index++)
                {
                    long applied = (long)buffer.indices[index] + buffer.baseVertex;
                    if (applied < 0 || applied > int.MaxValue)
                        return false;
                    stored.Add((int)applied);
                }
            }

            List<int> live = new List<int>((int)expectedCount);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    return false;
                live.AddRange(mesh.GetIndices(submesh, true));
            }
            if (live.Count != stored.Count)
                return false;
            for (int index = 0; index < live.Count; index++)
                if (live[index] != stored[index])
                    return false;
            return true;
        }

        private void SelectStoredTopology(
            out Vector3[] vertices,
            out int[] triangles,
            out int[] submeshCounts,
            out bool useCurrent)
        {
            bool hasCurrentTopology =
                currentVertices != null &&
                currentVertices.Length > 0 &&
                currentTriangles != null;
            bool hasBaseTopology =
                baseVertices != null &&
                baseVertices.Length > 0 &&
                baseTriangles != null;

            useCurrent = hasCurrentTopology || !hasBaseTopology;
            vertices = useCurrent ? currentVertices : baseVertices;
            triangles = useCurrent ? currentTriangles : baseTriangles;
            submeshCounts = useCurrent
                ? currentSubmeshIndexCounts
                : baseSubmeshIndexCounts;
        }

        private static bool HasValidSubmeshCounts(
            int[] submeshCounts,
            int totalIndexCount)
        {
            if (submeshCounts == null || submeshCounts.Length == 0)
                return false;

            long total = 0;
            for (int i = 0; i < submeshCounts.Length; i++)
            {
                int count = submeshCounts[i];
                if (count < 0 || count % 3 != 0)
                    return false;
                total += count;
            }
            return total == totalIndexCount;
        }

        private bool TryCaptureLegacySubmeshCounts(
            Mesh mesh,
            int[] storedTriangles,
            bool useCurrent,
            out int[] capturedCounts)
        {
            capturedCounts = null;
            if (mesh == null || storedTriangles == null || !mesh.isReadable)
                return false;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            int materialCount = renderer != null
                ? renderer.sharedMaterials.Length
                : 0;
            if (mesh.subMeshCount <= 0 ||
                (mesh.subMeshCount == 1 && materialCount > 1))
            {
                return false;
            }

            int[] liveTriangles;
            try
            {
                liveTriangles = mesh.triangles;
            }
            catch (UnityException)
            {
                return false;
            }

            if (liveTriangles.Length != storedTriangles.Length)
                return false;
            for (int i = 0; i < liveTriangles.Length; i++)
            {
                if (liveTriangles[i] != storedTriangles[i])
                    return false;
            }

            capturedCounts = CaptureSubmeshIndexCounts(mesh);
            if (!HasValidSubmeshCounts(
                    capturedCounts,
                    storedTriangles.Length))
            {
                capturedCounts = null;
                return false;
            }

            if (useCurrent)
                currentSubmeshIndexCounts = (int[])capturedCounts.Clone();
            else
                baseSubmeshIndexCounts = (int[])capturedCounts.Clone();

            // A paint-only legacy component normally captured identical base
            // and current topology. Migrate the counterpart only when both its
            // vertex data and flattened indices are exactly identical; sculpt
            // or Boolean edits may have the same index count with a different
            // material partition and must never inherit these boundaries.
            if (useCurrent &&
                !HasValidSubmeshCounts(
                    baseSubmeshIndexCounts,
                    baseTriangles?.Length ?? 0) &&
                StoredTopologyExactlyMatches(
                    currentVertices,
                    currentTriangles,
                    baseVertices,
                    baseTriangles))
            {
                baseSubmeshIndexCounts = (int[])capturedCounts.Clone();
            }
            else if (!useCurrent &&
                     !HasValidSubmeshCounts(
                         currentSubmeshIndexCounts,
                         currentTriangles?.Length ?? 0) &&
                     StoredTopologyExactlyMatches(
                         baseVertices,
                         baseTriangles,
                         currentVertices,
                         currentTriangles))
            {
                currentSubmeshIndexCounts = (int[])capturedCounts.Clone();
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this))
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    gameObject.scene);
            }
#endif
            warnedAboutMissingSubmeshData = false;
            return true;
        }

        private static bool StoredTopologyExactlyMatches(
            Vector3[] firstVertices,
            int[] firstTriangles,
            Vector3[] secondVertices,
            int[] secondTriangles)
        {
            if (firstVertices == null || secondVertices == null ||
                firstTriangles == null || secondTriangles == null ||
                firstVertices.Length != secondVertices.Length ||
                firstTriangles.Length != secondTriangles.Length)
            {
                return false;
            }

            for (int i = 0; i < firstVertices.Length; i++)
            {
                if (firstVertices[i] != secondVertices[i])
                    return false;
            }
            for (int i = 0; i < firstTriangles.Length; i++)
            {
                if (firstTriangles[i] != secondTriangles[i])
                    return false;
            }
            return true;
        }

        private void WarnAboutMissingSubmeshData(Mesh mesh)
        {
            if (warnedAboutMissingSubmeshData)
                return;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            int materialCount = renderer != null
                ? renderer.sharedMaterials.Length
                : 0;
            if (materialCount <= 1 || mesh == null || mesh.subMeshCount > 1)
                return;
            warnedAboutMissingSubmeshData = true;

            Debug.LogWarning(
                $"RealBlend cannot recover the material-to-face assignments on '{name}' because this legacy paint data has no submesh metadata and the mesh is already collapsed to one submesh. Restore the original multi-submesh mesh, paint once with the current RealBlend version, then create or bake the prefab again.",
                this);
        }

        public void RebuildMesh()
        {
            if (HasProBuilderMeshComponent())
            {
                Debug.LogWarning(
                    $"RealBlend did not rebuild '{name}' from VertexPaintStorage because ProBuilder owns this mesh. Use 'Sync From ProBuilder' in the component Inspector instead.",
                    this);
                return;
            }

            var mf = GetComponent<MeshFilter>();
            if (!mf) return;
            Mesh previousAssignedMesh = mf.sharedMesh;
            Mesh previousRuntimeMesh = _runtimeMesh;

            SelectStoredTopology(
                out Vector3[] verts,
                out int[] tris,
                out int[] submeshCounts,
                out bool useCurrent);
            var uvs = useCurrent && currentUVs?.Length > 0
                ? currentUVs
                : baseUVs;
            RealBlendStoredIndexBuffer[] storedSubmeshes = useCurrent
                ? currentSubmeshesV2
                : baseSubmeshesV2;
            Vector3[] storedNormals = useCurrent
                ? currentNormals
                : baseNormals;
            Vector4[] storedTangents = useCurrent
                ? currentTangents
                : baseTangents;
            RealBlendStoredUVChannel[] storedUVChannels = useCurrent
                ? currentUVChannels
                : baseUVChannels;

            // If we still have nothing, we can't build
            if (verts == null || verts.Length == 0) return;

            Mesh previousOwnedMesh = _ownsRuntimeMesh ? _runtimeMesh : null;
            Mesh rebuiltMesh = new Mesh();
            rebuiltMesh.name = "Painted_Runtime_Mesh";
            rebuiltMesh.indexFormat =
                verts.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
            rebuiltMesh.vertices = verts;
            if (RealBlendStoredMeshDataUtility.HasCompleteSubmeshes(storedSubmeshes))
                RealBlendStoredMeshDataUtility.ApplySubmeshes(rebuiltMesh, storedSubmeshes);
            else
                ApplyTriangleTopology(
                    rebuiltMesh,
                    tris,
                    submeshCounts);
            RealBlendStoredMeshDataUtility.ApplyUVChannels(
                rebuiltMesh,
                storedUVChannels,
                verts.Length);
            if (!RealBlendStoredMeshDataUtility.HasUVChannel(
                    storedUVChannels,
                    0,
                    verts.Length) &&
                uvs != null && uvs.Length == verts.Length)
            {
                rebuiltMesh.uv = uvs;
            }

            // Ensure colors match vertex count before assigning
            if (paintedColors != null && paintedColors.Length == verts.Length)
                rebuiltMesh.colors = paintedColors;

            // Rebuilding must reproduce the stored vertex/index topology
            // exactly. Generating lightmap UVs here can split vertices and
            // rewrite submesh indices, which breaks stored vertex colors and
            // material-to-face assignments. The explicit Bake to Asset action
            // may still generate UV1 on the permanent baked copy.
            if (storedNormals != null && storedNormals.Length == verts.Length)
                rebuiltMesh.normals = storedNormals;
            else
                rebuiltMesh.RecalculateNormals();
            if (storedTangents != null && storedTangents.Length == verts.Length)
                rebuiltMesh.tangents = storedTangents;
            else if (rebuiltMesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                rebuiltMesh.RecalculateTangents();
            rebuiltMesh.RecalculateBounds();

            _runtimeMesh = rebuiltMesh;
#if !UNITY_EDITOR
            _runtimeSourceMesh = null;
#endif
            _ownsRuntimeMesh = true;
            ownedEditableMesh = rebuiltMesh;
            mf.sharedMesh = rebuiltMesh;
            RebindOwnedMeshColliders(mf, previousAssignedMesh, rebuiltMesh);
            if (previousRuntimeMesh != previousAssignedMesh)
                RebindOwnedMeshColliders(mf, previousRuntimeMesh, rebuiltMesh);
            DestroyOwnedRuntimeMeshIfUnused(previousOwnedMesh);
        }

        public void ApplyColors()
        {
            if (HasProBuilderMeshComponent())
                return;

            var mf = GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh || paintedColors == null || paintedColors.Length != mf.sharedMesh.vertexCount) return;

#if UNITY_EDITOR
            Mesh target = CanSafelyApplyEditorColors(mf)
                ? mf.sharedMesh
                : null;
#else
            Mesh target = EnsureRuntimeColorMesh(mf);
#endif
            if (target == null || paintedColors.Length != target.vertexCount)
                return;
            target.colors = paintedColors;
            target.UploadMeshData(false);
        }

#if !UNITY_EDITOR
        private Mesh EnsureRuntimeColorMesh(MeshFilter filter)
        {
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null)
                return null;

            if (_ownsRuntimeMesh && _runtimeMesh == source)
                return source;

            Mesh previousOwnedMesh = _ownsRuntimeMesh ? _runtimeMesh : null;
            Mesh editable = Instantiate(source);
            editable.name = source.name + " (RealBlend Runtime)";

            _runtimeMesh = editable;
            _runtimeSourceMesh = source;
            _ownsRuntimeMesh = true;
            ownedEditableMesh = editable;
            filter.sharedMesh = editable;
            RebindOwnedMeshColliders(filter, source, editable);

            DestroyOwnedRuntimeMeshIfUnused(previousOwnedMesh);
            return editable;
        }

        private void ReleaseOwnedRuntimeMesh(bool restoreSource)
        {
            if (!_ownsRuntimeMesh || _runtimeMesh == null)
                return;

            Mesh ownedMesh = _runtimeMesh;
            MeshFilter filter = GetComponent<MeshFilter>();
            if (restoreSource)
            {
                if (filter != null && filter.sharedMesh == ownedMesh)
                    filter.sharedMesh = _runtimeSourceMesh;
                RebindOwnedMeshColliders(
                    filter,
                    ownedMesh,
                    _runtimeSourceMesh);
            }

            _runtimeMesh = null;
            _runtimeSourceMesh = null;
            _ownsRuntimeMesh = false;
            DestroyOwnedRuntimeMeshIfUnused(ownedMesh);
        }
#endif

        /// <summary>
        /// Rebinds collision proxies that belong to this MeshFilter whenever a
        /// stored mesh is reconstructed. Capturing the filter's assigned mesh
        /// makes this work after domain reload, when runtime fields are null.
        /// A collider beneath another MeshFilter is independently owned and is
        /// deliberately left unchanged.
        /// </summary>
        private static void RebindOwnedMeshColliders(
            MeshFilter owner,
            Mesh previousMesh,
            Mesh replacementMesh)
        {
            if (owner == null || previousMesh == null)
                return;

            MeshCollider[] colliders = Resources.FindObjectsOfTypeAll<MeshCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider collider = colliders[i];
                if (collider == null ||
                    collider.sharedMesh != previousMesh ||
                    !collider.gameObject.scene.IsValid() ||
                    !collider.gameObject.scene.isLoaded ||
                    collider.gameObject.scene != owner.gameObject.scene ||
                    collider.transform.root != owner.transform.root)
                {
                    continue;
                }

                MeshFilter nearestOwner = null;
                Transform current = collider.transform;
                while (current != null)
                {
                    nearestOwner = current.GetComponent<MeshFilter>();
                    if (nearestOwner != null)
                        break;
                    current = current.parent;
                }
                if (nearestOwner != null && nearestOwner != owner)
                    continue;

                collider.sharedMesh = null;
                if (replacementMesh != null)
                    collider.sharedMesh = replacementMesh;
            }
        }

        private static void DestroyOwnedRuntimeMeshIfUnused(Mesh mesh)
        {
            if (mesh == null || IsRuntimeMeshReferenced(mesh))
                return;

            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        private static bool IsRuntimeMeshReferenced(Mesh mesh)
        {
            MeshFilter[] filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] != null && filters[i].sharedMesh == mesh)
                    return true;
            }

            MeshCollider[] colliders = Resources.FindObjectsOfTypeAll<MeshCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].sharedMesh == mesh)
                    return true;
            }

            return false;
        }

#if UNITY_EDITOR
        private bool CanSafelyApplyEditorColors(MeshFilter filter)
        {
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null)
                return false;
            if (EditorUtility.IsPersistent(source))
                return false;

            MeshFilter[] filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter candidate = filters[i];
                if (candidate == null || candidate == filter ||
                    candidate.sharedMesh != source ||
                    EditorUtility.IsPersistent(candidate.gameObject) ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                {
                    continue;
                }
                return false;
            }
            return true;
        }
#endif

        /// <summary>
        /// Persists a paint-only edit. Geometry is compared before the color
        /// snapshot is stored and is only recopied when another tool changed a
        /// restorable stream since RealBlend last acquired the mesh.
        /// </summary>
        public void SavePaintedColors(Mesh mesh, Color[] colors)
        {
            if (mesh == null || colors == null || colors.Length != mesh.vertexCount)
                return;

            // Paint is color-only, but it is also the most common point at
            // which a mesh returns to RealBlend after being edited elsewhere.
            // Adopt the live geometry first so a later rebuild can never revive
            // a stale pre-sculpt/pre-tool snapshot. This comparison deliberately
            // ignores colors because the live mesh already contains this stroke.
            if (!MatchesCurrentGeometry(mesh))
                CaptureCurrentGeometry(mesh);

            paintedColors = (Color[])colors.Clone();
            paintedColorRevision++;
        }

        public void SetPaintedColorsForUndo(Color[] colors, int revision)
        {
            if (colors == null)
                return;

            paintedColors = (Color[])colors.Clone();
            paintedColorRevision = revision;
        }

        public void SetOwnedEditableMesh(Mesh mesh)
        {
            ownedEditableMesh = mesh;
        }

        public void SetGeneratedSolidWall(bool value)
        {
            generatedSolidWall = value;
        }

        public int GetGeneratedInteriorSubmeshAt(int index)
        {
            return generatedInteriorSubmeshes != null &&
                   index >= 0 &&
                   index < generatedInteriorSubmeshes.Length &&
                   generatedInteriorSubmeshes[index] != null
                ? generatedInteriorSubmeshes[index].submesh
                : -1;
        }

        public bool TryGetGeneratedInteriorSubmeshAt(
            Mesh mesh,
            int index,
            out int submesh)
        {
            submesh = GetGeneratedInteriorSubmeshAt(index);
            if (mesh == null ||
                !mesh.isReadable ||
                submesh < 0 ||
                submesh >= mesh.subMeshCount ||
                mesh.GetTopology(submesh) != MeshTopology.Triangles)
            {
                submesh = -1;
                return false;
            }

            RealBlendGeneratedInteriorSubmeshRecord record =
                generatedInteriorSubmeshes[index];
            if (record.indexSignature !=
                ComputeSubmeshIndexSignature(mesh, submesh))
            {
                submesh = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Re-signs provenance after a RealBlend-controlled topology rewrite.
        /// Call ValidateGeneratedInteriorSubmeshes against the pre-edit mesh
        /// first, inside the same Undo group, so stale external slots are never
        /// promoted into trusted generated geometry.
        /// </summary>
        public void CommitGeneratedInteriorSubmeshTopology(
            Mesh mesh,
            int generatedSubmesh = -1)
        {
            if (mesh == null || !mesh.isReadable)
                return;

            List<RealBlendGeneratedInteriorSubmeshRecord> records =
                new List<RealBlendGeneratedInteriorSubmeshRecord>();
            if (generatedInteriorSubmeshes != null)
            {
                for (int i = 0; i < generatedInteriorSubmeshes.Length; i++)
                {
                    RealBlendGeneratedInteriorSubmeshRecord existing =
                        generatedInteriorSubmeshes[i];
                    int existingSubmesh = existing != null
                        ? existing.submesh
                        : -1;
                    if (existingSubmesh < 0 ||
                        existingSubmesh == generatedSubmesh ||
                        existingSubmesh >= mesh.subMeshCount ||
                        mesh.GetTopology(existingSubmesh) != MeshTopology.Triangles)
                    {
                        continue;
                    }

                    records.Add(new RealBlendGeneratedInteriorSubmeshRecord
                    {
                        submesh = existingSubmesh,
                        indexSignature = ComputeSubmeshIndexSignature(
                            mesh,
                            existingSubmesh)
                    });
                }
            }

            if (generatedSubmesh >= 0 &&
                generatedSubmesh < mesh.subMeshCount &&
                mesh.GetTopology(generatedSubmesh) == MeshTopology.Triangles)
            {
                records.Add(new RealBlendGeneratedInteriorSubmeshRecord
                {
                    submesh = generatedSubmesh,
                    indexSignature = ComputeSubmeshIndexSignature(
                        mesh,
                        generatedSubmesh)
                });
            }

            generatedInteriorSubmeshes = records.Count > 0
                ? records.ToArray()
                : null;
        }

        public void ValidateGeneratedInteriorSubmeshes(Mesh mesh)
        {
            if (mesh == null ||
                !mesh.isReadable ||
                generatedInteriorSubmeshes == null ||
                generatedInteriorSubmeshes.Length == 0)
            {
                return;
            }

            List<RealBlendGeneratedInteriorSubmeshRecord> validRecords =
                new List<RealBlendGeneratedInteriorSubmeshRecord>(
                    generatedInteriorSubmeshes.Length);
            for (int i = 0; i < generatedInteriorSubmeshes.Length; i++)
            {
                RealBlendGeneratedInteriorSubmeshRecord record =
                    generatedInteriorSubmeshes[i];
                if (record == null ||
                    record.submesh < 0 ||
                    record.submesh >= mesh.subMeshCount ||
                    mesh.GetTopology(record.submesh) != MeshTopology.Triangles ||
                    record.indexSignature !=
                        ComputeSubmeshIndexSignature(mesh, record.submesh))
                {
                    continue;
                }

                validRecords.Add(record);
            }

            if (validRecords.Count == generatedInteriorSubmeshes.Length)
                return;

            generatedInteriorSubmeshes = validRecords.Count > 0
                ? validRecords.ToArray()
                : null;
        }

        public void ClearGeneratedInteriorSubmeshes()
        {
            generatedInteriorSubmeshes = null;
        }

        private static long ComputeSubmeshIndexSignature(
            Mesh mesh,
            int submesh)
        {
            // FNV-1a over the complete raw index layout. Positions are omitted
            // deliberately so normal sculpt deformation keeps provenance, while
            // reordering/replacing topology invalidates the generated slot.
            ulong hash = 1469598103934665603UL;
            HashGeneratedInteriorValue(
                ref hash,
                (int)mesh.GetTopology(submesh));
            HashGeneratedInteriorValue(
                ref hash,
                unchecked((int)mesh.GetBaseVertex(submesh)));

            int[] indices = mesh.GetIndices(submesh, false);
            HashGeneratedInteriorValue(ref hash, indices.Length);
            for (int i = 0; i < indices.Length; i++)
                HashGeneratedInteriorValue(ref hash, indices[i]);

            return unchecked((long)hash);
        }

        private static void HashGeneratedInteriorValue(
            ref ulong hash,
            int value)
        {
            const ulong prime = 1099511628211UL;
            uint unsignedValue = unchecked((uint)value);
            for (int byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
            {
                hash ^= (byte)(unsignedValue >> (byteIndex * 8));
                hash *= prime;
            }
        }

        /// <summary>
        /// Compares every stored non-color stream that RebuildMesh can restore.
        /// This is intentionally stricter than a vertex/index-count check: two
        /// meshes can have identical counts while containing different sculpted
        /// positions, triangle assignments, UVs, normals, or tangents.
        /// </summary>
        public bool MatchesCurrentGeometry(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable)
            {
                return false;
            }

            return ArraysEqual(currentVertices, mesh.vertices) &&
                   ArraysEqual(
                       currentTriangles,
                       RealBlendStoredMeshDataUtility.CaptureLegacyTriangles(mesh)) &&
                   ArraysEqual(
                       currentSubmeshIndexCounts,
                       CaptureSubmeshIndexCounts(mesh)) &&
                   StoredSubmeshesEqual(
                       currentSubmeshesV2,
                       RealBlendStoredMeshDataUtility.CaptureSubmeshes(mesh)) &&
                   ArraysEqual(currentUVs, mesh.uv) &&
                   StoredUVChannelsEqual(
                       currentUVChannels,
                       RealBlendStoredMeshDataUtility.CaptureUVChannels(mesh)) &&
                   ArraysEqual(currentNormals, CaptureNormals(mesh)) &&
                   ArraysEqual(currentTangents, CaptureTangents(mesh));
        }

        /// <summary>
        /// Checks whether vertex indices still have the same identity/layout as
        /// the stored paint snapshot without reading positions, UVs, normals, or
        /// tangents. ProBuilder uses this to distinguish a deformation (colors
        /// follow their indices) from a vertex reorder (colors need a spatial
        /// remap from the stored positions).
        /// </summary>
        public bool MatchesCurrentIndexLayout(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable ||
                currentVertices == null ||
                currentVertices.Length != mesh.vertexCount)
            {
                return false;
            }

            if (RealBlendStoredMeshDataUtility.HasCompleteSubmeshes(
                    currentSubmeshesV2))
            {
                if (mesh.subMeshCount != currentSubmeshesV2.Length)
                    return false;
                for (int submesh = 0;
                     submesh < currentSubmeshesV2.Length;
                     submesh++)
                {
                    RealBlendStoredIndexBuffer stored =
                        currentSubmeshesV2[submesh];
                    uint liveBaseVertex = mesh.GetBaseVertex(submesh);
                    if (stored.baseVertex < 0 ||
                        liveBaseVertex != (uint)stored.baseVertex ||
                        mesh.GetTopology(submesh) != stored.topology ||
                        mesh.GetIndexCount(submesh) !=
                            (uint)(stored.indices?.Length ?? 0) ||
                        !ArraysEqual(
                            stored.indices,
                            mesh.GetIndices(submesh, false)))
                    {
                        return false;
                    }
                }
                return true;
            }

            // Legacy components predate the lossless per-submesh snapshot.
            // Their flattened triangles plus recorded submesh counts are the
            // strongest layout identity available.
            return ArraysEqual(
                       currentTriangles,
                       RealBlendStoredMeshDataUtility.CaptureLegacyTriangles(mesh)) &&
                   ArraysEqual(
                       currentSubmeshIndexCounts,
                       CaptureSubmeshIndexCounts(mesh));
        }

        // --- CRITICAL FIXES START HERE ---

        public void SaveCurrentState(Mesh mesh)
        {
            if (mesh == null) return;
            CaptureCurrentGeometry(mesh);
            paintedColors = mesh.colors;
        }

        /// <summary>
        /// Makes the live mesh authoritative for geometry while leaving stored
        /// paint untouched. Paint uses this before applying its chosen colors,
        /// so hover previews can never become persistent by accident.
        /// </summary>
        public void CaptureCurrentGeometry(Mesh mesh)
        {
            if (mesh == null) return;
            ValidateGeneratedInteriorSubmeshes(mesh);
            currentVertices = mesh.vertices;
            currentTriangles = RealBlendStoredMeshDataUtility.CaptureLegacyTriangles(mesh);
            currentSubmeshIndexCounts = CaptureSubmeshIndexCounts(mesh);
            currentUVs = mesh.uv;
            currentSubmeshesV2 = RealBlendStoredMeshDataUtility.CaptureSubmeshes(mesh);
            currentNormals = CaptureNormals(mesh);
            currentTangents = CaptureTangents(mesh);
            currentUVChannels = RealBlendStoredMeshDataUtility.CaptureUVChannels(mesh);
        }

        /// <summary>
        /// Checks the serialized working snapshot without changing ownership or
        /// dirtying the scene. The ProBuilder editor bridge uses this during
        /// reload/Undo synchronization so an already-current companion does not
        /// create prefab overrides just because scripts recompiled.
        /// </summary>
        public bool MatchesCurrentState(Mesh mesh)
        {
            if (mesh == null ||
                !ArraysEqual(currentVertices, mesh.vertices) ||
                !ArraysEqual(
                    currentTriangles,
                    RealBlendStoredMeshDataUtility.CaptureLegacyTriangles(mesh)) ||
                !ArraysEqual(
                    currentSubmeshIndexCounts,
                    CaptureSubmeshIndexCounts(mesh)) ||
                !StoredSubmeshesEqual(
                    currentSubmeshesV2,
                    RealBlendStoredMeshDataUtility.CaptureSubmeshes(mesh)) ||
                !ArraysEqual(paintedColors, mesh.colors))
            {
                return false;
            }

            // ProBuilder owns UVs, normals, and tangents. Those generated
            // channels can be added/removed or recalculated during an otherwise
            // in-sync editor refresh (UV1 in particular), so they are not a
            // reliable reason to dirty companion storage after reload/Undo.
            // Explicit ProBuilder edit callbacks still SaveCurrentState and
            // capture every channel; this no-op check keys off stable topology
            // and paint data only.
            return true;
        }

        private static bool StoredSubmeshesEqual(
            RealBlendStoredIndexBuffer[] left,
            RealBlendStoredIndexBuffer[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                RealBlendStoredIndexBuffer a = left[i];
                RealBlendStoredIndexBuffer b = right[i];
                if (a == null || b == null)
                {
                    if (!ReferenceEquals(a, b))
                        return false;
                    continue;
                }
                if (a.topology != b.topology ||
                    a.baseVertex != b.baseVertex ||
                    !ArraysEqual(a.indices, b.indices))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StoredUVChannelsEqual(
            RealBlendStoredUVChannel[] left,
            RealBlendStoredUVChannel[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int channel = 0; channel < left.Length; channel++)
            {
                RealBlendStoredUVChannel a = left[channel];
                RealBlendStoredUVChannel b = right[channel];
                if (a == null || b == null)
                {
                    if (!ReferenceEquals(a, b))
                        return false;
                    continue;
                }
                if (a.dimension != b.dimension ||
                    !ArraysEqual(a.values, b.values))
                {
                    return false;
                }
            }
            return true;
        }

        private static Vector3[] CaptureNormals(Mesh mesh)
        {
            Vector3[] normals = mesh != null ? mesh.normals : null;
            return mesh != null && normals != null && normals.Length == mesh.vertexCount
                ? normals
                : null;
        }

        private static Vector4[] CaptureTangents(Mesh mesh)
        {
            Vector4[] tangents = mesh != null ? mesh.tangents : null;
            return mesh != null && tangents != null && tangents.Length == mesh.vertexCount
                ? tangents
                : null;
        }

        private static bool ArraysEqual<T>(T[] left, T[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < left.Length; i++)
            {
                if (!comparer.Equals(left[i], right[i]))
                    return false;
            }
            return true;
        }

        private static bool ArraysEqual(Vector3[] left, Vector3[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!left[i].x.Equals(right[i].x) ||
                    !left[i].y.Equals(right[i].y) ||
                    !left[i].z.Equals(right[i].z))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ArraysEqual(Color[] left, Color[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (!left[i].r.Equals(right[i].r) ||
                    !left[i].g.Equals(right[i].g) ||
                    !left[i].b.Equals(right[i].b) ||
                    !left[i].a.Equals(right[i].a))
                {
                    return false;
                }
            }
            return true;
        }

        public void SetBaseTopology(Mesh mesh)
        {
            if (mesh == null) return;
            generatedInteriorSubmeshes = null;
            baseVertices = mesh.vertices;
            baseTriangles = RealBlendStoredMeshDataUtility.CaptureLegacyTriangles(mesh);
            baseSubmeshIndexCounts = CaptureSubmeshIndexCounts(mesh);
            baseUVs = mesh.uv;
            baseSubmeshesV2 = RealBlendStoredMeshDataUtility.CaptureSubmeshes(mesh);
            Vector3[] normals = mesh.normals;
            baseNormals = normals != null && normals.Length == mesh.vertexCount
                ? normals
                : null;
            Vector4[] tangents = mesh.tangents;
            baseTangents = tangents != null && tangents.Length == mesh.vertexCount
                ? tangents
                : null;
            baseUVChannels = RealBlendStoredMeshDataUtility.CaptureUVChannels(mesh);
            hasBaseData = true; // This flag enables the Revert button logic

            // Also initialize current state to match base so we aren't null
            SaveCurrentState(mesh);
        }

        public void RestoreOriginalTopology()
        {
            if (HasProBuilderMeshComponent())
            {
                Debug.LogWarning(
                    $"RealBlend did not restore stored topology on '{name}' because ProBuilder owns this mesh.",
                    this);
                return;
            }
            if (!hasBaseData) return;
            currentVertices = baseVertices;
            currentTriangles = baseTriangles;
            currentSubmeshIndexCounts =
                baseSubmeshIndexCounts != null
                    ? (int[])baseSubmeshIndexCounts.Clone()
                    : null;
            currentUVs = baseUVs;
            currentSubmeshesV2 = baseSubmeshesV2;
            currentNormals = baseNormals;
            currentTangents = baseTangents;
            currentUVChannels = baseUVChannels;
            generatedInteriorSubmeshes = null;
            RebuildMesh();
        }

        // --- CRITICAL FIXES END HERE ---

        public void CaptureOriginals(Mesh mesh) => SetBaseTopology(mesh);

        private static int[] CaptureSubmeshIndexCounts(Mesh mesh)
        {
            if (mesh == null || mesh.subMeshCount <= 0)
                return null;

            int[] counts = new int[mesh.subMeshCount];
            for (int submesh = 0; submesh < counts.Length; submesh++)
            {
                long count = (long)mesh.GetIndexCount(submesh);
                if (count < 0 || count > int.MaxValue)
                    return null;
                counts[submesh] = (int)count;
            }

            return counts;
        }

        private static void ApplyTriangleTopology(
            Mesh mesh,
            int[] triangles,
            int[] submeshIndexCounts)
        {
            if (mesh == null)
                return;

            int[] safeTriangles = triangles ?? System.Array.Empty<int>();
            int total = 0;
            bool validSubmeshes =
                submeshIndexCounts != null &&
                submeshIndexCounts.Length > 0;
            if (validSubmeshes)
            {
                for (int i = 0; i < submeshIndexCounts.Length; i++)
                {
                    int count = submeshIndexCounts[i];
                    if (count < 0 ||
                        count % 3 != 0 ||
                        total > safeTriangles.Length - count)
                    {
                        validSubmeshes = false;
                        break;
                    }
                    total += count;
                }
                validSubmeshes &= total == safeTriangles.Length;
            }

            if (!validSubmeshes)
            {
                mesh.triangles = safeTriangles;
                return;
            }

            mesh.subMeshCount = submeshIndexCounts.Length;
            int offset = 0;
            for (int submesh = 0;
                 submesh < submeshIndexCounts.Length;
                 submesh++)
            {
                int count = submeshIndexCounts[submesh];
                int[] indices = new int[count];
                if (count > 0)
                    System.Array.Copy(safeTriangles, offset, indices, 0, count);
                mesh.SetTriangles(indices, submesh, false);
                offset += count;
            }
        }

#if UNITY_EDITOR
        public void BakeMeshAsset()
        {
            if (HasProBuilderMeshComponent())
            {
                EditorUtility.DisplayDialog(
                    "ProBuilder Mesh Is Still Editable",
                    "ProBuilder owns this MeshFilter and can regenerate it. Finalize or remove the ProBuilder component before baking RealBlend paint to a permanent mesh asset.",
                    "OK");
                return;
            }

            var mf = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (!mf || !mf.sharedMesh) { Debug.LogWarning("Nothing to bake"); return; }

            string dir = GlobalBakePath;
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = System.IO.Path.GetDirectoryName(dir).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(dir);
                AssetDatabase.CreateFolder(parent == "Assets" || string.IsNullOrEmpty(parent) ? "Assets" : parent, folder);
            }

            string fileName = $"{gameObject.name}_Baked.asset";
            string fullPath = System.IO.Path.Combine(dir, fileName).Replace("\\", "/");
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            // Keep the pre-unwrap topology available for provenance validation.
            // Secondary-UV generation can split/reindex the baked clone.
            Mesh sourceMesh = mf.sharedMesh;
            Mesh bakedMesh = Instantiate(sourceMesh);
            bakedMesh.name = gameObject.name + "_Baked";
            RealBlendMeshUVUtility.GenerateLightmapUVs(bakedMesh);

            AssetDatabase.CreateAsset(bakedMesh, assetPath);
            AssetDatabase.SaveAssets();

            // Record every reference before changing it. Recording after the
            // assignment makes Undo capture the baked mesh as both the old and
            // new value, which can leave the MeshFilter/collider out of sync.
            MeshFilter mfInstance = mf;
            var collider = GetComponent<MeshCollider>();
            Undo.RecordObject(mfInstance, "Bake Vertex Colors");
            if (collider)
                Undo.RecordObject(collider, "Bake Vertex Colors");
            Undo.RecordObject(this, "Bake Vertex Colors");
            ValidateGeneratedInteriorSubmeshes(sourceMesh);

            mfInstance.sharedMesh = bakedMesh;
            if (collider)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = bakedMesh;
            }
            SetOwnedEditableMesh(bakedMesh);
            CommitGeneratedInteriorSubmeshTopology(bakedMesh, -1);
            SaveCurrentState(bakedMesh);

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(mfInstance);
                if (collider)
                    EditorUtility.SetDirty(collider);
                EditorUtility.SetDirty(this);
                PrefabUtility.RecordPrefabInstancePropertyModifications(mfInstance);
                if (collider)
                    PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }

            Debug.Log($"Vertex colors baked permanently: {assetPath}");

            // Ask to remove component
            if (EditorUtility.DisplayDialog("Bake Complete!",
                $"Mesh with vertex colors saved to:\n{assetPath}\n\n" +
                "The GameObject now uses the permanent baked mesh.\n" +
                "Remove VertexPaintStorage component?",
                "Yes", "No"))
            {
                // DESTROY SAFELY (fixes MissingReferenceException)
                DestroyComponentSafely();
            }
        }

        // SAFELY DESTROY (prevents editor crash)
        private void DestroyComponentSafely()
        {
            if (this == null) return;

#if UNITY_EDITOR
            // Delay destruction so Inspector can finish drawing
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Undo.DestroyObjectImmediate(this);
            };
#else
        Destroy(this);
#endif
        }
#endif
    }

    public static class RealBlendMeshUVUtility
    {
#if UNITY_EDITOR
        public static void GenerateLightmapUVs(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0 || mesh.triangles == null || mesh.triangles.Length < 3)
                return;

            try
            {
                Unwrapping.GenerateSecondaryUVSet(mesh);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"RealBlend could not generate lightmap UVs for '{mesh.name}': {ex.Message}");
            }
        }
#endif

    }
}
