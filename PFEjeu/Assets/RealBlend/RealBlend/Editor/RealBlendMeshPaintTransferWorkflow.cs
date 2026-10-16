using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RealBlend
{
    public sealed class PaintTransferSourceSnapshot : IDisposable
    {
        public MeshFilter Source { get; internal set; }
        public Mesh Mesh { get; internal set; }
        public string Fingerprint { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
        public void Dispose() { if (Mesh != null) Object.DestroyImmediate(Mesh); Mesh = null; }
    }

    public sealed class MeshPaintTransferPreview : IDisposable
    {
        public PaintTransferSourceSnapshot SourceSnapshot { get; internal set; }
        public Mesh UpdatedMesh { get; internal set; }
        public Mesh Destination { get; internal set; }
        public PaintTransferSettings Settings { get; internal set; }
        public PaintTransferReport Report { get; internal set; }
        internal string DestinationFingerprint;
        internal string SettingsFingerprint;
        internal string ResultFingerprint;
        public void Dispose()
        {
            SourceSnapshot?.Dispose();
            if (UpdatedMesh != null) Object.DestroyImmediate(UpdatedMesh);
            UpdatedMesh = null;
        }
    }

    /// <summary>Optional, explicit transfer workflow. It never changes model import settings or source paint.</summary>
    public static class RealBlendMeshPaintTransferWorkflow
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static PaintTransferSourceSnapshot CaptureSource(MeshFilter source)
        {
            ValidateSource(source);
            Mesh live = source.sharedMesh;
            var result = new PaintTransferSourceSnapshot { Source = source };
            try
            {
                var storage = source.GetComponent<VertexPaintStorage>();
                if (storage == null)
                {
                    result.Mesh = Object.Instantiate(live);
                    Color[] committed = FindPainterCommittedColors(source);
                    if (committed != null) result.Mesh.colors = committed;
                    result.Warnings.Add("No Vertex Paint Storage: using the painted mesh colors (or the active painter's committed colors).");
                }
                else
                {
                    Color[] colors = Read<Color[]>(storage, "paintedColors");
                    if (colors == null || colors.Length == 0)
                        throw new InvalidOperationException("Vertex Paint Storage has no committed paint. Apply your paint before transferring.");
                    string prefix = HasVertices(storage, "currentVertices") ? "current" : "base";
                    Vector3[] positions = Read<Vector3[]>(storage, prefix + "Vertices");
                    int[] triangles = Read<int[]>(storage, prefix + "Triangles");
                    if (positions == null || positions.Length != colors.Length || triangles == null)
                        throw new InvalidOperationException("The saved paint has no matching geometry snapshot. Restore the original painted mesh before transferring.");
                    if (MatchesStoredGeometry(storage, prefix, live))
                        result.Mesh = Object.Instantiate(live);
                    else
                    {
                        result.Mesh = ReconstructStoredMesh(storage, prefix);
                        result.Warnings.Add("Preview uses the saved paint geometry snapshot. Check that it represents the original model you want to transfer from.");
                        if (Read<Vector3[]>(storage, prefix + "Normals") == null)
                            result.Warnings.Add("This storage version did not save normals. Snapshot normals were recalculated; inspect projected matches carefully.");
                        if (source.GetComponent<MeshRenderer>().sharedMaterials.Length > 1 &&
                            Read<Array>(storage, prefix + "SubmeshesV2") == null && Read<int[]>(storage, prefix + "SubmeshIndexCounts") == null)
                            result.Warnings.Add("This older snapshot did not save material-slot boundaries. The source preview uses one submesh; the updated mesh keeps all its material slots. Inspect coincident seam matches carefully.");
                    }
                    result.Mesh.colors = (Color[])colors.Clone();
                }
                result.Mesh.name = live.name + " (Committed Paint Preview)";
                result.Mesh.hideFlags = HideFlags.HideAndDontSave;
                if (result.Mesh.colors.Length != result.Mesh.vertexCount)
                    throw new InvalidOperationException("The source has no complete vertex paint. Paint and apply the source before transferring.");
                result.Fingerprint = MeshFingerprint(result.Mesh, true) + ":" + MeshFingerprint(live, false) + ":" + SourceContext(source);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static MeshPaintTransferPreview BuildPreview(MeshFilter source, Mesh destination, PaintTransferSettings settings)
        {
            ValidateDestination(source, destination);
            var preview = new MeshPaintTransferPreview();
            try
            {
                preview.SourceSnapshot = CaptureSource(source);
                preview.Destination = destination;
                preview.Settings = CopySettings(settings ?? new PaintTransferSettings());
                preview.SettingsFingerprint = SettingsKey(preview.Settings);
                preview.DestinationFingerprint = MeshFingerprint(destination, true);
                preview.Report = RealBlendMeshPaintTransfer.Transfer(preview.SourceSnapshot.Mesh, destination, preview.Settings);
                preview.UpdatedMesh = Object.Instantiate(destination);
                preview.UpdatedMesh.name = destination.name + " (Transferred Paint Preview)";
                preview.UpdatedMesh.hideFlags = HideFlags.HideAndDontSave;
                preview.UpdatedMesh.colors = preview.Report.Colors;
                preview.ResultFingerprint = MeshFingerprint(preview.UpdatedMesh, true);
                return preview;
            }
            catch { preview.Dispose(); throw; }
        }

        public static bool IsPreviewCurrent(MeshPaintTransferPreview preview, PaintTransferSettings settings, out string reason)
        {
            reason = null;
            if (preview == null || preview.SourceSnapshot == null || preview.SourceSnapshot.Mesh == null || preview.UpdatedMesh == null)
                reason = "Build a preview first.";
            else if (SettingsKey(settings ?? new PaintTransferSettings()) != preview.SettingsFingerprint)
                reason = "Transfer settings changed. Build a new preview.";
            else if (MeshFingerprint(preview.UpdatedMesh, true) != preview.ResultFingerprint)
                reason = "The preview mesh changed. Build a new preview.";
            else
            {
                try
                {
                    ValidateDestination(preview.SourceSnapshot.Source, preview.Destination);
                    if (MeshFingerprint(preview.Destination, true) != preview.DestinationFingerprint)
                        reason = "The updated mesh changed. Build a new preview.";
                    else using (var current = CaptureSource(preview.SourceSnapshot.Source))
                        if (current.Fingerprint != preview.SourceSnapshot.Fingerprint)
                            reason = "The source mesh, committed paint, or object settings changed. Build a new preview.";
                }
                catch (Exception e) { reason = e.Message; }
            }
            return reason == null;
        }

        public static GameObject CreateCopy(MeshPaintTransferPreview preview, bool hideOriginal, string assetFolder = "Assets/RealBlendTransferredMeshes")
        {
            if (!IsPreviewCurrent(preview, preview?.Settings, out string reason))
                throw new InvalidOperationException(reason);
            EnsureAssetFolder(assetFolder);
            Mesh savedMesh = Object.Instantiate(preview.UpdatedMesh);
            savedMesh.name = preview.Destination.name + " (RealBlend Paint)";
            savedMesh.hideFlags = HideFlags.None;
            string fileName = savedMesh.name;
            foreach (char invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(assetFolder.TrimEnd('/') + "/" + fileName + ".asset");
            AssetDatabase.CreateAsset(savedMesh, assetPath);
            AssetDatabase.SaveAssetIfDirty(savedMesh);

            const string undoName = "Update Mesh and Transfer Paint";
            MeshFilter source = preview.SourceSnapshot.Source;
            GameObject original = source.gameObject;
            GameObject copy = null;
            GameObject staging = null;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            try
            {
                // A saved mesh asset survives domain reload with older storage versions.
                // It is intentionally retained when scene-object creation is undone.
                staging = new GameObject("RealBlend Transfer Staging") { hideFlags = HideFlags.HideAndDontSave };
                staging.SetActive(false);
                SceneManager.MoveGameObjectToScene(staging, original.scene);
                copy = Object.Instantiate(original, staging.transform, false);
                copy.SetActive(false);
                copy.name = original.name + " (Updated Paint)";
                copy.transform.SetParent(original.transform.parent, false);
                copy.transform.localPosition = original.transform.localPosition;
                copy.transform.localRotation = original.transform.localRotation;
                copy.transform.localScale = original.transform.localScale;
                MeshFilter filter = copy.GetComponent<MeshFilter>();
                Mesh oldMesh = filter.sharedMesh;
                filter.sharedMesh = savedMesh;
                MeshRenderer renderer = copy.GetComponent<MeshRenderer>();
                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                renderer.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
                renderer.realtimeLightmapScaleOffset = new Vector4(1, 1, 0, 0);
                var propertyBlock = new MaterialPropertyBlock();
                MeshRenderer sourceRenderer = original.GetComponent<MeshRenderer>();
                sourceRenderer.GetPropertyBlock(propertyBlock);
                renderer.SetPropertyBlock(propertyBlock);
                for (int slot = 0; slot < sourceRenderer.sharedMaterials.Length; slot++)
                {
                    propertyBlock.Clear();
                    sourceRenderer.GetPropertyBlock(propertyBlock, slot);
                    renderer.SetPropertyBlock(propertyBlock, slot);
                }
                foreach (MeshCollider collider in copy.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh == oldMesh) collider.sharedMesh = savedMesh;
                VertexPaintStorage storage = copy.GetComponent<VertexPaintStorage>();
                if (storage == null) storage = copy.AddComponent<VertexPaintStorage>();
                if (!InvokeMeshMethod(storage, "SetBaseTopology", savedMesh) && !InvokeMeshMethod(storage, "CaptureOriginals", savedMesh))
                    throw new InvalidOperationException("This Vertex Paint Storage version has no supported topology capture method.");
                InvokeMeshMethod(storage, "SaveCurrentState", savedMesh);
                InvokeMeshMethod(storage, "SetOwnedEditableMesh", savedMesh);
                MethodInfo generatedWall = storage.GetType().GetMethod("SetGeneratedSolidWall", Members, null, new[] { typeof(bool) }, null);
                if (generatedWall != null) generatedWall.Invoke(storage, new object[] { false });
                // Old storage can cache the source mesh in this nonserialized field.
                FieldInfo runtime = storage.GetType().GetField("_runtimeMesh", Members);
                if (runtime != null && runtime.FieldType == typeof(Mesh)) runtime.SetValue(storage, savedMesh);
                EditorUtility.SetDirty(storage);
                EditorUtility.SetDirty(filter);
                copy.SetActive(original.activeSelf);
                Transform intendedParent = original.transform.parent;
                // Unity's creation Undo otherwise recreates the object in whichever
                // scene is active at Redo time. Record its intended scene explicitly,
                // including when it already belongs to that scene.
                if (copy.transform.parent != null) copy.transform.SetParent(null, true);
                Undo.RegisterCreatedObjectUndo(copy, undoName);
                Undo.MoveGameObjectToScene(copy, original.scene, undoName);
                if (intendedParent != null) Undo.SetTransformParent(copy.transform, intendedParent, undoName);
                Undo.RecordObject(copy.transform, undoName);
                copy.transform.localPosition = original.transform.localPosition;
                copy.transform.localRotation = original.transform.localRotation;
                copy.transform.localScale = original.transform.localScale;
                Undo.SetSiblingIndex(copy.transform, original.transform.GetSiblingIndex() + 1, undoName);
                if (hideOriginal)
                {
                    Undo.RecordObject(original, undoName);
                    original.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(original);
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                return copy;
            }
            catch
            {
                if (copy != null) Object.DestroyImmediate(copy);
                // Do not delete the generated asset: it remains recoverable even if a component rejects cloning.
                throw;
            }
            finally { if (staging != null) Object.DestroyImmediate(staging); }
        }

        static void ValidateSource(MeshFilter source)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before transferring mesh paint.");
            if (source == null || source.sharedMesh == null)
                throw new InvalidOperationException("Choose the original painted scene object's Mesh Filter.");
            if (EditorUtility.IsPersistent(source) || !source.gameObject.scene.IsValid() || !source.gameObject.scene.isLoaded ||
                UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(source.gameObject) != null)
                throw new InvalidOperationException("Choose a scene instance, outside Prefab Mode. Prefab and model assets are never edited.");
            if (source.GetComponent<MeshRenderer>() == null)
                throw new InvalidOperationException("The source needs a Mesh Filter and Mesh Renderer on the same object.");
            if (source.GetComponentInParent<LODGroup>(true) != null || source.GetComponentInChildren<LODGroup>(true) != null)
                throw new InvalidOperationException("This object belongs to an LOD Group. Transfer a standalone static copy, then assign the result to the intended LOD explicitly.");
            if (source.GetComponentsInChildren<MeshFilter>(true).Length != 1 || source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Select one static mesh object. Transfer child meshes separately; skinned meshes are not supported.");
            foreach (Component component in source.GetComponentsInChildren<Component>(true))
                if (component != null && component.GetType().FullName == "UnityEngine.ProBuilder.ProBuilderMesh")
                    throw new InvalidOperationException("ProBuilder meshes use their own paint synchronization. Export a static mesh copy before using this tool.");
            ValidateMesh(source.sharedMesh, "Source");
        }

        static void ValidateDestination(MeshFilter source, Mesh destination)
        {
            ValidateSource(source);
            ValidateMesh(destination, "Updated");
            if (source.GetComponent<MeshRenderer>().sharedMaterials.Length != destination.subMeshCount)
                throw new InvalidOperationException("The updated mesh's submesh count must match the source renderer's material slots. Match the material slots in your modelling app before exporting.");
        }

        static void ValidateMesh(Mesh mesh, string label)
        {
            if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException(label + " mesh is missing or empty.");
            if (!mesh.isReadable) throw new InvalidOperationException(label + " mesh is not readable. Enable Read/Write in its model import settings, then rebuild the preview.");
            if (mesh.blendShapeCount != 0) throw new InvalidOperationException(label + " mesh has blend shapes. This tool supports static meshes without blend shapes.");
        }

        static T Read<T>(object value, string name) where T : class
        {
            if (value == null) return null;
            Type type = value.GetType();
            FieldInfo field = type.GetField(name, Members);
            if (field != null) return field.GetValue(value) as T;
            PropertyInfo property = type.GetProperty(name, Members);
            return property != null ? property.GetValue(value, null) as T : null;
        }

        static object ReadValue(object value, string name)
        {
            FieldInfo field = value.GetType().GetField(name, Members);
            return field != null ? field.GetValue(value) : null;
        }

        static bool HasVertices(object storage, string name) => Read<Vector3[]>(storage, name)?.Length > 0;
        static bool Same<T>(T[] a, T[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Length; i++) if (!comparer.Equals(a[i], b[i])) return false;
            return true;
        }

        static bool MatchesStoredGeometry(object storage, string prefix, Mesh mesh)
        {
            MethodInfo method = storage.GetType().GetMethod("MatchesCurrentGeometry", Members, null, new[] { typeof(Mesh) }, null);
            if (prefix == "current" && method != null && (bool)method.Invoke(storage, new object[] { mesh })) return true;
            // Unity can serialize null optional UV records as empty records. Some
            // storage versions compare their reference shape rather than their data.
            // Validate the actual streams before falling back to reconstruction.
            if (!Same(Read<Vector3[]>(storage, prefix + "Vertices"), mesh.vertices)) return false;
            Array submeshes = Read<Array>(storage, prefix + "SubmeshesV2");
            if (submeshes != null && submeshes.Length > 0)
            {
                if (submeshes.Length != mesh.subMeshCount) return false;
                for (int s = 0; s < submeshes.Length; s++)
                {
                    object part = submeshes.GetValue(s);
                    if (part == null || !Same(Read<int[]>(part, "indices"), mesh.GetIndices(s, false)) ||
                        (MeshTopology)ReadValue(part, "topology") != mesh.GetTopology(s) ||
                        (int)ReadValue(part, "baseVertex") != mesh.GetBaseVertex(s)) return false;
                }
            }
            else if (!Same(Read<int[]>(storage, prefix + "Triangles"), TriangleIndices(mesh))) return false;
            int[] counts = Read<int[]>(storage, prefix + "SubmeshIndexCounts");
            if (counts != null && counts.Length > 0)
            {
                if (counts.Length != mesh.subMeshCount) return false;
                for (int s = 0; s < counts.Length; s++) if (counts[s] != mesh.GetIndexCount(s)) return false;
            }
            Vector2[] uv = Read<Vector2[]>(storage, prefix + "UVs");
            if (!Same(uv ?? Array.Empty<Vector2>(), mesh.uv)) return false;
            Array channels = Read<Array>(storage, prefix + "UVChannels");
            if (channels != null && channels.Length > 0)
            {
                for (int c = 0; c < 8; c++)
                {
                    object channel = c < channels.Length ? channels.GetValue(c) : null;
                    Vector4[] values = Read<Vector4[]>(channel, "values");
                    var liveValues = new List<Vector4>(); mesh.GetUVs(c, liveValues);
                    if (values == null || values.Length == 0)
                    {
                        if (liveValues.Count > 0) return false;
                    }
                    else
                    {
                        VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + c);
                        if (!mesh.HasVertexAttribute(attribute) || (int)ReadValue(channel, "dimension") != mesh.GetVertexAttributeDimension(attribute) ||
                            !Same(values, liveValues.ToArray())) return false;
                    }
                }
            }
            else if (HasMember(storage, prefix + "SPOMEdgeUVs"))
            {
                var edge = new List<Vector2>(); mesh.GetUVs(2, edge);
                if (!Same(Read<Vector2[]>(storage, prefix + "SPOMEdgeUVs") ?? Array.Empty<Vector2>(), edge.ToArray())) return false;
            }
            Vector3[] normals = Read<Vector3[]>(storage, prefix + "Normals");
            Vector4[] tangents = Read<Vector4[]>(storage, prefix + "Tangents");
            return (!HasMember(storage, prefix + "Normals") || Same(normals ?? Array.Empty<Vector3>(), mesh.normals)) &&
                   (!HasMember(storage, prefix + "Tangents") || Same(tangents ?? Array.Empty<Vector4>(), mesh.tangents));
        }

        static bool HasMember(object value, string name) => value.GetType().GetField(name, Members) != null || value.GetType().GetProperty(name, Members) != null;

        static int[] TriangleIndices(Mesh mesh)
        {
            var values = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return null;
                values.AddRange(mesh.GetIndices(s));
            }
            return values.ToArray();
        }

        static Mesh ReconstructStoredMesh(object storage, string prefix)
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.vertices = Read<Vector3[]>(storage, prefix + "Vertices");
                Array submeshes = Read<Array>(storage, prefix + "SubmeshesV2");
                if (submeshes != null && submeshes.Length > 0)
                {
                    mesh.subMeshCount = submeshes.Length;
                    for (int s = 0; s < submeshes.Length; s++)
                    {
                        object part = submeshes.GetValue(s);
                        mesh.SetIndices(Read<int[]>(part, "indices"), (MeshTopology)ReadValue(part, "topology"), s, false, (int)ReadValue(part, "baseVertex"));
                    }
                }
                else
                {
                    int[] triangles = Read<int[]>(storage, prefix + "Triangles");
                    int[] counts = Read<int[]>(storage, prefix + "SubmeshIndexCounts");
                    if (counts != null && counts.Length > 0)
                    {
                        mesh.subMeshCount = counts.Length;
                        int start = 0;
                        for (int s = 0; s < counts.Length; s++)
                        {
                            int[] part = new int[counts[s]];
                            Array.Copy(triangles, start, part, 0, part.Length);
                            mesh.SetTriangles(part, s, false);
                            start += part.Length;
                        }
                        if (start != triangles.Length) throw new InvalidOperationException("Saved submesh data is incomplete.");
                    }
                    else
                    {
                        mesh.triangles = triangles;
                    }
                }
                Vector2[] uv0 = Read<Vector2[]>(storage, prefix + "UVs");
                if (uv0?.Length == mesh.vertexCount) mesh.uv = uv0;
                Vector2[] edge = Read<Vector2[]>(storage, prefix + "SPOMEdgeUVs");
                if (edge?.Length == mesh.vertexCount) mesh.SetUVs(2, new List<Vector2>(edge));
                Array channels = Read<Array>(storage, prefix + "UVChannels");
                if (channels != null)
                    for (int c = 0; c < Math.Min(channels.Length, 8); c++)
                    {
                        object channel = channels.GetValue(c);
                        if (channel == null) continue;
                        Vector4[] values = Read<Vector4[]>(channel, "values");
                        if (values?.Length != mesh.vertexCount) continue;
                        int dimension = (int)ReadValue(channel, "dimension");
                        if (dimension == 2) mesh.SetUVs(c, new List<Vector2>(Array.ConvertAll(values, v => new Vector2(v.x, v.y))));
                        else if (dimension == 3) mesh.SetUVs(c, new List<Vector3>(Array.ConvertAll(values, v => new Vector3(v.x, v.y, v.z))));
                        else mesh.SetUVs(c, new List<Vector4>(values));
                    }
                Vector3[] normals = Read<Vector3[]>(storage, prefix + "Normals");
                if (normals?.Length == mesh.vertexCount) mesh.normals = normals; else mesh.RecalculateNormals();
                Vector4[] tangents = Read<Vector4[]>(storage, prefix + "Tangents");
                if (tangents?.Length == mesh.vertexCount) mesh.tangents = tangents;
                else if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { Object.DestroyImmediate(mesh); throw; }
        }

        static bool InvokeMeshMethod(object target, string name, Mesh mesh)
        {
            MethodInfo method = target.GetType().GetMethod(name, Members, null, new[] { typeof(Mesh) }, null);
            if (method == null) return false;
            method.Invoke(target, new object[] { mesh });
            return true;
        }

        static Color[] FindPainterCommittedColors(MeshFilter source)
        {
            // Compatibility-only: old painters may hold committed colors before storage is added.
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                object painter = Read<object>(window, "_paintTool");
                if (painter == null) continue;
                MeshFilter owner = Read<MeshFilter>(painter, "CurrentMeshFilter") ?? Read<MeshFilter>(painter, "currentMeshFilter");
                if (owner != source) continue;
                Color[] colors = Read<Color[]>(painter, "CurrentStoredColors") ?? Read<Color[]>(painter, "storedColors");
                if (colors?.Length == source.sharedMesh.vertexCount) return (Color[])colors.Clone();
                throw new InvalidOperationException("Finish or clear the active paint preview before transferring this object.");
            }
            return null;
        }

        static PaintTransferSettings CopySettings(PaintTransferSettings s) => new PaintTransferSettings
        { PositionTolerance = s.PositionTolerance, ProjectNewVertices = s.ProjectNewVertices, ProjectionDistance = s.ProjectionDistance, MaximumNormalAngle = s.MaximumNormalAngle };

        static string SettingsKey(PaintTransferSettings s) =>
            s.PositionTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + s.ProjectNewVertices + ":" +
            s.ProjectionDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + s.MaximumNormalAngle.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        static string SourceContext(MeshFilter source)
        {
            string value = ObjectIdentity(source) + ":" + ObjectIdentity(source.sharedMesh) + ":" + source.gameObject.activeSelf;
            foreach (Material material in source.GetComponent<MeshRenderer>().sharedMaterials) value += ":" + ObjectIdentity(material);
            return value;
        }

        static string ObjectIdentity(Object value)
        {
            if (value == null) return "0";
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId().ToString();
#else
            return value.GetInstanceID().ToString();
#endif
        }

        static string MeshFingerprint(Mesh mesh, bool includeColors)
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            {
                writer.Write(mesh.vertexCount);
                writer.Write((int)mesh.indexFormat);
                Bounds bounds = mesh.bounds;
                Write(writer, new Vector4(bounds.center.x, bounds.center.y, bounds.center.z, 0));
                Write(writer, new Vector4(bounds.extents.x, bounds.extents.y, bounds.extents.z, 0));
                foreach (Vector3 v in mesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Write(mesh.subMeshCount);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    writer.Write((int)mesh.GetTopology(s)); writer.Write(mesh.GetBaseVertex(s));
                    int[] indices = mesh.GetIndices(s, false); writer.Write(indices.Length);
                    foreach (int index in indices) writer.Write(index);
                }
                Vector3[] normals = mesh.normals;
                writer.Write(normals.Length);
                foreach (Vector3 v in normals) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                Vector4[] tangents = mesh.tangents;
                writer.Write(tangents.Length);
                foreach (Vector4 v in tangents) Write(writer, v);
                for (int c = 0; c < 8; c++)
                {
                    var values = new List<Vector4>(); mesh.GetUVs(c, values);
                    VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + c);
                    writer.Write(mesh.HasVertexAttribute(attribute) ? mesh.GetVertexAttributeDimension(attribute) : 0);
                    writer.Write(values.Count); foreach (Vector4 v in values) Write(writer, v);
                }
                if (includeColors)
                {
                    Color[] colors = mesh.colors;
                    writer.Write(colors.Length);
                    foreach (Color color in colors) Write(writer, new Vector4(color.r, color.g, color.b, color.a));
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes.ToArray()));
            }
        }

        static void Write(BinaryWriter writer, Vector4 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w); }

        static void EnsureAssetFolder(string folder)
        {
            folder = (folder ?? "").Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains("..") || folder.Contains(":"))
                throw new ArgumentException("Choose a generated mesh folder inside Assets.");
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i])) throw new ArgumentException("Invalid generated mesh folder.");
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
