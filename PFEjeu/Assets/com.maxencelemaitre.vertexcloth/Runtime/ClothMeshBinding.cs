using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VertexCloth
{
    /// <summary>
    /// Abstraction du support de rendu d'un vêtement :
    /// mesh statique (MeshFilter) ou SkinnedMeshRenderer (suit l'animation du corps).
    /// Le simulateur ne connaît que cette interface.
    /// </summary>
    public abstract class ClothMeshBinding
    {
        /// <summary>Mesh d'origine (pose de bind) : sert à valider les données bakées.</summary>
        public abstract Mesh SourceMesh { get; }

        /// <summary>Mesh modifié chaque frame par la simulation.</summary>
        public abstract Mesh OutputMesh { get; }

        /// <summary>Transform de référence (téléportation, échelle).</summary>
        public abstract Transform Anchor { get; }

        /// <summary>Matrice monde -> espace des vertices du mesh de sortie.</summary>
        public abstract float4x4 WorldToOutput { get; }

        /// <summary>Trouve les composants nécessaires. Retourne false si le support est inutilisable.</summary>
        public abstract bool Bind(GameObject host);

        /// <summary>Crée le mesh de sortie et branche le rendu dessus.</summary>
        public abstract void CreateOutput(VertexClothData data);

        /// <summary>Appelé une fois par frame sur le thread principal avant de planifier les jobs.</summary>
        public abstract void BeginFrame();

        /// <summary>Calcule immédiatement les cibles monde (initialisation, reset).</summary>
        public abstract void ComputeTargetsNow(NativeArray<float3> restLocal, NativeArray<float3> targets);

        /// <summary>Planifie le calcul des cibles monde de la frame.</summary>
        public abstract JobHandle ScheduleTargets(NativeArray<float3> restLocal, NativeArray<float3> targets, JobHandle dependency);

        /// <summary>Restaure le rendu d'origine et libère les ressources.</summary>
        public abstract void Release();

        /// <summary>Choisit l'implémentation selon les composants présents sur l'objet.</summary>
        public static ClothMeshBinding Create(GameObject host)
        {
            ClothMeshBinding binding;
            if (host.GetComponent<SkinnedMeshRenderer>() != null)
                binding = new SkinnedClothBinding();
            else
                binding = new StaticClothBinding();

            return binding.Bind(host) ? binding : null;
        }
    }

    /// <summary>Mesh statique : le MeshFilter reçoit une copie du mesh modifiée en espace local.</summary>
    public sealed class StaticClothBinding : ClothMeshBinding
    {
        private MeshFilter filter;
        private Mesh original;
        private Mesh runtime;

        public override Mesh SourceMesh => original;
        public override Mesh OutputMesh => runtime;
        public override Transform Anchor => filter.transform;
        public override float4x4 WorldToOutput => filter.transform.worldToLocalMatrix;

        public override bool Bind(GameObject host)
        {
            filter = host.GetComponent<MeshFilter>();
            original = filter != null ? filter.sharedMesh : null;
            return original != null;
        }

        public override void CreateOutput(VertexClothData data)
        {
            runtime = Object.Instantiate(original);
            runtime.name = original.name + " (Cloth)";
            runtime.MarkDynamic();
            filter.sharedMesh = runtime;
        }

        public override void BeginFrame()
        {
        }

        public override void ComputeTargetsNow(NativeArray<float3> restLocal, NativeArray<float3> targets)
        {
            float4x4 localToWorld = filter.transform.localToWorldMatrix;
            for (int i = 0; i < restLocal.Length; i++)
                targets[i] = math.transform(localToWorld, restLocal[i]);
        }

        public override JobHandle ScheduleTargets(NativeArray<float3> restLocal, NativeArray<float3> targets, JobHandle dependency)
        {
            var job = new ComputeTargetsJob
            {
                restLocal = restLocal,
                localToWorld = filter.transform.localToWorldMatrix,
                targets = targets
            };

            return job.Schedule(restLocal.Length, 256, dependency);
        }

        public override void Release()
        {
            if (filter != null && runtime != null && filter.sharedMesh == runtime)
                filter.sharedMesh = original;

            if (runtime != null)
                Object.Destroy(runtime);
            runtime = null;
        }
    }

    /// <summary>
    /// SkinnedMeshRenderer : le skinning CPU (BakeMesh) fournit la pose animée,
    /// la simulation s'ajoute par-dessus et le résultat est rendu par un objet dédié (espace monde).
    /// Le SkinnedMeshRenderer d'origine continue d'être animé mais n'est plus dessiné.
    /// </summary>
    public sealed class SkinnedClothBinding : ClothMeshBinding
    {
        private SkinnedMeshRenderer skinned;
        private Mesh original;
        private Mesh runtime;
        private Mesh bakeMesh;
        private GameObject renderObject;
        private bool previousForceRenderingOff;

        private NativeArray<float3> baked;
        private NativeArray<int> particleFirstVertex;

        public override Mesh SourceMesh => original;
        public override Mesh OutputMesh => runtime;
        public override Transform Anchor => skinned.transform;
        public override float4x4 WorldToOutput => float4x4.identity;

        public override bool Bind(GameObject host)
        {
            skinned = host.GetComponent<SkinnedMeshRenderer>();
            original = skinned != null ? skinned.sharedMesh : null;
            return original != null;
        }

        public override void CreateOutput(VertexClothData data)
        {
            runtime = Object.Instantiate(original);
            runtime.name = original.name + " (Cloth)";
            runtime.MarkDynamic();

            bakeMesh = new Mesh { name = original.name + " (Bake)" };
            bakeMesh.MarkDynamic();

            renderObject = new GameObject(skinned.name + "_ClothRender");
            renderObject.layer = skinned.gameObject.layer;

            MeshFilter outputFilter = renderObject.AddComponent<MeshFilter>();
            outputFilter.sharedMesh = runtime;

            MeshRenderer outputRenderer = renderObject.AddComponent<MeshRenderer>();
            outputRenderer.sharedMaterials = skinned.sharedMaterials;
            outputRenderer.shadowCastingMode = skinned.shadowCastingMode;
            outputRenderer.receiveShadows = skinned.receiveShadows;
            outputRenderer.lightProbeUsage = skinned.lightProbeUsage;
            outputRenderer.reflectionProbeUsage = skinned.reflectionProbeUsage;
            outputRenderer.probeAnchor = skinned.probeAnchor;
            outputRenderer.renderingLayerMask = skinned.renderingLayerMask;

            previousForceRenderingOff = skinned.forceRenderingOff;
            skinned.forceRenderingOff = true;

            baked = new NativeArray<float3>(original.vertexCount, Allocator.Persistent);

            int[] first = new int[data.ParticleCount];
            for (int i = 0; i < first.Length; i++)
                first[i] = -1;

            for (int v = 0; v < data.vertexToParticle.Length; v++)
            {
                int particle = data.vertexToParticle[v];
                if (first[particle] < 0)
                    first[particle] = v;
            }

            particleFirstVertex = new NativeArray<int>(first, Allocator.Persistent);
        }

        public override void BeginFrame()
        {
            skinned.BakeMesh(bakeMesh, true);

            using (Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(bakeMesh))
            {
                dataArray[0].GetVertices(baked.Reinterpret<Vector3>());
            }
        }

        public override void ComputeTargetsNow(NativeArray<float3> restLocal, NativeArray<float3> targets)
        {
            BeginFrame();

            float4x4 bakedToWorld = GetBakedToWorld();
            for (int i = 0; i < targets.Length; i++)
                targets[i] = math.transform(bakedToWorld, baked[particleFirstVertex[i]]);
        }

        public override JobHandle ScheduleTargets(NativeArray<float3> restLocal, NativeArray<float3> targets, JobHandle dependency)
        {
            var job = new ComputeSkinnedTargetsJob
            {
                bakedVertices = baked,
                particleFirstVertex = particleFirstVertex,
                bakedToWorld = GetBakedToWorld(),
                targets = targets
            };

            return job.Schedule(targets.Length, 256, dependency);
        }

        public override void Release()
        {
            if (skinned != null)
                skinned.forceRenderingOff = previousForceRenderingOff;

            if (renderObject != null)
                Object.Destroy(renderObject);
            renderObject = null;

            if (runtime != null)
                Object.Destroy(runtime);
            runtime = null;

            if (bakeMesh != null)
                Object.Destroy(bakeMesh);
            bakeMesh = null;

            if (baked.IsCreated) baked.Dispose();
            if (particleFirstVertex.IsCreated) particleFirstVertex.Dispose();
        }

        /// <summary>BakeMesh(useScale: true) donne des vertices à l'échelle monde : on n'applique que position + rotation.</summary>
        private float4x4 GetBakedToWorld()
        {
            Transform t = skinned.transform;
            return float4x4.TRS(t.position, t.rotation, new float3(1f, 1f, 1f));
        }
    }
}
