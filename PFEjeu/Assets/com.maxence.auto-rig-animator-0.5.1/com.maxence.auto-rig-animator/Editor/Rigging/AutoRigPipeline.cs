using System;
using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Orchestrates the Auto Rig pipeline: Phase 1's markers into Phase 2's skinned mesh, then
    /// Phase 2's skinned mesh into Phase 3's Humanoid Avatar.
    ///
    /// Both public methods validate their input and wrap the actual build steps in a try/catch: if
    /// anything throws partway through (an unexpected exception, or the user cancelling the skinning
    /// progress bar), everything created since the start of that call is automatically rolled back via
    /// <see cref="Undo.RevertAllDownToGroup"/>, so a failed attempt never leaves half-built
    /// GameObjects/components behind for the caller to clean up by hand.
    /// </summary>
    internal static class AutoRigPipeline
    {
        private const string SkinUndoGroupName = "Auto Rig: Build Skeleton And Skin";
        private const string AvatarUndoGroupName = "Auto Rig: Build Humanoid Avatar";
        private const string AvatarFolder = "Assets/AutoRig/Avatars";
        private const string AvatarAssetSuffix = "_Avatar";

        public enum Result
        {
            Success,
            AlreadyRigged,
            MissingMeshFilter,
            EmptyMesh,
            InvalidInput,
            Cancelled,
            UnexpectedError,
        }

        public enum AvatarResult
        {
            Success,
            NotRiggedYet,
            InvalidAvatar,
            BuildThrew,
            InvalidInput,
            UnexpectedError,
        }

        #region Phase 2 — Skeleton + Skinning

        public static Result BuildSkeletonAndSkin(GameObject meshObject, MarkerSet markers)
        {
            if (meshObject == null || markers == null || !markers.HasAllRequiredMarkers())
            {
                return Result.InvalidInput;
            }

            if (meshObject.GetComponent<SkinnedMeshRenderer>() != null)
            {
                return Result.AlreadyRigged;
            }

            var meshFilter = meshObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return Result.MissingMeshFilter;
            }

            if (meshFilter.sharedMesh.vertexCount == 0)
            {
                return Result.EmptyMesh;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(SkinUndoGroupName);

            try
            {
                SkeletonBuildResult skeleton = SkeletonBuilder.Build(meshObject, markers);
                BoneWeight[] boneWeights = AutoSkinner.ComputeBoneWeights(meshFilter.sharedMesh, meshObject.transform, skeleton.Bones, skeleton.BoneIndexByName);
                MeshSkinningConverter.Convert(meshObject, skeleton, boneWeights);
            }
            catch (OperationCanceledException)
            {
                // The user cancelled via the skinning progress bar: undo whatever this attempt already
                // created (bones, mostly) so nothing half-built is left behind.
                Undo.RevertAllDownToGroup(undoGroup);
                return Result.Cancelled;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Auto Rig] Construction du squelette/skinning interrompue : {exception}");
                Undo.RevertAllDownToGroup(undoGroup);
                return Result.UnexpectedError;
            }

            // One Ctrl+Z undoes the whole build: skeleton creation + mesh component swap.
            Undo.CollapseUndoOperations(undoGroup);
            return Result.Success;
        }

        #endregion

        #region Phase 3 — Humanoid Avatar

        public static AvatarResult BuildHumanoidAvatar(GameObject meshObject, out Avatar avatar, out GameObject rigRoot)
        {
            avatar = null;
            rigRoot = null;

            if (meshObject == null)
            {
                return AvatarResult.InvalidInput;
            }

            var skinnedRenderer = meshObject.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer == null || skinnedRenderer.rootBone == null || skinnedRenderer.bones == null || skinnedRenderer.bones.Length == 0)
            {
                return AvatarResult.NotRiggedYet;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(AvatarUndoGroupName);

            GameObject root;
            try
            {
                root = AvatarRootBuilder.CreateRoot(meshObject, skinnedRenderer.rootBone);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Auto Rig] Construction de la racine de l'Avatar interrompue : {exception}");
                Undo.RevertAllDownToGroup(undoGroup);
                return AvatarResult.UnexpectedError;
            }

            Avatar builtAvatar;
            try
            {
                builtAvatar = HumanoidAvatarBuilder.Build(root, skinnedRenderer.bones);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Auto Rig] AvatarBuilder a levé une exception : {exception.Message}");
                // Undoes the reparenting too, not just the avatar build attempt: previously this only
                // collapsed the undo group, leaving the half-built "_Rig" root behind for a manual
                // Ctrl+Z. Reverting automatically here matches the same "no half-built state survives
                // a failure" guarantee as the skinning step above.
                Undo.RevertAllDownToGroup(undoGroup);
                return AvatarResult.BuildThrew;
            }

            if (builtAvatar == null || !builtAvatar.isValid || !builtAvatar.isHuman)
            {
                if (builtAvatar != null)
                {
                    UnityEngine.Object.DestroyImmediate(builtAvatar);
                }

                // Same reasoning as above: don't leave a "_Rig" root with no usable Avatar behind.
                Undo.RevertAllDownToGroup(undoGroup);
                return AvatarResult.InvalidAvatar;
            }

            builtAvatar.name = meshObject.name + AvatarAssetSuffix;
            SaveAvatarAsset(builtAvatar);

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(root);
            }

            animator.avatar = builtAvatar;

            // The hierarchy change (reparenting, Animator) is undoable; the .asset file on disk is not
            // (AssetDatabase writes never go through the Undo system) — documented limitation.
            Undo.CollapseUndoOperations(undoGroup);

            avatar = builtAvatar;
            rigRoot = root;
            return AvatarResult.Success;
        }

        private static void SaveAvatarAsset(Avatar avatar)
        {
            if (!AssetDatabase.IsValidFolder("Assets/AutoRig"))
            {
                AssetDatabase.CreateFolder("Assets", "AutoRig");
            }

            if (!AssetDatabase.IsValidFolder(AvatarFolder))
            {
                AssetDatabase.CreateFolder("Assets/AutoRig", "Avatars");
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(AvatarFolder + "/" + avatar.name + ".asset");
            AssetDatabase.CreateAsset(avatar, assetPath);
            AssetDatabase.SaveAssets();
        }

        #endregion
    }
}
