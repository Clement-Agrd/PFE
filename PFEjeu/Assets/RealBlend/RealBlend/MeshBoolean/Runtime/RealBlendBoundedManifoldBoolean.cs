using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    /// <summary>
    /// Result codes for the bounded manifold Boolean path.  Anything other than
    /// Success is deliberately non-destructive: the caller may use another
    /// solver, change the operands, or show the returned diagnostic.
    /// </summary>
    internal enum RealBlendBoundedBooleanStatus
    {
        Success,
        NoVolumeOverlap,
        InvalidInput,
        OpenOrNonManifoldInput,
        CoplanarOrAmbiguous,
        UnsupportedCollider,
        CandidateBudgetExceeded,
        FragmentBudgetExceeded,
        RayBudgetExceeded,
        OutputBudgetExceeded,
        TimeBudgetExceeded,
        NumericalFailure
    }

    internal struct RealBlendBoundedBooleanReport
    {
        public RealBlendBoundedBooleanStatus status;
        public int targetTriangles;
        public int cutterTriangles;
        public int broadphaseCandidates;
        public int intersectionSegments;
        public int affectedTargetTriangles;
        public int affectedCutterTriangles;
        public int generatedFragments;
        public int rayTriangleTests;
        public int outputVertices;
        public int outputTriangles;
        public int cutSurfaceTriangles;
        public int generatedCutSurfaceTriangles;
        public float epsilon;
        public long elapsedMilliseconds;
        public bool usedColliderTessellation;
    }

    /// <summary>
    /// Every allocation-producing stage has a separate ceiling.  In particular,
    /// epsilon is never used as a voxel/grid cell size, so entering 1e-6 cannot
    /// create memory proportional to 1 / epsilon.
    /// </summary>
    internal sealed class RealBlendBoundedBooleanLimits
    {
        public int maxCandidatePairs = 1000000;
        public int maxIntersectionSegments = 250000;
        public int maxSegmentsPerTriangle = 256;
        public int maxArrangementVerticesPerTriangle = 4096;
        public int maxArrangementVertices = 500000;
        public int maxArrangementEdges = 750000;
        public long maxArrangementWork = 12000000;
        public int maxIntermediatePolygons = 300000;
        public int maxFragments = 350000;
        public int maxRayTriangleTests = 12000000;
        public int maxOutputTriangles = 300000;
        public int maxOutputVertices = 900000;
        public int maxOutputSubmeshes = 256;
        public int maxTraversalStack = 4096;
        public int maxMilliseconds = 30000;

        public static RealBlendBoundedBooleanLimits CreateDefault(
            int inputLimit,
            RealBlendBooleanResourceBudget resourceBudget =
                RealBlendBooleanResourceBudget.Expanded)
        {
            int triangles = Mathf.Clamp(inputLimit, 1, 100000);
            float scale =
                RealBlendBooleanBudgetUtility.GetMemoryScale(resourceBudget);
            return new RealBlendBoundedBooleanLimits
            {
                maxCandidatePairs = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 40, 100000, 1500000),
                    scale,
                    3000000),
                maxIntersectionSegments = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 10, 25000, 300000),
                    scale,
                    600000),
                maxArrangementVertices = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 20, 100000, 600000),
                    scale,
                    1200000),
                maxArrangementEdges = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 30, 150000, 900000),
                    scale,
                    1800000),
                maxArrangementWork = RealBlendBooleanBudgetUtility.ScaleLong(
                    Mathf.Clamp(triangles * 400, 2000000, 16000000),
                    scale,
                    32000000),
                maxIntermediatePolygons = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 16, 100000, 400000),
                    scale,
                    800000),
                maxFragments = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 18, 120000, 450000),
                    scale,
                    900000),
                maxRayTriangleTests = RealBlendBooleanBudgetUtility.ScaleInt(
                    12000000,
                    scale,
                    24000000),
                maxOutputTriangles = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 16, 100000, 400000),
                    scale,
                    800000),
                maxOutputVertices = RealBlendBooleanBudgetUtility.ScaleInt(
                    Mathf.Clamp(triangles * 48, 300000, 1200000),
                    scale,
                    2400000),
                maxMilliseconds =
                    RealBlendBooleanBudgetUtility.GetMaximumMilliseconds(
                        resourceBudget)
            };
        }
    }

    /// <summary>
    /// Bounded closed-manifold difference solver.  This is intentionally a
    /// narrow fast path: a BVH limits triangle-pair and ray work, only triangles
    /// touched by an intersection segment are partitioned, and ambiguous
    /// coplanar configurations return a typed fallback instead of retrying with
    /// progressively larger polygon sets.
    /// </summary>
    internal static class RealBlendBoundedManifoldBoolean
    {
        private const int BvhLeafSize = 8;
        private const float MinimumEpsilon = 0.0000001f;
        private const float MaximumRelativeEpsilon = 0.0001f;

        internal static bool TrySubtract(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            out Mesh result,
            out RealBlendBoundedBooleanReport report,
            out string error)
        {
            int inputLimit = settings != null
                ? settings.maxInputTriangles
                : 25000;
            return TrySubtract(
                targetMesh,
                targetLocalToWorld,
                cutterMesh,
                cutterLocalToWorld,
                settings,
                RealBlendBoundedBooleanLimits.CreateDefault(
                    inputLimit,
                    settings != null
                        ? settings.resourceBudget
                        : RealBlendBooleanResourceBudget.Expanded),
                false,
                out result,
                out report,
                out error);
        }

        internal static bool TrySubtract(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            RealBlendBoundedBooleanLimits limits,
            out Mesh result,
            out RealBlendBoundedBooleanReport report,
            out string error)
        {
            return TrySubtract(
                targetMesh,
                targetLocalToWorld,
                cutterMesh,
                cutterLocalToWorld,
                settings,
                limits,
                false,
                out result,
                out report,
                out error);
        }

        /// <summary>
        /// Collider entry point used by sculpt cutters that do not have a
        /// MeshFilter. Mesh, box, sphere, and capsule colliders are supported.
        /// Primitive colliders are tessellated directly in target-local space so
        /// CapsuleCollider.direction and Unity's lossy-scale rules are respected.
        /// </summary>
        internal static bool TrySubtract(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Collider cutterCollider,
            RealBlendMeshBooleanSettings settings,
            out Mesh result,
            out RealBlendBoundedBooleanReport report,
            out string error)
        {
            result = null;
            report = default;
            error = string.Empty;
            if (cutterCollider == null)
            {
                return Fail(
                    RealBlendBoundedBooleanStatus.InvalidInput,
                    "The cutter collider is missing.",
                    ref report,
                    out error);
            }

            if (cutterCollider is MeshCollider meshCollider)
            {
                if (meshCollider.sharedMesh == null)
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.InvalidInput,
                        "The MeshCollider has no shared mesh.",
                        ref report,
                        out error);
                }
                return TrySubtract(
                    targetMesh,
                    targetLocalToWorld,
                    meshCollider.sharedMesh,
                    meshCollider.transform.localToWorldMatrix,
                    settings,
                    RealBlendBoundedBooleanLimits.CreateDefault(
                        settings != null ? settings.maxInputTriangles : 25000,
                        settings != null
                            ? settings.resourceBudget
                            : RealBlendBooleanResourceBudget.Expanded),
                    false,
                    out result,
                    out report,
                    out error);
            }

            Matrix4x4 worldToTarget;
            if (!TryInverse(targetLocalToWorld, out worldToTarget))
            {
                return Fail(
                    RealBlendBoundedBooleanStatus.InvalidInput,
                    "The target transform must be finite and invertible.",
                    ref report,
                    out error);
            }

            Mesh temporary = null;
            try
            {
                if (cutterCollider is CapsuleCollider capsule)
                {
                    temporary = CreateCapsuleMesh(capsule, worldToTarget, 20, 8);
                }
                else if (cutterCollider is SphereCollider sphere)
                {
                    temporary = CreateSphereMesh(sphere, worldToTarget, 20, 12);
                }
                else if (cutterCollider is BoxCollider box)
                {
                    temporary = CreateBoxMesh(box, worldToTarget);
                }
                else
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.UnsupportedCollider,
                        "This collider type has no closed-surface tessellator. Use a MeshCollider, BoxCollider, SphereCollider, or CapsuleCollider.",
                        ref report,
                        out error);
                }

                if (temporary == null)
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.NumericalFailure,
                        "The collider could not be converted to a finite closed mesh.",
                        ref report,
                        out error);
                }

                return TrySubtract(
                    targetMesh,
                    targetLocalToWorld,
                    temporary,
                    // Primitive vertices were authored in target-local space.
                    // Feeding the target transform here makes cutterToTarget
                    // identity inside the shared mesh path.
                    targetLocalToWorld,
                    settings,
                    RealBlendBoundedBooleanLimits.CreateDefault(
                        settings != null ? settings.maxInputTriangles : 25000,
                        settings != null
                            ? settings.resourceBudget
                            : RealBlendBooleanResourceBudget.Expanded),
                    true,
                    out result,
                    out report,
                    out error);
            }
            finally
            {
                DestroyTemporary(temporary);
            }
        }

        private static bool TrySubtract(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            RealBlendBoundedBooleanLimits limits,
            bool colliderTessellated,
            out Mesh result,
            out RealBlendBoundedBooleanReport report,
            out string error)
        {
            result = null;
            report = default;
            report.usedColliderTessellation = colliderTessellated;
            error = string.Empty;
            settings = settings ?? new RealBlendMeshBooleanSettings();
            limits = limits ?? RealBlendBoundedBooleanLimits.CreateDefault(
                settings.maxInputTriangles,
                settings.resourceBudget);
            Stopwatch stopwatch = Stopwatch.StartNew();
            WorkBudget budget = new WorkBudget(limits, stopwatch, report);

            try
            {
                if (targetMesh == null || cutterMesh == null)
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.InvalidInput,
                        "Target and cutter meshes are required.",
                        ref report,
                        out error,
                        stopwatch);
                }
                if (settings.cutSurfaceSubmesh < 0 ||
                    settings.cutSurfaceSubmesh >= limits.maxOutputSubmeshes ||
                    targetMesh.subMeshCount < 1 ||
                    targetMesh.subMeshCount > limits.maxOutputSubmeshes)
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.InvalidInput,
                        "The requested cut submesh or target submesh count is outside the bounded solver's supported range (0-" +
                        (limits.maxOutputSubmeshes - 1) + ").",
                        ref report,
                        out error,
                        stopwatch);
                }
                if (!TryInverse(targetLocalToWorld, out Matrix4x4 worldToTarget) ||
                    !IsFinite(cutterLocalToWorld))
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.InvalidInput,
                        "Target and cutter transforms must be finite and invertible.",
                        ref report,
                        out error,
                        stopwatch);
                }

                int maxInput = Mathf.Clamp(settings.maxInputTriangles, 1, 100000);
                Matrix4x4 cutterToTarget = worldToTarget * cutterLocalToWorld;
                Bounds targetBounds = targetMesh.bounds;
                Bounds cutterBounds = TransformBounds(cutterMesh.bounds, cutterToTarget);
                float scale = Mathf.Max(
                    1f,
                    Mathf.Max(targetBounds.size.magnitude, cutterBounds.size.magnitude));
                float requested = Mathf.Max(MinimumEpsilon, settings.planeEpsilon);
                float epsilon = Mathf.Clamp(
                    requested,
                    Mathf.Max(MinimumEpsilon, scale * 0.00000001f),
                    Mathf.Max(MinimumEpsilon, scale * MaximumRelativeEpsilon));
                float topologyTolerance = Mathf.Clamp(
                    Mathf.Max(epsilon, settings.weldTolerance),
                    epsilon,
                    Mathf.Max(epsilon, scale * MaximumRelativeEpsilon));
                report.epsilon = epsilon;

                if (!BoundsOverlap(targetBounds, cutterBounds, epsilon, true))
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.NoVolumeOverlap,
                        "The cutter does not overlap the target volume.",
                        ref report,
                        out error,
                        stopwatch);
                }

                if (!TryReadOperand(
                        targetMesh,
                        Matrix4x4.identity,
                        maxInput,
                        epsilon,
                        topologyTolerance,
                        settings.requireClosedTarget,
                        "target",
                        out Operand target,
                        out RealBlendBoundedBooleanStatus readStatus,
                        out error))
                {
                    return Fail(readStatus, error, ref report, out error, stopwatch);
                }
                if (!TryReadOperand(
                        cutterMesh,
                        cutterToTarget,
                        maxInput,
                        epsilon,
                        topologyTolerance,
                        settings.requireClosedCutter,
                        "cutter",
                        out Operand cutter,
                        out readStatus,
                        out error))
                {
                    return Fail(readStatus, error, ref report, out error, stopwatch);
                }

                report.targetTriangles = target.triangles.Length;
                report.cutterTriangles = cutter.triangles.Length;
                budget.SetReport(report);

                if (!BoundsOverlap(target.bounds, cutter.bounds, epsilon, true))
                {
                    return Fail(
                        RealBlendBoundedBooleanStatus.NoVolumeOverlap,
                        "The cutter does not overlap the target volume.",
                        ref report,
                        out error,
                        stopwatch);
                }

                Bvh targetBvh = new Bvh(target.triangles);
                Bvh cutterBvh = new Bvh(cutter.triangles);
                List<CutSegment>[] targetCuts =
                    new List<CutSegment>[target.triangles.Length];
                List<CutSegment>[] cutterCuts =
                    new List<CutSegment>[cutter.triangles.Length];
                PositionSnapper seamSnapper = new PositionSnapper(epsilon * 4f);

                if (!CollectIntersections(
                        target,
                        cutter,
                        targetBvh,
                        cutterBvh,
                        epsilon,
                        seamSnapper,
                        targetCuts,
                        cutterCuts,
                        budget,
                        out RealBlendBoundedBooleanStatus intersectionStatus,
                        out error))
                {
                    report = budget.GetReport(intersectionStatus);
                    return Fail(
                        intersectionStatus,
                        error,
                        ref report,
                        out error,
                        stopwatch);
                }

                if (!BuildOperandEdgeNodes(
                        target,
                        targetCuts,
                        epsilon,
                        budget,
                        out List<Vector3>[] targetEdgeNodes,
                        out intersectionStatus,
                        out error) ||
                    !BuildOperandEdgeNodes(
                        cutter,
                        cutterCuts,
                        epsilon,
                        budget,
                        out List<Vector3>[] cutterEdgeNodes,
                        out intersectionStatus,
                        out error))
                {
                    report = budget.GetReport(intersectionStatus);
                    return Fail(
                        intersectionStatus,
                        error,
                        ref report,
                        out error,
                        stopwatch);
                }

                OutputBuilder output = new OutputBuilder(
                    target.attributes,
                    Mathf.Max(
                        targetMesh.subMeshCount,
                        settings.cutSurfaceSubmesh + 1),
                    settings.cutSurfaceColor,
                    topologyTolerance,
                    limits,
                    budget);

                PointClassifier cutterClassifier = new PointClassifier(
                    cutter,
                    cutterBvh,
                    epsilon,
                    budget);
                PointClassifier targetClassifier = new PointClassifier(
                    target,
                    targetBvh,
                    epsilon,
                    budget);

                if (!EmitTargetSurface(
                        target,
                        cutter,
                        targetCuts,
                        targetEdgeNodes,
                        cutterClassifier,
                        epsilon,
                        output,
                        budget,
                        out RealBlendBoundedBooleanStatus emitStatus,
                        out error))
                {
                    report = budget.GetReport(emitStatus);
                    return Fail(
                        emitStatus,
                        error,
                        ref report,
                        out error,
                        stopwatch);
                }

                // A difference reverses the cutter relative to the target's
                // orientation convention.  Comparing both volume signs also
                // supports consistently inward-wound target meshes.
                bool reverseCutter =
                    (cutter.signedVolume >= 0.0) ==
                    (target.signedVolume >= 0.0);
                if (!EmitCutSurface(
                        cutter,
                        target,
                        cutterCuts,
                        cutterEdgeNodes,
                        targetClassifier,
                        settings.cutSurfaceSubmesh,
                        reverseCutter,
                        epsilon,
                        output,
                        budget,
                        out emitStatus,
                        out error))
                {
                    report = budget.GetReport(emitStatus);
                    return Fail(
                        emitStatus,
                        error,
                        ref report,
                        out error,
                        stopwatch);
                }

                // With no surface intersections, an unchanged target and no
                // retained cutter shell means the operands' AABBs overlapped
                // but their solid volumes did not.  Report that explicitly
                // instead of manufacturing a duplicate "successful" mesh.
                // Contained cutters still have cut faces here, while a cutter
                // enclosing the whole target has zero output triangles.
                if (budget.IntersectionSegmentCount == 0 &&
                    output.CutSurfaceTriangleCount == 0 &&
                    output.TriangleCount == target.triangles.Length)
                {
                    report = budget.GetReport(
                        RealBlendBoundedBooleanStatus.NoVolumeOverlap);
                    return Fail(
                        RealBlendBoundedBooleanStatus.NoVolumeOverlap,
                        "The operand bounds overlap, but their closed solid volumes do not.",
                        ref report,
                        out error,
                        stopwatch);
                }

                if (!output.TryCreateMesh(
                        targetMesh.name + "_BoundedDifference",
                        settings.leaveCutOpen,
                        targetMesh.subMeshCount,
                        out result,
                        out RealBlendBoundedBooleanStatus createStatus,
                        out error))
                {
                    report = budget.GetReport(createStatus);
                    return Fail(
                        createStatus,
                        error,
                        ref report,
                        out error,
                        stopwatch);
                }

                int retainedOutputTriangles = settings.leaveCutOpen
                    ? output.TriangleCount - output.CutSurfaceTriangleCount
                    : output.TriangleCount;
                if (settings.leaveCutOpen &&
                    retainedOutputTriangles > 0 &&
                    !RealBlendMeshBoolean.TryValidateOpenBooleanResult(
                        targetMesh,
                        result,
                        topologyTolerance,
                        out string openResultError))
                {
                    DestroyTemporary(result);
                    result = null;
                    report = budget.GetReport(
                        RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous);
                    return Fail(
                        RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous,
                        "The open Boolean result was rejected because " +
                        openResultError + ".",
                        ref report,
                        out error,
                        stopwatch);
                }

                report = budget.GetReport(RealBlendBoundedBooleanStatus.Success);
                report.outputVertices = result != null ? result.vertexCount : 0;
                report.outputTriangles = retainedOutputTriangles;
                report.generatedCutSurfaceTriangles =
                    output.CutSurfaceTriangleCount;
                report.cutSurfaceTriangles = settings.leaveCutOpen
                    ? 0
                    : output.CutSurfaceTriangleCount;
                report.elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                report.usedColliderTessellation = colliderTessellated;
                error = string.Empty;
                return true;
            }
            catch (OutOfMemoryException exception)
            {
                DestroyTemporary(result);
                result = null;
                report = budget.GetReport(
                    RealBlendBoundedBooleanStatus.OutputBudgetExceeded);
                return Fail(
                    RealBlendBoundedBooleanStatus.OutputBudgetExceeded,
                    "The bounded Boolean exhausted its memory allowance and stopped terminally: " +
                    exception.Message,
                    ref report,
                    out error,
                    stopwatch);
            }
            catch (Exception exception)
            {
                DestroyTemporary(result);
                result = null;
                report = budget.GetReport(
                    RealBlendBoundedBooleanStatus.NumericalFailure);
                return Fail(
                    RealBlendBoundedBooleanStatus.NumericalFailure,
                    "The bounded Boolean stopped safely: " + exception.Message,
                    ref report,
                    out error,
                    stopwatch);
            }
        }

        private static bool CollectIntersections(
            Operand target,
            Operand cutter,
            Bvh targetBvh,
            Bvh cutterBvh,
            float epsilon,
            PositionSnapper seamSnapper,
            List<CutSegment>[] targetCuts,
            List<CutSegment>[] cutterCuts,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            NodePair[] stack = new NodePair[Mathf.Max(32, budget.Limits.maxTraversalStack)];
            int stackCount = 0;
            stack[stackCount++] = new NodePair(targetBvh.Root, cutterBvh.Root);

            while (stackCount > 0)
            {
                if (!budget.CheckTime(out status, out error))
                    return false;
                NodePair pair = stack[--stackCount];
                BvhNode targetNode = targetBvh.nodes[pair.first];
                BvhNode cutterNode = cutterBvh.nodes[pair.second];
                if (!BoundsOverlap(
                        targetNode.min,
                        targetNode.max,
                        cutterNode.min,
                        cutterNode.max,
                        epsilon))
                    continue;

                if (targetNode.IsLeaf && cutterNode.IsLeaf)
                {
                    for (int ta = 0; ta < targetNode.count; ta++)
                    {
                        int targetIndex = targetBvh.order[targetNode.start + ta];
                        SourceTriangle targetTriangle = target.triangles[targetIndex];
                        for (int cb = 0; cb < cutterNode.count; cb++)
                        {
                            int cutterIndex = cutterBvh.order[cutterNode.start + cb];
                            SourceTriangle cutterTriangle = cutter.triangles[cutterIndex];
                            if (!BoundsOverlap(
                                    targetTriangle.min,
                                    targetTriangle.max,
                                    cutterTriangle.min,
                                    cutterTriangle.max,
                                    epsilon))
                                continue;
                            if (!budget.AddCandidate(out status, out error))
                                return false;

                            TriangleIntersection intersection = IntersectTriangles(
                                targetTriangle,
                                cutterTriangle,
                                epsilon);
                            if (intersection.kind == IntersectionKind.CoplanarOverlap)
                            {
                                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                                error =
                                    "The operands contain overlapping coplanar triangles. " +
                                    "The bounded manifold path intentionally falls back instead of generating an unbounded planar arrangement.";
                                return false;
                            }
                            if (intersection.kind == IntersectionKind.Ambiguous)
                            {
                                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                                error =
                                    "A triangle pair produced a numerically ambiguous intersection. " +
                                    "The bounded manifold path stopped before modifying the mesh.";
                                return false;
                            }
                            if (intersection.kind != IntersectionKind.Segment)
                                continue;

                            // A pair is processed only once, but the same seam
                            // vertex is normally rediscovered by adjacent
                            // triangle pairs. Canonicalizing it here keeps the
                            // independently partitioned operands conformal.
                            CutSegment segment = new CutSegment(
                                seamSnapper.Snap(intersection.first),
                                seamSnapper.Snap(intersection.second));
                            if ((segment.second - segment.first).sqrMagnitude <=
                                epsilon * epsilon)
                                continue;
                            if (!AddCutSegment(
                                    targetCuts,
                                    targetIndex,
                                    segment,
                                    epsilon,
                                    budget,
                                    out status,
                                    out error) ||
                                !AddCutSegment(
                                    cutterCuts,
                                    cutterIndex,
                                    segment,
                                    epsilon,
                                    budget,
                                    out status,
                                    out error,
                                    false))
                                return false;
                        }
                    }
                    continue;
                }

                bool splitTarget = !targetNode.IsLeaf &&
                    (cutterNode.IsLeaf || targetNode.SurfaceArea >= cutterNode.SurfaceArea);
                if (splitTarget)
                {
                    if (!PushPair(stack, ref stackCount, targetNode.left, pair.second) ||
                        !PushPair(stack, ref stackCount, targetNode.right, pair.second))
                    {
                        status = RealBlendBoundedBooleanStatus.CandidateBudgetExceeded;
                        error = "The BVH traversal stack reached its hard safety limit.";
                        return false;
                    }
                }
                else
                {
                    if (!PushPair(stack, ref stackCount, pair.first, cutterNode.left) ||
                        !PushPair(stack, ref stackCount, pair.first, cutterNode.right))
                    {
                        status = RealBlendBoundedBooleanStatus.CandidateBudgetExceeded;
                        error = "The BVH traversal stack reached its hard safety limit.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool PushPair(
            NodePair[] stack,
            ref int count,
            int first,
            int second)
        {
            if (count >= stack.Length)
                return false;
            stack[count++] = new NodePair(first, second);
            return true;
        }

        private static bool AddCutSegment(
            List<CutSegment>[] cuts,
            int triangle,
            CutSegment segment,
            float epsilon,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error,
            bool countGlobal = true)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            List<CutSegment> list = cuts[triangle];
            if (list == null)
            {
                list = new List<CutSegment>(2);
                cuts[triangle] = list;
                budget.AddAffectedTriangle(countGlobal);
            }
            float squaredTolerance = epsilon * epsilon * 4f;
            for (int i = 0; i < list.Count; i++)
            {
                CutSegment existing = list[i];
                bool sameDirection =
                    (existing.first - segment.first).sqrMagnitude <= squaredTolerance &&
                    (existing.second - segment.second).sqrMagnitude <= squaredTolerance;
                bool oppositeDirection =
                    (existing.first - segment.second).sqrMagnitude <= squaredTolerance &&
                    (existing.second - segment.first).sqrMagnitude <= squaredTolerance;
                if (sameDirection || oppositeDirection)
                    return true;
            }
            if (list.Count >= budget.Limits.maxSegmentsPerTriangle)
            {
                status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                error = "One triangle received more intersection segments than the bounded splitter permits.";
                return false;
            }
            if (countGlobal && !budget.AddIntersectionSegment(out status, out error))
                return false;
            list.Add(segment);
            return true;
        }

        /// <summary>
        /// Propagates every seam endpoint lying on an operand source edge to all
        /// geometrically incident triangles before any local arrangement is
        /// built.  The key is positional rather than index-based so UV,
        /// material, and normal seams receive the same ordered boundary nodes.
        /// </summary>
        private static bool BuildOperandEdgeNodes(
            Operand operand,
            List<CutSegment>[] cuts,
            float epsilon,
            WorkBudget budget,
            out List<Vector3>[] triangleNodes,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            triangleNodes = new List<Vector3>[operand.triangles.Length];
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            long initialWork = (long)operand.triangles.Length * 3L;
            for (int i = 0; i < cuts.Length; i++)
            {
                if (cuts[i] != null)
                    initialWork += (long)cuts[i].Count * 6L;
            }
            if (!budget.ChargeArrangementWork(initialWork, out status, out error))
                return false;

            float keyTolerance = epsilon * 4f;
            Dictionary<PositionEdgeKey, SourceEdgeNodeRecord> edges =
                new Dictionary<PositionEdgeKey, SourceEdgeNodeRecord>(
                    operand.triangles.Length * 2);
            for (int triangleIndex = 0;
                 triangleIndex < operand.triangles.Length;
                 triangleIndex++)
            {
                SourceTriangle triangle = operand.triangles[triangleIndex];
                RegisterSourceEdge(edges, triangleIndex, triangle.a, triangle.b, keyTolerance);
                RegisterSourceEdge(edges, triangleIndex, triangle.b, triangle.c, keyTolerance);
                RegisterSourceEdge(edges, triangleIndex, triangle.c, triangle.a, keyTolerance);
            }

            for (int triangleIndex = 0; triangleIndex < cuts.Length; triangleIndex++)
            {
                List<CutSegment> triangleCuts = cuts[triangleIndex];
                if (triangleCuts == null)
                    continue;
                SourceTriangle triangle = operand.triangles[triangleIndex];
                PositionEdgeKey firstEdge =
                    new PositionEdgeKey(triangle.a, triangle.b, keyTolerance);
                PositionEdgeKey secondEdge =
                    new PositionEdgeKey(triangle.b, triangle.c, keyTolerance);
                PositionEdgeKey thirdEdge =
                    new PositionEdgeKey(triangle.c, triangle.a, keyTolerance);
                for (int segment = 0; segment < triangleCuts.Count; segment++)
                {
                    CollectSourceEdgeNode(
                        edges[firstEdge],
                        triangleCuts[segment].first,
                        epsilon);
                    CollectSourceEdgeNode(
                        edges[secondEdge],
                        triangleCuts[segment].first,
                        epsilon);
                    CollectSourceEdgeNode(
                        edges[thirdEdge],
                        triangleCuts[segment].first,
                        epsilon);
                    CollectSourceEdgeNode(
                        edges[firstEdge],
                        triangleCuts[segment].second,
                        epsilon);
                    CollectSourceEdgeNode(
                        edges[secondEdge],
                        triangleCuts[segment].second,
                        epsilon);
                    CollectSourceEdgeNode(
                        edges[thirdEdge],
                        triangleCuts[segment].second,
                        epsilon);
                }
            }

            long assignments = 0;
            foreach (SourceEdgeNodeRecord edge in edges.Values)
                assignments += (long)edge.nodes.Count * edge.triangles.Count;
            if (!budget.ChargeArrangementWork(assignments, out status, out error))
                return false;

            foreach (SourceEdgeNodeRecord edge in edges.Values)
            {
                if (edge.nodes.Count == 0)
                    continue;
                Vector3 direction = edge.second - edge.first;
                float lengthSquared = Mathf.Max(MinimumEpsilon, direction.sqrMagnitude);
                edge.nodes.Sort((first, second) =>
                {
                    float firstT = Vector3.Dot(first - edge.first, direction) /
                                   lengthSquared;
                    float secondT = Vector3.Dot(second - edge.first, direction) /
                                    lengthSquared;
                    return firstT.CompareTo(secondT);
                });
                for (int use = 0; use < edge.triangles.Count; use++)
                {
                    int triangleIndex = edge.triangles[use];
                    List<Vector3> nodes = triangleNodes[triangleIndex];
                    if (nodes == null)
                    {
                        nodes = new List<Vector3>(edge.nodes.Count);
                        triangleNodes[triangleIndex] = nodes;
                    }
                    for (int node = 0; node < edge.nodes.Count; node++)
                    {
                        Vector3 point = edge.nodes[node];
                        bool duplicate = false;
                        for (int existing = 0; existing < nodes.Count; existing++)
                        {
                            if ((nodes[existing] - point).sqrMagnitude <=
                                epsilon * epsilon * 4f)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                        if (!duplicate)
                            nodes.Add(point);
                    }
                }
            }
            return true;
        }

        private static void RegisterSourceEdge(
            Dictionary<PositionEdgeKey, SourceEdgeNodeRecord> edges,
            int triangle,
            Vector3 first,
            Vector3 second,
            float tolerance)
        {
            PositionEdgeKey key = new PositionEdgeKey(first, second, tolerance);
            if (!edges.TryGetValue(key, out SourceEdgeNodeRecord record))
            {
                record = new SourceEdgeNodeRecord(first, second);
                edges.Add(key, record);
            }
            if (!record.triangles.Contains(triangle))
                record.triangles.Add(triangle);
        }

        private static void CollectSourceEdgeNode(
            SourceEdgeNodeRecord edge,
            Vector3 point,
            float epsilon)
        {
            Vector3 direction = edge.second - edge.first;
            float lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= epsilon * epsilon)
                return;
            float t = Vector3.Dot(point - edge.first, direction) / lengthSquared;
            float parameterTolerance = epsilon /
                Mathf.Max(epsilon, Mathf.Sqrt(lengthSquared));
            if (t <= parameterTolerance || t >= 1f - parameterTolerance)
                return;
            Vector3 closest = edge.first + direction * t;
            if ((closest - point).sqrMagnitude > epsilon * epsilon * 16f)
                return;
            for (int i = 0; i < edge.nodes.Count; i++)
            {
                if ((edge.nodes[i] - point).sqrMagnitude <= epsilon * epsilon * 4f)
                    return;
            }
            edge.nodes.Add(point);
        }

        private static bool EmitTargetSurface(
            Operand target,
            Operand cutter,
            List<CutSegment>[] cuts,
            List<Vector3>[] edgeNodes,
            PointClassifier classifier,
            float epsilon,
            OutputBuilder output,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            for (int i = 0; i < target.triangles.Length; i++)
            {
                if ((i & 63) == 0 && !budget.CheckTime(out status, out error))
                    return false;
                SourceTriangle triangle = target.triangles[i];
                List<FragmentTriangle> fragments;
                if (!TrySplitTriangle(
                        triangle,
                        cuts[i],
                        edgeNodes[i],
                        epsilon,
                        budget,
                        out fragments,
                        out status,
                        out error))
                    return false;

                bool definitelyOutside = !BoundsOverlap(
                    triangle.min,
                    triangle.max,
                    cutter.bounds.min,
                    cutter.bounds.max,
                    epsilon);
                if (!TryClassifyFragmentRegions(
                        fragments,
                        classifier,
                        false,
                        definitelyOutside,
                        budget,
                        out PointRelation[] relations,
                        out status,
                        out error))
                    return false;
                for (int f = 0; f < fragments.Count; f++)
                {
                    FragmentTriangle fragment = fragments[f];
                    PointRelation relation = relations[fragment.region];
                    if (relation == PointRelation.Inside ||
                        relation == PointRelation.Boundary)
                        continue;
                    if (!output.TryAddTriangle(
                            target.attributes,
                            triangle,
                            fragment,
                            triangle.submesh,
                            false,
                            false,
                            out status,
                            out error))
                        return false;
                }
            }
            return true;
        }

        private static bool EmitCutSurface(
            Operand cutter,
            Operand target,
            List<CutSegment>[] cuts,
            List<Vector3>[] edgeNodes,
            PointClassifier classifier,
            int cutSubmesh,
            bool reverseCutter,
            float epsilon,
            OutputBuilder output,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            for (int i = 0; i < cutter.triangles.Length; i++)
            {
                if ((i & 63) == 0 && !budget.CheckTime(out status, out error))
                    return false;
                SourceTriangle triangle = cutter.triangles[i];
                if (!BoundsOverlap(
                        triangle.min,
                        triangle.max,
                        target.bounds.min,
                        target.bounds.max,
                        epsilon))
                    continue;

                if (!TrySplitTriangle(
                        triangle,
                        cuts[i],
                        edgeNodes[i],
                        epsilon,
                        budget,
                        out List<FragmentTriangle> fragments,
                        out status,
                        out error))
                    return false;

                if (!TryClassifyFragmentRegions(
                        fragments,
                        classifier,
                        true,
                        false,
                        budget,
                        out PointRelation[] relations,
                        out status,
                        out error))
                    return false;
                for (int f = 0; f < fragments.Count; f++)
                {
                    FragmentTriangle fragment = fragments[f];
                    PointRelation relation = relations[fragment.region];
                    if (relation != PointRelation.Inside)
                        continue;
                    if (!output.TryAddTriangle(
                            cutter.attributes,
                            triangle,
                            fragment,
                            cutSubmesh,
                            true,
                            reverseCutter,
                            out status,
                            out error))
                        return false;
                }
            }
            return true;
        }

        private static bool TryClassifyFragmentRegions(
            List<FragmentTriangle> fragments,
            PointClassifier classifier,
            bool keepInside,
            bool forceOutside,
            WorkBudget budget,
            out PointRelation[] relations,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            relations = null;
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            int regionCount = 0;
            for (int i = 0; i < fragments.Count; i++)
                regionCount = Mathf.Max(regionCount, fragments[i].region + 1);
            if (!budget.ChargeArrangementWork(
                    fragments.Count + regionCount,
                    out status,
                    out error))
                return false;

            relations = new PointRelation[regionCount];
            if (forceOutside)
            {
                for (int region = 0; region < regionCount; region++)
                    relations[region] = PointRelation.Outside;
                return true;
            }

            int[] representatives = new int[regionCount];
            float[] clearances = new float[regionCount];
            float[] areas = new float[regionCount];
            for (int region = 0; region < regionCount; region++)
            {
                representatives[region] = -1;
                clearances[region] = float.NegativeInfinity;
            }
            for (int i = 0; i < fragments.Count; i++)
            {
                FragmentTriangle fragment = fragments[i];
                int region = fragment.region;
                bool farther = fragment.constraintClearance > clearances[region];
                bool sameClearance =
                    float.IsPositiveInfinity(fragment.constraintClearance) &&
                    float.IsPositiveInfinity(clearances[region]) ||
                    Mathf.Abs(fragment.constraintClearance - clearances[region]) <=
                    MinimumEpsilon * MinimumEpsilon;
                if (representatives[region] < 0 ||
                    farther ||
                    (sameClearance && fragment.AreaSquared > areas[region]))
                {
                    representatives[region] = i;
                    clearances[region] = fragment.constraintClearance;
                    areas[region] = fragment.AreaSquared;
                }
            }

            for (int region = 0; region < regionCount; region++)
            {
                if (representatives[region] < 0)
                {
                    status = RealBlendBoundedBooleanStatus.NumericalFailure;
                    error = "A finite arrangement region has no representative face.";
                    return false;
                }
                FragmentTriangle representative = fragments[representatives[region]];
                PointRelation relation = classifier.ClassifyForDifference(
                    representative.Centroid,
                    representative.Normal,
                    keepInside,
                    out status,
                    out error);
                if (relation == PointRelation.Ambiguous)
                {
                    if (status == RealBlendBoundedBooleanStatus.Success)
                    {
                        status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                        error = "A finite arrangement region could not be classified consistently.";
                    }
                    return false;
                }
                relations[region] = relation;
            }
            return true;
        }

        private static bool TrySplitTriangle(
            SourceTriangle triangle,
            List<CutSegment> segments,
            List<Vector3> propagatedEdgeNodes,
            float epsilon,
            WorkBudget budget,
            out List<FragmentTriangle> fragments,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            fragments = null;
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            bool hasSegments = segments != null && segments.Count > 0;
            bool hasPropagatedNodes =
                propagatedEdgeNodes != null && propagatedEdgeNodes.Count > 0;
            if (!hasSegments && !hasPropagatedNodes)
            {
                if (!budget.AddFragments(1, out status, out error))
                    return false;
                fragments = new List<FragmentTriangle>(1)
                {
                    FragmentTriangle.FromSource(triangle)
                };
                return true;
            }
            int droppedAxis = DominantAxis(triangle.normal);
            Vector2 projectedA = Project(triangle.a, droppedAxis);
            Vector2 projectedB = Project(triangle.b, droppedAxis);
            Vector2 projectedC = Project(triangle.c, droppedAxis);
            float projectedSourceArea = Cross2D(
                projectedB - projectedA,
                projectedC - projectedA);
            if (Mathf.Abs(projectedSourceArea) <= epsilon * epsilon)
            {
                status = RealBlendBoundedBooleanStatus.NumericalFailure;
                error = "An affected triangle has an unstable planar projection.";
                return false;
            }

            if (!budget.ChargeArrangementWork(
                    (long)(hasSegments ? segments.Count : 0) *
                    ((hasSegments ? segments.Count : 0) + 1L) / 2L,
                    out status,
                    out error))
                return false;

            int segmentCount = hasSegments ? segments.Count : 0;
            List<PlanarInputSegment> inputs = new List<PlanarInputSegment>(segmentCount);
            for (int i = 0; i < segmentCount; i++)
            {
                if ((i & 31) == 0 && !budget.CheckTime(out status, out error))
                    return false;
                CutSegment source = segments[i];
                Vector2 first2D = Project(source.first, droppedAxis);
                Vector2 second2D = Project(source.second, droppedAxis);
                if (!ClipSegmentToTriangle(
                        first2D,
                        second2D,
                        projectedA,
                        projectedB,
                        projectedC,
                        epsilon,
                        out float firstT,
                        out float secondT))
                    continue;
                float parameterTolerance = epsilon /
                    Mathf.Max(epsilon, (second2D - first2D).magnitude);
                if (firstT > parameterTolerance * 4f ||
                    secondT < 1f - parameterTolerance * 4f)
                {
                    status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                    error =
                        "An intersection segment extended beyond its source triangle. " +
                        "The finite arrangement refused to create an unshared boundary point.";
                    return false;
                }
                Vector3 first3D = Vector3.LerpUnclamped(
                    source.first,
                    source.second,
                    Mathf.Clamp01(firstT));
                Vector3 second3D = Vector3.LerpUnclamped(
                    source.first,
                    source.second,
                    Mathf.Clamp01(secondT));
                if ((second3D - first3D).sqrMagnitude <= epsilon * epsilon)
                    continue;
                inputs.Add(new PlanarInputSegment(
                    first3D,
                    second3D,
                    Project(first3D, droppedAxis),
                    Project(second3D, droppedAxis)));
            }

            if (inputs.Count == 0 && !hasPropagatedNodes)
            {
                if (!budget.AddFragments(1, out status, out error))
                    return false;
                fragments = new List<FragmentTriangle>(1)
                {
                    FragmentTriangle.FromSource(triangle)
                };
                return true;
            }

            for (int first = 0; first < inputs.Count; first++)
            {
                for (int second = first + 1; second < inputs.Count; second++)
                {
                    if (!AddPlanarSegmentIntersections(
                            inputs[first],
                            inputs[second],
                            epsilon,
                            out bool ambiguous))
                    {
                        if (!ambiguous)
                            continue;
                        status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                        error = "Collinear intersection constraints could not be ordered safely.";
                        return false;
                    }
                }
            }

            List<ArrangementVertex> vertices = new List<ArrangementVertex>();
            ArrangementVertexRegistry registry = new ArrangementVertexRegistry(
                vertices,
                epsilon * 2f,
                triangle,
                droppedAxis,
                budget);
            if (!registry.TryGetOrAdd(
                    triangle.a,
                    out int cornerA,
                    out status,
                    out error) ||
                !registry.TryGetOrAdd(
                    triangle.b,
                    out int cornerB,
                    out status,
                    out error) ||
                !registry.TryGetOrAdd(
                    triangle.c,
                    out int cornerC,
                    out status,
                    out error))
                return false;

            for (int segment = 0; segment < inputs.Count; segment++)
            {
                PlanarInputSegment input = inputs[segment];
                input.parameters.Sort();
                for (int parameter = 0; parameter < input.parameters.Count; parameter++)
                {
                    float t = input.parameters[parameter];
                    if (!registry.TryGetOrAdd(
                            Vector3.LerpUnclamped(input.first3D, input.second3D, t),
                            out _,
                            out status,
                            out error))
                        return false;
                }
            }
            if (hasPropagatedNodes)
            {
                if (!budget.ChargeArrangementWork(
                        propagatedEdgeNodes.Count,
                        out status,
                        out error))
                    return false;
                for (int node = 0; node < propagatedEdgeNodes.Count; node++)
                {
                    if (!registry.TryGetOrAdd(
                            propagatedEdgeNodes[node],
                            out _,
                            out status,
                            out error))
                        return false;
                }
            }

            if (!budget.ChargeArrangementWork(
                    (long)inputs.Count * vertices.Count,
                    out status,
                    out error))
                return false;

            HashSet<IndexEdgeKey> requestedSet = new HashSet<IndexEdgeKey>();
            List<IndexEdgeKey> requestedEdges = new List<IndexEdgeKey>();
            for (int segment = 0; segment < inputs.Count; segment++)
            {
                PlanarInputSegment input = inputs[segment];
                List<VertexParameter> points = new List<VertexParameter>();
                Vector2 direction = input.second2D - input.first2D;
                float lengthSquared = direction.sqrMagnitude;
                float parameterTolerance = epsilon /
                    Mathf.Max(epsilon, Mathf.Sqrt(lengthSquared));
                for (int vertex = 0; vertex < vertices.Count; vertex++)
                {
                    Vector2 point = vertices[vertex].point;
                    float t = Vector2.Dot(point - input.first2D, direction) /
                              lengthSquared;
                    if (t < -parameterTolerance || t > 1f + parameterTolerance)
                        continue;
                    Vector2 closest = input.first2D + direction * t;
                    if ((closest - point).sqrMagnitude > epsilon * epsilon * 4f)
                        continue;
                    points.Add(new VertexParameter(vertex, Mathf.Clamp01(t)));
                }
                points.Sort(VertexParameterComparer.Instance);
                for (int point = 0; point + 1 < points.Count; point++)
                {
                    int firstVertex = points[point].vertex;
                    int secondVertex = points[point + 1].vertex;
                    if (firstVertex == secondVertex)
                        continue;
                    IndexEdgeKey edge = new IndexEdgeKey(firstVertex, secondVertex);
                    if (!requestedSet.Add(edge))
                        continue;
                    if (!budget.AddArrangementEdge(out status, out error))
                        return false;
                    requestedEdges.Add(edge);
                }
            }

            List<ArrangementTriangle> triangulation =
                new List<ArrangementTriangle>(Mathf.Max(4, vertices.Count * 2));
            AddArrangementTriangle(
                triangulation,
                vertices,
                cornerA,
                cornerB,
                cornerC,
                epsilon);
            for (int vertex = 0; vertex < vertices.Count; vertex++)
            {
                if (vertex == cornerA || vertex == cornerB || vertex == cornerC)
                    continue;
                if (!InsertArrangementVertex(
                        vertex,
                        vertices,
                        triangulation,
                        epsilon,
                        budget,
                        out status,
                        out error))
                    return false;
            }

            if (!ValidateLocalArrangement(
                    triangulation,
                    vertices,
                    cornerA,
                    cornerB,
                    cornerC,
                    epsilon,
                    budget,
                    out status,
                    out error))
                return false;

            HashSet<IndexEdgeKey> protectedEdges = new HashSet<IndexEdgeKey>();
            for (int edge = 0; edge < requestedEdges.Count; edge++)
            {
                if (!RecoverConstraintEdge(
                        requestedEdges[edge],
                        vertices,
                        triangulation,
                        protectedEdges,
                        cornerA,
                        cornerB,
                        cornerC,
                        epsilon,
                        budget,
                        out status,
                        out error))
                    return false;
                protectedEdges.Add(requestedEdges[edge]);
            }

            if (triangulation.Count == 0)
            {
                status = RealBlendBoundedBooleanStatus.NumericalFailure;
                error = "The finite triangle arrangement produced no faces.";
                return false;
            }
            if (!budget.AddFragments(triangulation.Count, out status, out error))
                return false;
            if (!budget.ChargeArrangementWork(
                    (long)triangulation.Count * Mathf.Max(1, protectedEdges.Count),
                    out status,
                    out error))
                return false;

            fragments = new List<FragmentTriangle>(triangulation.Count);
            int[] regions = BuildArrangementRegions(triangulation, protectedEdges);
            bool projectedOrientationMatchesSource = projectedSourceArea > 0f;
            for (int i = 0; i < triangulation.Count; i++)
            {
                ArrangementTriangle face = triangulation[i];
                FragmentVertex first = vertices[face.a].fragment;
                FragmentVertex second = vertices[face.b].fragment;
                FragmentVertex third = vertices[face.c].fragment;
                float clearance = ConstraintClearance(
                    face,
                    vertices,
                    protectedEdges);
                fragments.Add(projectedOrientationMatchesSource
                    ? new FragmentTriangle(first, second, third, regions[i], clearance)
                    : new FragmentTriangle(first, third, second, regions[i], clearance));
            }
            return true;
        }

        private static int[] BuildArrangementRegions(
            List<ArrangementTriangle> triangles,
            HashSet<IndexEdgeKey> protectedEdges)
        {
            int[] parents = new int[triangles.Count];
            for (int i = 0; i < parents.Length; i++)
                parents[i] = i;
            Dictionary<IndexEdgeKey, int> owner =
                new Dictionary<IndexEdgeKey, int>(triangles.Count * 2);
            for (int triangle = 0; triangle < triangles.Count; triangle++)
            {
                ArrangementTriangle face = triangles[triangle];
                UnionAcrossUnprotectedEdge(
                    new IndexEdgeKey(face.a, face.b),
                    triangle,
                    protectedEdges,
                    owner,
                    parents);
                UnionAcrossUnprotectedEdge(
                    new IndexEdgeKey(face.b, face.c),
                    triangle,
                    protectedEdges,
                    owner,
                    parents);
                UnionAcrossUnprotectedEdge(
                    new IndexEdgeKey(face.c, face.a),
                    triangle,
                    protectedEdges,
                    owner,
                    parents);
            }
            Dictionary<int, int> regionByRoot = new Dictionary<int, int>();
            int[] regions = new int[triangles.Count];
            for (int triangle = 0; triangle < triangles.Count; triangle++)
            {
                int root = FindRegionRoot(parents, triangle);
                if (!regionByRoot.TryGetValue(root, out int region))
                {
                    region = regionByRoot.Count;
                    regionByRoot.Add(root, region);
                }
                regions[triangle] = region;
            }
            return regions;
        }

        private static void UnionAcrossUnprotectedEdge(
            IndexEdgeKey edge,
            int triangle,
            HashSet<IndexEdgeKey> protectedEdges,
            Dictionary<IndexEdgeKey, int> owner,
            int[] parents)
        {
            if (protectedEdges.Contains(edge))
                return;
            if (!owner.TryGetValue(edge, out int other))
            {
                owner.Add(edge, triangle);
                return;
            }
            int firstRoot = FindRegionRoot(parents, triangle);
            int secondRoot = FindRegionRoot(parents, other);
            if (firstRoot != secondRoot)
                parents[firstRoot] = secondRoot;
        }

        private static int FindRegionRoot(int[] parents, int value)
        {
            int root = value;
            while (parents[root] != root)
                root = parents[root];
            while (parents[value] != value)
            {
                int next = parents[value];
                parents[value] = root;
                value = next;
            }
            return root;
        }

        private static float ConstraintClearance(
            ArrangementTriangle triangle,
            List<ArrangementVertex> vertices,
            HashSet<IndexEdgeKey> protectedEdges)
        {
            if (protectedEdges.Count == 0)
                return float.PositiveInfinity;
            Vector2 point = (
                vertices[triangle.a].point +
                vertices[triangle.b].point +
                vertices[triangle.c].point) / 3f;
            float minimum = float.PositiveInfinity;
            foreach (IndexEdgeKey edge in protectedEdges)
            {
                Vector2 first = vertices[edge.first].point;
                Vector2 second = vertices[edge.second].point;
                Vector2 direction = second - first;
                float lengthSquared = direction.sqrMagnitude;
                float t = lengthSquared > MinimumEpsilon * MinimumEpsilon
                    ? Mathf.Clamp01(Vector2.Dot(point - first, direction) /
                                    lengthSquared)
                    : 0f;
                float squared = (point - (first + direction * t)).sqrMagnitude;
                minimum = Mathf.Min(minimum, squared);
            }
            return minimum;
        }

        private static bool ClipSegmentToTriangle(
            Vector2 first,
            Vector2 second,
            Vector2 triangleA,
            Vector2 triangleB,
            Vector2 triangleC,
            float epsilon,
            out float minimum,
            out float maximum)
        {
            minimum = 0f;
            maximum = 1f;
            float orientation = Mathf.Sign(Cross2D(
                triangleB - triangleA,
                triangleC - triangleA));
            Vector2[] triangle = { triangleA, triangleB, triangleC };
            for (int edge = 0; edge < 3; edge++)
            {
                Vector2 edgeStart = triangle[edge];
                Vector2 edgeDirection = triangle[(edge + 1) % 3] - edgeStart;
                float firstSide = orientation * Cross2D(edgeDirection, first - edgeStart);
                float secondSide = orientation * Cross2D(edgeDirection, second - edgeStart);
                float sideTolerance = CrossTolerance(edgeDirection, epsilon);
                bool firstInside = firstSide >= -sideTolerance;
                bool secondInside = secondSide >= -sideTolerance;
                if (!firstInside && !secondInside)
                    return false;
                if (firstInside == secondInside)
                    continue;
                float denominator = firstSide - secondSide;
                if (Mathf.Abs(denominator) <= MinimumEpsilon)
                    return false;
                float t = firstSide / denominator;
                if (!firstInside)
                    minimum = Mathf.Max(minimum, t);
                else
                    maximum = Mathf.Min(maximum, t);
                if (minimum > maximum)
                    return false;
            }
            return maximum - minimum > MinimumEpsilon;
        }

        private static bool AddPlanarSegmentIntersections(
            PlanarInputSegment first,
            PlanarInputSegment second,
            float epsilon,
            out bool ambiguous)
        {
            ambiguous = false;
            Vector2 firstDirection = first.second2D - first.first2D;
            Vector2 secondDirection = second.second2D - second.first2D;
            Vector2 offset = second.first2D - first.first2D;
            float denominator = Cross2D(firstDirection, secondDirection);
            float scale = Mathf.Max(firstDirection.magnitude, secondDirection.magnitude);
            float crossTolerance = epsilon * Mathf.Max(epsilon, scale);
            if (Mathf.Abs(denominator) > crossTolerance)
            {
                float firstT = Cross2D(offset, secondDirection) / denominator;
                float secondT = Cross2D(offset, firstDirection) / denominator;
                float parameterTolerance = epsilon / Mathf.Max(epsilon, scale);
                if (firstT < -parameterTolerance || firstT > 1f + parameterTolerance ||
                    secondT < -parameterTolerance || secondT > 1f + parameterTolerance)
                    return false;
                first.AddParameter(Mathf.Clamp01(firstT), parameterTolerance);
                second.AddParameter(Mathf.Clamp01(secondT), parameterTolerance);
                return true;
            }

            if (Mathf.Abs(Cross2D(offset, firstDirection)) > crossTolerance)
                return false;
            float firstLengthSquared = firstDirection.sqrMagnitude;
            float secondLengthSquared = secondDirection.sqrMagnitude;
            if (firstLengthSquared <= epsilon * epsilon ||
                secondLengthSquared <= epsilon * epsilon)
            {
                ambiguous = true;
                return false;
            }
            float toleranceFirst = epsilon / Mathf.Sqrt(firstLengthSquared);
            float toleranceSecond = epsilon / Mathf.Sqrt(secondLengthSquared);
            AddCollinearEndpoint(first, second.first2D, toleranceFirst);
            AddCollinearEndpoint(first, second.second2D, toleranceFirst);
            AddCollinearEndpoint(second, first.first2D, toleranceSecond);
            AddCollinearEndpoint(second, first.second2D, toleranceSecond);
            return true;
        }

        private static void AddCollinearEndpoint(
            PlanarInputSegment segment,
            Vector2 point,
            float parameterTolerance)
        {
            Vector2 direction = segment.second2D - segment.first2D;
            float parameter = Vector2.Dot(point - segment.first2D, direction) /
                              direction.sqrMagnitude;
            if (parameter < -parameterTolerance || parameter > 1f + parameterTolerance)
                return;
            segment.AddParameter(Mathf.Clamp01(parameter), parameterTolerance);
        }

        private static bool InsertArrangementVertex(
            int vertex,
            List<ArrangementVertex> vertices,
            List<ArrangementTriangle> triangles,
            float epsilon,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            if (!budget.ChargeArrangementWork(triangles.Count, out status, out error))
                return false;
            Vector2 point = vertices[vertex].point;
            IndexEdgeKey containingEdge = default;
            bool foundEdge = false;
            for (int i = 0; i < triangles.Count && !foundEdge; i++)
            {
                ArrangementTriangle triangle = triangles[i];
                int[] indices = { triangle.a, triangle.b, triangle.c };
                for (int edge = 0; edge < 3; edge++)
                {
                    int first = indices[edge];
                    int second = indices[(edge + 1) % 3];
                    if (!PointStrictlyOnSegment(
                            point,
                            vertices[first].point,
                            vertices[second].point,
                            epsilon))
                        continue;
                    containingEdge = new IndexEdgeKey(first, second);
                    foundEdge = true;
                    break;
                }
            }

            List<ArrangementTriangle> rebuilt = new List<ArrangementTriangle>(
                triangles.Count + 2);
            bool inserted = false;
            if (foundEdge)
            {
                for (int i = 0; i < triangles.Count; i++)
                {
                    ArrangementTriangle triangle = triangles[i];
                    if (!triangle.ContainsEdge(containingEdge, out int opposite))
                    {
                        rebuilt.Add(triangle);
                        continue;
                    }
                    AddArrangementTriangle(
                        rebuilt,
                        vertices,
                        containingEdge.first,
                        vertex,
                        opposite,
                        epsilon);
                    AddArrangementTriangle(
                        rebuilt,
                        vertices,
                        vertex,
                        containingEdge.second,
                        opposite,
                        epsilon);
                    inserted = true;
                }
            }
            else
            {
                int containingTriangle = -1;
                for (int i = 0; i < triangles.Count; i++)
                {
                    ArrangementTriangle triangle = triangles[i];
                    if (PointInArrangementTriangle(
                            point,
                            vertices[triangle.a].point,
                            vertices[triangle.b].point,
                            vertices[triangle.c].point,
                            epsilon))
                    {
                        containingTriangle = i;
                        break;
                    }
                }
                for (int i = 0; i < triangles.Count; i++)
                {
                    ArrangementTriangle triangle = triangles[i];
                    if (i != containingTriangle)
                    {
                        rebuilt.Add(triangle);
                        continue;
                    }
                    AddArrangementTriangle(
                        rebuilt,
                        vertices,
                        triangle.a,
                        triangle.b,
                        vertex,
                        epsilon);
                    AddArrangementTriangle(
                        rebuilt,
                        vertices,
                        triangle.b,
                        triangle.c,
                        vertex,
                        epsilon);
                    AddArrangementTriangle(
                        rebuilt,
                        vertices,
                        triangle.c,
                        triangle.a,
                        vertex,
                        epsilon);
                    inserted = true;
                }
            }

            if (!inserted)
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "A finite-arrangement vertex could not be inserted into its source triangle.";
                return false;
            }
            triangles.Clear();
            triangles.AddRange(rebuilt);
            return true;
        }

        private static bool RecoverConstraintEdge(
            IndexEdgeKey constraint,
            List<ArrangementVertex> vertices,
            List<ArrangementTriangle> triangles,
            HashSet<IndexEdgeKey> protectedEdges,
            int sourceA,
            int sourceB,
            int sourceC,
            float epsilon,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            if (ArrangementHasEdge(triangles, constraint))
            {
                if (!budget.ChargeArrangementWork(
                        (long)(protectedEdges.Count + 1) *
                        Mathf.Max(1, triangles.Count),
                        out status,
                        out error))
                    return false;
                return ValidateRecoveredConstraints(
                    triangles,
                    protectedEdges,
                    constraint,
                    vertices,
                    sourceA,
                    sourceB,
                    sourceC,
                    epsilon,
                    budget,
                    out status,
                    out error);
            }
            if (!budget.ChargeArrangementWork(triangles.Count, out status, out error))
                return false;

            Vector2 first = vertices[constraint.first].point;
            Vector2 second = vertices[constraint.second].point;
            List<int> crossed = new List<int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                ArrangementTriangle triangle = triangles[i];
                if (!ClipSegmentToTriangle(
                        first,
                        second,
                        vertices[triangle.a].point,
                        vertices[triangle.b].point,
                        vertices[triangle.c].point,
                        epsilon * 0.25f,
                        out float minimum,
                        out float maximum))
                    continue;
                if (maximum - minimum <= 0.000001f)
                    continue;
                crossed.Add(i);
            }
            if (crossed.Count == 0)
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "A finite constraint did not cross the current local triangulation.";
                return false;
            }

            HashSet<int> crossedSet = new HashSet<int>(crossed);
            Dictionary<IndexEdgeKey, int> edgeUse = new Dictionary<IndexEdgeKey, int>();
            for (int i = 0; i < crossed.Count; i++)
            {
                ArrangementTriangle triangle = triangles[crossed[i]];
                AddIndexEdgeUse(edgeUse, triangle.a, triangle.b);
                AddIndexEdgeUse(edgeUse, triangle.b, triangle.c);
                AddIndexEdgeUse(edgeUse, triangle.c, triangle.a);
            }
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<IndexEdgeKey, int> pair in edgeUse)
            {
                if (pair.Value != 1)
                    continue;
                if (protectedEdges.Contains(pair.Key) &&
                    SegmentProperlyCrosses(
                        first,
                        second,
                        vertices[pair.Key.first].point,
                        vertices[pair.Key.second].point,
                        epsilon))
                {
                    status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                    error = "A finite constraint attempted to cross an existing constraint.";
                    return false;
                }
                AddAdjacency(adjacency, pair.Key.first, pair.Key.second);
                AddAdjacency(adjacency, pair.Key.second, pair.Key.first);
            }
            if (!adjacency.TryGetValue(constraint.first, out List<int> startNeighbours) ||
                startNeighbours.Count != 2 ||
                !adjacency.TryGetValue(constraint.second, out List<int> endNeighbours) ||
                endNeighbours.Count != 2)
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "The crossed-triangle strip did not form two finite boundary chains.";
                return false;
            }
            foreach (KeyValuePair<int, List<int>> pair in adjacency)
            {
                if (pair.Value.Count != 2)
                {
                    status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                    error = "The finite constraint strip has a branched boundary.";
                    return false;
                }
            }

            if (!TraceBoundaryChain(
                    constraint.first,
                    constraint.second,
                    startNeighbours[0],
                    adjacency,
                    out List<int> firstChain) ||
                !TraceBoundaryChain(
                    constraint.first,
                    constraint.second,
                    startNeighbours[1],
                    adjacency,
                    out List<int> secondChain))
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "A finite constraint boundary could not be ordered.";
                return false;
            }

            if (!budget.ChargeArrangementWork(
                    (long)firstChain.Count * firstChain.Count +
                    (long)secondChain.Count * secondChain.Count +
                    triangles.Count,
                    out status,
                    out error))
                return false;

            List<ArrangementTriangle> rebuilt = new List<ArrangementTriangle>(
                triangles.Count - crossed.Count +
                Mathf.Max(0, firstChain.Count - 2) +
                Mathf.Max(0, secondChain.Count - 2));
            for (int i = 0; i < triangles.Count; i++)
            {
                if (!crossedSet.Contains(i))
                    rebuilt.Add(triangles[i]);
            }
            if (!TriangulateSimplePolygon(
                    firstChain,
                    vertices,
                    epsilon,
                    rebuilt,
                    out error) ||
                !TriangulateSimplePolygon(
                    secondChain,
                    vertices,
                    epsilon,
                    rebuilt,
                    out error))
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                return false;
            }
            if (!budget.ChargeArrangementWork(
                    (long)(protectedEdges.Count + 1) *
                    Mathf.Max(1, rebuilt.Count),
                    out status,
                    out error))
                return false;
            if (!ValidateRecoveredConstraints(
                    rebuilt,
                    protectedEdges,
                    constraint,
                    vertices,
                    sourceA,
                    sourceB,
                    sourceC,
                    epsilon,
                    budget,
                    out status,
                    out error))
                return false;
            triangles.Clear();
            triangles.AddRange(rebuilt);
            return true;
        }

        private static bool ValidateRecoveredConstraints(
            List<ArrangementTriangle> triangles,
            HashSet<IndexEdgeKey> protectedEdges,
            IndexEdgeKey recovered,
            List<ArrangementVertex> vertices,
            int sourceA,
            int sourceB,
            int sourceC,
            float epsilon,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            foreach (IndexEdgeKey edge in protectedEdges)
            {
                if (ArrangementHasEdge(triangles, edge))
                    continue;
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "Recovering a finite constraint removed a previously protected " +
                        "intersection edge.";
                return false;
            }
            if (!ArrangementHasEdge(triangles, recovered))
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "The finite constraint could not be recovered as a triangulation edge.";
                return false;
            }
            return ValidateLocalArrangement(
                triangles,
                vertices,
                sourceA,
                sourceB,
                sourceC,
                epsilon,
                budget,
                out status,
                out error);
        }

        /// <summary>
        /// Postcondition for every source-triangle PSLG.  The arrangement must
        /// be a non-overlapping, consistently oriented tiling of the original
        /// triangle: internal edges occur twice in opposite directions and the
        /// only single-use edges lie on the source boundary.  Area conservation
        /// then detects any boundary-chain collapse or missing strip.
        /// </summary>
        private static bool ValidateLocalArrangement(
            List<ArrangementTriangle> triangles,
            List<ArrangementVertex> vertices,
            int sourceA,
            int sourceB,
            int sourceC,
            float epsilon,
            WorkBudget budget,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            status = RealBlendBoundedBooleanStatus.Success;
            error = string.Empty;
            if (triangles.Count == 0)
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "A finite arrangement lost the whole source triangle.";
                return false;
            }
            if (!budget.ChargeArrangementWork(
                    (long)triangles.Count * 4L,
                    out status,
                    out error))
                return false;

            Dictionary<IndexEdgeKey, LocalArrangementEdgeUse> edges =
                new Dictionary<IndexEdgeKey, LocalArrangementEdgeUse>(
                    triangles.Count * 2);
            HashSet<IndexTriangleKey> uniqueFaces =
                new HashSet<IndexTriangleKey>();
            double coveredTwiceArea = 0.0;
            float minimumTwiceArea = epsilon * epsilon;
            for (int i = 0; i < triangles.Count; i++)
            {
                if ((i & 255) == 0 &&
                    !budget.CheckTime(out status, out error))
                    return false;
                ArrangementTriangle triangle = triangles[i];
                if (!uniqueFaces.Add(new IndexTriangleKey(
                        triangle.a,
                        triangle.b,
                        triangle.c)))
                {
                    status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                    error = "A finite arrangement contains duplicate overlapping faces.";
                    return false;
                }
                float twiceArea = Cross2D(
                    vertices[triangle.b].point - vertices[triangle.a].point,
                    vertices[triangle.c].point - vertices[triangle.a].point);
                if (twiceArea <= minimumTwiceArea)
                {
                    status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                    error = "A finite arrangement contains a collapsed or reversed face.";
                    return false;
                }
                coveredTwiceArea += twiceArea;
                AddLocalArrangementEdge(edges, triangle.a, triangle.b);
                AddLocalArrangementEdge(edges, triangle.b, triangle.c);
                AddLocalArrangementEdge(edges, triangle.c, triangle.a);
            }

            Vector2 a = vertices[sourceA].point;
            Vector2 b = vertices[sourceB].point;
            Vector2 c = vertices[sourceC].point;
            float boundaryTolerance = epsilon * 5f;
            foreach (KeyValuePair<IndexEdgeKey, LocalArrangementEdgeUse> pair in edges)
            {
                LocalArrangementEdgeUse use = pair.Value;
                if (use.count == 2 && use.orientation == 0)
                    continue;
                if (use.count == 1 &&
                    EdgeLiesOnSourceBoundary(
                        pair.Key,
                        vertices,
                        a,
                        b,
                        c,
                        boundaryTolerance))
                    continue;
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = use.count == 2
                    ? "A finite arrangement contains same-directed overlapping faces."
                    : "A finite arrangement contains a hole or non-manifold edge with incidence " +
                      use.count + ".";
                return false;
            }

            double sourceTwiceArea = Math.Abs(Cross2D(b - a, c - a));
            double perimeter = (b - a).magnitude +
                               (c - b).magnitude +
                               (a - c).magnitude;
            double areaTolerance = Math.Max(
                epsilon * perimeter * 16.0,
                Math.Max(
                    epsilon * epsilon * Math.Max(32, triangles.Count * 8),
                    sourceTwiceArea * 0.00001));
            if (Math.Abs(coveredTwiceArea - sourceTwiceArea) > areaTolerance)
            {
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "A finite constraint strip did not conserve the source triangle area.";
                return false;
            }
            return true;
        }

        private static void AddLocalArrangementEdge(
            Dictionary<IndexEdgeKey, LocalArrangementEdgeUse> edges,
            int first,
            int second)
        {
            bool forward = first < second;
            IndexEdgeKey key = new IndexEdgeKey(first, second);
            edges.TryGetValue(key, out LocalArrangementEdgeUse use);
            use.count++;
            use.orientation += forward ? 1 : -1;
            edges[key] = use;
        }

        private static bool EdgeLiesOnSourceBoundary(
            IndexEdgeKey edge,
            List<ArrangementVertex> vertices,
            Vector2 sourceA,
            Vector2 sourceB,
            Vector2 sourceC,
            float epsilon)
        {
            Vector2 first = vertices[edge.first].point;
            Vector2 second = vertices[edge.second].point;
            return PointOnSegment2D(first, sourceA, sourceB, epsilon) &&
                       PointOnSegment2D(second, sourceA, sourceB, epsilon) ||
                   PointOnSegment2D(first, sourceB, sourceC, epsilon) &&
                       PointOnSegment2D(second, sourceB, sourceC, epsilon) ||
                   PointOnSegment2D(first, sourceC, sourceA, epsilon) &&
                       PointOnSegment2D(second, sourceC, sourceA, epsilon);
        }

        private struct LocalArrangementEdgeUse
        {
            public int count;
            public int orientation;
        }

        private readonly struct IndexTriangleKey : IEquatable<IndexTriangleKey>
        {
            private readonly int first;
            private readonly int second;
            private readonly int third;

            public IndexTriangleKey(int a, int b, int c)
            {
                if (a > b) Swap(ref a, ref b);
                if (b > c) Swap(ref b, ref c);
                if (a > b) Swap(ref a, ref b);
                first = a;
                second = b;
                third = c;
            }

            public bool Equals(IndexTriangleKey other)
            {
                return first == other.first &&
                       second == other.second &&
                       third == other.third;
            }

            public override bool Equals(object obj)
            {
                return obj is IndexTriangleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = first;
                    hash = hash * 397 ^ second;
                    return hash * 397 ^ third;
                }
            }

            private static void Swap(ref int firstValue, ref int secondValue)
            {
                int value = firstValue;
                firstValue = secondValue;
                secondValue = value;
            }
        }

        private static bool TriangulateSimplePolygon(
            List<int> source,
            List<ArrangementVertex> vertices,
            float epsilon,
            List<ArrangementTriangle> output,
            out string error)
        {
            error = string.Empty;
            if (source.Count < 3)
                return true;
            List<int> polygon = new List<int>(source);
            float signedArea = PolygonArea(polygon, vertices);
            if (Mathf.Abs(signedArea) <= epsilon * epsilon)
            {
                error = "A nontrivial finite-arrangement boundary chain collapsed to zero area.";
                return false;
            }
            if (signedArea < 0f)
                polygon.Reverse();

            int safety = polygon.Count * polygon.Count;
            while (polygon.Count > 3 && safety-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    int previous = polygon[(i + polygon.Count - 1) % polygon.Count];
                    int current = polygon[i];
                    int next = polygon[(i + 1) % polygon.Count];
                    Vector2 a = vertices[previous].point;
                    Vector2 b = vertices[current].point;
                    Vector2 c = vertices[next].point;
                    if (Cross2D(b - a, c - b) <=
                        Mathf.Max(
                            epsilon * epsilon,
                            epsilon * Mathf.Max(
                                (b - a).magnitude,
                                (c - b).magnitude)))
                        continue;
                    if (!EarDiagonalIsVisible(
                            polygon,
                            previous,
                            current,
                            next,
                            vertices,
                            epsilon))
                        continue;
                    bool contains = false;
                    for (int other = 0; other < polygon.Count; other++)
                    {
                        int candidate = polygon[other];
                        if (candidate == previous || candidate == current || candidate == next)
                            continue;
                        if (PointInArrangementTriangle(
                                vertices[candidate].point,
                                a,
                                b,
                                c,
                                epsilon))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains)
                        continue;
                    AddArrangementTriangle(
                        output,
                        vertices,
                        previous,
                        current,
                        next,
                        epsilon);
                    polygon.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    error = "A finite arrangement face could not be triangulated without crossing a constraint.";
                    return false;
                }
            }
            if (polygon.Count == 3)
            {
                AddArrangementTriangle(
                    output,
                    vertices,
                    polygon[0],
                    polygon[1],
                    polygon[2],
                    epsilon);
            }
            return true;
        }

        private static bool EarDiagonalIsVisible(
            List<int> polygon,
            int previous,
            int current,
            int next,
            List<ArrangementVertex> vertices,
            float epsilon)
        {
            Vector2 first = vertices[previous].point;
            Vector2 second = vertices[next].point;
            for (int vertex = 0; vertex < polygon.Count; vertex++)
            {
                int candidate = polygon[vertex];
                if (candidate == previous ||
                    candidate == current ||
                    candidate == next)
                    continue;
                if (PointStrictlyOnSegment(
                        vertices[candidate].point,
                        first,
                        second,
                        epsilon))
                    return false;
            }
            for (int edge = 0; edge < polygon.Count; edge++)
            {
                int edgeFirst = polygon[edge];
                int edgeSecond = polygon[(edge + 1) % polygon.Count];
                if (edgeFirst == previous || edgeSecond == previous ||
                    edgeFirst == next || edgeSecond == next)
                    continue;
                if (SegmentsIntersectInclusive2D(
                        first,
                        second,
                        vertices[edgeFirst].point,
                        vertices[edgeSecond].point,
                        epsilon))
                    return false;
            }
            return true;
        }

        private static bool TraceBoundaryChain(
            int start,
            int end,
            int firstStep,
            Dictionary<int, List<int>> adjacency,
            out List<int> chain)
        {
            chain = new List<int> { start };
            int previous = start;
            int current = firstStep;
            int safety = adjacency.Count + 2;
            while (safety-- > 0)
            {
                chain.Add(current);
                if (current == end)
                    return true;
                if (!adjacency.TryGetValue(current, out List<int> neighbours) ||
                    neighbours.Count != 2)
                    return false;
                int next = neighbours[0] == previous ? neighbours[1] : neighbours[0];
                previous = current;
                current = next;
                if (current == start)
                    return false;
            }
            return false;
        }

        private static void AddAdjacency(
            Dictionary<int, List<int>> adjacency,
            int vertex,
            int neighbour)
        {
            if (!adjacency.TryGetValue(vertex, out List<int> values))
            {
                values = new List<int>(2);
                adjacency.Add(vertex, values);
            }
            if (!values.Contains(neighbour))
                values.Add(neighbour);
        }

        private static void AddIndexEdgeUse(
            Dictionary<IndexEdgeKey, int> uses,
            int first,
            int second)
        {
            IndexEdgeKey edge = new IndexEdgeKey(first, second);
            uses.TryGetValue(edge, out int count);
            uses[edge] = count + 1;
        }

        private static bool ArrangementHasEdge(
            List<ArrangementTriangle> triangles,
            IndexEdgeKey edge)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                if (triangles[i].ContainsEdge(edge, out _))
                    return true;
            }
            return false;
        }

        private static void AddArrangementTriangle(
            List<ArrangementTriangle> triangles,
            List<ArrangementVertex> vertices,
            int first,
            int second,
            int third,
            float epsilon)
        {
            if (first == second || second == third || third == first)
                return;
            float area = Cross2D(
                vertices[second].point - vertices[first].point,
                vertices[third].point - vertices[first].point);
            if (Mathf.Abs(area) <= epsilon * epsilon)
                return;
            triangles.Add(area > 0f
                ? new ArrangementTriangle(first, second, third)
                : new ArrangementTriangle(first, third, second));
        }

        private static bool PointStrictlyOnSegment(
            Vector2 point,
            Vector2 first,
            Vector2 second,
            float epsilon)
        {
            Vector2 direction = second - first;
            float lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= epsilon * epsilon)
                return false;
            float t = Vector2.Dot(point - first, direction) / lengthSquared;
            float parameterTolerance = epsilon /
                Mathf.Max(epsilon, Mathf.Sqrt(lengthSquared));
            if (t <= parameterTolerance || t >= 1f - parameterTolerance)
                return false;
            Vector2 closest = first + direction * t;
            return (closest - point).sqrMagnitude <= epsilon * epsilon * 4f;
        }

        private static bool PointInArrangementTriangle(
            Vector2 point,
            Vector2 first,
            Vector2 second,
            Vector2 third,
            float epsilon)
        {
            Vector2 firstEdge = second - first;
            Vector2 secondEdge = third - second;
            Vector2 thirdEdge = first - third;
            return Cross2D(firstEdge, point - first) >=
                       -CrossTolerance(firstEdge, epsilon) &&
                   Cross2D(secondEdge, point - second) >=
                       -CrossTolerance(secondEdge, epsilon) &&
                   Cross2D(thirdEdge, point - third) >=
                       -CrossTolerance(thirdEdge, epsilon);
        }

        private static bool PointStrictlyInTriangle(
            Vector2 point,
            Vector2 first,
            Vector2 second,
            Vector2 third,
            float epsilon)
        {
            Vector2 firstEdge = second - first;
            Vector2 secondEdge = third - second;
            Vector2 thirdEdge = first - third;
            return Cross2D(firstEdge, point - first) >
                       CrossTolerance(firstEdge, epsilon) &&
                   Cross2D(secondEdge, point - second) >
                       CrossTolerance(secondEdge, epsilon) &&
                   Cross2D(thirdEdge, point - third) >
                       CrossTolerance(thirdEdge, epsilon);
        }

        private static bool SegmentProperlyCrosses(
            Vector2 firstA,
            Vector2 firstB,
            Vector2 secondA,
            Vector2 secondB,
            float epsilon)
        {
            float firstSideA = Cross2D(firstB - firstA, secondA - firstA);
            float firstSideB = Cross2D(firstB - firstA, secondB - firstA);
            float secondSideA = Cross2D(secondB - secondA, firstA - secondA);
            float secondSideB = Cross2D(secondB - secondA, firstB - secondA);
            float firstTolerance = CrossTolerance(firstB - firstA, epsilon);
            float secondTolerance = CrossTolerance(secondB - secondA, epsilon);
            return ((firstSideA > firstTolerance &&
                     firstSideB < -firstTolerance) ||
                    (firstSideA < -firstTolerance &&
                     firstSideB > firstTolerance)) &&
                   ((secondSideA > secondTolerance &&
                     secondSideB < -secondTolerance) ||
                    (secondSideA < -secondTolerance &&
                     secondSideB > secondTolerance));
        }

        private static bool SegmentsIntersectInclusive2D(
            Vector2 firstA,
            Vector2 firstB,
            Vector2 secondA,
            Vector2 secondB,
            float epsilon)
        {
            if (SegmentProperlyCrosses(
                    firstA,
                    firstB,
                    secondA,
                    secondB,
                    epsilon))
                return true;
            return PointOnSegment2D(firstA, secondA, secondB, epsilon) ||
                   PointOnSegment2D(firstB, secondA, secondB, epsilon) ||
                   PointOnSegment2D(secondA, firstA, firstB, epsilon) ||
                   PointOnSegment2D(secondB, firstA, firstB, epsilon);
        }

        private static float CrossTolerance(Vector2 direction, float epsilon)
        {
            return epsilon * Mathf.Max(epsilon, direction.magnitude);
        }

        private static float PolygonArea(
            List<int> polygon,
            List<ArrangementVertex> vertices)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 first = vertices[polygon[i]].point;
                Vector2 second = vertices[polygon[(i + 1) % polygon.Count]].point;
                area += Cross2D(first, second);
            }
            return area * 0.5f;
        }

        private static Vector3 ComputeBarycentric(
            Vector3 point,
            SourceTriangle triangle)
        {
            Vector3 first = triangle.b - triangle.a;
            Vector3 second = triangle.c - triangle.a;
            Vector3 relative = point - triangle.a;
            float d00 = Vector3.Dot(first, first);
            float d01 = Vector3.Dot(first, second);
            float d11 = Vector3.Dot(second, second);
            float d20 = Vector3.Dot(relative, first);
            float d21 = Vector3.Dot(relative, second);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) <= MinimumEpsilon * MinimumEpsilon)
                return new Vector3(1f, 0f, 0f);
            float y = (d11 * d20 - d01 * d21) / denominator;
            float z = (d00 * d21 - d01 * d20) / denominator;
            Vector3 barycentric = new Vector3(1f - y - z, y, z);
            float sum = barycentric.x + barycentric.y + barycentric.z;
            return Mathf.Abs(sum) > MinimumEpsilon
                ? barycentric / sum
                : new Vector3(1f, 0f, 0f);
        }

        private static TriangleIntersection IntersectTriangles(
            SourceTriangle first,
            SourceTriangle second,
            float epsilon)
        {
            float[] firstToSecond =
            {
                Vector3.Dot(second.normal, first.a - second.a),
                Vector3.Dot(second.normal, first.b - second.a),
                Vector3.Dot(second.normal, first.c - second.a)
            };
            float[] secondToFirst =
            {
                Vector3.Dot(first.normal, second.a - first.a),
                Vector3.Dot(first.normal, second.b - first.a),
                Vector3.Dot(first.normal, second.c - first.a)
            };

            if (AllStrictlySameSide(firstToSecond, epsilon) ||
                AllStrictlySameSide(secondToFirst, epsilon))
                return TriangleIntersection.None;

            Vector3 normalCross = Vector3.Cross(first.normal, second.normal);
            bool parallel = normalCross.sqrMagnitude <= 0.0000000001f;
            bool coplanar = parallel &&
                Mathf.Abs(firstToSecond[0]) <= epsilon &&
                Mathf.Abs(firstToSecond[1]) <= epsilon &&
                Mathf.Abs(firstToSecond[2]) <= epsilon;
            if (coplanar)
            {
                return CoplanarTrianglesOverlap(first, second, epsilon)
                    ? TriangleIntersection.Coplanar
                    : TriangleIntersection.None;
            }
            if (parallel)
                return TriangleIntersection.None;

            List<Vector3> points = new List<Vector3>(6);
            AddPlaneEdgeIntersections(
                first,
                firstToSecond,
                second,
                epsilon,
                points);
            AddPlaneEdgeIntersections(
                second,
                secondToFirst,
                first,
                epsilon,
                points);
            if (points.Count < 2)
                return TriangleIntersection.None;

            float farthest = 0f;
            Vector3 endpointA = default;
            Vector3 endpointB = default;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    float distance = (points[i] - points[j]).sqrMagnitude;
                    if (distance <= farthest)
                        continue;
                    farthest = distance;
                    endpointA = points[i];
                    endpointB = points[j];
                }
            }
            if (farthest <= epsilon * epsilon * 4f)
                return TriangleIntersection.None;

            Vector3 intersectionDirection = normalCross.normalized;
            for (int i = 0; i < points.Count; i++)
            {
                float offLine = Vector3.Cross(
                    intersectionDirection,
                    points[i] - endpointA).magnitude;
                if (offLine > epsilon * 8f)
                    return TriangleIntersection.Ambiguous;
            }
            return new TriangleIntersection(
                IntersectionKind.Segment,
                endpointA,
                endpointB);
        }

        private static bool AllStrictlySameSide(float[] distances, float epsilon)
        {
            bool positive = distances[0] > epsilon &&
                            distances[1] > epsilon &&
                            distances[2] > epsilon;
            bool negative = distances[0] < -epsilon &&
                            distances[1] < -epsilon &&
                            distances[2] < -epsilon;
            return positive || negative;
        }

        private static void AddPlaneEdgeIntersections(
            SourceTriangle triangle,
            float[] distances,
            SourceTriangle other,
            float epsilon,
            List<Vector3> points)
        {
            Vector3[] vertices = { triangle.a, triangle.b, triangle.c };
            for (int edge = 0; edge < 3; edge++)
            {
                int next = (edge + 1) % 3;
                Vector3 a = vertices[edge];
                Vector3 b = vertices[next];
                float da = distances[edge];
                float db = distances[next];
                if (Mathf.Abs(da) <= epsilon && PointInTriangle(a, other, epsilon))
                    AddUniquePoint(points, a, epsilon);
                if ((da > epsilon && db > epsilon) ||
                    (da < -epsilon && db < -epsilon))
                    continue;
                float denominator = da - db;
                if (Mathf.Abs(denominator) <= MinimumEpsilon)
                    continue;
                float t = da / denominator;
                if (t < -epsilon || t > 1f + epsilon)
                    continue;
                Vector3 point = Vector3.LerpUnclamped(a, b, Mathf.Clamp01(t));
                if (PointInTriangle(point, other, epsilon))
                    AddUniquePoint(points, point, epsilon);
            }
        }

        private static void AddUniquePoint(
            List<Vector3> points,
            Vector3 point,
            float epsilon)
        {
            float squared = epsilon * epsilon * 4f;
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - point).sqrMagnitude <= squared)
                    return;
            }
            points.Add(point);
        }

        private static bool PointInTriangle(
            Vector3 point,
            SourceTriangle triangle,
            float epsilon)
        {
            Vector3 v0 = triangle.b - triangle.a;
            Vector3 v1 = triangle.c - triangle.a;
            Vector3 v2 = point - triangle.a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) <= MinimumEpsilon * MinimumEpsilon)
                return false;
            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            float u = 1f - v - w;
            float barycentricTolerance = epsilon /
                Mathf.Max(MinimumEpsilon, triangle.longestEdge);
            return u >= -barycentricTolerance &&
                   v >= -barycentricTolerance &&
                   w >= -barycentricTolerance;
        }

        private static bool CoplanarTrianglesOverlap(
            SourceTriangle first,
            SourceTriangle second,
            float epsilon)
        {
            int axis = DominantAxis(first.normal);
            Vector2[] a =
            {
                Project(first.a, axis),
                Project(first.b, axis),
                Project(first.c, axis)
            };
            Vector2[] b =
            {
                Project(second.a, axis),
                Project(second.b, axis),
                Project(second.c, axis)
            };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (SegmentsProperlyIntersect2D(
                            a[i],
                            a[(i + 1) % 3],
                            b[j],
                            b[(j + 1) % 3],
                            epsilon))
                        return true;
                }
            }
            Vector2 centroidA = (a[0] + a[1] + a[2]) / 3f;
            Vector2 centroidB = (b[0] + b[1] + b[2]) / 3f;
            return PointInTriangle2D(centroidA, b[0], b[1], b[2], epsilon) ||
                   PointInTriangle2D(centroidB, a[0], a[1], a[2], epsilon);
        }

        private static bool PointInTriangle2D(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            float epsilon)
        {
            Vector2 firstEdge = b - a;
            Vector2 secondEdge = c - b;
            Vector2 thirdEdge = a - c;
            float first = Cross2D(firstEdge, point - a);
            float second = Cross2D(secondEdge, point - b);
            float third = Cross2D(thirdEdge, point - c);
            float firstTolerance = CrossTolerance(firstEdge, epsilon);
            float secondTolerance = CrossTolerance(secondEdge, epsilon);
            float thirdTolerance = CrossTolerance(thirdEdge, epsilon);
            bool hasNegative = first < -firstTolerance ||
                               second < -secondTolerance ||
                               third < -thirdTolerance;
            bool hasPositive = first > firstTolerance ||
                               second > secondTolerance ||
                               third > thirdTolerance;
            return !(hasNegative && hasPositive);
        }

        private static bool SegmentsProperlyIntersect2D(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float epsilon)
        {
            float abC = Cross2D(b - a, c - a);
            float abD = Cross2D(b - a, d - a);
            float cdA = Cross2D(d - c, a - c);
            float cdB = Cross2D(d - c, b - c);
            float abTolerance = CrossTolerance(b - a, epsilon);
            float cdTolerance = CrossTolerance(d - c, epsilon);
            return ((abC > abTolerance && abD < -abTolerance) ||
                    (abC < -abTolerance && abD > abTolerance)) &&
                   ((cdA > cdTolerance && cdB < -cdTolerance) ||
                    (cdA < -cdTolerance && cdB > cdTolerance));
        }

        private static bool PointOnSegment2D(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            float epsilon)
        {
            Vector2 direction = b - a;
            if (Mathf.Abs(Cross2D(direction, point - a)) >
                CrossTolerance(direction, epsilon))
                return false;
            return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + epsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - epsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + epsilon;
        }

        private static float Cross2D(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static int DominantAxis(Vector3 normal)
        {
            Vector3 absolute = new Vector3(
                Mathf.Abs(normal.x),
                Mathf.Abs(normal.y),
                Mathf.Abs(normal.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z) return 0;
            if (absolute.y >= absolute.z) return 1;
            return 2;
        }

        private static Vector2 Project(Vector3 value, int droppedAxis)
        {
            if (droppedAxis == 0) return new Vector2(value.y, value.z);
            if (droppedAxis == 1) return new Vector2(value.x, value.z);
            return new Vector2(value.x, value.y);
        }

        private static bool TryReadOperand(
            Mesh mesh,
            Matrix4x4 transform,
            int maxTriangles,
            float epsilon,
            float topologyTolerance,
            bool requireClosed,
            string label,
            out Operand operand,
            out RealBlendBoundedBooleanStatus status,
            out string error)
        {
            operand = null;
            status = RealBlendBoundedBooleanStatus.InvalidInput;
            error = string.Empty;
            if (mesh == null)
            {
                error = "The " + label + " mesh is missing.";
                return false;
            }
            if (!mesh.isReadable)
            {
                error = "The " + label + " mesh must have Read/Write enabled.";
                return false;
            }
            if (!IsFinite(transform) || Mathf.Abs(transform.determinant) <= 0.000000000001f)
            {
                error = "The " + label + " transform is not finite and invertible.";
                return false;
            }

            Vector3[] positions = mesh.vertices;
            if (positions == null || positions.Length < 4)
            {
                error = "The " + label + " mesh does not contain a solid.";
                return false;
            }
            List<SourceTriangle> triangles = new List<SourceTriangle>();
            bool mirrored = transform.determinant < 0f;
            double signedVolume = 0.0;
            Bounds bounds = default;
            bool hasBounds = false;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices;
                try
                {
                    indices = mesh.GetTriangles(submesh, true);
                }
                catch (Exception exception)
                {
                    error = "The " + label + " mesh indices could not be read: " +
                            exception.Message;
                    return false;
                }
                if ((indices.Length % 3) != 0)
                {
                    error = "The " + label + " mesh has a non-triangle submesh.";
                    return false;
                }
                if (triangles.Count + indices.Length / 3 > maxTriangles)
                {
                    error = "The " + label + " mesh exceeds the configured input triangle limit (" +
                            maxTriangles + ").";
                    return false;
                }
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int ia = indices[i];
                    int ib = indices[i + 1];
                    int ic = indices[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                    {
                        error = "The " + label + " mesh contains an out-of-range index.";
                        return false;
                    }
                    if (mirrored)
                    {
                        int swap = ib;
                        ib = ic;
                        ic = swap;
                    }
                    Vector3 a = transform.MultiplyPoint3x4(positions[ia]);
                    Vector3 b = transform.MultiplyPoint3x4(positions[ib]);
                    Vector3 c = transform.MultiplyPoint3x4(positions[ic]);
                    if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                    {
                        error = "The " + label + " mesh contains a non-finite vertex.";
                        return false;
                    }
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float longestSquared = Mathf.Max(
                        (b - a).sqrMagnitude,
                        Mathf.Max((c - a).sqrMagnitude, (c - b).sqrMagnitude));
                    if (longestSquared <= epsilon * epsilon ||
                        cross.sqrMagnitude <= epsilon * epsilon * longestSquared)
                    {
                        error = "The " + label + " mesh contains a degenerate triangle.";
                        return false;
                    }
                    SourceTriangle triangle = new SourceTriangle(
                        triangles.Count,
                        submesh,
                        ia,
                        ib,
                        ic,
                        a,
                        b,
                        c);
                    triangles.Add(triangle);
                    signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                    if (!hasBounds)
                    {
                        bounds = triangle.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(triangle.min);
                        bounds.Encapsulate(triangle.max);
                    }
                }
            }

            if (triangles.Count == 0 || !hasBounds)
            {
                error = "The " + label + " mesh has no triangles.";
                return false;
            }
            if (requireClosed && !ValidateClosedManifold(
                    triangles,
                    topologyTolerance,
                    out error))
            {
                status = RealBlendBoundedBooleanStatus.OpenOrNonManifoldInput;
                error = "The " + label + " is not a closed two-manifold: " + error;
                return false;
            }

            operand = new Operand(
                mesh,
                transform,
                triangles.ToArray(),
                bounds,
                signedVolume);
            status = RealBlendBoundedBooleanStatus.Success;
            return true;
        }

        private static bool ValidateClosedManifold(
            List<SourceTriangle> triangles,
            float tolerance,
            out string error)
        {
            error = string.Empty;
            Dictionary<PositionEdgeKey, EdgeUse> edges =
                new Dictionary<PositionEdgeKey, EdgeUse>(triangles.Count * 2);
            for (int i = 0; i < triangles.Count; i++)
            {
                SourceTriangle triangle = triangles[i];
                AddEdgeUse(edges, triangle.a, triangle.b, tolerance);
                AddEdgeUse(edges, triangle.b, triangle.c, tolerance);
                AddEdgeUse(edges, triangle.c, triangle.a, tolerance);
            }
            foreach (KeyValuePair<PositionEdgeKey, EdgeUse> pair in edges)
            {
                if (pair.Value.count != 2)
                {
                    error = "an edge is used " + pair.Value.count + " times (expected 2).";
                    return false;
                }
                if (pair.Value.orientation != 0)
                {
                    error = "two faces use a shared edge in the same direction.";
                    return false;
                }
            }
            return true;
        }

        private static void AddEdgeUse(
            Dictionary<PositionEdgeKey, EdgeUse> edges,
            Vector3 first,
            Vector3 second,
            float tolerance)
        {
            PositionKey a = new PositionKey(first, tolerance);
            PositionKey b = new PositionKey(second, tolerance);
            bool forward = a.CompareTo(b) <= 0;
            PositionEdgeKey key = forward
                ? new PositionEdgeKey(a, b)
                : new PositionEdgeKey(b, a);
            edges.TryGetValue(key, out EdgeUse use);
            use.count++;
            use.orientation += forward ? 1 : -1;
            edges[key] = use;
        }

        private static bool BoundsOverlap(
            Bounds first,
            Bounds second,
            float epsilon,
            bool requireVolume)
        {
            Vector3 overlap = Vector3.Min(first.max, second.max) -
                              Vector3.Max(first.min, second.min);
            if (requireVolume)
                return overlap.x > epsilon && overlap.y > epsilon && overlap.z > epsilon;
            return overlap.x >= -epsilon && overlap.y >= -epsilon && overlap.z >= -epsilon;
        }

        private static bool BoundsOverlap(
            Vector3 firstMin,
            Vector3 firstMax,
            Vector3 secondMin,
            Vector3 secondMax,
            float epsilon)
        {
            return firstMin.x <= secondMax.x + epsilon &&
                   firstMax.x + epsilon >= secondMin.x &&
                   firstMin.y <= secondMax.y + epsilon &&
                   firstMax.y + epsilon >= secondMin.y &&
                   firstMin.z <= secondMax.z + epsilon &&
                   firstMax.z + epsilon >= secondMin.z;
        }

        internal static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 transformedExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, transformedExtents * 2f);
        }

        private static bool TryInverse(Matrix4x4 matrix, out Matrix4x4 inverse)
        {
            inverse = Matrix4x4.identity;
            if (!IsFinite(matrix) || Mathf.Abs(matrix.determinant) <= 0.000000000001f)
                return false;
            inverse = matrix.inverse;
            return IsFinite(inverse);
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++)
            {
                float value = matrix[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool Fail(
            RealBlendBoundedBooleanStatus status,
            string message,
            ref RealBlendBoundedBooleanReport report,
            out string error,
            Stopwatch stopwatch = null)
        {
            report.status = status;
            if (stopwatch != null)
                report.elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            error = message;
            return false;
        }

        private static void DestroyTemporary(Mesh mesh)
        {
            if (mesh == null)
                return;
#if UNITY_EDITOR
            UnityEngine.Object.DestroyImmediate(mesh);
#else
            UnityEngine.Object.Destroy(mesh);
#endif
        }

        private static Mesh CreateBoxMesh(BoxCollider collider, Matrix4x4 worldToTarget)
        {
            Matrix4x4 toTarget = worldToTarget * collider.transform.localToWorldMatrix;
            Vector3 half = collider.size * 0.5f;
            Vector3 center = collider.center;
            Vector3[] vertices =
            {
                toTarget.MultiplyPoint3x4(center + new Vector3(-half.x, -half.y, -half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3( half.x, -half.y, -half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3( half.x,  half.y, -half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3(-half.x,  half.y, -half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3(-half.x, -half.y,  half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3( half.x, -half.y,  half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3( half.x,  half.y,  half.z)),
                toTarget.MultiplyPoint3x4(center + new Vector3(-half.x,  half.y,  half.z))
            };
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            return CreatePrimitiveMesh("RealBlend_BoxColliderCutter", vertices, triangles);
        }

        private static Mesh CreateSphereMesh(
            SphereCollider collider,
            Matrix4x4 worldToTarget,
            int radialSegments,
            int latitudeSegments)
        {
            Vector3 scale = Abs(collider.transform.lossyScale);
            float radius = collider.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            Vector3 center = collider.transform.TransformPoint(collider.center);
            return CreateRoundMesh(
                "RealBlend_SphereColliderCutter",
                center,
                Vector3.up,
                radius,
                0f,
                worldToTarget,
                radialSegments,
                Mathf.Max(2, latitudeSegments / 2));
        }

        private static Mesh CreateCapsuleMesh(
            CapsuleCollider collider,
            Matrix4x4 worldToTarget,
            int radialSegments,
            int hemisphereRings)
        {
            Vector3 scale = Abs(collider.transform.lossyScale);
            Vector3 localAxis;
            float axisScale;
            float radialScale;
            if (collider.direction == 0)
            {
                localAxis = Vector3.right;
                axisScale = scale.x;
                radialScale = Mathf.Max(scale.y, scale.z);
            }
            else if (collider.direction == 2)
            {
                localAxis = Vector3.forward;
                axisScale = scale.z;
                radialScale = Mathf.Max(scale.x, scale.y);
            }
            else
            {
                localAxis = Vector3.up;
                axisScale = scale.y;
                radialScale = Mathf.Max(scale.x, scale.z);
            }
            Vector3 axis = collider.transform.TransformDirection(localAxis).normalized;
            Vector3 center = collider.transform.TransformPoint(collider.center);
            float radius = Mathf.Max(MinimumEpsilon, collider.radius * radialScale);
            float halfHeight = Mathf.Max(radius, collider.height * axisScale * 0.5f);
            float cylinderHalfLength = Mathf.Max(0f, halfHeight - radius);
            return CreateRoundMesh(
                "RealBlend_CapsuleColliderCutter",
                center,
                axis,
                radius,
                cylinderHalfLength,
                worldToTarget,
                radialSegments,
                hemisphereRings);
        }

        private static Mesh CreateRoundMesh(
            string name,
            Vector3 worldCenter,
            Vector3 worldAxis,
            float radius,
            float cylinderHalfLength,
            Matrix4x4 worldToTarget,
            int radialSegments,
            int hemisphereRings)
        {
            radialSegments = Mathf.Clamp(radialSegments, 8, 64);
            hemisphereRings = Mathf.Clamp(hemisphereRings, 2, 24);
            Vector3 axis = worldAxis.sqrMagnitude > MinimumEpsilon
                ? worldAxis.normalized
                : Vector3.up;
            Vector3 tangent = Vector3.Cross(
                Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right,
                axis).normalized;
            Vector3 bitangent = Vector3.Cross(axis, tangent).normalized;
            List<Vector3> vertices = new List<Vector3>(
                2 + radialSegments * hemisphereRings * 2);
            List<int> triangles = new List<int>(
                radialSegments * hemisphereRings * 12);

            vertices.Add(worldToTarget.MultiplyPoint3x4(
                worldCenter - axis * (cylinderHalfLength + radius)));
            List<int> rings = new List<int>();
            for (int ring = 1; ring <= hemisphereRings; ring++)
            {
                float theta = -Mathf.PI * 0.5f +
                              (Mathf.PI * 0.5f * ring / hemisphereRings);
                rings.Add(AddRoundRing(
                    vertices,
                    worldCenter - axis * cylinderHalfLength,
                    axis,
                    tangent,
                    bitangent,
                    radius,
                    theta,
                    radialSegments,
                    worldToTarget));
            }
            if (cylinderHalfLength > MinimumEpsilon)
            {
                rings.Add(AddRoundRing(
                    vertices,
                    worldCenter + axis * cylinderHalfLength,
                    axis,
                    tangent,
                    bitangent,
                    radius,
                    0f,
                    radialSegments,
                    worldToTarget));
            }
            for (int ring = 1; ring < hemisphereRings; ring++)
            {
                float theta = Mathf.PI * 0.5f * ring / hemisphereRings;
                rings.Add(AddRoundRing(
                    vertices,
                    worldCenter + axis * cylinderHalfLength,
                    axis,
                    tangent,
                    bitangent,
                    radius,
                    theta,
                    radialSegments,
                    worldToTarget));
            }
            int top = vertices.Count;
            vertices.Add(worldToTarget.MultiplyPoint3x4(
                worldCenter + axis * (cylinderHalfLength + radius)));

            int firstRing = rings[0];
            for (int segment = 0; segment < radialSegments; segment++)
            {
                int next = (segment + 1) % radialSegments;
                triangles.Add(0);
                triangles.Add(firstRing + next);
                triangles.Add(firstRing + segment);
            }
            for (int ring = 0; ring + 1 < rings.Count; ring++)
            {
                int lower = rings[ring];
                int upper = rings[ring + 1];
                for (int segment = 0; segment < radialSegments; segment++)
                {
                    int next = (segment + 1) % radialSegments;
                    triangles.Add(lower + segment);
                    triangles.Add(lower + next);
                    triangles.Add(upper + next);
                    triangles.Add(lower + segment);
                    triangles.Add(upper + next);
                    triangles.Add(upper + segment);
                }
            }
            int lastRing = rings[rings.Count - 1];
            for (int segment = 0; segment < radialSegments; segment++)
            {
                int next = (segment + 1) % radialSegments;
                triangles.Add(lastRing + segment);
                triangles.Add(lastRing + next);
                triangles.Add(top);
            }
            return CreatePrimitiveMesh(name, vertices.ToArray(), triangles.ToArray());
        }

        private static int AddRoundRing(
            List<Vector3> vertices,
            Vector3 center,
            Vector3 axis,
            Vector3 tangent,
            Vector3 bitangent,
            float radius,
            float theta,
            int radialSegments,
            Matrix4x4 worldToTarget)
        {
            int start = vertices.Count;
            float ringRadius = Mathf.Cos(theta) * radius;
            Vector3 ringCenter = center + axis * (Mathf.Sin(theta) * radius);
            for (int segment = 0; segment < radialSegments; segment++)
            {
                float angle = Mathf.PI * 2f * segment / radialSegments;
                Vector3 world = ringCenter +
                    (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) *
                    ringRadius;
                vertices.Add(worldToTarget.MultiplyPoint3x4(world));
            }
            return start;
        }

        private static Mesh CreatePrimitiveMesh(
            string name,
            Vector3[] vertices,
            int[] triangles)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFinite(vertices[i]))
                    return null;
            }
            Mesh mesh = null;
            try
            {
                mesh = new Mesh { name = name };
                if (vertices.Length > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                return mesh;
            }
            catch
            {
                DestroyTemporary(mesh);
                throw;
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private enum IntersectionKind
        {
            None,
            Segment,
            CoplanarOverlap,
            Ambiguous
        }

        private enum PointRelation
        {
            Outside,
            Inside,
            Boundary,
            Ambiguous
        }

        private readonly struct TriangleIntersection
        {
            public static readonly TriangleIntersection None =
                new TriangleIntersection(IntersectionKind.None, default, default);
            public static readonly TriangleIntersection Coplanar =
                new TriangleIntersection(IntersectionKind.CoplanarOverlap, default, default);
            public static readonly TriangleIntersection Ambiguous =
                new TriangleIntersection(IntersectionKind.Ambiguous, default, default);

            public readonly IntersectionKind kind;
            public readonly Vector3 first;
            public readonly Vector3 second;

            public TriangleIntersection(
                IntersectionKind kind,
                Vector3 first,
                Vector3 second)
            {
                this.kind = kind;
                this.first = first;
                this.second = second;
            }
        }

        private readonly struct CutSegment
        {
            public readonly Vector3 first;
            public readonly Vector3 second;

            public CutSegment(Vector3 first, Vector3 second)
            {
                this.first = first;
                this.second = second;
            }
        }

        private readonly struct FragmentVertex
        {
            public readonly Vector3 position;
            public readonly Vector3 barycentric;

            public FragmentVertex(Vector3 position, Vector3 barycentric)
            {
                this.position = position;
                this.barycentric = barycentric;
            }

            public static FragmentVertex Lerp(
                FragmentVertex first,
                FragmentVertex second,
                float t)
            {
                return new FragmentVertex(
                    Vector3.LerpUnclamped(first.position, second.position, t),
                    Vector3.LerpUnclamped(first.barycentric, second.barycentric, t));
            }
        }

        private sealed class PlanarInputSegment
        {
            public readonly Vector3 first3D;
            public readonly Vector3 second3D;
            public readonly Vector2 first2D;
            public readonly Vector2 second2D;
            public readonly List<float> parameters = new List<float> { 0f, 1f };

            public PlanarInputSegment(
                Vector3 first3D,
                Vector3 second3D,
                Vector2 first2D,
                Vector2 second2D)
            {
                this.first3D = first3D;
                this.second3D = second3D;
                this.first2D = first2D;
                this.second2D = second2D;
            }

            public void AddParameter(float value, float tolerance)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (Mathf.Abs(parameters[i] - value) <= tolerance)
                        return;
                }
                parameters.Add(value);
            }
        }

        private readonly struct ArrangementVertex
        {
            public readonly Vector2 point;
            public readonly FragmentVertex fragment;

            public ArrangementVertex(Vector2 point, FragmentVertex fragment)
            {
                this.point = point;
                this.fragment = fragment;
            }
        }

        private readonly struct ArrangementTriangle
        {
            public readonly int a;
            public readonly int b;
            public readonly int c;

            public ArrangementTriangle(int a, int b, int c)
            {
                this.a = a;
                this.b = b;
                this.c = c;
            }

            public bool ContainsEdge(IndexEdgeKey edge, out int opposite)
            {
                if (edge.Equals(new IndexEdgeKey(a, b)))
                {
                    opposite = c;
                    return true;
                }
                if (edge.Equals(new IndexEdgeKey(b, c)))
                {
                    opposite = a;
                    return true;
                }
                if (edge.Equals(new IndexEdgeKey(c, a)))
                {
                    opposite = b;
                    return true;
                }
                opposite = -1;
                return false;
            }
        }

        private readonly struct IndexEdgeKey : IEquatable<IndexEdgeKey>
        {
            public readonly int first;
            public readonly int second;

            public IndexEdgeKey(int first, int second)
            {
                this.first = Mathf.Min(first, second);
                this.second = Mathf.Max(first, second);
            }

            public bool Equals(IndexEdgeKey other)
            {
                return first == other.first && second == other.second;
            }

            public override bool Equals(object obj)
            {
                return obj is IndexEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked { return first * 397 ^ second; }
            }
        }

        private readonly struct VertexParameter
        {
            public readonly int vertex;
            public readonly float parameter;

            public VertexParameter(int vertex, float parameter)
            {
                this.vertex = vertex;
                this.parameter = parameter;
            }
        }

        private sealed class VertexParameterComparer : IComparer<VertexParameter>
        {
            public static readonly VertexParameterComparer Instance =
                new VertexParameterComparer();

            public int Compare(VertexParameter first, VertexParameter second)
            {
                int comparison = first.parameter.CompareTo(second.parameter);
                return comparison != 0
                    ? comparison
                    : first.vertex.CompareTo(second.vertex);
            }
        }

        private sealed class ArrangementVertexRegistry
        {
            private readonly List<ArrangementVertex> vertices;
            private readonly Dictionary<SpatialKey, List<int>> cells =
                new Dictionary<SpatialKey, List<int>>();
            private readonly float tolerance;
            private readonly float squaredTolerance;
            private readonly SourceTriangle triangle;
            private readonly int droppedAxis;
            private readonly WorkBudget budget;

            public ArrangementVertexRegistry(
                List<ArrangementVertex> vertices,
                float tolerance,
                SourceTriangle triangle,
                int droppedAxis,
                WorkBudget budget)
            {
                this.vertices = vertices;
                this.tolerance = Mathf.Max(MinimumEpsilon, tolerance);
                squaredTolerance = this.tolerance * this.tolerance;
                this.triangle = triangle;
                this.droppedAxis = droppedAxis;
                this.budget = budget;
            }

            public bool TryGetOrAdd(
                Vector3 position,
                out int index,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                SpatialKey center = SpatialKey.FromPoint(position, tolerance);
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            SpatialKey key = center.Offset(x, y, z);
                            if (!cells.TryGetValue(key, out List<int> values))
                                continue;
                            for (int i = 0; i < values.Count; i++)
                            {
                                int candidate = values[i];
                                if ((vertices[candidate].fragment.position - position).sqrMagnitude <=
                                    squaredTolerance)
                                {
                                    index = candidate;
                                    status = RealBlendBoundedBooleanStatus.Success;
                                    error = string.Empty;
                                    return true;
                                }
                            }
                        }
                    }
                }

                if (vertices.Count >= budget.Limits.maxArrangementVerticesPerTriangle)
                {
                    index = -1;
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "One affected triangle exceeded its finite-arrangement vertex budget (" +
                            budget.Limits.maxArrangementVerticesPerTriangle + ").";
                    return false;
                }
                if (!budget.AddArrangementVertex(out status, out error))
                {
                    index = -1;
                    return false;
                }
                index = vertices.Count;
                vertices.Add(new ArrangementVertex(
                    Project(position, droppedAxis),
                    new FragmentVertex(position, ComputeBarycentric(position, triangle))));
                if (!cells.TryGetValue(center, out List<int> cell))
                {
                    cell = new List<int>(1);
                    cells.Add(center, cell);
                }
                cell.Add(index);
                return true;
            }
        }

        private readonly struct FragmentTriangle
        {
            public readonly FragmentVertex first;
            public readonly FragmentVertex second;
            public readonly FragmentVertex third;
            public readonly int region;
            public readonly float constraintClearance;

            public Vector3 Centroid =>
                (first.position + second.position + third.position) / 3f;
            public Vector3 Normal
            {
                get
                {
                    Vector3 value = Vector3.Cross(
                        second.position - first.position,
                        third.position - first.position);
                    return value.sqrMagnitude > MinimumEpsilon * MinimumEpsilon
                        ? value.normalized
                        : Vector3.up;
                }
            }
            public float AreaSquared => Vector3.Cross(
                second.position - first.position,
                third.position - first.position).sqrMagnitude;

            public FragmentTriangle(
                FragmentVertex first,
                FragmentVertex second,
                FragmentVertex third,
                int region = 0,
                float constraintClearance = float.PositiveInfinity)
            {
                this.first = first;
                this.second = second;
                this.third = third;
                this.region = region;
                this.constraintClearance = constraintClearance;
            }

            public static FragmentTriangle FromSource(SourceTriangle triangle)
            {
                return new FragmentTriangle(
                    new FragmentVertex(triangle.a, new Vector3(1f, 0f, 0f)),
                    new FragmentVertex(triangle.b, new Vector3(0f, 1f, 0f)),
                    new FragmentVertex(triangle.c, new Vector3(0f, 0f, 1f)));
            }
        }

        private sealed class Operand
        {
            public readonly SourceTriangle[] triangles;
            public readonly Bounds bounds;
            public readonly AttributeSource attributes;
            public readonly double signedVolume;

            public Operand(
                Mesh mesh,
                Matrix4x4 transform,
                SourceTriangle[] triangles,
                Bounds bounds,
                double signedVolume)
            {
                this.triangles = triangles;
                this.bounds = bounds;
                this.signedVolume = signedVolume;
                attributes = new AttributeSource(mesh, transform);
            }
        }

        private readonly struct SourceTriangle
        {
            public readonly int index;
            public readonly int submesh;
            public readonly int ia;
            public readonly int ib;
            public readonly int ic;
            public readonly Vector3 a;
            public readonly Vector3 b;
            public readonly Vector3 c;
            public readonly Vector3 normal;
            public readonly Vector3 min;
            public readonly Vector3 max;
            public readonly Vector3 centroid;
            public readonly Bounds bounds;
            public readonly float longestEdge;

            public SourceTriangle(
                int index,
                int submesh,
                int ia,
                int ib,
                int ic,
                Vector3 a,
                Vector3 b,
                Vector3 c)
            {
                this.index = index;
                this.submesh = submesh;
                this.ia = ia;
                this.ib = ib;
                this.ic = ic;
                this.a = a;
                this.b = b;
                this.c = c;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                normal = cross.normalized;
                min = Vector3.Min(a, Vector3.Min(b, c));
                max = Vector3.Max(a, Vector3.Max(b, c));
                centroid = (a + b + c) / 3f;
                bounds = new Bounds((min + max) * 0.5f, max - min);
                longestEdge = Mathf.Sqrt(Mathf.Max(
                    (b - a).sqrMagnitude,
                    Mathf.Max((c - a).sqrMagnitude, (c - b).sqrMagnitude)));
            }
        }

        private sealed class AttributeSource
        {
            private readonly Vector3[] normals;
            private readonly Vector4[] tangents;
            private readonly Color[] colors;
            private readonly UvSource[] uvs = new UvSource[8];
            private readonly Matrix4x4 normalMatrix;
            private readonly Matrix4x4 vectorMatrix;

            public readonly bool hasNormals;
            public readonly bool hasTangents;
            public readonly bool hasColors;
            public readonly bool colorsWereUNorm8;

            public AttributeSource(Mesh mesh, Matrix4x4 transform)
            {
                vectorMatrix = transform;
                normalMatrix = transform.inverse.transpose;
                int count = mesh.vertexCount;
                normals = mesh.normals;
                hasNormals = normals != null && normals.Length == count;
                tangents = mesh.tangents;
                hasTangents = tangents != null && tangents.Length == count;
                colors = mesh.colors;
                hasColors = colors != null && colors.Length == count;
                colorsWereUNorm8 = hasColors &&
                    mesh.GetVertexAttributeFormat(VertexAttribute.Color) ==
                    VertexAttributeFormat.UNorm8;
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    VertexAttribute attribute =
                        (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!mesh.HasVertexAttribute(attribute))
                        continue;
                    List<Vector4> values = new List<Vector4>();
                    mesh.GetUVs(channel, values);
                    if (values.Count != count)
                        continue;
                    uvs[channel] = new UvSource
                    {
                        dimension = Mathf.Clamp(
                            mesh.GetVertexAttributeDimension(attribute),
                            2,
                            4),
                        values = values.ToArray()
                    };
                }
            }

            public int GetUvDimension(int channel)
            {
                return channel >= 0 && channel < uvs.Length && uvs[channel] != null
                    ? uvs[channel].dimension
                    : 0;
            }

            public Vector3 SampleNormal(
                SourceTriangle triangle,
                Vector3 barycentric,
                bool reverse)
            {
                Vector3 value = hasNormals
                    ? normals[triangle.ia] * barycentric.x +
                      normals[triangle.ib] * barycentric.y +
                      normals[triangle.ic] * barycentric.z
                    : triangle.normal;
                if (hasNormals)
                    value = normalMatrix.MultiplyVector(value);
                if (value.sqrMagnitude <= MinimumEpsilon * MinimumEpsilon)
                    value = triangle.normal;
                value.Normalize();
                return reverse ? -value : value;
            }

            public Vector4 SampleTangent(
                SourceTriangle triangle,
                Vector3 barycentric,
                Vector3 outputNormal,
                bool reverse)
            {
                if (!hasTangents)
                    return CreateTangent(outputNormal);
                Vector4 source = tangents[triangle.ia] * barycentric.x +
                                 tangents[triangle.ib] * barycentric.y +
                                 tangents[triangle.ic] * barycentric.z;
                Vector3 direction = vectorMatrix.MultiplyVector(
                    new Vector3(source.x, source.y, source.z));
                direction -= outputNormal * Vector3.Dot(direction, outputNormal);
                if (direction.sqrMagnitude <= MinimumEpsilon * MinimumEpsilon)
                    return CreateTangent(outputNormal);
                direction.Normalize();
                float handedness = source.w >= 0f ? 1f : -1f;
                if (reverse) handedness = -handedness;
                return new Vector4(
                    direction.x,
                    direction.y,
                    direction.z,
                    handedness);
            }

            public Color SampleColor(
                SourceTriangle triangle,
                Vector3 barycentric)
            {
                return hasColors
                    ? colors[triangle.ia] * barycentric.x +
                      colors[triangle.ib] * barycentric.y +
                      colors[triangle.ic] * barycentric.z
                    : Color.white;
            }

            public Vector4 SampleUv(
                int channel,
                SourceTriangle triangle,
                Vector3 barycentric)
            {
                UvSource source = channel >= 0 && channel < uvs.Length
                    ? uvs[channel]
                    : null;
                if (source == null)
                    return Vector4.zero;
                return source.values[triangle.ia] * barycentric.x +
                       source.values[triangle.ib] * barycentric.y +
                       source.values[triangle.ic] * barycentric.z;
            }

            private sealed class UvSource
            {
                public int dimension;
                public Vector4[] values;
            }
        }

        private sealed class OutputBuilder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector4> tangents;
            private readonly List<Color> colors = new List<Color>();
            private readonly List<Vector4>[] uvs = new List<Vector4>[8];
            private readonly int[] uvDimensions = new int[8];
            private readonly List<int>[] submeshIndices;
            private readonly List<bool>[] cutSurfaceFlags;
            private readonly Color cutSurfaceColor;
            private readonly float topologyTolerance;
            private readonly RealBlendBoundedBooleanLimits limits;
            private readonly WorkBudget budget;
            private readonly bool colorsWereUNorm8;
            private int triangleCount;
            private int cutSurfaceTriangleCount;

            public int VertexCount => positions.Count;
            public int TriangleCount => triangleCount;
            public int CutSurfaceTriangleCount => cutSurfaceTriangleCount;

            public OutputBuilder(
                AttributeSource target,
                int submeshCount,
                Color cutSurfaceColor,
                float topologyTolerance,
                RealBlendBoundedBooleanLimits limits,
                WorkBudget budget)
            {
                this.cutSurfaceColor = cutSurfaceColor;
                this.topologyTolerance = Mathf.Max(MinimumEpsilon, topologyTolerance);
                this.limits = limits;
                this.budget = budget;
                if (submeshCount < 1 ||
                    submeshCount > limits.maxOutputSubmeshes)
                    throw new ArgumentOutOfRangeException(
                        nameof(submeshCount),
                        "The Boolean output submesh count is outside its safety limit.");
                colorsWereUNorm8 = target.colorsWereUNorm8;
                tangents = target.hasTangents ? new List<Vector4>() : null;
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    int dimension = target.GetUvDimension(channel);
                    if (dimension == 0)
                        continue;
                    uvDimensions[channel] = dimension;
                    uvs[channel] = new List<Vector4>();
                }
                submeshIndices = new List<int>[submeshCount];
                cutSurfaceFlags = new List<bool>[submeshCount];
                for (int i = 0; i < submeshIndices.Length; i++)
                {
                    submeshIndices[i] = new List<int>();
                    cutSurfaceFlags[i] = new List<bool>();
                }
            }

            public bool TryAddTriangle(
                AttributeSource attributes,
                SourceTriangle source,
                FragmentTriangle fragment,
                int submesh,
                bool cutSurface,
                bool reverse,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                if (triangleCount + 1 > limits.maxOutputTriangles ||
                    positions.Count + 3 > limits.maxOutputVertices)
                {
                    status = RealBlendBoundedBooleanStatus.OutputBudgetExceeded;
                    error = "The Boolean output reached its hard triangle/vertex safety limit.";
                    return false;
                }
                if (submesh < 0 || submesh >= submeshIndices.Length)
                {
                    status = RealBlendBoundedBooleanStatus.InvalidInput;
                    error = "A Boolean triangle referenced output submesh " + submesh +
                            ", but the output has " + submeshIndices.Length + " submeshes.";
                    return false;
                }
                int start = positions.Count;
                if (reverse)
                {
                    AddVertex(attributes, source, fragment.first, cutSurface, true);
                    AddVertex(attributes, source, fragment.third, cutSurface, true);
                    AddVertex(attributes, source, fragment.second, cutSurface, true);
                }
                else
                {
                    AddVertex(attributes, source, fragment.first, cutSurface, false);
                    AddVertex(attributes, source, fragment.second, cutSurface, false);
                    AddVertex(attributes, source, fragment.third, cutSurface, false);
                }
                submeshIndices[submesh].Add(start);
                submeshIndices[submesh].Add(start + 1);
                submeshIndices[submesh].Add(start + 2);
                cutSurfaceFlags[submesh].Add(cutSurface);
                triangleCount++;
                if (cutSurface)
                    cutSurfaceTriangleCount++;
                return true;
            }

            private void AddVertex(
                AttributeSource attributes,
                SourceTriangle source,
                FragmentVertex fragment,
                bool cutSurface,
                bool reverse)
            {
                positions.Add(fragment.position);
                Vector3 normal = attributes.SampleNormal(
                    source,
                    fragment.barycentric,
                    reverse);
                normals.Add(normal);
                if (tangents != null)
                {
                    tangents.Add(cutSurface
                        ? CreateTangent(normal)
                        : attributes.SampleTangent(
                            source,
                            fragment.barycentric,
                            normal,
                            reverse));
                }
                colors.Add(cutSurface
                    ? cutSurfaceColor
                    : attributes.SampleColor(source, fragment.barycentric));
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] == null)
                        continue;
                    uvs[channel].Add(cutSurface
                        ? channel == 0
                            ? (Vector4)ProjectBoxUv(fragment.position, normal)
                            : Vector4.zero
                        : attributes.SampleUv(channel, source, fragment.barycentric));
                }
            }

            /// <summary>
            /// The finite arrangements are expected to produce a closed,
            /// consistently oriented result directly.  Validate that contract
            /// before allocating a Unity Mesh.  Deliberately do not repair a
            /// same-directed edge here: two same-facing overlapping slivers
            /// look identical to a harmless winding typo in an edge-count-only
            /// pass, and flipping one would merely hide corrupt PSLG coverage.
            /// </summary>
            private bool TryValidateClosedOutput(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                if (triangleCount == 0)
                    return true;
                if (positions.Count != triangleCount * 3)
                {
                    status = RealBlendBoundedBooleanStatus.NumericalFailure;
                    error = "The bounded Boolean output lost its triangle/vertex correspondence.";
                    return false;
                }

                int[] canonical = new int[positions.Count];
                OutputPositionRegistry registry = new OutputPositionRegistry(
                    topologyTolerance);
                for (int i = 0; i < positions.Count; i++)
                {
                    if ((i & 1023) == 0 &&
                        !budget.CheckTime(out status, out error))
                        return false;
                    if (!registry.TryGetOrAdd(positions[i], out canonical[i]))
                    {
                        status = RealBlendBoundedBooleanStatus.NumericalFailure;
                        error = "The bounded Boolean output contains a non-finite position.";
                        return false;
                    }
                }

                Dictionary<IndexEdgeKey, OutputEdgeUse> edges =
                    new Dictionary<IndexEdgeKey, OutputEdgeUse>(
                        Mathf.Min(triangleCount * 2, limits.maxOutputTriangles * 2));
                HashSet<OutputTriangleKey> faces =
                    new HashSet<OutputTriangleKey>();
                int visitedTriangles = 0;
                for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
                {
                    List<int> indices = submeshIndices[submesh];
                    if ((indices.Count % 3) != 0)
                    {
                        status = RealBlendBoundedBooleanStatus.NumericalFailure;
                        error = "An output submesh contains an incomplete triangle.";
                        return false;
                    }
                    for (int offset = 0; offset < indices.Count; offset += 3)
                    {
                        if ((visitedTriangles++ & 1023) == 0 &&
                            !budget.CheckTime(out status, out error))
                            return false;
                        int firstIndex = indices[offset];
                        int secondIndex = indices[offset + 1];
                        int thirdIndex = indices[offset + 2];
                        if ((uint)firstIndex >= (uint)canonical.Length ||
                            (uint)secondIndex >= (uint)canonical.Length ||
                            (uint)thirdIndex >= (uint)canonical.Length)
                        {
                            status = RealBlendBoundedBooleanStatus.NumericalFailure;
                            error = "An output triangle contains an invalid vertex index.";
                            return false;
                        }
                        int first = canonical[firstIndex];
                        int second = canonical[secondIndex];
                        int third = canonical[thirdIndex];
                        if (first == second || second == third || third == first)
                        {
                            status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                            error = "Topology welding collapsed an output triangle.";
                            return false;
                        }
                        if (!faces.Add(new OutputTriangleKey(first, second, third)))
                        {
                            status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                            error = "The finite arrangements emitted overlapping duplicate faces.";
                            return false;
                        }
                        AddOutputEdgeUse(edges, first, second);
                        AddOutputEdgeUse(edges, second, third);
                        AddOutputEdgeUse(edges, third, first);
                    }
                }
                if (visitedTriangles != triangleCount)
                {
                    status = RealBlendBoundedBooleanStatus.NumericalFailure;
                    error = "The output triangle counter disagrees with its submesh indices.";
                    return false;
                }

                int checkedEdges = 0;
                foreach (KeyValuePair<IndexEdgeKey, OutputEdgeUse> pair in edges)
                {
                    if ((checkedEdges++ & 2047) == 0 &&
                        !budget.CheckTime(out status, out error))
                        return false;
                    if (pair.Value.count != 2)
                    {
                        status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                        error = "The finite arrangements left an output edge with incidence " +
                                pair.Value.count + " instead of 2.";
                        return false;
                    }
                    if (pair.Value.orientation != 0)
                    {
                        status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                        error = "The finite arrangements emitted two faces in the same direction across a shared edge.";
                        return false;
                    }
                }
                return true;
            }

            private static void AddOutputEdgeUse(
                Dictionary<IndexEdgeKey, OutputEdgeUse> edges,
                int first,
                int second)
            {
                bool forward = first < second;
                IndexEdgeKey key = new IndexEdgeKey(first, second);
                edges.TryGetValue(key, out OutputEdgeUse use);
                use.count++;
                use.orientation += forward ? 1 : -1;
                edges[key] = use;
            }

            private struct OutputEdgeUse
            {
                public int count;
                public int orientation;
            }

            private readonly struct OutputTriangleKey : IEquatable<OutputTriangleKey>
            {
                private readonly int first;
                private readonly int second;
                private readonly int third;

                public OutputTriangleKey(int a, int b, int c)
                {
                    if (a > b) Swap(ref a, ref b);
                    if (b > c) Swap(ref b, ref c);
                    if (a > b) Swap(ref a, ref b);
                    first = a;
                    second = b;
                    third = c;
                }

                public bool Equals(OutputTriangleKey other)
                {
                    return first == other.first &&
                           second == other.second &&
                           third == other.third;
                }

                public override bool Equals(object obj)
                {
                    return obj is OutputTriangleKey other && Equals(other);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = first;
                        hash = hash * 397 ^ second;
                        return hash * 397 ^ third;
                    }
                }

                private static void Swap(ref int firstValue, ref int secondValue)
                {
                    int value = firstValue;
                    firstValue = secondValue;
                    secondValue = value;
                }
            }

            private sealed class OutputPositionRegistry
            {
                private readonly float cellSize;
                private readonly float squaredTolerance;
                private readonly List<Vector3> values = new List<Vector3>();
                private readonly Dictionary<SpatialKey, List<int>> cells =
                    new Dictionary<SpatialKey, List<int>>();

                public OutputPositionRegistry(float tolerance)
                {
                    cellSize = Mathf.Max(MinimumEpsilon, tolerance);
                    squaredTolerance = cellSize * cellSize;
                }

                public bool TryGetOrAdd(Vector3 point, out int index)
                {
                    index = -1;
                    if (!IsFinite(point))
                        return false;
                    SpatialKey center = SpatialKey.FromPoint(point, cellSize);
                    for (int x = -1; x <= 1; x++)
                    {
                        for (int y = -1; y <= 1; y++)
                        {
                            for (int z = -1; z <= 1; z++)
                            {
                                SpatialKey key = center.Offset(x, y, z);
                                if (!cells.TryGetValue(key, out List<int> candidates))
                                    continue;
                                for (int candidate = 0;
                                     candidate < candidates.Count;
                                     candidate++)
                                {
                                    int existing = candidates[candidate];
                                    if ((values[existing] - point).sqrMagnitude >
                                        squaredTolerance)
                                        continue;
                                    index = existing;
                                    return true;
                                }
                            }
                        }
                    }
                    index = values.Count;
                    values.Add(point);
                    if (!cells.TryGetValue(center, out List<int> cell))
                    {
                        cell = new List<int>(1);
                        cells.Add(center, cell);
                    }
                    cell.Add(index);
                    return true;
                }
            }

            public bool TryCreateMesh(
                string name,
                bool leaveCutOpen,
                int minimumSubmeshCount,
                out Mesh mesh,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                mesh = null;
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                if (positions.Count > limits.maxOutputVertices ||
                    triangleCount > limits.maxOutputTriangles)
                {
                    status = RealBlendBoundedBooleanStatus.OutputBudgetExceeded;
                    error = "The Boolean output exceeded its hard safety budget.";
                    return false;
                }
                if (!TryValidateClosedOutput(out status, out error))
                    return false;

                List<Vector3> meshPositions = positions;
                List<Vector3> meshNormals = normals;
                List<Vector4> meshTangents = tangents;
                List<Color> meshColors = colors;
                List<Vector4>[] meshUvs = uvs;
                List<int>[] meshSubmeshIndices = submeshIndices;
                if (leaveCutOpen &&
                    !TryBuildOpenSurfaceData(
                        minimumSubmeshCount,
                        out meshPositions,
                        out meshNormals,
                        out meshTangents,
                        out meshColors,
                        out meshUvs,
                        out meshSubmeshIndices,
                        out error))
                {
                    status = RealBlendBoundedBooleanStatus.NumericalFailure;
                    return false;
                }

                Mesh output = null;
                try
                {
                    output = new Mesh { name = name };
                    if (meshPositions.Count > 65535)
                        output.indexFormat = IndexFormat.UInt32;
                    output.SetVertices(meshPositions);
                    output.SetNormals(meshNormals);
                    if (meshTangents != null)
                        output.SetTangents(meshTangents);
                    if (colorsWereUNorm8)
                    {
                        List<Color32> color32 =
                            new List<Color32>(meshColors.Count);
                        for (int i = 0; i < meshColors.Count; i++)
                            color32.Add(meshColors[i]);
                        output.SetColors(color32);
                    }
                    else
                    {
                        output.SetColors(meshColors);
                    }
                    for (int channel = 0; channel < meshUvs.Length; channel++)
                    {
                        if (meshUvs[channel] == null)
                            continue;
                        if (uvDimensions[channel] == 2)
                        {
                            List<Vector2> values =
                                new List<Vector2>(meshUvs[channel].Count);
                            for (int i = 0; i < meshUvs[channel].Count; i++)
                                values.Add(new Vector2(
                                    meshUvs[channel][i].x,
                                    meshUvs[channel][i].y));
                            output.SetUVs(channel, values);
                        }
                        else if (uvDimensions[channel] == 3)
                        {
                            List<Vector3> values =
                                new List<Vector3>(meshUvs[channel].Count);
                            for (int i = 0; i < meshUvs[channel].Count; i++)
                                values.Add(new Vector3(
                                    meshUvs[channel][i].x,
                                    meshUvs[channel][i].y,
                                    meshUvs[channel][i].z));
                            output.SetUVs(channel, values);
                        }
                        else
                        {
                            output.SetUVs(channel, meshUvs[channel]);
                        }
                    }
                    output.subMeshCount = meshSubmeshIndices.Length;
                    for (int submesh = 0;
                         submesh < meshSubmeshIndices.Length;
                         submesh++)
                    {
                        output.SetTriangles(
                            meshSubmeshIndices[submesh],
                            submesh,
                            false);
                    }
                    output.RecalculateBounds();
                    mesh = output;
                    return true;
                }
                catch
                {
                    DestroyTemporary(output);
                    throw;
                }
            }

            private bool TryBuildOpenSurfaceData(
                int minimumSubmeshCount,
                out List<Vector3> outputPositions,
                out List<Vector3> outputNormals,
                out List<Vector4> outputTangents,
                out List<Color> outputColors,
                out List<Vector4>[] outputUvs,
                out List<int>[] outputSubmeshIndices,
                out string error)
            {
                outputPositions = null;
                outputNormals = null;
                outputTangents = null;
                outputColors = null;
                outputUvs = null;
                outputSubmeshIndices = null;
                error = string.Empty;

                int highestRetainedSubmesh = -1;
                for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
                {
                    if (cutSurfaceFlags[submesh].Count * 3 !=
                        submeshIndices[submesh].Count)
                    {
                        error = "Boolean cut provenance no longer matches the output triangles.";
                        return false;
                    }
                    for (int triangle = 0;
                         triangle < cutSurfaceFlags[submesh].Count;
                         triangle++)
                    {
                        if (!cutSurfaceFlags[submesh][triangle])
                        {
                            highestRetainedSubmesh = submesh;
                            break;
                        }
                    }
                }

                int retainedSubmeshCount = Mathf.Clamp(
                    Mathf.Max(minimumSubmeshCount, highestRetainedSubmesh + 1),
                    1,
                    submeshIndices.Length);
                int retainedVertexCapacity = Mathf.Max(
                    0,
                    (triangleCount - cutSurfaceTriangleCount) * 3);
                outputPositions = new List<Vector3>(retainedVertexCapacity);
                outputNormals = new List<Vector3>(retainedVertexCapacity);
                outputTangents = tangents != null
                    ? new List<Vector4>(retainedVertexCapacity)
                    : null;
                outputColors = new List<Color>(retainedVertexCapacity);
                outputUvs = new List<Vector4>[uvs.Length];
                for (int channel = 0; channel < uvs.Length; channel++)
                {
                    if (uvs[channel] != null)
                    {
                        outputUvs[channel] =
                            new List<Vector4>(retainedVertexCapacity);
                    }
                }
                outputSubmeshIndices = new List<int>[retainedSubmeshCount];
                int[] remap = new int[positions.Count];
                for (int i = 0; i < remap.Length; i++)
                    remap[i] = -1;

                for (int submesh = 0;
                     submesh < retainedSubmeshCount;
                     submesh++)
                {
                    List<int> destination = new List<int>();
                    List<int> sourceIndices = submeshIndices[submesh];
                    List<bool> sourceFlags = cutSurfaceFlags[submesh];
                    for (int triangle = 0;
                         triangle < sourceFlags.Count;
                         triangle++)
                    {
                        if (sourceFlags[triangle])
                            continue;
                        int triangleStart = triangle * 3;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int sourceIndex = sourceIndices[triangleStart + corner];
                            if ((uint)sourceIndex >= (uint)positions.Count)
                            {
                                error = "A retained Boolean triangle referenced an invalid vertex.";
                                return false;
                            }
                            int destinationIndex = remap[sourceIndex];
                            if (destinationIndex < 0)
                            {
                                destinationIndex = outputPositions.Count;
                                remap[sourceIndex] = destinationIndex;
                                outputPositions.Add(positions[sourceIndex]);
                                outputNormals.Add(normals[sourceIndex]);
                                if (outputTangents != null)
                                    outputTangents.Add(tangents[sourceIndex]);
                                outputColors.Add(colors[sourceIndex]);
                                for (int channel = 0;
                                     channel < outputUvs.Length;
                                     channel++)
                                {
                                    if (outputUvs[channel] != null)
                                    {
                                        outputUvs[channel].Add(
                                            uvs[channel][sourceIndex]);
                                    }
                                }
                            }
                            destination.Add(destinationIndex);
                        }
                    }
                    outputSubmeshIndices[submesh] = destination;
                }
                return true;
            }
        }

        private static Vector4 CreateTangent(Vector3 normal)
        {
            Vector3 tangent = Vector3.Cross(
                Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right,
                normal);
            if (tangent.sqrMagnitude <= MinimumEpsilon * MinimumEpsilon)
                tangent = Vector3.right;
            tangent.Normalize();
            return new Vector4(tangent.x, tangent.y, tangent.z, 1f);
        }

        private static Vector2 ProjectBoxUv(Vector3 position, Vector3 normal)
        {
            Vector3 absolute = Abs(normal);
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
                return new Vector2(position.z, position.y);
            if (absolute.y >= absolute.z)
                return new Vector2(position.x, position.z);
            return new Vector2(position.x, position.y);
        }

        private sealed class PointClassifier
        {
            private static readonly Vector3[] RayDirections =
            {
                new Vector3(0.7385609f, 0.1934966f, 0.6465682f).normalized,
                new Vector3(-0.2345210f, 0.9146830f, 0.3283910f).normalized,
                new Vector3(0.4072390f, -0.5129330f, 0.7557210f).normalized
            };

            private readonly Operand operand;
            private readonly Bvh bvh;
            private readonly float epsilon;
            private readonly WorkBudget budget;
            private readonly List<float> hits = new List<float>(16);
            private readonly int[] traversalStack;

            public PointClassifier(
                Operand operand,
                Bvh bvh,
                float epsilon,
                WorkBudget budget)
            {
                this.operand = operand;
                this.bvh = bvh;
                this.epsilon = epsilon;
                this.budget = budget;
                traversalStack = new int[Mathf.Max(
                    32,
                    budget.Limits.maxTraversalStack)];
            }

            /// <summary>
            /// Resolves the tolerance-boundary case with operation ownership.
            /// Target fragments prefer an outside sample; cutter fragments
            /// prefer an inside sample.  This prevents a numerical seam band
            /// from being discarded by both operands while coplanar areas still
            /// fail earlier in the triangle-pair stage.
            /// </summary>
            public PointRelation ClassifyForDifference(
                Vector3 point,
                Vector3 surfaceNormal,
                bool keepInside,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                PointRelation relation = Classify(point, out status, out error);
                if (relation != PointRelation.Boundary)
                    return relation;

                Vector3 normal = surfaceNormal.sqrMagnitude >
                                 MinimumEpsilon * MinimumEpsilon
                    ? surfaceNormal.normalized
                    : Vector3.up;
                Vector3 tangent = Vector3.Cross(
                    Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right,
                    normal).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
                Vector3[] directions =
                {
                    normal,
                    -normal,
                    tangent,
                    -tangent,
                    bitangent,
                    -bitangent
                };
                PointRelation preferred = keepInside
                    ? PointRelation.Inside
                    : PointRelation.Outside;
                PointRelation alternate = keepInside
                    ? PointRelation.Outside
                    : PointRelation.Inside;
                bool sawAlternate = false;
                string lastAmbiguousError = string.Empty;
                float offset = epsilon * 8f;
                for (int i = 0; i < directions.Length; i++)
                {
                    PointRelation sample = Classify(
                        point + directions[i] * offset,
                        out RealBlendBoundedBooleanStatus sampleStatus,
                        out string sampleError);
                    if (sample == preferred)
                    {
                        status = RealBlendBoundedBooleanStatus.Success;
                        error = string.Empty;
                        return preferred;
                    }
                    if (sample == alternate)
                        sawAlternate = true;
                    if (sample == PointRelation.Ambiguous)
                    {
                        if (sampleStatus == RealBlendBoundedBooleanStatus.RayBudgetExceeded ||
                            sampleStatus == RealBlendBoundedBooleanStatus.TimeBudgetExceeded)
                        {
                            status = sampleStatus;
                            error = sampleError;
                            return PointRelation.Ambiguous;
                        }
                        lastAmbiguousError = sampleError;
                    }
                }
                if (sawAlternate)
                {
                    status = RealBlendBoundedBooleanStatus.Success;
                    error = string.Empty;
                    return alternate;
                }
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = string.IsNullOrEmpty(lastAmbiguousError)
                    ? "A seam sample remained on the classification boundary."
                    : lastAmbiguousError;
                return PointRelation.Ambiguous;
            }

            public PointRelation Classify(
                Vector3 point,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                if (!BoundsContainsExpanded(operand.bounds, point, epsilon))
                    return PointRelation.Outside;
                bool onBoundary = IsOnBoundary(point, out status, out error);
                if (status != RealBlendBoundedBooleanStatus.Success)
                    return PointRelation.Ambiguous;
                if (onBoundary)
                    return PointRelation.Boundary;

                int insideVotes = 0;
                int outsideVotes = 0;
                for (int i = 0; i < RayDirections.Length; i++)
                {
                    if (!CountRayIntersections(
                            point,
                            RayDirections[i],
                            out int count,
                            out status,
                            out error))
                        return PointRelation.Ambiguous;
                    if ((count & 1) == 1) insideVotes++;
                    else outsideVotes++;
                }
                if (insideVotes == RayDirections.Length)
                    return PointRelation.Inside;
                if (outsideVotes == RayDirections.Length)
                    return PointRelation.Outside;
                status = RealBlendBoundedBooleanStatus.CoplanarOrAmbiguous;
                error = "Point-in-solid rays disagreed near an edge or self-intersection.";
                return PointRelation.Ambiguous;
            }

            private bool IsOnBoundary(
                Vector3 point,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                int count = 0;
                traversalStack[count++] = bvh.Root;
                float squared = epsilon * epsilon * 4f;
                while (count > 0)
                {
                    int nodeIndex = traversalStack[--count];
                    BvhNode node = bvh.nodes[nodeIndex];
                    if (!PointInBounds(point, node.min, node.max, epsilon * 2f))
                        continue;
                    if (node.IsLeaf)
                    {
                        for (int i = 0; i < node.count; i++)
                        {
                            if (!budget.AddRayTriangleTest(out status, out error))
                                return false;
                            SourceTriangle triangle =
                                operand.triangles[bvh.order[node.start + i]];
                            float planeDistance = Mathf.Abs(Vector3.Dot(
                                triangle.normal,
                                point - triangle.a));
                            if (planeDistance <= epsilon * 2f &&
                                PointInTriangle(point, triangle, epsilon * 2f) &&
                                (ClosestPointOnTriangle(point, triangle) - point).sqrMagnitude <=
                                squared)
                                return true;
                        }
                    }
                    else
                    {
                        if (count + 2 > traversalStack.Length)
                        {
                            status = RealBlendBoundedBooleanStatus.RayBudgetExceeded;
                            error = "The point-classification traversal stack reached its safety limit.";
                            return false;
                        }
                        traversalStack[count++] = node.left;
                        traversalStack[count++] = node.right;
                    }
                }
                return false;
            }

            private bool CountRayIntersections(
                Vector3 origin,
                Vector3 direction,
                out int intersectionCount,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                intersectionCount = 0;
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                hits.Clear();
                int count = 0;
                traversalStack[count++] = bvh.Root;
                while (count > 0)
                {
                    int nodeIndex = traversalStack[--count];
                    BvhNode node = bvh.nodes[nodeIndex];
                    if (!RayIntersectsBounds(origin, direction, node.min, node.max))
                        continue;
                    if (node.IsLeaf)
                    {
                        for (int i = 0; i < node.count; i++)
                        {
                            if (!budget.AddRayTriangleTest(out status, out error))
                                return false;
                            SourceTriangle triangle =
                                operand.triangles[bvh.order[node.start + i]];
                            if (!RayIntersectsTriangle(
                                    origin,
                                    direction,
                                    triangle,
                                    epsilon,
                                    out float distance))
                                continue;
                            bool duplicate = false;
                            float mergeTolerance = Mathf.Max(
                                epsilon * 4f,
                                distance * 0.000001f);
                            for (int h = 0; h < hits.Count; h++)
                            {
                                if (Mathf.Abs(hits[h] - distance) <= mergeTolerance)
                                {
                                    duplicate = true;
                                    break;
                                }
                            }
                            if (!duplicate)
                                hits.Add(distance);
                        }
                    }
                    else
                    {
                        if (count + 2 > traversalStack.Length)
                        {
                            status = RealBlendBoundedBooleanStatus.RayBudgetExceeded;
                            error = "The ray traversal stack reached its safety limit.";
                            return false;
                        }
                        traversalStack[count++] = node.left;
                        traversalStack[count++] = node.right;
                    }
                }
                intersectionCount = hits.Count;
                return true;
            }
        }

        private static bool BoundsContainsExpanded(
            Bounds bounds,
            Vector3 point,
            float epsilon)
        {
            return PointInBounds(point, bounds.min, bounds.max, epsilon);
        }

        private static bool PointInBounds(
            Vector3 point,
            Vector3 min,
            Vector3 max,
            float epsilon)
        {
            return point.x >= min.x - epsilon && point.x <= max.x + epsilon &&
                   point.y >= min.y - epsilon && point.y <= max.y + epsilon &&
                   point.z >= min.z - epsilon && point.z <= max.z + epsilon;
        }

        private static bool RayIntersectsBounds(
            Vector3 origin,
            Vector3 direction,
            Vector3 min,
            Vector3 max)
        {
            float near = 0f;
            float far = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = direction[axis];
                float o = origin[axis];
                if (Mathf.Abs(d) <= MinimumEpsilon)
                {
                    if (o < min[axis] || o > max[axis])
                        return false;
                    continue;
                }
                float inverse = 1f / d;
                float first = (min[axis] - o) * inverse;
                float second = (max[axis] - o) * inverse;
                if (first > second)
                {
                    float swap = first;
                    first = second;
                    second = swap;
                }
                near = Mathf.Max(near, first);
                far = Mathf.Min(far, second);
                if (near > far)
                    return false;
            }
            return far >= 0f;
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            SourceTriangle triangle,
            float epsilon,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = triangle.b - triangle.a;
            Vector3 edge2 = triangle.c - triangle.a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) <= epsilon * 0.1f)
                return false;
            float inverse = 1f / determinant;
            Vector3 t = origin - triangle.a;
            float u = Vector3.Dot(t, p) * inverse;
            float baryTolerance = epsilon /
                Mathf.Max(MinimumEpsilon, triangle.longestEdge);
            if (u < -baryTolerance || u > 1f + baryTolerance)
                return false;
            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -baryTolerance || u + v > 1f + baryTolerance)
                return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > epsilon * 2f;
        }

        private static Vector3 ClosestPointOnTriangle(
            Vector3 point,
            SourceTriangle triangle)
        {
            Vector3 ab = triangle.b - triangle.a;
            Vector3 ac = triangle.c - triangle.a;
            Vector3 ap = point - triangle.a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return triangle.a;

            Vector3 bp = point - triangle.b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return triangle.b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return triangle.a + ab * (d1 / (d1 - d3));

            Vector3 cp = point - triangle.c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return triangle.c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return triangle.a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return triangle.b +
                       (triangle.c - triangle.b) *
                       ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denominator = 1f / (va + vb + vc);
            float v = vb * denominator;
            float w = vc * denominator;
            return triangle.a + ab * v + ac * w;
        }

        private sealed class Bvh
        {
            public readonly SourceTriangle[] triangles;
            public readonly int[] order;
            public readonly BvhNode[] nodes;
            public int Root => 0;

            public Bvh(SourceTriangle[] triangles)
            {
                this.triangles = triangles;
                order = new int[triangles.Length];
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                List<BvhNode> built = new List<BvhNode>(triangles.Length * 2);
                BuildNode(built, 0, order.Length);
                nodes = built.ToArray();
            }

            private int BuildNode(List<BvhNode> built, int start, int count)
            {
                Vector3 min = triangles[order[start]].min;
                Vector3 max = triangles[order[start]].max;
                Vector3 centroidMin = triangles[order[start]].centroid;
                Vector3 centroidMax = centroidMin;
                for (int i = start + 1; i < start + count; i++)
                {
                    SourceTriangle triangle = triangles[order[i]];
                    min = Vector3.Min(min, triangle.min);
                    max = Vector3.Max(max, triangle.max);
                    centroidMin = Vector3.Min(centroidMin, triangle.centroid);
                    centroidMax = Vector3.Max(centroidMax, triangle.centroid);
                }
                int nodeIndex = built.Count;
                built.Add(new BvhNode(min, max, start, count, -1, -1));
                if (count <= BvhLeafSize)
                    return nodeIndex;

                Vector3 extent = centroidMax - centroidMin;
                int axis = extent.x >= extent.y && extent.x >= extent.z
                    ? 0
                    : extent.y >= extent.z ? 1 : 2;
                Array.Sort(
                    order,
                    start,
                    count,
                    new TriangleCentroidComparer(triangles, axis));
                int firstCount = count / 2;
                int left = BuildNode(built, start, firstCount);
                int right = BuildNode(built, start + firstCount, count - firstCount);
                built[nodeIndex] = new BvhNode(min, max, start, 0, left, right);
                return nodeIndex;
            }
        }

        private readonly struct BvhNode
        {
            public readonly Vector3 min;
            public readonly Vector3 max;
            public readonly int start;
            public readonly int count;
            public readonly int left;
            public readonly int right;

            public bool IsLeaf => left < 0;
            public float SurfaceArea
            {
                get
                {
                    Vector3 size = max - min;
                    return 2f * (size.x * size.y + size.y * size.z + size.z * size.x);
                }
            }

            public BvhNode(
                Vector3 min,
                Vector3 max,
                int start,
                int count,
                int left,
                int right)
            {
                this.min = min;
                this.max = max;
                this.start = start;
                this.count = count;
                this.left = left;
                this.right = right;
            }
        }

        private sealed class TriangleCentroidComparer : IComparer<int>
        {
            private readonly SourceTriangle[] triangles;
            private readonly int axis;

            public TriangleCentroidComparer(SourceTriangle[] triangles, int axis)
            {
                this.triangles = triangles;
                this.axis = axis;
            }

            public int Compare(int first, int second)
            {
                float difference = triangles[first].centroid[axis] -
                                   triangles[second].centroid[axis];
                if (difference < 0f) return -1;
                if (difference > 0f) return 1;
                return first.CompareTo(second);
            }
        }

        private readonly struct NodePair
        {
            public readonly int first;
            public readonly int second;

            public NodePair(int first, int second)
            {
                this.first = first;
                this.second = second;
            }
        }

        private sealed class WorkBudget
        {
            private readonly Stopwatch stopwatch;
            private RealBlendBoundedBooleanReport report;
            private int intermediatePolygons;
            private int arrangementVertices;
            private int arrangementEdges;
            private long arrangementWork;

            public RealBlendBoundedBooleanLimits Limits { get; }
            public int IntersectionSegmentCount => report.intersectionSegments;

            public WorkBudget(
                RealBlendBoundedBooleanLimits limits,
                Stopwatch stopwatch,
                RealBlendBoundedBooleanReport report)
            {
                Limits = limits;
                this.stopwatch = stopwatch;
                this.report = report;
            }

            public void SetReport(RealBlendBoundedBooleanReport value)
            {
                bool tessellated = report.usedColliderTessellation;
                report = value;
                report.usedColliderTessellation = tessellated;
            }

            public RealBlendBoundedBooleanReport GetReport(
                RealBlendBoundedBooleanStatus status)
            {
                report.status = status;
                report.elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return report;
            }

            public bool CheckTime(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                if (Limits.maxMilliseconds > 0 &&
                    stopwatch.ElapsedMilliseconds > Limits.maxMilliseconds)
                {
                    status = RealBlendBoundedBooleanStatus.TimeBudgetExceeded;
                    error = "The bounded Boolean exceeded its " +
                            Limits.maxMilliseconds + " ms time budget and stopped safely.";
                    return false;
                }
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                return true;
            }

            public bool AddCandidate(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                report.broadphaseCandidates++;
                if (report.broadphaseCandidates > Limits.maxCandidatePairs)
                {
                    status = RealBlendBoundedBooleanStatus.CandidateBudgetExceeded;
                    error = "Triangle broadphase exceeded its hard candidate-pair budget (" +
                            Limits.maxCandidatePairs + ").";
                    return false;
                }
                return CheckTime(out status, out error);
            }

            public bool AddIntersectionSegment(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                report.intersectionSegments++;
                if (report.intersectionSegments > Limits.maxIntersectionSegments)
                {
                    status = RealBlendBoundedBooleanStatus.CandidateBudgetExceeded;
                    error = "Triangle intersections exceeded the hard segment budget (" +
                            Limits.maxIntersectionSegments + ").";
                    return false;
                }
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                return true;
            }

            public void AddAffectedTriangle(bool target)
            {
                if (target) report.affectedTargetTriangles++;
                else report.affectedCutterTriangles++;
            }

            public bool AddIntermediatePolygon(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                intermediatePolygons++;
                if (intermediatePolygons > Limits.maxIntermediatePolygons)
                {
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "Triangle partitioning exceeded its hard intermediate-polygon budget.";
                    return false;
                }
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                return true;
            }

            public bool AddArrangementVertex(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                if (arrangementVertices >= Limits.maxArrangementVertices)
                {
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "Finite arrangements reached their hard vertex budget (" +
                            Limits.maxArrangementVertices + ").";
                    return false;
                }
                arrangementVertices++;
                return CheckTime(out status, out error);
            }

            public bool AddArrangementEdge(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                if (arrangementEdges >= Limits.maxArrangementEdges)
                {
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "Finite arrangements reached their hard constraint-edge budget (" +
                            Limits.maxArrangementEdges + ").";
                    return false;
                }
                arrangementEdges++;
                return CheckTime(out status, out error);
            }

            public bool ChargeArrangementWork(
                long count,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                if (count < 0 ||
                    arrangementWork > Limits.maxArrangementWork - count)
                {
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "Finite arrangements reached their hard work budget (" +
                            Limits.maxArrangementWork + ").";
                    return false;
                }
                arrangementWork += count;
                return CheckTime(out status, out error);
            }

            public bool AddFragments(
                int count,
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                report.generatedFragments += count;
                if (report.generatedFragments > Limits.maxFragments)
                {
                    status = RealBlendBoundedBooleanStatus.FragmentBudgetExceeded;
                    error = "Triangle partitioning exceeded its hard fragment budget (" +
                            Limits.maxFragments + ").";
                    return false;
                }
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                return true;
            }

            public bool AddRayTriangleTest(
                out RealBlendBoundedBooleanStatus status,
                out string error)
            {
                report.rayTriangleTests++;
                if (report.rayTriangleTests > Limits.maxRayTriangleTests)
                {
                    status = RealBlendBoundedBooleanStatus.RayBudgetExceeded;
                    error = "Point-in-solid classification exceeded its hard ray-test budget (" +
                            Limits.maxRayTriangleTests + ").";
                    return false;
                }
                if ((report.rayTriangleTests & 4095) == 0)
                    return CheckTime(out status, out error);
                status = RealBlendBoundedBooleanStatus.Success;
                error = string.Empty;
                return true;
            }
        }

        private readonly struct PositionKey : IEquatable<PositionKey>, IComparable<PositionKey>
        {
            private readonly long x;
            private readonly long y;
            private readonly long z;

            public PositionKey(Vector3 value, float tolerance)
            {
                double inverse = 1.0 / Math.Max(MinimumEpsilon, tolerance);
                x = (long)Math.Round(value.x * inverse);
                y = (long)Math.Round(value.y * inverse);
                z = (long)Math.Round(value.z * inverse);
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

        private readonly struct PositionEdgeKey : IEquatable<PositionEdgeKey>
        {
            private readonly PositionKey first;
            private readonly PositionKey second;

            public PositionEdgeKey(PositionKey first, PositionKey second)
            {
                this.first = first;
                this.second = second;
            }

            public PositionEdgeKey(
                Vector3 first,
                Vector3 second,
                float tolerance)
            {
                PositionKey a = new PositionKey(first, tolerance);
                PositionKey b = new PositionKey(second, tolerance);
                if (a.CompareTo(b) <= 0)
                {
                    this.first = a;
                    this.second = b;
                }
                else
                {
                    this.first = b;
                    this.second = a;
                }
            }

            public bool Equals(PositionEdgeKey other)
            {
                return first.Equals(other.first) && second.Equals(other.second);
            }

            public override bool Equals(object obj)
            {
                return obj is PositionEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked { return first.GetHashCode() * 397 ^ second.GetHashCode(); }
            }
        }

        private struct EdgeUse
        {
            public int count;
            public int orientation;
        }

        private sealed class SourceEdgeNodeRecord
        {
            public readonly Vector3 first;
            public readonly Vector3 second;
            public readonly List<int> triangles = new List<int>(2);
            public readonly List<Vector3> nodes = new List<Vector3>();

            public SourceEdgeNodeRecord(Vector3 first, Vector3 second)
            {
                this.first = first;
                this.second = second;
            }
        }

        private sealed class PositionSnapper
        {
            private readonly float cellSize;
            private readonly float squaredTolerance;
            private readonly Dictionary<SpatialKey, List<Vector3>> cells =
                new Dictionary<SpatialKey, List<Vector3>>();

            public PositionSnapper(float tolerance)
            {
                cellSize = Mathf.Max(MinimumEpsilon, tolerance);
                squaredTolerance = cellSize * cellSize;
            }

            public Vector3 Snap(Vector3 point)
            {
                SpatialKey center = SpatialKey.FromPoint(point, cellSize);
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            SpatialKey key = center.Offset(x, y, z);
                            if (!cells.TryGetValue(key, out List<Vector3> values))
                                continue;
                            for (int i = 0; i < values.Count; i++)
                            {
                                if ((values[i] - point).sqrMagnitude <= squaredTolerance)
                                    return values[i];
                            }
                        }
                    }
                }

                if (!cells.TryGetValue(center, out List<Vector3> cell))
                {
                    cell = new List<Vector3>(1);
                    cells.Add(center, cell);
                }
                cell.Add(point);
                return point;
            }
        }

        private readonly struct SpatialKey : IEquatable<SpatialKey>
        {
            private readonly long x;
            private readonly long y;
            private readonly long z;

            private SpatialKey(long x, long y, long z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static SpatialKey FromPoint(Vector3 point, float cellSize)
            {
                double inverse = 1.0 / Math.Max(MinimumEpsilon, cellSize);
                return new SpatialKey(
                    (long)Math.Floor(point.x * inverse),
                    (long)Math.Floor(point.y * inverse),
                    (long)Math.Floor(point.z * inverse));
            }

            public SpatialKey Offset(int offsetX, int offsetY, int offsetZ)
            {
                return new SpatialKey(x + offsetX, y + offsetY, z + offsetZ);
            }

            public bool Equals(SpatialKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is SpatialKey other && Equals(other);
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
    }
}
