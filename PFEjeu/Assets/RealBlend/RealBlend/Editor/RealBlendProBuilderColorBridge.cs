using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    /// <summary>
    /// Optional ProBuilder integration. Reflection keeps RealBlend free of a
    /// hard package dependency while still copying its painted colors into
    /// ProBuilder's authoritative vertex data before geometry edits.
    /// </summary>
    [InitializeOnLoad]
    internal static class RealBlendProBuilderColorBridge
    {
        private const string ProBuilderUndoName =
            "RealBlend ProBuilder Mesh Modification";

        private sealed class BridgeState
        {
            internal EventInfo beforeEvent;
            internal EventInfo afterEvent;
            internal EventInfo optimizedEvent;
            internal EventInfo selectionEvent;
            internal Delegate beforeHandler;
            internal Delegate afterHandler;
            internal Delegate optimizedHandler;
            internal Delegate selectionHandler;
            internal PropertyInfo proBuilderVersionProperty;
            internal readonly Dictionary<int, ulong> observedVersions =
                new Dictionary<int, ulong>();
            internal readonly Dictionary<int, object> pendingSelectionMeshes =
                new Dictionary<int, object>();
            internal readonly Dictionary<int, bool> pendingSelectionUndo =
                new Dictionary<int, bool>();
            internal int suppressAutomaticCaptureDepth;
            internal bool selectionCaptureScheduled;
            internal bool initialSynchronizationScheduled;

            private event Action<MeshFilter> MeshModified;

            internal void AddMeshModifiedListener(Action<MeshFilter> listener)
            {
                MeshModified -= listener;
                MeshModified += listener;
            }

            internal void RemoveMeshModifiedListener(Action<MeshFilter> listener)
            {
                MeshModified -= listener;
            }

            internal void NotifyMeshModified(MeshFilter filter)
            {
                MeshModified?.Invoke(filter);
            }
        }

        private static readonly BridgeState State = new BridgeState();

        static RealBlendProBuilderColorBridge()
        {
            EditorApplication.delayCall += TrySubscribe;
            AssemblyReloadEvents.beforeAssemblyReload += Unsubscribe;
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedo;
        }

        [InitializeOnEnterPlayMode]
        private static void ResetTransientStateForPlayMode(
            EnterPlayModeOptions options)
        {
            EditorApplication.delayCall -= ProcessPendingSelectionCapture;
            State.pendingSelectionMeshes.Clear();
            State.pendingSelectionUndo.Clear();
            State.observedVersions.Clear();
            State.selectionCaptureScheduled = false;
            State.suppressAutomaticCaptureDepth = 0;
        }

        internal static void AddMeshModifiedListener(Action<MeshFilter> listener)
        {
            State.AddMeshModifiedListener(listener);
        }

        internal static void RemoveMeshModifiedListener(Action<MeshFilter> listener)
        {
            State.RemoveMeshModifiedListener(listener);
        }

        private static void TrySubscribe()
        {
            if (State.beforeHandler != null || State.afterHandler != null ||
                State.optimizedHandler != null || State.selectionHandler != null)
                return;

            Type editorType = FindType(
                "UnityEditor.ProBuilder.ProBuilderEditor");
            Type proBuilderType = FindType(
                "UnityEngine.ProBuilder.ProBuilderMesh");
            if (editorType == null || proBuilderType == null)
                return;

            State.beforeEvent = editorType.GetEvent(
                "beforeMeshModification",
                BindingFlags.Public | BindingFlags.Static);
            State.afterEvent = editorType.GetEvent(
                "afterMeshModification",
                BindingFlags.Public | BindingFlags.Static);
            State.selectionEvent = editorType.GetEvent(
                "selectionUpdated",
                BindingFlags.Public | BindingFlags.Static);
            Type editorMeshUtilityType = FindType(
                "UnityEditor.ProBuilder.EditorMeshUtility");
            State.optimizedEvent = editorMeshUtilityType?.GetEvent(
                "meshOptimized",
                BindingFlags.Public | BindingFlags.Static);
            State.proBuilderVersionProperty = proBuilderType.GetProperty(
                "versionIndex",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            State.beforeHandler = AddForwarder(
                State.beforeEvent,
                HandleBeforeModification);
            State.optimizedHandler = AddTwoArgumentForwarder(
                State.optimizedEvent,
                HandleMeshOptimized);
            // meshOptimized covers handle edits and almost every ProBuilder
            // action. Older releases without that public event retain the
            // narrower afterMeshModification fallback.
            if (State.optimizedHandler == null)
            {
                State.afterHandler = AddForwarder(
                    State.afterEvent,
                    HandleAfterModification);
            }
            // A few actions (notably Extrude Edges in ProBuilder 6.1) rebuild
            // without Optimize. versionIndex lets this callback remain a cheap
            // action-completion fallback instead of copying meshes on every
            // selection refresh.
            if (State.proBuilderVersionProperty != null)
                State.selectionHandler = AddForwarder(
                    State.selectionEvent,
                    HandleSelectionUpdated);

            ScheduleInitialSynchronization();
        }

        private static Delegate AddForwarder(
            EventInfo eventInfo,
            Action<object> callback)
        {
            if (eventInfo == null || eventInfo.EventHandlerType == null)
                return null;

            MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke?.GetParameters();
            if (parameters == null || parameters.Length < 1)
                return null;

            ParameterExpression[] arguments =
                new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = Expression.Parameter(
                    parameters[i].ParameterType,
                    "argument" + i);
            }
            ConstantExpression target = Expression.Constant(callback);
            MethodInfo actionInvoke = typeof(Action<object>).GetMethod("Invoke");
            Delegate handler = Expression.Lambda(
                    eventInfo.EventHandlerType,
                    Expression.Call(
                        target,
                        actionInvoke,
                        Expression.Convert(arguments[0], typeof(object))),
                    arguments)
                .Compile();
            eventInfo.AddEventHandler(null, handler);
            return handler;
        }

        private static Delegate AddTwoArgumentForwarder(
            EventInfo eventInfo,
            Action<object, object> callback)
        {
            if (eventInfo == null || eventInfo.EventHandlerType == null)
                return null;

            MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke?.GetParameters();
            if (parameters == null || parameters.Length != 2)
                return null;

            ParameterExpression first = Expression.Parameter(
                parameters[0].ParameterType,
                "mesh");
            ParameterExpression second = Expression.Parameter(
                parameters[1].ParameterType,
                "compiledMesh");
            ConstantExpression target = Expression.Constant(callback);
            MethodInfo actionInvoke =
                typeof(Action<object, object>).GetMethod("Invoke");
            Delegate handler = Expression.Lambda(
                    eventInfo.EventHandlerType,
                    Expression.Call(
                        target,
                        actionInvoke,
                        Expression.Convert(first, typeof(object)),
                        Expression.Convert(second, typeof(object))),
                    first,
                    second)
                .Compile();
            eventInfo.AddEventHandler(null, handler);
            return handler;
        }

        private static void HandleMeshOptimized(
            object meshObject,
            object compiledMeshObject)
        {
            if (State.suppressAutomaticCaptureDepth > 0 || Undo.isProcessing)
                return;

            Component component = meshObject as Component;
            if (!IsLoadedSceneComponent(component) ||
                component.GetComponent<VertexPaintStorage>() == null)
            {
                return;
            }

            CaptureAfterModification(
                new object[] { meshObject },
                true,
                component,
                compiledMeshObject as Mesh);
        }

        private static void HandleSelectionUpdated(object meshes)
        {
            if (State.suppressAutomaticCaptureDepth > 0 || Undo.isProcessing)
                return;

            foreach (object meshObject in Enumerate(meshes))
            {
                Component component = meshObject as Component;
                VertexPaintStorage storage = IsLoadedSceneComponent(component)
                    ? component.GetComponent<VertexPaintStorage>()
                    : null;
                if (storage == null ||
                    !TryReadProBuilderVersion(meshObject, out ulong version))
                {
                    continue;
                }

                int id = GetObjectKey(component);
                if (!State.observedVersions.TryGetValue(id, out ulong observed))
                {
                    MeshFilter filter = component.GetComponent<MeshFilter>();
                    Mesh compiledMesh = filter != null ? filter.sharedMesh : null;
                    if (compiledMesh != null && compiledMesh.isReadable &&
                        storage.MatchesCurrentState(compiledMesh))
                    {
                        // A first selection refresh with current storage is not
                        // an edit. Prime the sentinel without dirtying anything.
                        State.observedVersions[id] = version;
                        continue;
                    }

                    // Storage can be added after the bridge's initial scan. If
                    // the first observed version already differs from storage,
                    // capture it as a baseline. Do not manufacture a separate
                    // Undo item just for selecting a newly prepared object.
                    State.pendingSelectionMeshes[id] = meshObject;
                    State.pendingSelectionUndo[id] = false;
                    continue;
                }
                if (observed == version)
                {
                    continue;
                }
                State.pendingSelectionMeshes[id] = meshObject;
                State.pendingSelectionUndo[id] = true;
            }

            if (State.pendingSelectionMeshes.Count > 0)
                ScheduleSelectionCapture();
        }

        private static void ScheduleSelectionCapture()
        {
            if (State.selectionCaptureScheduled)
                return;
            State.selectionCaptureScheduled = true;
            EditorApplication.delayCall += ProcessPendingSelectionCapture;
        }

        private static void ProcessPendingSelectionCapture()
        {
            State.selectionCaptureScheduled = false;
            if (State.pendingSelectionMeshes.Count == 0)
                return;

            // Do not snapshot an intermediate mesh while an editor handle is
            // still active. The optimized callback handles ordinary drags;
            // this queue exists for completed actions that skip Optimize.
            if (State.suppressAutomaticCaptureDepth > 0 ||
                Undo.isProcessing ||
                GUIUtility.hotControl != 0)
            {
                ScheduleSelectionCapture();
                return;
            }

            List<object> changedWithUndo = new List<object>();
            List<object> changedWithoutUndo = new List<object>();
            foreach (KeyValuePair<int, object> pair in
                     State.pendingSelectionMeshes)
            {
                object meshObject = pair.Value;
                Component component = meshObject as Component;
                if (!IsLoadedSceneComponent(component) ||
                    component.GetComponent<VertexPaintStorage>() == null ||
                    !TryReadProBuilderVersion(meshObject, out ulong version))
                {
                    continue;
                }
                if (State.observedVersions.TryGetValue(
                        pair.Key,
                        out ulong observed) &&
                    observed == version)
                {
                    continue;
                }
                bool recordUndo = State.pendingSelectionUndo.TryGetValue(
                    pair.Key,
                    out bool pendingUndo) && pendingUndo;
                (recordUndo ? changedWithUndo : changedWithoutUndo).Add(meshObject);
            }
            State.pendingSelectionMeshes.Clear();
            State.pendingSelectionUndo.Clear();

            if (changedWithoutUndo.Count > 0)
                CaptureAfterModification(changedWithoutUndo, false);
            if (changedWithUndo.Count > 0)
                CaptureAfterModification(changedWithUndo, true);
        }

        private static bool TryReadProBuilderVersion(
            object meshObject,
            out ulong version)
        {
            version = 0;
            if (meshObject == null || State.proBuilderVersionProperty == null)
                return false;
            try
            {
                object value = State.proBuilderVersionProperty.GetValue(meshObject);
                if (value == null)
                    return false;
                version = Convert.ToUInt64(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void RememberObservedVersion(object meshObject)
        {
            Component component = meshObject as Component;
            if (component == null ||
                !TryReadProBuilderVersion(meshObject, out ulong version))
            {
                return;
            }
            State.observedVersions[GetObjectKey(component)] = version;
        }

        private static void ScheduleInitialSynchronization()
        {
            if (State.initialSynchronizationScheduled)
                return;
            State.initialSynchronizationScheduled = true;
            EditorApplication.delayCall += RunInitialSynchronization;
        }

        private static void RunInitialSynchronization()
        {
            State.initialSynchronizationScheduled = false;
            SynchronizeAllProBuilderStorages(true);
        }

        private static void HandleBeforeModification(object meshes)
        {
            if (Undo.isProcessing)
                return;

            foreach (object meshObject in Enumerate(meshes))
            {
                try
                {
                    Component component = meshObject as Component;
                    if (!IsLoadedSceneComponent(component))
                        continue;
                    VertexPaintStorage storage =
                        component.GetComponent<VertexPaintStorage>();
                    MeshFilter filter = component.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || storage == null)
                        continue;

                    RecordCompanionUndo(component, storage);
                    Color[] colors = storage != null ? storage.paintedColors : null;
                    if (colors == null ||
                        colors.Length != mesh.vertexCount)
                    {
                        continue;
                    }

                    if (WriteProBuilderColors(meshObject, storage, colors))
                        MarkProBuilderComponentDirty(component);
                    if (!ColorSequencesMatch(mesh.colors, colors))
                        mesh.colors = colors;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "RealBlend could not synchronize vertex colors before " +
                        "the ProBuilder edit. The edit will continue safely.\n" +
                        exception.Message);
                }
            }
        }

        private static void HandleAfterModification(object meshes)
        {
            if (Undo.isProcessing)
                return;
            CaptureAfterModification(meshes, false);
        }

        private static void CaptureAfterModification(
            object meshes,
            bool recordCompanionUndo,
            Component preferredOwner = null,
            Mesh preferredMesh = null,
            bool persistOnlyWhenChanged = false)
        {
            foreach (object meshObject in Enumerate(meshes))
            {
                try
                {
                    Component component = meshObject as Component;
                    if (!IsLoadedSceneComponent(component))
                        continue;
                    MeshFilter filter = component.GetComponent<MeshFilter>();
                    Mesh mesh = component == preferredOwner && preferredMesh != null
                        ? preferredMesh
                        : filter != null ? filter.sharedMesh : null;
                    if (mesh == null)
                        continue;

                    VertexPaintStorage storage =
                        component.GetComponent<VertexPaintStorage>();
                    if (storage == null)
                        continue;

                    if (recordCompanionUndo)
                        RecordCompanionUndo(component, storage);

                    Vector3[] meshPositions = mesh.vertices;
                    Color[] meshColors = mesh.colors;
                    Color[] colors;
                    if (meshColors != null &&
                        meshColors.Length == mesh.vertexCount)
                    {
                        // meshOptimized supplies ProBuilder's exact compiled
                        // vertex order. Prefer it over a position remap so
                        // coincident split vertices can retain distinct colors.
                        colors = meshColors;
                    }
                    else
                    {
                        Color[] fallback = RemapStoredColors(
                            storage,
                            meshPositions,
                            CreateClearColors(mesh.vertexCount));
                        colors = ReadAndRemapProBuilderColors(
                            meshObject,
                            meshPositions,
                            fallback);
                        if (colors == null || colors.Length != mesh.vertexCount)
                            colors = fallback;
                    }

                    if (!ColorSequencesMatch(meshColors, colors))
                        mesh.colors = colors;
                    bool storageChanged = !persistOnlyWhenChanged ||
                                          !storage.MatchesCurrentState(mesh);
                    if (storageChanged)
                    {
                        storage.SetOwnedEditableMesh(mesh);
                        storage.SaveCurrentState(mesh);
                        EditorUtility.SetDirty(storage);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
                    }

                    MeshCollider collider = component.GetComponent<MeshCollider>();
                    bool colliderChanged =
                        collider != null && collider.sharedMesh != mesh;

                    if (collider != null && collider.sharedMesh != mesh)
                    {
                        collider.sharedMesh = null;
                        collider.sharedMesh = mesh;
                    }
                    if (collider != null &&
                        (!persistOnlyWhenChanged || colliderChanged))
                    {
                        EditorUtility.SetDirty(collider);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(
                            collider);
                    }

                    RememberObservedVersion(meshObject);
                    State.NotifyMeshModified(filter);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "RealBlend could not persist vertex colors after the " +
                        "ProBuilder edit. The mesh edit itself was not blocked.\n" +
                        exception.Message);
                }
            }

            SceneView.RepaintAll();
        }

        private static Color[] ReadAndRemapProBuilderColors(
            object meshObject,
            Vector3[] destinationPositions,
            Color[] fallback)
        {
            if (meshObject == null || destinationPositions == null)
                return null;

            PropertyInfo colorProperty = meshObject.GetType().GetProperty(
                "colors",
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo positionProperty = meshObject.GetType().GetProperty(
                "positions",
                BindingFlags.Public | BindingFlags.Instance);
            List<Color> colors = ReadColorList(
                colorProperty?.GetValue(meshObject) as IEnumerable);
            List<Vector3> positions = ReadVector3List(
                positionProperty?.GetValue(meshObject) as IEnumerable);
            if (colors.Count == 0 || colors.Count != positions.Count)
                return null;

            Vector3[] sourcePositions = positions.ToArray();
            Color[] sourceColors = colors.ToArray();
            if (PositionSequencesMatch(sourcePositions, destinationPositions))
                return sourceColors;

            return RemapColors(
                sourcePositions,
                sourceColors,
                destinationPositions,
                fallback);
        }

        internal static void SyncPaintedColorsToProBuilder(
            MeshFilter filter,
            VertexPaintStorage storage,
            Color[] colors)
        {
            if (filter == null || storage == null || colors == null)
                return;

            if (!TryGetProBuilderComponent(filter, out Component component))
                return;

            try
            {
                if (WriteProBuilderColors(component, storage, colors))
                    MarkProBuilderComponentDirty(component);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "RealBlend could not synchronize its painted colors " +
                    "with ProBuilder.\n" + exception.Message);
            }
        }

        internal static bool HasProBuilderMesh(MeshFilter filter)
        {
            return TryGetProBuilderComponent(filter, out _);
        }

        internal static bool TrySyncFromProBuilder(
            MeshFilter filter,
            VertexPaintStorage storage,
            out string error)
        {
            error = null;
            if (filter == null || storage == null ||
                !TryGetProBuilderComponent(filter, out Component component))
            {
                error = "The selected object does not contain a ProBuilder mesh.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Sync ProBuilder to RealBlend");
            Undo.RegisterCompleteObjectUndo(component, "Sync ProBuilder to RealBlend");
            Undo.RegisterCompleteObjectUndo(filter, "Sync ProBuilder to RealBlend");
            RecordCompanionUndo(component, storage);

            bool rebuilt;
            State.suppressAutomaticCaptureDepth++;
            try
            {
                rebuilt = TryRebuildProBuilderOutput(component);
            }
            finally
            {
                State.suppressAutomaticCaptureDepth--;
            }

            Mesh compiledMesh = filter.sharedMesh;
            if (compiledMesh == null)
            {
                error = rebuilt
                    ? "ProBuilder rebuilt the object but did not provide a compiled mesh."
                    : "ProBuilder could not provide a compiled mesh to synchronize.";
                Undo.CollapseUndoOperations(undoGroup);
                return false;
            }

            CaptureAfterModification(
                new object[] { component },
                false,
                component,
                compiledMesh);
            Undo.CollapseUndoOperations(undoGroup);
            return true;
        }

        internal static bool TrySendPaintToProBuilder(
            MeshFilter filter,
            VertexPaintStorage storage,
            out string error)
        {
            error = null;
            if (filter == null || storage == null ||
                !TryGetProBuilderComponent(filter, out Component component))
            {
                error = "The selected object does not contain a ProBuilder mesh.";
                return false;
            }

            Color[] storedColors = storage.paintedColors;
            Mesh compiledMesh = filter.sharedMesh;
            if (compiledMesh == null || storedColors == null ||
                storedColors.Length == 0)
            {
                error = "RealBlend has no stored paint to send.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Send RealBlend Paint to ProBuilder");
            Undo.RegisterCompleteObjectUndo(
                component,
                "Send RealBlend Paint to ProBuilder");
            Undo.RegisterCompleteObjectUndo(
                compiledMesh,
                "Send RealBlend Paint to ProBuilder");

            try
            {
                Vector3[] destinationPositions = compiledMesh.vertices;
                Color[] currentColors = compiledMesh.colors;
                Color[] fallback = currentColors != null &&
                                   currentColors.Length == compiledMesh.vertexCount
                    ? currentColors
                    : CreateClearColors(compiledMesh.vertexCount);
                Color[] compiledColors = RemapStoredColors(
                    storage,
                    destinationPositions,
                    fallback,
                    out bool compiledMappingComplete);
                if (!compiledMappingComplete ||
                    compiledColors.Length != compiledMesh.vertexCount)
                {
                    error = "The stored paint did not match the compiled ProBuilder mesh.";
                    Undo.CollapseUndoOperations(undoGroup);
                    return false;
                }

                if (!WriteProBuilderColors(component, storage, storedColors))
                {
                    error =
                        "The stored paint could not be mapped onto ProBuilder's current vertices. " +
                        "Use 'Sync From ProBuilder' first if its topology changed.";
                    Undo.CollapseUndoOperations(undoGroup);
                    return false;
                }

                compiledMesh.colors = compiledColors;
                compiledMesh.UploadMeshData(false);
                MarkProBuilderComponentDirty(component);
                EditorUtility.SetDirty(compiledMesh);
                RememberObservedVersion(component);
                State.NotifyMeshModified(filter);
                SceneView.RepaintAll();
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Undo.CollapseUndoOperations(undoGroup);
                return false;
            }
        }

        internal static bool TryEnsureUniqueProBuilderMesh(
            MeshFilter filter,
            string undoName,
            out Mesh uniqueMesh,
            out string error)
        {
            uniqueMesh = filter != null ? filter.sharedMesh : null;
            error = null;
            if (filter == null ||
                !TryGetProBuilderComponent(filter, out Component component))
            {
                error = "No ProBuilder mesh was found.";
                return false;
            }

            MethodInfo makeUnique = component.GetType().GetMethod(
                "MakeUnique",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (makeUnique == null)
            {
                error = "This ProBuilder version does not expose MakeUnique.";
                return false;
            }

            string operation = string.IsNullOrEmpty(undoName)
                ? "Prepare Unique ProBuilder Mesh"
                : undoName;
            Mesh previousMesh = filter.sharedMesh;
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            Undo.RegisterCompleteObjectUndo(component, operation);
            Undo.RecordObject(filter, operation);
            if (collider != null)
                Undo.RecordObject(collider, operation);

            State.suppressAutomaticCaptureDepth++;
            try
            {
                makeUnique.Invoke(component, null);
            }
            catch (TargetInvocationException exception)
            {
                error = exception.InnerException?.Message ?? exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                State.suppressAutomaticCaptureDepth--;
            }

            uniqueMesh = filter.sharedMesh;
            if (uniqueMesh == null)
            {
                error = "ProBuilder did not create a unique compiled mesh.";
                return false;
            }
            if (collider != null && collider.sharedMesh == previousMesh)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = uniqueMesh;
                EditorUtility.SetDirty(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            MarkProBuilderComponentDirty(component);
            EditorUtility.SetDirty(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            RememberObservedVersion(component);
            return true;
        }

        private static bool TryGetProBuilderComponent(
            MeshFilter filter,
            out Component proBuilderComponent)
        {
            proBuilderComponent = null;
            if (filter == null)
                return false;

            Component[] components = filter.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null &&
                    component.GetType().FullName ==
                    "UnityEngine.ProBuilder.ProBuilderMesh")
                {
                    proBuilderComponent = component;
                    return true;
                }
            }
            return false;
        }

        private static bool WriteProBuilderColors(
            object meshObject,
            VertexPaintStorage storage,
            Color[] colors)
        {
            if (meshObject == null || colors == null)
                return false;

            PropertyInfo colorProperty = meshObject.GetType().GetProperty(
                "colors",
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo positionProperty = meshObject.GetType().GetProperty(
                "positions",
                BindingFlags.Public | BindingFlags.Instance);
            if (colorProperty == null || !colorProperty.CanWrite)
                return false;

            List<Vector3> positions = ReadVector3List(
                positionProperty?.GetValue(meshObject) as IEnumerable);
            if (positions.Count == 0)
                return false;

            Vector3[] sourcePositions = ResolveColorSourcePositions(
                meshObject as Component,
                storage,
                colors.Length);
            Vector3[] destinationPositions = positions.ToArray();
            List<Color> existingColors = ReadColorList(
                colorProperty.GetValue(meshObject) as IEnumerable);
            Color[] mappingFallback = existingColors.Count == destinationPositions.Length
                ? existingColors.ToArray()
                : CreateClearColors(destinationPositions.Length);
            Color[] mapped;
            if (sourcePositions != null &&
                PositionSequencesMatch(sourcePositions, destinationPositions))
            {
                mapped = (Color[])colors.Clone();
            }
            else if (sourcePositions != null)
            {
                mapped = RemapColors(
                    sourcePositions,
                    colors,
                    destinationPositions,
                    mappingFallback,
                    out bool mappingComplete);
                if (!mappingComplete)
                    return false;
            }
            else if (positions.Count == colors.Length)
            {
                // Older ProBuilder versions may expose colors without readable
                // source positions. Equal counts are the only safe fallback.
                mapped = (Color[])colors.Clone();
            }
            else
            {
                return false;
            }

            if (mapped.Length != positions.Count)
                return false;

            colorProperty.SetValue(meshObject, mapped);
            return true;
        }

        private static Vector3[] ResolveColorSourcePositions(
            Component component,
            VertexPaintStorage storage,
            int colorCount)
        {
            Vector3[] storedPositions = storage != null
                ? storage.currentVertices
                : null;
            MeshFilter filter = component != null
                ? component.GetComponent<MeshFilter>()
                : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            bool hasLivePositions = mesh != null &&
                                    mesh.isReadable &&
                                    mesh.vertexCount == colorCount;
            Vector3[] livePositions = hasLivePositions ? mesh.vertices : null;
            if (storedPositions != null &&
                storedPositions.Length == colorCount)
            {
                // The supplied colors are the storage paint snapshot (or were
                // just saved into it). If ProBuilder only deformed vertices and
                // kept the index layout, per-index color identity is unchanged
                // and colors should follow the live positions. If it reordered
                // topology, keep colors paired with their stored positions so
                // the spatial remap remains valid.
                if (livePositions != null &&
                    !PositionSequencesMatch(storedPositions, livePositions) &&
                    storage.MatchesCurrentIndexLayout(mesh))
                {
                    return livePositions;
                }
                return storedPositions;
            }
            if (livePositions != null)
            {
                // Legacy storage can have colors without a position snapshot.
                // In that case an equal-count live sequence is the only usable
                // fallback.
                return livePositions;
            }
            return null;
        }

        private static List<Vector3> ReadVector3List(IEnumerable values)
        {
            List<Vector3> positions = new List<Vector3>();
            if (values == null)
                return positions;
            foreach (object value in values)
            {
                if (value is Vector3 position)
                    positions.Add(position);
            }
            return positions;
        }

        private static List<Color> ReadColorList(IEnumerable values)
        {
            List<Color> colors = new List<Color>();
            if (values == null)
                return colors;
            foreach (object value in values)
            {
                if (value is Color color)
                    colors.Add(color);
                else if (value is Color32 color32)
                    colors.Add(color32);
            }
            return colors;
        }

        private static Color[] RemapStoredColors(
            VertexPaintStorage storage,
            Vector3[] destinationPositions,
            Color[] fallback)
        {
            return RemapStoredColors(
                storage,
                destinationPositions,
                fallback,
                out _);
        }

        private static Color[] RemapStoredColors(
            VertexPaintStorage storage,
            Vector3[] destinationPositions,
            Color[] fallback,
            out bool mappingComplete)
        {
            Vector3[] sourcePositions = storage != null
                ? storage.currentVertices
                : null;
            Color[] sourceColors = storage != null
                ? storage.paintedColors
                : null;

            // ProBuilder commonly compiles several distinct vertices at the
            // same position (UV seams, hard edges, and material boundaries).
            // When storage already describes the exact compiled sequence,
            // preserve its per-index colors instead of spatially remapping and
            // ambiguously choosing between coincident vertices.
            if (sourcePositions != null && sourceColors != null &&
                sourcePositions.Length == sourceColors.Length &&
                PositionSequencesMatch(sourcePositions, destinationPositions))
            {
                mappingComplete = true;
                return (Color[])sourceColors.Clone();
            }

            return RemapColors(
                sourcePositions,
                sourceColors,
                destinationPositions,
                fallback,
                out mappingComplete);
        }

        private static Color[] RemapColors(
            Vector3[] sourcePositions,
            Color[] sourceColors,
            Vector3[] destinationPositions,
            Color[] fallback)
        {
            return RemapColors(
                sourcePositions,
                sourceColors,
                destinationPositions,
                fallback,
                out _);
        }

        private static Color[] RemapColors(
            Vector3[] sourcePositions,
            Color[] sourceColors,
            Vector3[] destinationPositions,
            Color[] fallback,
            out bool mappingComplete)
        {
            mappingComplete = false;
            if (destinationPositions == null)
                return Array.Empty<Color>();
            if (sourcePositions == null || sourceColors == null ||
                sourcePositions.Length != sourceColors.Length ||
                sourcePositions.Length == 0)
            {
                return fallback ?? CreateClearColors(destinationPositions.Length);
            }

            Bounds bounds = new Bounds(sourcePositions[0], Vector3.zero);
            for (int i = 1; i < sourcePositions.Length; i++)
                bounds.Encapsulate(sourcePositions[i]);
            float cellSize = Mathf.Max(0.000001f, bounds.size.magnitude / 2048f);
            Dictionary<Vector3Int, List<int>> buckets =
                new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < sourcePositions.Length; i++)
            {
                Vector3Int key = ColorBucket(sourcePositions[i], cellSize);
                if (!buckets.TryGetValue(key, out List<int> indices))
                {
                    indices = new List<int>();
                    buckets.Add(key, indices);
                }
                indices.Add(i);
            }

            Color[] result = fallback != null &&
                             fallback.Length == destinationPositions.Length
                ? (Color[])fallback.Clone()
                : CreateClearColors(destinationPositions.Length);
            mappingComplete = true;
            for (int i = 0; i < destinationPositions.Length; i++)
            {
                Vector3 position = destinationPositions[i];
                Vector3Int center = ColorBucket(position, cellSize);
                float bestDistance = float.PositiveInfinity;
                float bestFallbackColorDistance = float.PositiveInfinity;
                int bestIndex = -1;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int key = center + new Vector3Int(x, y, z);
                    if (!buckets.TryGetValue(key, out List<int> candidates))
                        continue;
                    for (int candidateIndex = 0;
                         candidateIndex < candidates.Count;
                         candidateIndex++)
                    {
                        int sourceIndex = candidates[candidateIndex];
                        float distance =
                            (sourcePositions[sourceIndex] - position).sqrMagnitude;
                        float fallbackColorDistance = ColorDistanceSquared(
                            sourceColors[sourceIndex],
                            result[i]);
                        if (distance < bestDistance - 0.000000000001f ||
                            Mathf.Abs(distance - bestDistance) <= 0.000000000001f &&
                            fallbackColorDistance < bestFallbackColorDistance)
                        {
                            bestDistance = distance;
                            bestFallbackColorDistance = fallbackColorDistance;
                            bestIndex = sourceIndex;
                        }
                    }
                }
                if (bestIndex >= 0)
                    result[i] = sourceColors[bestIndex];
                else
                    mappingComplete = false;
            }
            return result;
        }

        private static bool PositionSequencesMatch(
            Vector3[] left,
            Vector3[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if ((left[i] - right[i]).sqrMagnitude > 0.000000000001f)
                    return false;
            }
            return true;
        }

        private static bool ColorSequencesMatch(Color[] left, Color[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static float ColorDistanceSquared(Color left, Color right)
        {
            float r = left.r - right.r;
            float g = left.g - right.g;
            float b = left.b - right.b;
            float a = left.a - right.a;
            return r * r + g * g + b * b + a * a;
        }

        private static Vector3Int ColorBucket(Vector3 position, float cellSize)
        {
            return new Vector3Int(
                Mathf.RoundToInt(position.x / cellSize),
                Mathf.RoundToInt(position.y / cellSize),
                Mathf.RoundToInt(position.z / cellSize));
        }

        private static Color[] CreateClearColors(int count)
        {
            return new Color[Mathf.Max(0, count)];
        }

        private static void RecordCompanionUndo(
            Component component,
            VertexPaintStorage storage)
        {
            if (storage != null)
                Undo.RegisterCompleteObjectUndo(storage, ProBuilderUndoName);

            MeshCollider collider = component != null
                ? component.GetComponent<MeshCollider>()
                : null;
            if (collider != null)
                Undo.RegisterCompleteObjectUndo(collider, ProBuilderUndoName);
        }

        private static void MarkProBuilderComponentDirty(Component component)
        {
            if (component == null)
                return;
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        private static bool IsLoadedSceneComponent(Component component)
        {
            if (component == null ||
                component.gameObject == null ||
                EditorUtility.IsPersistent(component) ||
                EditorUtility.IsPersistent(component.gameObject))
            {
                return false;
            }

            return component.gameObject.scene.IsValid() &&
                   component.gameObject.scene.isLoaded;
        }

        private static int GetObjectKey(UnityEngine.Object value)
        {
            if (value == null)
                return 0;
#if UNITY_6000_2_OR_NEWER
            return value.GetEntityId().GetHashCode();
#else
            return value.GetInstanceID();
#endif
        }

        private static IEnumerable Enumerate(object value)
        {
            return value as IEnumerable ?? Array.Empty<object>();
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static void Unsubscribe()
        {
            if (State.beforeEvent != null && State.beforeHandler != null)
                State.beforeEvent.RemoveEventHandler(null, State.beforeHandler);
            if (State.afterEvent != null && State.afterHandler != null)
                State.afterEvent.RemoveEventHandler(null, State.afterHandler);
            if (State.optimizedEvent != null && State.optimizedHandler != null)
            {
                State.optimizedEvent.RemoveEventHandler(
                    null,
                    State.optimizedHandler);
            }
            if (State.selectionEvent != null && State.selectionHandler != null)
            {
                State.selectionEvent.RemoveEventHandler(
                    null,
                    State.selectionHandler);
            }
            State.beforeHandler = null;
            State.afterHandler = null;
            State.optimizedHandler = null;
            State.selectionHandler = null;
            State.beforeEvent = null;
            State.afterEvent = null;
            State.optimizedEvent = null;
            State.selectionEvent = null;
            State.proBuilderVersionProperty = null;
            State.observedVersions.Clear();
            State.pendingSelectionMeshes.Clear();
            State.pendingSelectionUndo.Clear();
            EditorApplication.delayCall -= ProcessPendingSelectionCapture;
            EditorApplication.delayCall -= RunInitialSynchronization;
            EditorApplication.delayCall -= SynchronizeAllProBuilderStorages;
            State.selectionCaptureScheduled = false;
            State.initialSynchronizationScheduled = false;
            Undo.undoRedoEvent -= OnUndoRedo;
        }

        private static void OnUndoRedo(in UndoRedoInfo info)
        {
            EditorApplication.delayCall -= SynchronizeAllProBuilderStorages;
            EditorApplication.delayCall += SynchronizeAllProBuilderStorages;
        }

        private static void SynchronizeAllProBuilderStorages()
        {
            SynchronizeAllProBuilderStorages(false);
        }

        private static void SynchronizeAllProBuilderStorages(
            bool primeMissingColors)
        {
            Type proBuilderType = FindType(
                "UnityEngine.ProBuilder.ProBuilderMesh");
            if (proBuilderType == null)
                return;

            UnityEngine.Object[] objects =
                Resources.FindObjectsOfTypeAll(proBuilderType);
            List<object> sceneMeshes = new List<object>();
            State.suppressAutomaticCaptureDepth++;
            try
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    Component component = objects[i] as Component;
                    if (!IsLoadedSceneComponent(component) ||
                        component.GetComponent<VertexPaintStorage>() == null)
                    {
                        continue;
                    }

                    if (primeMissingColors)
                        PrimeMissingProBuilderColors(component);

                    if (!TryRebuildProBuilderOutput(objects[i]))
                    {
                        MeshFilter fallbackFilter =
                            component.GetComponent<MeshFilter>();
                        if (fallbackFilter == null ||
                            fallbackFilter.sharedMesh == null)
                        {
                            continue;
                        }
                    }
                    sceneMeshes.Add(objects[i]);
                }
            }
            finally
            {
                State.suppressAutomaticCaptureDepth--;
            }

            CaptureAfterModification(
                sceneMeshes,
                false,
                null,
                null,
                true);
        }

        private static void PrimeMissingProBuilderColors(Component component)
        {
            if (component == null)
                return;

            try
            {
                Type type = component.GetType();
                PropertyInfo colorProperty = type.GetProperty(
                    "colors",
                    BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo positionProperty = type.GetProperty(
                    "positions",
                    BindingFlags.Public | BindingFlags.Instance);
                List<Vector3> positions = ReadVector3List(
                    positionProperty?.GetValue(component) as IEnumerable);
                List<Color> colors = ReadColorList(
                    colorProperty?.GetValue(component) as IEnumerable);
                if (positions.Count == 0 || colors.Count == positions.Count)
                    return;

                VertexPaintStorage storage =
                    component.GetComponent<VertexPaintStorage>();
                if (storage?.paintedColors == null ||
                    storage.paintedColors.Length == 0)
                {
                    return;
                }

                if (WriteProBuilderColors(
                        component,
                        storage,
                        storage.paintedColors))
                {
                    MarkProBuilderComponentDirty(component);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "RealBlend could not initialize missing ProBuilder vertex colors.\n" +
                    exception.Message,
                    component);
            }
        }

        private static bool TryRebuildProBuilderOutput(object meshObject)
        {
            if (meshObject == null)
                return false;

            try
            {
                Type type = meshObject.GetType();
                IDisposable editScope = CreateNonVersionedEditScope(
                    type,
                    meshObject);
                try
                {
                    if (TrySynchronizeWithProBuilderEditor(meshObject, type))
                        return true;

                    MethodInfo toMesh = FindOptionalInstanceMethod(
                        type,
                        "ToMesh");
                    if (toMesh == null)
                        return false;
                    InvokeOptionalInstanceMethod(toMesh, meshObject);

                    MethodInfo refresh = FindOptionalInstanceMethod(
                        type,
                        "Refresh");
                    if (refresh != null)
                        InvokeOptionalInstanceMethod(refresh, meshObject);
                    return true;
                }
                finally
                {
                    editScope?.Dispose();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "RealBlend could not refresh a ProBuilder mesh, so its " +
                    "stored paint data was left unchanged.\n" +
                    exception.Message);
                return false;
            }
        }

        private static IDisposable CreateNonVersionedEditScope(
            Type proBuilderType,
            object meshObject)
        {
            Type scopeType = proBuilderType.GetNestedType(
                "NonVersionedEditScope",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (scopeType == null)
                return null;

            ConstructorInfo constructor = scopeType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance,
                null,
                new[] { proBuilderType },
                null);
            return constructor?.Invoke(new[] { meshObject }) as IDisposable;
        }

        private static bool TrySynchronizeWithProBuilderEditor(
            object meshObject,
            Type proBuilderType)
        {
            Type editorUtilityType = FindType(
                "UnityEditor.ProBuilder.EditorUtility");
            if (editorUtilityType == null)
                return false;

            MethodInfo synchronize = editorUtilityType.GetMethod(
                "SynchronizeWithMeshFilter",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { proBuilderType },
                null);
            if (synchronize == null)
                return false;

            synchronize.Invoke(null, new[] { meshObject });
            return true;
        }

        private static MethodInfo FindOptionalInstanceMethod(
            Type type,
            string methodName)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != methodName)
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 ||
                    (parameters.Length == 1 && parameters[0].IsOptional))
                {
                    return method;
                }
            }
            return null;
        }

        private static void InvokeOptionalInstanceMethod(
            MethodInfo method,
            object target)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                method.Invoke(target, null);
                return;
            }

            object defaultValue = parameters[0].DefaultValue;
            if (defaultValue == DBNull.Value || defaultValue == Type.Missing)
                defaultValue = Activator.CreateInstance(parameters[0].ParameterType);
            method.Invoke(target, new[] { defaultValue });
        }
    }
}
