using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    /// <summary>
    /// Gives every edited scene object its own mesh. Unity primitives, imported
    /// mesh assets, and duplicated objects all begin by sharing MeshFilter.sharedMesh;
    /// editing that object directly would otherwise modify every user of it.
    /// </summary>
    internal static class RealBlendEditableMeshUtility
    {
        internal static Mesh EnsureUniqueForEditing(
            MeshFilter meshFilter,
            ref VertexPaintStorage storage,
            string undoName,
            bool autoAddStorage,
            out bool cloned)
        {
            cloned = false;
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return null;
            if (EditorUtility.IsPersistent(meshFilter.gameObject))
                return null;

            GameObject owner = meshFilter.gameObject;
            storage = owner.GetComponent<VertexPaintStorage>();
            bool storageWasAdded = false;
            if (storage == null)
            {
                if (!autoAddStorage)
                    return meshFilter.sharedMesh;
                storage = Undo.AddComponent<VertexPaintStorage>(owner);
                storageWasAdded = true;
            }

            Mesh source = meshFilter.sharedMesh;
            bool mustClone = EditorUtility.IsPersistent(source) ||
                             IsReferencedByAnotherSceneObject(meshFilter, source);
            bool isProBuilderMesh =
                RealBlendProBuilderColorBridge.HasProBuilderMesh(meshFilter);
            bool proBuilderMadeUnique = false;

            if (mustClone && isProBuilderMesh)
            {
                // ProBuilder stores its own authoritative Mesh reference. A
                // RealBlend-only clone assigned to MeshFilter.sharedMesh would
                // be replaced on the next ProBuilder refresh or Undo. Ask
                // ProBuilder to make its mesh unique so both systems continue
                // referring to the same compiled object.
                if (!RealBlendProBuilderColorBridge.TryEnsureUniqueProBuilderMesh(
                        meshFilter,
                        undoName,
                        out Mesh proBuilderMesh,
                        out string error) ||
                    proBuilderMesh == null)
                {
                    Debug.LogWarning(
                        "RealBlend could not prepare a unique ProBuilder mesh, so the edit was not started.\n" +
                        error,
                        meshFilter);
                    return null;
                }

                proBuilderMadeUnique = proBuilderMesh != source;
                source = proBuilderMesh;
                mustClone = false;
            }

            if (!mustClone && storage.OwnedEditableMesh == source)
            {
                // The same Mesh object may have been changed by another editor
                // tool since RealBlend last saw it. Identity and equal counts do
                // not prove that the serialized positions/indices are current.
                // Reconcile once when acquiring the edit lease so later paint or
                // rebuild operations can never resurrect stale geometry.
                if (!storage.hasOriginalData ||
                    !storage.MatchesCurrentGeometry(source))
                {
                    // This is passive cache reconciliation for a mesh another
                    // editor already changed. Giving it an independent Undo
                    // record would let Ctrl+Z restore only stale storage while
                    // leaving the live mesh untouched, recreating the mismatch.
                    if (!storage.hasOriginalData)
                        storage.SetBaseTopology(source);
                    else
                        storage.CaptureCurrentGeometry(source);
                    EditorUtility.SetDirty(storage);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
                }
                return source;
            }

            Mesh editable = source;
            MeshCollider collider = null;
            if (mustClone)
            {
                // Register the clone before recording components that will point
                // at it. Redo replays operations in registration order; creating
                // the mesh last leaves those serialized references targeting a
                // destroyed object and the MeshFilter stays on its source mesh.
                editable = Object.Instantiate(source);
                editable.name = source.name + " (RealBlend Editable)";
                editable.hideFlags = source.hideFlags;
                Undo.RegisterCreatedObjectUndo(editable, undoName);

                Undo.RecordObject(meshFilter, undoName);
                collider = owner.GetComponent<MeshCollider>();
                if (collider != null)
                    Undo.RecordObject(collider, undoName);
            }

            // Existing storage that merely adopts an already-live unique mesh
            // is derived cache synchronization, not an independently undoable
            // edit. Recording it alone would let Ctrl+Z restore stale topology
            // while the MeshFilter remains unchanged. Clone/MakeUnique and a
            // newly added component are real operations and stay undoable.
            if (mustClone || proBuilderMadeUnique || storageWasAdded)
                Undo.RecordObject(storage, undoName);
            if (!storage.hasOriginalData)
                storage.SetBaseTopology(source);

            if (mustClone)
            {
                meshFilter.sharedMesh = editable;
                if (collider != null && collider.sharedMesh == source)
                {
                    collider.sharedMesh = null;
                    collider.sharedMesh = editable;
                }

                Color[] stored = storage.paintedColors;
                if (stored != null && stored.Length == editable.vertexCount)
                    editable.colors = stored;
                storage.SaveCurrentState(editable);
                cloned = true;
            }
            else if (proBuilderMadeUnique)
            {
                // MakeUnique can rebuild ProBuilder's compiled vertex order.
                // Adopt that exact output before the first RealBlend edit.
                storage.SaveCurrentState(editable);
                cloned = true;
            }
            else if (!storage.MatchesCurrentGeometry(editable))
            {
                // A unique, non-persistent mesh can still have been edited by a
                // third-party tool while the storage ownership reference was
                // missing (for example after a migration or domain reload).
                storage.CaptureCurrentGeometry(editable);
            }

            storage.SetOwnedEditableMesh(editable);

            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(storage);
            PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
            return editable;
        }

        private static bool IsReferencedByAnotherSceneObject(
            MeshFilter owner,
            Mesh mesh)
        {
            MeshFilter[] filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter candidate = filters[i];
                if (candidate == null || candidate == owner ||
                    candidate.sharedMesh != mesh ||
                    EditorUtility.IsPersistent(candidate.gameObject) ||
                    !candidate.gameObject.scene.IsValid() ||
                    !candidate.gameObject.scene.isLoaded)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
