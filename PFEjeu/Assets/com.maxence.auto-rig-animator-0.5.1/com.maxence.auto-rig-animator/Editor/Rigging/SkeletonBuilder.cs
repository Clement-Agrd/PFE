using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>Bone hierarchy created from a <see cref="MarkerSet"/>, ready for skinning.</summary>
    internal sealed class SkeletonBuildResult
    {
        public readonly Transform[] Bones;
        public readonly Transform RootBone;
        public readonly Dictionary<string, int> BoneIndexByName;

        public SkeletonBuildResult(Transform[] bones, Transform rootBone, Dictionary<string, int> boneIndexByName)
        {
            Bones = bones;
            RootBone = rootBone;
            BoneIndexByName = boneIndexByName;
        }
    }

    /// <summary>
    /// Builds the bone GameObject hierarchy from the markers placed in Phase 1.
    /// Bones are created as siblings of the mesh object (children of its parent, or scene root if it
    /// has none) so the mesh object itself is never reparented. An optional bone with no marker is
    /// skipped and its would-be children attach straight to its nearest placed ancestor instead.
    /// </summary>
    internal static class SkeletonBuilder
    {
        private const string UndoLabel = "Build Skeleton";

        // Bones along the spine aim roughly straight up (world up). Quaternion.LookRotation needs an
        // "up" reference that is NOT parallel to the aim direction, so these bones use the character's
        // computed forward vector as their up-reference instead of world up (see OrientBones).
        private static readonly HashSet<string> SpineChainBones = new HashSet<string> { "Hips", "Spine", "Chest", "Neck" };

        /// <summary>
        /// Builds the bone hierarchy. Throws on invalid input (null <paramref name="meshObject"/> or
        /// <paramref name="markers"/>) rather than failing deeper with a less obvious NullReferenceException
        /// — defense in depth, since this method is also the entry point a future batch tool (Phase 5c)
        /// will call directly, without going through <see cref="MarkerPlacementWindow"/>'s own UI checks.
        /// </summary>
        public static SkeletonBuildResult Build(GameObject meshObject, MarkerSet markers)
        {
            if (meshObject == null)
            {
                throw new ArgumentNullException(nameof(meshObject));
            }

            if (markers == null)
            {
                throw new ArgumentNullException(nameof(markers));
            }

            Transform siblingParent = meshObject.transform.parent;
            var createdByName = new Dictionary<string, Transform>(BoneTemplate.Bones.Length);
            var bones = new List<Transform>(BoneTemplate.Bones.Length);
            var boneIndexByName = new Dictionary<string, int>(BoneTemplate.Bones.Length);

            // Pass 1: create every bone at its marker position, identity rotation (as before).
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (!markers.TryGetPosition(bone.Name, out Vector3 worldPosition))
                {
                    // Not placed: an optional bone the user skipped.
                    continue;
                }

                Transform parentTransform = ResolveParentTransform(bone.ParentName, createdByName, siblingParent);

                var boneObject = new GameObject(bone.Name);
                Undo.RegisterCreatedObjectUndo(boneObject, UndoLabel);
                boneObject.transform.SetParent(parentTransform, false);
                boneObject.transform.position = worldPosition;
                boneObject.transform.rotation = Quaternion.identity;
                boneObject.transform.localScale = Vector3.one;

                createdByName[bone.Name] = boneObject.transform;
                boneIndexByName[bone.Name] = bones.Count;
                bones.Add(boneObject.transform);
            }

            // Pass 2: orient each bone to point down its limb/spine segment instead of leaving every
            // bone with identity (world-aligned) rotation. A Humanoid Avatar retargets an animation
            // clip's muscle data onto each bone's LOCAL axes; if every bone shares the same
            // world-aligned axes regardless of which way it actually points, the retargeted rotations
            // land on the wrong local axes and the result can look broken (limbs twisted, or the whole
            // character ending up lying down) even though positions/skinning are correct.
            OrientBones(createdByName);

            // Pass 3: re-apply the marker positions. Rotating a bone in Unity changes where its
            // children land in world space (child world position is derived from the parent's matrix),
            // so Pass 2 shifts every descendant's position. This restores them, in parent-before-child
            // order so a restored ancestor is never displaced again by a later fix further down.
            RestoreMarkerPositions(createdByName, markers);

            createdByName.TryGetValue("Hips", out Transform rootBone);
            return new SkeletonBuildResult(bones.ToArray(), rootBone, boneIndexByName);
        }

        /// <summary>
        /// Computes a consistent character forward/right/up from the placed markers, then rotates
        /// each created bone to aim at its <see cref="BoneDefinition.AimChildName"/> bone. Bones with
        /// no aim child (Head, hands, feet, toes) simply copy their parent's final rotation instead
        /// of staying at identity, so they don't look like an arbitrary "snap back to world axes".
        /// </summary>
        private static void OrientBones(Dictionary<string, Transform> createdByName)
        {
            Vector3 characterUp = Vector3.up;
            Vector3 characterRight = ComputeCharacterRight(createdByName);
            Vector3 characterForward = Vector3.Cross(characterUp, characterRight).normalized;
            if (characterForward.sqrMagnitude < 0.0001f)
            {
                // Degenerate (left/right markers nearly coincide): fall back to world forward rather
                // than feeding LookRotation a zero vector.
                characterForward = Vector3.forward;
            }

            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                BoneDefinition bone = BoneTemplate.Bones[i];
                if (!createdByName.TryGetValue(bone.Name, out Transform boneTransform))
                {
                    continue;
                }

                Transform aimChild = ResolveAimChild(bone, createdByName);
                if (aimChild == null)
                {
                    // Leaf bone: inherit the parent's orientation instead of leaving it at identity.
                    if (boneTransform.parent != null)
                    {
                        boneTransform.rotation = boneTransform.parent.rotation;
                    }

                    continue;
                }

                Vector3 aimDirection = aimChild.position - boneTransform.position;
                if (aimDirection.sqrMagnitude < 0.0001f)
                {
                    // Child marker placed on top of the parent: nothing sensible to aim at, leave as-is.
                    continue;
                }

                Vector3 upReference = SpineChainBones.Contains(bone.Name) ? characterForward : characterUp;
                boneTransform.rotation = Quaternion.LookRotation(aimDirection.normalized, upReference);
            }
        }

        /// <summary>
        /// "Right" vector of the character, from the hip markers (falls back to the hand markers,
        /// then to world right) — used as the up-reference for limb bones and, combined with world
        /// up, to derive the spine's own forward-facing up-reference.
        /// </summary>
        private static Vector3 ComputeCharacterRight(Dictionary<string, Transform> createdByName)
        {
            if (createdByName.TryGetValue("LeftUpperLeg", out Transform leftLeg) &&
                createdByName.TryGetValue("RightUpperLeg", out Transform rightLeg))
            {
                Vector3 right = rightLeg.position - leftLeg.position;
                if (right.sqrMagnitude > 0.0001f)
                {
                    return right.normalized;
                }
            }

            if (createdByName.TryGetValue("LeftHand", out Transform leftHand) &&
                createdByName.TryGetValue("RightHand", out Transform rightHand))
            {
                Vector3 right = rightHand.position - leftHand.position;
                if (right.sqrMagnitude > 0.0001f)
                {
                    return right.normalized;
                }
            }

            return Vector3.right; // Last-resort fallback.
        }

        /// <summary>
        /// Resolves a bone's aim-child Transform, walking past a skipped optional bone (e.g. no Toes
        /// placed) to the next one down the chain so the aim direction still points at something real.
        /// </summary>
        private static Transform ResolveAimChild(BoneDefinition bone, Dictionary<string, Transform> createdByName)
        {
            string childName = bone.AimChildName;
            while (!string.IsNullOrEmpty(childName))
            {
                if (createdByName.TryGetValue(childName, out Transform childTransform))
                {
                    return childTransform;
                }

                int index = BoneTemplate.IndexOf(childName);
                childName = index >= 0 ? BoneTemplate.Bones[index].AimChildName : null;
            }

            return null;
        }

        private static void RestoreMarkerPositions(Dictionary<string, Transform> createdByName, MarkerSet markers)
        {
            for (int i = 0; i < BoneTemplate.Bones.Length; i++)
            {
                string boneName = BoneTemplate.Bones[i].Name;
                if (createdByName.TryGetValue(boneName, out Transform boneTransform) &&
                    markers.TryGetPosition(boneName, out Vector3 worldPosition))
                {
                    boneTransform.position = worldPosition;
                }
            }
        }

        /// <summary>
        /// Walks up the template's parent chain until it finds a bone that was actually created,
        /// so a skipped optional bone (e.g. LeftShoulder) does not break the hierarchy below it.
        /// </summary>
        private static Transform ResolveParentTransform(string parentBoneName, Dictionary<string, Transform> createdByName, Transform fallbackParent)
        {
            string current = parentBoneName;
            while (!string.IsNullOrEmpty(current))
            {
                if (createdByName.TryGetValue(current, out Transform found))
                {
                    return found;
                }

                int index = BoneTemplate.IndexOf(current);
                current = index >= 0 ? BoneTemplate.Bones[index].ParentName : null;
            }

            return fallbackParent;
        }
    }
}
