using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Builds the <see cref="HumanDescription"/> AvatarBuilder needs from the hierarchy created in
    /// Phases 1-2, then calls <see cref="AvatarBuilder.BuildHumanAvatar"/>. The numeric defaults below
    /// mirror Unity's own Humanoid import defaults (twist 0.5, stretch 0.05, no feet spacing, no
    /// translation DoF) so the resulting Avatar behaves like a standard Mecanim rig.
    /// </summary>
    internal static class HumanoidAvatarBuilder
    {
        private const float DefaultTwist = 0.5f;
        private const float DefaultStretch = 0.05f;

        public static Avatar Build(GameObject rootObject, Transform[] bones)
        {
            HumanDescription description = BuildDescription(rootObject, bones);
            return AvatarBuilder.BuildHumanAvatar(rootObject, description);
        }

        private static HumanDescription BuildDescription(GameObject rootObject, Transform[] bones)
        {
            return new HumanDescription
            {
                human = BuildHumanBones(bones),
                skeleton = BuildSkeletonBones(rootObject.transform),
                upperArmTwist = DefaultTwist,
                lowerArmTwist = DefaultTwist,
                upperLegTwist = DefaultTwist,
                lowerLegTwist = DefaultTwist,
                armStretch = DefaultStretch,
                legStretch = DefaultStretch,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };
        }

        /// <summary>One entry per bone that maps to a Humanoid muscle; a bone we created but that is
        /// not in <see cref="BoneTemplate"/> (should not happen) is simply left out of the mapping.</summary>
        private static HumanBone[] BuildHumanBones(Transform[] bones)
        {
            var humanBones = new List<HumanBone>(bones.Length);

            for (int i = 0; i < bones.Length; i++)
            {
                int templateIndex = BoneTemplate.IndexOf(bones[i].name);
                if (templateIndex < 0)
                {
                    continue;
                }

                HumanBodyBones humanoidBone = BoneTemplate.Bones[templateIndex].HumanoidBone;
                humanBones.Add(new HumanBone
                {
                    boneName = bones[i].name,
                    humanName = HumanTrait.BoneName[(int)humanoidBone],
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }

            return humanBones.ToArray();
        }

        /// <summary>
        /// One entry per Transform under <paramref name="root"/> (root included), using each one's
        /// CURRENT local transform as its bind pose — this is why the mesh must be in T-pose at build time.
        /// </summary>
        private static SkeletonBone[] BuildSkeletonBones(Transform root)
        {
            var transforms = new List<Transform>();
            CollectHierarchy(root, transforms);

            var skeletonBones = new SkeletonBone[transforms.Count];
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform current = transforms[i];
                skeletonBones[i] = new SkeletonBone
                {
                    name = current.name,
                    position = current.localPosition,
                    rotation = current.localRotation,
                    scale = current.localScale,
                };
            }

            return skeletonBones;
        }

        private static void CollectHierarchy(Transform current, List<Transform> output)
        {
            output.Add(current);
            for (int i = 0; i < current.childCount; i++)
            {
                CollectHierarchy(current.GetChild(i), output);
            }
        }
    }
}
