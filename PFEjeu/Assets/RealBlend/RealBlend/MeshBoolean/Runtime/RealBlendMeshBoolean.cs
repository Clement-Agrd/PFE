using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    public enum RealBlendBooleanResourceBudget
    {
        Standard = 0,
        Expanded = 1,
        High = 2
    }

    internal static class RealBlendBooleanBudgetUtility
    {
        public static float GetMemoryScale(
            RealBlendBooleanResourceBudget budget)
        {
            switch (budget)
            {
                case RealBlendBooleanResourceBudget.High:
                    return 2f;
                case RealBlendBooleanResourceBudget.Expanded:
                    return 1.5f;
                default:
                    return 1f;
            }
        }

        public static int GetMaximumMilliseconds(
            RealBlendBooleanResourceBudget budget)
        {
            switch (budget)
            {
                case RealBlendBooleanResourceBudget.High:
                    return 60000;
                case RealBlendBooleanResourceBudget.Expanded:
                    return 30000;
                default:
                    return 10000;
            }
        }

        public static int ScaleInt(int value, float scale, int hardMaximum)
        {
            long scaled = (long)Math.Ceiling(Math.Max(1, value) *
                                             Math.Max(1.0, scale));
            return (int)Math.Min(Math.Max(1, hardMaximum), scaled);
        }

        public static long ScaleLong(
            long value,
            float scale,
            long hardMaximum)
        {
            double scaled = Math.Ceiling(Math.Max(1L, value) *
                                         Math.Max(1.0, scale));
            return (long)Math.Min(Math.Max(1L, hardMaximum), scaled);
        }

        public static int GetLegacyOutputTriangleLimit(
            RealBlendBooleanResourceBudget budget)
        {
            switch (budget)
            {
                case RealBlendBooleanResourceBudget.High:
                    return 1000000;
                case RealBlendBooleanResourceBudget.Expanded:
                    return 750000;
                default:
                    return 600000;
            }
        }
    }

    [Serializable]
    public sealed class RealBlendMeshBooleanSettings
    {
        [Min(0.000001f)] public float planeEpsilon = 0.00001f;
        [Min(0.000001f)] public float weldTolerance = 0.00001f;
        [Min(1)] public int maxInputTriangles = 25000;
        public RealBlendBooleanResourceBudget resourceBudget =
            RealBlendBooleanResourceBudget.Expanded;
        [Min(0)] public int cutSurfaceSubmesh;
        public Color cutSurfaceColor = Color.clear;
        // The Boolean is still solved and certified as a closed difference.
        // When enabled, only the cutter-derived faces are removed afterwards,
        // leaving the new boundary open without guessing from materials/colors.
        public bool leaveCutOpen;
        public bool requireClosedTarget = true;
        public bool requireClosedCutter = true;
    }

    public struct RealBlendMeshBooleanReport
    {
        public int targetInputTriangles;
        public int cutterInputTriangles;
        public int outputVertices;
        public int outputTriangles;
        public int cutSurfaceTriangles;
        public int generatedCutSurfaceTriangles;
        public int outputSubmeshes;
        public bool targetCompletelyRemoved;
        public float usedPlaneEpsilon;
        public float usedWeldTolerance;
        public float usedRepairTolerance;
        public int recoveryAttempts;
        public bool inputsAutomaticallyRepaired;
        public bool usedBoundedManifoldSolver;
        public int broadphaseCandidatePairs;
        public int intersectionSegments;
        public long processingMilliseconds;
    }

    /// <summary>
    /// Closed-solid mesh Boolean operations used by the RealBlend sculpt tools.
    /// Operands may have different transforms. Output is always expressed in the
    /// target mesh's local space and preserves target submesh assignments, UV0,
    /// UV2, normals, and vertex colors.
    /// </summary>
    public static class RealBlendMeshBoolean
    {
        private const int Coplanar = 0;
        private const int Front = 1;
        private const int Back = 2;
        private const int Spanning = 3;
        private const float PrecisionFirstTolerance = 0.00001f;
        private const string BooleanSafetyBudgetPrefix =
            "Boolean cut stopped at its safety budget: ";
        private static readonly bool TopologyDiagnosticsEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable("REALBLEND_BOOLEAN_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal);

        public static bool TrySubtract(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            out Mesh result,
            out RealBlendMeshBooleanReport report,
            out string error)
        {
            settings = settings ?? new RealBlendMeshBooleanSettings();
            float requestedPlane = Mathf.Max(0.0000001f, settings.planeEpsilon);
            float requestedWeld = Mathf.Max(requestedPlane, settings.weldTolerance);
            if (requestedPlane > PrecisionFirstTolerance * 1.0001f ||
                requestedWeld > PrecisionFirstTolerance * 1.0001f)
            {
                // Treat authored tolerances as recovery ceilings. Starting with
                // the precise profile avoids coarse operand healing and BSP plane
                // classification from disturbing dense wall/bevel topology. If
                // the input really needs a wider stitch, the authored profile is
                // still tried immediately afterwards.
                RealBlendMeshBooleanSettings precisionSettings =
                    new RealBlendMeshBooleanSettings
                    {
                        planeEpsilon = Mathf.Min(
                            requestedPlane,
                            PrecisionFirstTolerance),
                        weldTolerance = Mathf.Min(
                            requestedWeld,
                            PrecisionFirstTolerance),
                        maxInputTriangles = settings.maxInputTriangles,
                        resourceBudget = settings.resourceBudget,
                        cutSurfaceSubmesh = settings.cutSurfaceSubmesh,
                        cutSurfaceColor = settings.cutSurfaceColor,
                        leaveCutOpen = settings.leaveCutOpen,
                        requireClosedTarget = settings.requireClosedTarget,
                        requireClosedCutter = settings.requireClosedCutter
                    };
                if (TrySubtractConfigured(
                        targetMesh,
                        targetLocalToWorld,
                        cutterMesh,
                        cutterLocalToWorld,
                        precisionSettings,
                        out result,
                        out report,
                        out error))
                {
                    return true;
                }
                DestroyTemporaryMesh(result);
                result = null;
                if (!string.IsNullOrEmpty(error) &&
                    error.StartsWith(
                        BooleanSafetyBudgetPrefix,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return TrySubtractConfigured(
                targetMesh,
                targetLocalToWorld,
                cutterMesh,
                cutterLocalToWorld,
                settings,
                out result,
                out report,
                out error);
        }

        private static bool TrySubtractConfigured(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            out Mesh result,
            out RealBlendMeshBooleanReport report,
            out string error)
        {
            result = null;
            report = default;
            error = string.Empty;
            settings = settings ?? new RealBlendMeshBooleanSettings();

            float requestedEpsilon = Mathf.Max(0.0000001f, settings.planeEpsilon);
            float requestedWeldTolerance = Mathf.Max(
                requestedEpsilon,
                settings.weldTolerance);
            int maxTriangles = Mathf.Clamp(settings.maxInputTriangles, 1, 100000);

            if (!TryValidateMesh(
                    targetMesh,
                    false,
                    maxTriangles,
                    requestedWeldTolerance,
                    "Target",
                    out report.targetInputTriangles,
                    out error))
            {
                return false;
            }

            if (!TryValidateMesh(
                    cutterMesh,
                    false,
                    maxTriangles,
                    requestedWeldTolerance,
                    "Cutter",
                    out report.cutterInputTriangles,
                    out error))
            {
                return false;
            }

            if (!IsFiniteAndInvertible(targetLocalToWorld) ||
                !IsFiniteAndInvertible(cutterLocalToWorld))
            {
                error = "Target and cutter transforms must be finite and have non-zero scale.";
                return false;
            }

            Matrix4x4 cutterToTarget =
                targetLocalToWorld.inverse * cutterLocalToWorld;
            Bounds sourceTargetBounds = targetMesh.bounds;
            Bounds sourceCutterBounds = TransformBounds(
                cutterMesh.bounds,
                cutterToTarget);
            // UI tolerances are upper requests, not permission to merge geometry
            // at that scale. Derive a hard cap from the thinnest operand feature
            // before either operand reaches the automatic solid healer.
            float geometryRepairCeiling = ComputeRepairToleranceCeiling(
                sourceTargetBounds,
                cutterMesh.bounds,
                cutterToTarget);
            float epsilon = Mathf.Max(
                0.0000001f,
                Mathf.Min(requestedEpsilon, geometryRepairCeiling * 0.125f));
            float weldTolerance = Mathf.Max(
                epsilon,
                Mathf.Min(
                    requestedWeldTolerance,
                    geometryRepairCeiling * 0.25f));
            if (!BoundsOverlapInclusive(
                    sourceTargetBounds,
                    sourceCutterBounds,
                    epsilon))
            {
                error = "The cutter does not overlap the target mesh.";
                return false;
            }

            Mesh temporaryTarget = null;
            Mesh temporaryCutter = null;
            Mesh usableTarget = targetMesh;
            Mesh usableCutter = cutterMesh;
            bool inputsRepaired = false;
            try
            {
                if (settings.requireClosedTarget &&
                    !TryPrepareOperand(
                        targetMesh,
                        weldTolerance,
                        "target",
                        out usableTarget,
                        out temporaryTarget,
                        out bool targetRepaired,
                        out error))
                {
                    return false;
                }
                if (settings.requireClosedCutter &&
                    !TryPrepareOperand(
                        cutterMesh,
                        weldTolerance,
                        "cutter",
                        out usableCutter,
                        out temporaryCutter,
                        out bool cutterRepaired,
                        out error))
                {
                    return false;
                }
                inputsRepaired = (temporaryTarget != null) ||
                                 (temporaryCutter != null);

                Bounds targetBounds = usableTarget.bounds;
                Bounds cutterBounds = TransformBounds(
                    usableCutter.bounds,
                    cutterToTarget);
                if (!BoundsOverlapWithVolume(targetBounds, cutterBounds, epsilon))
                {
                    error = "The cutter does not overlap the target's solid volume.";
                    return false;
                }

                Vector3 overlapSize = Vector3.Min(targetBounds.max, cutterBounds.max) -
                                      Vector3.Max(targetBounds.min, cutterBounds.min);
                float narrowestOverlap = Mathf.Min(
                    overlapSize.x,
                    Mathf.Min(overlapSize.y, overlapSize.z));
                // Repair must never be wider than the feature being created. A
                // fixed 0.1 mm weld erased valid 0.05 mm shallow cuts by merging
                // the new floor back into the target face.
                float featureToleranceLimit = Mathf.Min(
                    geometryRepairCeiling,
                    Mathf.Max(0.0000001f, narrowestOverlap * 0.1f));
                float operationEpsilon = Mathf.Min(epsilon, featureToleranceLimit);
                float baseRepairTolerance = Mathf.Max(
                    Mathf.Max(weldTolerance, epsilon * 2f),
                    Mathf.Min(0.0001f, featureToleranceLimit));
                baseRepairTolerance = Mathf.Min(
                    baseRepairTolerance,
                    featureToleranceLimit);
                float maximumRepairTolerance = Mathf.Min(
                    geometryRepairCeiling,
                    Mathf.Min(featureToleranceLimit, baseRepairTolerance * 2.5f));

                // Closed solids first use the bounded BVH/manifold path. It
                // intersects only overlapping triangle pairs and partitions
                // only touched faces, so curved cutters do not feed every face
                // through every cutter plane. Ambiguous coplanar arrangements
                // may still fall back to the compatibility BSP below.
                if (settings.requireClosedTarget &&
                    settings.requireClosedCutter)
                {
                    float boundedValidationTolerance = Mathf.Min(
                        featureToleranceLimit,
                        Mathf.Max(weldTolerance, operationEpsilon * 4f));
                    if (TrySubtractBoundedManifold(
                            usableTarget,
                            targetLocalToWorld,
                            usableCutter,
                            cutterLocalToWorld,
                            settings,
                            operationEpsilon,
                            weldTolerance,
                            boundedValidationTolerance,
                            report.targetInputTriangles,
                            report.cutterInputTriangles,
                            inputsRepaired,
                            out result,
                            out RealBlendMeshBooleanReport boundedReport,
                            out string boundedError,
                            out bool boundedFailureIsTerminal))
                    {
                        report = boundedReport;
                        error = string.Empty;
                        return true;
                    }
                    if (boundedFailureIsTerminal)
                    {
                        report = boundedReport;
                        error = boundedError;
                        return false;
                    }
                }

                bool toleranceWasClamped =
                    requestedEpsilon > epsilon * 1.0001f ||
                    requestedWeldTolerance > weldTolerance * 1.0001f;
                // Oversized UI requests descend before one bounded expansion so
                // every retry remains feature-safe. Already-safe authored values
                // retain the widening sequence needed by dense and repeated cuts.
                float[] epsilonMultipliers = toleranceWasClamped
                    ? new[] { 1f, 0.5f, 0.25f, 2f }
                    : new[] { 1f, 0.25f, 2f, 0.1f };
                float[] repairMultipliers = toleranceWasClamped
                    ? new[] { 1f, 0.5f, 0.25f, 2f }
                    : new[] { 1f, 1.5f, 2.5f, 2.5f };
                string lastError = string.Empty;
                for (int attempt = 0;
                     attempt < epsilonMultipliers.Length;
                     attempt++)
                {
                    float repairTolerance = Mathf.Min(
                        maximumRepairTolerance,
                        baseRepairTolerance * repairMultipliers[attempt]);
                    repairTolerance = Mathf.Max(0.0000001f, repairTolerance);
                    float attemptEpsilon = Mathf.Max(
                        0.0000001f,
                        Mathf.Min(
                            repairTolerance * 0.5f,
                            operationEpsilon * epsilonMultipliers[attempt]));
                    float attemptWeldTolerance = toleranceWasClamped
                        ? Mathf.Max(
                            attemptEpsilon,
                            Mathf.Min(
                                repairTolerance,
                                weldTolerance * repairMultipliers[attempt]))
                        : Mathf.Max(
                            attemptEpsilon,
                            Mathf.Min(
                                repairTolerance,
                                Mathf.Max(
                                    Mathf.Min(weldTolerance, repairTolerance),
                                    Mathf.Min(
                                        repairTolerance * 0.25f,
                                        weldTolerance * 8f))));
                    // Validate with the same positional identity used to build
                    // and repair this candidate. A coarser fixed tolerance can
                    // merge distinct vertices on dense/bevelled walls and report
                    // conflicts that do not exist in the generated topology.
                    float outputValidationTolerance = repairTolerance;
                    if (TrySubtractCore(
                            usableTarget,
                            usableCutter,
                            cutterToTarget,
                            settings,
                            attemptEpsilon,
                            attemptWeldTolerance,
                            repairTolerance,
                            repairTolerance,
                            maxTriangles,
                            attempt,
                            out result,
                            out RealBlendMeshBooleanReport attemptReport,
                            out lastError,
                            out bool retryable))
                    {
                        PopulateAttemptReport(
                            ref attemptReport,
                            report.targetInputTriangles,
                            report.cutterInputTriangles,
                            attemptEpsilon,
                            attemptWeldTolerance,
                            outputValidationTolerance,
                            attempt,
                            inputsRepaired);
                        report = attemptReport;
                        error = string.Empty;
                        return true;
                    }
                    PopulateAttemptReport(
                        ref attemptReport,
                        report.targetInputTriangles,
                        report.cutterInputTriangles,
                        attemptEpsilon,
                        attemptWeldTolerance,
                        repairTolerance,
                        attempt,
                        inputsRepaired);
                    report = attemptReport;
                    if (!retryable)
                        break;
                }

                error = lastError;
                return false;
            }
            finally
            {
                DestroyTemporaryMesh(temporaryTarget);
                DestroyTemporaryMesh(temporaryCutter);
            }
        }

        private static bool TrySubtractBoundedManifold(
            Mesh targetMesh,
            Matrix4x4 targetLocalToWorld,
            Mesh cutterMesh,
            Matrix4x4 cutterLocalToWorld,
            RealBlendMeshBooleanSettings settings,
            float epsilon,
            float weldTolerance,
            float validationTolerance,
            int targetInputTriangles,
            int cutterInputTriangles,
            bool inputsRepaired,
            out Mesh result,
            out RealBlendMeshBooleanReport report,
            out string error,
            out bool terminalFailure)
        {
            result = null;
            report = default;
            error = string.Empty;
            terminalFailure = false;

            RealBlendMeshBooleanSettings boundedSettings =
                new RealBlendMeshBooleanSettings
                {
                    planeEpsilon = Mathf.Max(0.0000001f, epsilon),
                    weldTolerance = Mathf.Max(epsilon, weldTolerance),
                    maxInputTriangles = settings.maxInputTriangles,
                    resourceBudget = settings.resourceBudget,
                    cutSurfaceSubmesh = settings.cutSurfaceSubmesh,
                    cutSurfaceColor = settings.cutSurfaceColor,
                    leaveCutOpen = settings.leaveCutOpen,
                    requireClosedTarget = settings.requireClosedTarget,
                    requireClosedCutter = settings.requireClosedCutter
                };

            bool succeeded = RealBlendBoundedManifoldBoolean.TrySubtract(
                targetMesh,
                targetLocalToWorld,
                cutterMesh,
                cutterLocalToWorld,
                boundedSettings,
                out result,
                out RealBlendBoundedBooleanReport boundedReport,
                out error);

            report.targetInputTriangles = targetInputTriangles;
            report.cutterInputTriangles = cutterInputTriangles;
            report.outputVertices = boundedReport.outputVertices;
            report.outputTriangles = boundedReport.outputTriangles;
            report.cutSurfaceTriangles = boundedReport.cutSurfaceTriangles;
            report.generatedCutSurfaceTriangles =
                boundedReport.generatedCutSurfaceTriangles;
            report.outputSubmeshes = result != null ? result.subMeshCount : 0;
            report.targetCompletelyRemoved = succeeded &&
                                             boundedReport.outputTriangles == 0;
            report.usedPlaneEpsilon = boundedReport.epsilon;
            report.usedWeldTolerance = weldTolerance;
            report.usedRepairTolerance = validationTolerance;
            report.recoveryAttempts = 0;
            report.inputsAutomaticallyRepaired = inputsRepaired;
            report.usedBoundedManifoldSolver = succeeded;
            report.broadphaseCandidatePairs = boundedReport.broadphaseCandidates;
            report.intersectionSegments = boundedReport.intersectionSegments;
            report.processingMilliseconds = boundedReport.elapsedMilliseconds;

            if (!succeeded)
            {
                terminalFailure = IsBoundedBooleanBudgetFailure(
                    boundedReport.status);
                if (terminalFailure)
                {
                    error = BooleanSafetyBudgetPrefix + error;
                }
                return false;
            }

            // A valid subtraction does not always expose part of the cutter.
            // For example, an enclosing cutter can remove one disconnected
            // target component while another component remains elsewhere. In
            // that case there is a real volume reduction but no new cut face.
            // The closed-manifold and signed-volume checks below prove the
            // change without rejecting that legitimate result.
            bool valid = result != null &&
                         (report.targetCompletelyRemoved ||
                          report.outputTriangles > 0);
            string validationError = valid
                ? string.Empty
                : "the solver did not produce a usable result mesh";
            double targetSignedVolume = 0.0;
            if (valid && !report.targetCompletelyRemoved)
            {
                valid = MeshAttributesAreFinite(result);
                if (!valid)
                    validationError = "the result contains non-finite attributes";
            }
            if (valid && !report.targetCompletelyRemoved)
            {
                valid = TryValidateOutwardVolume(
                    targetMesh,
                    out targetSignedVolume,
                    out string targetVolumeError);
                if (!valid)
                    validationError = "the target " + targetVolumeError;
            }
            if (valid && !report.targetCompletelyRemoved &&
                settings.leaveCutOpen)
            {
                valid = TryValidateOpenBooleanResult(
                    targetMesh,
                    result,
                    validationTolerance,
                    out string openResultError);
                if (!valid)
                    validationError = openResultError;
            }
            if (valid && !report.targetCompletelyRemoved &&
                !settings.leaveCutOpen)
            {
                valid = IsClosedManifold(
                    result,
                    validationTolerance,
                    out int boundaryEdges,
                    out int conflictingEdges);
                if (!valid)
                {
                    validationError = string.Format(
                        "the result has {0:N0} boundary and {1:N0} conflicting edges",
                        boundaryEdges,
                        conflictingEdges);
                }
            }
            if (valid && !report.targetCompletelyRemoved &&
                !settings.leaveCutOpen)
            {
                valid = TryValidateOutwardVolume(
                    result,
                    out double outputSignedVolume,
                    out string outputVolumeError);
                if (!valid)
                {
                    validationError = "the result " + outputVolumeError;
                }
                else if (!HasMeaningfulVolumeReduction(
                             targetSignedVolume,
                             outputSignedVolume))
                {
                    valid = false;
                    validationError = "the result did not remove solid volume";
                }
            }

            if (valid)
                return true;

            DestroyTemporaryMesh(result);
            result = null;
            report.outputVertices = 0;
            report.outputTriangles = 0;
            report.cutSurfaceTriangles = 0;
            report.generatedCutSurfaceTriangles = 0;
            report.outputSubmeshes = 0;
            report.targetCompletelyRemoved = false;
            report.usedBoundedManifoldSolver = false;
            error = "The bounded manifold result was rejected because " +
                    validationError + ".";
            // A topology rejection may still be a simple coplanar case that
            // the compatibility solver handles, so keep it eligible for one
            // bounded fallback profile.
            terminalFailure = false;
            return false;
        }

        private static bool IsBoundedBooleanBudgetFailure(
            RealBlendBoundedBooleanStatus status)
        {
            return status == RealBlendBoundedBooleanStatus.CandidateBudgetExceeded ||
                   status == RealBlendBoundedBooleanStatus.FragmentBudgetExceeded ||
                   status == RealBlendBoundedBooleanStatus.RayBudgetExceeded ||
                   status == RealBlendBoundedBooleanStatus.OutputBudgetExceeded ||
                   status == RealBlendBoundedBooleanStatus.TimeBudgetExceeded;
        }

        private static bool TryPrepareOperand(
            Mesh source,
            float tolerance,
            string label,
            out Mesh usable,
            out Mesh temporary,
            out bool repaired,
            out string error)
        {
            usable = source;
            temporary = null;
            repaired = false;
            error = string.Empty;
            if (TryValidateMesh(
                    source,
                    true,
                    int.MaxValue,
                    tolerance,
                    label,
                    out _,
                    out _))
            {
                return true;
            }
            if (!RealBlendMeshBooleanOperandHealer.TryCreateUsableSolid(
                    source,
                    tolerance,
                    out Mesh healed,
                    out bool repairNeeded,
                    out _,
                    out _))
            {
                error = "RealBlend could not automatically infer a stable solid from the " +
                        label + ".";
                return false;
            }
            if (!repairNeeded)
                return true;
            if (healed == null)
            {
                error = "RealBlend could not automatically prepare the " + label +
                        " for this cut.";
                return false;
            }
            usable = healed;
            temporary = healed;
            repaired = true;
            return true;
        }

        private static void PopulateAttemptReport(
            ref RealBlendMeshBooleanReport report,
            int targetInputTriangles,
            int cutterInputTriangles,
            float planeEpsilon,
            float weldTolerance,
            float repairTolerance,
            int recoveryAttempts,
            bool inputsRepaired)
        {
            report.targetInputTriangles = targetInputTriangles;
            report.cutterInputTriangles = cutterInputTriangles;
            report.usedPlaneEpsilon = planeEpsilon;
            report.usedWeldTolerance = weldTolerance;
            report.usedRepairTolerance = repairTolerance;
            report.recoveryAttempts = recoveryAttempts;
            report.inputsAutomaticallyRepaired = inputsRepaired;
        }

        private static bool TrySubtractCore(
            Mesh targetMesh,
            Mesh cutterMesh,
            Matrix4x4 cutterToTarget,
            RealBlendMeshBooleanSettings settings,
            float epsilon,
            float weldTolerance,
            float repairTolerance,
            float validationTolerance,
            int maxTriangles,
            int orderVariant,
            out Mesh result,
            out RealBlendMeshBooleanReport report,
            out string error,
            out bool retryable)
        {
            result = null;
            report = default;
            error = string.Empty;
            retryable = true;
            try
            {
                List<BooleanPolygon> targetPolygons = MeshToPolygons(
                    targetMesh,
                    Matrix4x4.identity,
                    false,
                    0,
                    settings.cutSurfaceColor,
                    epsilon);
                List<BooleanPolygon> cutterPolygons = MeshToPolygons(
                    cutterMesh,
                    cutterToTarget,
                    true,
                    Mathf.Max(0, settings.cutSurfaceSubmesh),
                    settings.cutSurfaceColor,
                    epsilon);
                ApplyDeterministicPolygonOrder(
                    targetPolygons,
                    orderVariant,
                    0);
                ApplyDeterministicPolygonOrder(
                    cutterPolygons,
                    orderVariant,
                    1);

                if (targetPolygons.Count == 0 || cutterPolygons.Count == 0)
                {
                    error = "The operands contain no usable solid surface.";
                    retryable = false;
                    return false;
                }

                // The legacy BSP is retained as a compatibility fallback, but
                // its intermediate work must be bounded. Curved cutters can
                // otherwise split the same fragments through hundreds of
                // planes before the old post-build triangle limit is reached.
                // Base the ceiling on the real operand size (not tolerance),
                // so lowering Seam Tolerance cannot increase memory use.
                int inputPolygonCount = targetPolygons.Count + cutterPolygons.Count;
                float resourceScale =
                    RealBlendBooleanBudgetUtility.GetMemoryScale(
                        settings.resourceBudget);
                long maximumOperations =
                    RealBlendBooleanBudgetUtility.ScaleLong(
                        Math.Max(2000000L, Math.Min(10000000L,
                            (long)inputPolygonCount * 2048L)),
                        resourceScale,
                        20000000L);
                int maximumGeneratedFragments =
                    RealBlendBooleanBudgetUtility.ScaleInt(
                        Math.Max(32768, Math.Min(250000,
                            inputPolygonCount * 64)),
                        resourceScale,
                        500000);
                int maximumLiveFragments =
                    RealBlendBooleanBudgetUtility.ScaleInt(
                        Math.Max(24576, Math.Min(200000,
                            inputPolygonCount * 48)),
                        resourceScale,
                        400000);
                BooleanWorkBudget workBudget = new BooleanWorkBudget(
                    maximumOperations,
                    maximumGeneratedFragments,
                    maximumLiveFragments,
                    RealBlendBooleanBudgetUtility.GetMaximumMilliseconds(
                        settings.resourceBudget));
                BooleanNode a = new BooleanNode(
                    targetPolygons,
                    epsilon,
                    workBudget);
                BooleanNode b = new BooleanNode(
                    cutterPolygons,
                    epsilon,
                    workBudget);
                a.Invert();
                a.ClipTo(b);
                b.ClipTo(a);
                b.Invert();
                b.ClipTo(a);
                b.Invert();
                a.Build(b.AllPolygons());
                a.Invert();

                List<BooleanPolygon> outputPolygons = a.AllPolygons();
                int maximumOutputTriangles = Math.Min(
                    RealBlendBooleanBudgetUtility.GetLegacyOutputTriangleLimit(
                        settings.resourceBudget),
                    Math.Max(
                        inputPolygonCount + 1000,
                        RealBlendBooleanBudgetUtility.ScaleInt(
                            inputPolygonCount * 16,
                            resourceScale,
                            int.MaxValue)));
                if (CountTriangulatedPolygons(
                        outputPolygons,
                        maximumOutputTriangles) > maximumOutputTriangles)
                {
                    error = string.Format(
                        "Boolean output exceeded its {0:N0}-triangle safety budget.",
                        maximumOutputTriangles);
                    retryable = false;
                    return false;
                }
                int cutPolygonCount = 0;
                for (int i = 0; i < outputPolygons.Count; i++)
                {
                    if (outputPolygons[i].tag.isCutSurface)
                        cutPolygonCount++;
                }
                if (outputPolygons.Count > 0 &&
                    cutPolygonCount == 0 &&
                    !settings.leaveCutOpen)
                {
                    error = "The cutter does not intersect the target's solid volume.";
                    retryable = false;
                    return false;
                }

                ResolveTJunctions(
                    outputPolygons,
                    repairTolerance,
                    workBudget,
                    maximumOutputTriangles);
                if (CountTriangulatedPolygons(
                        outputPolygons,
                        maximumOutputTriangles) > maximumOutputTriangles)
                {
                    error = string.Format(
                        "Boolean output exceeded its {0:N0}-triangle safety budget.",
                        maximumOutputTriangles);
                    retryable = false;
                    return false;
                }
                result = BuildMesh(
                    outputPolygons,
                    Mathf.Max(targetMesh.subMeshCount, settings.cutSurfaceSubmesh + 1),
                    weldTolerance,
                    epsilon,
                    repairTolerance,
                    targetMesh.name + "_BooleanCut",
                    out report.outputVertices,
                    out report.outputTriangles,
                    out report.cutSurfaceTriangles,
                    out report.outputSubmeshes,
                    out List<bool>[] cutSurfaceTriangleFlags);
                report.generatedCutSurfaceTriangles =
                    report.cutSurfaceTriangles;
                report.targetCompletelyRemoved = report.outputTriangles == 0;

                if (!HasRetainedCutSurface(
                        report.outputTriangles,
                        report.cutSurfaceTriangles,
                        report.targetCompletelyRemoved) &&
                    !settings.leaveCutOpen)
                {
                    error = "The Boolean cut could not be completed reliably.";
                    DestroyTemporaryMesh(result);
                    result = null;
                    return false;
                }

                if (report.outputTriangles > maximumOutputTriangles)
                {
                    error = string.Format(
                        "Boolean output exceeded its {0:N0}-triangle safety budget.",
                        maximumOutputTriangles);
                    retryable = false;
                    DestroyTemporaryMesh(result);
                    result = null;
                    return false;
                }
                if (!MeshAttributesAreFinite(result))
                {
                    error = "Boolean output contains invalid geometry attributes.";
                    DestroyTemporaryMesh(result);
                    result = null;
                    return false;
                }
                if (!report.targetCompletelyRemoved &&
                    settings.requireClosedTarget &&
                    settings.requireClosedCutter)
                {
                    if (!TryValidateOutwardVolume(
                            targetMesh,
                            out double targetSignedVolume,
                            out _))
                    {
                        error = "The Boolean cut could not be completed reliably.";
                        DestroyTemporaryMesh(result);
                        result = null;
                        return false;
                    }
                    if (!IsClosedManifold(
                            result,
                            validationTolerance,
                            out int boundaryEdges,
                            out int nonManifoldEdges))
                    {
                        DumpTopologyDiagnosticMesh(
                            result,
                            validationTolerance,
                            boundaryEdges,
                            nonManifoldEdges);
                        error = string.Format(
                            "Boolean recovery could not seal the result ({0:N0} boundary, {1:N0} conflicting edges).",
                            boundaryEdges,
                            nonManifoldEdges);
                        DestroyTemporaryMesh(result);
                        result = null;
                        return false;
                    }
                    if (!TryValidateOutwardVolume(
                            result,
                            out double outputSignedVolume,
                            out string outputVolumeError))
                    {
                        error = "Boolean recovery produced invalid volume: " + outputVolumeError;
                        DestroyTemporaryMesh(result);
                        result = null;
                        return false;
                    }
                    if (!HasMeaningfulVolumeReduction(
                            targetSignedVolume,
                            outputSignedVolume))
                    {
                        error = "The Boolean cut could not be completed reliably.";
                        DestroyTemporaryMesh(result);
                        result = null;
                        return false;
                    }
                }

                if (settings.leaveCutOpen &&
                    !report.targetCompletelyRemoved)
                {
                    Mesh openResult = CreateMeshWithoutCutSurfaces(
                        result,
                        cutSurfaceTriangleFlags,
                        targetMesh.subMeshCount,
                        out report.outputVertices,
                        out report.outputTriangles,
                        out report.outputSubmeshes);
                    if (openResult == null || report.outputTriangles <= 0)
                    {
                        DestroyTemporaryMesh(openResult);
                        error = "Removing the generated cut surface left no usable target surface.";
                        DestroyTemporaryMesh(result);
                        result = null;
                        return false;
                    }
                    DestroyTemporaryMesh(result);
                    result = openResult;
                    report.cutSurfaceTriangles = 0;

                    if (!TryValidateOpenBooleanResult(
                            targetMesh,
                            result,
                            validationTolerance,
                            out string openResultError))
                    {
                        error = "The open Boolean result was rejected because " +
                                openResultError + ".";
                        DestroyTemporaryMesh(result);
                        result = null;
                        retryable = false;
                        return false;
                    }
                }
                return true;
            }
            catch (BooleanWorkBudgetExceededException exception)
            {
                if (result != null)
                {
                    DestroyTemporaryMesh(result);
                    result = null;
                }
                retryable = false;
                error = BooleanSafetyBudgetPrefix +
                        exception.Message;
                return false;
            }
            catch (OutOfMemoryException)
            {
                if (result != null)
                {
                    DestroyTemporaryMesh(result);
                    result = null;
                }
                retryable = false;
                error = BooleanSafetyBudgetPrefix +
                        "the operation could not reserve bounded working memory.";
                return false;
            }
            catch (Exception exception)
            {
                if (result != null)
                {
                    DestroyTemporaryMesh(result);
                    result = null;
                }
                error = "Boolean recovery failed: " + exception.Message;
                return false;
            }
        }

        private static long CountTriangulatedPolygons(
            List<BooleanPolygon> polygons,
            long stopAfter)
        {
            long count = 0L;
            for (int i = 0; i < polygons.Count; i++)
            {
                BooleanPolygon polygon = polygons[i];
                if (polygon == null || polygon.vertices == null ||
                    polygon.vertices.Count < 3)
                {
                    continue;
                }

                count += polygon.vertices.Count - 2L;
                if (count > stopAfter)
                    return count;
            }
            return count;
        }

        private static void ApplyDeterministicPolygonOrder(
            List<BooleanPolygon> polygons,
            int variant,
            int salt)
        {
            if (polygons == null || polygons.Count < 2 || variant <= 0)
                return;
            int mode = (variant - 1 + salt) % 3;
            if (mode == 0)
            {
                polygons.Reverse();
                return;
            }

            List<BooleanPolygon> source = new List<BooleanPolygon>(polygons);
            if (mode == 1)
            {
                int offset = Mathf.Max(1, source.Count / 3);
                for (int index = 0; index < source.Count; index++)
                    polygons[index] = source[(index + offset) % source.Count];
                return;
            }

            int write = 0;
            int left = 0;
            int right = source.Count - 1;
            while (left <= right)
            {
                polygons[write++] = source[left++];
                if (left <= right)
                    polygons[write++] = source[right--];
            }
        }

        private static void DumpTopologyDiagnosticMesh(
            Mesh mesh,
            float tolerance,
            int boundaryEdges,
            int conflictingEdges)
        {
            if (!TopologyDiagnosticsEnabled)
                return;
            string directory = Environment.GetEnvironmentVariable(
                "REALBLEND_BOOLEAN_DUMP_DIR");
            if (string.IsNullOrEmpty(directory))
                return;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                string dumpId = Guid.NewGuid().ToString("N");
                string path = System.IO.Path.Combine(
                    directory,
                    string.Format(
                        "candidate-{0}-{1}b-{2}c-{3:G5}.obj",
                        dumpId,
                        boundaryEdges,
                        conflictingEdges,
                        tolerance));
                Vector3[] positions = mesh.vertices;
                using (System.IO.StreamWriter writer =
                       new System.IO.StreamWriter(path, false))
                {
                    writer.WriteLine("# RealBlend topology diagnostic");
                    writer.WriteLine("# tolerance " + tolerance.ToString("R"));
                    for (int vertex = 0; vertex < positions.Length; vertex++)
                    {
                        Vector3 position = positions[vertex];
                        writer.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "v {0:R} {1:R} {2:R}",
                            position.x,
                            position.y,
                            position.z));
                    }
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        writer.WriteLine("g submesh_" + submesh);
                        int[] triangles = mesh.GetTriangles(submesh);
                        for (int start = 0; start + 2 < triangles.Length; start += 3)
                        {
                            writer.WriteLine(string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "f {0} {1} {2}",
                                triangles[start] + 1,
                                triangles[start + 1] + 1,
                                triangles[start + 2] + 1));
                        }
                    }
                }
                TraceTopologyRecovery("wrote diagnostic mesh " + path);
            }
            catch (Exception exception)
            {
                TraceTopologyRecovery(
                    "could not write diagnostic mesh: " + exception.Message);
            }
        }

        public static bool TryValidateMesh(
            Mesh mesh,
            bool requireClosed,
            int maxTriangles,
            float weldTolerance,
            string operandName,
            out int triangleCount,
            out string error)
        {
            triangleCount = 0;
            error = string.Empty;
            string label = string.IsNullOrEmpty(operandName) ? "Mesh" : operandName;

            if (mesh == null)
            {
                error = label + " has no mesh.";
                return false;
            }

            if (!mesh.isReadable)
            {
                error = label + " mesh is not readable. Enable Read/Write in its import settings.";
                return false;
            }

            if (mesh.vertexCount < 3)
            {
                error = label + " contains no triangle surface.";
                return false;
            }

            Vector3[] positions = mesh.vertices;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!IsFinite(positions[i]))
                {
                    error = label + " contains a vertex with NaN or infinite coordinates.";
                    return false;
                }
            }

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    error = label + " uses a non-triangle submesh, which cannot be used for a Boolean cut.";
                    return false;
                }

                long count = (long)mesh.GetIndexCount(submesh);
                if (count % 3L != 0L)
                {
                    error = label + " contains an incomplete triangle index buffer.";
                    return false;
                }

                triangleCount += (int)(count / 3L);
            }

            if (triangleCount == 0)
            {
                error = label + " has no triangles.";
                return false;
            }

            if (triangleCount > Mathf.Max(1, maxTriangles))
            {
                error = string.Format(
                    "{0} has {1:N0} triangles, above the current Boolean processing budget of {2:N0}.",
                    label,
                    triangleCount,
                    Mathf.Max(1, maxTriangles));
                return false;
            }

            if (requireClosed &&
                !IsClosedManifold(mesh, Mathf.Max(0.000001f, weldTolerance), out int boundaryEdges, out int nonManifoldEdges))
            {
                error = string.Format(
                    "{0} requires automatic solid preparation ({1:N0} boundary edges, {2:N0} conflicting edges).",
                    label,
                    boundaryEdges,
                    nonManifoldEdges);
                return false;
            }


            if (requireClosed &&
                !TryValidateOutwardVolume(mesh, out string volumeError))
            {
                error = label + " " + volumeError;
                return false;
            }

            return true;
        }

        public static bool IsClosedManifold(
            Mesh mesh,
            float positionTolerance,
            out int boundaryEdges,
            out int nonManifoldEdges)
        {
            boundaryEdges = 0;
            nonManifoldEdges = 0;
            if (mesh == null || !mesh.isReadable)
                return false;

            float tolerance = Mathf.Max(0.000001f, positionTolerance);
            Vector3[] vertices = mesh.vertices;
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                tolerance,
                out _);

            Dictionary<EdgeKey, EdgeIncidence> edgeCounts =
                new Dictionary<EdgeKey, EdgeIncidence>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    return false;

                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = weldedIds[triangles[i]];
                    int b = weldedIds[triangles[i + 1]];
                    int c = weldedIds[triangles[i + 2]];
                    if (a == b || b == c || c == a)
                    {
                        nonManifoldEdges++;
                        continue;
                    }
                    AddEdge(edgeCounts, a, b);
                    AddEdge(edgeCounts, b, c);
                    AddEdge(edgeCounts, c, a);
                }
            }

            foreach (KeyValuePair<EdgeKey, EdgeIncidence> pair in edgeCounts)
            {
                if (pair.Value.count == 1)
                    boundaryEdges++;
                else if (pair.Value.count != 2 || pair.Value.directionSum != 0)
                    nonManifoldEdges++;
            }

            return boundaryEdges == 0 && nonManifoldEdges == 0;
        }

        public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
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

        private static float ComputeRepairToleranceCeiling(
            Bounds targetBounds,
            Bounds cutterLocalBounds,
            Matrix4x4 cutterToTarget)
        {
            Vector3 targetSize = targetBounds.size;
            Vector3 cutterSize = cutterLocalBounds.size;
            float smallestPositiveDimension = float.PositiveInfinity;
            AccumulatePositiveDimension(targetSize.x, ref smallestPositiveDimension);
            AccumulatePositiveDimension(targetSize.y, ref smallestPositiveDimension);
            AccumulatePositiveDimension(targetSize.z, ref smallestPositiveDimension);
            AccumulatePositiveDimension(
                cutterSize.x * cutterToTarget.MultiplyVector(Vector3.right).magnitude,
                ref smallestPositiveDimension);
            AccumulatePositiveDimension(
                cutterSize.y * cutterToTarget.MultiplyVector(Vector3.up).magnitude,
                ref smallestPositiveDimension);
            AccumulatePositiveDimension(
                cutterSize.z * cutterToTarget.MultiplyVector(Vector3.forward).magnitude,
                ref smallestPositiveDimension);
            if (float.IsInfinity(smallestPositiveDimension))
                return 0.00001f;
            return Mathf.Clamp(
                smallestPositiveDimension * 0.002f,
                0.00001f,
                0.001f);
        }

        private static void AccumulatePositiveDimension(
            float dimension,
            ref float smallest)
        {
            if (!float.IsNaN(dimension) &&
                !float.IsInfinity(dimension) &&
                dimension > 0.0000001f)
                smallest = Mathf.Min(smallest, dimension);
        }

        private static bool BoundsOverlapWithVolume(Bounds a, Bounds b, float epsilon)
        {
            Vector3 minimum = Vector3.Max(a.min, b.min);
            Vector3 maximum = Vector3.Min(a.max, b.max);
            Vector3 overlap = maximum - minimum;
            return overlap.x > epsilon && overlap.y > epsilon && overlap.z > epsilon;
        }

        private static bool BoundsOverlapInclusive(
            Bounds a,
            Bounds b,
            float epsilon)
        {
            Vector3 minimum = Vector3.Max(a.min, b.min);
            Vector3 maximum = Vector3.Min(a.max, b.max);
            Vector3 overlap = maximum - minimum;
            return overlap.x >= -epsilon &&
                   overlap.y >= -epsilon &&
                   overlap.z >= -epsilon;
        }

        private static bool IsFiniteAndInvertible(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float value = matrix[row, column];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        return false;
                }
            }
            return Mathf.Abs(matrix.determinant) > 0.000000001f;
        }

        private static int[] BuildWeldedPositionIds(
            IList<Vector3> positions,
            float positionTolerance,
            out List<Vector3> representativePositions)
        {
            float tolerance = Mathf.Max(0.0000001f, positionTolerance);
            float squaredTolerance = tolerance * tolerance;
            int count = positions != null ? positions.Count : 0;
            int[] parents = new int[count];
            Dictionary<PositionKey, List<int>> spatialBuckets =
                new Dictionary<PositionKey, List<int>>();

            for (int index = 0; index < count; index++)
            {
                parents[index] = index;
                Vector3 position = positions[index];
                PositionKey key = new PositionKey(position, tolerance);
                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            // Offset the already-quantized key. Re-quantizing
                            // position + tolerance in float precision can skip
                            // the true adjacent bucket near a half-cell boundary.
                            PositionKey neighborKey = key.Offset(x, y, z);
                            if (!spatialBuckets.TryGetValue(
                                    neighborKey,
                                    out List<int> candidates))
                            {
                                continue;
                            }

                            for (int candidateIndex = 0;
                                 candidateIndex < candidates.Count;
                                 candidateIndex++)
                            {
                                int candidate = candidates[candidateIndex];
                                if ((positions[candidate] - position).sqrMagnitude <=
                                    squaredTolerance)
                                {
                                    UnionPositionRoots(parents, candidate, index);
                                }
                            }
                        }
                    }
                }

                if (!spatialBuckets.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>();
                    spatialBuckets.Add(key, bucket);
                }
                bucket.Add(index);
            }

            Dictionary<int, int> rootIds = new Dictionary<int, int>();
            representativePositions = new List<Vector3>();
            int[] weldedIds = new int[count];
            for (int index = 0; index < count; index++)
            {
                int root = FindPositionRoot(parents, index);
                if (!rootIds.TryGetValue(root, out int id))
                {
                    id = representativePositions.Count;
                    rootIds.Add(root, id);
                    representativePositions.Add(positions[root]);
                }
                weldedIds[index] = id;
            }
            return weldedIds;
        }

        private static int FindPositionRoot(int[] parents, int index)
        {
            int root = index;
            while (parents[root] != root)
                root = parents[root];
            while (parents[index] != index)
            {
                int next = parents[index];
                parents[index] = root;
                index = next;
            }
            return root;
        }

        private static void UnionPositionRoots(int[] parents, int left, int right)
        {
            int leftRoot = FindPositionRoot(parents, left);
            int rightRoot = FindPositionRoot(parents, right);
            if (leftRoot == rightRoot)
                return;
            if (leftRoot < rightRoot)
                parents[rightRoot] = leftRoot;
            else
                parents[leftRoot] = rightRoot;
        }

        private static void AddEdge(
            Dictionary<EdgeKey, EdgeIncidence> counts,
            int a,
            int b)
        {
            if (a == b)
                return;
            EdgeKey key = new EdgeKey(a, b);
            counts.TryGetValue(key, out EdgeIncidence incidence);
            incidence.count++;
            incidence.directionSum += a < b ? 1 : -1;
            counts[key] = incidence;
        }

        private static bool TryValidateOutwardVolume(
            Mesh mesh,
            out string error)
        {
            return TryValidateOutwardVolume(mesh, out _, out error);
        }

        private static bool TryValidateOutwardVolume(
            Mesh mesh,
            out double signedVolume,
            out string error)
        {
            error = string.Empty;
            signedVolume = 0.0;
            Vector3[] vertices = mesh.vertices;
            float minimumAltitude = Mathf.Max(
                0.00000001f,
                mesh.bounds.size.magnitude * 0.0000000001f);
            float minimumAltitudeSquared = minimumAltitude * minimumAltitude;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]];
                    Vector3 b = vertices[triangles[i + 1]];
                    Vector3 c = vertices[triangles[i + 2]];
                    Vector3 ab = b - a;
                    Vector3 ac = c - a;
                    Vector3 bc = c - b;
                    float longestEdgeSquared = Mathf.Max(
                        ab.sqrMagnitude,
                        Mathf.Max(ac.sqrMagnitude, bc.sqrMagnitude));
                    // Cross magnitude is area (length squared), so compare its
                    // square against edge-length-squared times an altitude
                    // tolerance.  The previous bounds-based comparison mixed
                    // units and rejected valid, very narrow seam triangles.
                    if (longestEdgeSquared <= minimumAltitudeSquared ||
                        Vector3.Cross(ab, ac).sqrMagnitude <=
                        longestEdgeSquared * minimumAltitudeSquared)
                    {
                        error = "contains a zero-area triangle.";
                        return false;
                    }
                    // Accumulate the scalar triple product in double precision.
                    // Open-mode validation compares two independently
                    // triangulated versions of the same closed surface; doing
                    // the cross/dot in float can manufacture a tiny apparent
                    // volume reduction and accept a fully-contained cutter as
                    // a successful no-op.
                    double crossX = (double)b.y * c.z - (double)b.z * c.y;
                    double crossY = (double)b.z * c.x - (double)b.x * c.z;
                    double crossZ = (double)b.x * c.y - (double)b.y * c.x;
                    signedVolume += ((double)a.x * crossX +
                                     (double)a.y * crossY +
                                     (double)a.z * crossZ) / 6.0;
                }
            }

            Vector3 size = mesh.bounds.size;
            double boundsVolume = Math.Abs((double)size.x * size.y * size.z);
            double minimumVolume = Math.Max(1e-12, boundsVolume * 1e-9);
            if (Math.Abs(signedVolume) <= minimumVolume)
            {
                error = "has zero enclosed volume.";
                return false;
            }
            if (signedVolume < 0.0)
            {
                error = "requires automatic winding normalization.";
                return false;
            }
            return true;
        }

        private static bool HasRetainedCutSurface(
            int outputTriangles,
            int cutSurfaceTriangles,
            bool targetCompletelyRemoved)
        {
            return targetCompletelyRemoved ||
                   outputTriangles == 0 ||
                   cutSurfaceTriangles > 0;
        }

        private static bool HasMeaningfulVolumeReduction(
            double targetSignedVolume,
            double outputSignedVolume)
        {
            double minimumReduction = Math.Max(
                1e-12,
                Math.Abs(targetSignedVolume) * 1e-9);
            return targetSignedVolume - outputSignedVolume > minimumReduction;
        }

        /// <summary>
        /// Certifies the post-filtered Leave Cut Open result. A normal surface
        /// cut must expose clean boundary edges. A cutter can also remove an
        /// entire disconnected target component without creating a boundary;
        /// that closed remainder is accepted only when signed volume proves a
        /// real reduction. This rejects a fully-contained cutter whose internal
        /// shell was removed and whose visible outer target stayed unchanged.
        /// </summary>
        internal static bool TryValidateOpenBooleanResult(
            Mesh targetMesh,
            Mesh openResult,
            float tolerance,
            out string error)
        {
            error = string.Empty;
            if (targetMesh == null || openResult == null)
            {
                error = "the open result mesh is missing";
                return false;
            }

            bool closed = IsClosedManifold(
                openResult,
                tolerance,
                out int boundaryEdges,
                out int conflictingEdges);
            if (conflictingEdges > 0)
            {
                error = string.Format(
                    "the open result has {0:N0} conflicting edge{1}",
                    conflictingEdges,
                    conflictingEdges == 1 ? string.Empty : "s");
                return false;
            }
            if (boundaryEdges > 0)
                return true;
            if (!closed)
            {
                error = "the open result could not be classified safely";
                return false;
            }

            if (!TryValidateOutwardVolume(
                    targetMesh,
                    out double targetSignedVolume,
                    out string targetVolumeError))
            {
                error = "the target " + targetVolumeError;
                return false;
            }
            if (!TryValidateOutwardVolume(
                    openResult,
                    out double outputSignedVolume,
                    out string outputVolumeError))
            {
                error = "the closed open-mode remainder " + outputVolumeError;
                return false;
            }
            if (!HasMeaningfulVolumeReduction(
                    targetSignedVolume,
                    outputSignedVolume))
            {
                error =
                    "the open result has no boundary and removed no solid volume; the cutter appears fully contained by the target";
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

        private static bool MeshAttributesAreFinite(Mesh mesh)
        {
            if (mesh == null)
                return false;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv0 = mesh.uv;
            List<Vector2> uv2 = new List<Vector2>();
            mesh.GetUVs(2, uv2);
            Color[] colors = mesh.colors;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFinite(vertices[i]))
                    return false;
                if (normals.Length == vertices.Length && !IsFinite(normals[i]))
                    return false;
                if (uv0.Length == vertices.Length && !IsFinite(uv0[i]))
                    return false;
                if (uv2.Count == vertices.Length && !IsFinite(uv2[i]))
                    return false;
                if (colors.Length == vertices.Length && !IsFinite(colors[i]))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static bool IsFinite(Color value)
        {
            return !float.IsNaN(value.r) && !float.IsInfinity(value.r) &&
                   !float.IsNaN(value.g) && !float.IsInfinity(value.g) &&
                   !float.IsNaN(value.b) && !float.IsInfinity(value.b) &&
                   !float.IsNaN(value.a) && !float.IsInfinity(value.a);
        }

        private static void DestroyTemporaryMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
#if UNITY_EDITOR
            UnityEngine.Object.DestroyImmediate(mesh);
#else
            UnityEngine.Object.Destroy(mesh);
#endif
        }

        private static List<BooleanPolygon> MeshToPolygons(
            Mesh mesh,
            Matrix4x4 toTarget,
            bool isCutSurface,
            int cutSurfaceSubmesh,
            Color cutSurfaceColor,
            float epsilon)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv0 = mesh.uv;
            List<Vector2> uv2List = new List<Vector2>();
            mesh.GetUVs(2, uv2List);
            Vector2[] uv2 = uv2List.Count == positions.Length
                ? uv2List.ToArray()
                : Array.Empty<Vector2>();
            Color[] colors = mesh.colors;
            Matrix4x4 normalMatrix = toTarget.inverse.transpose;
            bool mirrored = toTarget.determinant < 0f;
            int triangleCapacity = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                triangleCapacity += (int)(mesh.GetIndexCount(submesh) / 3L);
            List<BooleanPolygon> polygons =
                new List<BooleanPolygon>(triangleCapacity);

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                int outputSubmesh = isCutSurface ? cutSurfaceSubmesh : submesh;
                PolygonTag tag = new PolygonTag(outputSubmesh, isCutSurface);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    int a = triangles[triangle];
                    int b = triangles[triangle + 1];
                    int c = triangles[triangle + 2];
                    if (mirrored)
                    {
                        int swap = b;
                        b = c;
                        c = swap;
                    }

                    BooleanVertex va = ReadVertex(
                        a, positions, normals, uv0, uv2, colors,
                        toTarget, normalMatrix, isCutSurface, cutSurfaceColor);
                    BooleanVertex vb = ReadVertex(
                        b, positions, normals, uv0, uv2, colors,
                        toTarget, normalMatrix, isCutSurface, cutSurfaceColor);
                    BooleanVertex vc = ReadVertex(
                        c, positions, normals, uv0, uv2, colors,
                        toTarget, normalMatrix, isCutSurface, cutSurfaceColor);

                    Vector3 geometricNormal = Vector3.Cross(
                        vb.position - va.position,
                        vc.position - va.position);
                    // Cross-product squared magnitude has units of length^4;
                    // comparing it directly with epsilon^2 (length^2) deletes
                    // perfectly valid short/slender triangles. Connected wall
                    // bevels are especially vulnerable because their 3 cm edge
                    // triangles can be only a few millimetres tall. Use the same
                    // altitude-scaled degeneracy test as output triangulation so
                    // an authored bevel or joined edge cannot disappear before
                    // the Boolean operation starts.
                    Vector3 edgeAB = vb.position - va.position;
                    Vector3 edgeAC = vc.position - va.position;
                    Vector3 edgeBC = vc.position - vb.position;
                    float longestEdgeSquared = Mathf.Max(
                        edgeAB.sqrMagnitude,
                        Mathf.Max(edgeAC.sqrMagnitude, edgeBC.sqrMagnitude));
                    float minimumAltitude = Mathf.Max(
                        0.0000001f,
                        epsilon * 0.25f);
                    float minimumAltitudeSquared =
                        minimumAltitude * minimumAltitude;
                    if (longestEdgeSquared <= minimumAltitudeSquared ||
                        geometricNormal.sqrMagnitude <=
                        minimumAltitudeSquared * longestEdgeSquared)
                        continue;
                    geometricNormal.Normalize();
                    if (va.normal.sqrMagnitude <= 0.0000001f) va.normal = geometricNormal;
                    if (vb.normal.sqrMagnitude <= 0.0000001f) vb.normal = geometricNormal;
                    if (vc.normal.sqrMagnitude <= 0.0000001f) vc.normal = geometricNormal;
                    if (isCutSurface)
                    {
                        va.uv0 = ProjectBoxUV(va.position, geometricNormal);
                        vb.uv0 = ProjectBoxUV(vb.position, geometricNormal);
                        vc.uv0 = ProjectBoxUV(vc.position, geometricNormal);
                    }

                    if (BooleanPolygon.TryCreate(
                            new List<BooleanVertex> { va, vb, vc },
                            tag,
                            epsilon,
                            out BooleanPolygon polygon))
                    {
                        polygons.Add(polygon);
                    }
                }
            }

            return polygons;
        }

        private static BooleanVertex ReadVertex(
            int index,
            Vector3[] positions,
            Vector3[] normals,
            Vector2[] uv0,
            Vector2[] uv2,
            Color[] colors,
            Matrix4x4 transform,
            Matrix4x4 normalMatrix,
            bool isCutSurface,
            Color cutSurfaceColor)
        {
            Vector3 position = transform.MultiplyPoint3x4(positions[index]);
            Vector3 normal = normals != null && normals.Length == positions.Length
                ? normalMatrix.MultiplyVector(normals[index]).normalized
                : Vector3.zero;
            return new BooleanVertex(
                position,
                normal,
                isCutSurface
                    ? ProjectBoxUV(position, normal)
                    : uv0 != null && uv0.Length == positions.Length
                        ? uv0[index]
                        : Vector2.zero,
                uv2 != null && uv2.Length == positions.Length ? uv2[index] : Vector2.zero,
                isCutSurface
                    ? cutSurfaceColor
                    : colors != null && colors.Length == positions.Length
                        ? colors[index]
                        : Color.white);
        }

        private static Vector2 ProjectBoxUV(Vector3 position, Vector3 normal)
        {
            Vector3 absolute = new Vector3(
                Mathf.Abs(normal.x),
                Mathf.Abs(normal.y),
                Mathf.Abs(normal.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            {
                return normal.x >= 0f
                    ? new Vector2(-position.z, position.y)
                    : new Vector2(position.z, position.y);
            }
            if (absolute.y >= absolute.z)
            {
                return normal.y >= 0f
                    ? new Vector2(position.x, position.z)
                    : new Vector2(position.x, -position.z);
            }
            return normal.z >= 0f
                ? new Vector2(position.x, position.y)
                : new Vector2(-position.x, position.y);
        }

        private static Mesh BuildMesh(
            List<BooleanPolygon> polygons,
            int minimumSubmeshCount,
            float weldTolerance,
            float epsilon,
            float repairTolerance,
            string name,
            out int outputVertexCount,
            out int outputTriangleCount,
            out int cutSurfaceTriangleCount,
            out int outputSubmeshCount,
            out List<bool>[] outputCutSurfaceTriangleFlags)
        {
            int estimatedVertexCount = 0;
            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                estimatedVertexCount += polygons[polygonIndex].vertices.Count;
            List<Vector3> vertices = new List<Vector3>(estimatedVertexCount);
            List<Vector3> normals = new List<Vector3>(estimatedVertexCount);
            List<Vector2> uv0 = new List<Vector2>(estimatedVertexCount);
            List<Vector2> uv2 = new List<Vector2>(estimatedVertexCount);
            List<Color> colors = new List<Color>(estimatedVertexCount);
            Dictionary<OutputVertexKey, int> vertexLookup =
                new Dictionary<OutputVertexKey, int>(estimatedVertexCount);

            int submeshCount = Mathf.Max(1, minimumSubmeshCount);
            for (int i = 0; i < polygons.Count; i++)
                submeshCount = Mathf.Max(submeshCount, polygons[i].tag.submesh + 1);
            List<int>[] indices = new List<int>[submeshCount];
            List<bool>[] cutTriangleFlags = new List<bool>[submeshCount];
            for (int i = 0; i < submeshCount; i++)
            {
                indices[i] = new List<int>();
                cutTriangleFlags[i] = new List<bool>();
            }

            // A cross product has units of length squared, so comparing its
            // squared magnitude directly with epsilon squared removes perfectly
            // valid small triangles (especially the narrow seam triangles made
            // by dense cutters). Judge degeneracy by triangle altitude instead.
            float minimumAltitude = Mathf.Max(0.0000001f, epsilon * 0.25f);
            float minimumAltitudeSquared = minimumAltitude * minimumAltitude;
            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                BooleanPolygon polygon = polygons[polygonIndex];
                if (polygon.vertices.Count < 3)
                    continue;

                int submesh = Mathf.Clamp(polygon.tag.submesh, 0, submeshCount - 1);
                BooleanVertex root = polygon.vertices[0];
                for (int vertexIndex = 2; vertexIndex < polygon.vertices.Count; vertexIndex++)
                {
                    BooleanVertex b = polygon.vertices[vertexIndex - 1];
                    BooleanVertex c = polygon.vertices[vertexIndex];
                    Vector3 rootToB = b.position - root.position;
                    Vector3 rootToC = c.position - root.position;
                    Vector3 bToC = c.position - b.position;
                    float longestEdgeSquared = Mathf.Max(
                        rootToB.sqrMagnitude,
                        Mathf.Max(rootToC.sqrMagnitude, bToC.sqrMagnitude));
                    if (longestEdgeSquared <= minimumAltitudeSquared ||
                        Vector3.Cross(rootToB, rootToC).sqrMagnitude <=
                        minimumAltitudeSquared * longestEdgeSquared)
                    {
                        continue;
                    }

                    indices[submesh].Add(GetOrAddVertex(
                        root, weldTolerance, vertices, normals, uv0, uv2, colors, vertexLookup));
                    indices[submesh].Add(GetOrAddVertex(
                        b, weldTolerance, vertices, normals, uv0, uv2, colors, vertexLookup));
                    indices[submesh].Add(GetOrAddVertex(
                        c, weldTolerance, vertices, normals, uv0, uv2, colors, vertexLookup));
                    cutTriangleFlags[submesh].Add(polygon.tag.isCutSurface);
                }
            }

            ResolveTriangleTJunctions(
                indices,
                cutTriangleFlags,
                vertices,
                normals,
                uv0,
                uv2,
                colors,
                repairTolerance);

            float seamTolerance = Mathf.Max(0.000001f, repairTolerance);
            for (int snapPass = 0; snapPass < 4; snapPass++)
            {
                if (SnapNearReversedBoundaryEdges(
                        indices,
                        vertices,
                        seamTolerance) == 0)
                {
                    break;
                }
            }
            ResolveTriangleTJunctions(
                indices,
                cutTriangleFlags,
                vertices,
                normals,
                uv0,
                uv2,
                colors,
                seamTolerance);

            // Welding can expose both sub-tolerance slivers and coincident BSP
            // fragments.  Remove those zero-topology faces, cancel opposite
            // duplicate pairs, then repair the newly exposed edge partitions.
            // A second pass is important: the duplicate may have hidden the
            // long edge of a T-junction during the first repair pass.
            for (int cleanupPass = 0; cleanupPass < 12; cleanupPass++)
            {
                int removed = RemoveWeldCollapsedTriangles(
                    indices,
                    cutTriangleFlags,
                    vertices,
                    seamTolerance);
                removed += RemoveGeometricDegenerateTriangles(
                    indices,
                    cutTriangleFlags,
                    vertices,
                    Mathf.Max(0.00000001f, epsilon * 0.1f));
                removed += RemoveCoincidentTriangleDuplicates(
                    indices,
                    cutTriangleFlags,
                    vertices,
                    seamTolerance);
                int collapsedEdges = CollapseShortNonManifoldEdges(
                    indices,
                    vertices,
                    seamTolerance,
                    seamTolerance * 8f);
                if (collapsedEdges > 0)
                {
                    removed += RemoveWeldCollapsedTriangles(
                        indices,
                        cutTriangleFlags,
                        vertices,
                        seamTolerance);
                    removed += RemoveGeometricDegenerateTriangles(
                        indices,
                        cutTriangleFlags,
                        vertices,
                        Mathf.Max(0.00000001f, epsilon * 0.1f));
                    removed += RemoveCoincidentTriangleDuplicates(
                        indices,
                        cutTriangleFlags,
                        vertices,
                        seamTolerance);
                }
                if (removed == 0)
                    break;

                ResolveTriangleTJunctions(
                    indices,
                    cutTriangleFlags,
                    vertices,
                    normals,
                    uv0,
                    uv2,
                    colors,
                    seamTolerance);
                for (int snapPass = 0; snapPass < 4; snapPass++)
                {
                    if (SnapNearReversedBoundaryEdges(
                            indices,
                            vertices,
                            seamTolerance) == 0)
                    {
                        break;
                    }
                }
            }

            // A coplanar BSP overlap can occasionally survive duplicate removal
            // with two different triangulations: one valid surface plus a
            // two-triangle flap laid over it. Such a flap has a precise topology:
            // each triangle owns one boundary edge and one excess edge occurrence,
            // while the pair shares an otherwise healthy edge. Remove only pairs
            // for which every affected edge becomes either absent or exactly
            // two-incidence/opposite-winding. The final manifold and volume gates
            // still validate the complete result after this local recovery.
            if (TopologyDiagnosticsEnabled)
            {
                CountTopologyDefects(
                    indices,
                    vertices,
                    seamTolerance,
                    out int preFastBoundary,
                    out int preFastConflict);
                TraceTopologyRecovery(string.Format(
                    "pre-fast: {0:N0} triangles, {1:N0} boundary, {2:N0} conflicting, tolerance={3:G9}",
                    CountTriangles(indices),
                    preFastBoundary,
                    preFastConflict,
                    seamTolerance));
            }
            for (int wedgePass = 0; wedgePass < 4; wedgePass++)
            {
                if (RemoveRedundantBoundaryWedges(
                        indices,
                        cutTriangleFlags,
                        vertices,
                        seamTolerance) == 0)
                {
                    break;
                }
            }
            for (int flapPass = 0; flapPass < 4; flapPass++)
            {
                if (RemoveRedundantTopologyFlaps(
                        indices,
                        cutTriangleFlags,
                        vertices,
                        seamTolerance) == 0)
                {
                    break;
                }
            }

            if (TopologyDiagnosticsEnabled)
            {
                CountTopologyDefects(
                    indices,
                    vertices,
                    seamTolerance,
                    out int postFastBoundary,
                    out int postFastConflict);
                TraceTopologyRecovery(string.Format(
                    "post-fast: {0:N0} triangles, {1:N0} boundary, {2:N0} conflicting",
                    CountTriangles(indices),
                    postFastBoundary,
                    postFastConflict));
            }

            Mesh mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(2, uv2);
            mesh.SetColors(colors);
            mesh.subMeshCount = submeshCount;
            for (int submesh = 0; submesh < submeshCount; submesh++)
                mesh.SetTriangles(indices[submesh], submesh, false);
            mesh.RecalculateBounds();
            if (vertices.Count > 0)
            {
                try
                {
                    mesh.RecalculateTangents();
                }
                catch
                {
                    // Tangents are optional; malformed source UVs should not invalidate
                    // an otherwise sound Boolean result.
                }
            }

            outputVertexCount = vertices.Count;
            outputTriangleCount = 0;
            cutSurfaceTriangleCount = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                outputTriangleCount += indices[i].Count / 3;
                for (int triangle = 0;
                     triangle < cutTriangleFlags[i].Count;
                     triangle++)
                {
                    if (cutTriangleFlags[i][triangle])
                        cutSurfaceTriangleCount++;
                }
            }
            outputSubmeshCount = submeshCount;
            outputCutSurfaceTriangleFlags = cutTriangleFlags;
            return mesh;
        }

        /// <summary>
        /// Removes only triangles whose Boolean provenance says they came from
        /// the cutter.  Material slots and vertex colors are deliberately not
        /// used as classifiers because either can also occur on retained target
        /// faces.  Unreferenced cap-only vertices are compacted out so later
        /// paint/sculpt operations do not see invisible geometry.
        /// </summary>
        private static Mesh CreateMeshWithoutCutSurfaces(
            Mesh source,
            List<bool>[] cutSurfaceTriangleFlags,
            int minimumSubmeshCount,
            out int outputVertexCount,
            out int outputTriangleCount,
            out int outputSubmeshCount)
        {
            outputVertexCount = 0;
            outputTriangleCount = 0;
            outputSubmeshCount = 0;
            if (source == null ||
                cutSurfaceTriangleFlags == null ||
                cutSurfaceTriangleFlags.Length != source.subMeshCount)
            {
                return null;
            }

            List<int>[] retainedIndices = new List<int>[source.subMeshCount];
            int highestUsedSubmesh = -1;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] sourceIndices = source.GetTriangles(submesh);
                List<bool> flags = cutSurfaceTriangleFlags[submesh];
                if (flags == null || flags.Count != sourceIndices.Length / 3)
                    return null;

                List<int> retained = new List<int>(sourceIndices.Length);
                for (int triangle = 0; triangle < flags.Count; triangle++)
                {
                    if (flags[triangle])
                        continue;
                    int start = triangle * 3;
                    retained.Add(sourceIndices[start]);
                    retained.Add(sourceIndices[start + 1]);
                    retained.Add(sourceIndices[start + 2]);
                }
                retainedIndices[submesh] = retained;
                if (retained.Count > 0)
                    highestUsedSubmesh = submesh;
            }

            int retainedSubmeshCount = Mathf.Clamp(
                Mathf.Max(minimumSubmeshCount, highestUsedSubmesh + 1),
                1,
                source.subMeshCount);
            Vector3[] sourcePositions = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv0 = source.uv;
            List<Vector2> sourceUv2 = new List<Vector2>();
            source.GetUVs(2, sourceUv2);
            Color[] sourceColors = source.colors;

            bool hasNormals = sourceNormals.Length == sourcePositions.Length;
            bool hasTangents = sourceTangents.Length == sourcePositions.Length;
            bool hasUv0 = sourceUv0.Length == sourcePositions.Length;
            bool hasUv2 = sourceUv2.Count == sourcePositions.Length;
            bool hasColors = sourceColors.Length == sourcePositions.Length;
            int[] remap = new int[sourcePositions.Length];
            for (int i = 0; i < remap.Length; i++)
                remap[i] = -1;

            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = hasNormals ? new List<Vector3>() : null;
            List<Vector4> tangents = hasTangents ? new List<Vector4>() : null;
            List<Vector2> uv0 = hasUv0 ? new List<Vector2>() : null;
            List<Vector2> uv2 = hasUv2 ? new List<Vector2>() : null;
            List<Color> colors = hasColors ? new List<Color>() : null;
            List<int>[] outputIndices = new List<int>[retainedSubmeshCount];

            for (int submesh = 0; submesh < retainedSubmeshCount; submesh++)
            {
                List<int> sourceSubmeshIndices = retainedIndices[submesh];
                List<int> destination = new List<int>(sourceSubmeshIndices.Count);
                for (int index = 0; index < sourceSubmeshIndices.Count; index++)
                {
                    int sourceIndex = sourceSubmeshIndices[index];
                    if ((uint)sourceIndex >= (uint)sourcePositions.Length)
                        return null;
                    int destinationIndex = remap[sourceIndex];
                    if (destinationIndex < 0)
                    {
                        destinationIndex = positions.Count;
                        remap[sourceIndex] = destinationIndex;
                        positions.Add(sourcePositions[sourceIndex]);
                        if (hasNormals) normals.Add(sourceNormals[sourceIndex]);
                        if (hasTangents) tangents.Add(sourceTangents[sourceIndex]);
                        if (hasUv0) uv0.Add(sourceUv0[sourceIndex]);
                        if (hasUv2) uv2.Add(sourceUv2[sourceIndex]);
                        if (hasColors) colors.Add(sourceColors[sourceIndex]);
                    }
                    destination.Add(destinationIndex);
                }
                outputIndices[submesh] = destination;
                outputTriangleCount += destination.Count / 3;
            }

            Mesh mesh = new Mesh
            {
                name = source.name,
                indexFormat = positions.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(positions);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            if (hasUv0) mesh.SetUVs(0, uv0);
            if (hasUv2) mesh.SetUVs(2, uv2);
            if (hasColors) mesh.SetColors(colors);
            mesh.subMeshCount = retainedSubmeshCount;
            for (int submesh = 0; submesh < retainedSubmeshCount; submesh++)
                mesh.SetTriangles(outputIndices[submesh], submesh, false);
            mesh.RecalculateBounds();

            outputVertexCount = positions.Count;
            outputSubmeshCount = retainedSubmeshCount;
            return mesh;
        }

        private static int SnapNearReversedBoundaryEdges(
            List<int>[] submeshIndices,
            List<Vector3> vertices,
            float positionTolerance)
        {
            float tolerance = Mathf.Max(0.000001f, positionTolerance);
            float squaredTolerance = tolerance * tolerance;
            float strictTolerance = Mathf.Max(
                0.0000001f,
                Mathf.Min(0.000001f, tolerance * 0.01f));
            int[] strictIds = BuildWeldedPositionIds(
                vertices,
                strictTolerance,
                out _);
            Dictionary<EdgeKey, List<BoundaryEdgeOccurrence>> occurrences =
                new Dictionary<EdgeKey, List<BoundaryEdgeOccurrence>>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangleStart = 0;
                     triangleStart + 2 < triangles.Count;
                     triangleStart += 3)
                {
                    int a = triangles[triangleStart];
                    int b = triangles[triangleStart + 1];
                    int c = triangles[triangleStart + 2];
                    AddBoundaryOccurrence(
                        occurrences, strictIds, submesh, triangleStart, a, b, c);
                    AddBoundaryOccurrence(
                        occurrences, strictIds, submesh, triangleStart, b, c, a);
                    AddBoundaryOccurrence(
                        occurrences, strictIds, submesh, triangleStart, c, a, b);
                }
            }

            List<BoundaryEdgeOccurrence> boundaryEdges =
                new List<BoundaryEdgeOccurrence>();
            foreach (KeyValuePair<EdgeKey, List<BoundaryEdgeOccurrence>> pair in occurrences)
            {
                if (pair.Value.Count == 1)
                    boundaryEdges.Add(pair.Value[0]);
            }
            if (boundaryEdges.Count < 2)
                return 0;

            // Pair only edges whose tolerance-welded endpoint IDs are reversed.
            // The old all-pairs search was O(boundaryEdges^2) and dominated dense
            // cuts even though almost every edge has only one plausible mate.
            int[] snapIds = BuildWeldedPositionIds(vertices, tolerance, out _);
            Dictionary<DirectedEdgeKey, List<int>> edgesByDirection =
                new Dictionary<DirectedEdgeKey, List<int>>(boundaryEdges.Count);
            for (int edgeIndex = 0; edgeIndex < boundaryEdges.Count; edgeIndex++)
            {
                BoundaryEdgeOccurrence edge = boundaryEdges[edgeIndex];
                DirectedEdgeKey key = new DirectedEdgeKey(
                    snapIds[edge.startVertex],
                    snapIds[edge.endVertex]);
                if (!edgesByDirection.TryGetValue(key, out List<int> edgeIndices))
                {
                    edgeIndices = new List<int>(1);
                    edgesByDirection.Add(key, edgeIndices);
                }
                edgeIndices.Add(edgeIndex);
            }

            List<SeamEdgePair> candidates = new List<SeamEdgePair>();
            for (int left = 0; left < boundaryEdges.Count; left++)
            {
                BoundaryEdgeOccurrence a = boundaryEdges[left];
                Vector3 aStart = vertices[a.startVertex];
                Vector3 aEnd = vertices[a.endVertex];
                DirectedEdgeKey reversed = new DirectedEdgeKey(
                    snapIds[a.endVertex],
                    snapIds[a.startVertex]);
                if (!edgesByDirection.TryGetValue(reversed, out List<int> possibleMates))
                    continue;
                for (int mateIndex = 0; mateIndex < possibleMates.Count; mateIndex++)
                {
                    int right = possibleMates[mateIndex];
                    if (right <= left)
                        continue;
                    BoundaryEdgeOccurrence b = boundaryEdges[right];
                    float startDistance =
                        (aStart - vertices[b.endVertex]).sqrMagnitude;
                    if (startDistance > squaredTolerance)
                        continue;
                    float endDistance =
                        (aEnd - vertices[b.startVertex]).sqrMagnitude;
                    if (endDistance > squaredTolerance)
                        continue;
                    candidates.Add(new SeamEdgePair(
                        left,
                        right,
                        startDistance + endDistance));
                }
            }
            if (candidates.Count == 0)
                return 0;

            candidates.Sort((left, right) => left.score.CompareTo(right.score));
            bool[] matched = new bool[boundaryEdges.Count];
            int[] parents = new int[vertices.Count];
            Dictionary<int, int> firstVertexByStrictId = new Dictionary<int, int>();
            for (int vertex = 0; vertex < vertices.Count; vertex++)
            {
                parents[vertex] = vertex;
                if (firstVertexByStrictId.TryGetValue(
                        strictIds[vertex],
                        out int firstVertex))
                {
                    UnionPositionRoots(parents, firstVertex, vertex);
                }
                else
                {
                    firstVertexByStrictId.Add(strictIds[vertex], vertex);
                }
            }

            int matchedPairs = 0;
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                SeamEdgePair candidate = candidates[candidateIndex];
                if (matched[candidate.left] || matched[candidate.right])
                    continue;
                BoundaryEdgeOccurrence left = boundaryEdges[candidate.left];
                BoundaryEdgeOccurrence right = boundaryEdges[candidate.right];
                UnionPositionRoots(parents, left.startVertex, right.endVertex);
                UnionPositionRoots(parents, left.endVertex, right.startVertex);
                matched[candidate.left] = true;
                matched[candidate.right] = true;
                matchedPairs++;
            }

            if (matchedPairs == 0)
                return 0;
            for (int vertex = 0; vertex < vertices.Count; vertex++)
                vertices[vertex] = vertices[FindPositionRoot(parents, vertex)];
            return matchedPairs;
        }

        private static int RemoveGeometricDegenerateTriangles(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            float minimumAltitude)
        {
            float minimumAltitudeSquared = minimumAltitude * minimumAltitude;
            int removedTriangles = 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> kept = new List<int>(source.Count);
                List<bool> keptFlags = new List<bool>(sourceFlags.Count);
                for (int triangleStart = 0;
                     triangleStart + 2 < source.Count;
                     triangleStart += 3)
                {
                    Vector3 a = vertices[source[triangleStart]];
                    Vector3 b = vertices[source[triangleStart + 1]];
                    Vector3 c = vertices[source[triangleStart + 2]];
                    Vector3 ab = b - a;
                    Vector3 ac = c - a;
                    Vector3 bc = c - b;
                    float longestEdgeSquared = Mathf.Max(
                        ab.sqrMagnitude,
                        Mathf.Max(ac.sqrMagnitude, bc.sqrMagnitude));
                    if (longestEdgeSquared <= minimumAltitudeSquared ||
                        Vector3.Cross(ab, ac).sqrMagnitude <=
                        longestEdgeSquared * minimumAltitudeSquared)
                    {
                        removedTriangles++;
                        continue;
                    }
                    kept.Add(source[triangleStart]);
                    kept.Add(source[triangleStart + 1]);
                    kept.Add(source[triangleStart + 2]);
                    keptFlags.Add(sourceFlags[triangleStart / 3]);
                }
                submeshIndices[submesh] = kept;
                cutTriangleFlags[submesh] = keptFlags;
            }
            return removedTriangles;
        }

        private static int RemoveWeldCollapsedTriangles(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            float positionTolerance)
        {
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            int removedTriangles = 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> kept = new List<int>(source.Count);
                List<bool> keptFlags = new List<bool>(sourceFlags.Count);
                for (int triangleStart = 0;
                     triangleStart + 2 < source.Count;
                     triangleStart += 3)
                {
                    int a = source[triangleStart];
                    int b = source[triangleStart + 1];
                    int c = source[triangleStart + 2];
                    if (weldedIds[a] == weldedIds[b] ||
                        weldedIds[b] == weldedIds[c] ||
                        weldedIds[c] == weldedIds[a])
                    {
                        removedTriangles++;
                        continue;
                    }

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                    keptFlags.Add(sourceFlags[triangleStart / 3]);
                }
                submeshIndices[submesh] = kept;
                cutTriangleFlags[submesh] = keptFlags;
            }
            return removedTriangles;
        }

        private static int RemoveCoincidentTriangleDuplicates(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            float positionTolerance)
        {
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            Dictionary<TriangleKey, List<TriangleOccurrence>> groups =
                new Dictionary<TriangleKey, List<TriangleOccurrence>>();
            bool[][] keep = new bool[submeshIndices.Length][];
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                int triangleCount = triangles.Count / 3;
                keep[submesh] = new bool[triangleCount];
                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    keep[submesh][triangle] = true;
                    int start = triangle * 3;
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    if (a == b || b == c || c == a)
                        continue;
                    TriangleKey key = new TriangleKey(a, b, c);
                    if (!groups.TryGetValue(
                            key,
                            out List<TriangleOccurrence> occurrences))
                    {
                        occurrences = new List<TriangleOccurrence>();
                        groups.Add(key, occurrences);
                    }
                    occurrences.Add(new TriangleOccurrence(
                        submesh,
                        triangle,
                        key.IsPositiveWinding(a, b, c),
                        cutTriangleFlags[submesh][triangle]));
                }
            }

            int removedTriangles = 0;
            foreach (KeyValuePair<TriangleKey, List<TriangleOccurrence>> pair in groups)
            {
                List<TriangleOccurrence> occurrences = pair.Value;
                if (occurrences.Count <= 1)
                    continue;

                int positiveCount = 0;
                int negativeCount = 0;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    if (occurrences[i].positive)
                        positiveCount++;
                    else
                        negativeCount++;
                    keep[occurrences[i].submesh][occurrences[i].triangle] = false;
                }

                bool keepPositive = positiveCount > negativeCount;
                bool keepNegative = negativeCount > positiveCount;
                if (keepPositive || keepNegative)
                {
                    int survivor = -1;
                    for (int i = 0; i < occurrences.Count; i++)
                    {
                        if (occurrences[i].positive != keepPositive)
                            continue;
                        if (survivor < 0 ||
                            (!occurrences[survivor].cutFlag &&
                             occurrences[i].cutFlag))
                            survivor = i;
                    }
                    if (survivor >= 0)
                    {
                        TriangleOccurrence kept = occurrences[survivor];
                        keep[kept.submesh][kept.triangle] = true;
                    }
                }
                removedTriangles += occurrences.Count -
                                    ((keepPositive || keepNegative) ? 1 : 0);
            }

            if (removedTriangles == 0)
                return 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> kept = new List<int>(source.Count);
                List<bool> keptFlags = new List<bool>(sourceFlags.Count);
                for (int triangle = 0; triangle < keep[submesh].Length; triangle++)
                {
                    if (!keep[submesh][triangle])
                        continue;
                    int start = triangle * 3;
                    kept.Add(source[start]);
                    kept.Add(source[start + 1]);
                    kept.Add(source[start + 2]);
                    keptFlags.Add(sourceFlags[triangle]);
                }
                submeshIndices[submesh] = kept;
                cutTriangleFlags[submesh] = keptFlags;
            }
            return removedTriangles;
        }

        private static int RemoveRedundantBoundaryWedges(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            float positionTolerance)
        {
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            Dictionary<EdgeKey, EdgeIncidence> edgeCounts =
                new Dictionary<EdgeKey, EdgeIncidence>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int start = 0; start + 2 < triangles.Count; start += 3)
                {
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    AddEdge(edgeCounts, a, b);
                    AddEdge(edgeCounts, b, c);
                    AddEdge(edgeCounts, c, a);
                }
            }

            bool[][] remove = new bool[submeshIndices.Length][];
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
                remove[submesh] = new bool[submeshIndices[submesh].Count / 3];
            HashSet<EdgeKey> claimedEdges = new HashSet<EdgeKey>();
            int removedTriangles = 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangle = 0; triangle < triangles.Count / 3; triangle++)
                {
                    int start = triangle * 3;
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    if (a == b || b == c || c == a)
                        continue;
                    EdgeKey[] edges =
                    {
                        new EdgeKey(a, b),
                        new EdgeKey(b, c),
                        new EdgeKey(c, a)
                    };
                    int[] signs =
                    {
                        a < b ? 1 : -1,
                        b < c ? 1 : -1,
                        c < a ? 1 : -1
                    };
                    int boundaryCount = 0;
                    int repairableConflictCount = 0;
                    bool valid = true;
                    for (int edge = 0; edge < 3; edge++)
                    {
                        EdgeIncidence incidence = edgeCounts[edges[edge]];
                        if (incidence.count == 1)
                        {
                            boundaryCount++;
                        }
                        else if (incidence.count == 3 &&
                                 incidence.directionSum - signs[edge] == 0)
                        {
                            repairableConflictCount++;
                        }
                        else
                        {
                            valid = false;
                            break;
                        }
                    }
                    // Every accepted edge has a proof of what deletion does:
                    // a boundary edge disappears (1 -> 0), while a conflict
                    // becomes a balanced manifold edge (3 -> 2 with opposite
                    // winding). Support both redundant-wedge arrangements,
                    // 2-boundary/1-conflict and 1-boundary/2-conflict. The latter
                    // is produced when a BSP sliver bridges two overlapping
                    // triangulations at a connected wall seam.
                    if (!valid ||
                        boundaryCount < 1 ||
                        repairableConflictCount < 1 ||
                        claimedEdges.Contains(edges[0]) ||
                        claimedEdges.Contains(edges[1]) ||
                        claimedEdges.Contains(edges[2]))
                    {
                        continue;
                    }
                    remove[submesh][triangle] = true;
                    claimedEdges.Add(edges[0]);
                    claimedEdges.Add(edges[1]);
                    claimedEdges.Add(edges[2]);
                    removedTriangles++;
                }
            }
            if (removedTriangles == 0)
                return 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> kept = new List<int>(source.Count);
                List<bool> keptFlags = new List<bool>(sourceFlags.Count);
                for (int triangle = 0; triangle < remove[submesh].Length; triangle++)
                {
                    if (remove[submesh][triangle])
                        continue;
                    int start = triangle * 3;
                    kept.Add(source[start]);
                    kept.Add(source[start + 1]);
                    kept.Add(source[start + 2]);
                    keptFlags.Add(sourceFlags[triangle]);
                }
                submeshIndices[submesh] = kept;
                cutTriangleFlags[submesh] = keptFlags;
            }
            return removedTriangles;
        }

        private static int RemoveRedundantTopologyFlaps(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            float positionTolerance)
        {
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            Dictionary<EdgeKey, EdgeIncidence> edgeCounts =
                new Dictionary<EdgeKey, EdgeIncidence>();
            Dictionary<EdgeKey, List<TopologyEdgeOccurrence>> edgeOccurrences =
                new Dictionary<EdgeKey, List<TopologyEdgeOccurrence>>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangle = 0; triangle < triangles.Count / 3; triangle++)
                {
                    int start = triangle * 3;
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    if (a == b || b == c || c == a)
                        continue;
                    TopologyTriangleId triangleId = new TopologyTriangleId(
                        submesh,
                        triangle);
                    AddTopologyEdgeOccurrence(
                        edgeCounts, edgeOccurrences, triangleId, a, b);
                    AddTopologyEdgeOccurrence(
                        edgeCounts, edgeOccurrences, triangleId, b, c);
                    AddTopologyEdgeOccurrence(
                        edgeCounts, edgeOccurrences, triangleId, c, a);
                }
            }

            Dictionary<TopologyTriangleId, TopologyFlapCandidate> candidates =
                new Dictionary<TopologyTriangleId, TopologyFlapCandidate>();
            List<TopologyTriangleId> candidateOrder =
                new List<TopologyTriangleId>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangle = 0; triangle < triangles.Count / 3; triangle++)
                {
                    int start = triangle * 3;
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    if (a == b || b == c || c == a)
                        continue;

                    EdgeKey[] keys =
                    {
                        new EdgeKey(a, b),
                        new EdgeKey(b, c),
                        new EdgeKey(c, a)
                    };
                    int[] directionSigns =
                    {
                        a < b ? 1 : -1,
                        b < c ? 1 : -1,
                        c < a ? 1 : -1
                    };
                    int boundaryCount = 0;
                    int conflictCount = 0;
                    int regularCount = 0;
                    EdgeKey boundaryEdge = default;
                    EdgeKey conflictEdge = default;
                    EdgeKey regularEdge = default;
                    bool usable = true;
                    for (int edge = 0; edge < 3; edge++)
                    {
                        EdgeIncidence incidence = edgeCounts[keys[edge]];
                        if (incidence.count == 1)
                        {
                            boundaryCount++;
                            boundaryEdge = keys[edge];
                        }
                        else if (incidence.count == 3 &&
                                 incidence.directionSum - directionSigns[edge] == 0)
                        {
                            // This triangle is the unique removable occurrence:
                            // deleting it leaves a balanced two-face edge.
                            conflictCount++;
                            conflictEdge = keys[edge];
                        }
                        else if (incidence.count == 2 &&
                                 incidence.directionSum == 0)
                        {
                            regularCount++;
                            regularEdge = keys[edge];
                        }
                        else
                        {
                            usable = false;
                            break;
                        }
                    }
                    if (!usable ||
                        boundaryCount != 1 ||
                        conflictCount != 1 ||
                        regularCount != 1)
                    {
                        continue;
                    }

                    TopologyTriangleId triangleId = new TopologyTriangleId(
                        submesh,
                        triangle);
                    candidates.Add(
                        triangleId,
                        new TopologyFlapCandidate(
                            boundaryEdge,
                            conflictEdge,
                            regularEdge));
                    candidateOrder.Add(triangleId);
                }
            }
            if (candidates.Count < 2)
                return 0;

            bool[][] remove = new bool[submeshIndices.Length][];
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
                remove[submesh] = new bool[submeshIndices[submesh].Count / 3];
            HashSet<TopologyTriangleId> consumed =
                new HashSet<TopologyTriangleId>();
            HashSet<EdgeKey> claimedDefectEdges = new HashSet<EdgeKey>();
            int removedTriangles = 0;
            for (int index = 0; index < candidateOrder.Count; index++)
            {
                TopologyTriangleId currentId = candidateOrder[index];
                if (consumed.Contains(currentId))
                    continue;
                TopologyFlapCandidate current = candidates[currentId];
                List<TopologyEdgeOccurrence> regularOccurrences =
                    edgeOccurrences[current.regularEdge];
                if (regularOccurrences.Count != 2)
                    continue;

                TopologyTriangleId otherId;
                if (regularOccurrences[0].triangle.Equals(currentId))
                    otherId = regularOccurrences[1].triangle;
                else if (regularOccurrences[1].triangle.Equals(currentId))
                    otherId = regularOccurrences[0].triangle;
                else
                    continue;
                if (otherId.Equals(currentId) ||
                    consumed.Contains(otherId) ||
                    !candidates.TryGetValue(
                        otherId,
                        out TopologyFlapCandidate other) ||
                    !other.regularEdge.Equals(current.regularEdge) ||
                    other.boundaryEdge.Equals(current.boundaryEdge) ||
                    other.conflictEdge.Equals(current.conflictEdge))
                {
                    continue;
                }
                if (claimedDefectEdges.Contains(current.boundaryEdge) ||
                    claimedDefectEdges.Contains(current.conflictEdge) ||
                    claimedDefectEdges.Contains(other.boundaryEdge) ||
                    claimedDefectEdges.Contains(other.conflictEdge))
                {
                    continue;
                }

                remove[currentId.submesh][currentId.triangle] = true;
                remove[otherId.submesh][otherId.triangle] = true;
                consumed.Add(currentId);
                consumed.Add(otherId);
                claimedDefectEdges.Add(current.boundaryEdge);
                claimedDefectEdges.Add(current.conflictEdge);
                claimedDefectEdges.Add(other.boundaryEdge);
                claimedDefectEdges.Add(other.conflictEdge);
                removedTriangles += 2;
            }
            if (removedTriangles == 0)
                return 0;

            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> kept = new List<int>(source.Count);
                List<bool> keptFlags = new List<bool>(sourceFlags.Count);
                for (int triangle = 0; triangle < remove[submesh].Length; triangle++)
                {
                    if (remove[submesh][triangle])
                        continue;
                    int start = triangle * 3;
                    kept.Add(source[start]);
                    kept.Add(source[start + 1]);
                    kept.Add(source[start + 2]);
                    keptFlags.Add(sourceFlags[triangle]);
                }
                submeshIndices[submesh] = kept;
                cutTriangleFlags[submesh] = keptFlags;
            }
            return removedTriangles;
        }

        private static int CountTriangles(List<int>[] submeshIndices)
        {
            int triangleCount = 0;
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
                triangleCount += submeshIndices[submesh].Count / 3;
            return triangleCount;
        }

        private static void CountTopologyDefects(
            List<int>[] submeshIndices,
            List<Vector3> vertices,
            float positionTolerance,
            out int boundaryEdges,
            out int conflictingEdges)
        {
            boundaryEdges = 0;
            conflictingEdges = 0;
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            Dictionary<EdgeKey, EdgeIncidence> edges =
                new Dictionary<EdgeKey, EdgeIncidence>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int start = 0; start + 2 < triangles.Count; start += 3)
                {
                    int a = weldedIds[triangles[start]];
                    int b = weldedIds[triangles[start + 1]];
                    int c = weldedIds[triangles[start + 2]];
                    if (a == b || b == c || c == a)
                    {
                        conflictingEdges++;
                        continue;
                    }
                    AddEdge(edges, a, b);
                    AddEdge(edges, b, c);
                    AddEdge(edges, c, a);
                }
            }
            foreach (KeyValuePair<EdgeKey, EdgeIncidence> pair in edges)
            {
                if (pair.Value.count == 1)
                    boundaryEdges++;
                else if (pair.Value.count != 2 || pair.Value.directionSum != 0)
                    conflictingEdges++;
            }
        }

        private static void TraceTopologyRecovery(string message)
        {
            if (TopologyDiagnosticsEnabled)
                Debug.Log("[RealBlend Boolean topology] " + message);
        }

        private static void AddTopologyEdgeOccurrence(
            Dictionary<EdgeKey, EdgeIncidence> counts,
            Dictionary<EdgeKey, List<TopologyEdgeOccurrence>> occurrences,
            TopologyTriangleId triangle,
            int startPositionId,
            int endPositionId)
        {
            EdgeKey key = new EdgeKey(startPositionId, endPositionId);
            int directionSign = startPositionId < endPositionId ? 1 : -1;
            counts.TryGetValue(key, out EdgeIncidence incidence);
            incidence.count++;
            incidence.directionSum += directionSign;
            counts[key] = incidence;
            if (!occurrences.TryGetValue(
                    key,
                    out List<TopologyEdgeOccurrence> edgeOccurrences))
            {
                edgeOccurrences = new List<TopologyEdgeOccurrence>(2);
                occurrences.Add(key, edgeOccurrences);
            }
            edgeOccurrences.Add(new TopologyEdgeOccurrence(
                triangle,
                directionSign));
        }

        private static int CollapseShortNonManifoldEdges(
            List<int>[] submeshIndices,
            List<Vector3> vertices,
            float positionTolerance,
            float maximumEdgeLength)
        {
            int[] weldedIds = BuildWeldedPositionIds(
                vertices,
                positionTolerance,
                out _);
            Dictionary<EdgeKey, NonManifoldEdgeInfo> edges =
                new Dictionary<EdgeKey, NonManifoldEdgeInfo>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int start = 0; start + 2 < triangles.Count; start += 3)
                {
                    AddNonManifoldEdgeInfo(
                        edges, weldedIds, triangles[start], triangles[start + 1]);
                    AddNonManifoldEdgeInfo(
                        edges, weldedIds, triangles[start + 1], triangles[start + 2]);
                    AddNonManifoldEdgeInfo(
                        edges, weldedIds, triangles[start + 2], triangles[start]);
                }
            }

            int[] parents = new int[vertices.Count];
            Dictionary<int, int> firstVertexByWeldedId = new Dictionary<int, int>();
            for (int vertex = 0; vertex < vertices.Count; vertex++)
            {
                parents[vertex] = vertex;
                if (firstVertexByWeldedId.TryGetValue(
                        weldedIds[vertex],
                        out int firstVertex))
                {
                    UnionPositionRoots(parents, firstVertex, vertex);
                }
                else
                {
                    firstVertexByWeldedId.Add(weldedIds[vertex], vertex);
                }
            }

            float maximumLengthSquared = maximumEdgeLength * maximumEdgeLength;
            int collapsedEdges = 0;
            foreach (KeyValuePair<EdgeKey, NonManifoldEdgeInfo> pair in edges)
            {
                NonManifoldEdgeInfo edge = pair.Value;
                // Only contract a balanced, even pinch.  Long, unbalanced, or
                // same-direction edges signal a real topology error and must
                // remain visible to the manifold validator.
                if (edge.count <= 2 ||
                    (edge.count & 1) != 0 ||
                    edge.directionSum != 0)
                {
                    continue;
                }
                if ((vertices[edge.startVertex] - vertices[edge.endVertex])
                    .sqrMagnitude > maximumLengthSquared)
                {
                    continue;
                }
                UnionPositionRoots(parents, edge.startVertex, edge.endVertex);
                collapsedEdges++;
            }

            if (collapsedEdges == 0)
                return 0;
            for (int vertex = 0; vertex < vertices.Count; vertex++)
                vertices[vertex] = vertices[FindPositionRoot(parents, vertex)];
            return collapsedEdges;
        }

        private static void AddNonManifoldEdgeInfo(
            Dictionary<EdgeKey, NonManifoldEdgeInfo> edges,
            int[] weldedIds,
            int startVertex,
            int endVertex)
        {
            int startId = weldedIds[startVertex];
            int endId = weldedIds[endVertex];
            if (startId == endId)
                return;
            EdgeKey key = new EdgeKey(startId, endId);
            edges.TryGetValue(key, out NonManifoldEdgeInfo edge);
            if (edge.count == 0)
            {
                edge.startVertex = startVertex;
                edge.endVertex = endVertex;
            }
            edge.count++;
            edge.directionSum += startId < endId ? 1 : -1;
            edges[key] = edge;
        }

        private static void ResolveTriangleTJunctions(
            List<int>[] submeshIndices,
            List<bool>[] cutTriangleFlags,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv0,
            List<Vector2> uv2,
            List<Color> colors,
            float positionTolerance)
        {
            float tolerance = Mathf.Max(0.000001f, positionTolerance);
            float sqrTolerance = tolerance * tolerance;
            int inputTriangleCount = 0;
            for (int i = 0; i < submeshIndices.Length; i++)
                inputTriangleCount += submeshIndices[i].Count / 3;
            int maximumSplits = Mathf.Clamp(inputTriangleCount * 8, 1024, 100000);
            int[] weldedVertexIds = BuildWeldedPositionIds(
                vertices,
                tolerance,
                out List<Vector3> representativePositions);

            Dictionary<EdgeKey, int> edgeIncidence = new Dictionary<EdgeKey, int>();
            HashSet<int> boundaryPositionIds = new HashSet<int>();
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangleStart = 0;
                     triangleStart + 2 < triangles.Count;
                     triangleStart += 3)
                {
                    CountUndirectedEdge(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart], triangles[triangleStart + 1]);
                    CountUndirectedEdge(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart + 1], triangles[triangleStart + 2]);
                    CountUndirectedEdge(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart + 2], triangles[triangleStart]);
                }
            }
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> triangles = submeshIndices[submesh];
                for (int triangleStart = 0;
                     triangleStart + 2 < triangles.Count;
                     triangleStart += 3)
                {
                    AddBoundaryPositionIds(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart], triangles[triangleStart + 1],
                        boundaryPositionIds);
                    AddBoundaryPositionIds(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart + 1], triangles[triangleStart + 2],
                        boundaryPositionIds);
                    AddBoundaryPositionIds(edgeIncidence, weldedVertexIds,
                        triangles[triangleStart + 2], triangles[triangleStart],
                        boundaryPositionIds);
                }
            }
            if (boundaryPositionIds.Count < 3)
                return;

            PositionAxisIndex positionIndex = new PositionAxisIndex(
                representativePositions,
                boundaryPositionIds);
            List<int> rangeCandidates = new List<int>();
            List<IndexedEdgeSplit> edgeSplits = new List<IndexedEdgeSplit>();
            List<int> perimeter = new List<int>();
            int totalSplits = 0;

            // Rebuild every affected source triangle atomically. Mutating a
            // triangle in-place makes all saved triangle offsets stale and was
            // the cause of both the 20,000-pass loop and multi-edge corruption.
            for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            {
                List<int> source = submeshIndices[submesh];
                List<bool> sourceFlags = cutTriangleFlags[submesh];
                List<int> rebuilt = new List<int>(source.Count);
                List<bool> rebuiltFlags = new List<bool>(sourceFlags.Count);
                for (int triangleStart = 0;
                     triangleStart + 2 < source.Count;
                     triangleStart += 3)
                {
                    int a = source[triangleStart];
                    int b = source[triangleStart + 1];
                    int c = source[triangleStart + 2];
                    bool cutFlag = sourceFlags[triangleStart / 3];
                    perimeter.Clear();
                    perimeter.Add(a);
                    totalSplits += AppendTriangleEdgeSplits(
                        a, b, edgeIncidence, weldedVertexIds,
                        representativePositions, positionIndex,
                        rangeCandidates, edgeSplits, tolerance, sqrTolerance,
                        vertices, normals, uv0, uv2, colors, perimeter);
                    perimeter.Add(b);
                    totalSplits += AppendTriangleEdgeSplits(
                        b, c, edgeIncidence, weldedVertexIds,
                        representativePositions, positionIndex,
                        rangeCandidates, edgeSplits, tolerance, sqrTolerance,
                        vertices, normals, uv0, uv2, colors, perimeter);
                    perimeter.Add(c);
                    totalSplits += AppendTriangleEdgeSplits(
                        c, a, edgeIncidence, weldedVertexIds,
                        representativePositions, positionIndex,
                        rangeCandidates, edgeSplits, tolerance, sqrTolerance,
                        vertices, normals, uv0, uv2, colors, perimeter);

                    if (totalSplits > maximumSplits)
                    {
                        throw new BooleanWorkBudgetExceededException(
                            "Boolean seam repair exceeded its topology safety limit.");
                    }
                    if (perimeter.Count == 3)
                    {
                        rebuilt.Add(a);
                        rebuilt.Add(b);
                        rebuilt.Add(c);
                        rebuiltFlags.Add(cutFlag);
                        continue;
                    }

                    // A corner fan creates zero-area triangles when several
                    // splits lie on that corner's edge. Fan around a true
                    // interior point instead, keeping the original winding.
                    int center = AddTriangleCenterVertex(
                        a, b, c, vertices, normals, uv0, uv2, colors);
                    for (int ringIndex = 0; ringIndex < perimeter.Count; ringIndex++)
                    {
                        int next = (ringIndex + 1) % perimeter.Count;
                        rebuilt.Add(center);
                        rebuilt.Add(perimeter[ringIndex]);
                        rebuilt.Add(perimeter[next]);
                        rebuiltFlags.Add(cutFlag);
                    }
                }
                submeshIndices[submesh] = rebuilt;
                cutTriangleFlags[submesh] = rebuiltFlags;
            }
        }

        private static void CountUndirectedEdge(
            Dictionary<EdgeKey, int> counts,
            int[] weldedIds,
            int startVertex,
            int endVertex)
        {
            int startId = weldedIds[startVertex];
            int endId = weldedIds[endVertex];
            if (startId == endId)
                return;
            EdgeKey key = new EdgeKey(startId, endId);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static void AddBoundaryPositionIds(
            Dictionary<EdgeKey, int> counts,
            int[] weldedIds,
            int startVertex,
            int endVertex,
            HashSet<int> boundaryPositionIds)
        {
            int startId = weldedIds[startVertex];
            int endId = weldedIds[endVertex];
            if (startId == endId ||
                !counts.TryGetValue(new EdgeKey(startId, endId), out int count) ||
                count != 1)
            {
                return;
            }
            boundaryPositionIds.Add(startId);
            boundaryPositionIds.Add(endId);
        }

        private static int AppendTriangleEdgeSplits(
            int startVertex,
            int endVertex,
            Dictionary<EdgeKey, int> edgeIncidence,
            int[] weldedVertexIds,
            List<Vector3> representativePositions,
            PositionAxisIndex positionIndex,
            List<int> rangeCandidates,
            List<IndexedEdgeSplit> edgeSplits,
            float tolerance,
            float squaredTolerance,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv0,
            List<Vector2> uv2,
            List<Color> colors,
            List<int> perimeter)
        {
            int startPositionId = weldedVertexIds[startVertex];
            int endPositionId = weldedVertexIds[endVertex];
            if (startPositionId == endPositionId ||
                !edgeIncidence.TryGetValue(
                    new EdgeKey(startPositionId, endPositionId),
                    out int incidence) ||
                incidence != 1)
            {
                return 0;
            }

            Vector3 start = vertices[startVertex];
            Vector3 end = vertices[endVertex];
            Vector3 direction = end - start;
            float lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= squaredTolerance)
                return 0;

            float endpointMargin = tolerance / Mathf.Sqrt(lengthSquared);
            rangeCandidates.Clear();
            positionIndex.CollectSegmentRange(start, end, tolerance, rangeCandidates);
            edgeSplits.Clear();
            for (int candidateIndex = 0;
                 candidateIndex < rangeCandidates.Count;
                 candidateIndex++)
            {
                int positionId = rangeCandidates[candidateIndex];
                if (positionId == startPositionId || positionId == endPositionId)
                    continue;
                Vector3 candidate = representativePositions[positionId];
                float amount = Vector3.Dot(candidate - start, direction) / lengthSquared;
                if (amount <= endpointMargin || amount >= 1f - endpointMargin)
                    continue;
                Vector3 closest = start + direction * amount;
                if ((candidate - closest).sqrMagnitude > squaredTolerance)
                    continue;
                edgeSplits.Add(new IndexedEdgeSplit(amount, positionId, candidate));
            }
            if (edgeSplits.Count == 0)
                return 0;

            edgeSplits.Sort(CompareIndexedEdgeSplits);
            int added = 0;
            float previousAmount = -1f;
            for (int splitIndex = 0; splitIndex < edgeSplits.Count; splitIndex++)
            {
                IndexedEdgeSplit split = edgeSplits[splitIndex];
                if (previousAmount >= 0f &&
                    Mathf.Abs(split.amount - previousAmount) <= endpointMargin)
                {
                    continue;
                }
                int newVertex = vertices.Count;
                vertices.Add(split.position);
                Vector3 normal = Vector3.LerpUnclamped(
                    normals[startVertex], normals[endVertex], split.amount);
                normals.Add(normal.sqrMagnitude > 0.0000001f
                    ? normal.normalized
                    : Vector3.up);
                uv0.Add(Vector2.LerpUnclamped(
                    uv0[startVertex], uv0[endVertex], split.amount));
                uv2.Add(Vector2.LerpUnclamped(
                    uv2[startVertex], uv2[endVertex], split.amount));
                colors.Add(Color.LerpUnclamped(
                    colors[startVertex], colors[endVertex], split.amount));
                perimeter.Add(newVertex);
                previousAmount = split.amount;
                added++;
            }
            return added;
        }

        private static int CompareIndexedEdgeSplits(
            IndexedEdgeSplit left,
            IndexedEdgeSplit right)
        {
            int amountComparison = left.amount.CompareTo(right.amount);
            return amountComparison != 0
                ? amountComparison
                : left.positionId.CompareTo(right.positionId);
        }

        private static int AddTriangleCenterVertex(
            int a,
            int b,
            int c,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv0,
            List<Vector2> uv2,
            List<Color> colors)
        {
            int center = vertices.Count;
            vertices.Add((vertices[a] + vertices[b] + vertices[c]) / 3f);
            Vector3 normal = normals[a] + normals[b] + normals[c];
            normals.Add(normal.sqrMagnitude > 0.0000001f
                ? normal.normalized
                : Vector3.up);
            uv0.Add((uv0[a] + uv0[b] + uv0[c]) / 3f);
            uv2.Add((uv2[a] + uv2[b] + uv2[c]) / 3f);
            colors.Add((colors[a] + colors[b] + colors[c]) / 3f);
            return center;
        }

        private static void AddBoundaryOccurrence(
            Dictionary<EdgeKey, List<BoundaryEdgeOccurrence>> occurrences,
            int[] weldedVertexIds,
            int submesh,
            int triangleStart,
            int startVertex,
            int endVertex,
            int oppositeVertex)
        {
            int startId = weldedVertexIds[startVertex];
            int endId = weldedVertexIds[endVertex];
            if (startId == endId)
                return;
            EdgeKey key = new EdgeKey(startId, endId);
            if (!occurrences.TryGetValue(key, out List<BoundaryEdgeOccurrence> list))
            {
                list = new List<BoundaryEdgeOccurrence>();
                occurrences.Add(key, list);
            }
            list.Add(new BoundaryEdgeOccurrence(
                submesh,
                triangleStart,
                startVertex,
                endVertex,
                oppositeVertex));
        }

        /// <summary>
        /// BSP clipping can leave a vertex from one polygon resting in the middle
        /// of a neighboring polygon edge. The surfaces touch visually, but that is
        /// still a topological T-junction. Split every affected edge before
        /// triangulation so the Boolean result is a genuinely sealed mesh.
        /// </summary>
        private static void ResolveTJunctions(
            List<BooleanPolygon> polygons,
            float positionTolerance,
            BooleanWorkBudget workBudget,
            int maximumOutputTriangles)
        {
            if (polygons == null || polygons.Count == 0)
                return;

            float tolerance = Mathf.Max(0.000001f, positionTolerance);
            float sqrTolerance = tolerance * tolerance;
            Dictionary<PositionKey, Vector3> uniquePositions =
                new Dictionary<PositionKey, Vector3>();
            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                List<BooleanVertex> polygonVertices = polygons[polygonIndex].vertices;
                workBudget.ConsumeOperations(polygonVertices.Count);
                for (int vertexIndex = 0; vertexIndex < polygonVertices.Count; vertexIndex++)
                {
                    Vector3 position = polygonVertices[vertexIndex].position;
                    PositionKey key = new PositionKey(position, tolerance);
                    if (!uniquePositions.ContainsKey(key))
                        uniquePositions.Add(key, position);
                }
            }

            List<Vector3> candidatePositions =
                new List<Vector3>(uniquePositions.Values);
            List<int> candidatePositionIds = new List<int>(candidatePositions.Count);
            for (int candidateIndex = 0;
                 candidateIndex < candidatePositions.Count;
                 candidateIndex++)
            {
                candidatePositionIds.Add(candidateIndex);
            }
            PositionAxisIndex positionIndex = new PositionAxisIndex(
                candidatePositions,
                candidatePositionIds);
            List<int> rangeCandidates = new List<int>();
            List<EdgeSplit> splits = new List<EdgeSplit>();
            long triangulatedCount = CountTriangulatedPolygons(
                polygons,
                maximumOutputTriangles);
            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                BooleanPolygon polygon = polygons[polygonIndex];
                List<BooleanVertex> source =
                    new List<BooleanVertex>(polygon.vertices);
                List<BooleanVertex> resolved = new List<BooleanVertex>();
                for (int edgeIndex = 0; edgeIndex < source.Count; edgeIndex++)
                {
                    BooleanVertex start = source[edgeIndex];
                    BooleanVertex end = source[(edgeIndex + 1) % source.Count];
                    resolved.Add(start);
                    Vector3 direction = end.position - start.position;
                    float lengthSquared = direction.sqrMagnitude;
                    if (lengthSquared <= sqrTolerance)
                        continue;

                    splits.Clear();
                    float endpointMargin = tolerance / Mathf.Sqrt(lengthSquared);
                    rangeCandidates.Clear();
                    positionIndex.CollectSegmentRange(
                        start.position,
                        end.position,
                        tolerance,
                        rangeCandidates);
                    for (int rangeIndex = 0;
                         rangeIndex < rangeCandidates.Count;
                         rangeIndex++)
                    {
                        workBudget.ConsumeOperation();
                        int candidateIndex = rangeCandidates[rangeIndex];
                        Vector3 candidate = candidatePositions[candidateIndex];
                        float amount = Vector3.Dot(
                            candidate - start.position,
                            direction) / lengthSquared;
                        if (amount <= endpointMargin || amount >= 1f - endpointMargin)
                            continue;
                        Vector3 closest = start.position + direction * amount;
                        if ((candidate - closest).sqrMagnitude > sqrTolerance)
                            continue;
                        splits.Add(new EdgeSplit(amount, candidate));
                    }

                    if (splits.Count == 0)
                        continue;
                    splits.Sort((left, right) => left.amount.CompareTo(right.amount));
                    float previousAmount = -1f;
                    for (int splitIndex = 0; splitIndex < splits.Count; splitIndex++)
                    {
                        float amount = splits[splitIndex].amount;
                        if (previousAmount >= 0f &&
                            Mathf.Abs(amount - previousAmount) <= endpointMargin)
                        {
                            continue;
                        }
                        BooleanVertex splitVertex = start.Interpolate(end, amount);
                        // Use the already-existing seam vertex verbatim.  The
                        // projected point can differ by up to the T-junction
                        // tolerance on dense BSP output, leaving two almost
                        // coincident (but topologically distinct) edge ends.
                        splitVertex.position = splits[splitIndex].position;
                        if (triangulatedCount >= maximumOutputTriangles)
                        {
                            throw new BooleanWorkBudgetExceededException(
                                string.Format(
                                    "seam repair exceeded the {0:N0}-triangle output budget.",
                                    maximumOutputTriangles));
                        }
                        resolved.Add(splitVertex);
                        triangulatedCount++;
                        previousAmount = amount;
                    }
                }

                polygon.vertices.Clear();
                polygon.vertices.AddRange(resolved);
            }
        }

        private static int GetOrAddVertex(
            BooleanVertex vertex,
            float weldTolerance,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv0,
            List<Vector2> uv2,
            List<Color> colors,
            Dictionary<OutputVertexKey, int> lookup)
        {
            OutputVertexKey key = new OutputVertexKey(vertex, weldTolerance);
            if (lookup.TryGetValue(key, out int existing))
                return existing;

            int index = vertices.Count;
            lookup.Add(key, index);
            vertices.Add(vertex.position);
            normals.Add(vertex.normal.sqrMagnitude > 0.0000001f
                ? vertex.normal.normalized
                : Vector3.up);
            uv0.Add(vertex.uv0);
            uv2.Add(vertex.uv2);
            colors.Add(vertex.color);
            return index;
        }

        private sealed class BooleanWorkBudgetExceededException : Exception
        {
            public BooleanWorkBudgetExceededException(string message)
                : base(message)
            {
            }
        }

        private sealed class BooleanWorkBudget
        {
            private readonly long _maximumOperations;
            private readonly int _maximumGeneratedFragments;
            private readonly int _maximumLiveFragments;
            private readonly int _maximumMilliseconds;
            private readonly System.Diagnostics.Stopwatch _stopwatch;
            private long _operations;
            private int _generatedFragments;
            private long _nextTimeCheckOperation = 1024L;

            public BooleanWorkBudget(
                long maximumOperations,
                int maximumGeneratedFragments,
                int maximumLiveFragments,
                int maximumMilliseconds = 10000)
            {
                _maximumOperations = Math.Max(1L, maximumOperations);
                _maximumGeneratedFragments = Math.Max(
                    1,
                    maximumGeneratedFragments);
                _maximumLiveFragments = Math.Max(1, maximumLiveFragments);
                _maximumMilliseconds = Math.Max(100, maximumMilliseconds);
                _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            }

            public void ConsumeOperation()
            {
                ConsumeOperations(1L);
            }

            public void ConsumeOperations(long count)
            {
                if (count <= 0L)
                    return;
                _operations = _operations > long.MaxValue - count
                    ? long.MaxValue
                    : _operations + count;
                if (_operations > _maximumOperations)
                {
                    throw new BooleanWorkBudgetExceededException(
                        string.Format(
                            "the BSP fallback exceeded {0:N0} plane/fragment operations.",
                            _maximumOperations));
                }
                if (_operations >= _nextTimeCheckOperation)
                {
                    _nextTimeCheckOperation = _operations > long.MaxValue - 1024L
                        ? long.MaxValue
                        : _operations + 1024L;
                    if (_stopwatch.ElapsedMilliseconds > _maximumMilliseconds)
                    {
                        throw new BooleanWorkBudgetExceededException(
                            string.Format(
                                "the BSP fallback exceeded its {0:N0} ms time budget.",
                                _maximumMilliseconds));
                    }
                }
            }

            public void AddGeneratedFragments(int count)
            {
                if (count <= 0)
                    return;
                _generatedFragments = _generatedFragments > int.MaxValue - count
                    ? int.MaxValue
                    : _generatedFragments + count;
                if (_generatedFragments > _maximumGeneratedFragments)
                {
                    throw new BooleanWorkBudgetExceededException(
                        string.Format(
                            "the BSP fallback generated more than {0:N0} fragments.",
                            _maximumGeneratedFragments));
                }
            }

            public void ObserveLiveFragments(long count)
            {
                if (count > _maximumLiveFragments)
                {
                    throw new BooleanWorkBudgetExceededException(
                        string.Format(
                            "the BSP fallback exceeded {0:N0} live fragments.",
                            _maximumLiveFragments));
                }
            }
        }

        private sealed class BooleanNode
        {
            private readonly float _epsilon;
            private readonly BooleanWorkBudget _workBudget;
            private BooleanPlane _plane;
            private BooleanNode _front;
            private BooleanNode _back;
            private List<BooleanPolygon> _polygons = new List<BooleanPolygon>();

            public BooleanNode(
                List<BooleanPolygon> polygons,
                float epsilon,
                BooleanWorkBudget workBudget)
            {
                _epsilon = epsilon;
                _workBudget = workBudget;
                if (polygons != null && polygons.Count > 0)
                    Build(polygons);
            }

            private BooleanNode(
                float epsilon,
                BooleanWorkBudget workBudget)
            {
                _epsilon = epsilon;
                _workBudget = workBudget;
            }

            public void Invert()
            {
                Stack<BooleanNode> pending = new Stack<BooleanNode>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    BooleanNode node = pending.Pop();
                    for (int i = 0; i < node._polygons.Count; i++)
                        node._polygons[i].Flip();
                    node._plane?.Flip();

                    BooleanNode previousFront = node._front;
                    node._front = node._back;
                    node._back = previousFront;
                    if (node._front != null)
                        pending.Push(node._front);
                    if (node._back != null)
                        pending.Push(node._back);
                }
            }

            public List<BooleanPolygon> ClipPolygons(List<BooleanPolygon> polygons)
            {
                if (_plane == null)
                    return new List<BooleanPolygon>(polygons);

                List<BooleanPolygon> result = new List<BooleanPolygon>();
                // Queue preserves the legacy breadth-first ordering while
                // releasing processed references. The former append-only List
                // retained every intermediate polygon for the full pass.
                Queue<ClipWorkItem> pending =
                    new Queue<ClipWorkItem>(polygons.Count);
                for (int i = 0; i < polygons.Count; i++)
                    pending.Enqueue(new ClipWorkItem(this, polygons[i]));

                while (pending.Count > 0)
                {
                    _workBudget.ConsumeOperation();
                    ClipWorkItem work = pending.Dequeue();
                    BooleanNode node = work.node;
                    if (node._plane == null)
                    {
                        result.Add(work.polygon);
                        continue;
                    }

                    List<BooleanPolygon> front = new List<BooleanPolygon>(1);
                    List<BooleanPolygon> back = new List<BooleanPolygon>(1);
                    node._plane.SplitPolygon(
                        work.polygon,
                        front,
                        back,
                        front,
                        back,
                        node._epsilon,
                        node._workBudget);

                    for (int i = 0; i < front.Count; i++)
                    {
                        if (node._front != null)
                            pending.Enqueue(new ClipWorkItem(node._front, front[i]));
                        else
                            result.Add(front[i]);
                    }
                    if (node._back != null)
                    {
                        for (int i = 0; i < back.Count; i++)
                            pending.Enqueue(new ClipWorkItem(node._back, back[i]));
                    }
                    _workBudget.ObserveLiveFragments(
                        (long)pending.Count + result.Count);
                }
                return result;
            }

            public void ClipTo(BooleanNode other)
            {
                Stack<BooleanNode> pending = new Stack<BooleanNode>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    BooleanNode node = pending.Pop();
                    node._polygons = other.ClipPolygons(node._polygons);
                    if (node._front != null)
                        pending.Push(node._front);
                    if (node._back != null)
                        pending.Push(node._back);
                }
            }

            public List<BooleanPolygon> AllPolygons()
            {
                List<BooleanPolygon> polygons = new List<BooleanPolygon>();
                Stack<BooleanNode> pending = new Stack<BooleanNode>();
                pending.Push(this);
                while (pending.Count > 0)
                {
                    BooleanNode node = pending.Pop();
                    node._workBudget.ObserveLiveFragments(
                        (long)polygons.Count + node._polygons.Count);
                    polygons.AddRange(node._polygons);
                    if (node._back != null)
                        pending.Push(node._back);
                    if (node._front != null)
                        pending.Push(node._front);
                }
                return polygons;
            }

            public void Build(List<BooleanPolygon> polygons)
            {
                if (polygons == null || polygons.Count == 0)
                    return;

                Stack<BuildWorkItem> pending = new Stack<BuildWorkItem>();
                pending.Push(new BuildWorkItem(this, polygons));
                long pendingPolygonReferences = polygons.Count;
                while (pending.Count > 0)
                {
                    BuildWorkItem work = pending.Pop();
                    BooleanNode node = work.node;
                    List<BooleanPolygon> workPolygons = work.polygons;
                    pendingPolygonReferences -= workPolygons != null
                        ? workPolygons.Count
                        : 0;
                    if (workPolygons == null || workPolygons.Count == 0)
                        continue;
                    if (node._plane == null)
                    {
                        node._plane = ChooseSplitterPlane(
                            workPolygons,
                            node._epsilon,
                            node._workBudget);
                    }

                    List<BooleanPolygon> front = new List<BooleanPolygon>();
                    List<BooleanPolygon> back = new List<BooleanPolygon>();
                    for (int i = 0; i < workPolygons.Count; i++)
                    {
                        node._plane.SplitPolygon(
                            workPolygons[i],
                            node._polygons,
                            node._polygons,
                            front,
                            back,
                            node._epsilon,
                            node._workBudget);
                    }

                    if (back.Count > 0)
                    {
                        if (node._back == null)
                            node._back = new BooleanNode(
                                node._epsilon,
                                node._workBudget);
                        pending.Push(new BuildWorkItem(node._back, back));
                        pendingPolygonReferences += back.Count;
                    }
                    if (front.Count > 0)
                    {
                        if (node._front == null)
                            node._front = new BooleanNode(
                                node._epsilon,
                                node._workBudget);
                        pending.Push(new BuildWorkItem(node._front, front));
                        pendingPolygonReferences += front.Count;
                    }
                    _workBudget.ObserveLiveFragments(
                        pendingPolygonReferences + node._polygons.Count);
                }
            }

            private static BooleanPlane ChooseSplitterPlane(
                List<BooleanPolygon> polygons,
                float epsilon,
                BooleanWorkBudget workBudget)
            {
                // The legacy first-polygon splitter is numerically steadier for
                // ordinary and repeatedly cut meshes. Sampling is reserved for
                // genuinely dense polygon sets, where it prevents pathological
                // BSP depth without changing the established small-mesh path.
                const int minimumBalancedPolygonCount = 2048;
                if (polygons.Count < minimumBalancedPolygonCount)
                    return polygons[0].plane.Clone();

                const int maximumCandidateSamples = 32;
                const int spanningPenalty = 8;
                int candidateCount = Math.Min(
                    maximumCandidateSamples,
                    polygons.Count);
                int bestPolygonIndex = 0;
                int bestScore = int.MaxValue;
                for (int sample = 0; sample < candidateCount; sample++)
                {
                    int candidateIndex = candidateCount == polygons.Count
                        ? sample
                        : (int)((long)sample * (polygons.Count - 1) /
                                Math.Max(1, candidateCount - 1));
                    BooleanPlane candidate = polygons[candidateIndex].plane;
                    int frontCount = 0;
                    int backCount = 0;
                    int spanningCount = 0;
                    for (int polygonIndex = 0;
                         polygonIndex < polygons.Count;
                         polygonIndex++)
                    {
                        workBudget.ConsumeOperations(
                            polygons[polygonIndex].vertices.Count);
                        int classification = candidate.ClassifyPolygon(
                            polygons[polygonIndex],
                            epsilon);
                        if (classification == Front)
                            frontCount++;
                        else if (classification == Back)
                            backCount++;
                        else if (classification == Spanning)
                            spanningCount++;
                    }
                    int score = spanningCount * spanningPenalty +
                                Math.Abs(frontCount - backCount);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPolygonIndex = candidateIndex;
                        if (score == 0)
                            break;
                    }
                }
                return polygons[bestPolygonIndex].plane.Clone();
            }

            private readonly struct ClipWorkItem
            {
                public readonly BooleanNode node;
                public readonly BooleanPolygon polygon;

                public ClipWorkItem(BooleanNode node, BooleanPolygon polygon)
                {
                    this.node = node;
                    this.polygon = polygon;
                }
            }

            private readonly struct BuildWorkItem
            {
                public readonly BooleanNode node;
                public readonly List<BooleanPolygon> polygons;

                public BuildWorkItem(BooleanNode node, List<BooleanPolygon> polygons)
                {
                    this.node = node;
                    this.polygons = polygons;
                }
            }
        }

        private sealed class BooleanPlane
        {
            public Vector3 normal;
            public float distance;

            public BooleanPlane(Vector3 normal, float distance)
            {
                this.normal = normal;
                this.distance = distance;
            }

            public static BooleanPlane FromPoints(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                return new BooleanPlane(normal, Vector3.Dot(normal, a));
            }

            public BooleanPlane Clone()
            {
                return new BooleanPlane(normal, distance);
            }

            public void Flip()
            {
                normal = -normal;
                distance = -distance;
            }

            public int ClassifyPolygon(
                BooleanPolygon polygon,
                float epsilon)
            {
                int polygonType = Coplanar;
                for (int i = 0; i < polygon.vertices.Count; i++)
                {
                    float side = Vector3.Dot(
                        normal,
                        polygon.vertices[i].position) - distance;
                    polygonType |= side < -epsilon
                        ? Back
                        : side > epsilon
                            ? Front
                            : Coplanar;
                    if (polygonType == Spanning)
                        break;
                }
                return polygonType;
            }

            public void SplitPolygon(
                BooleanPolygon polygon,
                List<BooleanPolygon> coplanarFront,
                List<BooleanPolygon> coplanarBack,
                List<BooleanPolygon> front,
                List<BooleanPolygon> back,
                float epsilon,
                BooleanWorkBudget workBudget)
            {
                int polygonType = Coplanar;
                workBudget.ConsumeOperations(polygon.vertices.Count);
                for (int i = 0; i < polygon.vertices.Count; i++)
                {
                    float side = Vector3.Dot(normal, polygon.vertices[i].position) - distance;
                    int type = side < -epsilon
                        ? Back
                        : side > epsilon
                            ? Front
                            : Coplanar;
                    polygonType |= type;
                }

                switch (polygonType)
                {
                    case Coplanar:
                        if (Vector3.Dot(normal, polygon.plane.normal) > 0f)
                            coplanarFront.Add(polygon);
                        else
                            coplanarBack.Add(polygon);
                        break;
                    case Front:
                        front.Add(polygon);
                        break;
                    case Back:
                        back.Add(polygon);
                        break;
                    case Spanning:
                    {
                        // Charge the worst-case pair before allocating the two
                        // fragment vertex lists. Degenerate halves may later be
                        // discarded, but never allow allocation to outrun the
                        // operation's deterministic memory ceiling.
                        workBudget.AddGeneratedFragments(2);
                        int[] types = new int[polygon.vertices.Count];
                        for (int i = 0; i < polygon.vertices.Count; i++)
                        {
                            float side = Vector3.Dot(
                                normal,
                                polygon.vertices[i].position) - distance;
                            types[i] = side < -epsilon
                                ? Back
                                : side > epsilon
                                    ? Front
                                    : Coplanar;
                        }
                        List<BooleanVertex> frontVertices = new List<BooleanVertex>();
                        List<BooleanVertex> backVertices = new List<BooleanVertex>();
                        for (int i = 0; i < polygon.vertices.Count; i++)
                        {
                            int next = (i + 1) % polygon.vertices.Count;
                            int currentType = types[i];
                            int nextType = types[next];
                            BooleanVertex current = polygon.vertices[i];
                            BooleanVertex following = polygon.vertices[next];

                            if (currentType != Back)
                                frontVertices.Add(current);
                            if (currentType != Front)
                                backVertices.Add(currentType != Back ? current.Clone() : current);
                            if ((currentType | nextType) == Spanning)
                            {
                                Vector3 direction = following.position - current.position;
                                float denominator = Vector3.Dot(normal, direction);
                                if (Mathf.Abs(denominator) <= 0.000000001f)
                                    continue;
                                float amount =
                                    (distance - Vector3.Dot(normal, current.position)) /
                                    denominator;
                                BooleanVertex split = current.Interpolate(following, amount);
                                frontVertices.Add(split);
                                backVertices.Add(split.Clone());
                            }
                        }

                        if (BooleanPolygon.TryCreate(
                                frontVertices,
                                polygon.tag,
                                epsilon,
                                polygon.plane,
                                out BooleanPolygon frontPolygon))
                        {
                            front.Add(frontPolygon);
                        }
                        if (BooleanPolygon.TryCreate(
                                backVertices,
                                polygon.tag,
                                epsilon,
                                polygon.plane,
                                out BooleanPolygon backPolygon))
                        {
                            back.Add(backPolygon);
                        }
                        break;
                    }
                }
            }
        }

        private sealed class BooleanPolygon
        {
            public readonly List<BooleanVertex> vertices;
            public readonly PolygonTag tag;
            public BooleanPlane plane;

            private BooleanPolygon(
                List<BooleanVertex> vertices,
                PolygonTag tag,
                BooleanPlane plane)
            {
                this.vertices = vertices;
                this.tag = tag;
                this.plane = plane;
            }

            public static bool TryCreate(
                List<BooleanVertex> sourceVertices,
                PolygonTag tag,
                float epsilon,
                out BooleanPolygon polygon)
            {
                return TryCreate(
                    sourceVertices,
                    tag,
                    epsilon,
                    null,
                    out polygon);
            }

            public static bool TryCreate(
                List<BooleanVertex> sourceVertices,
                PolygonTag tag,
                float epsilon,
                BooleanPlane inheritedPlane,
                out BooleanPolygon polygon)
            {
                polygon = null;
                if (sourceVertices == null || sourceVertices.Count < 3)
                    return false;

                float positionTolerance = Mathf.Max(0.0000001f, epsilon * 0.5f);
                float positionToleranceSquared = positionTolerance * positionTolerance;
                List<BooleanVertex> cleaned =
                    new List<BooleanVertex>(sourceVertices.Count);
                for (int i = 0; i < sourceVertices.Count; i++)
                {
                    BooleanVertex candidate = sourceVertices[i];
                    if (candidate == null)
                        continue;
                    if (cleaned.Count > 0 &&
                        (candidate.position - cleaned[cleaned.Count - 1].position)
                        .sqrMagnitude <= positionToleranceSquared)
                    {
                        continue;
                    }
                    cleaned.Add(candidate);
                }
                if (cleaned.Count > 1 &&
                    (cleaned[0].position - cleaned[cleaned.Count - 1].position)
                    .sqrMagnitude <= positionToleranceSquared)
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }
                if (cleaned.Count < 3)
                    return false;

                // Plane splits can produce valid fragments whose first three
                // points are collinear. Remove only redundant ring points and
                // derive the plane from the strongest remaining triangle.
                bool removedPoint;
                do
                {
                    removedPoint = false;
                    for (int i = 0; i < cleaned.Count && cleaned.Count >= 3; i++)
                    {
                        Vector3 previous = cleaned[(i - 1 + cleaned.Count) % cleaned.Count].position;
                        Vector3 current = cleaned[i].position;
                        Vector3 next = cleaned[(i + 1) % cleaned.Count].position;
                        Vector3 incoming = current - previous;
                        Vector3 outgoing = next - current;
                        if (incoming.sqrMagnitude <= positionToleranceSquared ||
                            outgoing.sqrMagnitude <= positionToleranceSquared)
                        {
                            cleaned.RemoveAt(i);
                            removedPoint = true;
                            break;
                        }

                        Vector3 span = next - previous;
                        float spanLengthSquared = span.sqrMagnitude;
                        if (spanLengthSquared <= positionToleranceSquared)
                        {
                            cleaned.RemoveAt(i);
                            removedPoint = true;
                            break;
                        }
                        float lineDistanceSquared = Vector3.Cross(
                            current - previous,
                            span).sqrMagnitude / spanLengthSquared;
                        float projection = Vector3.Dot(
                            current - previous,
                            span) / spanLengthSquared;
                        float projectionMargin = positionTolerance /
                                                 Mathf.Sqrt(spanLengthSquared);
                        if (lineDistanceSquared <= positionToleranceSquared &&
                            projection >= -projectionMargin &&
                            projection <= 1f + projectionMargin)
                        {
                            cleaned.RemoveAt(i);
                            removedPoint = true;
                            break;
                        }
                    }
                }
                while (removedPoint && cleaned.Count >= 3);

                if (cleaned.Count < 3)
                    return false;

                Vector3 origin = cleaned[0].position;
                Vector3 strongestNormal = Vector3.zero;
                float strongestAreaSquared = 0f;
                float longestEdgeSquared = 0f;
                for (int i = 0; i < cleaned.Count; i++)
                {
                    longestEdgeSquared = Mathf.Max(
                        longestEdgeSquared,
                        (cleaned[(i + 1) % cleaned.Count].position -
                         cleaned[i].position).sqrMagnitude);
                }
                for (int i = 1; i + 1 < cleaned.Count; i++)
                {
                    Vector3 candidateNormal = Vector3.Cross(
                        cleaned[i].position - origin,
                        cleaned[i + 1].position - origin);
                    float candidateAreaSquared = candidateNormal.sqrMagnitude;
                    if (candidateAreaSquared > strongestAreaSquared)
                    {
                        strongestAreaSquared = candidateAreaSquared;
                        strongestNormal = candidateNormal;
                    }
                }
                float areaTolerance = positionTolerance *
                                      Mathf.Sqrt(Mathf.Max(
                                          longestEdgeSquared,
                                          positionToleranceSquared));
                float areaToleranceSquared = areaTolerance * areaTolerance;
                if (strongestAreaSquared <= areaToleranceSquared)
                    return false;

                BooleanPlane resolvedPlane = inheritedPlane != null &&
                                             inheritedPlane.normal.sqrMagnitude > 0.5f
                    ? inheritedPlane.Clone()
                    : new BooleanPlane(
                        strongestNormal.normalized,
                        Vector3.Dot(strongestNormal.normalized, origin));
                polygon = new BooleanPolygon(
                    cleaned,
                    tag,
                    resolvedPlane);
                return true;
            }

            public void Flip()
            {
                vertices.Reverse();
                for (int i = 0; i < vertices.Count; i++)
                    vertices[i].Flip();
                plane.Flip();
            }
        }

        private sealed class PolygonTag
        {
            public readonly int submesh;
            public readonly bool isCutSurface;

            public PolygonTag(int submesh, bool isCutSurface)
            {
                this.submesh = submesh;
                this.isCutSurface = isCutSurface;
            }
        }

        private sealed class BooleanVertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector2 uv0;
            public Vector2 uv2;
            public Color color;

            public BooleanVertex(
                Vector3 position,
                Vector3 normal,
                Vector2 uv0,
                Vector2 uv2,
                Color color)
            {
                this.position = position;
                this.normal = normal;
                this.uv0 = uv0;
                this.uv2 = uv2;
                this.color = color;
            }

            public BooleanVertex Clone()
            {
                return new BooleanVertex(position, normal, uv0, uv2, color);
            }

            public void Flip()
            {
                normal = -normal;
            }

            public BooleanVertex Interpolate(BooleanVertex other, float amount)
            {
                float t = Mathf.Clamp01(amount);
                return new BooleanVertex(
                    Vector3.LerpUnclamped(position, other.position, t),
                    Vector3.LerpUnclamped(normal, other.normal, t).normalized,
                    Vector2.LerpUnclamped(uv0, other.uv0, t),
                    Vector2.LerpUnclamped(uv2, other.uv2, t),
                    Color.LerpUnclamped(color, other.color, t));
            }
        }

        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private readonly long _x;
            private readonly long _y;
            private readonly long _z;

            private PositionKey(long x, long y, long z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            public PositionKey(Vector3 position, float tolerance)
            {
                double inverse = 1.0 / Math.Max(0.000000001, tolerance);
                _x = (long)Math.Round(position.x * inverse);
                _y = (long)Math.Round(position.y * inverse);
                _z = (long)Math.Round(position.z * inverse);
            }

            public PositionKey Offset(int x, int y, int z)
            {
                return new PositionKey(_x + x, _y + y, _z + z);
            }

            public bool Equals(PositionKey other)
            {
                return _x == other._x && _y == other._y && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is PositionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x.GetHashCode();
                    hash = hash * 397 ^ _y.GetHashCode();
                    hash = hash * 397 ^ _z.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly int _a;
            private readonly int _b;

            public EdgeKey(int a, int b)
            {
                _a = Mathf.Min(a, b);
                _b = Mathf.Max(a, b);
            }

            public bool Equals(EdgeKey other)
            {
                return _a == other._a && _b == other._b;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _a * 397 ^ _b;
            }
        }

        private readonly struct DirectedEdgeKey : IEquatable<DirectedEdgeKey>
        {
            private readonly int _start;
            private readonly int _end;

            public DirectedEdgeKey(int start, int end)
            {
                _start = start;
                _end = end;
            }

            public bool Equals(DirectedEdgeKey other)
            {
                return _start == other._start && _end == other._end;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectedEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _start * 397 ^ _end;
            }
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int _a;
            private readonly int _b;
            private readonly int _c;

            public TriangleKey(int a, int b, int c)
            {
                _a = Mathf.Min(a, Mathf.Min(b, c));
                _c = Mathf.Max(a, Mathf.Max(b, c));
                _b = a + b + c - _a - _c;
            }

            public bool IsPositiveWinding(int a, int b, int c)
            {
                return (a == _a && b == _b) ||
                       (a == _b && b == _c) ||
                       (a == _c && b == _a);
            }

            public bool Equals(TriangleKey other)
            {
                return _a == other._a && _b == other._b && _c == other._c;
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _a;
                    hash = hash * 397 ^ _b;
                    hash = hash * 397 ^ _c;
                    return hash;
                }
            }
        }

        private struct EdgeIncidence
        {
            public int count;
            public int directionSum;
        }

        private struct NonManifoldEdgeInfo
        {
            public int count;
            public int directionSum;
            public int startVertex;
            public int endVertex;
        }

        private readonly struct TopologyTriangleId : IEquatable<TopologyTriangleId>
        {
            public readonly int submesh;
            public readonly int triangle;

            public TopologyTriangleId(int submesh, int triangle)
            {
                this.submesh = submesh;
                this.triangle = triangle;
            }

            public bool Equals(TopologyTriangleId other)
            {
                return submesh == other.submesh && triangle == other.triangle;
            }

            public override bool Equals(object obj)
            {
                return obj is TopologyTriangleId other && Equals(other);
            }

            public override int GetHashCode()
            {
                return submesh * 397 ^ triangle;
            }
        }

        private readonly struct TopologyEdgeOccurrence
        {
            public readonly TopologyTriangleId triangle;
            public readonly int directionSign;

            public TopologyEdgeOccurrence(
                TopologyTriangleId triangle,
                int directionSign)
            {
                this.triangle = triangle;
                this.directionSign = directionSign;
            }
        }

        private readonly struct TopologyFlapCandidate
        {
            public readonly EdgeKey boundaryEdge;
            public readonly EdgeKey conflictEdge;
            public readonly EdgeKey regularEdge;

            public TopologyFlapCandidate(
                EdgeKey boundaryEdge,
                EdgeKey conflictEdge,
                EdgeKey regularEdge)
            {
                this.boundaryEdge = boundaryEdge;
                this.conflictEdge = conflictEdge;
                this.regularEdge = regularEdge;
            }
        }

        private readonly struct OutputVertexKey : IEquatable<OutputVertexKey>
        {
            private readonly long _px;
            private readonly long _py;
            private readonly long _pz;
            private readonly int _nx;
            private readonly int _ny;
            private readonly int _nz;
            private readonly int _u0x;
            private readonly int _u0y;
            private readonly int _u2x;
            private readonly int _u2y;
            private readonly int _r;
            private readonly int _g;
            private readonly int _b;
            private readonly int _a;

            public OutputVertexKey(BooleanVertex vertex, float tolerance)
            {
                double inverse = 1.0 / Math.Max(0.000000001, tolerance);
                _px = (long)Math.Round(vertex.position.x * inverse);
                _py = (long)Math.Round(vertex.position.y * inverse);
                _pz = (long)Math.Round(vertex.position.z * inverse);
                _nx = Quantize(vertex.normal.x, 100000f);
                _ny = Quantize(vertex.normal.y, 100000f);
                _nz = Quantize(vertex.normal.z, 100000f);
                _u0x = Quantize(vertex.uv0.x, 100000f);
                _u0y = Quantize(vertex.uv0.y, 100000f);
                _u2x = Quantize(vertex.uv2.x, 100000f);
                _u2y = Quantize(vertex.uv2.y, 100000f);
                _r = Quantize(vertex.color.r, 65535f);
                _g = Quantize(vertex.color.g, 65535f);
                _b = Quantize(vertex.color.b, 65535f);
                _a = Quantize(vertex.color.a, 65535f);
            }

            private static int Quantize(float value, float scale)
            {
                return Mathf.RoundToInt(value * scale);
            }

            public bool Equals(OutputVertexKey other)
            {
                return _px == other._px && _py == other._py && _pz == other._pz &&
                       _nx == other._nx && _ny == other._ny && _nz == other._nz &&
                       _u0x == other._u0x && _u0y == other._u0y &&
                       _u2x == other._u2x && _u2y == other._u2y &&
                       _r == other._r && _g == other._g &&
                       _b == other._b && _a == other._a;
            }

            public override bool Equals(object obj)
            {
                return obj is OutputVertexKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _px.GetHashCode();
                    hash = hash * 397 ^ _py.GetHashCode();
                    hash = hash * 397 ^ _pz.GetHashCode();
                    hash = hash * 397 ^ _nx;
                    hash = hash * 397 ^ _ny;
                    hash = hash * 397 ^ _nz;
                    hash = hash * 397 ^ _u0x;
                    hash = hash * 397 ^ _u0y;
                    hash = hash * 397 ^ _u2x;
                    hash = hash * 397 ^ _u2y;
                    hash = hash * 397 ^ _r;
                    hash = hash * 397 ^ _g;
                    hash = hash * 397 ^ _b;
                    hash = hash * 397 ^ _a;
                    return hash;
                }
            }
        }

        private readonly struct EdgeSplit
        {
            public readonly float amount;
            public readonly Vector3 position;

            public EdgeSplit(float amount, Vector3 position)
            {
                this.amount = amount;
                this.position = position;
            }
        }

        private readonly struct IndexedEdgeSplit
        {
            public readonly float amount;
            public readonly int positionId;
            public readonly Vector3 position;

            public IndexedEdgeSplit(float amount, int positionId, Vector3 position)
            {
                this.amount = amount;
                this.positionId = positionId;
                this.position = position;
            }
        }

        /// <summary>
        /// Immutable three-axis point index. Segment queries choose the axis on
        /// which the segment has the greatest extent, binary-search that range,
        /// and leave the exact capsule test to the caller. Unlike a tiny-cell
        /// uniform grid, cost does not explode for metre-long wall edges.
        /// </summary>
        private sealed class PositionAxisIndex
        {
            private readonly IList<Vector3> _positions;
            private readonly int[] _byX;
            private readonly int[] _byY;
            private readonly int[] _byZ;

            public PositionAxisIndex(
                IList<Vector3> positions,
                IEnumerable<int> positionIds)
            {
                _positions = positions;
                List<int> ids = new List<int>(positionIds);
                _byX = ids.ToArray();
                _byY = ids.ToArray();
                _byZ = ids.ToArray();
                Array.Sort(_byX, (left, right) => Compare(left, right, 0));
                Array.Sort(_byY, (left, right) => Compare(left, right, 1));
                Array.Sort(_byZ, (left, right) => Compare(left, right, 2));
            }

            public void CollectSegmentRange(
                Vector3 start,
                Vector3 end,
                float tolerance,
                List<int> result)
            {
                Vector3 extent = end - start;
                int axis = Mathf.Abs(extent.x) >= Mathf.Abs(extent.y)
                    ? Mathf.Abs(extent.x) >= Mathf.Abs(extent.z) ? 0 : 2
                    : Mathf.Abs(extent.y) >= Mathf.Abs(extent.z) ? 1 : 2;
                int[] sorted = axis == 0 ? _byX : axis == 1 ? _byY : _byZ;
                float startCoordinate = Coordinate(start, axis);
                float endCoordinate = Coordinate(end, axis);
                float minimum = Mathf.Min(startCoordinate, endCoordinate) - tolerance;
                float maximum = Mathf.Max(startCoordinate, endCoordinate) + tolerance;
                int first = LowerBound(sorted, minimum, axis);
                for (int index = first; index < sorted.Length; index++)
                {
                    int positionId = sorted[index];
                    if (Coordinate(_positions[positionId], axis) > maximum)
                        break;
                    result.Add(positionId);
                }
            }

            private int LowerBound(int[] sorted, float value, int axis)
            {
                int lower = 0;
                int upper = sorted.Length;
                while (lower < upper)
                {
                    int middle = lower + (upper - lower) / 2;
                    if (Coordinate(_positions[sorted[middle]], axis) < value)
                        lower = middle + 1;
                    else
                        upper = middle;
                }
                return lower;
            }

            private int Compare(int left, int right, int axis)
            {
                int coordinateComparison = Coordinate(
                    _positions[left], axis).CompareTo(Coordinate(_positions[right], axis));
                return coordinateComparison != 0
                    ? coordinateComparison
                    : left.CompareTo(right);
            }

            private static float Coordinate(Vector3 value, int axis)
            {
                return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
            }
        }

        private readonly struct BoundaryEdgeOccurrence
        {
            public readonly int submesh;
            public readonly int triangleStart;
            public readonly int startVertex;
            public readonly int endVertex;
            public readonly int oppositeVertex;

            public BoundaryEdgeOccurrence(
                int submesh,
                int triangleStart,
                int startVertex,
                int endVertex,
                int oppositeVertex)
            {
                this.submesh = submesh;
                this.triangleStart = triangleStart;
                this.startVertex = startVertex;
                this.endVertex = endVertex;
                this.oppositeVertex = oppositeVertex;
            }
        }

        private readonly struct TriangleOccurrence
        {
            public readonly int submesh;
            public readonly int triangle;
            public readonly bool positive;
            public readonly bool cutFlag;

            public TriangleOccurrence(
                int submesh,
                int triangle,
                bool positive,
                bool cutFlag)
            {
                this.submesh = submesh;
                this.triangle = triangle;
                this.positive = positive;
                this.cutFlag = cutFlag;
            }
        }

        private readonly struct SeamEdgePair
        {
            public readonly int left;
            public readonly int right;
            public readonly float score;

            public SeamEdgePair(int left, int right, float score)
            {
                this.left = left;
                this.right = right;
                this.score = score;
            }
        }
    }
}
