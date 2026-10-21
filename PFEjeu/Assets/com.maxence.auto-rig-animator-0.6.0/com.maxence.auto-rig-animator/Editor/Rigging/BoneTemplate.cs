using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>Immutable description of one bone in the simplified humanoid skeleton template.</summary>
    internal readonly struct BoneDefinition
    {
        public readonly string Name;
        public readonly string ParentName;
        public readonly HumanBodyBones HumanoidBone;
        public readonly bool IsOptional;

        /// <summary>
        /// Name of the bone this one should visually "aim" at (point its forward axis toward) when
        /// <see cref="SkeletonBuilder"/> orients the skeleton — null for a leaf bone (Head, hands,
        /// feet, toes), which instead just copies its parent's final rotation.
        /// </summary>
        public readonly string AimChildName;

        public BoneDefinition(string name, string parentName, HumanBodyBones humanoidBone, bool isOptional, string aimChildName)
        {
            Name = name;
            ParentName = parentName;
            HumanoidBone = humanoidBone;
            IsOptional = isOptional;
            AimChildName = aimChildName;
        }
    }

    /// <summary>
    /// Fixed list of bones for the simplified humanoid skeleton (no fingers, one spine segment).
    /// Order matters: a bone's parent always appears earlier in the list, which lets the placement
    /// tool and the future skeleton builder walk the list top to bottom without resolving order.
    /// Names match Unity's Humanoid convention 1:1, so mapping to <see cref="HumanBodyBones"/> is direct
    /// (needed in Phase 3 for AvatarBuilder.BuildHumanAvatar).
    /// </summary>
    internal static class BoneTemplate
    {
        public static readonly BoneDefinition[] Bones =
        {
            new BoneDefinition("Hips", null, HumanBodyBones.Hips, false, "Spine"),
            new BoneDefinition("Spine", "Hips", HumanBodyBones.Spine, false, "Chest"),
            new BoneDefinition("Chest", "Spine", HumanBodyBones.Chest, false, "Neck"),
            new BoneDefinition("Neck", "Chest", HumanBodyBones.Neck, false, "Head"),
            new BoneDefinition("Head", "Neck", HumanBodyBones.Head, false, null),

            new BoneDefinition("LeftShoulder", "Chest", HumanBodyBones.LeftShoulder, true, "LeftUpperArm"),
            new BoneDefinition("LeftUpperArm", "LeftShoulder", HumanBodyBones.LeftUpperArm, false, "LeftLowerArm"),
            new BoneDefinition("LeftLowerArm", "LeftUpperArm", HumanBodyBones.LeftLowerArm, false, "LeftHand"),
            new BoneDefinition("LeftHand", "LeftLowerArm", HumanBodyBones.LeftHand, false, null),

            new BoneDefinition("RightShoulder", "Chest", HumanBodyBones.RightShoulder, true, "RightUpperArm"),
            new BoneDefinition("RightUpperArm", "RightShoulder", HumanBodyBones.RightUpperArm, false, "RightLowerArm"),
            new BoneDefinition("RightLowerArm", "RightUpperArm", HumanBodyBones.RightLowerArm, false, "RightHand"),
            new BoneDefinition("RightHand", "RightLowerArm", HumanBodyBones.RightHand, false, null),

            new BoneDefinition("LeftUpperLeg", "Hips", HumanBodyBones.LeftUpperLeg, false, "LeftLowerLeg"),
            new BoneDefinition("LeftLowerLeg", "LeftUpperLeg", HumanBodyBones.LeftLowerLeg, false, "LeftFoot"),
            new BoneDefinition("LeftFoot", "LeftLowerLeg", HumanBodyBones.LeftFoot, false, "LeftToes"),
            new BoneDefinition("LeftToes", "LeftFoot", HumanBodyBones.LeftToes, true, null),

            new BoneDefinition("RightUpperLeg", "Hips", HumanBodyBones.RightUpperLeg, false, "RightLowerLeg"),
            new BoneDefinition("RightLowerLeg", "RightUpperLeg", HumanBodyBones.RightLowerLeg, false, "RightFoot"),
            new BoneDefinition("RightFoot", "RightLowerLeg", HumanBodyBones.RightFoot, false, "RightToes"),
            new BoneDefinition("RightToes", "RightFoot", HumanBodyBones.RightToes, true, null),
        };

        /// <summary>Index of a bone by name in <see cref="Bones"/>, or -1 if it doesn't exist.</summary>
        public static int IndexOf(string boneName)
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                if (Bones[i].Name == boneName)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
