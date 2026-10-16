using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    public enum RealBlendSculptRemoveShape
    {
        Circle,
        Rectangle
    }

    public struct RealBlendSculptRemoveSample
    {
        public Vector3 worldCenter;
        public Vector3 worldNormal;
        public RealBlendSculptRemoveShape shape;
        public float radius;
        public float width;
        public float height;

        public static RealBlendSculptRemoveSample Circle(
            Vector3 center,
            Vector3 normal,
            float radius)
        {
            return new RealBlendSculptRemoveSample
            {
                worldCenter = center,
                worldNormal = normal,
                shape = RealBlendSculptRemoveShape.Circle,
                radius = Mathf.Max(0.0001f, radius),
                width = Mathf.Max(0.0002f, radius * 2f),
                height = Mathf.Max(0.0002f, radius * 2f)
            };
        }

        public static RealBlendSculptRemoveSample Rectangle(
            Vector3 center,
            Vector3 normal,
            float width,
            float height)
        {
            return new RealBlendSculptRemoveSample
            {
                worldCenter = center,
                worldNormal = normal,
                shape = RealBlendSculptRemoveShape.Rectangle,
                radius = Mathf.Max(0.0001f, Mathf.Max(width, height) * 0.5f),
                width = Mathf.Max(0.0002f, width),
                height = Mathf.Max(0.0002f, height)
            };
        }
    }

    public sealed class RealBlendSculptRemoveOptions
    {
        public bool throughWall;
        public int circleSegments = 32;
        public float surfaceDepthTolerance;
    }

    public struct RealBlendSculptRemoveReport
    {
        public int inputTriangles;
        public int outputTriangles;
        public int insertedVertices;
        public int rimVertices;
        public int bridgeTriangles;
    }

    /// <summary>
    /// Topology-aware Remove implementation. The legacy Sculpt Remove path only
    /// changes index buffers; this utility is called exclusively by the opt-in
    /// shaped-boundary/through-wall modes.
    /// </summary>
    public static class RealBlendSculptRemoveUtility
    {
        private const float FacingEpsilon = 0.01f;
        private const float GeometryEpsilon = 0.000001f;
        private const int TopologyPrecision = 100000;

        public static bool TryRemove(
            Mesh source,
            Matrix4x4 localToWorld,
            IList<RealBlendSculptRemoveSample> samples,
            RealBlendSculptRemoveOptions options,
            out Mesh result,
            out RealBlendSculptRemoveReport report,
            out string error)
        {
            result = null;
            report = default;
            error = string.Empty;
            options = options ?? new RealBlendSculptRemoveOptions();

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
            if (samples == null || samples.Count == 0)
            {
                error = "The stroke did not contain a valid brush sample.";
                return false;
            }
            if (source.blendShapeCount > 0 ||
                source.bindposes.Length > 0 ||
                source.HasVertexAttribute(VertexAttribute.BlendWeight) ||
                source.HasVertexAttribute(VertexAttribute.BlendIndices))
            {
                error =
                    "Shaped topology removal is not supported on meshes with blend shapes or skinning. " +
                    "Disable the shaped option to use the original index-only Remove behavior without altering those streams.";
                return false;
            }

            Matrix4x4 worldToLocal = localToWorld.inverse;
            List<TriangleRecord> triangles = BuildTriangleRecords(
                source,
                localToWorld,
                out int inputTriangleCount);
            if (triangles.Count == 0)
            {
                error = "The selected mesh has no readable triangle submeshes.";
                return false;
            }

            List<BrushFrame> frames = new List<BrushFrame>(samples.Count);
            int circleSegments = Mathf.Clamp(options.circleSegments, 8, 96);
            for (int i = 0; i < samples.Count; i++)
            {
                if (TryBuildFrame(samples[i], circleSegments, out BrushFrame frame))
                    frames.Add(frame);
            }
            if (frames.Count == 0)
            {
                error = "The stroke normal or brush dimensions were invalid.";
                return false;
            }

            // A through-wall opening is intentionally one coherent opening per
            // gesture. The editor keeps the last brush sample when dragging.
            if (options.throughWall && frames.Count > 1)
                frames.RemoveRange(0, frames.Count - 1);

            ThroughContext through = null;
            if (options.throughWall)
            {
                if (!TryBuildThroughContext(
                        source,
                        triangles,
                        frames[0],
                        out through,
                        out error))
                {
                    return false;
                }
            }

            MeshStreams streams = new MeshStreams(source);
            List<int>[] outputIndices = new List<int>[source.subMeshCount];
            MeshTopology[] topologies = new MeshTopology[source.subMeshCount];
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                topologies[submesh] = source.GetTopology(submesh);
                outputIndices[submesh] = topologies[submesh] == MeshTopology.Triangles
                    ? new List<int>((int)source.GetIndexCount(submesh) + 256)
                    : new List<int>(source.GetIndices(submesh, true));
            }

            List<RimPoint> frontRim = options.throughWall
                ? new List<RimPoint>()
                : null;
            List<RimPoint> backRim = options.throughWall
                ? new List<RimPoint>()
                : null;
            List<RimSegment> frontRimSegments = options.throughWall
                ? new List<RimSegment>()
                : null;
            List<RimSegment> backRimSegments = options.throughWall
                ? new List<RimSegment>()
                : null;
            int clippedTriangleCount = 0;
            Dictionary<EdgeInterpolationKey, int> sharedEdgeVertexCache =
                new Dictionary<EdgeInterpolationKey, int>();
            Dictionary<SourceEdgeKey, HashSet<int>> sourceEdgeBreaks =
                new Dictionary<SourceEdgeKey, HashSet<int>>();
            List<PendingTriangle> pendingTriangles =
                new List<PendingTriangle>(triangles.Count * 2);

            for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                TriangleRecord triangle = triangles[triangleIndex];
                List<List<ClipVertex>> pieces = new List<List<ClipVertex>>(1)
                {
                    new List<ClipVertex>(3)
                    {
                        new ClipVertex(new Vector3(1f, 0f, 0f)),
                        new ClipVertex(new Vector3(0f, 1f, 0f)),
                        new ClipVertex(new Vector3(0f, 0f, 1f))
                    }
                };
                bool changed = false;

                for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                {
                    BrushFrame frame = frames[frameIndex];
                    if (!ShouldClipTriangle(
                            triangle,
                            frame,
                            through,
                            options.surfaceDepthTolerance))
                    {
                        continue;
                    }

                    List<List<ClipVertex>> nextPieces = new List<List<ClipVertex>>();
                    for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                    {
                        List<ClipVertex> piece = pieces[pieceIndex];
                        if (!ConvexPolygonsIntersect(
                                ProjectPolygon(piece, triangle, frame),
                                frame.polygon))
                        {
                            nextPieces.Add(piece);
                            continue;
                        }

                        changed = true;
                        SubtractConvexPolygon(
                            piece,
                            triangle,
                            frame,
                            nextPieces);
                    }
                    pieces = nextPieces;
                    if (pieces.Count == 0)
                        break;
                }

                if (!changed)
                {
                    pendingTriangles.Add(new PendingTriangle(
                        triangle,
                        new ClipVertex(new Vector3(1f, 0f, 0f)), triangle.a,
                        new ClipVertex(new Vector3(0f, 1f, 0f)), triangle.b,
                        new ClipVertex(new Vector3(0f, 0f, 1f)), triangle.c,
                        triangle.worldNormal.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon));
                    continue;
                }

                clippedTriangleCount++;
                pieces = MakePieceBoundariesConforming(pieces);
                RegisterSourceEdgeBreaks(
                    triangle,
                    pieces,
                    sourceEdgeBreaks);
                Dictionary<BarycentricKey, int> triangleVertexCache =
                    new Dictionary<BarycentricKey, int>();
                for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                {
                    List<ClipVertex> piece = CleanPolygon(pieces[pieceIndex]);
                    if (piece.Count < 3)
                        continue;

                    Vector3 centerBarycentric = Vector3.zero;
                    for (int i = 0; i < piece.Count; i++)
                        centerBarycentric += piece[i].barycentric;
                    centerBarycentric /= piece.Count;
                    int center = GetOrAppendClipVertex(
                        streams,
                        triangle,
                        new ClipVertex(centerBarycentric),
                        triangleVertexCache,
                        sharedEdgeVertexCache);
                    RimPoint?[] pieceRim = new RimPoint?[piece.Count];
                    bool isFrontRim = triangle.facing > 0f;
                    bool isBackRim = triangle.facing < 0f;
                    for (int i = 0; i < piece.Count; i++)
                    {
                        int first = GetOrAppendClipVertex(
                            streams,
                            triangle,
                            piece[i],
                            triangleVertexCache,
                            sharedEdgeVertexCache);
                        int second = GetOrAppendClipVertex(
                            streams,
                            triangle,
                            piece[(i + 1) % piece.Count],
                            triangleVertexCache,
                            sharedEdgeVertexCache);
                        if (!IsDegenerate(
                                streams.positions[center],
                                streams.positions[first],
                                streams.positions[second]))
                        {
                            pendingTriangles.Add(new PendingTriangle(
                                triangle,
                                new ClipVertex(centerBarycentric), center,
                                piece[i], first,
                                piece[(i + 1) % piece.Count], second));
                        }
                    }

                    if (through == null)
                        continue;

                    for (int i = 0; i < piece.Count; i++)
                    {
                        ClipVertex clipVertex = piece[i];
                        Vector3 world = triangle.WorldPosition(
                            NormalizeBarycentric(clipVertex.barycentric));
                        Vector2 projected = through.frame.Project(world);
                        if (!through.frame.TryGetBoundaryParameter(
                                projected,
                                out float perimeterParameter))
                        {
                            continue;
                        }

                        int surfaceIndex = GetOrAppendClipVertex(
                            streams,
                            triangle,
                            clipVertex,
                            triangleVertexCache,
                            sharedEdgeVertexCache);
                        world = localToWorld.MultiplyPoint3x4(
                            streams.positions[surfaceIndex]);
                        RimPoint rimPoint = new RimPoint
                        {
                            parameter = perimeterParameter,
                            worldPosition = world,
                            surfaceVertex = surfaceIndex,
                            submesh = triangle.submesh
                        };
                        if (triangle.facing > 0f &&
                            through.IsFrontDepth(Vector3.Dot(
                                world - through.frame.center,
                                through.frame.normal)))
                        {
                            frontRim.Add(rimPoint);
                            pieceRim[i] = rimPoint;
                        }
                        else if (triangle.facing < 0f &&
                                 through.IsBackDepth(Vector3.Dot(
                                     world - through.frame.center,
                                     through.frame.normal)))
                        {
                            backRim.Add(rimPoint);
                            pieceRim[i] = rimPoint;
                        }
                    }

                    List<RimSegment> rimSegments = isFrontRim
                        ? frontRimSegments
                        : isBackRim ? backRimSegments : null;
                    if (rimSegments == null)
                        continue;
                    for (int i = 0; i < pieceRim.Length; i++)
                    {
                        RimPoint? first = pieceRim[i];
                        RimPoint? second = pieceRim[(i + 1) % pieceRim.Length];
                        if (!first.HasValue || !second.HasValue)
                            continue;
                        Vector2 midpoint = through.frame.Project(
                            (first.Value.worldPosition + second.Value.worldPosition) * 0.5f);
                        if (!through.frame.TryGetBoundaryParameter(midpoint, out _))
                            continue;
                        rimSegments.Add(new RimSegment(
                            first.Value.parameter,
                            second.Value.parameter));
                    }
                }
            }

            WritePendingTriangles(
                streams,
                outputIndices,
                pendingTriangles,
                sharedEdgeVertexCache,
                sourceEdgeBreaks);

            int bridgeTriangles = 0;
            int rimVertexCount = 0;
            if (through != null)
            {
                if (!TryBuildBridge(
                        streams,
                        outputIndices,
                        localToWorld,
                        worldToLocal,
                        through,
                        frontRim,
                        backRim,
                        frontRimSegments,
                        backRimSegments,
                        out bridgeTriangles,
                        out rimVertexCount,
                        out error))
                {
                    return false;
                }
            }

            if (clippedTriangleCount == 0)
            {
                error = "No triangles intersected the shaped brush boundary.";
                return false;
            }

            result = new Mesh
            {
                name = source.name,
                indexFormat = streams.positions.Count > 65535
                    ? IndexFormat.UInt32
                    : source.indexFormat
            };
            streams.WriteTo(result);
            result.subMeshCount = source.subMeshCount;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                result.SetIndices(
                    outputIndices[submesh],
                    topologies[submesh],
                    submesh,
                    false,
                    0);
            }
            if (!streams.HasNormals)
                result.RecalculateNormals();
            result.RecalculateBounds();

            int outputTriangleCount = 0;
            for (int submesh = 0; submesh < result.subMeshCount; submesh++)
            {
                if (result.GetTopology(submesh) == MeshTopology.Triangles)
                    outputTriangleCount += (int)result.GetIndexCount(submesh) / 3;
            }

            report = new RealBlendSculptRemoveReport
            {
                inputTriangles = inputTriangleCount,
                outputTriangles = outputTriangleCount,
                insertedVertices = result.vertexCount - source.vertexCount,
                rimVertices = rimVertexCount,
                bridgeTriangles = bridgeTriangles
            };
            return true;
        }

        private static List<TriangleRecord> BuildTriangleRecords(
            Mesh source,
            Matrix4x4 localToWorld,
            out int triangleCount)
        {
            triangleCount = 0;
            Vector3[] positions = source.vertices;
            List<TriangleRecord> records = new List<TriangleRecord>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;

                int[] indices = source.GetIndices(submesh, true);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];
                    if (a < 0 || b < 0 || c < 0 ||
                        a >= positions.Length || b >= positions.Length || c >= positions.Length)
                    {
                        continue;
                    }

                    Vector3 worldA = localToWorld.MultiplyPoint3x4(positions[a]);
                    Vector3 worldB = localToWorld.MultiplyPoint3x4(positions[b]);
                    Vector3 worldC = localToWorld.MultiplyPoint3x4(positions[c]);
                    Vector3 normal = Vector3.Cross(worldB - worldA, worldC - worldA);
                    if (normal.sqrMagnitude > GeometryEpsilon * GeometryEpsilon)
                        normal.Normalize();
                    else
                        normal = Vector3.zero;
                    records.Add(new TriangleRecord
                    {
                        submesh = submesh,
                        a = a,
                        b = b,
                        c = c,
                        worldA = worldA,
                        worldB = worldB,
                        worldC = worldC,
                        worldNormal = normal
                    });
                    triangleCount++;
                }
            }
            return records;
        }

        private static bool TryBuildFrame(
            RealBlendSculptRemoveSample sample,
            int circleSegments,
            out BrushFrame frame)
        {
            frame = null;
            Vector3 normal = sample.worldNormal;
            if (normal.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
                return false;
            normal.Normalize();

            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(normal, Vector3.right);
            if (right.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
                return false;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;

            List<Vector2> polygon = new List<Vector2>();
            if (sample.shape == RealBlendSculptRemoveShape.Circle)
            {
                float radius = Mathf.Max(0.0001f, sample.radius);
                for (int i = 0; i < circleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / circleSegments;
                    polygon.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            else
            {
                float halfWidth = Mathf.Max(0.0001f, sample.width * 0.5f);
                float halfHeight = Mathf.Max(0.0001f, sample.height * 0.5f);
                polygon.Add(new Vector2(-halfWidth, -halfHeight));
                polygon.Add(new Vector2(halfWidth, -halfHeight));
                polygon.Add(new Vector2(halfWidth, halfHeight));
                polygon.Add(new Vector2(-halfWidth, halfHeight));
            }

            frame = new BrushFrame(
                sample.worldCenter,
                normal,
                right,
                up,
                sample.shape,
                polygon);
            return frame.perimeter > GeometryEpsilon;
        }

        private static bool ShouldClipTriangle(
            TriangleRecord triangle,
            BrushFrame frame,
            ThroughContext through,
            float requestedDepthTolerance)
        {
            float facing = Vector3.Dot(triangle.worldNormal, frame.normal);
            triangle.facing = facing;
            if (through == null)
            {
                if (facing <= FacingEpsilon)
                    return false;
                float range = frame.MaxRange;
                float tolerance = requestedDepthTolerance > 0f
                    ? requestedDepthTolerance
                    : Mathf.Max(0.0025f, range * 0.1f);
                float da = Vector3.Dot(triangle.worldA - frame.center, frame.normal);
                float db = Vector3.Dot(triangle.worldB - frame.center, frame.normal);
                float dc = Vector3.Dot(triangle.worldC - frame.center, frame.normal);
                return Mathf.Max(da, Mathf.Max(db, dc)) >= -tolerance &&
                       Mathf.Min(da, Mathf.Min(db, dc)) <= tolerance;
            }

            if (Mathf.Abs(facing) <= FacingEpsilon)
                return false;
            float centroidDepth = Vector3.Dot(
                triangle.Centroid - frame.center,
                frame.normal);
            return facing > 0f
                ? through.IsFrontDepth(centroidDepth)
                : through.IsBackDepth(centroidDepth);
        }

        private static bool TryBuildThroughContext(
            Mesh source,
            List<TriangleRecord> triangles,
            BrushFrame frame,
            out ThroughContext context,
            out string error)
        {
            context = null;
            error = string.Empty;
            SurfaceHit[] frontHits = new SurfaceHit[frame.polygon.Count];
            SurfaceHit[] backHits = new SurfaceHit[frame.polygon.Count];
            float frontMin = float.PositiveInfinity;
            float frontMax = float.NegativeInfinity;
            float backMin = float.PositiveInfinity;
            float backMax = float.NegativeInfinity;

            for (int i = 0; i < frame.polygon.Count; i++)
            {
                // Sampling just inside the rim avoids an exact mesh edge while
                // retaining the depth of the intended boundary on planar walls.
                Vector2 inset = frame.polygon[i] * 0.9995f;
                Vector3 lineOrigin = frame.Unproject(inset, 0f);
                if (!TryFindSurfacePair(
                        triangles,
                        frame.normal,
                        lineOrigin,
                        out SurfaceHit front,
                        out SurfaceHit back))
                {
                    error =
                        "Cut Through Wall needs a closed, thick wall with a front and back surface under the entire brush. " +
                        "Move or shrink the brush so the opening stays inside both wall faces.";
                    return false;
                }

                float thickness = front.depth - back.depth;
                if (thickness <= Mathf.Max(0.0005f, frame.MaxRange * 0.001f))
                {
                    error = "The two wall surfaces are too close together to build a stable connected rim.";
                    return false;
                }

                frontHits[i] = front;
                backHits[i] = back;
                frontMin = Mathf.Min(frontMin, front.depth);
                frontMax = Mathf.Max(frontMax, front.depth);
                backMin = Mathf.Min(backMin, back.depth);
                backMax = Mathf.Max(backMax, back.depth);
            }

            float tolerance = Mathf.Max(
                0.0025f,
                frame.MaxRange * 0.02f,
                (frontMax - frontMin) * 2f + 0.0005f,
                (backMax - backMin) * 2f + 0.0005f);
            context = new ThroughContext
            {
                frame = frame,
                frontHits = frontHits,
                backHits = backHits,
                frontDepth = (frontMin + frontMax) * 0.5f,
                backDepth = (backMin + backMax) * 0.5f,
                depthTolerance = tolerance,
                uvScale = EstimateUVScale(source, triangles, frontHits[0])
            };
            return true;
        }

        private static bool TryFindSurfacePair(
            List<TriangleRecord> triangles,
            Vector3 lineDirection,
            Vector3 lineOrigin,
            out SurfaceHit front,
            out SurfaceHit back)
        {
            front = default;
            back = default;
            bool hasFront = false;
            bool hasBack = false;
            float bestFrontAbs = float.PositiveInfinity;

            List<SurfaceHit> negativeHits = new List<SurfaceHit>();
            for (int i = 0; i < triangles.Count; i++)
            {
                TriangleRecord triangle = triangles[i];
                float facing = Vector3.Dot(triangle.worldNormal, lineDirection);
                if (Mathf.Abs(facing) < 0.05f)
                    continue;

                float depth = Vector3.Dot(
                    triangle.worldA - lineOrigin,
                    triangle.worldNormal) / facing;
                Vector3 point = lineOrigin + lineDirection * depth;
                if (!TryGetBarycentric(
                        point,
                        triangle.worldA,
                        triangle.worldB,
                        triangle.worldC,
                        out Vector3 barycentric))
                {
                    continue;
                }

                SurfaceHit hit = new SurfaceHit
                {
                    triangle = triangle,
                    barycentric = barycentric,
                    worldPosition = point,
                    depth = depth
                };
                if (facing > 0f)
                {
                    float absolute = Mathf.Abs(depth);
                    if (!hasFront || absolute < bestFrontAbs)
                    {
                        hasFront = true;
                        bestFrontAbs = absolute;
                        front = hit;
                    }
                }
                else
                {
                    negativeHits.Add(hit);
                }
            }

            if (!hasFront)
                return false;

            float bestBackDepth = float.NegativeInfinity;
            float minimumGap = 0.0005f;
            for (int i = 0; i < negativeHits.Count; i++)
            {
                SurfaceHit hit = negativeHits[i];
                if (hit.depth >= front.depth - minimumGap || hit.depth <= bestBackDepth)
                    continue;
                bestBackDepth = hit.depth;
                back = hit;
                hasBack = true;
            }
            return hasBack;
        }

        private static float EstimateUVScale(
            Mesh source,
            List<TriangleRecord> triangles,
            SurfaceHit hit)
        {
            if (!source.HasVertexAttribute(VertexAttribute.TexCoord0))
                return 1f;

            List<Vector4> uv = new List<Vector4>();
            source.GetUVs(0, uv);
            TriangleRecord triangle = hit.triangle;
            if (uv.Count != source.vertexCount)
                return 1f;

            float total = 0f;
            int count = 0;
            AddScale(triangle.worldA, triangle.worldB, uv[triangle.a], uv[triangle.b], ref total, ref count);
            AddScale(triangle.worldB, triangle.worldC, uv[triangle.b], uv[triangle.c], ref total, ref count);
            AddScale(triangle.worldC, triangle.worldA, uv[triangle.c], uv[triangle.a], ref total, ref count);
            return count > 0 ? Mathf.Clamp(total / count, 0.0001f, 10000f) : 1f;
        }

        private static void AddScale(
            Vector3 a,
            Vector3 b,
            Vector4 uvA,
            Vector4 uvB,
            ref float total,
            ref int count)
        {
            float worldDistance = Vector3.Distance(a, b);
            if (worldDistance <= GeometryEpsilon)
                return;
            float uvDistance = Vector2.Distance(
                new Vector2(uvA.x, uvA.y),
                new Vector2(uvB.x, uvB.y));
            if (uvDistance <= GeometryEpsilon)
                return;
            total += uvDistance / worldDistance;
            count++;
        }

        private static List<Vector2> ProjectPolygon(
            List<ClipVertex> polygon,
            TriangleRecord triangle,
            BrushFrame frame)
        {
            List<Vector2> projected = new List<Vector2>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
                projected.Add(frame.Project(triangle.WorldPosition(polygon[i].barycentric)));
            return projected;
        }

        private static void SubtractConvexPolygon(
            List<ClipVertex> subject,
            TriangleRecord triangle,
            BrushFrame frame,
            List<List<ClipVertex>> kept)
        {
            List<ClipVertex> remaining = new List<ClipVertex>(subject);
            for (int edgeIndex = 0; edgeIndex < frame.polygon.Count; edgeIndex++)
            {
                Vector2 edgeStart = frame.polygon[edgeIndex];
                Vector2 edgeEnd = frame.polygon[(edgeIndex + 1) % frame.polygon.Count];
                SplitPolygonByEdge(
                    remaining,
                    triangle,
                    frame,
                    edgeStart,
                    edgeEnd,
                    out List<ClipVertex> inside,
                    out List<ClipVertex> outside);
                outside = CleanPolygon(outside);
                if (outside.Count >= 3)
                    kept.Add(outside);
                remaining = CleanPolygon(inside);
                if (remaining.Count < 3)
                    return;
            }
            // Whatever remains is inside every convex brush half-plane and is
            // therefore the part intentionally removed.
        }

        private static void SplitPolygonByEdge(
            List<ClipVertex> input,
            TriangleRecord triangle,
            BrushFrame frame,
            Vector2 edgeStart,
            Vector2 edgeEnd,
            out List<ClipVertex> inside,
            out List<ClipVertex> outside)
        {
            inside = new List<ClipVertex>(input.Count + 2);
            outside = new List<ClipVertex>(input.Count + 2);
            if (input.Count == 0)
                return;

            Vector2 edge = edgeEnd - edgeStart;
            ClipVertex previous = input[input.Count - 1];
            float previousDistance = Cross2D(
                edge,
                frame.Project(triangle.WorldPosition(previous.barycentric)) - edgeStart);
            bool previousInside = previousDistance >= -GeometryEpsilon;

            for (int i = 0; i < input.Count; i++)
            {
                ClipVertex current = input[i];
                float currentDistance = Cross2D(
                    edge,
                    frame.Project(triangle.WorldPosition(current.barycentric)) - edgeStart);
                bool currentInside = currentDistance >= -GeometryEpsilon;

                if (currentInside != previousInside)
                {
                    float denominator = previousDistance - currentDistance;
                    float t = Mathf.Abs(denominator) > GeometryEpsilon
                        ? Mathf.Clamp01(previousDistance / denominator)
                        : 0.5f;
                    ClipVertex intersection = ClipVertex.Lerp(previous, current, t);
                    inside.Add(intersection);
                    outside.Add(intersection);
                }

                if (currentInside)
                    inside.Add(current);
                else
                    outside.Add(current);

                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
        }

        private static int GetOrAppendClipVertex(
            MeshStreams streams,
            TriangleRecord triangle,
            ClipVertex vertex,
            Dictionary<BarycentricKey, int> cache,
            Dictionary<EdgeInterpolationKey, int> sharedEdgeCache)
        {
            Vector3 barycentric = NormalizeBarycentric(vertex.barycentric);
            if (barycentric.x >= 1f - 0.000001f)
                return triangle.a;
            if (barycentric.y >= 1f - 0.000001f)
                return triangle.b;
            if (barycentric.z >= 1f - 0.000001f)
                return triangle.c;

            BarycentricKey key = new BarycentricKey(barycentric);
            if (cache.TryGetValue(key, out int index))
                return index;
            if (TryGetEdgeInterpolationKey(
                    triangle,
                    barycentric,
                    out EdgeInterpolationKey edgeKey) &&
                sharedEdgeCache.TryGetValue(edgeKey, out index))
            {
                cache.Add(key, index);
                return index;
            }
            index = streams.AppendInterpolated(
                triangle.a,
                triangle.b,
                triangle.c,
                barycentric);
            cache.Add(key, index);
            if (TryGetEdgeInterpolationKey(triangle, barycentric, out edgeKey))
                sharedEdgeCache[edgeKey] = index;
            return index;
        }

        private static bool TryGetEdgeInterpolationKey(
            TriangleRecord triangle,
            Vector3 barycentric,
            out EdgeInterpolationKey key)
        {
            const float edgeTolerance = 0.000002f;
            if (barycentric.x <= edgeTolerance)
            {
                key = new EdgeInterpolationKey(
                    triangle.b,
                    triangle.c,
                    barycentric.y,
                    barycentric.z);
                return true;
            }
            if (barycentric.y <= edgeTolerance)
            {
                key = new EdgeInterpolationKey(
                    triangle.a,
                    triangle.c,
                    barycentric.x,
                    barycentric.z);
                return true;
            }
            if (barycentric.z <= edgeTolerance)
            {
                key = new EdgeInterpolationKey(
                    triangle.a,
                    triangle.b,
                    barycentric.x,
                    barycentric.y);
                return true;
            }
            key = default;
            return false;
        }

        private static bool TryBuildBridge(
            MeshStreams streams,
            List<int>[] outputIndices,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal,
            ThroughContext through,
            List<RimPoint> rawFront,
            List<RimPoint> rawBack,
            List<RimSegment> frontSegments,
            List<RimSegment> backSegments,
            out int bridgeTriangleCount,
            out int rimVertexCount,
            out string error)
        {
            bridgeTriangleCount = 0;
            rimVertexCount = 0;
            error = string.Empty;
            List<RimPoint> front = NormalizeRim(
                rawFront,
                through.frame.perimeter,
                through.frame.MaxRange);
            List<RimPoint> back = NormalizeRim(
                rawBack,
                through.frame.perimeter,
                through.frame.MaxRange);
            if (front.Count < through.frame.polygon.Count ||
                back.Count < through.frame.polygon.Count)
            {
                error =
                    "RealBlend found the two wall faces, but could not form two complete opening rims. " +
                    "Keep the brush away from an existing hole, bevel, or wall edge.";
                return false;
            }

            if (!RimCoversPolygonCorners(front, through.frame) ||
                !RimCoversPolygonCorners(back, through.frame))
            {
                error =
                    "The opening rim was incomplete on one side of the wall. No geometry was changed.";
                return false;
            }

            if (!RimSegmentsCoverPerimeter(frontSegments, through.frame) ||
                !RimSegmentsCoverPerimeter(backSegments, through.frame))
            {
                error =
                    "The opening boundary contains a gap or an existing hole on one wall face. " +
                    "No geometry was changed; move or shrink the brush onto continuous wall surfaces.";
                return false;
            }

            Vector4 baseUV = streams.HasUV(0)
                ? streams.GetUV(0, front[0].surfaceVertex)
                : Vector4.zero;
            BridgeVertex[] frontBridge = BuildBridgeVertices(
                streams,
                front,
                through,
                worldToLocal,
                baseUV,
                true);
            BridgeVertex[] backBridge = BuildBridgeVertices(
                streams,
                back,
                through,
                worldToLocal,
                baseUV,
                false);

            int frontIndex = 0;
            int backIndex = 0;
            float parameterTolerance = Mathf.Max(
                0.00001f,
                through.frame.perimeter * 0.00001f);
            while (frontIndex < front.Count || backIndex < back.Count)
            {
                BridgeVertex frontCurrent = frontBridge[frontIndex];
                BridgeVertex backCurrent = backBridge[backIndex];
                bool hasFrontEdge = frontIndex < front.Count;
                bool hasBackEdge = backIndex < back.Count;
                BridgeVertex frontNext = hasFrontEdge
                    ? frontBridge[frontIndex + 1]
                    : frontCurrent;
                BridgeVertex backNext = hasBackEdge
                    ? backBridge[backIndex + 1]
                    : backCurrent;
                float nextFrontParameter = hasFrontEdge
                    ? frontNext.parameter
                    : float.PositiveInfinity;
                float nextBackParameter = hasBackEdge
                    ? backNext.parameter
                    : float.PositiveInfinity;
                int submesh = Mathf.Clamp(
                    frontCurrent.submesh,
                    0,
                    outputIndices.Length - 1);

                if (hasFrontEdge &&
                    hasBackEdge &&
                    Mathf.Abs(nextFrontParameter - nextBackParameter) <= parameterTolerance)
                {
                    AddOrientedBridgeTriangle(
                        streams,
                        outputIndices[submesh],
                        localToWorld,
                        frontCurrent.vertex,
                        frontNext.vertex,
                        backCurrent.vertex,
                        through.frame.InwardNormal((frontCurrent.parameter + nextFrontParameter) * 0.5f));
                    AddOrientedBridgeTriangle(
                        streams,
                        outputIndices[submesh],
                        localToWorld,
                        frontNext.vertex,
                        backNext.vertex,
                        backCurrent.vertex,
                        through.frame.InwardNormal((frontCurrent.parameter + nextFrontParameter) * 0.5f));
                    bridgeTriangleCount += 2;
                    frontIndex++;
                    backIndex++;
                }
                else if (nextFrontParameter < nextBackParameter)
                {
                    AddOrientedBridgeTriangle(
                        streams,
                        outputIndices[submesh],
                        localToWorld,
                        frontCurrent.vertex,
                        frontNext.vertex,
                        backCurrent.vertex,
                        through.frame.InwardNormal((frontCurrent.parameter + nextFrontParameter) * 0.5f));
                    bridgeTriangleCount++;
                    frontIndex++;
                }
                else
                {
                    AddOrientedBridgeTriangle(
                        streams,
                        outputIndices[submesh],
                        localToWorld,
                        frontCurrent.vertex,
                        backNext.vertex,
                        backCurrent.vertex,
                        through.frame.InwardNormal((backCurrent.parameter + nextBackParameter) * 0.5f));
                    bridgeTriangleCount++;
                    backIndex++;
                }
            }

            rimVertexCount = front.Count + back.Count;
            return bridgeTriangleCount > 0;
        }

        private static BridgeVertex[] BuildBridgeVertices(
            MeshStreams streams,
            List<RimPoint> rim,
            ThroughContext through,
            Matrix4x4 worldToLocal,
            Vector4 baseUV,
            bool front)
        {
            BridgeVertex[] result = new BridgeVertex[rim.Count + 1];
            for (int i = 0; i <= rim.Count; i++)
            {
                RimPoint point = rim[i % rim.Count];
                float parameter = i == rim.Count
                    ? through.frame.perimeter
                    : point.parameter;
                Vector3 worldNormal = through.frame.InwardNormal(parameter);
                Vector3 localNormal = TransformWorldNormalToLocal(
                    worldToLocal,
                    worldNormal);
                Vector3 worldTangent = through.frame.Tangent(parameter);
                Vector3 localTangent = worldToLocal.MultiplyVector(worldTangent).normalized;
                float depth = Vector3.Dot(
                    point.worldPosition - through.frame.center,
                    through.frame.normal);
                float vMeters = front
                    ? 0f
                    : Mathf.Max(0f, through.frontDepth - depth);
                Vector4 uv = baseUV;
                uv.x = baseUV.x + parameter * through.uvScale;
                uv.y = baseUV.y + vMeters * through.uvScale;
                int vertex = streams.AppendCopy(
                    point.surfaceVertex,
                    worldToLocal.MultiplyPoint3x4(point.worldPosition),
                    localNormal,
                    // UV V runs from the front rim toward the back rim, which
                    // is opposite the brush normal. With U following the rim
                    // tangent, the tunnel basis therefore needs w = -1.
                    new Vector4(localTangent.x, localTangent.y, localTangent.z, -1f),
                    uv);
                result[i] = new BridgeVertex
                {
                    parameter = parameter,
                    vertex = vertex,
                    submesh = point.submesh
                };
            }
            return result;
        }

        private static Vector3 TransformWorldNormalToLocal(
            Matrix4x4 worldToLocal,
            Vector3 worldNormal)
        {
            // worldNormal = transpose(worldToLocal) * localNormal
            // therefore localNormal = transpose(localToWorld) * worldNormal.
            Matrix4x4 localToWorld = worldToLocal.inverse;
            Vector3 local = localToWorld.transpose.MultiplyVector(worldNormal);
            return local.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                ? local.normalized
                : Vector3.up;
        }

        private static void AddOrientedBridgeTriangle(
            MeshStreams streams,
            List<int> indices,
            Matrix4x4 localToWorld,
            int a,
            int b,
            int c,
            Vector3 desiredWorldNormal)
        {
            Vector3 worldA = localToWorld.MultiplyPoint3x4(streams.positions[a]);
            Vector3 worldB = localToWorld.MultiplyPoint3x4(streams.positions[b]);
            Vector3 worldC = localToWorld.MultiplyPoint3x4(streams.positions[c]);
            Vector3 geometric = Vector3.Cross(worldB - worldA, worldC - worldA);
            if (geometric.sqrMagnitude <= GeometryEpsilon * GeometryEpsilon)
                return;
            if (Vector3.Dot(geometric, desiredWorldNormal) < 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private static List<RimPoint> NormalizeRim(
            List<RimPoint> raw,
            float perimeter,
            float brushRange)
        {
            raw.Sort((left, right) => left.parameter.CompareTo(right.parameter));
            float parameterTolerance = Mathf.Max(0.00001f, perimeter * 0.00001f);
            float positionTolerance = Mathf.Max(0.00001f, brushRange * 0.00001f);
            float positionToleranceSqr = positionTolerance * positionTolerance;
            List<RimPoint> normalized = new List<RimPoint>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                RimPoint point = raw[i];
                point.parameter = Mathf.Repeat(point.parameter, perimeter);
                bool duplicate = false;
                for (int candidate = normalized.Count - 1;
                     candidate >= 0 &&
                     point.parameter - normalized[candidate].parameter <= parameterTolerance;
                     candidate--)
                {
                    if ((normalized[candidate].worldPosition - point.worldPosition).sqrMagnitude <= positionToleranceSqr)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    normalized.Add(point);
            }
            normalized.Sort((left, right) => left.parameter.CompareTo(right.parameter));
            return normalized;
        }

        private static bool RimCoversPolygonCorners(
            List<RimPoint> rim,
            BrushFrame frame)
        {
            float tolerance = Mathf.Max(0.0001f, frame.perimeter * 0.0001f);
            for (int corner = 0; corner < frame.cumulative.Count - 1; corner++)
            {
                float target = frame.cumulative[corner];
                bool found = false;
                for (int i = 0; i < rim.Count; i++)
                {
                    float delta = Mathf.Abs(rim[i].parameter - target);
                    delta = Mathf.Min(delta, frame.perimeter - delta);
                    if (delta <= tolerance)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            return true;
        }

        private static bool RimSegmentsCoverPerimeter(
            List<RimSegment> segments,
            BrushFrame frame)
        {
            if (segments == null || segments.Count == 0)
                return false;
            float perimeter = frame.perimeter;
            float tolerance = Mathf.Max(0.00001f, perimeter * 0.00002f);
            List<Vector2> intervals = new List<Vector2>(segments.Count + 2);
            for (int i = 0; i < segments.Count; i++)
            {
                float first = Mathf.Repeat(segments[i].first, perimeter);
                float second = Mathf.Repeat(segments[i].second, perimeter);
                float minimum = Mathf.Min(first, second);
                float maximum = Mathf.Max(first, second);
                if (maximum - minimum <= tolerance)
                    continue;
                if (maximum - minimum > perimeter * 0.5f)
                {
                    intervals.Add(new Vector2(0f, minimum));
                    intervals.Add(new Vector2(maximum, perimeter));
                }
                else
                {
                    intervals.Add(new Vector2(minimum, maximum));
                }
            }
            if (intervals.Count == 0)
                return false;
            intervals.Sort((left, right) => left.x.CompareTo(right.x));
            float covered = 0f;
            for (int i = 0; i < intervals.Count; i++)
            {
                Vector2 interval = intervals[i];
                if (interval.x > covered + tolerance)
                    return false;
                covered = Mathf.Max(covered, interval.y);
            }
            return covered >= perimeter - tolerance;
        }

        private static bool ConvexPolygonsIntersect(
            List<Vector2> first,
            List<Vector2> second)
        {
            if (first.Count < 3 || second.Count < 3)
                return false;
            for (int i = 0; i < first.Count; i++)
            {
                if (PointInConvexPolygon(first[i], second))
                    return true;
            }
            for (int i = 0; i < second.Count; i++)
            {
                if (PointInConvexPolygon(second[i], first))
                    return true;
            }
            for (int a = 0; a < first.Count; a++)
            {
                Vector2 a0 = first[a];
                Vector2 a1 = first[(a + 1) % first.Count];
                for (int b = 0; b < second.Count; b++)
                {
                    if (SegmentsIntersect(
                            a0,
                            a1,
                            second[b],
                            second[(b + 1) % second.Count]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool PointInConvexPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool hasNegative = false;
            bool hasPositive = false;
            for (int i = 0; i < polygon.Count; i++)
            {
                float cross = Cross2D(
                    polygon[(i + 1) % polygon.Count] - polygon[i],
                    point - polygon[i]);
                hasNegative |= cross < -GeometryEpsilon;
                hasPositive |= cross > GeometryEpsilon;
                if (hasNegative && hasPositive)
                    return false;
            }
            return true;
        }

        private static bool SegmentsIntersect(
            Vector2 a0,
            Vector2 a1,
            Vector2 b0,
            Vector2 b1)
        {
            Vector2 a = a1 - a0;
            Vector2 b = b1 - b0;
            float denominator = Cross2D(a, b);
            if (Mathf.Abs(denominator) <= GeometryEpsilon)
                return DistancePointToSegmentSqr(a0, b0, b1) <= GeometryEpsilon * GeometryEpsilon ||
                       DistancePointToSegmentSqr(a1, b0, b1) <= GeometryEpsilon * GeometryEpsilon ||
                       DistancePointToSegmentSqr(b0, a0, a1) <= GeometryEpsilon * GeometryEpsilon ||
                       DistancePointToSegmentSqr(b1, a0, a1) <= GeometryEpsilon * GeometryEpsilon;
            Vector2 delta = b0 - a0;
            float t = Cross2D(delta, b) / denominator;
            float u = Cross2D(delta, a) / denominator;
            return t >= -GeometryEpsilon && t <= 1f + GeometryEpsilon &&
                   u >= -GeometryEpsilon && u <= 1f + GeometryEpsilon;
        }

        private static float DistancePointToSegmentSqr(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 edge = end - start;
            float lengthSqr = edge.sqrMagnitude;
            if (lengthSqr <= GeometryEpsilon * GeometryEpsilon)
                return (point - start).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / lengthSqr);
            return (point - (start + edge * t)).sqrMagnitude;
        }

        private static bool TryGetBarycentric(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out Vector3 barycentric)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = point - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) <= GeometryEpsilon * GeometryEpsilon)
            {
                barycentric = default;
                return false;
            }
            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            float u = 1f - v - w;
            const float tolerance = 0.0002f;
            if (u < -tolerance || v < -tolerance || w < -tolerance ||
                u > 1f + tolerance || v > 1f + tolerance || w > 1f + tolerance)
            {
                barycentric = default;
                return false;
            }
            barycentric = NormalizeBarycentric(new Vector3(u, v, w));
            return true;
        }

        private static Vector3 NormalizeBarycentric(Vector3 barycentric)
        {
            barycentric.x = Mathf.Clamp01(barycentric.x);
            barycentric.y = Mathf.Clamp01(barycentric.y);
            barycentric.z = Mathf.Clamp01(barycentric.z);
            float sum = barycentric.x + barycentric.y + barycentric.z;
            return sum > GeometryEpsilon
                ? barycentric / sum
                : new Vector3(1f, 0f, 0f);
        }

        private static List<ClipVertex> CleanPolygon(List<ClipVertex> polygon)
        {
            if (polygon == null || polygon.Count < 2)
                return polygon ?? new List<ClipVertex>();
            List<ClipVertex> clean = new List<ClipVertex>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex vertex = polygon[i];
                if (clean.Count == 0 ||
                    (clean[clean.Count - 1].barycentric - vertex.barycentric).sqrMagnitude > 0.0000000001f)
                {
                    clean.Add(vertex);
                }
            }
            if (clean.Count > 1 &&
                (clean[0].barycentric - clean[clean.Count - 1].barycentric).sqrMagnitude <= 0.0000000001f)
            {
                clean.RemoveAt(clean.Count - 1);
            }
            return clean;
        }

        private static List<List<ClipVertex>> MakePieceBoundariesConforming(
            List<List<ClipVertex>> pieces)
        {
            List<ClipVertex> candidates = new List<ClipVertex>();
            HashSet<BarycentricKey> seen = new HashSet<BarycentricKey>();
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                List<ClipVertex> clean = CleanPolygon(pieces[pieceIndex]);
                pieces[pieceIndex] = clean;
                for (int vertexIndex = 0; vertexIndex < clean.Count; vertexIndex++)
                {
                    ClipVertex candidate = clean[vertexIndex];
                    if (seen.Add(new BarycentricKey(candidate.barycentric)))
                        candidates.Add(candidate);
                }
            }

            List<List<ClipVertex>> conforming = new List<List<ClipVertex>>(pieces.Count);
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                List<ClipVertex> piece = pieces[pieceIndex];
                if (piece.Count < 3)
                    continue;

                List<ClipVertex> refined = new List<ClipVertex>(piece.Count + 4);
                for (int edgeIndex = 0; edgeIndex < piece.Count; edgeIndex++)
                {
                    ClipVertex start = piece[edgeIndex];
                    ClipVertex end = piece[(edgeIndex + 1) % piece.Count];
                    refined.Add(start);

                    Vector3 edge = end.barycentric - start.barycentric;
                    float edgeLengthSqr = edge.sqrMagnitude;
                    if (edgeLengthSqr <= GeometryEpsilon * GeometryEpsilon)
                        continue;

                    List<KeyValuePair<float, ClipVertex>> onEdge =
                        new List<KeyValuePair<float, ClipVertex>>();
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        ClipVertex candidate = candidates[candidateIndex];
                        float t = Vector3.Dot(
                            candidate.barycentric - start.barycentric,
                            edge) / edgeLengthSqr;
                        if (t <= 0.000001f || t >= 0.999999f)
                            continue;
                        Vector3 nearest = start.barycentric + edge * t;
                        if ((candidate.barycentric - nearest).sqrMagnitude > 0.0000000001f)
                            continue;
                        onEdge.Add(new KeyValuePair<float, ClipVertex>(t, candidate));
                    }
                    onEdge.Sort((left, right) => left.Key.CompareTo(right.Key));
                    for (int candidateIndex = 0; candidateIndex < onEdge.Count; candidateIndex++)
                    {
                        ClipVertex candidate = onEdge[candidateIndex].Value;
                        if ((refined[refined.Count - 1].barycentric - candidate.barycentric).sqrMagnitude >
                            0.0000000001f)
                        {
                            refined.Add(candidate);
                        }
                    }
                }
                conforming.Add(CleanPolygon(refined));
            }
            return conforming;
        }

        private static void RegisterSourceEdgeBreaks(
            TriangleRecord triangle,
            List<List<ClipVertex>> pieces,
            Dictionary<SourceEdgeKey, HashSet<int>> breaks)
        {
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                List<ClipVertex> piece = pieces[pieceIndex];
                for (int vertexIndex = 0; vertexIndex < piece.Count; vertexIndex++)
                {
                    if (!TryGetSourceEdgeBreak(
                            triangle,
                            NormalizeBarycentric(piece[vertexIndex].barycentric),
                            out SourceEdgeKey edge,
                            out int interpolation) ||
                        interpolation <= 0 ||
                        interpolation >= TopologyPrecision)
                    {
                        continue;
                    }

                    if (!breaks.TryGetValue(edge, out HashSet<int> values))
                    {
                        values = new HashSet<int>();
                        breaks.Add(edge, values);
                    }
                    values.Add(interpolation);
                }
            }
        }

        private static void WritePendingTriangles(
            MeshStreams streams,
            List<int>[] outputIndices,
            List<PendingTriangle> pending,
            Dictionary<EdgeInterpolationKey, int> sharedEdgeVertexCache,
            Dictionary<SourceEdgeKey, HashSet<int>> sourceEdgeBreaks)
        {
            Dictionary<TriangleRecord, HashSet<TriangleKey>> emittedBySource =
                new Dictionary<TriangleRecord, HashSet<TriangleKey>>();
            for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
            {
                PendingTriangle triangle = pending[pendingIndex];
                if (triangle.preserveExactly)
                {
                    List<int> exact = outputIndices[triangle.source.submesh];
                    exact.Add(triangle.vertices[0].index);
                    exact.Add(triangle.vertices[1].index);
                    exact.Add(triangle.vertices[2].index);
                    continue;
                }
                if (!emittedBySource.TryGetValue(
                        triangle.source,
                        out HashSet<TriangleKey> emitted))
                {
                    emitted = new HashSet<TriangleKey>();
                    emittedBySource.Add(triangle.source, emitted);
                }
                List<PendingVertex> boundary = new List<PendingVertex>(6);
                bool refined = false;
                for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
                {
                    PendingVertex start = triangle.vertices[edgeIndex];
                    PendingVertex end = triangle.vertices[(edgeIndex + 1) % 3];
                    boundary.Add(start);
                    if (!TryGetCommonSourceEdge(
                            triangle.source,
                            start.clip.barycentric,
                            end.clip.barycentric,
                            out SourceEdgeKey sourceEdge) ||
                        !sourceEdgeBreaks.TryGetValue(sourceEdge, out HashSet<int> values))
                    {
                        continue;
                    }

                    Vector3 edge = end.clip.barycentric - start.clip.barycentric;
                    float edgeLengthSqr = edge.sqrMagnitude;
                    if (edgeLengthSqr <= GeometryEpsilon * GeometryEpsilon)
                        continue;

                    List<KeyValuePair<float, ClipVertex>> onEdge =
                        new List<KeyValuePair<float, ClipVertex>>();
                    foreach (int interpolation in values)
                    {
                        ClipVertex candidate = SourceEdgeClipVertex(
                            triangle.source,
                            sourceEdge,
                            interpolation);
                        float t = Vector3.Dot(
                            candidate.barycentric - start.clip.barycentric,
                            edge) / edgeLengthSqr;
                        if (t <= 0.000001f || t >= 0.999999f)
                            continue;
                        Vector3 nearest = start.clip.barycentric + edge * t;
                        if ((candidate.barycentric - nearest).sqrMagnitude > 0.0000000001f)
                            continue;
                        onEdge.Add(new KeyValuePair<float, ClipVertex>(t, candidate));
                    }
                    onEdge.Sort((left, right) => left.Key.CompareTo(right.Key));
                    Dictionary<BarycentricKey, int> cache =
                        new Dictionary<BarycentricKey, int>();
                    for (int candidateIndex = 0; candidateIndex < onEdge.Count; candidateIndex++)
                    {
                        ClipVertex candidate = onEdge[candidateIndex].Value;
                        if ((boundary[boundary.Count - 1].clip.barycentric - candidate.barycentric).sqrMagnitude <=
                            0.0000000001f)
                        {
                            continue;
                        }
                        int index = GetOrAppendClipVertex(
                            streams,
                            triangle.source,
                            candidate,
                            cache,
                            sharedEdgeVertexCache);
                        boundary.Add(new PendingVertex(candidate, index));
                        refined = true;
                    }
                }

                List<int> indices = outputIndices[triangle.source.submesh];
                if (!refined)
                {
                    AddOrientedPendingTriangle(
                        indices,
                        triangle.source,
                        triangle.vertices[0],
                        triangle.vertices[1],
                        triangle.vertices[2],
                        emitted);
                    continue;
                }

                Vector3 centerBarycentric = Vector3.zero;
                for (int vertexIndex = 0; vertexIndex < boundary.Count; vertexIndex++)
                    centerBarycentric += boundary[vertexIndex].clip.barycentric;
                centerBarycentric /= boundary.Count;
                int center = streams.AppendInterpolated(
                    triangle.source.a,
                    triangle.source.b,
                    triangle.source.c,
                    NormalizeBarycentric(centerBarycentric));
                for (int vertexIndex = 0; vertexIndex < boundary.Count; vertexIndex++)
                {
                    int first = boundary[vertexIndex].index;
                    int second = boundary[(vertexIndex + 1) % boundary.Count].index;
                    if (IsDegenerate(
                            streams.positions[center],
                            streams.positions[first],
                            streams.positions[second]))
                    {
                        continue;
                    }
                    AddOrientedPendingTriangle(
                        indices,
                        triangle.source,
                        new PendingVertex(new ClipVertex(centerBarycentric), center),
                        boundary[vertexIndex],
                        boundary[(vertexIndex + 1) % boundary.Count],
                        emitted);
                }
            }
        }

        private static void AddOrientedPendingTriangle(
            List<int> indices,
            TriangleRecord source,
            PendingVertex a,
            PendingVertex b,
            PendingVertex c,
            HashSet<TriangleKey> emitted)
        {
            Vector3 worldA = source.WorldPosition(
                NormalizeBarycentric(a.clip.barycentric));
            Vector3 worldB = source.WorldPosition(
                NormalizeBarycentric(b.clip.barycentric));
            Vector3 worldC = source.WorldPosition(
                NormalizeBarycentric(c.clip.barycentric));
            if (Vector3.Dot(
                    Vector3.Cross(worldB - worldA, worldC - worldA),
                    source.worldNormal) < 0f)
            {
                PendingVertex swap = b;
                b = c;
                c = swap;
            }
            if (a.index == b.index || b.index == c.index || c.index == a.index ||
                !emitted.Add(new TriangleKey(a.index, b.index, c.index)))
            {
                return;
            }
            indices.Add(a.index);
            indices.Add(b.index);
            indices.Add(c.index);
        }

        private static bool TryGetCommonSourceEdge(
            TriangleRecord triangle,
            Vector3 first,
            Vector3 second,
            out SourceEdgeKey edge)
        {
            const float tolerance = 0.000002f;
            if (first.x <= tolerance && second.x <= tolerance)
            {
                edge = new SourceEdgeKey(triangle.b, triangle.c);
                return true;
            }
            if (first.y <= tolerance && second.y <= tolerance)
            {
                edge = new SourceEdgeKey(triangle.a, triangle.c);
                return true;
            }
            if (first.z <= tolerance && second.z <= tolerance)
            {
                edge = new SourceEdgeKey(triangle.a, triangle.b);
                return true;
            }
            edge = default;
            return false;
        }

        private static bool TryGetSourceEdgeBreak(
            TriangleRecord triangle,
            Vector3 barycentric,
            out SourceEdgeKey edge,
            out int interpolation)
        {
            const float tolerance = 0.000002f;
            if (barycentric.x <= tolerance)
                return BuildSourceEdgeBreak(
                    triangle.b,
                    triangle.c,
                    barycentric.y,
                    barycentric.z,
                    out edge,
                    out interpolation);
            if (barycentric.y <= tolerance)
                return BuildSourceEdgeBreak(
                    triangle.a,
                    triangle.c,
                    barycentric.x,
                    barycentric.z,
                    out edge,
                    out interpolation);
            if (barycentric.z <= tolerance)
                return BuildSourceEdgeBreak(
                    triangle.a,
                    triangle.b,
                    barycentric.x,
                    barycentric.y,
                    out edge,
                    out interpolation);
            edge = default;
            interpolation = 0;
            return false;
        }

        private static bool BuildSourceEdgeBreak(
            int firstVertex,
            int secondVertex,
            float firstWeight,
            float secondWeight,
            out SourceEdgeKey edge,
            out int interpolation)
        {
            float total = firstWeight + secondWeight;
            float weightOfSecond = total > GeometryEpsilon
                ? secondWeight / total
                : 0f;
            edge = new SourceEdgeKey(firstVertex, secondVertex);
            interpolation = Mathf.RoundToInt(
                (firstVertex <= secondVertex ? weightOfSecond : 1f - weightOfSecond) *
                TopologyPrecision);
            return true;
        }

        private static ClipVertex SourceEdgeClipVertex(
            TriangleRecord triangle,
            SourceEdgeKey edge,
            int interpolation)
        {
            float secondWeight = interpolation / (float)TopologyPrecision;
            int first = edge.first;
            int second = edge.second;
            if (edge.Equals(new SourceEdgeKey(triangle.b, triangle.c)))
            {
                return triangle.b == first
                    ? new ClipVertex(new Vector3(0f, 1f - secondWeight, secondWeight))
                    : new ClipVertex(new Vector3(0f, secondWeight, 1f - secondWeight));
            }
            if (edge.Equals(new SourceEdgeKey(triangle.a, triangle.c)))
            {
                return triangle.a == first
                    ? new ClipVertex(new Vector3(1f - secondWeight, 0f, secondWeight))
                    : new ClipVertex(new Vector3(secondWeight, 0f, 1f - secondWeight));
            }
            return triangle.a == first
                ? new ClipVertex(new Vector3(1f - secondWeight, secondWeight, 0f))
                : new ClipVertex(new Vector3(secondWeight, 1f - secondWeight, 0f));
        }

        private static bool IsDegenerate(Vector3 a, Vector3 b, Vector3 c)
        {
            // Clipping can legitimately create very small sliver triangles at
            // a brush/grid intersection. Dropping those slivers opens a crack;
            // reject only an effectively exact zero-area triangle instead.
            return Vector3.Cross(b - a, c - a).sqrMagnitude <= 0.00000000000000000001f;
        }

        private static float Cross2D(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private sealed class BrushFrame
        {
            public readonly Vector3 center;
            public readonly Vector3 normal;
            public readonly Vector3 right;
            public readonly Vector3 up;
            public readonly RealBlendSculptRemoveShape shape;
            public readonly List<Vector2> polygon;
            public readonly List<float> cumulative;
            public readonly float perimeter;

            public BrushFrame(
                Vector3 center,
                Vector3 normal,
                Vector3 right,
                Vector3 up,
                RealBlendSculptRemoveShape shape,
                List<Vector2> polygon)
            {
                this.center = center;
                this.normal = normal;
                this.right = right;
                this.up = up;
                this.shape = shape;
                this.polygon = polygon;
                cumulative = new List<float>(polygon.Count + 1) { 0f };
                float total = 0f;
                for (int i = 0; i < polygon.Count; i++)
                {
                    total += Vector2.Distance(
                        polygon[i],
                        polygon[(i + 1) % polygon.Count]);
                    cumulative.Add(total);
                }
                perimeter = total;
            }

            public float MaxRange
            {
                get
                {
                    float max = 0f;
                    for (int i = 0; i < polygon.Count; i++)
                        max = Mathf.Max(max, polygon[i].magnitude);
                    return max;
                }
            }

            public Vector2 Project(Vector3 world)
            {
                Vector3 delta = world - center;
                return new Vector2(
                    Vector3.Dot(delta, right),
                    Vector3.Dot(delta, up));
            }

            public Vector3 Unproject(Vector2 plane, float depth)
            {
                return center + right * plane.x + up * plane.y + normal * depth;
            }

            public bool TryGetBoundaryParameter(
                Vector2 point,
                out float parameter)
            {
                parameter = 0f;
                float tolerance = Mathf.Max(0.00001f, MaxRange * 0.00002f);
                float toleranceSqr = tolerance * tolerance;
                float bestDistance = float.PositiveInfinity;
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 start = polygon[i];
                    Vector2 end = polygon[(i + 1) % polygon.Count];
                    Vector2 edge = end - start;
                    float edgeSqr = edge.sqrMagnitude;
                    float t = edgeSqr > GeometryEpsilon * GeometryEpsilon
                        ? Mathf.Clamp01(Vector2.Dot(point - start, edge) / edgeSqr)
                        : 0f;
                    Vector2 nearest = start + edge * t;
                    float distance = (nearest - point).sqrMagnitude;
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    parameter = cumulative[i] + Mathf.Sqrt(edgeSqr) * t;
                }
                if (bestDistance > toleranceSqr)
                    return false;
                if (parameter >= perimeter - tolerance)
                    parameter = 0f;
                return true;
            }

            public Vector3 InwardNormal(float parameter)
            {
                Vector2 point = PointAt(parameter, out int segment, out _);
                Vector2 inward;
                if (shape == RealBlendSculptRemoveShape.Circle &&
                    point.sqrMagnitude > GeometryEpsilon * GeometryEpsilon)
                {
                    inward = -point.normalized;
                }
                else
                {
                    Vector2 edge = (
                        polygon[(segment + 1) % polygon.Count] -
                        polygon[segment]).normalized;
                    inward = new Vector2(-edge.y, edge.x);
                }
                return (right * inward.x + up * inward.y).normalized;
            }

            public Vector3 Tangent(float parameter)
            {
                PointAt(parameter, out int segment, out _);
                Vector2 tangent2D = (
                    polygon[(segment + 1) % polygon.Count] -
                    polygon[segment]).normalized;
                return (right * tangent2D.x + up * tangent2D.y).normalized;
            }

            private Vector2 PointAt(
                float parameter,
                out int segment,
                out float segmentT)
            {
                float wrapped = Mathf.Repeat(parameter, perimeter);
                segment = polygon.Count - 1;
                for (int i = 0; i < polygon.Count; i++)
                {
                    if (wrapped <= cumulative[i + 1] + GeometryEpsilon)
                    {
                        segment = i;
                        break;
                    }
                }
                float length = cumulative[segment + 1] - cumulative[segment];
                segmentT = length > GeometryEpsilon
                    ? Mathf.Clamp01((wrapped - cumulative[segment]) / length)
                    : 0f;
                return Vector2.Lerp(
                    polygon[segment],
                    polygon[(segment + 1) % polygon.Count],
                    segmentT);
            }
        }

        private sealed class ThroughContext
        {
            public BrushFrame frame;
            public SurfaceHit[] frontHits;
            public SurfaceHit[] backHits;
            public float frontDepth;
            public float backDepth;
            public float depthTolerance;
            public float uvScale;

            public bool IsFrontDepth(float depth)
            {
                return Mathf.Abs(depth - frontDepth) <= depthTolerance;
            }

            public bool IsBackDepth(float depth)
            {
                return Mathf.Abs(depth - backDepth) <= depthTolerance;
            }
        }

        private sealed class TriangleRecord
        {
            public int submesh;
            public int a;
            public int b;
            public int c;
            public Vector3 worldA;
            public Vector3 worldB;
            public Vector3 worldC;
            public Vector3 worldNormal;
            public float facing;

            public Vector3 Centroid => (worldA + worldB + worldC) / 3f;

            public Vector3 WorldPosition(Vector3 barycentric)
            {
                return worldA * barycentric.x +
                       worldB * barycentric.y +
                       worldC * barycentric.z;
            }
        }

        private struct SurfaceHit
        {
            public TriangleRecord triangle;
            public Vector3 barycentric;
            public Vector3 worldPosition;
            public float depth;
        }

        private struct ClipVertex
        {
            public Vector3 barycentric;

            public ClipVertex(Vector3 barycentric)
            {
                this.barycentric = barycentric;
            }

            public static ClipVertex Lerp(ClipVertex a, ClipVertex b, float t)
            {
                return new ClipVertex(Vector3.LerpUnclamped(
                    a.barycentric,
                    b.barycentric,
                    t));
            }
        }

        private struct RimPoint
        {
            public float parameter;
            public Vector3 worldPosition;
            public int surfaceVertex;
            public int submesh;
        }

        private readonly struct RimSegment
        {
            public readonly float first;
            public readonly float second;

            public RimSegment(float first, float second)
            {
                this.first = first;
                this.second = second;
            }
        }

        private struct BridgeVertex
        {
            public float parameter;
            public int vertex;
            public int submesh;
        }

        private sealed class PendingTriangle
        {
            public readonly TriangleRecord source;
            public readonly PendingVertex[] vertices;
            public readonly bool preserveExactly;

            public PendingTriangle(
                TriangleRecord source,
                ClipVertex a,
                int indexA,
                ClipVertex b,
                int indexB,
                ClipVertex c,
                int indexC,
                bool preserveExactly = false)
            {
                this.source = source;
                this.preserveExactly = preserveExactly;
                vertices = new[]
                {
                    new PendingVertex(a, indexA),
                    new PendingVertex(b, indexB),
                    new PendingVertex(c, indexC)
                };
            }
        }

        private readonly struct PendingVertex
        {
            public readonly ClipVertex clip;
            public readonly int index;

            public PendingVertex(ClipVertex clip, int index)
            {
                this.clip = clip;
                this.index = index;
            }
        }

        private readonly struct SourceEdgeKey : IEquatable<SourceEdgeKey>
        {
            public readonly int first;
            public readonly int second;

            public SourceEdgeKey(int firstVertex, int secondVertex)
            {
                first = Mathf.Min(firstVertex, secondVertex);
                second = Mathf.Max(firstVertex, secondVertex);
            }

            public bool Equals(SourceEdgeKey other)
            {
                return first == other.first && second == other.second;
            }

            public override bool Equals(object obj)
            {
                return obj is SourceEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked { return first * 397 ^ second; }
            }
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int first;
            private readonly int second;
            private readonly int third;

            public TriangleKey(int a, int b, int c)
            {
                first = Mathf.Min(a, Mathf.Min(b, c));
                third = Mathf.Max(a, Mathf.Max(b, c));
                second = a + b + c - first - third;
            }

            public bool Equals(TriangleKey other)
            {
                return first == other.first &&
                       second == other.second &&
                       third == other.third;
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = first;
                    hash = hash * 397 ^ second;
                    hash = hash * 397 ^ third;
                    return hash;
                }
            }
        }

        private readonly struct BarycentricKey : IEquatable<BarycentricKey>
        {
            private readonly int x;
            private readonly int y;
            private readonly int z;

            public BarycentricKey(Vector3 barycentric)
            {
                x = Mathf.RoundToInt(barycentric.x * TopologyPrecision);
                y = Mathf.RoundToInt(barycentric.y * TopologyPrecision);
                z = Mathf.RoundToInt(barycentric.z * TopologyPrecision);
            }

            public bool Equals(BarycentricKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is BarycentricKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = hash * 397 ^ y;
                    hash = hash * 397 ^ z;
                    return hash;
                }
            }
        }

        private readonly struct EdgeInterpolationKey : IEquatable<EdgeInterpolationKey>
        {
            private readonly int first;
            private readonly int second;
            private readonly int interpolation;

            public EdgeInterpolationKey(
                int firstVertex,
                int secondVertex,
                float firstWeight,
                float secondWeight)
            {
                float total = firstWeight + secondWeight;
                float weightOfSecond = total > GeometryEpsilon
                    ? secondWeight / total
                    : 0f;
                if (firstVertex <= secondVertex)
                {
                    first = firstVertex;
                    second = secondVertex;
                    interpolation = Mathf.RoundToInt(weightOfSecond * TopologyPrecision);
                }
                else
                {
                    first = secondVertex;
                    second = firstVertex;
                    interpolation = Mathf.RoundToInt((1f - weightOfSecond) * TopologyPrecision);
                }
            }

            public bool Equals(EdgeInterpolationKey other)
            {
                return first == other.first &&
                       second == other.second &&
                       interpolation == other.interpolation;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeInterpolationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = first;
                    hash = hash * 397 ^ second;
                    hash = hash * 397 ^ interpolation;
                    return hash;
                }
            }
        }

        private sealed class MeshStreams
        {
            public readonly List<Vector3> positions;
            private readonly List<Vector3> normals;
            private readonly List<Vector4> tangents;
            private readonly List<Color> colors;
            private readonly bool colorsWereUNorm8;
            private readonly UVStream[] uvs = new UVStream[8];

            public MeshStreams(Mesh source)
            {
                positions = new List<Vector3>(source.vertices);

                Vector3[] sourceNormals = source.normals;
                if (sourceNormals != null && sourceNormals.Length == source.vertexCount)
                    normals = new List<Vector3>(sourceNormals);

                Vector4[] sourceTangents = source.tangents;
                if (sourceTangents != null && sourceTangents.Length == source.vertexCount)
                    tangents = new List<Vector4>(sourceTangents);

                if (source.HasVertexAttribute(VertexAttribute.Color))
                {
                    colors = new List<Color>();
                    source.GetColors(colors);
                    if (colors.Count != source.vertexCount)
                        colors = null;
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
                        continue;
                    uvs[channel] = new UVStream
                    {
                        dimension = Mathf.Clamp(
                            source.GetVertexAttributeDimension(attribute),
                            2,
                            4),
                        values = values
                    };
                }
            }

            public bool HasUV(int channel)
            {
                return channel >= 0 && channel < uvs.Length && uvs[channel] != null;
            }

            public bool HasNormals => normals != null;

            public Vector4 GetUV(int channel, int vertex)
            {
                return HasUV(channel) &&
                       vertex >= 0 &&
                       vertex < uvs[channel].values.Count
                    ? uvs[channel].values[vertex]
                    : Vector4.zero;
            }

            public int AppendInterpolated(
                int a,
                int b,
                int c,
                Vector3 barycentric)
            {
                int index = positions.Count;
                positions.Add(Interpolate(
                    positions[a],
                    positions[b],
                    positions[c],
                    barycentric));
                if (normals != null)
                {
                    Vector3 value = Interpolate(
                        normals[a],
                        normals[b],
                        normals[c],
                        barycentric);
                    normals.Add(value.sqrMagnitude > GeometryEpsilon * GeometryEpsilon
                        ? value.normalized
                        : Vector3.up);
                }
                if (tangents != null)
                {
                    Vector4 value = Interpolate(
                        tangents[a],
                        tangents[b],
                        tangents[c],
                        barycentric);
                    Vector3 direction = new Vector3(value.x, value.y, value.z);
                    if (direction.sqrMagnitude > GeometryEpsilon * GeometryEpsilon)
                        direction.Normalize();
                    tangents.Add(new Vector4(
                        direction.x,
                        direction.y,
                        direction.z,
                        value.w >= 0f ? 1f : -1f));
                }
                if (colors != null)
                {
                    colors.Add(
                        colors[a] * barycentric.x +
                        colors[b] * barycentric.y +
                        colors[c] * barycentric.z);
                }
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] == null)
                        continue;
                    List<Vector4> values = uvs[channel].values;
                    values.Add(Interpolate(
                        values[a],
                        values[b],
                        values[c],
                        barycentric));
                }
                return index;
            }

            public int AppendCopy(
                int sourceVertex,
                Vector3 position,
                Vector3 normal,
                Vector4 tangent,
                Vector4 uv0)
            {
                int index = positions.Count;
                positions.Add(position);
                if (normals != null)
                    normals.Add(normal);
                if (tangents != null)
                    tangents.Add(tangent);
                if (colors != null)
                    colors.Add(colors[sourceVertex]);
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] == null)
                        continue;
                    uvs[channel].values.Add(channel == 0
                        ? uv0
                        : uvs[channel].values[sourceVertex]);
                }
                return index;
            }

            public void WriteTo(Mesh mesh)
            {
                mesh.SetVertices(positions);
                if (normals != null)
                    mesh.SetNormals(normals);
                if (tangents != null)
                    mesh.SetTangents(tangents);
                if (colors != null)
                {
                    if (colorsWereUNorm8)
                    {
                        List<Color32> color32 = new List<Color32>(colors.Count);
                        for (int i = 0; i < colors.Count; i++)
                            color32.Add(colors[i]);
                        mesh.SetColors(color32);
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
                            values.Add(new Vector3(
                                stream.values[i].x,
                                stream.values[i].y,
                                stream.values[i].z));
                        mesh.SetUVs(channel, values);
                    }
                    else
                    {
                        mesh.SetUVs(channel, stream.values);
                    }
                }
            }

            private static Vector3 Interpolate(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 barycentric)
            {
                return a * barycentric.x +
                       b * barycentric.y +
                       c * barycentric.z;
            }

            private static Vector4 Interpolate(
                Vector4 a,
                Vector4 b,
                Vector4 c,
                Vector3 barycentric)
            {
                return a * barycentric.x +
                       b * barycentric.y +
                       c * barycentric.z;
            }

            private sealed class UVStream
            {
                public int dimension;
                public List<Vector4> values;
            }
        }
    }
}
