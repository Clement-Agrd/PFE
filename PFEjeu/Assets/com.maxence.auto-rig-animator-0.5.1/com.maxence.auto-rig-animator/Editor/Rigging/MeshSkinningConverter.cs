using System;
using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Swaps a raw mesh's MeshFilter/MeshRenderer for a SkinnedMeshRenderer bound to the built
    /// skeleton. The source mesh asset is never modified: a duplicate carries the new bindposes
    /// and bone weights, so re-running the tool on another object never corrupts a shared asset.
    /// </summary>
    internal static class MeshSkinningConverter
    {
        private const string SkinnedMeshSuffix = "_Skinned";

        /// <summary>
        /// Converts <paramref name="meshObject"/> in place. Throws on invalid input or on a
        /// boneWeights/vertex-count mismatch instead of letting Unity fail deeper with a cryptic
        /// internal error — defense in depth, since this is also the entry point a future batch tool
        /// (Phase 5c) will call directly, without going through the placement window's UI.
        /// </summary>
        public static SkinnedMeshRenderer Convert(GameObject meshObject, SkeletonBuildResult skeleton, BoneWeight[] boneWeights)
        {
            if (meshObject == null)
            {
                throw new ArgumentNullException(nameof(meshObject));
            }

            if (skeleton == null)
            {
                throw new ArgumentNullException(nameof(skeleton));
            }

            var meshFilter = meshObject.GetComponent<MeshFilter>();
            var meshRenderer = meshObject.GetComponent<MeshRenderer>();
            Mesh sourceMesh = meshFilter.sharedMesh;
            Material[] sharedMaterials = meshRenderer != null ? meshRenderer.sharedMaterials : null;

            if (boneWeights == null || boneWeights.Length != sourceMesh.vertexCount)
            {
                throw new InvalidOperationException(
                    $"BoneWeights count ({boneWeights?.Length ?? 0}) does not match the mesh's vertex count ({sourceMesh.vertexCount}).");
            }

            Mesh skinnedMesh = UnityEngine.Object.Instantiate(sourceMesh);
            skinnedMesh.name = sourceMesh.name + SkinnedMeshSuffix;
            skinnedMesh.bindposes = BuildBindposes(meshObject.transform, skeleton.Bones);
            skinnedMesh.boneWeights = boneWeights;

            if (meshRenderer != null)
            {
                Undo.DestroyObjectImmediate(meshRenderer);
            }

            Undo.DestroyObjectImmediate(meshFilter);

            SkinnedMeshRenderer skinnedRenderer = Undo.AddComponent<SkinnedMeshRenderer>(meshObject);
            skinnedRenderer.sharedMesh = skinnedMesh;
            skinnedRenderer.bones = skeleton.Bones;
            skinnedRenderer.rootBone = skeleton.RootBone;

            if (sharedMaterials != null)
            {
                skinnedRenderer.sharedMaterials = sharedMaterials;
            }

            return skinnedRenderer;
        }

        /// <summary>
        /// bindpose[i] = bone[i].worldToLocalMatrix * meshObjectLocalToWorld — the standard formula for
        /// binding a skeleton built in world space to a mesh authored in the mesh object's local space.
        /// </summary>
        private static Matrix4x4[] BuildBindposes(Transform meshTransform, Transform[] bones)
        {
            Matrix4x4 meshLocalToWorld = meshTransform.localToWorldMatrix;
            var bindposes = new Matrix4x4[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                bindposes[i] = bones[i].worldToLocalMatrix * meshLocalToWorld;
            }

            return bindposes;
        }
    }
}
