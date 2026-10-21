using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Creates the single root GameObject a Humanoid Avatar needs above both the skinned mesh and
    /// the bone hierarchy (AvatarBuilder.BuildHumanAvatar walks one hierarchy from one root).
    /// Phase 2 built the bones as siblings of the mesh; this reparents both under a new common root,
    /// preserving every world transform so nothing visually moves.
    /// </summary>
    internal static class AvatarRootBuilder
    {
        private const string RootSuffix = "_Rig";
        private const string UndoLabel = "Build Avatar Root";

        public static GameObject CreateRoot(GameObject meshObject, Transform rootBone)
        {
            Transform originalParent = meshObject.transform.parent;

            var rootObject = new GameObject(meshObject.name + RootSuffix);
            Undo.RegisterCreatedObjectUndo(rootObject, UndoLabel);
            Undo.SetTransformParent(rootObject.transform, originalParent, UndoLabel);
            rootObject.transform.position = meshObject.transform.position;
            rootObject.transform.rotation = meshObject.transform.rotation;
            rootObject.transform.localScale = Vector3.one;

            // World-position-preserving reparent, exactly like dragging in the Hierarchy window.
            Undo.SetTransformParent(meshObject.transform, rootObject.transform, UndoLabel);
            Undo.SetTransformParent(rootBone, rootObject.transform, UndoLabel);

            return rootObject;
        }
    }
}
