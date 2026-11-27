using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VertexCloth
{
    public struct CapsuleData
    {
        public float3 a;
        public float3 b;
        public float radius;
    }

    public struct SimParams
    {
        public float deltaTime;
        public int substeps;
        public int iterations;
        public float3 gravity;
        public float damping;
        public float stretchStiffness;
        public float bendStiffness;
        public float shapeAtPins;
        public float shapeAtFree;
        public float maxTravel;      // monde
        public float restScale;      // local -> monde
        public float3 windVelocity;
        public float windInfluence;
        public float windTurbulence;
        public float turbulenceFrequency;
        public float time;
        public float collisionThickness;
        public float collisionFriction;
    }

    /// <summary>Cible monde de chaque particule pour un mesh statique (pose de repos suivant le transform).</summary>
    [BurstCompile]
    public struct ComputeTargetsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> restLocal;
        public float4x4 localToWorld;
        [WriteOnly] public NativeArray<float3> targets;

        public void Execute(int i)
        {
            targets[i] = math.transform(localToWorld, restLocal[i]);
        }
    }

    /// <summary>Cible monde de chaque particule pour un SkinnedMeshRenderer (vertices issus de BakeMesh).</summary>
    [BurstCompile]
    public struct ComputeSkinnedTargetsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> bakedVertices;
        [ReadOnly] public NativeArray<int> particleFirstVertex;
        public float4x4 bakedToWorld;
        [WriteOnly] public NativeArray<float3> targets;

        public void Execute(int i)
        {
            targets[i] = math.transform(bakedToWorld, bakedVertices[particleFirstVertex[i]]);
        }
    }

    /// <summary>Simulation PBD en espace monde : vent, étirement, flexion, retour à la forme, collisions.</summary>
    [BurstCompile]
    public struct ClothSimulationJob : IJob
    {
        public SimParams p;

        public NativeArray<float3> positions;
        public NativeArray<float3> previous;
        public NativeArray<float3> normals;

        [ReadOnly] public NativeArray<float> freedom;
        [ReadOnly] public NativeArray<float3> targetStart;
        [ReadOnly] public NativeArray<float3> targetEnd;
        [ReadOnly] public NativeArray<int2> stretchEdges;
        [ReadOnly] public NativeArray<float> stretchRest;
        [ReadOnly] public NativeArray<int2> bendPairs;
        [ReadOnly] public NativeArray<float> bendRest;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<CapsuleData> capsules;

        public void Execute()
        {
            float h = p.deltaTime / p.substeps;
            float dampFactor = math.exp(-p.damping * h);
            float shapeTimeScale = h * 60f;
            float stretchK = PerIterationStiffness(p.stretchStiffness, p.iterations);
            float bendK = PerIterationStiffness(p.bendStiffness, p.iterations);

            for (int s = 0; s < p.substeps; s++)
            {
                float t = (s + 1f) / p.substeps;

                ComputeNormals();
                Integrate(h, t, dampFactor);

                for (int it = 0; it < p.iterations; it++)
                {
                    SolveDistance(stretchEdges, stretchRest, stretchK);
                    SolveDistance(bendPairs, bendRest, bendK);
                }

                ApplyShapeAndTravel(t, shapeTimeScale);
                SolveCollisions();
            }
        }

        private static float PerIterationStiffness(float k, int iterations)
        {
            return 1f - math.pow(1f - math.saturate(k), 1f / iterations);
        }

        private void ComputeNormals()
        {
            for (int i = 0; i < normals.Length; i++)
                normals[i] = float3.zero;

            for (int i = 0; i < triangles.Length; i++)
            {
                int3 tri = triangles[i];
                float3 a = positions[tri.x];
                float3 n = math.cross(positions[tri.y] - a, positions[tri.z] - a);
                normals[tri.x] += n;
                normals[tri.y] += n;
                normals[tri.z] += n;
            }

            for (int i = 0; i < normals.Length; i++)
                normals[i] = math.normalizesafe(normals[i], new float3(0f, 1f, 0f));
        }

        private void Integrate(float h, float t, float dampFactor)
        {
            float windSpeed = math.length(p.windVelocity);

            for (int i = 0; i < positions.Length; i++)
            {
                float w = freedom[i];
                float3 target = math.lerp(targetStart[i], targetEnd[i], t);

                if (w <= 0f)
                {
                    positions[i] = target;
                    previous[i] = target;
                    continue;
                }

                float3 pos = positions[i];
                float3 vel = (pos - previous[i]) / h * dampFactor;

                float3 windVel = p.windVelocity;
                if (windSpeed > 1e-4f && p.windTurbulence > 0f)
                {
                    float3 q = pos * p.turbulenceFrequency + p.time * p.turbulenceFrequency * 0.7f;
                    float3 turb = new float3(
                        noise.snoise(q),
                        noise.snoise(q + 17.31f),
                        noise.snoise(q + 43.77f));
                    windVel += turb * (p.windTurbulence * windSpeed);
                }

                // Traînée aérodynamique : composante normale à la surface + légère composante tangentielle
                float3 rel = windVel - vel;
                float3 relNormal = normals[i] * math.dot(normals[i], rel);
                float3 relTangent = rel - relNormal;

                float3 accel = p.gravity + p.windInfluence * (relNormal + 0.15f * relTangent);
                vel += accel * h;

                previous[i] = pos;
                positions[i] = pos + vel * h;
            }
        }

        private void SolveDistance(NativeArray<int2> pairs, NativeArray<float> rest, float k)
        {
            if (k <= 0f)
                return;

            for (int e = 0; e < pairs.Length; e++)
            {
                int2 pair = pairs[e];
                float wi = freedom[pair.x];
                float wj = freedom[pair.y];
                float ws = wi + wj;
                if (ws <= 0f)
                    continue;

                float3 d = positions[pair.y] - positions[pair.x];
                float len = math.length(d);
                if (len < 1e-8f)
                    continue;

                float diff = (len - rest[e] * p.restScale) / len;
                float3 corr = d * (diff * k / ws);
                positions[pair.x] = positions[pair.x] + corr * wi;
                positions[pair.y] = positions[pair.y] - corr * wj;
            }
        }

        private void ApplyShapeAndTravel(float t, float shapeTimeScale)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float w = freedom[i];
                if (w <= 0f)
                    continue;

                float3 target = math.lerp(targetStart[i], targetEnd[i], t);
                float k = math.lerp(p.shapeAtPins, p.shapeAtFree, w);
                float kEff = 1f - math.pow(1f - math.saturate(k), shapeTimeScale);

                float3 pos = positions[i];
                pos += (target - pos) * kEff;

                float limit = p.maxTravel * w;
                float3 offset = pos - target;
                float dist = math.length(offset);
                if (dist > limit && dist > 1e-8f)
                    pos = target + offset * (limit / dist);

                positions[i] = pos;
            }
        }

        private void SolveCollisions()
        {
            if (capsules.Length == 0)
                return;

            for (int i = 0; i < positions.Length; i++)
            {
                if (freedom[i] <= 0f)
                    continue;

                float3 pos = positions[i];

                for (int c = 0; c < capsules.Length; c++)
                {
                    CapsuleData cap = capsules[c];
                    if (cap.radius <= 0f)
                        continue;

                    float3 ab = cap.b - cap.a;
                    float abLen2 = math.dot(ab, ab);
                    float tt = abLen2 > 1e-10f ? math.saturate(math.dot(pos - cap.a, ab) / abLen2) : 0f;
                    float3 closest = cap.a + ab * tt;

                    float3 delta = pos - closest;
                    float dist = math.length(delta);
                    float minDist = cap.radius + p.collisionThickness;
                    if (dist >= minDist || dist < 1e-6f)
                        continue;

                    float3 n = delta / dist;
                    pos = closest + n * minDist;

                    // Friction : réduit la vitesse tangentielle
                    float3 vel = pos - previous[i];
                    float3 velTangent = vel - n * math.dot(vel, n);
                    previous[i] = previous[i] + velTangent * p.collisionFriction;
                }

                positions[i] = pos;
            }
        }
    }

    /// <summary>Reconstruit vertices, normales (arêtes dures conservées) et bounds dans l'espace de sortie du mesh.</summary>
    [BurstCompile]
    public struct ClothWriteMeshJob : IJob
    {
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<int> vertexToParticle;
        [ReadOnly] public NativeArray<int> meshTriangles;
        public float4x4 worldToOutput;

        public NativeArray<float3> vertices;
        public NativeArray<float3> vertexNormals;
        public NativeArray<float3> bounds; // [0] = min, [1] = max

        public void Execute()
        {
            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);

            for (int v = 0; v < vertices.Length; v++)
            {
                float3 local = math.transform(worldToOutput, positions[vertexToParticle[v]]);
                vertices[v] = local;
                vertexNormals[v] = float3.zero;
                min = math.min(min, local);
                max = math.max(max, local);
            }

            for (int i = 0; i + 2 < meshTriangles.Length; i += 3)
            {
                int ia = meshTriangles[i];
                int ib = meshTriangles[i + 1];
                int ic = meshTriangles[i + 2];
                float3 a = vertices[ia];
                float3 n = math.cross(vertices[ib] - a, vertices[ic] - a);
                vertexNormals[ia] += n;
                vertexNormals[ib] += n;
                vertexNormals[ic] += n;
            }

            for (int v = 0; v < vertexNormals.Length; v++)
                vertexNormals[v] = math.normalizesafe(vertexNormals[v], new float3(0f, 1f, 0f));

            bounds[0] = min;
            bounds[1] = max;
        }
    }
}
