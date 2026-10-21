using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Computes bone weights for a raw mesh from an already-built skeleton, using a simple
    /// "closest bone segments" heuristic: each vertex blends the TWO nearest bone segments
    /// (parent → child), weighted by inverse squared distance, up to 4 distinct bones total.
    /// Blending two segments (not just one) matters most at a joint where several bones meet
    /// (e.g. Hips, where Spine and both upper legs start): with a single nearest segment a vertex
    /// there could flip hard between bones for a tiny position change, showing as a crease: this is
    /// a deliberately simple envelope-style skin, not heat-diffusion weights, so results are still
    /// not as smooth as a dedicated DCC tool, but joints no longer hard-switch.
    /// </summary>
    internal static class AutoSkinner
    {
        private const float MinSegmentLengthSqr = 0.0001f;
        private const float FalloffEpsilonSqr = 0.0001f;

        // The loop itself is fast even at tens of thousands of vertices (a few million simple float
        // operations run in milliseconds) — this is not here to fix a real bottleneck. It exists so a
        // large mesh never LOOKS frozen with no feedback, and so it can be cancelled instead of waited
        // out if something else unexpected makes it slow (e.g. an editor already under load).
        private const int ProgressBarVertexThreshold = 2000;
        private const int ProgressBarUpdateInterval = 200;

        /// <summary>Accumulates up to 4 distinct bone influences (BoneWeight's own limit) without
        /// allocating, merging repeat contributions to the same bone (e.g. a joint bone referenced
        /// by both of the two nearest segments).</summary>
        private struct WeightAccumulator
        {
            private int _boneIndex0, _boneIndex1, _boneIndex2, _boneIndex3;
            private float _weight0, _weight1, _weight2, _weight3;
            private int _count;

            public void Add(int boneIndex, float weight)
            {
                if (weight <= 0f)
                {
                    return;
                }

                if (_count > 0 && _boneIndex0 == boneIndex) { _weight0 += weight; return; }
                if (_count > 1 && _boneIndex1 == boneIndex) { _weight1 += weight; return; }
                if (_count > 2 && _boneIndex2 == boneIndex) { _weight2 += weight; return; }
                if (_count > 3 && _boneIndex3 == boneIndex) { _weight3 += weight; return; }

                switch (_count)
                {
                    case 0: _boneIndex0 = boneIndex; _weight0 = weight; break;
                    case 1: _boneIndex1 = boneIndex; _weight1 = weight; break;
                    case 2: _boneIndex2 = boneIndex; _weight2 = weight; break;
                    case 3: _boneIndex3 = boneIndex; _weight3 = weight; break;
                    default: return; // More than 4 distinct bones should not happen (2 segments * 2 ends).
                }

                _count++;
            }

            public BoneWeight ToBoneWeight()
            {
                float sum = _weight0 + _weight1 + _weight2 + _weight3;
                if (sum <= 0f)
                {
                    return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                }

                float inverseSum = 1f / sum;
                return new BoneWeight
                {
                    boneIndex0 = _boneIndex0,
                    weight0 = _weight0 * inverseSum,
                    boneIndex1 = _boneIndex1,
                    weight1 = _weight1 * inverseSum,
                    boneIndex2 = _boneIndex2,
                    weight2 = _weight2 * inverseSum,
                    boneIndex3 = _boneIndex3,
                    weight3 = _weight3 * inverseSum,
                };
            }
        }

        private readonly struct Segment
        {
            public readonly int ParentIndex;
            public readonly int ChildIndex;
            public readonly Vector3 ParentWorldPosition;
            public readonly Vector3 ChildWorldPosition;

            public Segment(int parentIndex, int childIndex, Vector3 parentWorldPosition, Vector3 childWorldPosition)
            {
                ParentIndex = parentIndex;
                ChildIndex = childIndex;
                ParentWorldPosition = parentWorldPosition;
                ChildWorldPosition = childWorldPosition;
            }
        }

        /// <summary>
        /// Computes one BoneWeight per vertex. Throws on invalid input instead of failing deeper with
        /// a less obvious exception — defense in depth, since this is also the entry point a future
        /// batch tool (Phase 5c) will call directly, without going through the placement window's UI.
        /// </summary>
        public static BoneWeight[] ComputeBoneWeights(Mesh sourceMesh, Transform meshTransform, Transform[] bones, Dictionary<string, int> boneIndexByName)
        {
            if (sourceMesh == null)
            {
                throw new ArgumentNullException(nameof(sourceMesh));
            }

            if (meshTransform == null)
            {
                throw new ArgumentNullException(nameof(meshTransform));
            }

            if (bones == null || boneIndexByName == null)
            {
                throw new ArgumentNullException(nameof(bones));
            }

            List<Segment> segments = BuildSegments(bones, boneIndexByName);
            Vector3[] localVertices = sourceMesh.vertices;
            var weights = new BoneWeight[localVertices.Length];
            bool showProgress = localVertices.Length > ProgressBarVertexThreshold;

            try
            {
                for (int v = 0; v < localVertices.Length; v++)
                {
                    if (showProgress && (v % ProgressBarUpdateInterval) == 0)
                    {
                        bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                            "Auto Rig",
                            $"Calcul du skinning… ({v}/{localVertices.Length} sommets)",
                            v / (float)localVertices.Length);

                        if (cancelled)
                        {
                            throw new OperationCanceledException("Calcul du skinning annulé par l'utilisateur.");
                        }
                    }

                    Vector3 worldVertex = meshTransform.TransformPoint(localVertices[v]);
                    weights[v] = ComputeVertexWeight(worldVertex, segments);
                }
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            return weights;
        }

        private static List<Segment> BuildSegments(Transform[] bones, Dictionary<string, int> boneIndexByName)
        {
            var segments = new List<Segment>(BoneTemplate.Bones.Length);

            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (bone.ParentName == null || !boneIndexByName.TryGetValue(bone.Name, out int childIndex))
                {
                    continue;
                }

                string ancestorName = ResolveCreatedAncestor(bone.ParentName, boneIndexByName);
                if (ancestorName == null)
                {
                    continue;
                }

                int parentIndex = boneIndexByName[ancestorName];
                segments.Add(new Segment(parentIndex, childIndex, bones[parentIndex].position, bones[childIndex].position));
            }

            return segments;
        }

        /// <summary>Same "skip missing optional bone" walk as <see cref="SkeletonBuilder"/>, kept local on purpose: it reasons about indices here, not Transforms.</summary>
        private static string ResolveCreatedAncestor(string boneName, Dictionary<string, int> boneIndexByName)
        {
            string current = boneName;
            while (!string.IsNullOrEmpty(current))
            {
                if (boneIndexByName.ContainsKey(current))
                {
                    return current;
                }

                int index = BoneTemplate.IndexOf(current);
                current = index >= 0 ? BoneTemplate.Bones[index].ParentName : null;
            }

            return null;
        }

        private static BoneWeight ComputeVertexWeight(Vector3 worldVertex, List<Segment> segments)
        {
            if (segments.Count == 0)
            {
                // Should not happen: Hips+Spine always exist once at least one marker is placed.
                return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }

            int bestIndex = -1;
            int secondBestIndex = -1;
            float bestDistanceSqr = float.MaxValue;
            float secondDistanceSqr = float.MaxValue;
            float bestT = 0f;
            float secondT = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                float t = ClosestPointParameter(worldVertex, segment.ParentWorldPosition, segment.ChildWorldPosition);
                Vector3 closestPoint = Vector3.Lerp(segment.ParentWorldPosition, segment.ChildWorldPosition, t);
                float distanceSqr = (worldVertex - closestPoint).sqrMagnitude;

                if (distanceSqr < bestDistanceSqr)
                {
                    secondDistanceSqr = bestDistanceSqr;
                    secondBestIndex = bestIndex;
                    secondT = bestT;

                    bestDistanceSqr = distanceSqr;
                    bestIndex = i;
                    bestT = t;
                }
                else if (distanceSqr < secondDistanceSqr)
                {
                    secondDistanceSqr = distanceSqr;
                    secondBestIndex = i;
                    secondT = t;
                }
            }

            var accumulator = new WeightAccumulator();
            AddSegmentInfluence(ref accumulator, segments[bestIndex], bestT, bestDistanceSqr);
            if (secondBestIndex >= 0)
            {
                AddSegmentInfluence(ref accumulator, segments[secondBestIndex], secondT, secondDistanceSqr);
            }

            return accumulator.ToBoneWeight();
        }

        private static void AddSegmentInfluence(ref WeightAccumulator accumulator, Segment segment, float t, float distanceSqr)
        {
            float influence = 1f / (distanceSqr + FalloffEpsilonSqr);
            accumulator.Add(segment.ParentIndex, influence * (1f - t));
            accumulator.Add(segment.ChildIndex, influence * t);
        }

        private static float ClosestPointParameter(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector3 segment = segmentEnd - segmentStart;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < MinSegmentLengthSqr)
            {
                return 0f;
            }

            float t = Vector3.Dot(point - segmentStart, segment) / lengthSqr;
            return Mathf.Clamp01(t);
        }
    }
}
