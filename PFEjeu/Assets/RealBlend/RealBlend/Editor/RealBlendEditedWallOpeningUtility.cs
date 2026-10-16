using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    /// <summary>
    /// Adds an aligned opening to a RealBlend solid wall whose current mesh may
    /// already contain unrelated Boolean geometry.  The immutable base topology
    /// stored by VertexPaintStorage is used only as provenance: a base cell is
    /// editable when both of its original triangles still exist, unchanged, in
    /// the current mesh.  Everything else in the current mesh is copied verbatim.
    /// </summary>
    internal static class RealBlendEditedWallOpeningUtility
    {
        private const float NormalAlignment = 0.985f;
        private const float MatrixEpsilon = 0.00000001f;

        internal static bool TryCutOpening(
            Mesh source,
            VertexPaintStorage storage,
            Matrix4x4 localToWorld,
            RealBlendSculptRemoveSample rectangle,
            out Mesh result,
            out RealBlendSculptRemoveReport report,
            out string error)
        {
            return TryCutOpening(
                source,
                storage,
                localToWorld,
                rectangle,
                -1,
                out result,
                out report,
                out error);
        }

        internal static bool TryCutOpening(
            Mesh source,
            VertexPaintStorage storage,
            Matrix4x4 localToWorld,
            RealBlendSculptRemoveSample rectangle,
            int revealSubmesh,
            out Mesh result,
            out RealBlendSculptRemoveReport report,
            out string error)
        {
            result = null;
            report = default;
            error = string.Empty;

            if (!TryValidateInput(
                    source,
                    storage,
                    localToWorld,
                    rectangle,
                    out error))
            {
                return false;
            }
            if (!TryValidateRevealSubmesh(source, revealSubmesh, out error))
                return false;

            try
            {
                Context context = new Context(
                    source,
                    storage,
                    localToWorld,
                    rectangle);
                if (!context.TryBuild(out error) ||
                    !context.TryFindOpening(out CellRange opening, out error) ||
                    !context.TryCollectRemoval(
                        opening,
                        out HashSet<TriangleLocation> removed,
                        out Dictionary<int, int> frontVertices,
                        out Dictionary<int, int> backVertices,
                        out error))
                {
                    return false;
                }

                MeshStreams streams = new MeshStreams(source);
                List<int>[] outputIndices = BuildFilteredIndices(
                    source,
                    removed,
                    revealSubmesh,
                    out MeshTopology[] topologies,
                    out int inputTriangles,
                    out int retainedTriangles);

                int bridgeTriangles = 0;
                int rimVertices = 0;
                context.AppendRevealGeometry(
                    opening,
                    frontVertices,
                    backVertices,
                    streams,
                    outputIndices,
                    revealSubmesh,
                    ref bridgeTriangles,
                    ref rimVertices);

                Mesh built = new Mesh
                {
                    name = source.name,
                    hideFlags = source.hideFlags,
                    indexFormat = streams.VertexCount > 65535
                        ? IndexFormat.UInt32
                        : source.indexFormat
                };
                try
                {
                    streams.WriteTo(built);
                    built.subMeshCount = outputIndices.Length;
                    for (int submesh = 0; submesh < outputIndices.Length; submesh++)
                    {
                        built.SetIndices(
                            outputIndices[submesh],
                            topologies[submesh],
                            submesh,
                            false,
                            0);
                    }
                    built.RecalculateBounds();
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(built);
                    throw;
                }

                result = built;
                report = new RealBlendSculptRemoveReport
                {
                    inputTriangles = inputTriangles,
                    outputTriangles = retainedTriangles + bridgeTriangles,
                    insertedVertices = streams.VertexCount - source.vertexCount,
                    rimVertices = rimVertices,
                    bridgeTriangles = bridgeTriangles
                };
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "The edited RealBlend wall opening could not be built without changing the source mesh.\n" +
                    exception.Message;
                return false;
            }
        }

        private static bool TryValidateRevealSubmesh(
            Mesh source,
            int revealSubmesh,
            out string error)
        {
            error = string.Empty;
            if (revealSubmesh < 0)
                return true;
            if (revealSubmesh > 255)
            {
                error = "The requested reveal submesh exceeds RealBlend's supported material-slot range.";
                return false;
            }
            if (revealSubmesh < source.subMeshCount &&
                source.GetTopology(revealSubmesh) != MeshTopology.Triangles)
            {
                error = "The requested reveal submesh is not a triangle submesh.";
                return false;
            }
            return true;
        }

        private static bool TryValidateInput(
            Mesh source,
            VertexPaintStorage storage,
            Matrix4x4 localToWorld,
            RealBlendSculptRemoveSample rectangle,
            out string error)
        {
            error = string.Empty;
            if (source == null)
            {
                error = "The selected object has no mesh.";
                return false;
            }
            if (!source.isReadable)
            {
                error = "The selected mesh is not readable. Enable Read/Write on its importer first.";
                return false;
            }
            if (storage == null || !storage.hasBaseData ||
                storage.baseVertices == null || storage.baseVertices.Length < 8 ||
                storage.baseNormals == null ||
                storage.baseNormals.Length != storage.baseVertices.Length)
            {
                error =
                    "This edited wall has no complete original RealBlend wall topology in Vertex Paint Storage.";
                return false;
            }
            if (rectangle.shape != RealBlendSculptRemoveShape.Rectangle ||
                rectangle.width <= 0.000001f ||
                rectangle.height <= 0.000001f ||
                rectangle.worldNormal.sqrMagnitude <= MatrixEpsilon)
            {
                error = "Through-wall Remove requires a non-zero rectangular brush sample.";
                return false;
            }
            if (Mathf.Abs(localToWorld.determinant) <= MatrixEpsilon)
            {
                error = "The wall transform has a zero scale and cannot be cut safely.";
                return false;
            }
            if (source.blendShapeCount > 0 ||
                source.bindposes.Length > 0 ||
                source.HasVertexAttribute(VertexAttribute.BlendWeight) ||
                source.HasVertexAttribute(VertexAttribute.BlendIndices))
            {
                error = "Through-wall Remove is not supported on meshes with blend shapes or skinning data.";
                return false;
            }
            if (!source.HasVertexAttribute(VertexAttribute.Normal) ||
                source.normals.Length != source.vertexCount)
            {
                error = "The current wall mesh needs a complete normal stream.";
                return false;
            }
            if (source.GetVertexAttributeFormat(VertexAttribute.Position) !=
                    VertexAttributeFormat.Float32 ||
                source.GetVertexAttributeDimension(VertexAttribute.Position) != 3 ||
                source.GetVertexAttributeFormat(VertexAttribute.Normal) !=
                    VertexAttributeFormat.Float32 ||
                source.GetVertexAttributeDimension(VertexAttribute.Normal) != 3)
            {
                error = "The wall uses compressed position or normal streams that cannot be extended losslessly.";
                return false;
            }
            if (source.HasVertexAttribute(VertexAttribute.Tangent) &&
                (source.GetVertexAttributeFormat(VertexAttribute.Tangent) !=
                     VertexAttributeFormat.Float32 ||
                 source.GetVertexAttributeDimension(VertexAttribute.Tangent) != 4 ||
                 source.tangents.Length != source.vertexCount))
            {
                error = "The wall uses an unsupported tangent stream.";
                return false;
            }
            if (source.HasVertexAttribute(VertexAttribute.Color))
            {
                VertexAttributeFormat format =
                    source.GetVertexAttributeFormat(VertexAttribute.Color);
                if (format != VertexAttributeFormat.Float32 &&
                    format != VertexAttributeFormat.UNorm8 ||
                    source.GetVertexAttributeDimension(VertexAttribute.Color) != 4)
                {
                    error = "The wall uses an unsupported compressed vertex-color stream.";
                    return false;
                }
            }
            for (int channel = 0; channel < 8; channel++)
            {
                VertexAttribute attribute =
                    (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute))
                    continue;
                int dimension = source.GetVertexAttributeDimension(attribute);
                if (source.GetVertexAttributeFormat(attribute) !=
                        VertexAttributeFormat.Float32 ||
                    dimension < 2 || dimension > 4)
                {
                    error =
                        $"The wall uses an unsupported UV{channel} stream that cannot be extended losslessly.";
                    return false;
                }
            }
            return true;
        }

        private static List<int>[] BuildFilteredIndices(
            Mesh source,
            HashSet<TriangleLocation> removed,
            int revealSubmesh,
            out MeshTopology[] topologies,
            out int inputTriangles,
            out int retainedTriangles)
        {
            inputTriangles = 0;
            retainedTriangles = 0;
            int submeshCount = Mathf.Max(
                source.subMeshCount,
                revealSubmesh + 1);
            topologies = new MeshTopology[submeshCount];
            List<int>[] output = new List<int>[submeshCount];
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                MeshTopology topology = source.GetTopology(submesh);
                topologies[submesh] = topology;
                int[] indices = source.GetIndices(submesh, true);
                output[submesh] = new List<int>(indices.Length + 128);
                if (topology != MeshTopology.Triangles)
                {
                    output[submesh].AddRange(indices);
                    continue;
                }
                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    inputTriangles++;
                    if (removed.Contains(new TriangleLocation(submesh, offset)))
                        continue;
                    output[submesh].Add(indices[offset]);
                    output[submesh].Add(indices[offset + 1]);
                    output[submesh].Add(indices[offset + 2]);
                    retainedTriangles++;
                }
            }
            for (int submesh = source.subMeshCount;
                 submesh < submeshCount;
                 submesh++)
            {
                topologies[submesh] = MeshTopology.Triangles;
                output[submesh] = new List<int>(128);
            }
            return output;
        }

        private enum PatchKind
        {
            PositiveX,
            NegativeX,
            PositiveY,
            NegativeY,
            PositiveZ,
            NegativeZ
        }

        private readonly struct CellRange
        {
            public readonly int minX;
            public readonly int maxX;
            public readonly int minY;
            public readonly int maxY;

            public CellRange(int minX, int maxX, int minY, int maxY)
            {
                this.minX = minX;
                this.maxX = maxX;
                this.minY = minY;
                this.maxY = maxY;
            }
        }

        private readonly struct TriangleLocation : IEquatable<TriangleLocation>
        {
            public readonly int submesh;
            public readonly int offset;

            public TriangleLocation(int submesh, int offset)
            {
                this.submesh = submesh;
                this.offset = offset;
            }

            public bool Equals(TriangleLocation other)
            {
                return submesh == other.submesh && offset == other.offset;
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleLocation other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked { return submesh * 397 ^ offset; }
            }
        }

        private sealed class BaseTriangle
        {
            public int submesh;
            public int a;
            public int b;
            public int c;
        }

        private sealed class BaseCell
        {
            public readonly List<BaseTriangle> triangles = new List<BaseTriangle>(2);
            public int submesh = -1;
        }

        private sealed class PatchGrid
        {
            public readonly PatchKind kind;
            public readonly int[,] vertices;
            public readonly BaseCell[,] cells;

            public int UCount => vertices.GetLength(0);
            public int VCount => vertices.GetLength(1);

            public PatchGrid(PatchKind kind, int uCount, int vCount)
            {
                this.kind = kind;
                vertices = new int[uCount, vCount];
                cells = new BaseCell[uCount - 1, vCount - 1];
                for (int u = 0; u < uCount; u++)
                {
                    for (int v = 0; v < vCount; v++)
                        vertices[u, v] = -1;
                }
                for (int u = 0; u < uCount - 1; u++)
                {
                    for (int v = 0; v < vCount - 1; v++)
                        cells[u, v] = new BaseCell();
                }
            }
        }

        private sealed class MatchedCell
        {
            public readonly Dictionary<int, int> vertices =
                new Dictionary<int, int>(4);
            public readonly TriangleLocation[] triangles =
                new TriangleLocation[2];
            public int submesh;
        }

        private sealed class CurrentTriangle
        {
            public TriangleLocation location;
            public int submesh;
            public int a;
            public int b;
            public int c;
            public Vector3 normal;
        }

        private readonly struct PositionKey : IEquatable<PositionKey>, IComparable<PositionKey>
        {
            private readonly long x;
            private readonly long y;
            private readonly long z;

            public PositionKey(Vector3 position, float inverseTolerance)
            {
                x = (long)Math.Round(position.x * inverseTolerance);
                y = (long)Math.Round(position.y * inverseTolerance);
                z = (long)Math.Round(position.z * inverseTolerance);
            }

            public int CompareTo(PositionKey other)
            {
                int comparison = x.CompareTo(other.x);
                if (comparison != 0) return comparison;
                comparison = y.CompareTo(other.y);
                return comparison != 0 ? comparison : z.CompareTo(other.z);
            }

            public bool Equals(PositionKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is PositionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x.GetHashCode();
                    hash = hash * 397 ^ y.GetHashCode();
                    hash = hash * 397 ^ z.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct TriangleGeometryKey : IEquatable<TriangleGeometryKey>
        {
            private readonly int submesh;
            private readonly PositionKey first;
            private readonly PositionKey second;
            private readonly PositionKey third;

            public TriangleGeometryKey(
                int submesh,
                Vector3 a,
                Vector3 b,
                Vector3 c,
                float inverseTolerance)
            {
                this.submesh = submesh;
                PositionKey ka = new PositionKey(a, inverseTolerance);
                PositionKey kb = new PositionKey(b, inverseTolerance);
                PositionKey kc = new PositionKey(c, inverseTolerance);
                if (ka.CompareTo(kb) > 0) Swap(ref ka, ref kb);
                if (kb.CompareTo(kc) > 0) Swap(ref kb, ref kc);
                if (ka.CompareTo(kb) > 0) Swap(ref ka, ref kb);
                first = ka;
                second = kb;
                third = kc;
            }

            public bool Equals(TriangleGeometryKey other)
            {
                return submesh == other.submesh &&
                       first.Equals(other.first) &&
                       second.Equals(other.second) &&
                       third.Equals(other.third);
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleGeometryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = submesh;
                    hash = hash * 397 ^ first.GetHashCode();
                    hash = hash * 397 ^ second.GetHashCode();
                    hash = hash * 397 ^ third.GetHashCode();
                    return hash;
                }
            }

            private static void Swap(ref PositionKey left, ref PositionKey right)
            {
                PositionKey temporary = left;
                left = right;
                right = temporary;
            }
        }

        private sealed class CurrentTriangleIndex
        {
            private readonly Vector3[] positions;
            private readonly float tolerance;
            private readonly float inverseTolerance;
            private readonly Dictionary<TriangleGeometryKey, List<CurrentTriangle>> triangles =
                new Dictionary<TriangleGeometryKey, List<CurrentTriangle>>();

            public CurrentTriangleIndex(Mesh mesh, float tolerance)
            {
                positions = mesh.vertices;
                this.tolerance = tolerance;
                inverseTolerance = 1f / tolerance;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                        continue;
                    int[] indices = mesh.GetIndices(submesh, true);
                    for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                    {
                        int a = indices[offset];
                        int b = indices[offset + 1];
                        int c = indices[offset + 2];
                        if (!IsValid(a) || !IsValid(b) || !IsValid(c))
                            continue;
                        Vector3 geometric = Vector3.Cross(
                            positions[b] - positions[a],
                            positions[c] - positions[a]);
                        if (geometric.sqrMagnitude <= MatrixEpsilon)
                            continue;
                        CurrentTriangle triangle = new CurrentTriangle
                        {
                            location = new TriangleLocation(submesh, offset),
                            submesh = submesh,
                            a = a,
                            b = b,
                            c = c,
                            normal = geometric.normalized
                        };
                        TriangleGeometryKey key = new TriangleGeometryKey(
                            submesh,
                            positions[a],
                            positions[b],
                            positions[c],
                            inverseTolerance);
                        if (!triangles.TryGetValue(key, out List<CurrentTriangle> matches))
                        {
                            matches = new List<CurrentTriangle>(1);
                            triangles.Add(key, matches);
                        }
                        matches.Add(triangle);
                    }
                }
            }

            public bool TryFind(
                BaseTriangle source,
                Vector3 expectedNormal,
                Vector3[] basePositions,
                out CurrentTriangle match,
                out Dictionary<int, int> mappedVertices)
            {
                match = null;
                mappedVertices = null;
                TriangleGeometryKey key = new TriangleGeometryKey(
                    source.submesh,
                    basePositions[source.a],
                    basePositions[source.b],
                    basePositions[source.c],
                    inverseTolerance);
                if (!triangles.TryGetValue(key, out List<CurrentTriangle> candidates))
                    return false;

                int matchCount = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    CurrentTriangle candidate = candidates[i];
                    if (Vector3.Dot(candidate.normal, expectedNormal) < NormalAlignment ||
                        !TryMapTriangleVertices(
                            source,
                            candidate,
                            basePositions,
                            out Dictionary<int, int> candidateMap))
                    {
                        continue;
                    }
                    match = candidate;
                    mappedVertices = candidateMap;
                    matchCount++;
                }
                return matchCount == 1;
            }

            private bool TryMapTriangleVertices(
                BaseTriangle source,
                CurrentTriangle candidate,
                Vector3[] basePositions,
                out Dictionary<int, int> mapping)
            {
                mapping = new Dictionary<int, int>(3);
                int[] baseIndices = { source.a, source.b, source.c };
                int[] currentIndices = { candidate.a, candidate.b, candidate.c };
                bool[] used = new bool[3];
                float toleranceSqr = tolerance * tolerance;
                for (int baseOffset = 0; baseOffset < 3; baseOffset++)
                {
                    int best = -1;
                    float bestDistance = float.MaxValue;
                    Vector3 expected = basePositions[baseIndices[baseOffset]];
                    for (int currentOffset = 0; currentOffset < 3; currentOffset++)
                    {
                        if (used[currentOffset])
                            continue;
                        float distance =
                            (positions[currentIndices[currentOffset]] - expected).sqrMagnitude;
                        if (distance <= toleranceSqr && distance < bestDistance)
                        {
                            best = currentOffset;
                            bestDistance = distance;
                        }
                    }
                    if (best < 0)
                        return false;
                    used[best] = true;
                    mapping.Add(baseIndices[baseOffset], currentIndices[best]);
                }
                return true;
            }

            private bool IsValid(int vertex)
            {
                return vertex >= 0 && vertex < positions.Length;
            }
        }

        private sealed class Context
        {
            private readonly Mesh source;
            private readonly VertexPaintStorage storage;
            private readonly Matrix4x4 localToWorld;
            private readonly RealBlendSculptRemoveSample sample;
            private readonly Vector3[] basePositions;
            private readonly Vector3[] baseNormals;
            private readonly PatchKind[] basePatches;
            private readonly int[] vertexU;
            private readonly int[] vertexV;
            private readonly Dictionary<PatchKind, List<int>> patchVertices =
                new Dictionary<PatchKind, List<int>>();
            private readonly Dictionary<PatchKind, PatchGrid> grids =
                new Dictionary<PatchKind, PatchGrid>();

            private float[] xLevels;
            private float[] yLevels;
            private float[] zLevels;
            private float tolerance;
            private PatchKind frontKind;
            private PatchKind backKind;
            private Vector3 brushNormal;
            private Vector3 brushRight;
            private Vector3 brushUp;
            private CurrentTriangleIndex currentTriangles;

            public Context(
                Mesh source,
                VertexPaintStorage storage,
                Matrix4x4 localToWorld,
                RealBlendSculptRemoveSample sample)
            {
                this.source = source;
                this.storage = storage;
                this.localToWorld = localToWorld;
                this.sample = sample;
                basePositions = storage.baseVertices;
                baseNormals = storage.baseNormals;
                basePatches = new PatchKind[basePositions.Length];
                vertexU = new int[basePositions.Length];
                vertexV = new int[basePositions.Length];
                for (int i = 0; i < basePositions.Length; i++)
                {
                    vertexU[i] = -1;
                    vertexV[i] = -1;
                }
                foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
                    patchVertices[kind] = new List<int>();
            }

            public bool TryBuild(out string error)
            {
                error = string.Empty;
                brushNormal = sample.worldNormal.normalized;
                brushRight = Vector3.Cross(brushNormal, Vector3.up);
                if (brushRight.sqrMagnitude < 0.001f)
                    brushRight = Vector3.Cross(brushNormal, Vector3.right);
                if (brushRight.sqrMagnitude <= MatrixEpsilon)
                {
                    error = "The wall surface did not provide a stable rectangular brush frame.";
                    return false;
                }
                brushRight.Normalize();
                brushUp = Vector3.Cross(brushRight, brushNormal).normalized;

                Bounds bounds = BoundsOf(basePositions);
                float extent = Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z));
                tolerance = Mathf.Max(0.000005f, extent * 0.00001f);

                Vector3 localBrushNormal =
                    localToWorld.transpose.MultiplyVector(brushNormal);
                if (localBrushNormal.sqrMagnitude <= MatrixEpsilon)
                {
                    error = "The wall transform could not convert the brush normal safely.";
                    return false;
                }
                localBrushNormal.Normalize();
                if (Mathf.Abs(localBrushNormal.z) < NormalAlignment)
                {
                    error = "The selected surface is not the original front or back grid of this RealBlend wall.";
                    return false;
                }
                frontKind = localBrushNormal.z >= 0f
                    ? PatchKind.PositiveZ
                    : PatchKind.NegativeZ;
                backKind = localBrushNormal.z >= 0f
                    ? PatchKind.NegativeZ
                    : PatchKind.PositiveZ;

                if (!ClassifyBaseVertices(bounds, out error) ||
                    !BuildCoordinateLevels(out error) ||
                    !BuildPatchGrids(out error) ||
                    !AssignBaseTriangles(out error))
                {
                    return false;
                }

                float currentExtent = Mathf.Max(
                    source.bounds.size.x,
                    Mathf.Max(source.bounds.size.y, source.bounds.size.z));
                float matchTolerance = Mathf.Max(
                    tolerance,
                    currentExtent * 0.00001f);
                currentTriangles = new CurrentTriangleIndex(source, matchTolerance);

                float frontDepth = AverageWorldDepth(grids[frontKind]);
                float backDepth = AverageWorldDepth(grids[backKind]);
                if (frontDepth <= backDepth + matchTolerance)
                {
                    error = "The original front and back wall grids are not separated along the selected normal.";
                    return false;
                }
                float sampleDepth = Vector3.Dot(sample.worldCenter, brushNormal);
                float hitTolerance = Mathf.Max(0.0025f, GetMaximumWorldExtent() * 0.001f);
                if (Mathf.Abs(sampleDepth - frontDepth) > hitTolerance)
                {
                    error = "The rectangular sample is not on the selected original outer wall surface.";
                    return false;
                }
                return true;
            }

            public bool TryFindOpening(out CellRange opening, out string error)
            {
                opening = default;
                error = string.Empty;
                PatchGrid front = grids[frontKind];
                int minX = int.MaxValue;
                int maxX = int.MinValue;
                int minY = int.MaxValue;
                int maxY = int.MinValue;
                float contactTolerance = Mathf.Max(
                    0.000001f,
                    Mathf.Max(sample.width, sample.height) * 0.00001f);

                for (int x = 0; x < front.UCount - 1; x++)
                {
                    for (int y = 0; y < front.VCount - 1; y++)
                    {
                        Vector2[] projected =
                        {
                            ProjectBase(front.vertices[x, y]),
                            ProjectBase(front.vertices[x + 1, y]),
                            ProjectBase(front.vertices[x + 1, y + 1]),
                            ProjectBase(front.vertices[x, y + 1])
                        };
                        if (!ConvexCellTouchesRectangle(
                                projected,
                                sample.width * 0.5f,
                                sample.height * 0.5f,
                                contactTolerance))
                        {
                            continue;
                        }
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
                if (minX == int.MaxValue)
                {
                    error = "The rectangular brush does not touch an original wall grid cell.";
                    return false;
                }
                opening = new CellRange(minX, maxX, minY, maxY);
                return true;
            }

            public bool TryCollectRemoval(
                CellRange opening,
                out HashSet<TriangleLocation> removed,
                out Dictionary<int, int> frontVertices,
                out Dictionary<int, int> backVertices,
                out string error)
            {
                removed = new HashSet<TriangleLocation>();
                frontVertices = new Dictionary<int, int>();
                backVertices = new Dictionary<int, int>();
                error = string.Empty;
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];

                for (int x = opening.minX; x <= opening.maxX; x++)
                {
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        if (!TryCollectCell(
                                front,
                                x,
                                y,
                                removed,
                                frontVertices,
                                out error) ||
                            !TryCollectCell(
                                back,
                                x,
                                y,
                                removed,
                                backVertices,
                                out error))
                        {
                            error =
                                "The square overlaps a Boolean-modified, sculpted, or already removed wall cell. " +
                                error;
                            return false;
                        }
                    }
                }

                if (!ValidateBoundaryPairs(
                        opening,
                        frontVertices,
                        backVertices,
                        out error))
                {
                    return false;
                }

                if (opening.minX == 0 &&
                    !TryCollectClosureStrip(
                        grids[PatchKind.NegativeX],
                        opening.minY,
                        opening.maxY,
                        false,
                        removed,
                        out error) ||
                    opening.maxX == front.UCount - 2 &&
                    !TryCollectClosureStrip(
                        grids[PatchKind.PositiveX],
                        opening.minY,
                        opening.maxY,
                        false,
                        removed,
                        out error) ||
                    opening.minY == 0 &&
                    !TryCollectClosureStrip(
                        grids[PatchKind.NegativeY],
                        opening.minX,
                        opening.maxX,
                        true,
                        removed,
                        out error) ||
                    opening.maxY == front.VCount - 2 &&
                    !TryCollectClosureStrip(
                        grids[PatchKind.PositiveY],
                        opening.minX,
                        opening.maxX,
                        true,
                        removed,
                        out error))
                {
                    error =
                        "The opening reaches an outer wall edge whose original closure cells were modified. " +
                        error;
                    return false;
                }
                return true;
            }

            public void AppendRevealGeometry(
                CellRange opening,
                Dictionary<int, int> frontVertices,
                Dictionary<int, int> backVertices,
                MeshStreams streams,
                List<int>[] outputIndices,
                int revealSubmesh,
                ref int bridgeTriangles,
                ref int rimVertices)
            {
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];
                if (opening.minX > 0)
                {
                    Dictionary<int, int> copies = new Dictionary<int, int>();
                    int boundary = opening.minX;
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        AppendBridgeSegment(
                            frontVertices[front.vertices[boundary, y]],
                            frontVertices[front.vertices[boundary, y + 1]],
                            backVertices[back.vertices[boundary, y]],
                            backVertices[back.vertices[boundary, y + 1]],
                            Vector3.right,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[opening.minX, y].submesh,
                            copies,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.maxX < front.UCount - 2)
                {
                    Dictionary<int, int> copies = new Dictionary<int, int>();
                    int boundary = opening.maxX + 1;
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        AppendBridgeSegment(
                            frontVertices[front.vertices[boundary, y]],
                            frontVertices[front.vertices[boundary, y + 1]],
                            backVertices[back.vertices[boundary, y]],
                            backVertices[back.vertices[boundary, y + 1]],
                            Vector3.left,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[opening.maxX, y].submesh,
                            copies,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.minY > 0)
                {
                    Dictionary<int, int> copies = new Dictionary<int, int>();
                    int boundary = opening.minY;
                    for (int x = opening.minX; x <= opening.maxX; x++)
                    {
                        AppendBridgeSegment(
                            frontVertices[front.vertices[x, boundary]],
                            frontVertices[front.vertices[x + 1, boundary]],
                            backVertices[back.vertices[x, boundary]],
                            backVertices[back.vertices[x + 1, boundary]],
                            Vector3.up,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[x, opening.minY].submesh,
                            copies,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.maxY < front.VCount - 2)
                {
                    Dictionary<int, int> copies = new Dictionary<int, int>();
                    int boundary = opening.maxY + 1;
                    for (int x = opening.minX; x <= opening.maxX; x++)
                    {
                        AppendBridgeSegment(
                            frontVertices[front.vertices[x, boundary]],
                            frontVertices[front.vertices[x + 1, boundary]],
                            backVertices[back.vertices[x, boundary]],
                            backVertices[back.vertices[x + 1, boundary]],
                            Vector3.down,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[x, opening.maxY].submesh,
                            copies,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
            }

            private bool TryCollectCell(
                PatchGrid grid,
                int u,
                int v,
                HashSet<TriangleLocation> removed,
                Dictionary<int, int> mappedVertices,
                out string error)
            {
                error = string.Empty;
                BaseCell cell = grid.cells[u, v];
                if (cell.triangles.Count != 2)
                {
                    error = "The stored base cell is incomplete.";
                    return false;
                }
                MatchedCell match = new MatchedCell { submesh = cell.submesh };
                Vector3 expectedNormal = GetPatchNormal(grid.kind);
                for (int triangleIndex = 0; triangleIndex < 2; triangleIndex++)
                {
                    BaseTriangle triangle = cell.triangles[triangleIndex];
                    if (!currentTriangles.TryFind(
                            triangle,
                            expectedNormal,
                            basePositions,
                            out CurrentTriangle current,
                            out Dictionary<int, int> vertexMap))
                    {
                        error = "One of the exact original cell triangles is no longer present.";
                        return false;
                    }
                    if (!removed.Add(current.location))
                    {
                        error = "An original current triangle was matched by more than one cell.";
                        return false;
                    }
                    match.triangles[triangleIndex] = current.location;
                    foreach (KeyValuePair<int, int> pair in vertexMap)
                    {
                        if (match.vertices.TryGetValue(pair.Key, out int existing) &&
                            existing != pair.Value)
                        {
                            error = "The current cell uses split duplicates instead of one intact original face.";
                            return false;
                        }
                        match.vertices[pair.Key] = pair.Value;
                    }
                }
                int[] corners =
                {
                    grid.vertices[u, v],
                    grid.vertices[u + 1, v],
                    grid.vertices[u + 1, v + 1],
                    grid.vertices[u, v + 1]
                };
                for (int i = 0; i < corners.Length; i++)
                {
                    if (!match.vertices.TryGetValue(corners[i], out int currentVertex))
                    {
                        error = "The exact current cell did not contain all four original corners.";
                        return false;
                    }
                    if (mappedVertices.TryGetValue(corners[i], out int existing) &&
                        existing != currentVertex)
                    {
                        error = "Adjacent current cells do not share their original boundary vertex.";
                        return false;
                    }
                    mappedVertices[corners[i]] = currentVertex;
                }
                return true;
            }

            private bool TryCollectClosureStrip(
                PatchGrid closure,
                int firstAlongWall,
                int lastAlongWall,
                bool alongU,
                HashSet<TriangleLocation> removed,
                out string error)
            {
                error = string.Empty;
                Dictionary<int, int> ignored = new Dictionary<int, int>();
                if (alongU)
                {
                    for (int u = firstAlongWall; u <= lastAlongWall; u++)
                    {
                        for (int v = 0; v < closure.VCount - 1; v++)
                        {
                            if (!TryCollectCell(
                                    closure,
                                    u,
                                    v,
                                    removed,
                                    ignored,
                                    out error))
                                return false;
                        }
                    }
                }
                else
                {
                    for (int u = 0; u < closure.UCount - 1; u++)
                    {
                        for (int v = firstAlongWall; v <= lastAlongWall; v++)
                        {
                            if (!TryCollectCell(
                                    closure,
                                    u,
                                    v,
                                    removed,
                                    ignored,
                                    out error))
                                return false;
                        }
                    }
                }
                return true;
            }

            private bool ValidateBoundaryPairs(
                CellRange opening,
                Dictionary<int, int> frontVertices,
                Dictionary<int, int> backVertices,
                out string error)
            {
                error = string.Empty;
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];
                Vector3[] currentPositions = source.vertices;
                for (int x = opening.minX; x <= opening.maxX + 1; x++)
                {
                    if (!ValidatePair(x, opening.minY) ||
                        !ValidatePair(x, opening.maxY + 1))
                    {
                        error = "The front/back opening boundary vertices are not congruent.";
                        return false;
                    }
                }
                for (int y = opening.minY; y <= opening.maxY + 1; y++)
                {
                    if (!ValidatePair(opening.minX, y) ||
                        !ValidatePair(opening.maxX + 1, y))
                    {
                        error = "The front/back opening boundary vertices are not congruent.";
                        return false;
                    }
                }
                return true;

                bool ValidatePair(int x, int y)
                {
                    int frontBase = front.vertices[x, y];
                    int backBase = back.vertices[x, y];
                    if (!frontVertices.TryGetValue(frontBase, out int frontCurrent) ||
                        !backVertices.TryGetValue(backBase, out int backCurrent))
                    {
                        return false;
                    }
                    Vector3 first = currentPositions[frontCurrent];
                    Vector3 second = currentPositions[backCurrent];
                    return Mathf.Abs(first.x - second.x) <= tolerance &&
                           Mathf.Abs(first.y - second.y) <= tolerance &&
                           Mathf.Abs(first.z - second.z) > tolerance;
                }
            }

            private bool ClassifyBaseVertices(Bounds bounds, out string error)
            {
                error = string.Empty;
                for (int vertex = 0; vertex < basePositions.Length; vertex++)
                {
                    Vector3 normal = baseNormals[vertex];
                    if (normal.sqrMagnitude <= MatrixEpsilon)
                    {
                        error = "The stored base wall contains a zero-length normal.";
                        return false;
                    }
                    normal.Normalize();
                    ClassifyAxisNormal(normal, out PatchKind kind, out float alignment);
                    if (alignment < NormalAlignment)
                    {
                        error = "The stored base mesh is not an untouched rectangular RealBlend Solid Wall.";
                        return false;
                    }
                    int axis = GetPatchAxis(kind);
                    float plane = GetExpectedPatchPlane(kind, bounds);
                    if (Mathf.Abs(basePositions[vertex][axis] - plane) > tolerance)
                    {
                        error = "The stored base mesh has vertices away from its six generated wall planes.";
                        return false;
                    }
                    basePatches[vertex] = kind;
                    patchVertices[kind].Add(vertex);
                }
                foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
                {
                    if (patchVertices[kind].Count < 4)
                    {
                        error = "The stored base mesh is missing a generated wall patch.";
                        return false;
                    }
                }
                return true;
            }

            private bool BuildCoordinateLevels(out string error)
            {
                error = string.Empty;
                xLevels = ClusterValues(
                    patchVertices[PatchKind.PositiveZ],
                    vertex => basePositions[vertex].x);
                yLevels = ClusterValues(
                    patchVertices[PatchKind.PositiveZ],
                    vertex => basePositions[vertex].y);
                zLevels = ClusterValues(
                    patchVertices[PatchKind.PositiveX],
                    vertex => basePositions[vertex].z);
                if (xLevels.Length < 2 || yLevels.Length < 2 || zLevels.Length < 2)
                {
                    error = "The stored base wall grids do not contain enough rows.";
                    return false;
                }
                if (!LevelsMatch(
                        xLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeZ],
                            vertex => basePositions[vertex].x)) ||
                    !LevelsMatch(
                        yLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeZ],
                            vertex => basePositions[vertex].y)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeX],
                            vertex => basePositions[vertex].z)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.PositiveY],
                            vertex => basePositions[vertex].z)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeY],
                            vertex => basePositions[vertex].z)))
                {
                    error = "The stored base wall patches do not share the same generated grid.";
                    return false;
                }
                return true;
            }

            private bool BuildPatchGrids(out string error)
            {
                return TryBuildPatchGrid(
                           PatchKind.PositiveZ,
                           xLevels,
                           yLevels,
                           vertex => basePositions[vertex].x,
                           vertex => basePositions[vertex].y,
                           out error) &&
                       TryBuildPatchGrid(
                           PatchKind.NegativeZ,
                           xLevels,
                           yLevels,
                           vertex => basePositions[vertex].x,
                           vertex => basePositions[vertex].y,
                           out error) &&
                       TryBuildPatchGrid(
                           PatchKind.PositiveX,
                           zLevels,
                           yLevels,
                           vertex => basePositions[vertex].z,
                           vertex => basePositions[vertex].y,
                           out error) &&
                       TryBuildPatchGrid(
                           PatchKind.NegativeX,
                           zLevels,
                           yLevels,
                           vertex => basePositions[vertex].z,
                           vertex => basePositions[vertex].y,
                           out error) &&
                       TryBuildPatchGrid(
                           PatchKind.PositiveY,
                           xLevels,
                           zLevels,
                           vertex => basePositions[vertex].x,
                           vertex => basePositions[vertex].z,
                           out error) &&
                       TryBuildPatchGrid(
                           PatchKind.NegativeY,
                           xLevels,
                           zLevels,
                           vertex => basePositions[vertex].x,
                           vertex => basePositions[vertex].z,
                           out error);
            }

            private bool TryBuildPatchGrid(
                PatchKind kind,
                float[] uLevels,
                float[] vLevels,
                Func<int, float> getU,
                Func<int, float> getV,
                out string error)
            {
                error = string.Empty;
                List<int> vertices = patchVertices[kind];
                if (vertices.Count != uLevels.Length * vLevels.Length)
                {
                    error = $"The stored {kind} base patch is not a complete rectangular grid.";
                    return false;
                }
                PatchGrid grid = new PatchGrid(kind, uLevels.Length, vLevels.Length);
                for (int i = 0; i < vertices.Count; i++)
                {
                    int vertex = vertices[i];
                    int u = FindLevel(uLevels, getU(vertex));
                    int v = FindLevel(vLevels, getV(vertex));
                    if (u < 0 || v < 0 || grid.vertices[u, v] >= 0)
                    {
                        error = $"The stored {kind} base patch has a duplicate or off-grid vertex.";
                        return false;
                    }
                    grid.vertices[u, v] = vertex;
                    vertexU[vertex] = u;
                    vertexV[vertex] = v;
                }
                grids[kind] = grid;
                return true;
            }

            private bool AssignBaseTriangles(out string error)
            {
                error = string.Empty;
                if (!TryGetBaseSubmeshes(storage, out BaseSubmesh[] submeshes, out error))
                    return false;
                for (int submesh = 0; submesh < submeshes.Length; submesh++)
                {
                    BaseSubmesh stored = submeshes[submesh];
                    if (stored.topology != MeshTopology.Triangles)
                    {
                        error = "The stored base wall contains a non-triangle submesh.";
                        return false;
                    }
                    int[] indices = stored.indices;
                    for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                    {
                        int a = indices[offset];
                        int b = indices[offset + 1];
                        int c = indices[offset + 2];
                        if (!IsValidBaseVertex(a) || !IsValidBaseVertex(b) ||
                            !IsValidBaseVertex(c) ||
                            basePatches[a] != basePatches[b] ||
                            basePatches[a] != basePatches[c])
                        {
                            error = "The stored base topology is not the six duplicated generated wall patches.";
                            return false;
                        }
                        PatchKind kind = basePatches[a];
                        PatchGrid grid = grids[kind];
                        int minU = Mathf.Min(vertexU[a], Mathf.Min(vertexU[b], vertexU[c]));
                        int maxU = Mathf.Max(vertexU[a], Mathf.Max(vertexU[b], vertexU[c]));
                        int minV = Mathf.Min(vertexV[a], Mathf.Min(vertexV[b], vertexV[c]));
                        int maxV = Mathf.Max(vertexV[a], Mathf.Max(vertexV[b], vertexV[c]));
                        if (minU < 0 || minV < 0 ||
                            maxU != minU + 1 || maxV != minV + 1)
                        {
                            error = "A stored base wall triangle does not span exactly one grid cell.";
                            return false;
                        }
                        Vector3 geometric = Vector3.Cross(
                            basePositions[b] - basePositions[a],
                            basePositions[c] - basePositions[a]);
                        if (geometric.sqrMagnitude <= MatrixEpsilon ||
                            Vector3.Dot(geometric.normalized, GetPatchNormal(kind)) <
                            NormalAlignment)
                        {
                            error = "A stored base wall triangle has invalid winding.";
                            return false;
                        }
                        BaseCell cell = grid.cells[minU, minV];
                        cell.triangles.Add(new BaseTriangle
                        {
                            submesh = submesh,
                            a = a,
                            b = b,
                            c = c
                        });
                        if (cell.submesh < 0)
                            cell.submesh = submesh;
                        else if (cell.submesh != submesh)
                        {
                            error = "A stored base cell spans more than one material submesh.";
                            return false;
                        }
                    }
                }
                foreach (PatchGrid grid in grids.Values)
                {
                    for (int u = 0; u < grid.UCount - 1; u++)
                    {
                        for (int v = 0; v < grid.VCount - 1; v++)
                        {
                            if (grid.cells[u, v].triangles.Count != 2)
                            {
                                error = "The stored base wall contains an incomplete generated cell.";
                                return false;
                            }
                        }
                    }
                }
                return true;
            }

            private Vector2 ProjectBase(int vertex)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(basePositions[vertex]);
                Vector3 delta = world - sample.worldCenter;
                return new Vector2(
                    Vector3.Dot(delta, brushRight),
                    Vector3.Dot(delta, brushUp));
            }

            private float AverageWorldDepth(PatchGrid grid)
            {
                float sum = 0f;
                int count = 0;
                for (int u = 0; u < grid.UCount; u++)
                {
                    for (int v = 0; v < grid.VCount; v++)
                    {
                        Vector3 world = localToWorld.MultiplyPoint3x4(
                            basePositions[grid.vertices[u, v]]);
                        sum += Vector3.Dot(world, brushNormal);
                        count++;
                    }
                }
                return count > 0 ? sum / count : 0f;
            }

            private float GetMaximumWorldExtent()
            {
                Vector3 first = localToWorld.MultiplyPoint3x4(basePositions[0]);
                Bounds bounds = new Bounds(first, Vector3.zero);
                for (int i = 1; i < basePositions.Length; i++)
                    bounds.Encapsulate(localToWorld.MultiplyPoint3x4(basePositions[i]));
                return Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            }

            private float[] ClusterValues(List<int> vertices, Func<int, float> getValue)
            {
                List<float> values = new List<float>(vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                    values.Add(getValue(vertices[i]));
                values.Sort();
                List<float> levels = new List<float>();
                int start = 0;
                while (start < values.Count)
                {
                    float sum = values[start];
                    int count = 1;
                    int end = start + 1;
                    while (end < values.Count &&
                           Mathf.Abs(values[end] - values[start]) <= tolerance)
                    {
                        sum += values[end];
                        count++;
                        end++;
                    }
                    levels.Add(sum / count);
                    start = end;
                }
                return levels.ToArray();
            }

            private bool LevelsMatch(float[] first, float[] second)
            {
                if (first.Length != second.Length)
                    return false;
                for (int i = 0; i < first.Length; i++)
                {
                    if (Mathf.Abs(first[i] - second[i]) > tolerance)
                        return false;
                }
                return true;
            }

            private int FindLevel(float[] levels, float value)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < levels.Length; i++)
                {
                    float distance = Mathf.Abs(levels[i] - value);
                    if (distance < bestDistance)
                    {
                        best = i;
                        bestDistance = distance;
                    }
                }
                return bestDistance <= tolerance ? best : -1;
            }

            private bool IsValidBaseVertex(int vertex)
            {
                return vertex >= 0 && vertex < basePositions.Length;
            }
        }

        private sealed class BaseSubmesh
        {
            public MeshTopology topology;
            public int[] indices;
        }

        private static bool TryGetBaseSubmeshes(
            VertexPaintStorage storage,
            out BaseSubmesh[] result,
            out string error)
        {
            result = null;
            error = string.Empty;
            RealBlendStoredIndexBuffer[] stored = storage.baseSubmeshesV2;
            if (stored != null && stored.Length > 0)
            {
                result = new BaseSubmesh[stored.Length];
                for (int submesh = 0; submesh < stored.Length; submesh++)
                {
                    RealBlendStoredIndexBuffer item = stored[submesh];
                    if (item == null || item.indices == null)
                    {
                        error = "The stored base wall has incomplete submesh data.";
                        return false;
                    }
                    int[] indices = new int[item.indices.Length];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        long applied = (long)item.indices[i] + item.baseVertex;
                        if (applied < int.MinValue || applied > int.MaxValue)
                        {
                            error = "A stored base wall index is outside the supported range.";
                            return false;
                        }
                        indices[i] = (int)applied;
                    }
                    result[submesh] = new BaseSubmesh
                    {
                        topology = item.topology,
                        indices = indices
                    };
                }
                return true;
            }

            if (storage.baseTriangles == null ||
                storage.baseSubmeshIndexCounts == null ||
                storage.baseSubmeshIndexCounts.Length == 0)
            {
                error = "The stored base wall has no recoverable submesh topology.";
                return false;
            }
            int expected = 0;
            for (int i = 0; i < storage.baseSubmeshIndexCounts.Length; i++)
                expected += storage.baseSubmeshIndexCounts[i];
            if (expected != storage.baseTriangles.Length)
            {
                error = "The legacy stored base submesh counts are inconsistent.";
                return false;
            }
            result = new BaseSubmesh[storage.baseSubmeshIndexCounts.Length];
            int offset = 0;
            for (int submesh = 0; submesh < result.Length; submesh++)
            {
                int count = storage.baseSubmeshIndexCounts[submesh];
                int[] indices = new int[count];
                Array.Copy(storage.baseTriangles, offset, indices, 0, count);
                offset += count;
                result[submesh] = new BaseSubmesh
                {
                    topology = MeshTopology.Triangles,
                    indices = indices
                };
            }
            return true;
        }

        private static void AppendBridgeSegment(
            int frontA,
            int frontB,
            int backA,
            int backB,
            Vector3 desiredNormal,
            int submesh,
            Dictionary<int, int> revealVertices,
            MeshStreams streams,
            List<int>[] outputIndices,
            ref int bridgeTriangles,
            ref int rimVertices)
        {
            Vector3 segment = streams.GetPosition(frontB) - streams.GetPosition(frontA);
            Vector3 tangent = segment -
                              desiredNormal * Vector3.Dot(segment, desiredNormal);
            if (tangent.sqrMagnitude <= MatrixEpsilon)
                tangent = Vector3.Cross(desiredNormal, Vector3.forward);
            if (tangent.sqrMagnitude <= MatrixEpsilon)
                tangent = Vector3.Cross(desiredNormal, Vector3.up);
            tangent.Normalize();

            int fa = GetOrAppendRevealVertex(
                frontA, desiredNormal, tangent, revealVertices, streams, ref rimVertices);
            int fb = GetOrAppendRevealVertex(
                frontB, desiredNormal, tangent, revealVertices, streams, ref rimVertices);
            int ba = GetOrAppendRevealVertex(
                backA, desiredNormal, tangent, revealVertices, streams, ref rimVertices);
            int bb = GetOrAppendRevealVertex(
                backB, desiredNormal, tangent, revealVertices, streams, ref rimVertices);

            bool forward = Vector3.Dot(
                Vector3.Cross(
                    streams.GetPosition(fb) - streams.GetPosition(fa),
                    streams.GetPosition(bb) - streams.GetPosition(fa)),
                desiredNormal) >= 0f;
            List<int> indices = outputIndices[Mathf.Clamp(submesh, 0, outputIndices.Length - 1)];
            if (forward)
            {
                indices.Add(fa); indices.Add(fb); indices.Add(bb);
                indices.Add(fa); indices.Add(bb); indices.Add(ba);
            }
            else
            {
                indices.Add(fa); indices.Add(bb); indices.Add(fb);
                indices.Add(fa); indices.Add(ba); indices.Add(bb);
            }
            bridgeTriangles += 2;
        }

        private static int GetOrAppendRevealVertex(
            int sourceVertex,
            Vector3 normal,
            Vector3 tangent,
            Dictionary<int, int> revealVertices,
            MeshStreams streams,
            ref int rimVertices)
        {
            if (revealVertices.TryGetValue(sourceVertex, out int existing))
                return existing;
            int appended = streams.AppendCopy(sourceVertex, normal, tangent);
            revealVertices.Add(sourceVertex, appended);
            rimVertices++;
            return appended;
        }

        private sealed class MeshStreams
        {
            private readonly List<Vector3> positions;
            private readonly List<Vector3> normals;
            private readonly List<Vector4> tangents;
            private readonly List<Color> colors;
            private readonly bool colorsWereUNorm8;
            private readonly UVStream[] uvs = new UVStream[8];
            private readonly RealBlendRevealUVProjector revealUVs;

            public int VertexCount => positions.Count;

            public MeshStreams(Mesh source)
            {
                positions = new List<Vector3>(source.vertices);
                normals = new List<Vector3>(source.normals);
                revealUVs = new RealBlendRevealUVProjector(source);
                Vector4[] sourceTangents = source.tangents;
                if (sourceTangents != null && sourceTangents.Length == source.vertexCount)
                    tangents = new List<Vector4>(sourceTangents);
                if (source.HasVertexAttribute(VertexAttribute.Color))
                {
                    colors = new List<Color>();
                    source.GetColors(colors);
                    if (colors.Count != source.vertexCount)
                        throw new InvalidOperationException("The vertex-color stream is incomplete.");
                    colorsWereUNorm8 =
                        source.GetVertexAttributeFormat(VertexAttribute.Color) ==
                        VertexAttributeFormat.UNorm8;
                }
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    VertexAttribute attribute =
                        (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!source.HasVertexAttribute(attribute))
                        continue;
                    List<Vector4> values = new List<Vector4>();
                    source.GetUVs(channel, values);
                    if (values.Count != source.vertexCount)
                        throw new InvalidOperationException($"UV{channel} is incomplete.");
                    uvs[channel] = new UVStream
                    {
                        dimension = source.GetVertexAttributeDimension(attribute),
                        values = values
                    };
                }
            }

            public Vector3 GetPosition(int vertex)
            {
                return positions[vertex];
            }

            public int AppendCopy(int sourceVertex, Vector3 normal, Vector3 fallbackTangent)
            {
                int index = positions.Count;
                Vector3 position = positions[sourceVertex];
                bool projectedReveal = revealUVs.TryProject(
                    position,
                    normal,
                    out Vector2 projectedUV,
                    out Vector3 projectedTangent,
                    out float projectedHandedness);
                positions.Add(position);
                normals.Add(normal);
                if (tangents != null)
                {
                    Vector4 source = tangents[sourceVertex];
                    Vector3 direction = projectedReveal
                        ? projectedTangent
                        : new Vector3(source.x, source.y, source.z);
                    if (!projectedReveal)
                    {
                        direction -= normal * Vector3.Dot(direction, normal);
                        if (direction.sqrMagnitude <= MatrixEpsilon)
                            direction = fallbackTangent;
                        direction.Normalize();
                    }
                    tangents.Add(new Vector4(
                        direction.x,
                        direction.y,
                        direction.z,
                        projectedReveal
                            ? projectedHandedness
                            : source.w >= 0f ? 1f : -1f));
                }
                if (colors != null)
                    colors.Add(colors[sourceVertex]);
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] != null)
                        uvs[channel].values.Add(uvs[channel].values[sourceVertex]);
                }
                if (projectedReveal && uvs[0] != null)
                {
                    Vector4 uv0 = uvs[0].values[index];
                    uv0.x = projectedUV.x;
                    uv0.y = projectedUV.y;
                    uvs[0].values[index] = uv0;
                }
                return index;
            }

            public void WriteTo(Mesh mesh)
            {
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                if (tangents != null)
                    mesh.SetTangents(tangents);
                if (colors != null)
                {
                    if (colorsWereUNorm8)
                    {
                        List<Color32> converted = new List<Color32>(colors.Count);
                        for (int i = 0; i < colors.Count; i++)
                            converted.Add(colors[i]);
                        mesh.SetColors(converted);
                    }
                    else
                    {
                        mesh.SetColors(colors);
                    }
                }
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    UVStream stream = uvs[channel];
                    if (stream == null)
                        continue;
                    if (stream.dimension == 2)
                    {
                        List<Vector2> values = new List<Vector2>(stream.values.Count);
                        for (int i = 0; i < stream.values.Count; i++)
                            values.Add(new Vector2(stream.values[i].x, stream.values[i].y));
                        mesh.SetUVs(channel, values);
                    }
                    else if (stream.dimension == 3)
                    {
                        List<Vector3> values = new List<Vector3>(stream.values.Count);
                        for (int i = 0; i < stream.values.Count; i++)
                        {
                            Vector4 value = stream.values[i];
                            values.Add(new Vector3(value.x, value.y, value.z));
                        }
                        mesh.SetUVs(channel, values);
                    }
                    else
                    {
                        mesh.SetUVs(channel, stream.values);
                    }
                }
            }

            private sealed class UVStream
            {
                public int dimension;
                public List<Vector4> values;
            }
        }

        private static Bounds BoundsOf(Vector3[] positions)
        {
            Bounds bounds = new Bounds(positions[0], Vector3.zero);
            for (int i = 1; i < positions.Length; i++)
                bounds.Encapsulate(positions[i]);
            return bounds;
        }

        private static void ClassifyAxisNormal(
            Vector3 normal,
            out PatchKind kind,
            out float alignment)
        {
            float ax = Mathf.Abs(normal.x);
            float ay = Mathf.Abs(normal.y);
            float az = Mathf.Abs(normal.z);
            if (ax >= ay && ax >= az)
            {
                kind = normal.x >= 0f ? PatchKind.PositiveX : PatchKind.NegativeX;
                alignment = ax;
            }
            else if (ay >= az)
            {
                kind = normal.y >= 0f ? PatchKind.PositiveY : PatchKind.NegativeY;
                alignment = ay;
            }
            else
            {
                kind = normal.z >= 0f ? PatchKind.PositiveZ : PatchKind.NegativeZ;
                alignment = az;
            }
        }

        private static Vector3 GetPatchNormal(PatchKind kind)
        {
            switch (kind)
            {
                case PatchKind.PositiveX: return Vector3.right;
                case PatchKind.NegativeX: return Vector3.left;
                case PatchKind.PositiveY: return Vector3.up;
                case PatchKind.NegativeY: return Vector3.down;
                case PatchKind.PositiveZ: return Vector3.forward;
                default: return Vector3.back;
            }
        }

        private static int GetPatchAxis(PatchKind kind)
        {
            switch (kind)
            {
                case PatchKind.PositiveX:
                case PatchKind.NegativeX:
                    return 0;
                case PatchKind.PositiveY:
                case PatchKind.NegativeY:
                    return 1;
                default:
                    return 2;
            }
        }

        private static float GetExpectedPatchPlane(PatchKind kind, Bounds bounds)
        {
            switch (kind)
            {
                case PatchKind.PositiveX: return bounds.max.x;
                case PatchKind.NegativeX: return bounds.min.x;
                case PatchKind.PositiveY: return bounds.max.y;
                case PatchKind.NegativeY: return bounds.min.y;
                case PatchKind.PositiveZ: return bounds.max.z;
                default: return bounds.min.z;
            }
        }

        private static bool ConvexCellTouchesRectangle(
            Vector2[] polygon,
            float halfWidth,
            float halfHeight,
            float tolerance)
        {
            if (!IntervalsOverlapOnAxis(
                    polygon, Vector2.right, halfWidth, halfHeight, tolerance) ||
                !IntervalsOverlapOnAxis(
                    polygon, Vector2.up, halfWidth, halfHeight, tolerance))
            {
                return false;
            }
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                if (edge.sqrMagnitude <= MatrixEpsilon)
                    continue;
                Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                if (!IntervalsOverlapOnAxis(
                        polygon, axis, halfWidth, halfHeight, tolerance))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IntervalsOverlapOnAxis(
            Vector2[] polygon,
            Vector2 axis,
            float halfWidth,
            float halfHeight,
            float tolerance)
        {
            float minimum = Vector2.Dot(polygon[0], axis);
            float maximum = minimum;
            for (int i = 1; i < polygon.Length; i++)
            {
                float projection = Vector2.Dot(polygon[i], axis);
                minimum = Mathf.Min(minimum, projection);
                maximum = Mathf.Max(maximum, projection);
            }
            float rectangleRadius =
                Mathf.Abs(axis.x) * halfWidth +
                Mathf.Abs(axis.y) * halfHeight;
            return maximum >= -rectangleRadius - tolerance &&
                   minimum <= rectangleRadius + tolerance;
        }
    }
}
