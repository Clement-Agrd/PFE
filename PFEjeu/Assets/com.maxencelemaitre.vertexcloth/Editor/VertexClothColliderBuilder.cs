using UnityEditor;
using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>Génère des ClothCapsuleCollider sur les bones d'un personnage Humanoid.</summary>
    public static class VertexClothColliderBuilder
    {
        public const string ColliderPrefix = "ClothCollider_";

        // Rayon des membres = longueur du segment x facteur (proportions humaines moyennes)
        private const float UpperLegFactor = 0.22f;
        private const float LowerLegFactor = 0.16f;
        private const float UpperArmFactor = 0.17f;
        private const float LowerArmFactor = 0.15f;
        private const float TorsoFromShoulderWidth = 0.30f;
        private const float PelvisFromHipWidth = 0.75f;

        /// <summary>Supprime les colliders générés précédemment. Retourne le nombre supprimé.</summary>
        public static int RemoveAutoColliders(GameObject root)
        {
            int removed = 0;
            foreach (ClothCapsuleCollider collider in root.GetComponentsInChildren<ClothCapsuleCollider>(true))
            {
                if (collider != null && collider.gameObject.name.StartsWith(ColliderPrefix))
                {
                    Undo.DestroyObjectImmediate(collider.gameObject);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>Crée les capsules du corps. Retourne le nombre créé (0 si l'Animator n'est pas Humanoid).</summary>
        public static int AddHumanoidColliders(Animator animator, float radiusScale, bool includeHead)
        {
            if (animator == null || !animator.isHuman)
                return 0;

            RemoveAutoColliders(animator.gameObject);

            Transform hips = Bone(animator, HumanBodyBones.Hips);
            Transform neck = Bone(animator, HumanBodyBones.Neck);
            Transform head = Bone(animator, HumanBodyBones.Head);
            Transform upperLegL = Bone(animator, HumanBodyBones.LeftUpperLeg);
            Transform upperLegR = Bone(animator, HumanBodyBones.RightUpperLeg);
            Transform upperArmL = Bone(animator, HumanBodyBones.LeftUpperArm);
            Transform upperArmR = Bone(animator, HumanBodyBones.RightUpperArm);

            if (neck == null)
                neck = Bone(animator, HumanBodyBones.UpperChest) ?? Bone(animator, HumanBodyBones.Chest);

            float hipWidth = Distance(upperLegL, upperLegR);
            float shoulderWidth = Distance(upperArmL, upperArmR);

            int count = 0;

            // Tronc et bassin
            count += Create(hips, neck, shoulderWidth * TorsoFromShoulderWidth * radiusScale, "Torso") ? 1 : 0;
            count += Create(hips, hips, hipWidth * PelvisFromHipWidth * radiusScale, "Pelvis") ? 1 : 0;

            // Jambes
            count += CreateLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, UpperLegFactor, radiusScale, "UpperLegL");
            count += CreateLimb(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, LowerLegFactor, radiusScale, "LowerLegL");
            count += CreateLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, UpperLegFactor, radiusScale, "UpperLegR");
            count += CreateLimb(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, LowerLegFactor, radiusScale, "LowerLegR");

            // Bras
            count += CreateLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, UpperArmFactor, radiusScale, "UpperArmL");
            count += CreateLimb(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, LowerArmFactor, radiusScale, "LowerArmL");
            count += CreateLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, UpperArmFactor, radiusScale, "UpperArmR");
            count += CreateLimb(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, LowerArmFactor, radiusScale, "LowerArmR");

            if (includeHead && head != null && neck != null)
                count += CreateHead(neck, head, radiusScale) ? 1 : 0;

            return count;
        }

        private static int CreateLimb(Animator animator, HumanBodyBones from, HumanBodyBones to,
            float factor, float radiusScale, string label)
        {
            Transform a = Bone(animator, from);
            Transform b = Bone(animator, to);
            if (a == null || b == null)
                return 0;

            return Create(a, b, Vector3.Distance(a.position, b.position) * factor * radiusScale, label) ? 1 : 0;
        }

        private static bool CreateHead(Transform neck, Transform head, float radiusScale)
        {
            float headLength = Vector3.Distance(neck.position, head.position);
            if (headLength < 1e-4f)
                return false;

            Vector3 center = head.position + (head.position - neck.position) * 0.8f;
            return CreateAt(head, center, center, headLength * 0.9f * radiusScale, "Head");
        }

        private static bool Create(Transform from, Transform to, float worldRadius, string label)
        {
            if (from == null || to == null)
                return false;

            return CreateAt(from, from.position, to.position, worldRadius, label);
        }

        /// <summary>Crée un collider enfant de parent, entre deux points monde, avec un rayon monde.</summary>
        private static bool CreateAt(Transform parent, Vector3 worldStart, Vector3 worldEnd, float worldRadius, string label)
        {
            if (parent == null || worldRadius <= 1e-5f)
                return false;

            var go = new GameObject(ColliderPrefix + label);
            Undo.RegisterCreatedObjectUndo(go, "Add Cloth Colliders");
            go.transform.SetParent(parent, false);

            Vector3 s = go.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 1e-5f);

            var collider = go.AddComponent<ClothCapsuleCollider>();
            collider.Configure(
                go.transform.InverseTransformPoint(worldStart),
                go.transform.InverseTransformPoint(worldEnd),
                worldRadius / maxScale);
            return true;
        }

        private static Transform Bone(Animator animator, HumanBodyBones bone)
        {
            return animator.GetBoneTransform(bone);
        }

        private static float Distance(Transform a, Transform b)
        {
            return a != null && b != null ? Vector3.Distance(a.position, b.position) : 0f;
        }
    }
}
