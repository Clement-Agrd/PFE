using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexCloth
{
    /// <summary>
    /// Simule un vêtement / une cape / des cheveux / un drapeau directement sur les vertices d'un mesh.
    /// À placer sur l'objet portant le MeshFilter ou le SkinnedMeshRenderer.
    /// Cycle : OnEnable (allocation) -> LateUpdate (jobs Burst) -> OnDisable (libération, rendu d'origine restauré).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class VertexClothSimulator : MonoBehaviour
    {
        #region Serialized
        [SerializeField] private VertexClothData data;
        [SerializeField] private VertexClothSettings settings;

        [Header("Vent")]
        [Tooltip("Optionnel : direction (forward), windMain et pulse sont lus (mode Directional).")]
        [SerializeField] private WindZone windZone;
        [Tooltip("Multiplicateur appliqué à windMain pour obtenir une vitesse en m/s.")]
        [SerializeField, Min(0f)] private float windZoneSpeedScale = 5f;
        [SerializeField] private Vector3 fallbackWindDirection = Vector3.forward;
        [SerializeField, Min(0f)] private float fallbackWindSpeed = 0f;

        [Header("Collisions")]
        [Tooltip("Racine où chercher les ClothCapsuleCollider (par défaut : transform.root).")]
        [SerializeField] private Transform collidersRoot;
        [SerializeField] private List<ClothCapsuleCollider> colliders = new List<ClothCapsuleCollider>();
        #endregion

        public VertexClothData Data => data;
        public VertexClothSettings Settings => settings;

        private ClothMeshBinding binding;
        private VertexClothSettings activeSettings;
        private bool ownsSettings;
        private bool initialized;
        private Vector3 lastPosition;

        private NativeArray<float3> positions, previous, normals, restLocal, targetStart, targetEnd;
        private NativeArray<float> freedom, stretchRest, bendRest;
        private NativeArray<int2> stretchEdges, bendPairs;
        private NativeArray<int3> triangles;
        private NativeArray<int> vertexToParticle, meshTriangles;
        private NativeArray<CapsuleData> capsules;
        private NativeArray<float3> meshVertices, meshNormals, meshBounds;

        #region Unity lifecycle
        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;

            float dt = Mathf.Min(Time.deltaTime, activeSettings.maxDeltaTime);
            if (dt <= 0f)
                return;

            HandleTeleport();
            RefreshColliders();

            binding.BeginFrame();

            // La cible précédente devient le point de départ de la frame
            (targetStart, targetEnd) = (targetEnd, targetStart);

            var simulationJob = new ClothSimulationJob
            {
                p = BuildParams(dt, GetAverageScale()),
                positions = positions,
                previous = previous,
                normals = normals,
                freedom = freedom,
                targetStart = targetStart,
                targetEnd = targetEnd,
                stretchEdges = stretchEdges,
                stretchRest = stretchRest,
                bendPairs = bendPairs,
                bendRest = bendRest,
                triangles = triangles,
                capsules = capsules
            };

            var writeJob = new ClothWriteMeshJob
            {
                positions = positions,
                vertexToParticle = vertexToParticle,
                meshTriangles = meshTriangles,
                worldToOutput = binding.WorldToOutput,
                vertices = meshVertices,
                vertexNormals = meshNormals,
                bounds = meshBounds
            };

            JobHandle handle = binding.ScheduleTargets(restLocal, targetEnd, default);
            handle = simulationJob.Schedule(handle);
            handle = writeJob.Schedule(handle);
            handle.Complete();

            ApplyToMesh();
        }
        #endregion

        #region Public API
        /// <summary>Assigne les données (utilisé par les outils d'édition).</summary>
        public void Configure(VertexClothData newData, VertexClothSettings newSettings)
        {
            data = newData;
            settings = newSettings;

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Shutdown();
                Initialize();
            }
        }

        public void SetWindZone(WindZone zone)
        {
            windZone = zone;
        }

        /// <summary>Replace instantanément le vêtement sur sa pose cible (téléportation, respawn).</summary>
        public void ResetSimulation()
        {
            if (!initialized)
                return;

            binding.ComputeTargetsNow(restLocal, positions);
            previous.CopyFrom(positions);
            targetStart.CopyFrom(positions);
            targetEnd.CopyFrom(positions);
            lastPosition = binding.Anchor.position;
        }

        [ContextMenu("Collect Colliders From Root")]
        public void CollectColliders()
        {
            Transform root = collidersRoot != null ? collidersRoot : transform.root;
            colliders.Clear();
            colliders.AddRange(root.GetComponentsInChildren<ClothCapsuleCollider>(true));
        }
        #endregion

        #region Initialisation
        private void Initialize()
        {
            binding = ClothMeshBinding.Create(gameObject);
            if (binding == null)
            {
                Debug.LogError("[VertexCloth] Aucun MeshFilter ou SkinnedMeshRenderer avec mesh sur cet objet.", this);
                enabled = false;
                return;
            }

            if (data == null || !data.IsValidFor(binding.SourceMesh))
            {
                Debug.LogError("[VertexCloth] Données absentes ou incompatibles avec le mesh. Refais un Bake.", this);
                binding = null;
                enabled = false;
                return;
            }

            activeSettings = settings;
            if (activeSettings == null)
            {
                activeSettings = ScriptableObject.CreateInstance<VertexClothSettings>();
                activeSettings.hideFlags = HideFlags.DontSave;
                ownsSettings = true;
            }

            if (colliders.Count == 0)
                CollectColliders();

            binding.CreateOutput(data);
            AllocateNative();
            initialized = true;
            ResetSimulation();
        }

        private void AllocateNative()
        {
            int count = data.ParticleCount;
            int vertexCount = binding.SourceMesh.vertexCount;

            restLocal = ToFloat3(data.particleRestPositions);
            freedom = new NativeArray<float>(data.particleFreedom, Allocator.Persistent);
            positions = new NativeArray<float3>(count, Allocator.Persistent);
            previous = new NativeArray<float3>(count, Allocator.Persistent);
            normals = new NativeArray<float3>(count, Allocator.Persistent);
            targetStart = new NativeArray<float3>(count, Allocator.Persistent);
            targetEnd = new NativeArray<float3>(count, Allocator.Persistent);

            stretchEdges = ToInt2(data.stretchEdges);
            stretchRest = new NativeArray<float>(data.stretchRestLengths, Allocator.Persistent);
            bendPairs = ToInt2(data.bendPairs);
            bendRest = new NativeArray<float>(data.bendRestLengths, Allocator.Persistent);
            triangles = ToInt3(data.particleTriangles);

            vertexToParticle = new NativeArray<int>(data.vertexToParticle, Allocator.Persistent);
            meshTriangles = new NativeArray<int>(data.meshTriangles, Allocator.Persistent);

            meshVertices = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            meshNormals = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            meshBounds = new NativeArray<float3>(2, Allocator.Persistent);

            capsules = new NativeArray<CapsuleData>(colliders.Count, Allocator.Persistent);
        }

        private void Shutdown()
        {
            if (binding != null)
                binding.Release();
            binding = null;

            Free(ref positions); Free(ref previous); Free(ref normals);
            Free(ref restLocal); Free(ref targetStart); Free(ref targetEnd);
            Free(ref freedom); Free(ref stretchRest); Free(ref bendRest);
            Free(ref stretchEdges); Free(ref bendPairs); Free(ref triangles);
            Free(ref vertexToParticle); Free(ref meshTriangles);
            Free(ref capsules);
            Free(ref meshVertices); Free(ref meshNormals); Free(ref meshBounds);

            if (ownsSettings && activeSettings != null)
                Destroy(activeSettings);
            activeSettings = null;
            ownsSettings = false;
            initialized = false;
        }

        private static void Free<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
        }
        #endregion

        #region Frame helpers
        private void HandleTeleport()
        {
            Vector3 position = binding.Anchor.position;
            float limit = activeSettings.teleportDistance;
            if (limit > 0f && (position - lastPosition).sqrMagnitude > limit * limit)
                ResetSimulation();

            lastPosition = position;
        }

        private void RefreshColliders()
        {
            if (capsules.Length != colliders.Count)
            {
                Free(ref capsules);
                capsules = new NativeArray<CapsuleData>(colliders.Count, Allocator.Persistent);
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                ClothCapsuleCollider c = colliders[i];
                capsules[i] = c == null
                    ? default
                    : new CapsuleData { a = c.WorldStart, b = c.WorldEnd, radius = c.WorldRadius };
            }
        }

        private SimParams BuildParams(float dt, float scale)
        {
            VertexClothSettings s = activeSettings;
            return new SimParams
            {
                deltaTime = dt,
                substeps = s.substeps,
                iterations = s.iterations,
                gravity = s.gravity * s.gravityScale,
                damping = s.damping,
                stretchStiffness = s.stretchStiffness,
                bendStiffness = s.bendStiffness,
                shapeAtPins = s.shapeStiffnessAtPins,
                shapeAtFree = s.shapeStiffnessAtFree,
                maxTravel = s.maxTravel * scale,
                restScale = scale,
                windVelocity = SampleWindVelocity(Time.time),
                windInfluence = s.windInfluence,
                windTurbulence = s.windTurbulence,
                turbulenceFrequency = s.turbulenceFrequency,
                time = Time.time,
                collisionThickness = s.collisionThickness * scale,
                collisionFriction = s.collisionFriction
            };
        }

        private float3 SampleWindVelocity(float time)
        {
            Vector3 direction = fallbackWindDirection;
            float speed = fallbackWindSpeed;

            if (windZone != null && windZone.gameObject.activeInHierarchy && windZone.mode == WindZoneMode.Directional)
            {
                direction = windZone.transform.forward;
                float pulse = 1f + windZone.windPulseMagnitude
                    * Mathf.Sin(time * windZone.windPulseFrequency * Mathf.PI * 2f);
                speed = windZone.windMain * windZoneSpeedScale * pulse;
            }

            if (direction.sqrMagnitude < 1e-6f || speed <= 0f)
                return float3.zero;

            float gust = (Mathf.PerlinNoise(time * activeSettings.gustFrequency, 0.5f) * 2f - 1f)
                         * activeSettings.gustAmount;
            speed = Mathf.Max(0f, speed * (1f + gust));
            return direction.normalized * speed;
        }

        private float GetAverageScale()
        {
            Vector3 s = binding.Anchor.lossyScale;
            return Mathf.Max(1e-5f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
        }

        private void ApplyToMesh()
        {
            Mesh output = binding.OutputMesh;
            output.SetVertices(meshVertices, 0, meshVertices.Length, MeshUpdateFlags.DontRecalculateBounds);
            output.SetNormals(meshNormals, 0, meshNormals.Length, MeshUpdateFlags.DontRecalculateBounds);

            var bounds = new Bounds();
            bounds.SetMinMax(meshBounds[0], meshBounds[1]);
            output.bounds = bounds;
        }
        #endregion

        #region Conversions
        private static NativeArray<float3> ToFloat3(Vector3[] source)
        {
            var result = new NativeArray<float3>(source.Length, Allocator.Persistent);
            for (int i = 0; i < source.Length; i++)
                result[i] = source[i];
            return result;
        }

        private static NativeArray<int2> ToInt2(Vector2Int[] source)
        {
            var result = new NativeArray<int2>(source.Length, Allocator.Persistent);
            for (int i = 0; i < source.Length; i++)
                result[i] = new int2(source[i].x, source[i].y);
            return result;
        }

        private static NativeArray<int3> ToInt3(int[] flat)
        {
            var result = new NativeArray<int3>(flat.Length / 3, Allocator.Persistent);
            for (int i = 0; i < result.Length; i++)
                result[i] = new int3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
            return result;
        }
        #endregion
    }
}