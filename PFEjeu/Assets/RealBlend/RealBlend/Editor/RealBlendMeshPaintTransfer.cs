using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealBlend
{
    public sealed class PaintTransferSettings
    {
        public float PositionTolerance = 0.0001f;
        public bool ProjectNewVertices;
        public float ProjectionDistance = 0.05f;
        public float MaximumNormalAngle = 60f;
    }

    public sealed class PaintTransferReport
    {
        public Color[] Colors;
        public int ExactMatches;
        public int ProjectedMatches;
        public int UnmatchedVertices;
        // Ambiguities are included in UnmatchedVertices; their fallback colors are retained.
        public int AmbiguousVertices;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>Transfers local-space vertex colors without modifying either mesh.</summary>
    public static class RealBlendMeshPaintTransfer
    {
        private const double UvToleranceSquared = 1e-10;
        private const double NormalIdentityCosine = 0.9998476951563913; // One degree.

        public static PaintTransferReport Transfer(Mesh source, Mesh destination, PaintTransferSettings settings)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            settings = settings ?? new PaintTransferSettings();
            ValidateSettings(settings);
            MeshData from = ReadMesh(source, "Source");
            MeshData to = ReadMesh(destination, "Destination");
            if (from.Vertices.Length == 0 || from.Colors.Length != from.Vertices.Length)
                throw new ArgumentException("The source mesh must have a committed color for every vertex.", nameof(source));
            for (int i = 0; i < from.Colors.Length; i++)
                if (!IsFinite(from.Colors[i]))
                    throw new ArgumentException("The source mesh contains a non-finite vertex color.", nameof(source));

            var report = new PaintTransferReport { Colors = new Color[to.Vertices.Length] };
            bool validFallback = to.Colors.Length == to.Vertices.Length;
            for (int i = 0; validFallback && i < to.Colors.Length; i++) validFallback = IsFinite(to.Colors[i]);
            if (validFallback) Array.Copy(to.Colors, report.Colors, to.Colors.Length);
            // A new Color[] is transparent black, RealBlend's unpainted Base color.
            double toleranceSquared = (double)settings.PositionTolerance * settings.PositionTolerance;
            var grid = BuildPositionGrid(from.Vertices, settings.PositionTolerance);
            var nearby = new List<int>();
            var visitedCells = new HashSet<Cell>();
            TriangleBvh bvh = null;
            if (settings.ProjectNewVertices)
            {
                for (int submesh = 0; submesh < from.Topologies.Length; submesh++)
                    if (from.Topologies[submesh] != MeshTopology.Triangles)
                        throw new ArgumentException("Surface projection requires triangle topology in every source submesh.", nameof(source));
                bvh = new TriangleBvh(from, report.Warnings);
            }

            var projected = new List<ProjectionCandidate>();
            var traversal = new Stack<int>();
            double projectionSquared = (double)settings.ProjectionDistance * settings.ProjectionDistance;
            double normalCosine = Math.Cos(settings.MaximumNormalAngle * Math.PI / 180.0);
            int missingOrientation = 0;
            for (int vertex = 0; vertex < to.Vertices.Length; vertex++)
            {
                FindNearestVertices(from.Vertices, to.Vertices[vertex], grid, settings.PositionTolerance,
                    toleranceSquared, nearby, visitedCells);
                if (nearby.Count > 0)
                {
                    if (TryResolveExact(from, to, vertex, nearby, out Color color))
                    {
                        report.Colors[vertex] = color;
                        report.ExactMatches++;
                    }
                    else
                    {
                        report.UnmatchedVertices++;
                        report.AmbiguousVertices++;
                    }
                    // A geometric coincidence with conflicting paint must never be
                    // replaced by a projection onto one arbitrarily selected face.
                    continue;
                }

                if (bvh != null && bvh.HasTriangles)
                {
                    if (!to.NormalValid[vertex]) missingOrientation++;
                    else
                    {
                        bvh.FindNearest(new D3(to.Vertices[vertex]), to.Normals[vertex], normalCosine,
                            projectionSquared, toleranceSquared, projected, traversal);
                        if (projected.Count > 0)
                        {
                            if (TryResolveProjection(from, to, vertex, projected, out Color color))
                            {
                                report.Colors[vertex] = color;
                                report.ProjectedMatches++;
                                continue;
                            }
                            report.AmbiguousVertices++;
                        }
                    }
                }
                report.UnmatchedVertices++;
            }
            if (missingOrientation > 0)
                report.Warnings.Add($"{missingOrientation} unmatched vertices have no usable normal or triangle-derived orientation; surface projection was skipped for them.");
            if (report.AmbiguousVertices > 0)
                report.Warnings.Add($"{report.AmbiguousVertices} vertices have conflicting paint matches. Their existing destination colors, or unpainted Base colors, were retained.");
            return report;
        }

        private static void ValidateSettings(PaintTransferSettings settings)
        {
            if (!IsFinite(settings.PositionTolerance) || settings.PositionTolerance <= 0f)
                throw new ArgumentException("Position tolerance must be finite and greater than zero.", nameof(settings));
            if (!IsFinite(settings.ProjectionDistance) || settings.ProjectionDistance < 0f)
                throw new ArgumentException("Projection distance must be finite and non-negative.", nameof(settings));
            if (!IsFinite(settings.MaximumNormalAngle) || settings.MaximumNormalAngle < 0f || settings.MaximumNormalAngle > 180f)
                throw new ArgumentException("Maximum normal angle must be between 0 and 180 degrees.", nameof(settings));
        }

        private sealed class MeshData
        {
            public Vector3[] Vertices;
            public Color[] Colors;
            public Vector2[] Uvs;
            public Vector3[] Normals;
            public bool[] NormalValid;
            public int[][] Indices;
            public MeshTopology[] Topologies;
            public int[] MembershipOffsets;
            public int[] Memberships;

            public bool HasUv(int vertex) => Uvs.Length == Vertices.Length && IsFinite(Uvs[vertex]);
            public int MembershipCount(int vertex) => MembershipOffsets[vertex + 1] - MembershipOffsets[vertex];
            public bool BelongsTo(int vertex, int submesh)
            {
                for (int i = MembershipOffsets[vertex]; i < MembershipOffsets[vertex + 1]; i++)
                    if (Memberships[i] == submesh) return true;
                return false;
            }
        }

        private static MeshData ReadMesh(Mesh mesh, string label)
        {
            if (!mesh.isReadable) throw new ArgumentException(label + " mesh must have Read/Write enabled.");
            var data = new MeshData
            {
                Vertices = mesh.vertices, Colors = mesh.colors, Uvs = mesh.uv,
                Indices = new int[mesh.subMeshCount][], Topologies = new MeshTopology[mesh.subMeshCount]
            };
            int count = data.Vertices.Length;
            for (int i = 0; i < count; i++)
                if (!IsFinite(data.Vertices[i])) throw new ArgumentException(label + " mesh contains a non-finite vertex position.");
            var counts = new int[count];
            var lastSubmesh = new int[count];
            for (int i = 0; i < count; i++) lastSubmesh[i] = -1;
            var derivedNormals = new D3[count];
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetIndices(submesh);
                data.Indices[submesh] = indices;
                data.Topologies[submesh] = mesh.GetTopology(submesh);
                foreach (int index in indices)
                {
                    if (index < 0 || index >= count) throw new ArgumentException(label + " mesh contains an invalid vertex index.");
                    if (lastSubmesh[index] == submesh) continue;
                    lastSubmesh[index] = submesh;
                    counts[index]++;
                }
                if (data.Topologies[submesh] != MeshTopology.Triangles) continue;
                if (indices.Length % 3 != 0) throw new ArgumentException(label + " mesh has an incomplete triangle index list.");
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    D3 cross = D3.Cross(new D3(data.Vertices[b]) - new D3(data.Vertices[a]),
                        new D3(data.Vertices[c]) - new D3(data.Vertices[a]));
                    derivedNormals[a] += cross;
                    derivedNormals[b] += cross;
                    derivedNormals[c] += cross;
                }
            }
            data.MembershipOffsets = new int[count + 1];
            for (int i = 0; i < count; i++) data.MembershipOffsets[i + 1] = checked(data.MembershipOffsets[i] + counts[i]);
            data.Memberships = new int[data.MembershipOffsets[count]];
            Array.Clear(counts, 0, count);
            for (int i = 0; i < count; i++) lastSubmesh[i] = -1;
            for (int submesh = 0; submesh < data.Indices.Length; submesh++)
                foreach (int index in data.Indices[submesh])
                {
                    if (lastSubmesh[index] == submesh) continue;
                    lastSubmesh[index] = submesh;
                    data.Memberships[data.MembershipOffsets[index] + counts[index]++] = submesh;
                }
            Vector3[] suppliedNormals = mesh.normals;
            data.Normals = new Vector3[count];
            data.NormalValid = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 normal = default;
                bool supplied = suppliedNormals.Length == count && IsFinite(suppliedNormals[i]) &&
                                new D3(suppliedNormals[i]).TryNormalize(out normal);
                if (supplied) data.Normals[i] = normal;
                else if (derivedNormals[i].TryNormalize(out normal)) data.Normals[i] = normal;
                else continue;
                data.NormalValid[i] = true;
            }
            return data;
        }

        private readonly struct Cell : IEquatable<Cell>
        {
            private readonly double x, y, z;
            public Cell(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public static Cell At(Vector3 p, double size) => new Cell(Math.Floor(p.x / size), Math.Floor(p.y / size), Math.Floor(p.z / size));
            public Cell Offset(int dx, int dy, int dz) => new Cell(x + dx, y + dy, z + dz);
            public bool Equals(Cell other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is Cell other && Equals(other);
            public override int GetHashCode() { unchecked { return (x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode(); } }
        }

        private static Dictionary<Cell, List<int>> BuildPositionGrid(Vector3[] vertices, double size)
        {
            var grid = new Dictionary<Cell, List<int>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Cell key = Cell.At(vertices[i], size);
                if (!grid.TryGetValue(key, out List<int> bucket)) grid.Add(key, bucket = new List<int>());
                bucket.Add(i);
            }
            return grid;
        }

        private static void FindNearestVertices(Vector3[] vertices, Vector3 point,
            Dictionary<Cell, List<int>> grid, double size, double toleranceSquared, List<int> result, HashSet<Cell> visited)
        {
            result.Clear(); visited.Clear();
            Cell center = Cell.At(point, size);
            double nearest = double.PositiveInfinity;
            double tie = toleranceSquared * 1e-8;
            D3 p = new D3(point);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                Cell cell = center.Offset(x, y, z);
                if (!visited.Add(cell) || !grid.TryGetValue(cell, out List<int> bucket)) continue;
                foreach (int candidate in bucket)
                {
                    double distance = (new D3(vertices[candidate]) - p).LengthSquared;
                    if (distance > toleranceSquared) continue;
                    if (distance < nearest)
                    {
                        if (distance < nearest - tie) result.Clear();
                        nearest = distance;
                    }
                    else if (distance > nearest + tie) continue;
                    result.Add(candidate);
                }
            }
            // Keep the tie group relative to the actual nearest point, independent
            // of source vertex order when several distances differ by tiny amounts.
            for (int i = result.Count - 1; i >= 0; i--)
                if ((new D3(vertices[result[i]]) - p).LengthSquared > nearest + tie) result.RemoveAt(i);
        }

        private static bool TryResolveExact(MeshData source, MeshData destination, int vertex, List<int> candidates, out Color color)
        {
            color = source.Colors[candidates[0]];
            if (SameColors(source, candidates)) return true;
            var remaining = new List<int>(candidates);
            if (destination.HasUv(vertex) && candidates.TrueForAll(i => source.HasUv(i)))
                ApplyEvidence(candidates, remaining, i => UvDistanceSquared(source.Uvs[i], destination.Uvs[vertex]) <= UvToleranceSquared);
            if (destination.NormalValid[vertex] && candidates.TrueForAll(i => source.NormalValid[i]))
                ApplyEvidence(candidates, remaining, i => Dot(source.Normals[i], destination.Normals[vertex]) >= NormalIdentityCosine);
            if (destination.MembershipCount(vertex) > 0 && candidates.TrueForAll(i => source.MembershipCount(i) > 0))
                ApplyEvidence(candidates, remaining, i => SameMembership(source, i, destination, vertex));
            if (remaining.Count == 0 || !SameColors(source, remaining)) return false;
            color = source.Colors[remaining[0]];
            return true;
        }

        // Use evidence only if it matches at least one original candidate. Intersect
        // independent evidence; contradictory UV/material/normal matches stay ambiguous.
        private static void ApplyEvidence<T>(List<T> original, List<T> remaining, Predicate<T> matches)
        {
            if (original.Exists(matches)) remaining.RemoveAll(candidate => !matches(candidate));
        }

        private static bool SameColors(MeshData data, List<int> vertices)
        {
            Color color = data.Colors[vertices[0]];
            for (int i = 1; i < vertices.Count; i++) if (!EqualColor(color, data.Colors[vertices[i]])) return false;
            return true;
        }

        private static bool SameMembership(MeshData a, int ai, MeshData b, int bi)
        {
            int count = a.MembershipCount(ai);
            if (count != b.MembershipCount(bi)) return false;
            for (int i = 0; i < count; i++)
                if (a.Memberships[a.MembershipOffsets[ai] + i] != b.Memberships[b.MembershipOffsets[bi] + i]) return false;
            return true;
        }

        private struct ProjectionCandidate
        {
            public Color Color;
            public Vector2 Uv;
            public bool UvValid;
            public Vector3 Normal;
            public int Submesh;
            public double DistanceSquared;
        }

        private static bool TryResolveProjection(MeshData source, MeshData destination, int vertex,
            List<ProjectionCandidate> candidates, out Color color)
        {
            color = StableProjectedColor(candidates);
            if (SameProjectedColors(candidates)) return true;
            var remaining = new List<ProjectionCandidate>(candidates);
            if (destination.HasUv(vertex) && candidates.TrueForAll(candidate => candidate.UvValid))
                ApplyEvidence(candidates, remaining, candidate => UvDistanceSquared(candidate.Uv, destination.Uvs[vertex]) <= UvToleranceSquared);
            ApplyEvidence(candidates, remaining, candidate => Dot(candidate.Normal, destination.Normals[vertex]) >= NormalIdentityCosine);
            if (destination.MembershipCount(vertex) > 0)
                ApplyEvidence(candidates, remaining, candidate => destination.BelongsTo(vertex, candidate.Submesh));
            if (remaining.Count == 0 || !SameProjectedColors(remaining)) return false;
            color = StableProjectedColor(remaining);
            return true;
        }

        private static bool SameProjectedColors(List<ProjectionCandidate> candidates)
        {
            // Adjacent triangles can interpolate identical edge paint a few float
            // rounding steps apart. This tolerance applies only to projection;
            // exact vertex matches always compare and copy RGBA without tolerance.
            // Compare the full component range so equivalence does not depend on
            // which triangle happened to be visited first.
            for (int channel = 0; channel < 4; channel++)
            {
                double minimum = candidates[0].Color[channel], maximum = minimum;
                for (int i = 1; i < candidates.Count; i++)
                {
                    minimum = Math.Min(minimum, candidates[i].Color[channel]);
                    maximum = Math.Max(maximum, candidates[i].Color[channel]);
                }
                if (maximum - minimum > 1e-6 + 1e-6 * Math.Max(Math.Abs(minimum), Math.Abs(maximum))) return false;
            }
            return true;
        }

        private static Color StableProjectedColor(List<ProjectionCandidate> candidates)
        {
            ProjectionCandidate chosen = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                ProjectionCandidate candidate = candidates[i];
                if (candidate.DistanceSquared < chosen.DistanceSquared) chosen = candidate;
                else if (candidate.DistanceSquared == chosen.DistanceSquared)
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int comparison = candidate.Color[channel].CompareTo(chosen.Color[channel]);
                        if (comparison < 0) chosen = candidate;
                        if (comparison != 0) break;
                    }
                }
            }
            return chosen.Color;
        }

        private struct Triangle
        {
            public int A, B, C, Submesh, Order;
            public D3 Centroid;
            public Box Bounds;
            public Vector3 Normal;
        }

        private struct BvhNode
        {
            public Box Bounds;
            public int Left, Right, Start, Count;
        }

        private sealed class TriangleBvh
        {
            private readonly MeshData source;
            private readonly Triangle[] triangles;
            private readonly List<BvhNode> nodes = new List<BvhNode>();
            public bool HasTriangles => triangles.Length > 0;

            public TriangleBvh(MeshData source, List<string> warnings)
            {
                this.source = source;
                var list = new List<Triangle>();
                int skipped = 0;
                for (int submesh = 0; submesh < source.Indices.Length; submesh++)
                {
                    int[] indices = source.Indices[submesh];
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int ai = indices[i], bi = indices[i + 1], ci = indices[i + 2];
                        D3 a = new D3(source.Vertices[ai]), b = new D3(source.Vertices[bi]), c = new D3(source.Vertices[ci]);
                        D3 ab = b - a, ac = c - a;
                        D3 cross = D3.Cross(ab, ac);
                        double longest = Math.Max(ab.LengthSquared, Math.Max(ac.LengthSquared, (c - b).LengthSquared));
                        if (cross.LengthSquared <= longest * longest * 1e-24 || !cross.TryNormalize(out Vector3 normal))
                        { skipped++; continue; }
                        list.Add(new Triangle { A = ai, B = bi, C = ci, Submesh = submesh, Order = list.Count,
                            Centroid = (a + b + c) * (1.0 / 3.0), Bounds = new Box(a, b, c), Normal = normal });
                    }
                }
                triangles = list.ToArray();
                if (triangles.Length > 0) Build(0, triangles.Length);
                else warnings.Add("The source mesh has no usable triangles for surface projection.");
                if (skipped > 0) warnings.Add($"{skipped} degenerate or numerically unstable source triangles were excluded from surface projection.");
            }

            private int Build(int start, int count)
            {
                Box bounds = triangles[start].Bounds;
                Box centers = new Box(triangles[start].Centroid);
                for (int i = start + 1; i < start + count; i++)
                { bounds.Include(triangles[i].Bounds); centers.Include(triangles[i].Centroid); }
                int index = nodes.Count;
                nodes.Add(new BvhNode { Bounds = bounds, Start = start, Count = count, Left = -1, Right = -1 });
                if (count <= 8) return index;
                D3 size = centers.Max - centers.Min;
                int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                int middle = start + count / 2;
                SelectMedian(start, start + count - 1, middle, axis);
                int left = Build(start, middle - start);
                int right = Build(middle, start + count - middle);
                nodes[index] = new BvhNode { Bounds = bounds, Left = left, Right = right, Start = start, Count = 0 };
                return index;
            }

            private void SelectMedian(int left, int right, int target, int axis)
            {
                while (left < right)
                {
                    Triangle pivot = triangles[left + (right - left) / 2];
                    int i = left, j = right;
                    while (i <= j)
                    {
                        while (Compare(triangles[i], pivot, axis) < 0) i++;
                        while (Compare(triangles[j], pivot, axis) > 0) j--;
                        if (i > j) break;
                        Triangle swap = triangles[i]; triangles[i] = triangles[j]; triangles[j] = swap;
                        i++; j--;
                    }
                    if (target <= j) right = j;
                    else if (target >= i) left = i;
                    else return;
                }
            }

            private static int Compare(Triangle a, Triangle b, int axis)
            {
                int compare = a.Centroid.Axis(axis).CompareTo(b.Centroid.Axis(axis));
                return compare != 0 ? compare : a.Order.CompareTo(b.Order);
            }

            public void FindNearest(D3 point, Vector3 normal, double cosine, double maximumSquared,
                double positionToleranceSquared, List<ProjectionCandidate> result, Stack<int> stack)
            {
                result.Clear(); stack.Clear(); stack.Push(0);
                double closest = double.PositiveInfinity;
                double tie = Math.Max(positionToleranceSquared * 1e-8, maximumSquared * 1e-10);
                while (stack.Count > 0)
                {
                    BvhNode node = nodes[stack.Pop()];
                    if (node.Bounds.DistanceSquared(point) > Math.Min(maximumSquared, closest + tie)) continue;
                    if (node.Count == 0)
                    {
                        double left = nodes[node.Left].Bounds.DistanceSquared(point);
                        double right = nodes[node.Right].Bounds.DistanceSquared(point);
                        // Visit the nearer child first, tightening the distance bound early.
                        if (left < right) { stack.Push(node.Right); stack.Push(node.Left); }
                        else { stack.Push(node.Left); stack.Push(node.Right); }
                        continue;
                    }
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        Triangle triangle = triangles[i];
                        if (Dot(normal, triangle.Normal) + 1e-7 < cosine) continue;
                        D3 a = new D3(source.Vertices[triangle.A]), b = new D3(source.Vertices[triangle.B]), c = new D3(source.Vertices[triangle.C]);
                        D3 weights = ClosestWeights(point, a, b, c);
                        double distance = (a * weights.X + b * weights.Y + c * weights.Z - point).LengthSquared;
                        if (distance > maximumSquared) continue;
                        if (distance < closest)
                        {
                            if (distance < closest - tie) result.Clear();
                            closest = distance;
                        }
                        else if (distance > closest + tie) continue;
                        bool uvValid = source.HasUv(triangle.A) && source.HasUv(triangle.B) && source.HasUv(triangle.C);
                        Vector2 uv = default;
                        if (uvValid)
                        {
                            Vector2 au = source.Uvs[triangle.A], bu = source.Uvs[triangle.B], cu = source.Uvs[triangle.C];
                            uv = new Vector2((float)(au.x * weights.X + bu.x * weights.Y + cu.x * weights.Z),
                                (float)(au.y * weights.X + bu.y * weights.Y + cu.y * weights.Z));
                        }
                        result.Add(new ProjectionCandidate { Color = Interpolate(source.Colors[triangle.A], source.Colors[triangle.B],
                            source.Colors[triangle.C], weights), Uv = uv, UvValid = uvValid, Normal = triangle.Normal,
                            Submesh = triangle.Submesh, DistanceSquared = distance });
                    }
                }
                for (int i = result.Count - 1; i >= 0; i--)
                    if (result[i].DistanceSquared > closest + tie) result.RemoveAt(i);
            }
        }

        // Closest-point region tests return barycentric weights without relying on UVs.
        private static D3 ClosestWeights(D3 p, D3 a, D3 b, D3 c)
        {
            D3 ab = b - a, ac = c - a, ap = p - a;
            double d1 = D3.Dot(ab, ap), d2 = D3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return new D3(1, 0, 0);
            D3 bp = p - b;
            double d3 = D3.Dot(ab, bp), d4 = D3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return new D3(0, 1, 0);
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            { double v = d1 / (d1 - d3); return new D3(1 - v, v, 0); }
            D3 cp = p - c;
            double d5 = D3.Dot(ab, cp), d6 = D3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return new D3(0, 0, 1);
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            { double w = d2 / (d2 - d6); return new D3(1 - w, 0, w); }
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            { double w = (d4 - d3) / (d4 - d3 + d5 - d6); return new D3(0, 1 - w, w); }
            double inverse = 1.0 / (va + vb + vc);
            double bv = vb * inverse, bw = vc * inverse;
            return new D3(1 - bv - bw, bv, bw);
        }

        private struct Box
        {
            public D3 Min, Max;
            public Box(D3 point) { Min = Max = point; }
            public Box(D3 a, D3 b, D3 c) : this(a) { Include(b); Include(c); }
            public void Include(D3 p) { Min = D3.Min(Min, p); Max = D3.Max(Max, p); }
            public void Include(Box box) { Include(box.Min); Include(box.Max); }
            public double DistanceSquared(D3 p)
            {
                double x = Math.Max(0, Math.Max(Min.X - p.X, p.X - Max.X));
                double y = Math.Max(0, Math.Max(Min.Y - p.Y, p.Y - Max.Y));
                double z = Math.Max(0, Math.Max(Min.Z - p.Z, p.Z - Max.Z));
                return x * x + y * y + z * z;
            }
        }

        private readonly struct D3
        {
            public readonly double X, Y, Z;
            public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public D3(Vector3 v) : this(v.x, v.y, v.z) { }
            public double LengthSquared => X * X + Y * Y + Z * Z;
            public double Axis(int axis) => axis == 0 ? X : axis == 1 ? Y : Z;
            public bool TryNormalize(out Vector3 normal)
            {
                double length = Math.Sqrt(LengthSquared);
                if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length)) { normal = default; return false; }
                normal = new Vector3((float)(X / length), (float)(Y / length), (float)(Z / length));
                return true;
            }
            public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
            public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public static D3 Min(D3 a, D3 b) => new D3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            public static D3 Max(D3 a, D3 b) => new D3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        }

        private static Color Interpolate(Color a, Color b, Color c, D3 weights) => new Color(
            (float)(a.r * weights.X + b.r * weights.Y + c.r * weights.Z),
            (float)(a.g * weights.X + b.g * weights.Y + c.g * weights.Z),
            (float)(a.b * weights.X + b.b * weights.Y + c.b * weights.Z),
            (float)(a.a * weights.X + b.a * weights.Y + c.a * weights.Z));
        private static bool EqualColor(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        private static double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        private static double UvDistanceSquared(Vector2 a, Vector2 b) { double x = (double)a.x - b.x, y = (double)a.y - b.y; return x * x + y * y; }
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Color value) => IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);
    }
}
