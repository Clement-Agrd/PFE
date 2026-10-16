using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    /// <summary>
    /// Creates a grid-snapped, through-thickness opening in an untouched
    /// rectangular wall produced by RealBlend's Solid Wall creation mode.
    ///
    /// This deliberately is not a general-purpose Boolean. Its strict topology
    /// checks let it use the generated wall's aligned front/back grids, which is
    /// substantially cheaper and more predictable than running a CSG kernel.
    /// </summary>
    public static class RealBlendWallOpeningUtility
    {
        private const float NormalAlignment = 0.985f;
        private const float MatrixEpsilon = 0.00000001f;

        /// <summary>
        /// Builds a new mesh containing a rectangular opening. The source mesh
        /// is never mutated, including when validation fails.
        /// </summary>
        public static bool TryCutOpening(
            Mesh source,
            Matrix4x4 localToWorld,
            RealBlendSculptRemoveSample rectangle,
            out Mesh result,
            out RealBlendSculptRemoveReport report,
            out string error)
        {
            return TryCutOpening(
                source,
                localToWorld,
                rectangle,
                -1,
                out result,
                out report,
                out error);
        }

        /// <summary>
        /// Builds a wall opening and optionally routes every generated reveal
        /// triangle into one dedicated triangle submesh. Pass -1 to inherit the
        /// adjacent wall-face submesh (the legacy behaviour).
        /// </summary>
        public static bool TryCutOpening(
            Mesh source,
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

            if (!TryValidateInput(source, localToWorld, rectangle, out error))
                return false;
            if (!TryValidateRevealSubmesh(source, revealSubmesh, out error))
                return false;

            try
            {
                WallTopology topology = new WallTopology(
                    source,
                    localToWorld,
                    rectangle);
                if (!topology.TryBuild(out error))
                    return false;

                if (!topology.TryFindTouchedCellRange(
                        out CellRange opening,
                        out error))
                {
                    return false;
                }

                HashSet<TriangleLocation> removed =
                    new HashSet<TriangleLocation>();
                topology.MarkOpeningTriangles(opening, removed);

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
                topology.AppendRevealGeometry(
                    opening,
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
                    "The RealBlend wall opening could not be built without changing the source mesh.\n" +
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
                error =
                    "The selected mesh is not readable. Enable Read/Write on its importer first.";
                return false;
            }
            if (rectangle.shape != RealBlendSculptRemoveShape.Rectangle ||
                rectangle.width <= 0.000001f ||
                rectangle.height <= 0.000001f)
            {
                error = "Through-wall Remove requires a non-zero rectangular brush sample.";
                return false;
            }
            if (rectangle.worldNormal.sqrMagnitude <= MatrixEpsilon)
            {
                error = "The Remove sample did not contain a valid surface normal.";
                return false;
            }
            if (Mathf.Abs(localToWorld.determinant) <= MatrixEpsilon)
            {
                error = "The wall transform has a zero scale and cannot be cut safely.";
                return false;
            }
            if (!source.name.StartsWith(
                    "RealBlend Solid Wall",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Through-wall Remove is limited to rectangular meshes created as a RealBlend Solid Wall. " +
                    "Use the regular Remove or Boolean Cut mode for other meshes.";
                return false;
            }
            if (source.blendShapeCount > 0 ||
                source.bindposes.Length > 0 ||
                source.HasVertexAttribute(VertexAttribute.BlendWeight) ||
                source.HasVertexAttribute(VertexAttribute.BlendIndices))
            {
                error =
                    "Through-wall Remove is not supported on meshes with blend shapes or skinning data.";
                return false;
            }
            if (!source.HasVertexAttribute(VertexAttribute.Normal) ||
                source.normals.Length != source.vertexCount)
            {
                error =
                    "The generated wall's explicit face normals are missing, so its six source patches cannot be validated safely.";
                return false;
            }
            if (!ValidateSupportedStreams(source, out error))
                return false;

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetBaseVertex(submesh) != 0)
                {
                    error =
                        "Through-wall Remove requires zero-based submesh indices. " +
                        "Use the regular Remove mode for this mesh.";
                    return false;
                }
                if (source.GetIndexCount(submesh) > 0 &&
                    source.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    error =
                        "Through-wall Remove only supports triangle submeshes on generated solid walls.";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateSupportedStreams(Mesh source, out string error)
        {
            error = string.Empty;
            if (source.GetVertexAttributeFormat(VertexAttribute.Position) !=
                    VertexAttributeFormat.Float32 ||
                source.GetVertexAttributeDimension(VertexAttribute.Position) != 3 ||
                source.GetVertexAttributeFormat(VertexAttribute.Normal) !=
                    VertexAttributeFormat.Float32 ||
                source.GetVertexAttributeDimension(VertexAttribute.Normal) != 3)
            {
                error =
                    "The wall uses compressed position or normal streams that cannot be extended losslessly. " +
                    "The mesh was left unchanged.";
                return false;
            }
            if (source.HasVertexAttribute(VertexAttribute.Tangent) &&
                (source.GetVertexAttributeFormat(VertexAttribute.Tangent) !=
                     VertexAttributeFormat.Float32 ||
                 source.GetVertexAttributeDimension(VertexAttribute.Tangent) != 4 ||
                 source.tangents.Length != source.vertexCount))
            {
                error =
                    "The wall uses an unsupported tangent stream. The mesh was left unchanged.";
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
                    error =
                        "The wall uses an unsupported compressed vertex-color format. " +
                        "The mesh was left unchanged.";
                    return false;
                }
            }

            for (int channel = 0; channel < 8; channel++)
            {
                VertexAttribute attribute =
                    (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute))
                    continue;
                if (source.GetVertexAttributeFormat(attribute) !=
                    VertexAttributeFormat.Float32)
                {
                    error =
                        $"UV{channel} uses a compressed format that cannot be extended losslessly. " +
                        "The mesh was left unchanged.";
                    return false;
                }
                int dimension = source.GetVertexAttributeDimension(attribute);
                if (dimension < 2 || dimension > 4)
                {
                    error = $"UV{channel} has an unsupported dimension.";
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
            int outputSubmeshCount = Mathf.Max(
                source.subMeshCount,
                revealSubmesh + 1);
            topologies = new MeshTopology[outputSubmeshCount];
            List<int>[] output = new List<int>[outputSubmeshCount];
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                MeshTopology topology = source.GetTopology(submesh);
                topologies[submesh] = topology;
                int[] indices = source.GetIndices(submesh, true);
                List<int> kept = new List<int>(indices.Length + 128);
                output[submesh] = kept;
                if (topology != MeshTopology.Triangles)
                {
                    kept.AddRange(indices);
                    continue;
                }

                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    inputTriangles++;
                    if (removed.Contains(new TriangleLocation(submesh, offset)))
                        continue;
                    kept.Add(indices[offset]);
                    kept.Add(indices[offset + 1]);
                    kept.Add(indices[offset + 2]);
                    retainedTriangles++;
                }
            }
            for (int submesh = source.subMeshCount;
                 submesh < outputSubmeshCount;
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
                unchecked
                {
                    return submesh * 397 ^ offset;
                }
            }
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

        private sealed class CellTriangles
        {
            public readonly List<TriangleLocation> triangles =
                new List<TriangleLocation>(2);
            public int submesh = -1;
        }

        private sealed class PatchGrid
        {
            public readonly PatchKind kind;
            public readonly int[,] vertices;
            public readonly CellTriangles[,] cells;

            public int UCount => vertices.GetLength(0);
            public int VCount => vertices.GetLength(1);

            public PatchGrid(PatchKind kind, int uCount, int vCount)
            {
                this.kind = kind;
                vertices = new int[uCount, vCount];
                cells = new CellTriangles[uCount - 1, vCount - 1];
                for (int u = 0; u < uCount; u++)
                {
                    for (int v = 0; v < vCount; v++)
                        vertices[u, v] = -1;
                }
                for (int u = 0; u < uCount - 1; u++)
                {
                    for (int v = 0; v < vCount - 1; v++)
                        cells[u, v] = new CellTriangles();
                }
            }
        }

        private sealed class WallTopology
        {
            private readonly Mesh source;
            private readonly Matrix4x4 localToWorld;
            private readonly RealBlendSculptRemoveSample sample;
            private readonly Vector3[] positions;
            private readonly Vector3[] normals;
            private readonly PatchKind[] vertexPatches;
            private readonly bool[] sourcePatchVertices;
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

            public WallTopology(
                Mesh source,
                Matrix4x4 localToWorld,
                RealBlendSculptRemoveSample sample)
            {
                this.source = source;
                this.localToWorld = localToWorld;
                this.sample = sample;
                positions = source.vertices;
                normals = source.normals;
                vertexPatches = new PatchKind[source.vertexCount];
                sourcePatchVertices = new bool[source.vertexCount];
                vertexU = new int[source.vertexCount];
                vertexV = new int[source.vertexCount];
                for (int i = 0; i < source.vertexCount; i++)
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

                Bounds bounds = source.bounds;
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
                    error =
                        "The selected surface is not the aligned front or back grid of a RealBlend Solid Wall.";
                    return false;
                }
                frontKind = localBrushNormal.z >= 0f
                    ? PatchKind.PositiveZ
                    : PatchKind.NegativeZ;
                backKind = localBrushNormal.z >= 0f
                    ? PatchKind.NegativeZ
                    : PatchKind.PositiveZ;

                if (!ClassifyVertices(out error) ||
                    !BuildCoordinateLevels(out error) ||
                    !BuildPatchGrids(out error) ||
                    !AssignTriangles(out error))
                {
                    return false;
                }

                PatchGrid front = grids[frontKind];
                float frontDepth = AverageWorldDepth(front);
                float backDepth = AverageWorldDepth(grids[backKind]);
                if (frontDepth <= backDepth + tolerance)
                {
                    error =
                        "The wall's front and back patches are not separated along the selected surface normal.";
                    return false;
                }
                float centerDepth = Vector3.Dot(
                    sample.worldCenter,
                    brushNormal);
                float worldExtent = GetMaximumWorldExtent();
                float hitTolerance = Mathf.Max(0.0025f, worldExtent * 0.001f);
                if (Mathf.Abs(centerDepth - frontDepth) > hitTolerance)
                {
                    error =
                        "The rectangular sample is not on the selected outer wall surface.";
                    return false;
                }
                return true;
            }

            public bool TryFindTouchedCellRange(
                out CellRange opening,
                out string error)
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
                        Vector2[] projected = new Vector2[4]
                        {
                            Project(front.vertices[x, y]),
                            Project(front.vertices[x + 1, y]),
                            Project(front.vertices[x + 1, y + 1]),
                            Project(front.vertices[x, y + 1])
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
                    error = "The rectangular brush does not touch a front-grid wall cell.";
                    return false;
                }

                opening = new CellRange(minX, maxX, minY, maxY);
                return ValidateNewOpeningRange(opening, out error);
            }

            public void MarkOpeningTriangles(
                CellRange opening,
                HashSet<TriangleLocation> removed)
            {
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];
                for (int x = opening.minX; x <= opening.maxX; x++)
                {
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        AddCellTriangles(front.cells[x, y], removed);
                        AddCellTriangles(back.cells[x, y], removed);
                    }
                }

                if (opening.minX == 0)
                {
                    MarkClosureStrip(
                        grids[PatchKind.NegativeX],
                        opening.minY,
                        opening.maxY,
                        false,
                        removed);
                }
                if (opening.maxX == front.UCount - 2)
                {
                    MarkClosureStrip(
                        grids[PatchKind.PositiveX],
                        opening.minY,
                        opening.maxY,
                        false,
                        removed);
                }
                if (opening.minY == 0)
                {
                    MarkClosureStrip(
                        grids[PatchKind.NegativeY],
                        opening.minX,
                        opening.maxX,
                        true,
                        removed);
                }
                if (opening.maxY == front.VCount - 2)
                {
                    MarkClosureStrip(
                        grids[PatchKind.PositiveY],
                        opening.minX,
                        opening.maxX,
                        true,
                        removed);
                }
            }

            public void AppendRevealGeometry(
                CellRange opening,
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
                    Dictionary<int, int> revealVertices =
                        new Dictionary<int, int>();
                    int boundary = opening.minX;
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        AppendBridgeSegment(
                            front.vertices[boundary, y],
                            front.vertices[boundary, y + 1],
                            back.vertices[boundary, y],
                            back.vertices[boundary, y + 1],
                            Vector3.right,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[opening.minX, y].submesh,
                            revealVertices,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.maxX < front.UCount - 2)
                {
                    Dictionary<int, int> revealVertices =
                        new Dictionary<int, int>();
                    int boundary = opening.maxX + 1;
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        AppendBridgeSegment(
                            front.vertices[boundary, y],
                            front.vertices[boundary, y + 1],
                            back.vertices[boundary, y],
                            back.vertices[boundary, y + 1],
                            Vector3.left,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[opening.maxX, y].submesh,
                            revealVertices,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.minY > 0)
                {
                    Dictionary<int, int> revealVertices =
                        new Dictionary<int, int>();
                    int boundary = opening.minY;
                    for (int x = opening.minX; x <= opening.maxX; x++)
                    {
                        AppendBridgeSegment(
                            front.vertices[x, boundary],
                            front.vertices[x + 1, boundary],
                            back.vertices[x, boundary],
                            back.vertices[x + 1, boundary],
                            Vector3.up,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[x, opening.minY].submesh,
                            revealVertices,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
                if (opening.maxY < front.VCount - 2)
                {
                    Dictionary<int, int> revealVertices =
                        new Dictionary<int, int>();
                    int boundary = opening.maxY + 1;
                    for (int x = opening.minX; x <= opening.maxX; x++)
                    {
                        AppendBridgeSegment(
                            front.vertices[x, boundary],
                            front.vertices[x + 1, boundary],
                            back.vertices[x, boundary],
                            back.vertices[x + 1, boundary],
                            Vector3.down,
                            revealSubmesh >= 0
                                ? revealSubmesh
                                : front.cells[x, opening.maxY].submesh,
                            revealVertices,
                            streams,
                            outputIndices,
                            ref bridgeTriangles,
                            ref rimVertices);
                    }
                }
            }

            private bool ClassifyVertices(out string error)
            {
                error = string.Empty;
                Bounds bounds = source.bounds;
                for (int vertex = 0; vertex < positions.Length; vertex++)
                {
                    Vector3 normal = normals[vertex];
                    if (normal.sqrMagnitude <= MatrixEpsilon)
                    {
                        error = "The generated wall contains a zero-length face normal.";
                        return false;
                    }
                    normal.Normalize();
                    PatchKind kind;
                    float alignment;
                    ClassifyAxisNormal(normal, out kind, out alignment);
                    if (alignment < NormalAlignment)
                    {
                        error =
                            "The mesh contains smoothed or non-axis-aligned vertices and no longer matches generated Solid Wall topology.";
                        return false;
                    }
                    vertexPatches[vertex] = kind;
                    int axis = GetPatchAxis(kind);
                    float expectedPlane = GetExpectedPatchPlane(kind, bounds);
                    if (Mathf.Abs(positions[vertex][axis] - expectedPlane) <= tolerance)
                    {
                        sourcePatchVertices[vertex] = true;
                        patchVertices[kind].Add(vertex);
                    }
                    else if (kind == PatchKind.PositiveX ||
                             kind == PatchKind.NegativeX ||
                             kind == PatchKind.PositiveY ||
                             kind == PatchKind.NegativeY)
                    {
                        // A prior opening adds duplicated hard-edge vertices on
                        // an internal X or Y plane. They are validated with
                        // their reveal triangles later and are intentionally not
                        // allowed to pollute the six immutable source grids.
                        sourcePatchVertices[vertex] = false;
                    }
                    else
                    {
                        error =
                            "The front/back wall grid contains inserted interior vertices and no longer matches the repeatable Solid Wall opening layout.";
                        return false;
                    }
                }

                foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
                {
                    if (patchVertices[kind].Count < 4)
                    {
                        error =
                            "The mesh is missing one or more of the six generated Solid Wall surface patches.";
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
                    vertex => positions[vertex].x);
                yLevels = ClusterValues(
                    patchVertices[PatchKind.PositiveZ],
                    vertex => positions[vertex].y);
                zLevels = ClusterValues(
                    patchVertices[PatchKind.PositiveX],
                    vertex => positions[vertex].z);

                if (xLevels.Length < 2 || yLevels.Length < 2 || zLevels.Length < 2)
                {
                    error = "The generated wall grids do not contain enough rows to form an opening.";
                    return false;
                }

                if (!LevelsMatch(
                        xLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeZ],
                            vertex => positions[vertex].x)) ||
                    !LevelsMatch(
                        yLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeZ],
                            vertex => positions[vertex].y)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeX],
                            vertex => positions[vertex].z)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.PositiveY],
                            vertex => positions[vertex].z)) ||
                    !LevelsMatch(
                        zLevels,
                        ClusterValues(
                            patchVertices[PatchKind.NegativeY],
                            vertex => positions[vertex].z)))
                {
                    error =
                        "The wall's front, back, and thickness vertices are not aligned to the same generated grid.";
                    return false;
                }

                float xMin = xLevels[0];
                float xMax = xLevels[xLevels.Length - 1];
                float yMin = yLevels[0];
                float yMax = yLevels[yLevels.Length - 1];
                float zMin = zLevels[0];
                float zMax = zLevels[zLevels.Length - 1];
                if (!PatchIsOnPlane(PatchKind.PositiveZ, 2, zMax) ||
                    !PatchIsOnPlane(PatchKind.NegativeZ, 2, zMin) ||
                    !PatchIsOnPlane(PatchKind.PositiveX, 0, xMax) ||
                    !PatchIsOnPlane(PatchKind.NegativeX, 0, xMin) ||
                    !PatchIsOnPlane(PatchKind.PositiveY, 1, yMax) ||
                    !PatchIsOnPlane(PatchKind.NegativeY, 1, yMin))
                {
                    error =
                        "The wall's six patches are not on the expected rectangular boundary planes.";
                    return false;
                }
                return true;
            }

            private bool BuildPatchGrids(out string error)
            {
                error = string.Empty;
                if (!TryBuildPatchGrid(
                        PatchKind.PositiveZ,
                        xLevels,
                        yLevels,
                        vertex => positions[vertex].x,
                        vertex => positions[vertex].y,
                        out error) ||
                    !TryBuildPatchGrid(
                        PatchKind.NegativeZ,
                        xLevels,
                        yLevels,
                        vertex => positions[vertex].x,
                        vertex => positions[vertex].y,
                        out error) ||
                    !TryBuildPatchGrid(
                        PatchKind.PositiveX,
                        zLevels,
                        yLevels,
                        vertex => positions[vertex].z,
                        vertex => positions[vertex].y,
                        out error) ||
                    !TryBuildPatchGrid(
                        PatchKind.NegativeX,
                        zLevels,
                        yLevels,
                        vertex => positions[vertex].z,
                        vertex => positions[vertex].y,
                        out error) ||
                    !TryBuildPatchGrid(
                        PatchKind.PositiveY,
                        xLevels,
                        zLevels,
                        vertex => positions[vertex].x,
                        vertex => positions[vertex].z,
                        out error) ||
                    !TryBuildPatchGrid(
                        PatchKind.NegativeY,
                        xLevels,
                        zLevels,
                        vertex => positions[vertex].x,
                        vertex => positions[vertex].z,
                        out error))
                {
                    return false;
                }
                return true;
            }

            private bool AssignTriangles(out string error)
            {
                error = string.Empty;
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                {
                    int[] indices = source.GetIndices(submesh, true);
                    for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                    {
                        int a = indices[offset];
                        int b = indices[offset + 1];
                        int c = indices[offset + 2];
                        if (!IsValidVertex(a) || !IsValidVertex(b) || !IsValidVertex(c))
                        {
                            error = "The wall contains an out-of-range triangle index.";
                            return false;
                        }
                        PatchKind kind = vertexPatches[a];
                        if (vertexPatches[b] != kind || vertexPatches[c] != kind)
                        {
                            error =
                                "The wall contains triangles that join its duplicated source patches and no longer matches generated topology.";
                            return false;
                        }

                        bool sourceA = sourcePatchVertices[a];
                        bool sourceB = sourcePatchVertices[b];
                        bool sourceC = sourcePatchVertices[c];
                        if (!sourceA || !sourceB || !sourceC)
                        {
                            if (sourceA || sourceB || sourceC ||
                                !ValidateExistingRevealTriangle(a, b, c, kind))
                            {
                                error =
                                    "The wall contains unsupported inserted topology. " +
                                    "Existing openings must have been created by RealBlend's aligned wall Remove mode.";
                                return false;
                            }
                            continue;
                        }

                        PatchGrid grid = grids[kind];
                        int minU = Mathf.Min(vertexU[a], Mathf.Min(vertexU[b], vertexU[c]));
                        int maxU = Mathf.Max(vertexU[a], Mathf.Max(vertexU[b], vertexU[c]));
                        int minV = Mathf.Min(vertexV[a], Mathf.Min(vertexV[b], vertexV[c]));
                        int maxV = Mathf.Max(vertexV[a], Mathf.Max(vertexV[b], vertexV[c]));
                        if (minU < 0 || minV < 0 ||
                            maxU != minU + 1 || maxV != minV + 1 ||
                            maxU >= grid.UCount || maxV >= grid.VCount)
                        {
                            error =
                                "A wall triangle no longer spans exactly one generated grid cell.";
                            return false;
                        }

                        Vector3 triangleNormal = Vector3.Cross(
                            positions[b] - positions[a],
                            positions[c] - positions[a]);
                        if (triangleNormal.sqrMagnitude <= MatrixEpsilon ||
                            Vector3.Dot(
                                triangleNormal.normalized,
                                GetPatchNormal(kind)) < NormalAlignment)
                        {
                            error =
                                "A wall triangle has invalid winding for its generated surface patch.";
                            return false;
                        }

                        CellTriangles cell = grid.cells[minU, minV];
                        cell.triangles.Add(new TriangleLocation(submesh, offset));
                        if (cell.submesh < 0)
                            cell.submesh = submesh;
                        else if (cell.submesh != submesh)
                        {
                            error =
                                "Both triangles of a generated wall cell must remain in the same material submesh.";
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
                            CellTriangles cell = grid.cells[u, v];
                            if (cell.triangles.Count == 1 ||
                                cell.triangles.Count > 2 ||
                                (cell.triangles.Count == 2 && cell.submesh < 0))
                            {
                                error =
                                    "A generated wall cell is only partially present. " +
                                    "Use Boolean Cut for topology that was not removed as complete aligned cells.";
                                return false;
                            }
                        }
                    }
                }
                return ValidateFrontBackCellPairing(out error);
            }

            private bool ValidateFrontBackCellPairing(out string error)
            {
                error = string.Empty;
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];
                for (int x = 0; x < front.UCount - 1; x++)
                {
                    for (int y = 0; y < front.VCount - 1; y++)
                    {
                        bool frontMissing = front.cells[x, y].triangles.Count == 0;
                        bool backMissing = back.cells[x, y].triangles.Count == 0;
                        if (frontMissing == backMissing)
                            continue;
                        error =
                            "The front and back wall grids have different missing cells. " +
                            "An aligned through-opening can only be repeated on walls whose earlier cuts removed matching cells on both faces.";
                        return false;
                    }
                }
                return true;
            }

            private bool ValidateNewOpeningRange(
                CellRange opening,
                out string error)
            {
                error = string.Empty;
                PatchGrid front = grids[frontKind];
                PatchGrid back = grids[backKind];
                for (int x = opening.minX; x <= opening.maxX; x++)
                {
                    for (int y = opening.minY; y <= opening.maxY; y++)
                    {
                        if (front.cells[x, y].triangles.Count != 2 ||
                            back.cells[x, y].triangles.Count != 2)
                        {
                            error =
                                "The new wall opening overlaps an existing opening. " +
                                "Place separate openings at least one complete grid cell apart.";
                            return false;
                        }
                    }
                }

                int neighborMinX = Mathf.Max(0, opening.minX - 1);
                int neighborMaxX = Mathf.Min(front.UCount - 2, opening.maxX + 1);
                int neighborMinY = Mathf.Max(0, opening.minY - 1);
                int neighborMaxY = Mathf.Min(front.VCount - 2, opening.maxY + 1);
                for (int x = neighborMinX; x <= neighborMaxX; x++)
                {
                    for (int y = neighborMinY; y <= neighborMaxY; y++)
                    {
                        bool inside = x >= opening.minX && x <= opening.maxX &&
                                      y >= opening.minY && y <= opening.maxY;
                        if (inside || front.cells[x, y].triangles.Count != 0)
                            continue;
                        error =
                            "The new wall opening touches an existing opening. " +
                            "Leave at least one complete wall grid cell between separate openings.";
                        return false;
                    }
                }

                if (opening.minX == 0 &&
                    !ClosureStripIsComplete(
                        grids[PatchKind.NegativeX],
                        opening.minY,
                        opening.maxY,
                        false) ||
                    opening.maxX == front.UCount - 2 &&
                    !ClosureStripIsComplete(
                        grids[PatchKind.PositiveX],
                        opening.minY,
                        opening.maxY,
                        false) ||
                    opening.minY == 0 &&
                    !ClosureStripIsComplete(
                        grids[PatchKind.NegativeY],
                        opening.minX,
                        opening.maxX,
                        true) ||
                    opening.maxY == front.VCount - 2 &&
                    !ClosureStripIsComplete(
                        grids[PatchKind.PositiveY],
                        opening.minX,
                        opening.maxX,
                        true))
                {
                    error =
                        "The new opening overlaps a previously removed outer wall closure strip.";
                    return false;
                }
                return true;
            }

            private static bool ClosureStripIsComplete(
                PatchGrid closure,
                int firstAlongWall,
                int lastAlongWall,
                bool alongU)
            {
                if (alongU)
                {
                    for (int u = firstAlongWall; u <= lastAlongWall; u++)
                    {
                        for (int v = 0; v < closure.VCount - 1; v++)
                        {
                            if (closure.cells[u, v].triangles.Count != 2)
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
                            if (closure.cells[u, v].triangles.Count != 2)
                                return false;
                        }
                    }
                }
                return true;
            }

            private bool ValidateExistingRevealTriangle(
                int a,
                int b,
                int c,
                PatchKind kind)
            {
                if (kind == PatchKind.PositiveZ || kind == PatchKind.NegativeZ)
                    return false;

                Vector3 pa = positions[a];
                Vector3 pb = positions[b];
                Vector3 pc = positions[c];
                Vector3 geometricNormal = Vector3.Cross(pb - pa, pc - pa);
                if (geometricNormal.sqrMagnitude <= MatrixEpsilon ||
                    Vector3.Dot(
                        geometricNormal.normalized,
                        GetPatchNormal(kind)) < NormalAlignment)
                {
                    return false;
                }

                if (kind == PatchKind.PositiveX || kind == PatchKind.NegativeX)
                {
                    if (Mathf.Abs(pa.x - pb.x) > tolerance ||
                        Mathf.Abs(pa.x - pc.x) > tolerance ||
                        pa.x <= xLevels[0] + tolerance ||
                        pa.x >= xLevels[xLevels.Length - 1] - tolerance)
                    {
                        return false;
                    }
                    int ay = FindLevel(yLevels, pa.y);
                    int by = FindLevel(yLevels, pb.y);
                    int cy = FindLevel(yLevels, pc.y);
                    int az = FindLevel(zLevels, pa.z);
                    int bz = FindLevel(zLevels, pb.z);
                    int cz = FindLevel(zLevels, pc.z);
                    return AreRevealTriangleCoordinatesValid(
                        ay,
                        by,
                        cy,
                        az,
                        bz,
                        cz,
                        yLevels.Length,
                        zLevels.Length);
                }

                if (Mathf.Abs(pa.y - pb.y) > tolerance ||
                    Mathf.Abs(pa.y - pc.y) > tolerance ||
                    pa.y <= yLevels[0] + tolerance ||
                    pa.y >= yLevels[yLevels.Length - 1] - tolerance)
                {
                    return false;
                }
                int ax = FindLevel(xLevels, pa.x);
                int bx = FindLevel(xLevels, pb.x);
                int cx = FindLevel(xLevels, pc.x);
                int azLevel = FindLevel(zLevels, pa.z);
                int bzLevel = FindLevel(zLevels, pb.z);
                int czLevel = FindLevel(zLevels, pc.z);
                return AreRevealTriangleCoordinatesValid(
                    ax,
                    bx,
                    cx,
                    azLevel,
                    bzLevel,
                    czLevel,
                    xLevels.Length,
                    zLevels.Length);
            }

            private static bool AreRevealTriangleCoordinatesValid(
                int alongA,
                int alongB,
                int alongC,
                int depthA,
                int depthB,
                int depthC,
                int alongLevelCount,
                int depthLevelCount)
            {
                if (alongA < 0 || alongB < 0 || alongC < 0 ||
                    depthA < 0 || depthB < 0 || depthC < 0)
                {
                    return false;
                }
                int minAlong = Mathf.Min(alongA, Mathf.Min(alongB, alongC));
                int maxAlong = Mathf.Max(alongA, Mathf.Max(alongB, alongC));
                int minDepth = Mathf.Min(depthA, Mathf.Min(depthB, depthC));
                int maxDepth = Mathf.Max(depthA, Mathf.Max(depthB, depthC));
                return minAlong >= 0 &&
                       maxAlong < alongLevelCount &&
                       maxAlong == minAlong + 1 &&
                       minDepth == 0 &&
                       maxDepth == depthLevelCount - 1;
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
                    error =
                        $"The {kind} wall patch is not a complete rectangular vertex grid.";
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
                        error =
                            $"The {kind} wall patch has a duplicate or off-grid vertex.";
                        return false;
                    }
                    grid.vertices[u, v] = vertex;
                    vertexU[vertex] = u;
                    vertexV[vertex] = v;
                }
                grids[kind] = grid;
                return true;
            }

            private void AppendBridgeSegment(
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
                Vector3 segment = positions[frontB] - positions[frontA];
                Vector3 tangent = segment -
                                  desiredNormal * Vector3.Dot(segment, desiredNormal);
                if (tangent.sqrMagnitude <= MatrixEpsilon)
                    tangent = Vector3.Cross(desiredNormal, Vector3.forward);
                if (tangent.sqrMagnitude <= MatrixEpsilon)
                    tangent = Vector3.Cross(desiredNormal, Vector3.up);
                tangent.Normalize();

                int fa = GetOrAppendRevealVertex(
                    frontA,
                    desiredNormal,
                    tangent,
                    revealVertices,
                    streams,
                    ref rimVertices);
                int fb = GetOrAppendRevealVertex(
                    frontB,
                    desiredNormal,
                    tangent,
                    revealVertices,
                    streams,
                    ref rimVertices);
                int ba = GetOrAppendRevealVertex(
                    backA,
                    desiredNormal,
                    tangent,
                    revealVertices,
                    streams,
                    ref rimVertices);
                int bb = GetOrAppendRevealVertex(
                    backB,
                    desiredNormal,
                    tangent,
                    revealVertices,
                    streams,
                    ref rimVertices);

                bool forward = Vector3.Dot(
                    Vector3.Cross(
                        streams.GetPosition(fb) - streams.GetPosition(fa),
                        streams.GetPosition(bb) - streams.GetPosition(fa)),
                    desiredNormal) >= 0f;
                List<int> indices = outputIndices[submesh];
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

            private static void MarkClosureStrip(
                PatchGrid closure,
                int firstAlongWall,
                int lastAlongWall,
                bool alongU,
                HashSet<TriangleLocation> removed)
            {
                if (alongU)
                {
                    for (int u = firstAlongWall; u <= lastAlongWall; u++)
                    {
                        for (int v = 0; v < closure.VCount - 1; v++)
                            AddCellTriangles(closure.cells[u, v], removed);
                    }
                }
                else
                {
                    for (int u = 0; u < closure.UCount - 1; u++)
                    {
                        for (int v = firstAlongWall; v <= lastAlongWall; v++)
                            AddCellTriangles(closure.cells[u, v], removed);
                    }
                }
            }

            private static void AddCellTriangles(
                CellTriangles cell,
                HashSet<TriangleLocation> removed)
            {
                for (int i = 0; i < cell.triangles.Count; i++)
                    removed.Add(cell.triangles[i]);
            }

            private Vector2 Project(int vertex)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(positions[vertex]);
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
                            positions[grid.vertices[u, v]]);
                        sum += Vector3.Dot(world, brushNormal);
                        count++;
                    }
                }
                return count > 0 ? sum / count : 0f;
            }

            private float GetMaximumWorldExtent()
            {
                Vector3 first = localToWorld.MultiplyPoint3x4(positions[0]);
                Bounds bounds = new Bounds(first, Vector3.zero);
                for (int i = 1; i < positions.Length; i++)
                    bounds.Encapsulate(localToWorld.MultiplyPoint3x4(positions[i]));
                return Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z));
            }

            private float[] ClusterValues(
                List<int> vertices,
                Func<int, float> getValue)
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

            private bool PatchIsOnPlane(
                PatchKind kind,
                int axis,
                float expected)
            {
                List<int> vertices = patchVertices[kind];
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 position = positions[vertices[i]];
                    if (Mathf.Abs(position[axis] - expected) > tolerance)
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
                        bestDistance = distance;
                        best = i;
                    }
                }
                return bestDistance <= tolerance ? best : -1;
            }

            private bool IsValidVertex(int vertex)
            {
                return vertex >= 0 && vertex < positions.Length;
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
                    kind = normal.x >= 0f
                        ? PatchKind.PositiveX
                        : PatchKind.NegativeX;
                    alignment = ax;
                }
                else if (ay >= az)
                {
                    kind = normal.y >= 0f
                        ? PatchKind.PositiveY
                        : PatchKind.NegativeY;
                    alignment = ay;
                }
                else
                {
                    kind = normal.z >= 0f
                        ? PatchKind.PositiveZ
                        : PatchKind.NegativeZ;
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

            private static float GetExpectedPatchPlane(
                PatchKind kind,
                Bounds bounds)
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
                        polygon,
                        Vector2.right,
                        halfWidth,
                        halfHeight,
                        tolerance) ||
                    !IntervalsOverlapOnAxis(
                        polygon,
                        Vector2.up,
                        halfWidth,
                        halfHeight,
                        tolerance))
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
                            polygon,
                            axis,
                            halfWidth,
                            halfHeight,
                            tolerance))
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
                float polygonMin = Vector2.Dot(polygon[0], axis);
                float polygonMax = polygonMin;
                for (int i = 1; i < polygon.Length; i++)
                {
                    float projection = Vector2.Dot(polygon[i], axis);
                    polygonMin = Mathf.Min(polygonMin, projection);
                    polygonMax = Mathf.Max(polygonMax, projection);
                }
                float rectangleRadius =
                    Mathf.Abs(axis.x) * halfWidth +
                    Mathf.Abs(axis.y) * halfHeight;
                return polygonMax >= -rectangleRadius - tolerance &&
                       polygonMin <= rectangleRadius + tolerance;
            }
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
                if (sourceTangents != null &&
                    sourceTangents.Length == source.vertexCount)
                {
                    tangents = new List<Vector4>(sourceTangents);
                }

                if (source.HasVertexAttribute(VertexAttribute.Color))
                {
                    colors = new List<Color>();
                    source.GetColors(colors);
                    if (colors.Count != source.vertexCount)
                    {
                        throw new InvalidOperationException(
                            "The vertex-color stream does not match the wall's vertex count.");
                    }
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
                    {
                        throw new InvalidOperationException(
                            $"UV{channel} does not match the wall's vertex count.");
                    }
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

            public int AppendCopy(
                int sourceVertex,
                Vector3 normal,
                Vector3 tangentDirection)
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
                    Vector4 sourceTangent = tangents[sourceVertex];
                    Vector3 projected = projectedReveal
                        ? projectedTangent
                        : new Vector3(
                            sourceTangent.x,
                            sourceTangent.y,
                            sourceTangent.z);
                    if (!projectedReveal)
                    {
                        projected -= normal * Vector3.Dot(projected, normal);
                        if (projected.sqrMagnitude <= MatrixEpsilon)
                            projected = tangentDirection;
                        projected.Normalize();
                    }
                    tangents.Add(new Vector4(
                        projected.x,
                        projected.y,
                        projected.z,
                        projectedReveal
                            ? projectedHandedness
                            : sourceTangent.w >= 0f ? 1f : -1f));
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
                        {
                            Vector4 value = stream.values[i];
                            values.Add(new Vector2(value.x, value.y));
                        }
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
    }

    /// <summary>
    /// Extends the planar UV layout already present on a generated solid wall
    /// onto duplicated opening-reveal vertices. Matching the untouched outer
    /// patch preserves custom tile size/rotation; the fallback is a stable
    /// local-metre box projection.
    /// </summary>
    internal sealed class RealBlendRevealUVProjector
    {
        private const float NormalAlignment = 0.985f;
        private const float ProjectionEpsilon = 0.0000000001f;

        private enum RevealFace
        {
            PositiveX,
            NegativeX,
            PositiveY,
            NegativeY
        }

        private sealed class FaceProjection
        {
            public Vector3 axisA;
            public Vector3 axisB;
            public Vector2 gradientA;
            public Vector2 gradientB;
            public Vector2 offset;

            public Vector2 Project(Vector3 position)
            {
                return offset +
                       gradientA * Vector3.Dot(position, axisA) +
                       gradientB * Vector3.Dot(position, axisB);
            }

            public bool TryGetTangentFrame(
                Vector3 normal,
                out Vector3 tangent,
                out float handedness)
            {
                float determinant =
                    gradientA.x * gradientB.y -
                    gradientB.x * gradientA.y;
                if (Mathf.Abs(determinant) <= ProjectionEpsilon)
                {
                    tangent = Vector3.zero;
                    handedness = 1f;
                    return false;
                }

                tangent =
                    (axisA * gradientB.y - axisB * gradientA.y) /
                    determinant;
                Vector3 bitangent =
                    (-axisA * gradientB.x + axisB * gradientA.x) /
                    determinant;
                if (tangent.sqrMagnitude <= ProjectionEpsilon ||
                    bitangent.sqrMagnitude <= ProjectionEpsilon)
                {
                    tangent = Vector3.zero;
                    handedness = 1f;
                    return false;
                }

                tangent.Normalize();
                bitangent.Normalize();
                Vector3 safeNormal = normal.sqrMagnitude > ProjectionEpsilon
                    ? normal.normalized
                    : Vector3.forward;
                handedness = Vector3.Dot(
                    Vector3.Cross(safeNormal, tangent),
                    bitangent) >= 0f
                    ? 1f
                    : -1f;
                return true;
            }
        }

        private readonly FaceProjection[] projections =
            new FaceProjection[4];
        private readonly bool hasUV0;

        public RealBlendRevealUVProjector(Mesh source)
        {
            if (source == null ||
                !source.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                return;
            }

            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            List<Vector4> uv0 = new List<Vector4>();
            source.GetUVs(0, uv0);
            if (positions == null || normals == null ||
                positions.Length != source.vertexCount ||
                normals.Length != source.vertexCount ||
                uv0.Count != source.vertexCount)
            {
                return;
            }

            hasUV0 = true;
            Bounds bounds = source.bounds;
            float tolerance = Mathf.Max(
                0.00001f,
                Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) *
                0.00001f);
            for (int faceIndex = 0; faceIndex < projections.Length; faceIndex++)
            {
                RevealFace face = (RevealFace)faceIndex;
                FaceProjection projection = CreateFallback(face);
                if (TryFitOuterPatch(
                        face,
                        positions,
                        normals,
                        uv0,
                        bounds,
                        tolerance,
                        out FaceProjection fitted))
                {
                    projection = fitted;
                }
                projections[faceIndex] = projection;
            }
        }

        public bool TryProject(
            Vector3 position,
            Vector3 normal,
            out Vector2 uv,
            out Vector3 tangent,
            out float handedness)
        {
            uv = Vector2.zero;
            tangent = Vector3.zero;
            handedness = 1f;
            if (!hasUV0 || !TryClassify(normal, out RevealFace face))
                return false;

            FaceProjection projection = projections[(int)face];
            if (projection == null ||
                !projection.TryGetTangentFrame(
                    normal,
                    out tangent,
                    out handedness))
            {
                return false;
            }
            uv = projection.Project(position);
            return true;
        }

        private static FaceProjection CreateFallback(RevealFace face)
        {
            switch (face)
            {
                case RevealFace.PositiveX:
                    return new FaceProjection
                    {
                        axisA = Vector3.forward,
                        axisB = Vector3.up,
                        gradientA = Vector2.left,
                        gradientB = Vector2.up
                    };
                case RevealFace.NegativeX:
                    return new FaceProjection
                    {
                        axisA = Vector3.forward,
                        axisB = Vector3.up,
                        gradientA = Vector2.right,
                        gradientB = Vector2.up
                    };
                case RevealFace.PositiveY:
                    return new FaceProjection
                    {
                        axisA = Vector3.right,
                        axisB = Vector3.forward,
                        gradientA = Vector2.right,
                        gradientB = Vector2.up
                    };
                default:
                    return new FaceProjection
                    {
                        axisA = Vector3.right,
                        axisB = Vector3.forward,
                        gradientA = Vector2.right,
                        gradientB = Vector2.down
                    };
            }
        }

        private static bool TryFitOuterPatch(
            RevealFace face,
            Vector3[] positions,
            Vector3[] normals,
            List<Vector4> uv0,
            Bounds bounds,
            float tolerance,
            out FaceProjection projection)
        {
            projection = null;
            FaceProjection fallback = CreateFallback(face);
            int anchor = -1;
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                if (MatchesOuterPatch(
                        face,
                        positions[vertex],
                        normals[vertex],
                        bounds,
                        tolerance))
                {
                    anchor = vertex;
                    break;
                }
            }
            if (anchor < 0)
                return false;

            float anchorA = Vector3.Dot(positions[anchor], fallback.axisA);
            float anchorB = Vector3.Dot(positions[anchor], fallback.axisB);
            int alongA = -1;
            int alongB = -1;
            float bestA = tolerance;
            float bestB = tolerance;
            for (int vertex = anchor + 1; vertex < positions.Length; vertex++)
            {
                if (!MatchesOuterPatch(
                        face,
                        positions[vertex],
                        normals[vertex],
                        bounds,
                        tolerance))
                {
                    continue;
                }

                float deltaA =
                    Vector3.Dot(positions[vertex], fallback.axisA) - anchorA;
                float deltaB =
                    Vector3.Dot(positions[vertex], fallback.axisB) - anchorB;
                if (Mathf.Abs(deltaB) <= tolerance &&
                    Mathf.Abs(deltaA) > bestA)
                {
                    bestA = Mathf.Abs(deltaA);
                    alongA = vertex;
                }
                if (Mathf.Abs(deltaA) <= tolerance &&
                    Mathf.Abs(deltaB) > bestB)
                {
                    bestB = Mathf.Abs(deltaB);
                    alongB = vertex;
                }
            }
            if (alongA < 0 || alongB < 0)
                return false;

            float distanceA =
                Vector3.Dot(positions[alongA], fallback.axisA) - anchorA;
            float distanceB =
                Vector3.Dot(positions[alongB], fallback.axisB) - anchorB;
            if (Mathf.Abs(distanceA) <= tolerance ||
                Mathf.Abs(distanceB) <= tolerance)
            {
                return false;
            }

            Vector2 anchorUV = new Vector2(uv0[anchor].x, uv0[anchor].y);
            Vector2 gradientA =
                (new Vector2(uv0[alongA].x, uv0[alongA].y) - anchorUV) /
                distanceA;
            Vector2 gradientB =
                (new Vector2(uv0[alongB].x, uv0[alongB].y) - anchorUV) /
                distanceB;
            float determinant =
                gradientA.x * gradientB.y -
                gradientB.x * gradientA.y;
            if (Mathf.Abs(determinant) <= ProjectionEpsilon)
                return false;

            projection = new FaceProjection
            {
                axisA = fallback.axisA,
                axisB = fallback.axisB,
                gradientA = gradientA,
                gradientB = gradientB,
                offset = anchorUV - gradientA * anchorA - gradientB * anchorB
            };
            return true;
        }

        private static bool MatchesOuterPatch(
            RevealFace face,
            Vector3 position,
            Vector3 normal,
            Bounds bounds,
            float tolerance)
        {
            switch (face)
            {
                case RevealFace.PositiveX:
                    return normal.x >= NormalAlignment &&
                           Mathf.Abs(position.x - bounds.max.x) <= tolerance;
                case RevealFace.NegativeX:
                    return normal.x <= -NormalAlignment &&
                           Mathf.Abs(position.x - bounds.min.x) <= tolerance;
                case RevealFace.PositiveY:
                    return normal.y >= NormalAlignment &&
                           Mathf.Abs(position.y - bounds.max.y) <= tolerance;
                default:
                    return normal.y <= -NormalAlignment &&
                           Mathf.Abs(position.y - bounds.min.y) <= tolerance;
            }
        }

        private static bool TryClassify(
            Vector3 normal,
            out RevealFace face)
        {
            float x = Mathf.Abs(normal.x);
            float y = Mathf.Abs(normal.y);
            if (x >= y && x >= NormalAlignment)
            {
                face = normal.x >= 0f
                    ? RevealFace.PositiveX
                    : RevealFace.NegativeX;
                return true;
            }
            if (y >= NormalAlignment)
            {
                face = normal.y >= 0f
                    ? RevealFace.PositiveY
                    : RevealFace.NegativeY;
                return true;
            }
            face = default;
            return false;
        }
    }
}
