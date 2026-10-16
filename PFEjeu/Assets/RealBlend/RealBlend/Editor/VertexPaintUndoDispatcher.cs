using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    [InitializeOnLoad]
    internal static class VertexPaintUndoDispatcher
    {
        internal const string UndoName = "RealBlend Vertex Paint Stroke";

        private sealed class DispatcherState
        {
            internal event System.Action<VertexPaintColorUndoState> ColorsRestored;
            internal int ActiveStrokeGroup = -1;
            internal VertexPaintColorUndoState ActiveStrokeState;

            internal void AddColorsRestoredListener(
                System.Action<VertexPaintColorUndoState> listener)
            {
                ColorsRestored -= listener;
                ColorsRestored += listener;
            }

            internal void RemoveColorsRestoredListener(
                System.Action<VertexPaintColorUndoState> listener)
            {
                ColorsRestored -= listener;
            }

            internal void NotifyColorsRestored(VertexPaintColorUndoState state)
            {
                ColorsRestored?.Invoke(state);
            }

            internal void ClearActiveStroke()
            {
                ActiveStrokeGroup = -1;
                ActiveStrokeState = null;
            }
        }

        private static readonly DispatcherState State = new DispatcherState();

        static VertexPaintUndoDispatcher()
        {
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedo;
        }

        [InitializeOnEnterPlayMode]
        private static void ResetActiveStrokeForPlayMode(
            EnterPlayModeOptions options)
        {
            State.ClearActiveStroke();
        }

        internal static void AddColorsRestoredListener(
            System.Action<VertexPaintColorUndoState> listener)
        {
            State.AddColorsRestoredListener(listener);
        }

        internal static void RemoveColorsRestoredListener(
            System.Action<VertexPaintColorUndoState> listener)
        {
            State.RemoveColorsRestoredListener(listener);
        }

        private static bool IsPaintStroke(string undoName)
        {
            return string.Equals(
                undoName,
                UndoName,
                System.StringComparison.Ordinal);
        }

        private static void OnUndoRedo(in UndoRedoInfo undo)
        {
            if (!IsVertexColorUndo(undo.undoName))
            {
                bool isGeometryUndo = IsGeometryUndo(undo.undoName);
                if (CanChangeStoredVertexData(
                        undo.undoName,
                        isGeometryUndo))
                {
                    SynchronizeCarriersFromStorage();
                }
                if (isGeometryUndo)
                    SceneView.RepaintAll();
                return;
            }

            VertexPaintColorUndoState forcedState =
                undo.undoGroup == State.ActiveStrokeGroup
                    ? State.ActiveStrokeState
                    : null;
            foreach (VertexPaintColorUndoState state in
                     VertexPaintColorUndoState.ActiveStates)
            {
                if (state == null ||
                    !state.gameObject.scene.IsValid() ||
                    !state.gameObject.scene.isLoaded)
                {
                    continue;
                }

                VertexPaintStorage storage = state.GetComponent<VertexPaintStorage>();
                MeshFilter filter = state.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Color[] colors = state.Colors;
                if (storage == null ||
                    mesh == null ||
                    colors == null ||
                    colors.Length != mesh.vertexCount)
                    continue;
                // Only the carrier in the Undo group changes revision. Other
                // targets may legitimately differ after Flood/Dirt/Sculpt and
                // must not be reverted by an unrelated paint Undo.
                if (storage.PaintedColorRevision == state.Revision &&
                    state != forcedState)
                    continue;

                mesh.colors = colors;
                mesh.UploadMeshData(false);
                storage.SetPaintedColorsForUndo(colors, state.Revision);
                RealBlendProBuilderColorBridge.SyncPaintedColorsToProBuilder(
                    filter,
                    storage,
                    colors);
                EditorUtility.SetDirty(storage);
                PrefabUtility.RecordPrefabInstancePropertyModifications(storage);

                State.NotifyColorsRestored(state);
            }

            if (forcedState != null)
                CompleteActiveStroke(undo.undoGroup);

            SceneView.RepaintAll();
        }

        private static void SynchronizeCarriersFromStorage()
        {
            foreach (VertexPaintColorUndoState state in
                     VertexPaintColorUndoState.ActiveStates)
            {
                if (state == null ||
                    !state.gameObject.scene.IsValid() ||
                    !state.gameObject.scene.isLoaded)
                {
                    continue;
                }

                VertexPaintStorage storage = state.GetComponent<VertexPaintStorage>();
                MeshFilter filter = state.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Color[] colors = storage != null &&
                                 storage.paintedColors != null &&
                                 mesh != null &&
                                 storage.paintedColors.Length == mesh.vertexCount
                    ? storage.paintedColors
                    : mesh != null ? mesh.colors : null;
                if (colors == null)
                    continue;

                state.Initialize(
                    colors,
                    storage != null ? storage.PaintedColorRevision : state.Revision);
                EditorUtility.SetDirty(state);
            }
        }

        private static bool CanChangeStoredVertexData(
            string undoName,
            bool isGeometryUndo)
        {
            if (string.IsNullOrEmpty(undoName))
                return false;

            return isGeometryUndo ||
                   undoName == "Flood Fill" ||
                   undoName == "Apply Dirt" ||
                   undoName == "Vertex Paint";
        }

        private static bool IsGeometryUndo(string undoName)
        {
            if (string.IsNullOrEmpty(undoName))
                return false;

            if (undoName == "Finalize Sculpt" ||
                undoName == "Sculpt Mesh" ||
                undoName == "Remove Triangles" ||
                undoName == "Remove With Shaped Boundary" ||
                undoName == "Cut Opening Through Wall" ||
                undoName == "Refine Mesh" ||
                undoName == "Revert Keep Paint" ||
                undoName == "RealBlend Boolean Cut" ||
                undoName == "RealBlend Boolean Cut Batch" ||
                undoName == "Recalculate Normals" ||
                undoName == "Repair RealBlend Mesh UVs" ||
                undoName == "Repair RealBlend Mesh UV Data" ||
                undoName == "Prepare Mesh for Normal Recalculation" ||
                undoName == "Prepare Mesh for UV Repair" ||
                undoName == "Prepare Unique Mesh for Painting" ||
                undoName == "Prepare Unique Mesh for Element Editing" ||
                undoName == "Prepare Unique Mesh for Sculpting" ||
                undoName == "Prepare Unique Mesh for Triangle Removal" ||
                undoName == "Prepare Unique Mesh for Refinement" ||
                undoName == "Prepare Unique Mesh for Topology Revert")
            {
                return true;
            }

            return IsElementTransformUndo(undoName);
        }

        private static bool IsElementTransformUndo(string undoName)
        {
            return undoName == "Move Vertex" ||
                   undoName == "Move Edge" ||
                   undoName == "Move Face" ||
                   undoName == "Rotate Vertex" ||
                   undoName == "Rotate Edge" ||
                   undoName == "Rotate Face" ||
                   undoName == "Scale Vertex" ||
                   undoName == "Scale Edge" ||
                   undoName == "Scale Face";
        }

        internal static bool IsVertexColorUndo(string undoName)
        {
            return IsPaintStroke(undoName) ||
                   string.Equals(
                       undoName,
                       VertexPainter_PaintTab.FloodFillUndoName,
                       System.StringComparison.Ordinal) ||
                   string.Equals(
                       undoName,
                       VertexPainterNoiseModule.DirtUndoName,
                       System.StringComparison.Ordinal);
        }

        internal static void TrackActiveStroke(
            int undoGroup,
            VertexPaintColorUndoState state)
        {
            State.ActiveStrokeGroup = state != null ? undoGroup : -1;
            State.ActiveStrokeState = state;
        }

        internal static void CompleteActiveStroke(int undoGroup)
        {
            if (undoGroup != State.ActiveStrokeGroup)
                return;

            State.ClearActiveStroke();
        }

    }
}
