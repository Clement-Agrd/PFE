using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace RealBlend
{
    /// <summary>
    /// Describes the non-destructive preparation performed for a Boolean operand.
    /// A returned healed mesh is temporary and is owned by the caller.
    /// </summary>
    public struct RealBlendMeshOperandHealReport
    {
        public bool repairNeeded;
        public bool solidifiedOpenSurface;
        public int inputTriangles;
        public int outputTriangles;
        public int removedDegenerateTriangles;
        public int removedDuplicateTriangles;
        public int removedNonManifoldTriangles;
        public int flippedTriangles;
        public int weldedCrackVertices;
        public int splitBoundaryEdges;
        public int cappedBoundaryLoops;
        public int capTriangles;
        public float usedTolerance;
        public float solidifyThickness;
    }

    /// <summary>
    /// Converts common imperfect triangle meshes into closed, consistently wound
    /// Boolean operands without changing the source asset. Clean operands take the
    /// allocation-free path: success is true, repairNeeded is false, and healedMesh
    /// is null. The caller must destroy a non-null healedMesh when it is no longer used.
    /// </summary>
    public static class RealBlendMeshBooleanOperandHealer
    {
        private const float MinimumTolerance = 0.0000001f;

        public static bool TryCreateUsableSolid(
            Mesh sourceMesh,
            float tolerance,
            out Mesh healedMesh,
            out bool repairNeeded,
            out RealBlendMeshOperandHealReport report,
            out string diagnostic)
        {
            healedMesh = null;
            repairNeeded = false;
            report = default;
            diagnostic = string.Empty;

            if (sourceMesh == null)
            {
                diagnostic = "No Boolean operand was supplied.";
                return false;
            }

            if (!sourceMesh.isReadable)
            {
                diagnostic = "The Boolean operand is not CPU-readable at edit time.";
                return false;
            }

            Vector3[] sourcePositions;
            try
            {
                sourcePositions = sourceMesh.vertices;
            }
            catch (Exception exception)
            {
                diagnostic = "The Boolean operand could not be read: " + exception.Message;
                return false;
            }

            if (sourcePositions == null || sourcePositions.Length < 3)
            {
                diagnostic = "The Boolean operand contains no usable surface.";
                return false;
            }

            float diagonal = Mathf.Max(sourceMesh.bounds.size.magnitude, MinimumTolerance);
            float usedTolerance = Mathf.Max(
                MinimumTolerance,
                Mathf.Max(Mathf.Abs(tolerance), diagonal * 0.0000001f));
            report.usedTolerance = usedTolerance;

            SourceAttributes attributes = ReadAttributes(sourceMesh, sourcePositions.Length);
            WeldResult weld = WeldPositions(sourcePositions, usedTolerance);
            attributes.SetRepresentatives(weld.vertexToPosition, weld.positions.Count);
            List<Vector3> positions = weld.positions;
            report.weldedCrackVertices = weld.movedVertexCount;

            List<HealFace> faces;
            if (!TryReadFaces(
                    sourceMesh,
                    sourcePositions,
                    weld.vertexToPosition,
                    positions,
                    usedTolerance,
                    out faces,
                    ref report,
                    out diagnostic))
            {
                return false;
            }

            report.inputTriangles = faces.Count +
                                    report.removedDegenerateTriangles +
                                    report.removedDuplicateTriangles;
            if (faces.Count == 0)
            {
                diagnostic = "The Boolean operand contains no usable surface.";
                return false;
            }

            report.splitBoundaryEdges = ResolveCollinearBoundaryTJunctions(
                faces,
                positions,
                attributes,
                usedTolerance);
            RemoveNonManifoldOverflow(faces, positions, ref report);
            if (faces.Count == 0)
            {
                diagnostic = "The Boolean operand contains no usable surface.";
                return false;
            }

            List<List<int>> components;
            bool orientationConflict = OrientFaces(faces, positions, ref report, out components);
            bool changed = report.removedDegenerateTriangles > 0 ||
                           report.removedDuplicateTriangles > 0 ||
                           report.removedNonManifoldTriangles > 0 ||
                           report.flippedTriangles > 0 ||
                           report.splitBoundaryEdges > 0 ||
                           report.weldedCrackVertices > 0;

            List<HealFace> finalFaces = new List<HealFace>(faces.Count + 32);
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                List<int> component = components[componentIndex];
                List<BoundaryEdge> boundary = GetBoundaryEdges(faces, component);
                bool shouldSolidify = orientationConflict ||
                                      (boundary.Count > 0 &&
                                       IsNearlyPlanar(faces, component, positions, usedTolerance));

                if (!shouldSolidify && boundary.Count == 0)
                {
                    float volume = SignedVolume(faces, component, positions);
                    float volumeThreshold = VolumeThreshold(faces, component, positions, usedTolerance);
                    if (Mathf.Abs(volume) <= volumeThreshold)
                    {
                        shouldSolidify = true;
                    }
                    else
                    {
                        if (volume < 0f)
                        {
                            FlipComponent(faces, component, ref report);
                            changed = true;
                        }

                        AppendComponentFaces(finalFaces, faces, component);
                        continue;
                    }
                }

                if (!shouldSolidify && boundary.Count > 0)
                {
                    int firstAddedFace = finalFaces.Count;
                    AppendComponentFaces(finalFaces, faces, component);
                    if (TryCapBoundaryLoops(
                            finalFaces,
                            positions,
                            attributes,
                            boundary,
                            usedTolerance,
                            ref report))
                    {
                        List<int> cappedComponent = SequentialIndices(
                            firstAddedFace,
                            finalFaces.Count - firstAddedFace);
                        float volume = SignedVolume(finalFaces, cappedComponent, positions);
                        float volumeThreshold = VolumeThreshold(
                            finalFaces,
                            cappedComponent,
                            positions,
                            usedTolerance);
                        if (Mathf.Abs(volume) > volumeThreshold)
                        {
                            if (volume < 0f)
                            {
                                FlipComponent(finalFaces, cappedComponent, ref report);
                            }

                            changed = true;
                            continue;
                        }
                    }

                    finalFaces.RemoveRange(firstAddedFace, finalFaces.Count - firstAddedFace);
                    shouldSolidify = true;
                }

                if (shouldSolidify)
                {
                    float thickness = SolidifyComponent(
                        finalFaces,
                        faces,
                        component,
                        positions,
                        attributes,
                        usedTolerance);
                    report.solidifiedOpenSurface = true;
                    report.solidifyThickness = Mathf.Max(report.solidifyThickness, thickness);
                    changed = true;
                }
            }

            if (finalFaces.Count == 0 || !HasClosedOrientedTopology(finalFaces))
            {
                diagnostic = "Automatic Boolean preparation could not construct a stable solid.";
                return false;
            }

            report.outputTriangles = finalFaces.Count;
            repairNeeded = changed;
            report.repairNeeded = changed;
            if (!changed)
            {
                diagnostic = "Ready as supplied.";
                return true;
            }

            healedMesh = BuildMesh(
                sourceMesh,
                positions,
                finalFaces,
                attributes,
                usedTolerance);
            if (healedMesh == null)
            {
                repairNeeded = false;
                report.repairNeeded = false;
                diagnostic = "Automatic Boolean preparation could not build the temporary solid.";
                return false;
            }

            diagnostic = BuildDiagnostic(report);
            return true;
        }

        private static SourceAttributes ReadAttributes(Mesh mesh, int vertexCount)
        {
            Vector2[] sourceUV0 = mesh.uv;
            Color[] sourceColors = mesh.colors;
            SourceAttributes attributes = new SourceAttributes
            {
                uv0 = sourceUV0 != null && sourceUV0.Length == vertexCount
                    ? sourceUV0
                    : null,
                colors = sourceColors != null && sourceColors.Length == vertexCount
                    ? sourceColors
                    : null
            };

            List<Vector2> uv2 = new List<Vector2>();
            mesh.GetUVs(2, uv2);
            attributes.uv2 = uv2.Count == vertexCount ? uv2.ToArray() : null;
            attributes.SetSourceCount(vertexCount);
            return attributes;
        }

        private static bool TryReadFaces(
            Mesh mesh,
            Vector3[] sourcePositions,
            int[] vertexToPosition,
            List<Vector3> weldedPositions,
            float tolerance,
            out List<HealFace> faces,
            ref RealBlendMeshOperandHealReport report,
            out string diagnostic)
        {
            faces = new List<HealFace>();
            diagnostic = string.Empty;
            HashSet<TriangleKey> uniqueTriangles = new HashSet<TriangleKey>();
            float areaTolerance = Mathf.Max(
                tolerance * tolerance * 0.01f,
                mesh.bounds.size.sqrMagnitude * 0.000000000001f);

            try
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    {
                        diagnostic = "Automatic Boolean preparation only consumes triangle submeshes.";
                        return false;
                    }

                    int[] triangles = mesh.GetTriangles(submesh, false);
                    for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                    {
                        int sourceA = triangles[triangle];
                        int sourceB = triangles[triangle + 1];
                        int sourceC = triangles[triangle + 2];
                        if (sourceA < 0 || sourceB < 0 || sourceC < 0 ||
                            sourceA >= sourcePositions.Length ||
                            sourceB >= sourcePositions.Length ||
                            sourceC >= sourcePositions.Length)
                        {
                            report.removedDegenerateTriangles++;
                            continue;
                        }

                        int a = vertexToPosition[sourceA];
                        int b = vertexToPosition[sourceB];
                        int c = vertexToPosition[sourceC];
                        Vector3 cross = Vector3.Cross(
                            weldedPositions[b] - weldedPositions[a],
                            weldedPositions[c] - weldedPositions[a]);
                        if (a == b || b == c || c == a ||
                            !IsFinite(cross) || cross.magnitude <= areaTolerance)
                        {
                            report.removedDegenerateTriangles++;
                            continue;
                        }

                        TriangleKey key = new TriangleKey(a, b, c);
                        if (!uniqueTriangles.Add(key))
                        {
                            report.removedDuplicateTriangles++;
                            continue;
                        }

                        faces.Add(new HealFace(
                            a,
                            b,
                            c,
                            sourceA,
                            sourceB,
                            sourceC,
                            submesh));
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostic = "The Boolean operand topology could not be read: " + exception.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves a common generated-mesh seam without adding or removing any
        /// surface. Two coincident boundary chains can describe the same closed
        /// edge with different subdivisions (for example ten intervals opposite
        /// eleven). Split the longer adjacent triangle edges at the union of the
        /// collinear boundary breakpoints so the resulting atomic intervals pair.
        /// </summary>
        private static int ResolveCollinearBoundaryTJunctions(
            List<HealFace> faces,
            List<Vector3> positions,
            SourceAttributes attributes,
            float tolerance)
        {
            const int MaximumPasses = 32;
            const long MaximumCandidateComparisons = 4000000L;
            int splitEdgeCount = 0;
            long candidateComparisons = 0L;
            float squaredTolerance = tolerance * tolerance;
            for (int pass = 0; pass < MaximumPasses; pass++)
            {
                Dictionary<EdgeKey, List<EdgeUse>> uses =
                    BuildEdgeUses(faces, null);
                Dictionary<int, List<int>> boundaryNeighbors =
                    new Dictionary<int, List<int>>();
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    HealFace face = faces[faceIndex];
                    for (int edge = 0; edge < 3; edge++)
                    {
                        face.GetDirectedEdge(edge, out int start, out int end);
                        if (!uses.TryGetValue(
                                new EdgeKey(start, end),
                                out List<EdgeUse> edgeUses) ||
                            edgeUses.Count != 1)
                        {
                            continue;
                        }

                        AddBoundaryNeighbor(boundaryNeighbors, start, end);
                        AddBoundaryNeighbor(boundaryNeighbors, end, start);
                    }
                }
                if (boundaryNeighbors.Count == 0)
                    break;

                List<int> boundaryVertices =
                    new List<int>(boundaryNeighbors.Keys);
                boundaryVertices.Sort();
                foreach (KeyValuePair<int, List<int>> pair in boundaryNeighbors)
                    pair.Value.Sort();

                Dictionary<int, BoundarySplitPlan> plans =
                    new Dictionary<int, BoundarySplitPlan>();
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    HealFace face = faces[faceIndex];
                    for (int edge = 0; edge < 3; edge++)
                    {
                        GetFaceEdge(
                            face,
                            edge,
                            out int start,
                            out int end,
                            out int opposite,
                            out int sourceStart,
                            out int sourceEnd,
                            out int sourceOpposite);
                        if (!uses.TryGetValue(
                                new EdgeKey(start, end),
                                out List<EdgeUse> edgeUses) ||
                            edgeUses.Count != 1)
                        {
                            continue;
                        }

                        Vector3 edgeStart = positions[start];
                        Vector3 edgeVector = positions[end] - edgeStart;
                        float edgeLengthSquared = edgeVector.sqrMagnitude;
                        if (edgeLengthSquared <= squaredTolerance)
                            continue;
                        float edgeLength = Mathf.Sqrt(edgeLengthSquared);
                        float endpointGuard = Mathf.Min(
                            0.25f,
                            tolerance * 0.5f / edgeLength);
                        List<BoundarySplitPoint> splitPoints =
                            new List<BoundarySplitPoint>();
                        for (int candidateIndex = 0;
                             candidateIndex < boundaryVertices.Count;
                             candidateIndex++)
                        {
                            // A malformed open soup can have every triangle edge
                            // on the boundary. Keep automatic preparation bounded;
                            // a partial set of completed passes is still the exact
                            // same surface and the ordinary cap/solidify fallback
                            // can handle whatever remains.
                            if (++candidateComparisons >
                                MaximumCandidateComparisons)
                            {
                                return splitEdgeCount;
                            }
                            int candidate = boundaryVertices[candidateIndex];
                            if (candidate == start ||
                                candidate == end ||
                                candidate == opposite)
                            {
                                continue;
                            }
                            Vector3 offset = positions[candidate] - edgeStart;
                            float amount = Vector3.Dot(offset, edgeVector) /
                                           edgeLengthSquared;
                            if (amount <= endpointGuard ||
                                amount >= 1f - endpointGuard)
                            {
                                continue;
                            }
                            Vector3 projected = edgeStart + edgeVector * amount;
                            if ((positions[candidate] - projected).sqrMagnitude >
                                squaredTolerance)
                            {
                                continue;
                            }
                            if (!ContinuesCollinearBoundary(
                                    candidate,
                                    edgeVector,
                                    positions,
                                    boundaryNeighbors,
                                    tolerance))
                            {
                                continue;
                            }
                            splitPoints.Add(new BoundarySplitPoint(
                                candidate,
                                amount));
                        }
                        if (splitPoints.Count == 0)
                            continue;

                        splitPoints.Sort(CompareBoundarySplitPoints);
                        for (int index = splitPoints.Count - 1; index > 0; index--)
                        {
                            if (splitPoints[index].position ==
                                splitPoints[index - 1].position)
                            {
                                splitPoints.RemoveAt(index);
                            }
                        }
                        plans.Add(faceIndex, new BoundarySplitPlan(
                            start,
                            end,
                            opposite,
                            sourceStart,
                            sourceEnd,
                            sourceOpposite,
                            face.submesh,
                            splitPoints));
                        // Rebuild at most one edge of a triangle per pass. Any
                        // second boundary edge remains exposed on one child and
                        // is handled deterministically by the next pass.
                        break;
                    }
                }
                if (plans.Count == 0)
                    break;

                List<HealFace> rebuilt = new List<HealFace>(
                    faces.Count + plans.Count * 2);
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    if (!plans.TryGetValue(
                            faceIndex,
                            out BoundarySplitPlan plan))
                    {
                        rebuilt.Add(faces[faceIndex]);
                        continue;
                    }

                    int previousPosition = plan.start;
                    int previousSource = plan.sourceStart;
                    for (int splitIndex = 0;
                         splitIndex < plan.points.Count;
                         splitIndex++)
                    {
                        BoundarySplitPoint point = plan.points[splitIndex];
                        int pointSource = attributes.InterpolateSource(
                            plan.sourceStart,
                            plan.sourceEnd,
                            point.amount);
                        rebuilt.Add(new HealFace(
                            previousPosition,
                            point.position,
                            plan.opposite,
                            previousSource,
                            pointSource,
                            plan.sourceOpposite,
                            plan.submesh));
                        previousPosition = point.position;
                        previousSource = pointSource;
                    }
                    rebuilt.Add(new HealFace(
                        previousPosition,
                        plan.end,
                        plan.opposite,
                        previousSource,
                        plan.sourceEnd,
                        plan.sourceOpposite,
                        plan.submesh));
                }

                faces.Clear();
                faces.AddRange(rebuilt);
                splitEdgeCount += plans.Count;
            }
            return splitEdgeCount;
        }

        private static void AddBoundaryNeighbor(
            Dictionary<int, List<int>> neighbors,
            int position,
            int neighbor)
        {
            if (!neighbors.TryGetValue(position, out List<int> values))
            {
                values = new List<int>(2);
                neighbors.Add(position, values);
            }
            if (!values.Contains(neighbor))
                values.Add(neighbor);
        }

        private static bool ContinuesCollinearBoundary(
            int position,
            Vector3 reference,
            List<Vector3> positions,
            Dictionary<int, List<int>> boundaryNeighbors,
            float tolerance)
        {
            if (!boundaryNeighbors.TryGetValue(
                    position,
                    out List<int> neighbors))
            {
                return false;
            }
            float referenceLengthSquared = reference.sqrMagnitude;
            for (int index = 0; index < neighbors.Count; index++)
            {
                Vector3 direction = positions[neighbors[index]] -
                                    positions[position];
                float directionLengthSquared = direction.sqrMagnitude;
                if (directionLengthSquared <= tolerance * tolerance)
                    continue;
                float absoluteCosine = Mathf.Abs(Vector3.Dot(
                    reference,
                    direction)) /
                    Mathf.Sqrt(referenceLengthSquared * directionLengthSquared);
                if (absoluteCosine >= 0.9999f)
                    return true;
            }
            return false;
        }

        private static int CompareBoundarySplitPoints(
            BoundarySplitPoint left,
            BoundarySplitPoint right)
        {
            int comparison = left.amount.CompareTo(right.amount);
            return comparison != 0
                ? comparison
                : left.position.CompareTo(right.position);
        }

        private static void GetFaceEdge(
            HealFace face,
            int edge,
            out int start,
            out int end,
            out int opposite,
            out int sourceStart,
            out int sourceEnd,
            out int sourceOpposite)
        {
            if (edge == 0)
            {
                start = face.a;
                end = face.b;
                opposite = face.c;
                sourceStart = face.sourceA;
                sourceEnd = face.sourceB;
                sourceOpposite = face.sourceC;
            }
            else if (edge == 1)
            {
                start = face.b;
                end = face.c;
                opposite = face.a;
                sourceStart = face.sourceB;
                sourceEnd = face.sourceC;
                sourceOpposite = face.sourceA;
            }
            else
            {
                start = face.c;
                end = face.a;
                opposite = face.b;
                sourceStart = face.sourceC;
                sourceEnd = face.sourceA;
                sourceOpposite = face.sourceB;
            }
        }

        private static void RemoveNonManifoldOverflow(
            List<HealFace> faces,
            List<Vector3> positions,
            ref RealBlendMeshOperandHealReport report)
        {
            // Imported meshes most often become non-manifold because a face was
            // exported twice. Exact duplicates are removed above. This second pass
            // handles remaining edge fans deterministically, then the newly exposed
            // boundary is closed by the cap/solidify stage.
            for (int pass = 0; pass < 8; pass++)
            {
                Dictionary<EdgeKey, List<EdgeUse>> uses = BuildEdgeUses(faces, null);
                HashSet<int> remove = new HashSet<int>();
                foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in uses)
                {
                    List<EdgeUse> edgeUses = pair.Value;
                    if (edgeUses.Count <= 2)
                        continue;

                    int keepA = 0;
                    int keepB = 1;
                    float bestScore = float.NegativeInfinity;
                    for (int a = 0; a < edgeUses.Count; a++)
                    {
                        for (int b = a + 1; b < edgeUses.Count; b++)
                        {
                            Vector3 normalA = FaceNormal(faces[edgeUses[a].face], positions);
                            Vector3 normalB = FaceNormal(faces[edgeUses[b].face], positions);
                            float oppositeDirectionBonus =
                                edgeUses[a].forward != edgeUses[b].forward ? 4f : 0f;
                            float score = oppositeDirectionBonus +
                                          (1f - Mathf.Abs(Vector3.Dot(normalA, normalB)));
                            if (score > bestScore)
                            {
                                bestScore = score;
                                keepA = a;
                                keepB = b;
                            }
                        }
                    }

                    for (int use = 0; use < edgeUses.Count; use++)
                    {
                        if (use != keepA && use != keepB)
                            remove.Add(edgeUses[use].face);
                    }
                }

                if (remove.Count == 0)
                    return;

                List<HealFace> kept = new List<HealFace>(faces.Count - remove.Count);
                for (int face = 0; face < faces.Count; face++)
                {
                    if (!remove.Contains(face))
                        kept.Add(faces[face]);
                }

                report.removedNonManifoldTriangles += faces.Count - kept.Count;
                faces.Clear();
                faces.AddRange(kept);
            }
        }

        private static bool OrientFaces(
            List<HealFace> faces,
            List<Vector3> positions,
            ref RealBlendMeshOperandHealReport report,
            out List<List<int>> components)
        {
            Dictionary<EdgeKey, List<EdgeUse>> uses = BuildEdgeUses(faces, null);
            List<List<OrientationLink>> links = new List<List<OrientationLink>>(faces.Count);
            for (int face = 0; face < faces.Count; face++)
                links.Add(new List<OrientationLink>(3));

            foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in uses)
            {
                List<EdgeUse> edgeUses = pair.Value;
                if (edgeUses.Count != 2)
                    continue;

                EdgeUse a = edgeUses[0];
                EdgeUse b = edgeUses[1];
                bool needsOppositeFlip = a.forward == b.forward;
                links[a.face].Add(new OrientationLink(b.face, needsOppositeFlip));
                links[b.face].Add(new OrientationLink(a.face, needsOppositeFlip));
            }

            components = new List<List<int>>();
            int[] state = new int[faces.Count];
            bool conflict = false;
            Queue<int> queue = new Queue<int>();
            for (int start = 0; start < faces.Count; start++)
            {
                if (state[start] != 0)
                    continue;

                List<int> component = new List<int>();
                state[start] = 1;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int face = queue.Dequeue();
                    component.Add(face);
                    List<OrientationLink> faceLinks = links[face];
                    for (int linkIndex = 0; linkIndex < faceLinks.Count; linkIndex++)
                    {
                        OrientationLink link = faceLinks[linkIndex];
                        bool currentFlipped = state[face] == 2;
                        bool expectedFlip = currentFlipped ^ link.needsOppositeFlip;
                        int expectedState = expectedFlip ? 2 : 1;
                        if (state[link.face] == 0)
                        {
                            state[link.face] = expectedState;
                            queue.Enqueue(link.face);
                        }
                        else if (state[link.face] != expectedState)
                        {
                            conflict = true;
                        }
                    }
                }

                components.Add(component);
            }

            for (int face = 0; face < faces.Count; face++)
            {
                if (state[face] == 2)
                {
                    faces[face].Flip();
                    report.flippedTriangles++;
                }
            }

            return conflict;
        }

        private static List<BoundaryEdge> GetBoundaryEdges(
            List<HealFace> faces,
            List<int> component)
        {
            Dictionary<EdgeKey, List<EdgeUse>> uses = BuildEdgeUses(faces, component);
            List<BoundaryEdge> boundary = new List<BoundaryEdge>();
            foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in uses)
            {
                if (pair.Value.Count != 1)
                    continue;

                EdgeUse use = pair.Value[0];
                HealFace face = faces[use.face];
                int a;
                int b;
                face.GetDirectedEdge(use.edge, out a, out b);
                boundary.Add(new BoundaryEdge(a, b, use.face, face.submesh));
            }

            return boundary;
        }

        private static bool TryCapBoundaryLoops(
            List<HealFace> output,
            List<Vector3> positions,
            SourceAttributes attributes,
            List<BoundaryEdge> boundary,
            float tolerance,
            ref RealBlendMeshOperandHealReport report)
        {
            List<List<BoundaryEdge>> loops;
            if (!TryBuildBoundaryLoops(boundary, out loops))
                return false;

            int addedTriangles = 0;
            for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                List<BoundaryEdge> edges = loops[loopIndex];
                List<int> loop = new List<int>(edges.Count);
                for (int edge = edges.Count - 1; edge >= 0; edge--)
                    loop.Add(edges[edge].b);

                List<IntTriangle> triangles;
                if (!TryTriangulateLoop(loop, positions, tolerance, out triangles))
                    return false;

                int submesh = edges[0].submesh;
                for (int triangle = 0; triangle < triangles.Count; triangle++)
                {
                    IntTriangle indices = triangles[triangle];
                    output.Add(new HealFace(
                        indices.a,
                        indices.b,
                        indices.c,
                        attributes.RepresentativeSource(indices.a),
                        attributes.RepresentativeSource(indices.b),
                        attributes.RepresentativeSource(indices.c),
                        submesh));
                }

                addedTriangles += triangles.Count;
            }

            report.cappedBoundaryLoops += loops.Count;
            report.capTriangles += addedTriangles;
            return true;
        }

        private static bool TryBuildBoundaryLoops(
            List<BoundaryEdge> boundary,
            out List<List<BoundaryEdge>> loops)
        {
            loops = new List<List<BoundaryEdge>>();
            Dictionary<int, List<int>> outgoing = new Dictionary<int, List<int>>();
            Dictionary<int, int> incomingCounts = new Dictionary<int, int>();
            for (int edge = 0; edge < boundary.Count; edge++)
            {
                BoundaryEdge current = boundary[edge];
                List<int> list;
                if (!outgoing.TryGetValue(current.a, out list))
                {
                    list = new List<int>(1);
                    outgoing.Add(current.a, list);
                }

                list.Add(edge);
                int incoming;
                incomingCounts.TryGetValue(current.b, out incoming);
                incomingCounts[current.b] = incoming + 1;
            }

            foreach (KeyValuePair<int, List<int>> pair in outgoing)
            {
                int incoming;
                incomingCounts.TryGetValue(pair.Key, out incoming);
                if (pair.Value.Count != 1 || incoming != 1)
                    return false;
            }

            bool[] used = new bool[boundary.Count];
            for (int start = 0; start < boundary.Count; start++)
            {
                if (used[start])
                    continue;

                List<BoundaryEdge> loop = new List<BoundaryEdge>();
                int currentIndex = start;
                int firstVertex = boundary[start].a;
                for (int guard = 0; guard <= boundary.Count; guard++)
                {
                    if (used[currentIndex])
                        return false;

                    BoundaryEdge edge = boundary[currentIndex];
                    used[currentIndex] = true;
                    loop.Add(edge);
                    if (edge.b == firstVertex)
                        break;

                    List<int> next;
                    if (!outgoing.TryGetValue(edge.b, out next) || next.Count != 1)
                        return false;
                    currentIndex = next[0];
                }

                if (loop.Count < 3 || loop[loop.Count - 1].b != firstVertex)
                    return false;
                loops.Add(loop);
            }

            return loops.Count > 0;
        }

        private static bool TryTriangulateLoop(
            List<int> loop,
            List<Vector3> positions,
            float tolerance,
            out List<IntTriangle> triangles)
        {
            triangles = new List<IntTriangle>();
            if (loop.Count < 3)
                return false;

            Vector3 normal = Vector3.zero;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 current = positions[loop[i]];
                Vector3 next = positions[loop[(i + 1) % loop.Count]];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            if (normal.sqrMagnitude <= tolerance * tolerance * tolerance * tolerance)
                return false;

            int dropAxis = DominantAxis(normal);
            List<int> remaining = new List<int>(loop);
            float signedArea = 0f;
            for (int i = 0; i < remaining.Count; i++)
            {
                Vector2 a = Project(positions[remaining[i]], dropAxis);
                Vector2 b = Project(positions[remaining[(i + 1) % remaining.Count]], dropAxis);
                signedArea += a.x * b.y - b.x * a.y;
            }

            float orientation = signedArea >= 0f ? 1f : -1f;
            float crossTolerance = tolerance * tolerance * 0.01f;
            int guardCount = 0;
            while (remaining.Count > 3 && guardCount++ < loop.Count * loop.Count)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = Project(positions[previous], dropAxis);
                    Vector2 b = Project(positions[current], dropAxis);
                    Vector2 c = Project(positions[next], dropAxis);
                    float cross = Cross2D(b - a, c - b) * orientation;
                    if (cross <= crossTolerance)
                        continue;

                    bool containsVertex = false;
                    for (int test = 0; test < remaining.Count; test++)
                    {
                        int candidate = remaining[test];
                        if (candidate == previous || candidate == current || candidate == next)
                            continue;
                        if (PointInTriangle(
                                Project(positions[candidate], dropAxis),
                                a,
                                b,
                                c,
                                orientation,
                                crossTolerance))
                        {
                            containsVertex = true;
                            break;
                        }
                    }

                    if (containsVertex)
                        continue;

                    triangles.Add(new IntTriangle(previous, current, next));
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    return false;
            }

            if (remaining.Count == 3)
            {
                triangles.Add(new IntTriangle(remaining[0], remaining[1], remaining[2]));
            }

            return triangles.Count == loop.Count - 2;
        }

        private static float SolidifyComponent(
            List<HealFace> output,
            List<HealFace> sourceFaces,
            List<int> component,
            List<Vector3> positions,
            SourceAttributes attributes,
            float tolerance)
        {
            HashSet<int> usedPositions = new HashSet<int>();
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = sourceFaces[component[i]];
                usedPositions.Add(face.a);
                usedPositions.Add(face.b);
                usedPositions.Add(face.c);
            }

            Bounds bounds = BoundsForPositions(usedPositions, positions);
            float diagonal = Mathf.Max(bounds.size.magnitude, tolerance);
            float thickness = Mathf.Max(tolerance * 4f, diagonal * 0.001f);
            thickness = Mathf.Min(thickness, diagonal * 0.02f);
            float halfThickness = thickness * 0.5f;

            Dictionary<int, Vector3> normalSums = new Dictionary<int, Vector3>();
            foreach (int position in usedPositions)
                normalSums[position] = Vector3.zero;
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = sourceFaces[component[i]];
                Vector3 normal = Vector3.Cross(
                    positions[face.b] - positions[face.a],
                    positions[face.c] - positions[face.a]);
                normalSums[face.a] += normal;
                normalSums[face.b] += normal;
                normalSums[face.c] += normal;
            }

            Dictionary<int, int> top = new Dictionary<int, int>();
            Dictionary<int, int> bottom = new Dictionary<int, int>();
            foreach (int position in usedPositions)
            {
                Vector3 normal = normalSums[position];
                if (normal.sqrMagnitude <= 0.000000000001f)
                    normal = Vector3.up;
                else
                    normal.Normalize();
                top[position] = positions.Count;
                positions.Add(positions[position] + normal * halfThickness);
                bottom[position] = positions.Count;
                positions.Add(positions[position] - normal * halfThickness);
            }

            int firstOutputFace = output.Count;
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = sourceFaces[component[i]];
                output.Add(new HealFace(
                    top[face.a], top[face.b], top[face.c],
                    face.sourceA, face.sourceB, face.sourceC, face.submesh));
                output.Add(new HealFace(
                    bottom[face.c], bottom[face.b], bottom[face.a],
                    face.sourceC, face.sourceB, face.sourceA, face.submesh));
            }

            List<BoundaryEdge> boundary = GetBoundaryEdges(sourceFaces, component);
            for (int edge = 0; edge < boundary.Count; edge++)
            {
                BoundaryEdge side = boundary[edge];
                int sourceA = attributes.RepresentativeSource(side.a);
                int sourceB = attributes.RepresentativeSource(side.b);
                output.Add(new HealFace(
                    top[side.b], top[side.a], bottom[side.a],
                    sourceB, sourceA, sourceA, side.submesh));
                output.Add(new HealFace(
                    top[side.b], bottom[side.a], bottom[side.b],
                    sourceB, sourceA, sourceB, side.submesh));
            }

            List<int> solidComponent = SequentialIndices(
                firstOutputFace,
                output.Count - firstOutputFace);
            if (SignedVolume(output, solidComponent, positions) < 0f)
            {
                for (int i = 0; i < solidComponent.Count; i++)
                    output[solidComponent[i]].Flip();
            }

            return thickness;
        }

        private static Mesh BuildMesh(
            Mesh source,
            List<Vector3> positions,
            List<HealFace> faces,
            SourceAttributes attributes,
            float tolerance)
        {
            List<Vector3> vertices = new List<Vector3>(faces.Count * 2);
            List<Vector2> uv0 = new List<Vector2>(faces.Count * 2);
            List<Vector2> uv2 = new List<Vector2>(faces.Count * 2);
            List<Color> colors = new List<Color>(faces.Count * 2);
            int submeshCount = Mathf.Max(1, source.subMeshCount);
            List<int>[] triangles = new List<int>[submeshCount];
            for (int submesh = 0; submesh < submeshCount; submesh++)
                triangles[submesh] = new List<int>();

            Dictionary<OutputVertexKey, int> lookup =
                new Dictionary<OutputVertexKey, int>(faces.Count * 2);
            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                HealFace face = faces[faceIndex];
                int outputA = AddOutputVertex(
                    face.a, face.sourceA, positions, attributes,
                    vertices, uv0, uv2, colors, lookup);
                int outputB = AddOutputVertex(
                    face.b, face.sourceB, positions, attributes,
                    vertices, uv0, uv2, colors, lookup);
                int outputC = AddOutputVertex(
                    face.c, face.sourceC, positions, attributes,
                    vertices, uv0, uv2, colors, lookup);
                if (outputA == outputB || outputB == outputC || outputC == outputA)
                    continue;
                int submesh = Mathf.Clamp(face.submesh, 0, submeshCount - 1);
                triangles[submesh].Add(outputA);
                triangles[submesh].Add(outputB);
                triangles[submesh].Add(outputC);
            }

            if (vertices.Count < 4)
                return null;

            Mesh mesh = new Mesh
            {
                name = source.name + " (Boolean Prepared)",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(2, uv2);
            mesh.SetColors(colors);
            mesh.subMeshCount = submeshCount;
            for (int submesh = 0; submesh < submeshCount; submesh++)
                mesh.SetTriangles(triangles[submesh], submesh, false);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int AddOutputVertex(
            int position,
            int source,
            List<Vector3> positions,
            SourceAttributes attributes,
            List<Vector3> vertices,
            List<Vector2> uv0,
            List<Vector2> uv2,
            List<Color> colors,
            Dictionary<OutputVertexKey, int> lookup)
        {
            OutputVertexKey key = new OutputVertexKey(position, source);
            int output;
            if (lookup.TryGetValue(key, out output))
                return output;

            output = vertices.Count;
            lookup.Add(key, output);
            vertices.Add(positions[position]);
            uv0.Add(attributes.GetUV0(source));
            uv2.Add(attributes.GetUV2(source));
            colors.Add(attributes.GetColor(source));
            return output;
        }

        private static WeldResult WeldPositions(Vector3[] source, float tolerance)
        {
            int count = source.Length;
            int[] parent = new int[count];
            int[] rank = new int[count];
            Dictionary<GridKey, List<int>> buckets = new Dictionary<GridKey, List<int>>();
            float toleranceSquared = tolerance * tolerance;
            for (int index = 0; index < count; index++)
            {
                parent[index] = index;
                Vector3 position = source[index];
                GridKey cell = new GridKey(position, tolerance);
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            List<int> candidates;
                            if (!buckets.TryGetValue(cell.Offset(x, y, z), out candidates))
                                continue;
                            for (int candidateIndex = 0;
                                 candidateIndex < candidates.Count;
                                 candidateIndex++)
                            {
                                int candidate = candidates[candidateIndex];
                                if ((source[candidate] - position).sqrMagnitude <= toleranceSquared)
                                    Union(parent, rank, index, candidate);
                            }
                        }
                    }
                }

                List<int> bucket;
                if (!buckets.TryGetValue(cell, out bucket))
                {
                    bucket = new List<int>();
                    buckets.Add(cell, bucket);
                }
                bucket.Add(index);
            }

            Dictionary<int, int> rootToPosition = new Dictionary<int, int>();
            List<Vector3> sums = new List<Vector3>();
            List<int> groupCounts = new List<int>();
            int[] vertexToPosition = new int[count];
            for (int index = 0; index < count; index++)
            {
                int root = Find(parent, index);
                int position;
                if (!rootToPosition.TryGetValue(root, out position))
                {
                    position = sums.Count;
                    rootToPosition.Add(root, position);
                    sums.Add(Vector3.zero);
                    groupCounts.Add(0);
                }

                sums[position] += source[index];
                groupCounts[position]++;
                vertexToPosition[index] = position;
            }

            List<Vector3> positions = new List<Vector3>(sums.Count);
            for (int i = 0; i < sums.Count; i++)
                positions.Add(sums[i] / groupCounts[i]);

            int moved = 0;
            float movementThreshold = Mathf.Max(MinimumTolerance * 0.01f, tolerance * 0.0001f);
            float movementThresholdSquared = movementThreshold * movementThreshold;
            for (int index = 0; index < count; index++)
            {
                if ((source[index] - positions[vertexToPosition[index]]).sqrMagnitude >
                    movementThresholdSquared)
                {
                    moved++;
                }
            }

            return new WeldResult(positions, vertexToPosition, moved);
        }

        private static Dictionary<EdgeKey, List<EdgeUse>> BuildEdgeUses(
            List<HealFace> faces,
            List<int> subset)
        {
            Dictionary<EdgeKey, List<EdgeUse>> uses =
                new Dictionary<EdgeKey, List<EdgeUse>>();
            if (subset == null)
            {
                for (int face = 0; face < faces.Count; face++)
                    AddFaceEdges(uses, faces[face], face);
            }
            else
            {
                for (int i = 0; i < subset.Count; i++)
                {
                    int face = subset[i];
                    AddFaceEdges(uses, faces[face], face);
                }
            }

            return uses;
        }

        private static void AddFaceEdges(
            Dictionary<EdgeKey, List<EdgeUse>> uses,
            HealFace face,
            int faceIndex)
        {
            AddEdgeUse(uses, faceIndex, 0, face.a, face.b);
            AddEdgeUse(uses, faceIndex, 1, face.b, face.c);
            AddEdgeUse(uses, faceIndex, 2, face.c, face.a);
        }

        private static void AddEdgeUse(
            Dictionary<EdgeKey, List<EdgeUse>> uses,
            int face,
            int edge,
            int a,
            int b)
        {
            EdgeKey key = new EdgeKey(a, b);
            List<EdgeUse> list;
            if (!uses.TryGetValue(key, out list))
            {
                list = new List<EdgeUse>(2);
                uses.Add(key, list);
            }
            list.Add(new EdgeUse(face, edge, a < b));
        }

        private static bool HasClosedOrientedTopology(List<HealFace> faces)
        {
            Dictionary<EdgeKey, List<EdgeUse>> uses = BuildEdgeUses(faces, null);
            foreach (KeyValuePair<EdgeKey, List<EdgeUse>> pair in uses)
            {
                if (pair.Value.Count != 2 ||
                    pair.Value[0].forward == pair.Value[1].forward)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsNearlyPlanar(
            List<HealFace> faces,
            List<int> component,
            List<Vector3> positions,
            float tolerance)
        {
            HashSet<int> ids = new HashSet<int>();
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = faces[component[i]];
                ids.Add(face.a);
                ids.Add(face.b);
                ids.Add(face.c);
                normal += Vector3.Cross(
                    positions[face.b] - positions[face.a],
                    positions[face.c] - positions[face.a]);
            }

            if (normal.sqrMagnitude <= tolerance * tolerance)
                return true;
            normal.Normalize();
            int first = -1;
            foreach (int id in ids)
            {
                first = id;
                break;
            }
            if (first < 0)
                return true;

            Bounds bounds = BoundsForPositions(ids, positions);
            float allowed = Mathf.Max(tolerance * 2f, bounds.size.magnitude * 0.000001f);
            Vector3 origin = positions[first];
            foreach (int id in ids)
            {
                if (Mathf.Abs(Vector3.Dot(positions[id] - origin, normal)) > allowed)
                    return false;
            }
            return true;
        }

        private static float SignedVolume(
            List<HealFace> faces,
            List<int> component,
            List<Vector3> positions)
        {
            double volume = 0.0;
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = faces[component[i]];
                Vector3 a = positions[face.a];
                Vector3 b = positions[face.b];
                Vector3 c = positions[face.c];
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            return (float)volume;
        }

        private static float VolumeThreshold(
            List<HealFace> faces,
            List<int> component,
            List<Vector3> positions,
            float tolerance)
        {
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < component.Count; i++)
            {
                HealFace face = faces[component[i]];
                ids.Add(face.a);
                ids.Add(face.b);
                ids.Add(face.c);
            }
            Bounds bounds = BoundsForPositions(ids, positions);
            float diagonal = Mathf.Max(bounds.size.magnitude, tolerance);
            return Mathf.Max(
                tolerance * tolerance * tolerance * 4f,
                diagonal * diagonal * diagonal * 0.000000001f);
        }

        private static Bounds BoundsForPositions(
            IEnumerable<int> ids,
            List<Vector3> positions)
        {
            bool initialized = false;
            Bounds bounds = default;
            foreach (int id in ids)
            {
                if (!initialized)
                {
                    bounds = new Bounds(positions[id], Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(positions[id]);
                }
            }
            return bounds;
        }

        private static void FlipComponent(
            List<HealFace> faces,
            List<int> component,
            ref RealBlendMeshOperandHealReport report)
        {
            for (int i = 0; i < component.Count; i++)
            {
                faces[component[i]].Flip();
                report.flippedTriangles++;
            }
        }

        private static void AppendComponentFaces(
            List<HealFace> output,
            List<HealFace> faces,
            List<int> component)
        {
            for (int i = 0; i < component.Count; i++)
                output.Add(faces[component[i]]);
        }

        private static List<int> SequentialIndices(int start, int count)
        {
            List<int> indices = new List<int>(count);
            for (int i = 0; i < count; i++)
                indices.Add(start + i);
            return indices;
        }

        private static Vector3 FaceNormal(HealFace face, List<Vector3> positions)
        {
            Vector3 normal = Vector3.Cross(
                positions[face.b] - positions[face.a],
                positions[face.c] - positions[face.a]);
            return normal.sqrMagnitude > 0.000000000001f
                ? normal.normalized
                : Vector3.zero;
        }

        private static int DominantAxis(Vector3 normal)
        {
            Vector3 absolute = new Vector3(
                Mathf.Abs(normal.x),
                Mathf.Abs(normal.y),
                Mathf.Abs(normal.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
                return 0;
            return absolute.y >= absolute.z ? 1 : 2;
        }

        private static Vector2 Project(Vector3 point, int dropAxis)
        {
            if (dropAxis == 0)
                return new Vector2(point.y, point.z);
            if (dropAxis == 1)
                return new Vector2(point.x, point.z);
            return new Vector2(point.x, point.y);
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool PointInTriangle(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            float orientation,
            float tolerance)
        {
            float ab = Cross2D(b - a, point - a) * orientation;
            float bc = Cross2D(c - b, point - b) * orientation;
            float ca = Cross2D(a - c, point - c) * orientation;
            return ab >= -tolerance && bc >= -tolerance && ca >= -tolerance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                     float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                     float.IsNaN(value.z) || float.IsInfinity(value.z));
        }

        private static string BuildDiagnostic(RealBlendMeshOperandHealReport report)
        {
            StringBuilder builder = new StringBuilder("Automatically prepared Boolean solid");
            List<string> details = new List<string>();
            if (report.weldedCrackVertices > 0)
                details.Add(report.weldedCrackVertices + " crack vertices welded");
            if (report.removedDegenerateTriangles > 0)
                details.Add(report.removedDegenerateTriangles + " degenerate faces removed");
            if (report.removedDuplicateTriangles > 0)
                details.Add(report.removedDuplicateTriangles + " duplicate faces removed");
            if (report.removedNonManifoldTriangles > 0)
                details.Add(report.removedNonManifoldTriangles + " edge-fan faces resolved");
            if (report.flippedTriangles > 0)
                details.Add(report.flippedTriangles + " faces reoriented");
            if (report.splitBoundaryEdges > 0)
                details.Add(report.splitBoundaryEdges + " collinear seam edges repartitioned");
            if (report.cappedBoundaryLoops > 0)
                details.Add(report.cappedBoundaryLoops + " openings capped");
            if (report.solidifiedOpenSurface)
                details.Add("open surface solidified");
            if (details.Count > 0)
                builder.Append(": ").Append(string.Join(", ", details.ToArray()));
            builder.Append('.');
            return builder.ToString();
        }

        private static int Find(int[] parent, int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        private static void Union(int[] parent, int[] rank, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA == rootB)
                return;
            if (rank[rootA] < rank[rootB])
                parent[rootA] = rootB;
            else if (rank[rootA] > rank[rootB])
                parent[rootB] = rootA;
            else
            {
                parent[rootB] = rootA;
                rank[rootA]++;
            }
        }

        private sealed class HealFace
        {
            public int a;
            public int b;
            public int c;
            public int sourceA;
            public int sourceB;
            public int sourceC;
            public readonly int submesh;

            public HealFace(
                int a,
                int b,
                int c,
                int sourceA,
                int sourceB,
                int sourceC,
                int submesh)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                this.sourceA = sourceA;
                this.sourceB = sourceB;
                this.sourceC = sourceC;
                this.submesh = submesh;
            }

            public void Flip()
            {
                int position = b;
                b = c;
                c = position;
                int source = sourceB;
                sourceB = sourceC;
                sourceC = source;
            }

            public void GetDirectedEdge(int edge, out int start, out int end)
            {
                if (edge == 0)
                {
                    start = a;
                    end = b;
                }
                else if (edge == 1)
                {
                    start = b;
                    end = c;
                }
                else
                {
                    start = c;
                    end = a;
                }
            }
        }

        private sealed class SourceAttributes
        {
            private int[] _representatives;
            private int _sourceCount;
            private readonly List<Vector2> _generatedUv0 =
                new List<Vector2>();
            private readonly List<Vector2> _generatedUv2 =
                new List<Vector2>();
            private readonly List<Color> _generatedColors =
                new List<Color>();
            public Vector2[] uv0;
            public Vector2[] uv2;
            public Color[] colors;

            public void SetSourceCount(int sourceCount)
            {
                _sourceCount = Mathf.Max(0, sourceCount);
            }

            public void SetRepresentatives(int[] vertexToPosition, int positionCount)
            {
                _representatives = new int[positionCount];
                for (int i = 0; i < _representatives.Length; i++)
                    _representatives[i] = -1;
                for (int source = 0; source < vertexToPosition.Length; source++)
                {
                    int position = vertexToPosition[source];
                    if (_representatives[position] < 0)
                        _representatives[position] = source;
                }
            }

            public int RepresentativeSource(int position)
            {
                return _representatives != null &&
                       position >= 0 && position < _representatives.Length
                    ? _representatives[position]
                    : -1;
            }

            public Vector2 GetUV0(int source)
            {
                if (uv0 != null && source >= 0 && source < uv0.Length)
                    return uv0[source];
                int generated = source - _sourceCount;
                return generated >= 0 && generated < _generatedUv0.Count
                    ? _generatedUv0[generated]
                    : Vector2.zero;
            }

            public Vector2 GetUV2(int source)
            {
                if (uv2 != null && source >= 0 && source < uv2.Length)
                    return uv2[source];
                int generated = source - _sourceCount;
                return generated >= 0 && generated < _generatedUv2.Count
                    ? _generatedUv2[generated]
                    : Vector2.zero;
            }

            public Color GetColor(int source)
            {
                if (colors != null && source >= 0 && source < colors.Length)
                    return colors[source];
                int generated = source - _sourceCount;
                return generated >= 0 && generated < _generatedColors.Count
                    ? _generatedColors[generated]
                    : Color.white;
            }

            public int InterpolateSource(int sourceA, int sourceB, float amount)
            {
                float t = Mathf.Clamp01(amount);
                int source = _sourceCount + _generatedUv0.Count;
                _generatedUv0.Add(Vector2.LerpUnclamped(
                    GetUV0(sourceA),
                    GetUV0(sourceB),
                    t));
                _generatedUv2.Add(Vector2.LerpUnclamped(
                    GetUV2(sourceA),
                    GetUV2(sourceB),
                    t));
                _generatedColors.Add(Color.LerpUnclamped(
                    GetColor(sourceA),
                    GetColor(sourceB),
                    t));
                return source;
            }
        }

        private readonly struct WeldResult
        {
            public readonly List<Vector3> positions;
            public readonly int[] vertexToPosition;
            public readonly int movedVertexCount;

            public WeldResult(
                List<Vector3> positions,
                int[] vertexToPosition,
                int movedVertexCount)
            {
                this.positions = positions;
                this.vertexToPosition = vertexToPosition;
                this.movedVertexCount = movedVertexCount;
            }
        }

        private readonly struct EdgeUse
        {
            public readonly int face;
            public readonly int edge;
            public readonly bool forward;

            public EdgeUse(int face, int edge, bool forward)
            {
                this.face = face;
                this.edge = edge;
                this.forward = forward;
            }
        }

        private readonly struct OrientationLink
        {
            public readonly int face;
            public readonly bool needsOppositeFlip;

            public OrientationLink(int face, bool needsOppositeFlip)
            {
                this.face = face;
                this.needsOppositeFlip = needsOppositeFlip;
            }
        }

        private readonly struct BoundaryEdge
        {
            public readonly int a;
            public readonly int b;
            public readonly int face;
            public readonly int submesh;

            public BoundaryEdge(int a, int b, int face, int submesh)
            {
                this.a = a;
                this.b = b;
                this.face = face;
                this.submesh = submesh;
            }
        }

        private sealed class BoundarySplitPlan
        {
            public readonly int start;
            public readonly int end;
            public readonly int opposite;
            public readonly int sourceStart;
            public readonly int sourceEnd;
            public readonly int sourceOpposite;
            public readonly int submesh;
            public readonly List<BoundarySplitPoint> points;

            public BoundarySplitPlan(
                int start,
                int end,
                int opposite,
                int sourceStart,
                int sourceEnd,
                int sourceOpposite,
                int submesh,
                List<BoundarySplitPoint> points)
            {
                this.start = start;
                this.end = end;
                this.opposite = opposite;
                this.sourceStart = sourceStart;
                this.sourceEnd = sourceEnd;
                this.sourceOpposite = sourceOpposite;
                this.submesh = submesh;
                this.points = points;
            }
        }

        private readonly struct BoundarySplitPoint
        {
            public readonly int position;
            public readonly float amount;

            public BoundarySplitPoint(int position, float amount)
            {
                this.position = position;
                this.amount = amount;
            }
        }

        private readonly struct IntTriangle
        {
            public readonly int a;
            public readonly int b;
            public readonly int c;

            public IntTriangle(int a, int b, int c)
            {
                this.a = a;
                this.b = b;
                this.c = c;
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
                return obj is EdgeKey && Equals((EdgeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_a * 397) ^ _b;
                }
            }
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int _a;
            private readonly int _b;
            private readonly int _c;

            public TriangleKey(int a, int b, int c)
            {
                if (a > b)
                {
                    int swap = a;
                    a = b;
                    b = swap;
                }
                if (b > c)
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }
                if (a > b)
                {
                    int swap = a;
                    a = b;
                    b = swap;
                }
                _a = a;
                _b = b;
                _c = c;
            }

            public bool Equals(TriangleKey other)
            {
                return _a == other._a && _b == other._b && _c == other._c;
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleKey && Equals((TriangleKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _a;
                    hash = (hash * 397) ^ _b;
                    return (hash * 397) ^ _c;
                }
            }
        }

        private readonly struct OutputVertexKey : IEquatable<OutputVertexKey>
        {
            private readonly int _position;
            private readonly int _source;

            public OutputVertexKey(int position, int source)
            {
                _position = position;
                _source = source;
            }

            public bool Equals(OutputVertexKey other)
            {
                return _position == other._position && _source == other._source;
            }

            public override bool Equals(object obj)
            {
                return obj is OutputVertexKey && Equals((OutputVertexKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_position * 397) ^ _source;
                }
            }
        }

        private readonly struct GridKey : IEquatable<GridKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            public GridKey(Vector3 position, float cellSize)
            {
                _x = Mathf.FloorToInt(position.x / cellSize);
                _y = Mathf.FloorToInt(position.y / cellSize);
                _z = Mathf.FloorToInt(position.z / cellSize);
            }

            private GridKey(int x, int y, int z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            public GridKey Offset(int x, int y, int z)
            {
                return new GridKey(_x + x, _y + y, _z + z);
            }

            public bool Equals(GridKey other)
            {
                return _x == other._x && _y == other._y && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is GridKey && Equals((GridKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x;
                    hash = (hash * 397) ^ _y;
                    return (hash * 397) ^ _z;
                }
            }
        }
    }
}
