using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealBlend
{
    public struct RealBlendCircleRemoveBoundaryReport
    {
        public int weldedVertexGroups;
        public int spatiallyWeldedClones;
        public int boundaryEdges;
        public int validLoops;
        public int rejectedComponents;
        public int movedWeldGroups;
        public int movedVertices;
        public float appliedBlend;
    }

    /// <summary>
    /// Rounds the existing edge of an index-only circular Remove operation.
    /// This utility deliberately does not add, remove, or re-index vertices. It
    /// only moves the spatially welded boundary ring after proving that the
    /// proposed result keeps the affected retained triangles valid.
    /// </summary>
    internal static class RealBlendCircleRemoveBoundaryUtility
    {
        private const float MinimumSafeBlend = 0.0625f;
        private const int BlendRefinementSteps = 5;
        private const float MinimumAreaRatio = 0.02f;
        private const float MinimumNormalDot = 0.001f;
        private const float MinimumLoopNormalDot = 0.5f;

        /// <summary>
        /// Shapes simple circular Remove boundaries in-place. The immutable
        /// triangle arrays must use the same convention as
        /// Mesh.GetTriangles(submesh, false); base vertices are supplied
        /// separately and removed ordinals are zero-based per submesh.
        /// </summary>
        internal static bool TryShapeBoundary(
            Mesh mesh,
            Matrix4x4 localToWorld,
            int[][] sourceSubmeshTriangles,
            int[] sourceSubmeshBaseVertices,
            IDictionary<int, HashSet<int>> removedTriangleOrdinals,
            IList<RealBlendSculptRemoveSample> samples,
            out RealBlendCircleRemoveBoundaryReport report,
            out string error)
        {
            report = default;
            error = string.Empty;

            if (!TryValidateInputs(
                    mesh,
                    localToWorld,
                    sourceSubmeshTriangles,
                    sourceSubmeshBaseVertices,
                    removedTriangleOrdinals,
                    samples,
                    out error))
            {
                return false;
            }

            Vector3[] localVertices;
            try
            {
                localVertices = mesh.vertices;
            }
            catch (Exception exception)
            {
                error = "Could not read the mesh vertices: " + exception.Message;
                return false;
            }

            if (localVertices == null || localVertices.Length != mesh.vertexCount)
            {
                error = "The mesh vertex stream changed while the Remove stroke was being committed.";
                return false;
            }

            Vector3[] worldVertices = new Vector3[localVertices.Length];
            Vector3 worldMinimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 worldMaximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (int vertex = 0; vertex < localVertices.Length; vertex++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(localVertices[vertex]);
                if (!IsFinite(world))
                {
                    error = "The mesh contains a non-finite vertex position.";
                    return false;
                }

                worldVertices[vertex] = world;
                worldMinimum = Vector3.Min(worldMinimum, world);
                worldMaximum = Vector3.Max(worldMaximum, world);
            }

            float worldDiagonal = (worldMaximum - worldMinimum).magnitude;
            float weldTolerance = Mathf.Clamp(
                Mathf.Max(0.000001f, worldDiagonal * 0.0000001f),
                0.000001f,
                0.0001f);

            BuildSpatialWeldGroups(
                worldVertices,
                weldTolerance,
                out List<WeldGroup> weldGroups,
                out int[] vertexToWeldGroup);
            report.weldedVertexGroups = weldGroups.Count;
            report.spatiallyWeldedClones = localVertices.Length - weldGroups.Count;

            if (!TryBuildSourceTopology(
                    mesh,
                    sourceSubmeshTriangles,
                    sourceSubmeshBaseVertices,
                    removedTriangleOrdinals,
                    vertexToWeldGroup,
                    out List<SourceTriangle> sourceTriangles,
                    out Dictionary<WeldEdge, EdgeIncidence> edgeIncidences,
                    out error))
            {
                return false;
            }

            List<CircleFrame> circleFrames = BuildCircleFrames(samples);
            if (circleFrames.Count == 0)
            {
                error = "The stroke did not contain a valid circular brush sample.";
                return false;
            }

            List<BoundaryLoop> loops = BuildBoundaryLoops(
                edgeIncidences,
                ref report);
            if (loops.Count == 0)
            {
                error = report.boundaryEdges == 0
                    ? "The removed triangles do not share a retained geometric boundary."
                    : "The circular boundary was not a simple closed, manifold loop with at least six vertices.";
                return false;
            }

            Dictionary<int, Vector3> groupTargets = new Dictionary<int, Vector3>();
            List<BoundaryLoop> targetableLoops = new List<BoundaryLoop>(loops.Count);
            for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                BoundaryLoop loop = loops[loopIndex];
                if (!TryAssignCircleTargets(
                        loop,
                        weldGroups,
                        circleFrames,
                        weldTolerance,
                        groupTargets))
                {
                    report.rejectedComponents++;
                    continue;
                }

                if (!TryPrepareLoopProjection(
                        loop,
                        weldGroups,
                        groupTargets,
                        weldTolerance))
                {
                    RemoveLoopTargets(loop, groupTargets);
                    report.rejectedComponents++;
                    continue;
                }

                targetableLoops.Add(loop);
            }

            if (targetableLoops.Count == 0)
            {
                error = "The boundary could not be associated safely with the circular brush stroke.";
                return false;
            }

            bool[] movedGroups = new bool[weldGroups.Count];
            foreach (KeyValuePair<int, Vector3> target in groupTargets)
            {
                int groupIndex = target.Key;
                if ((target.Value - weldGroups[groupIndex].worldPosition).sqrMagnitude >
                    weldTolerance * weldTolerance * 0.0001f)
                {
                    movedGroups[groupIndex] = true;
                    report.movedWeldGroups++;
                    report.movedVertices += weldGroups[groupIndex].vertexIndices.Count;
                }
            }

            if (report.movedWeldGroups == 0)
            {
                error = "The boundary already follows the circular brush closely enough.";
                return false;
            }

            Vector3[] candidateWorldVertices = new Vector3[worldVertices.Length];
            float appliedBlend = FindSafeGlobalBlend(
                worldVertices,
                candidateWorldVertices,
                vertexToWeldGroup,
                weldGroups,
                movedGroups,
                groupTargets,
                sourceTriangles,
                targetableLoops,
                worldDiagonal,
                weldTolerance,
                out string validationError);
            if (appliedBlend < MinimumSafeBlend)
            {
                error = string.IsNullOrEmpty(validationError)
                    ? "The rounded boundary would create invalid geometry."
                    : validationError;
                return false;
            }

            BuildCandidateWorldVertices(
                worldVertices,
                candidateWorldVertices,
                vertexToWeldGroup,
                movedGroups,
                groupTargets,
                appliedBlend);

            Matrix4x4 worldToLocal = localToWorld.inverse;
            if (!IsFinite(worldToLocal))
            {
                error = "The target transform cannot be inverted safely.";
                return false;
            }

            Vector3[] shapedLocalVertices = (Vector3[])localVertices.Clone();
            for (int vertex = 0; vertex < shapedLocalVertices.Length; vertex++)
            {
                int group = vertexToWeldGroup[vertex];
                if (!movedGroups[group])
                    continue;

                Vector3 local = worldToLocal.MultiplyPoint3x4(candidateWorldVertices[vertex]);
                if (!IsFinite(local))
                {
                    error = "The rounded boundary produced a non-finite local vertex position.";
                    return false;
                }
                shapedLocalVertices[vertex] = local;
            }

            // Assigning a same-length position array preserves every other
            // vertex stream and every submesh/index buffer. Normals, tangents,
            // and bounds are intentionally left to the caller's normal commit.
            try
            {
                mesh.vertices = shapedLocalVertices;
            }
            catch (Exception exception)
            {
                error = "Could not assign the rounded boundary positions: " + exception.Message;
                return false;
            }
            report.validLoops = targetableLoops.Count;
            report.appliedBlend = appliedBlend;
            return true;
        }

        private static bool TryValidateInputs(
            Mesh mesh,
            Matrix4x4 localToWorld,
            int[][] sourceSubmeshTriangles,
            int[] sourceSubmeshBaseVertices,
            IDictionary<int, HashSet<int>> removedTriangleOrdinals,
            IList<RealBlendSculptRemoveSample> samples,
            out string error)
        {
            error = string.Empty;
            if (mesh == null)
            {
                error = "The Remove stroke has no mesh.";
                return false;
            }
            if (!mesh.isReadable)
            {
                error = "The mesh is not readable.";
                return false;
            }
            if (!IsFinite(localToWorld) || Mathf.Abs(localToWorld.determinant) <= 0.0000000001f)
            {
                error = "The target has a singular or non-finite transform.";
                return false;
            }
            if (sourceSubmeshTriangles == null ||
                sourceSubmeshBaseVertices == null ||
                sourceSubmeshTriangles.Length != mesh.subMeshCount ||
                sourceSubmeshBaseVertices.Length != mesh.subMeshCount)
            {
                error = "The immutable Remove topology no longer matches the mesh submeshes.";
                return false;
            }
            if (removedTriangleOrdinals == null || removedTriangleOrdinals.Count == 0)
            {
                error = "No removed triangle ordinals were supplied.";
                return false;
            }
            if (samples == null || samples.Count == 0)
            {
                error = "The Remove stroke has no brush samples.";
                return false;
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in removedTriangleOrdinals)
            {
                if (entry.Key < 0 || entry.Key >= sourceSubmeshTriangles.Length)
                {
                    error = "A removed triangle references an invalid submesh.";
                    return false;
                }

                int[] indices = sourceSubmeshTriangles[entry.Key];
                if (indices == null)
                {
                    if (entry.Value != null && entry.Value.Count > 0)
                    {
                        error = "A removed triangle references a non-triangle submesh.";
                        return false;
                    }
                    continue;
                }

                int triangleCount = indices.Length / 3;
                if (entry.Value == null)
                    continue;
                foreach (int ordinal in entry.Value)
                {
                    if (ordinal < 0 || ordinal >= triangleCount)
                    {
                        error = "A removed triangle ordinal is outside its immutable submesh snapshot.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static void BuildSpatialWeldGroups(
            Vector3[] worldVertices,
            float tolerance,
            out List<WeldGroup> groups,
            out int[] vertexToGroup)
        {
            groups = new List<WeldGroup>(worldVertices.Length);
            vertexToGroup = new int[worldVertices.Length];
            Dictionary<SpatialCell, List<int>> cells =
                new Dictionary<SpatialCell, List<int>>(worldVertices.Length);
            float toleranceSquared = tolerance * tolerance;

            for (int vertex = 0; vertex < worldVertices.Length; vertex++)
            {
                Vector3 point = worldVertices[vertex];
                SpatialCell cell = SpatialCell.FromPoint(point, tolerance);
                int closestGroup = -1;
                float closestDistance = toleranceSquared;
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            SpatialCell neighbour = new SpatialCell(
                                cell.x + x,
                                cell.y + y,
                                cell.z + z);
                            if (!cells.TryGetValue(neighbour, out List<int> candidates))
                                continue;

                            for (int candidateIndex = 0;
                                 candidateIndex < candidates.Count;
                                 candidateIndex++)
                            {
                                int groupIndex = candidates[candidateIndex];
                                float distance =
                                    (groups[groupIndex].worldPosition - point).sqrMagnitude;
                                if (distance <= closestDistance)
                                {
                                    closestDistance = distance;
                                    closestGroup = groupIndex;
                                }
                            }
                        }
                    }
                }

                if (closestGroup < 0)
                {
                    closestGroup = groups.Count;
                    groups.Add(new WeldGroup(vertex, point));
                    if (!cells.TryGetValue(cell, out List<int> occupants))
                    {
                        occupants = new List<int>(1);
                        cells.Add(cell, occupants);
                    }
                    occupants.Add(closestGroup);
                }
                else
                {
                    groups[closestGroup].Add(vertex, point);
                }

                vertexToGroup[vertex] = closestGroup;
            }
        }

        private static bool TryBuildSourceTopology(
            Mesh mesh,
            int[][] sourceSubmeshTriangles,
            int[] sourceSubmeshBaseVertices,
            IDictionary<int, HashSet<int>> removedTriangleOrdinals,
            int[] vertexToGroup,
            out List<SourceTriangle> triangles,
            out Dictionary<WeldEdge, EdgeIncidence> edges,
            out string error)
        {
            triangles = new List<SourceTriangle>();
            edges = new Dictionary<WeldEdge, EdgeIncidence>();
            error = string.Empty;
            int removedCount = 0;
            int retainedCount = 0;

            for (int submesh = 0; submesh < sourceSubmeshTriangles.Length; submesh++)
            {
                int[] indices = sourceSubmeshTriangles[submesh];
                if (indices == null)
                    continue;
                if (indices.Length % 3 != 0)
                {
                    error = "An immutable triangle submesh has an incomplete index triplet.";
                    return false;
                }

                int baseVertex = sourceSubmeshBaseVertices[submesh];
                removedTriangleOrdinals.TryGetValue(
                    submesh,
                    out HashSet<int> removedOrdinals);
                int triangleCount = indices.Length / 3;
                for (int ordinal = 0; ordinal < triangleCount; ordinal++)
                {
                    int offset = ordinal * 3;
                    if (!TryResolveVertexIndex(indices[offset], baseVertex, mesh.vertexCount, out int a) ||
                        !TryResolveVertexIndex(indices[offset + 1], baseVertex, mesh.vertexCount, out int b) ||
                        !TryResolveVertexIndex(indices[offset + 2], baseVertex, mesh.vertexCount, out int c))
                    {
                        error = "An immutable submesh index is outside the mesh vertex stream.";
                        return false;
                    }

                    bool removed = removedOrdinals != null && removedOrdinals.Contains(ordinal);
                    if (removed)
                        removedCount++;
                    else
                        retainedCount++;

                    SourceTriangle triangle = new SourceTriangle(
                        a,
                        b,
                        c,
                        vertexToGroup[a],
                        vertexToGroup[b],
                        vertexToGroup[c],
                        removed);
                    triangles.Add(triangle);
                    AddEdgeIncidence(edges, triangle.groupA, triangle.groupB, removed);
                    AddEdgeIncidence(edges, triangle.groupB, triangle.groupC, removed);
                    AddEdgeIncidence(edges, triangle.groupC, triangle.groupA, removed);
                }
            }

            if (removedCount == 0)
            {
                error = "No triangles were marked as removed.";
                return false;
            }
            if (retainedCount == 0)
            {
                error = "The stroke removed the entire triangle topology, leaving no boundary to shape.";
                return false;
            }
            return true;
        }

        private static bool TryResolveVertexIndex(
            int index,
            int baseVertex,
            int vertexCount,
            out int resolved)
        {
            long value = (long)index + baseVertex;
            if (value < 0 || value >= vertexCount)
            {
                resolved = -1;
                return false;
            }
            resolved = (int)value;
            return true;
        }

        private static void AddEdgeIncidence(
            Dictionary<WeldEdge, EdgeIncidence> edges,
            int groupA,
            int groupB,
            bool removed)
        {
            if (groupA == groupB)
                return;

            WeldEdge edge = new WeldEdge(groupA, groupB);
            if (!edges.TryGetValue(edge, out EdgeIncidence incidence))
            {
                incidence = new EdgeIncidence();
                edges.Add(edge, incidence);
            }
            incidence.total++;
            if (removed)
                incidence.removed++;
            else
                incidence.retained++;
        }

        private static List<CircleFrame> BuildCircleFrames(
            IList<RealBlendSculptRemoveSample> samples)
        {
            List<CircleFrame> frames = new List<CircleFrame>(samples.Count);
            for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
            {
                RealBlendSculptRemoveSample sample = samples[sampleIndex];
                if (sample.shape != RealBlendSculptRemoveShape.Circle ||
                    !IsFinite(sample.worldCenter) ||
                    !IsFinite(sample.worldNormal) ||
                    sample.worldNormal.sqrMagnitude <= 0.00000001f ||
                    !IsFinite(sample.radius) ||
                    sample.radius <= 0.000001f)
                {
                    continue;
                }

                frames.Add(new CircleFrame(
                    sample.worldCenter,
                    sample.worldNormal.normalized,
                    sample.radius));
            }
            return frames;
        }

        private static List<BoundaryLoop> BuildBoundaryLoops(
            Dictionary<WeldEdge, EdgeIncidence> incidences,
            ref RealBlendCircleRemoveBoundaryReport report)
        {
            Dictionary<int, List<WeldEdge>> adjacency =
                new Dictionary<int, List<WeldEdge>>();
            Dictionary<WeldEdge, EdgeIncidence> boundary =
                new Dictionary<WeldEdge, EdgeIncidence>();
            foreach (KeyValuePair<WeldEdge, EdgeIncidence> entry in incidences)
            {
                EdgeIncidence incidence = entry.Value;
                if (incidence.removed == 0 || incidence.retained == 0)
                    continue;

                boundary.Add(entry.Key, incidence);
                AddAdjacentEdge(adjacency, entry.Key.a, entry.Key);
                AddAdjacentEdge(adjacency, entry.Key.b, entry.Key);
            }
            report.boundaryEdges = boundary.Count;

            List<BoundaryLoop> loops = new List<BoundaryLoop>();
            HashSet<WeldEdge> visitedEdges = new HashSet<WeldEdge>();
            foreach (KeyValuePair<WeldEdge, EdgeIncidence> seedEntry in boundary)
            {
                if (visitedEdges.Contains(seedEntry.Key))
                    continue;

                HashSet<WeldEdge> componentEdges = new HashSet<WeldEdge>();
                HashSet<int> componentVertices = new HashSet<int>();
                Stack<WeldEdge> pending = new Stack<WeldEdge>();
                pending.Push(seedEntry.Key);
                while (pending.Count > 0)
                {
                    WeldEdge edge = pending.Pop();
                    if (!componentEdges.Add(edge))
                        continue;
                    visitedEdges.Add(edge);
                    componentVertices.Add(edge.a);
                    componentVertices.Add(edge.b);

                    PushUnvisitedAdjacentEdges(edge.a, adjacency, componentEdges, pending);
                    PushUnvisitedAdjacentEdges(edge.b, adjacency, componentEdges, pending);
                }

                bool valid = componentVertices.Count >= 6 &&
                    componentEdges.Count == componentVertices.Count;
                foreach (WeldEdge edge in componentEdges)
                {
                    EdgeIncidence incidence = boundary[edge];
                    if (incidence.total != 2 ||
                        incidence.removed != 1 ||
                        incidence.retained != 1)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    foreach (int vertex in componentVertices)
                    {
                        if (!adjacency.TryGetValue(vertex, out List<WeldEdge> connected) ||
                            connected.Count != 2)
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (!valid || !TryOrderLoop(componentVertices, adjacency, out List<int> ordered))
                {
                    report.rejectedComponents++;
                    continue;
                }

                loops.Add(new BoundaryLoop(ordered));
            }
            return loops;
        }

        private static void AddAdjacentEdge(
            Dictionary<int, List<WeldEdge>> adjacency,
            int vertex,
            WeldEdge edge)
        {
            if (!adjacency.TryGetValue(vertex, out List<WeldEdge> connected))
            {
                connected = new List<WeldEdge>(2);
                adjacency.Add(vertex, connected);
            }
            connected.Add(edge);
        }

        private static void PushUnvisitedAdjacentEdges(
            int vertex,
            Dictionary<int, List<WeldEdge>> adjacency,
            HashSet<WeldEdge> component,
            Stack<WeldEdge> pending)
        {
            if (!adjacency.TryGetValue(vertex, out List<WeldEdge> connected))
                return;
            for (int edgeIndex = 0; edgeIndex < connected.Count; edgeIndex++)
            {
                if (!component.Contains(connected[edgeIndex]))
                    pending.Push(connected[edgeIndex]);
            }
        }

        private static bool TryOrderLoop(
            HashSet<int> componentVertices,
            Dictionary<int, List<WeldEdge>> adjacency,
            out List<int> ordered)
        {
            ordered = new List<int>(componentVertices.Count);
            int start = int.MaxValue;
            foreach (int vertex in componentVertices)
                start = Mathf.Min(start, vertex);

            int previous = -1;
            int current = start;
            HashSet<int> visited = new HashSet<int>();
            for (int step = 0; step <= componentVertices.Count; step++)
            {
                if (current == start && step > 0)
                    return ordered.Count == componentVertices.Count;
                if (!visited.Add(current) ||
                    !adjacency.TryGetValue(current, out List<WeldEdge> connected) ||
                    connected.Count != 2)
                {
                    return false;
                }

                ordered.Add(current);
                int neighbourA = connected[0].Other(current);
                int neighbourB = connected[1].Other(current);
                int next;
                if (previous < 0)
                    next = Mathf.Min(neighbourA, neighbourB);
                else if (neighbourA == previous)
                    next = neighbourB;
                else if (neighbourB == previous)
                    next = neighbourA;
                else
                    return false;

                previous = current;
                current = next;
            }
            return false;
        }

        private static bool TryAssignCircleTargets(
            BoundaryLoop loop,
            List<WeldGroup> weldGroups,
            List<CircleFrame> frames,
            float weldTolerance,
            Dictionary<int, Vector3> targets)
        {
            Vector3 accumulatedNormal = Vector3.zero;
            Vector3[] proposedTargets = new Vector3[loop.groups.Count];
            Vector3[] targetNormals = new Vector3[loop.groups.Count];
            for (int index = 0; index < loop.groups.Count; index++)
            {
                int group = loop.groups[index];
                if (!TryFindCircleTarget(
                        weldGroups[group].worldPosition,
                        frames,
                        weldTolerance,
                        out proposedTargets[index],
                        out targetNormals[index]))
                {
                    return false;
                }
                accumulatedNormal += targetNormals[index];
            }

            if (accumulatedNormal.sqrMagnitude <= 0.00000001f)
                return false;
            accumulatedNormal.Normalize();
            for (int index = 0; index < targetNormals.Length; index++)
            {
                if (Vector3.Dot(accumulatedNormal, targetNormals[index]) < MinimumLoopNormalDot)
                    return false;
            }

            loop.approximateNormal = accumulatedNormal;
            for (int index = 0; index < loop.groups.Count; index++)
                targets[loop.groups[index]] = proposedTargets[index];
            return true;
        }

        private static bool TryFindCircleTarget(
            Vector3 point,
            List<CircleFrame> frames,
            float weldTolerance,
            out Vector3 target,
            out Vector3 normal)
        {
            target = point;
            normal = Vector3.zero;
            float bestScore = float.PositiveInfinity;

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                CircleFrame frame = frames[frameIndex];
                Vector3 delta = point - frame.center;
                float depth = Vector3.Dot(delta, frame.normal);
                float maximumDepth = Mathf.Max(frame.radius * 0.35f, weldTolerance * 8f);
                if (Mathf.Abs(depth) > maximumDepth)
                    continue;

                Vector3 planar = delta - frame.normal * depth;
                float radialDistance = planar.magnitude;
                if (radialDistance <= Mathf.Max(frame.radius * 0.0001f, weldTolerance))
                    continue;

                Vector3 candidate = frame.center +
                    frame.normal * depth +
                    planar * (frame.radius / radialDistance);
                float score = (candidate - point).sqrMagnitude +
                    depth * depth * 0.01f;
                if (score < bestScore)
                {
                    bestScore = score;
                    target = candidate;
                    normal = frame.normal;
                }
            }
            return IsFinite(bestScore);
        }

        private static bool TryPrepareLoopProjection(
            BoundaryLoop loop,
            List<WeldGroup> weldGroups,
            Dictionary<int, Vector3> targets,
            float weldTolerance)
        {
            Vector3 normal = loop.approximateNormal.normalized;
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude <= 0.000001f)
                right = Vector3.Cross(normal, Vector3.right);
            if (right.sqrMagnitude <= 0.000001f)
                return false;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;

            Vector3 origin = Vector3.zero;
            for (int index = 0; index < loop.groups.Count; index++)
                origin += weldGroups[loop.groups[index]].worldPosition;
            origin /= loop.groups.Count;

            loop.projectionOrigin = origin;
            loop.projectionRight = right;
            loop.projectionUp = up;
            if (!TryValidateProjectedLoop(
                    loop,
                    weldGroups,
                    targets,
                    0f,
                    weldTolerance,
                    out float originalSignedArea))
            {
                return false;
            }
            loop.originalSignedArea = originalSignedArea;
            return true;
        }

        private static void RemoveLoopTargets(
            BoundaryLoop loop,
            Dictionary<int, Vector3> targets)
        {
            for (int index = 0; index < loop.groups.Count; index++)
                targets.Remove(loop.groups[index]);
        }

        private static float FindSafeGlobalBlend(
            Vector3[] originalWorldVertices,
            Vector3[] candidateWorldVertices,
            int[] vertexToGroup,
            List<WeldGroup> weldGroups,
            bool[] movedGroups,
            Dictionary<int, Vector3> groupTargets,
            List<SourceTriangle> sourceTriangles,
            List<BoundaryLoop> loops,
            float worldDiagonal,
            float weldTolerance,
            out string validationError)
        {
            validationError = string.Empty;
            if (TryValidateBlend(
                    1f,
                    originalWorldVertices,
                    candidateWorldVertices,
                    vertexToGroup,
                    weldGroups,
                    movedGroups,
                    groupTargets,
                    sourceTriangles,
                    loops,
                    worldDiagonal,
                    weldTolerance,
                    out validationError))
            {
                return 1f;
            }

            float unsafeBlend = 1f;
            float safeBlend = 0f;
            float trial = 0.5f;
            while (trial >= MinimumSafeBlend)
            {
                if (TryValidateBlend(
                        trial,
                        originalWorldVertices,
                        candidateWorldVertices,
                        vertexToGroup,
                        weldGroups,
                        movedGroups,
                        groupTargets,
                        sourceTriangles,
                        loops,
                        worldDiagonal,
                        weldTolerance,
                        out validationError))
                {
                    safeBlend = trial;
                    break;
                }
                unsafeBlend = trial;
                trial *= 0.5f;
            }

            if (safeBlend < MinimumSafeBlend)
                return 0f;

            for (int refinement = 0; refinement < BlendRefinementSteps; refinement++)
            {
                trial = (safeBlend + unsafeBlend) * 0.5f;
                if (TryValidateBlend(
                        trial,
                        originalWorldVertices,
                        candidateWorldVertices,
                        vertexToGroup,
                        weldGroups,
                        movedGroups,
                        groupTargets,
                        sourceTriangles,
                        loops,
                        worldDiagonal,
                        weldTolerance,
                        out string ignoredError))
                {
                    safeBlend = trial;
                }
                else
                {
                    unsafeBlend = trial;
                }
            }
            validationError = string.Empty;
            return safeBlend;
        }

        private static bool TryValidateBlend(
            float blend,
            Vector3[] originalWorldVertices,
            Vector3[] candidateWorldVertices,
            int[] vertexToGroup,
            List<WeldGroup> weldGroups,
            bool[] movedGroups,
            Dictionary<int, Vector3> groupTargets,
            List<SourceTriangle> sourceTriangles,
            List<BoundaryLoop> loops,
            float worldDiagonal,
            float weldTolerance,
            out string error)
        {
            error = string.Empty;
            BuildCandidateWorldVertices(
                originalWorldVertices,
                candidateWorldVertices,
                vertexToGroup,
                movedGroups,
                groupTargets,
                blend);

            float minimumCrossMagnitude = Mathf.Max(
                0.000000000001f,
                worldDiagonal * worldDiagonal * 0.0000000001f);
            for (int triangleIndex = 0; triangleIndex < sourceTriangles.Count; triangleIndex++)
            {
                SourceTriangle triangle = sourceTriangles[triangleIndex];
                if (triangle.removed ||
                    (!movedGroups[triangle.groupA] &&
                     !movedGroups[triangle.groupB] &&
                     !movedGroups[triangle.groupC]))
                {
                    continue;
                }

                Vector3 originalCross = Vector3.Cross(
                    originalWorldVertices[triangle.b] - originalWorldVertices[triangle.a],
                    originalWorldVertices[triangle.c] - originalWorldVertices[triangle.a]);
                Vector3 candidateCross = Vector3.Cross(
                    candidateWorldVertices[triangle.b] - candidateWorldVertices[triangle.a],
                    candidateWorldVertices[triangle.c] - candidateWorldVertices[triangle.a]);
                float originalArea = originalCross.magnitude;
                float candidateArea = candidateCross.magnitude;
                if (!IsFinite(originalArea) ||
                    !IsFinite(candidateArea) ||
                    originalArea <= minimumCrossMagnitude ||
                    candidateArea <= Mathf.Max(
                        minimumCrossMagnitude,
                        originalArea * MinimumAreaRatio))
                {
                    error = "Rounding the hole would collapse a retained triangle.";
                    return false;
                }

                float normalDot = Vector3.Dot(originalCross, candidateCross);
                if (normalDot <= originalArea * candidateArea * MinimumNormalDot)
                {
                    error = "Rounding the hole would flip a retained triangle.";
                    return false;
                }
            }

            for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                BoundaryLoop loop = loops[loopIndex];
                if (!TryValidateProjectedLoop(
                        loop,
                        weldGroups,
                        groupTargets,
                        blend,
                        weldTolerance,
                        out float signedArea) ||
                    signedArea * loop.originalSignedArea <= 0f)
                {
                    error = "Rounding the hole would make its boundary cross or reverse itself.";
                    return false;
                }
            }
            return true;
        }

        private static void BuildCandidateWorldVertices(
            Vector3[] originals,
            Vector3[] candidates,
            int[] vertexToGroup,
            bool[] movedGroups,
            Dictionary<int, Vector3> groupTargets,
            float blend)
        {
            Array.Copy(originals, candidates, originals.Length);
            for (int vertex = 0; vertex < originals.Length; vertex++)
            {
                int group = vertexToGroup[vertex];
                if (!movedGroups[group])
                    continue;
                candidates[vertex] = Vector3.LerpUnclamped(
                    originals[vertex],
                    groupTargets[group],
                    blend);
            }
        }

        private static bool TryValidateProjectedLoop(
            BoundaryLoop loop,
            List<WeldGroup> weldGroups,
            Dictionary<int, Vector3> targets,
            float blend,
            float weldTolerance,
            out float signedArea)
        {
            signedArea = 0f;
            int count = loop.groups.Count;
            Vector2[] projected = new Vector2[count];
            float maximumExtent = weldTolerance;
            for (int index = 0; index < count; index++)
            {
                int group = loop.groups[index];
                Vector3 point = weldGroups[group].worldPosition;
                if (blend > 0f && targets.TryGetValue(group, out Vector3 target))
                    point = Vector3.LerpUnclamped(point, target, blend);
                Vector3 delta = point - loop.projectionOrigin;
                projected[index] = new Vector2(
                    Vector3.Dot(delta, loop.projectionRight),
                    Vector3.Dot(delta, loop.projectionUp));
                maximumExtent = Mathf.Max(maximumExtent, projected[index].magnitude);
            }

            float epsilon = Mathf.Max(weldTolerance, maximumExtent * 0.000001f);
            float epsilonSquared = epsilon * epsilon;
            double area = 0d;
            for (int index = 0; index < count; index++)
            {
                Vector2 a = projected[index];
                Vector2 b = projected[(index + 1) % count];
                if ((b - a).sqrMagnitude <= epsilonSquared)
                    return false;
                area += (double)a.x * b.y - (double)b.x * a.y;
            }
            signedArea = (float)(area * 0.5d);
            if (!IsFinite(signedArea) ||
                Mathf.Abs(signedArea) <= epsilonSquared * count)
            {
                return false;
            }

            for (int first = 0; first < count; first++)
            {
                int firstNext = (first + 1) % count;
                for (int second = first + 1; second < count; second++)
                {
                    int secondNext = (second + 1) % count;
                    if (first == second ||
                        firstNext == second ||
                        secondNext == first)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(
                            projected[first],
                            projected[firstNext],
                            projected[second],
                            projected[secondNext],
                            epsilon))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float epsilon)
        {
            float orientationTolerance = epsilon * Mathf.Max(
                epsilon,
                Mathf.Max((b - a).magnitude, (d - c).magnitude));
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);

            if (((abC > orientationTolerance && abD < -orientationTolerance) ||
                 (abC < -orientationTolerance && abD > orientationTolerance)) &&
                ((cdA > orientationTolerance && cdB < -orientationTolerance) ||
                 (cdA < -orientationTolerance && cdB > orientationTolerance)))
            {
                return true;
            }

            if (Mathf.Abs(abC) <= orientationTolerance && IsOnSegment(a, b, c, epsilon)) return true;
            if (Mathf.Abs(abD) <= orientationTolerance && IsOnSegment(a, b, d, epsilon)) return true;
            if (Mathf.Abs(cdA) <= orientationTolerance && IsOnSegment(c, d, a, epsilon)) return true;
            if (Mathf.Abs(cdB) <= orientationTolerance && IsOnSegment(c, d, b, epsilon)) return true;
            return false;
        }

        private static bool IsOnSegment(
            Vector2 a,
            Vector2 b,
            Vector2 point,
            float epsilon)
        {
            return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + epsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - epsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + epsilon;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int index = 0; index < 16; index++)
            {
                if (!IsFinite(value[index]))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class WeldGroup
        {
            internal readonly List<int> vertexIndices = new List<int>(1);
            internal Vector3 worldPosition;

            internal WeldGroup(int vertex, Vector3 position)
            {
                vertexIndices.Add(vertex);
                worldPosition = position;
            }

            internal void Add(int vertex, Vector3 position)
            {
                int previousCount = vertexIndices.Count;
                vertexIndices.Add(vertex);
                worldPosition =
                    (worldPosition * previousCount + position) / vertexIndices.Count;
            }
        }

        private sealed class EdgeIncidence
        {
            internal int total;
            internal int removed;
            internal int retained;
        }

        private sealed class BoundaryLoop
        {
            internal readonly List<int> groups;
            internal Vector3 approximateNormal;
            internal Vector3 projectionOrigin;
            internal Vector3 projectionRight;
            internal Vector3 projectionUp;
            internal float originalSignedArea;

            internal BoundaryLoop(List<int> orderedGroups)
            {
                groups = orderedGroups;
            }
        }

        private struct SourceTriangle
        {
            internal readonly int a;
            internal readonly int b;
            internal readonly int c;
            internal readonly int groupA;
            internal readonly int groupB;
            internal readonly int groupC;
            internal readonly bool removed;

            internal SourceTriangle(
                int a,
                int b,
                int c,
                int groupA,
                int groupB,
                int groupC,
                bool removed)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                this.groupA = groupA;
                this.groupB = groupB;
                this.groupC = groupC;
                this.removed = removed;
            }
        }

        private struct CircleFrame
        {
            internal readonly Vector3 center;
            internal readonly Vector3 normal;
            internal readonly float radius;

            internal CircleFrame(Vector3 center, Vector3 normal, float radius)
            {
                this.center = center;
                this.normal = normal;
                this.radius = radius;
            }
        }

        private struct WeldEdge : IEquatable<WeldEdge>
        {
            internal readonly int a;
            internal readonly int b;

            internal WeldEdge(int first, int second)
            {
                if (first <= second)
                {
                    a = first;
                    b = second;
                }
                else
                {
                    a = second;
                    b = first;
                }
            }

            internal int Other(int vertex)
            {
                return vertex == a ? b : a;
            }

            public bool Equals(WeldEdge other)
            {
                return a == other.a && b == other.b;
            }

            public override bool Equals(object obj)
            {
                return obj is WeldEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (a * 397) ^ b;
                }
            }
        }

        private struct SpatialCell : IEquatable<SpatialCell>
        {
            internal readonly long x;
            internal readonly long y;
            internal readonly long z;

            internal SpatialCell(long x, long y, long z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            internal static SpatialCell FromPoint(Vector3 point, float cellSize)
            {
                double inverse = 1d / cellSize;
                return new SpatialCell(
                    (long)Math.Floor(point.x * inverse),
                    (long)Math.Floor(point.y * inverse),
                    (long)Math.Floor(point.z * inverse));
            }

            public bool Equals(SpatialCell other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is SpatialCell other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x.GetHashCode();
                    hash = (hash * 397) ^ y.GetHashCode();
                    hash = (hash * 397) ^ z.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
