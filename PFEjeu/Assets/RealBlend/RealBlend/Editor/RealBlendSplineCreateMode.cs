using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public enum RealBlendTunnelProfile
    {
        BarrelVault,
        Round,
        Box,
        Gothic,
        LowCulvert,
        Custom
    }

    public enum RealBlendTunnelProfileUVMode
    {
        Continuous,
        MirrorAtCrown
    }

    [Serializable]
    public class RealBlendSplineCreateMode : IRealBlendCreationMode, IRealBlendAdditionalCreationMode
    {
        // Keep the serialized field name and object reference while avoiding a
        // compile-time dependency on the optional Splines package.
        public Component splineContainer;
        public int splineIndex;
        public RealBlendTunnelProfile tunnelProfile = RealBlendTunnelProfile.BarrelVault;
        public float tunnelWidth = 4f;
        public float tunnelHeight = 3f;
        public float wallThickness = 0.25f;
        public AnimationCurve widthProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve heightProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve thicknessProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [Range(1, 12)] public int lengthDensity = 3;
        [Range(4, 48)] public int profileSegments = 18;
        public bool includeFloor = false;
        public bool separateFloorMesh = false;
        public Material floorMaterial;
        public bool capEnds = true;
        public RealBlendTunnelProfileUVMode profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
        public float customProfileScale = 1f;
        public List<Vector2> customProfile = new List<Vector2>
        {
            new Vector2(-0.5f, 0f),
            new Vector2(-0.5f, 0.65f),
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0.65f),
            new Vector2(0.5f, 0f)
        };
        public float newCustomKeyLength = 0.25f;
        public float newCustomKeyAngle = 0f;

        public string DisplayName => "Spline";

        public void OnGUI(RealBlendCreationSettings settings)
        {
            if (!RealBlendSplinesBridge.IsAvailable)
            {
                EditorGUILayout.HelpBox(RealBlendSplinesBridge.UnavailableMessage, MessageType.Info);
                if (GUILayout.Button("Open Splines in Package Manager"))
                    UnityEditor.PackageManager.UI.Window.Open("com.unity.splines");
                return;
            }

            splineContainer = (Component)EditorGUILayout.ObjectField("Spline Container", splineContainer, RealBlendSplinesBridge.ContainerType, true);

            int splineCount = SplineCount;
            using (new EditorGUI.DisabledScope(splineCount <= 0))
                splineIndex = Mathf.Clamp(EditorGUILayout.IntField("Spline Index", splineIndex), 0, Mathf.Max(0, splineCount - 1));

            if (!RealBlendSplinesBridge.IsContainer(splineContainer))
                EditorGUILayout.HelpBox("Assign a SplineContainer to generate a tunnel along it.", MessageType.Info);
            else if (splineCount <= 0)
                EditorGUILayout.HelpBox("The assigned SplineContainer has no splines.", MessageType.Warning);

            tunnelProfile = (RealBlendTunnelProfile)EditorGUILayout.EnumPopup("Tunnel Profile", tunnelProfile);
            tunnelWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField("Interior Width", tunnelWidth));
            tunnelHeight = Mathf.Max(0.01f, EditorGUILayout.FloatField("Interior Height", tunnelHeight));
            if (tunnelProfile == RealBlendTunnelProfile.Custom)
                DrawCustomProfileGUI();

            wallThickness = Mathf.Max(0f, EditorGUILayout.FloatField("Wall Thickness", wallThickness));
            widthProfile = EditorGUILayout.CurveField("Width Along Spline", widthProfile);
            heightProfile = EditorGUILayout.CurveField("Height Along Spline", heightProfile);
            thicknessProfile = EditorGUILayout.CurveField("Thickness Along Spline", thicknessProfile);
            lengthDensity = EditorGUILayout.IntSlider("Length Density", lengthDensity, 1, 12);
            profileSegments = EditorGUILayout.IntSlider("Profile Segments", profileSegments, 4, 48);
            includeFloor = EditorGUILayout.Toggle("Include Floor", includeFloor);
            using (new EditorGUI.DisabledScope(!includeFloor))
            {
                separateFloorMesh = EditorGUILayout.Toggle("Separate Floor Mesh", separateFloorMesh);
                if (separateFloorMesh)
                    floorMaterial = (Material)EditorGUILayout.ObjectField("Floor Material", floorMaterial, typeof(Material), false);
            }
            if (!includeFloor)
                separateFloorMesh = false;
            capEnds = EditorGUILayout.Toggle("Cap Start/End", capEnds);
            profileUVMode = (RealBlendTunnelProfileUVMode)EditorGUILayout.EnumPopup("Profile UV Mode", profileUVMode);

            GUILayout.Space(4);
            EditorGUILayout.LabelField("Tunnel Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Barrel Vault")) ApplyBarrelVault();
                if (GUILayout.Button("Round Pipe")) ApplyRoundPipe();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Box Tunnel")) ApplyBoxTunnel();
                if (GUILayout.Button("Gothic Arch")) ApplyGothicArch();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Low Culvert")) ApplyLowCulvert();
                if (GUILayout.Button("Custom Arch")) ApplyCustomArch();
            }
        }

        public void OnSceneGUI(SceneView sceneView, RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            if (!CanGenerate(settings)) return;
            if (!TryBuildFrames(settings, out List<TunnelFrame> frames, 96)) return;

            Handles.color = new Color(1f, 0.92f, 0.016f, 0.85f);
            int step = Mathf.Max(1, frames.Count / 24);
            for (int i = 0; i < frames.Count - 1; i++)
            {
                if (tunnelProfile == RealBlendTunnelProfile.Custom)
                    DrawCustomTunnelRails(frames[i], frames[i + 1]);
                else
                {
                    DrawTunnelRail(frames[i], frames[i + 1], -0.5f, 0f);
                    DrawTunnelRail(frames[i], frames[i + 1], 0.5f, 0f);
                    DrawTunnelRail(frames[i], frames[i + 1], 0f, 1f);
                }

                if (settings.showTriangles && i % step == 0)
                    DrawProfilePreview(frames[i]);
            }

            DrawProfilePreview(frames[0]);
            DrawProfilePreview(frames[frames.Count - 1]);
        }

        public List<RealBlendAdditionalMeshBuild> BuildAdditionalMeshes(RealBlendCreationSettings settings, RealBlendCreationPreviewState preview)
        {
            List<RealBlendAdditionalMeshBuild> builds = new List<RealBlendAdditionalMeshBuild>();
            if (!includeFloor || !separateFloorMesh)
                return builds;

            RealBlendMeshBuildData floorData = BuildSeparateFloorMesh(settings);
            if (floorData.vertices.Count >= 3 && floorData.triangles.Count >= 3)
            {
                builds.Add(new RealBlendAdditionalMeshBuild
                {
                    data = floorData,
                    material = floorMaterial != null ? floorMaterial : settings.defaultMaterial,
                    placement = GetPlacement(preview)
                });
            }

            return builds;
        }

        private RealBlendMeshBuildData BuildSeparateFloorMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData { name = "RealBlend Tunnel Floor" };
            if (!TryBuildFrames(settings, out List<TunnelFrame> frames, -1))
                return data;

            RealBlendPlacement placement = GetPlacement(null);
            Matrix4x4 worldToLocal = Matrix4x4.TRS(placement.position, placement.rotation, Vector3.one).inverse;
            Quaternion inverseRotation = Quaternion.Inverse(placement.rotation);
            int widthCount = RealBlendCreationCore.ResolutionCount(tunnelWidth, settings.resolutionPerMeter);

            RealBlendCreationCore.AddPatch(data, frames.Count, widthCount, (u, v) =>
            {
                TunnelFrame frame = frames[u];
                float widthNorm = v / (float)(widthCount - 1);
                float x = Mathf.Lerp(-frame.width * 0.5f, frame.width * 0.5f, widthNorm);
                Vector3 world = frame.position + frame.right * x;
                return new RealBlendPatchVertex(
                    worldToLocal.MultiplyPoint3x4(world),
                    inverseRotation * Vector3.up,
                    new Vector2(frame.distance, widthNorm * frame.width));
            }, settings, false);

            return data;
        }

        public bool CanGenerate(RealBlendCreationSettings settings)
        {
            return splineContainer != null && SplineCount > 0;
        }

        public RealBlendMeshBuildData BuildMesh(RealBlendCreationSettings settings)
        {
            RealBlendMeshBuildData data = new RealBlendMeshBuildData { name = "RealBlend Tunnel" };
            if (!TryBuildFrames(settings, out List<TunnelFrame> frames, -1))
                return data;

            RealBlendPlacement placement = GetPlacement(null);
            Matrix4x4 worldToLocal = Matrix4x4.TRS(placement.position, placement.rotation, Vector3.one).inverse;
            Quaternion inverseRotation = Quaternion.Inverse(placement.rotation);

            AddTunnelSurface(data, settings, frames, worldToLocal, inverseRotation, false);

            if (HasThickness(frames))
            {
                AddTunnelSurface(data, settings, frames, worldToLocal, inverseRotation, true);

                if (!IncludeFloorInProfile)
                {
                    AddOpenRim(data, settings, frames, worldToLocal, inverseRotation, false);
                    AddOpenRim(data, settings, frames, worldToLocal, inverseRotation, true);
                }

                if (capEnds)
                {
                    AddEndCap(data, settings, frames[0], worldToLocal, inverseRotation, -1f);
                    AddEndCap(data, settings, frames[frames.Count - 1], worldToLocal, inverseRotation, 1f);
                }
            }

            return data;
        }

        public RealBlendPlacement GetPlacement(RealBlendCreationPreviewState preview)
        {
            if (splineContainer == null)
                return RealBlendPlacement.Identity;

            return new RealBlendPlacement
            {
                position = splineContainer.transform.position,
                rotation = splineContainer.transform.rotation
            };
        }

        public void GetStats(RealBlendCreationSettings settings, out int vertexCount, out int triangleCount)
        {
            if (!CanGenerate(settings))
            {
                vertexCount = 0;
                triangleCount = 0;
                return;
            }

            int sampleCount = GetSampleCount(settings);
            int profileCount = BuildProfile(new TunnelFrame
            {
                width = tunnelWidth,
                height = tunnelHeight,
                thickness = wallThickness
            }).Count;
            List<Vector2Int> profileSpans = BuildProfileSpans(BuildProfile(new TunnelFrame
            {
                width = tunnelWidth,
                height = tunnelHeight,
                thickness = wallThickness
            }));
            int surfaceProfileRows = CountProfileSpanRows(profileSpans);
            int surfaceProfileSegments = CountProfileSpanSegments(profileSpans);

            int stripCount = profileCount - 1;
            vertexCount = sampleCount * surfaceProfileRows;
            triangleCount = (sampleCount - 1) * surfaceProfileSegments * 2;

            if (wallThickness > 0.0001f)
            {
                vertexCount += sampleCount * surfaceProfileRows;
                triangleCount += (sampleCount - 1) * surfaceProfileSegments * 2;

                if (!IncludeFloorInProfile)
                {
                    vertexCount += sampleCount * 2 * 2;
                    triangleCount += (sampleCount - 1) * 2 * 2;
                }

                if (capEnds)
                {
                    vertexCount += profileCount * 2 * 2;
                    triangleCount += stripCount * 2 * 2;
                }
            }
        }

        private int SplineCount => RealBlendSplinesBridge.GetSplineCount(splineContainer);
        private bool IncludeFloorInProfile => includeFloor && !separateFloorMesh;

        private void ApplyBarrelVault()
        {
            tunnelProfile = RealBlendTunnelProfile.BarrelVault;
            tunnelWidth = 4f;
            tunnelHeight = 3.2f;
            wallThickness = 0.25f;
            profileSegments = 18;
            includeFloor = false;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
            ResetCurves();
        }

        private void ApplyRoundPipe()
        {
            tunnelProfile = RealBlendTunnelProfile.Round;
            tunnelWidth = 3.5f;
            tunnelHeight = 3.5f;
            wallThickness = 0.25f;
            profileSegments = 24;
            includeFloor = true;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.Continuous;
            ResetCurves();
        }

        private void ApplyBoxTunnel()
        {
            tunnelProfile = RealBlendTunnelProfile.Box;
            tunnelWidth = 4f;
            tunnelHeight = 2.8f;
            wallThickness = 0.2f;
            profileSegments = 8;
            includeFloor = false;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
            ResetCurves();
        }

        private void ApplyGothicArch()
        {
            tunnelProfile = RealBlendTunnelProfile.Gothic;
            tunnelWidth = 4f;
            tunnelHeight = 4.2f;
            wallThickness = 0.25f;
            profileSegments = 22;
            includeFloor = false;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
            ResetCurves();
        }

        private void ApplyLowCulvert()
        {
            tunnelProfile = RealBlendTunnelProfile.LowCulvert;
            tunnelWidth = 4.5f;
            tunnelHeight = 1.8f;
            wallThickness = 0.3f;
            profileSegments = 18;
            includeFloor = true;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
            ResetCurves();
        }

        private void ApplyCustomArch()
        {
            tunnelProfile = RealBlendTunnelProfile.Custom;
            tunnelWidth = 4f;
            tunnelHeight = 3f;
            wallThickness = 0.25f;
            profileSegments = 12;
            includeFloor = false;
            separateFloorMesh = false;
            profileUVMode = RealBlendTunnelProfileUVMode.MirrorAtCrown;
            customProfileScale = 1f;
            customProfile = new List<Vector2>
            {
                new Vector2(-0.5f, 0f),
                new Vector2(-0.5f, 0.55f),
                new Vector2(-0.2f, 0.92f),
                new Vector2(0f, 1f),
                new Vector2(0.2f, 0.92f),
                new Vector2(0.5f, 0.55f),
                new Vector2(0.5f, 0f)
            };
            ResetCurves();
        }

        private void ResetCurves()
        {
            widthProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            heightProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            thicknessProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        }

        private void DrawCustomProfileGUI()
        {
            EnsureCustomProfile();
            customProfileScale = Mathf.Max(0.01f, EditorGUILayout.FloatField("Profile Scale", customProfileScale));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Custom Shape Graph", EditorStyles.boldLabel);
                for (int i = 0; i < customProfile.Count; i++)
                    customProfile[i] = EditorGUILayout.Vector2Field($"Key {i}  X/Y", customProfile[i]);

                GUILayout.Space(4);
                newCustomKeyLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("New Key Length", newCustomKeyLength));
                newCustomKeyAngle = EditorGUILayout.Slider("New Key Angle", newCustomKeyAngle, -179f, 179f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Key")) AddCustomKey();
                    if (GUILayout.Button("Add Angled Key")) AddAngledCustomKey();
                    using (new EditorGUI.DisabledScope(customProfile.Count <= 2))
                    {
                        if (GUILayout.Button("Remove Last")) customProfile.RemoveAt(customProfile.Count - 1);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset Arch")) ResetCustomArchProfile();
                    if (GUILayout.Button("Reset Box")) ResetCustomBoxProfile();
                }
            }
        }

        private void DrawTunnelRail(TunnelFrame a, TunnelFrame b, float xNorm, float yNorm)
        {
            Vector3 pa = a.position + a.right * (a.width * xNorm) + Vector3.up * (a.height * yNorm);
            Vector3 pb = b.position + b.right * (b.width * xNorm) + Vector3.up * (b.height * yNorm);
            Handles.DrawLine(pa, pb);
        }

        private void DrawCustomTunnelRails(TunnelFrame a, TunnelFrame b)
        {
            List<Vector2> aGraph = GetScaledCustomProfile(a);
            List<Vector2> bGraph = GetScaledCustomProfile(b);
            int count = Mathf.Min(aGraph.Count, bGraph.Count);
            for (int i = 0; i < count; i++)
            {
                Vector3 pa = a.position + a.right * aGraph[i].x + Vector3.up * aGraph[i].y;
                Vector3 pb = b.position + b.right * bGraph[i].x + Vector3.up * bGraph[i].y;
                Handles.DrawLine(pa, pb);
            }
        }

        private void DrawProfilePreview(TunnelFrame frame)
        {
            Handles.color = new Color(1f, 0.92f, 0.016f, 0.85f);
            List<TunnelProfilePoint> profile = BuildProfile(frame);
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector3 a = frame.position + frame.right * profile[i].point.x + Vector3.up * profile[i].point.y;
                Vector3 b = frame.position + frame.right * profile[i + 1].point.x + Vector3.up * profile[i + 1].point.y;
                Handles.DrawLine(a, b);
            }

            if (tunnelProfile != RealBlendTunnelProfile.Custom)
                return;

            Handles.color = new Color(0f, 1f, 1f, 0.85f);
            List<Vector2> graph = GetScaledCustomProfile(frame);
            for (int i = 0; i < graph.Count; i++)
            {
                Vector3 point = frame.position + frame.right * graph[i].x + Vector3.up * graph[i].y;
                Handles.SphereHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * 0.035f, EventType.Repaint);
                Handles.Label(point, $"Key {i}");
            }
        }

        private void AddTunnelSurface(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            List<TunnelFrame> frames,
            Matrix4x4 worldToLocal,
            Quaternion inverseRotation,
            bool outer)
        {
            List<TunnelProfilePoint>[] profiles = BuildProfiles(frames);
            List<Vector2Int> profileSpans = BuildProfileSpans(profiles[0]);

            for (int spanIndex = 0; spanIndex < profileSpans.Count; spanIndex++)
            {
                Vector2Int span = profileSpans[spanIndex];
                int spanCount = span.y - span.x + 1;
                if (spanCount < 2)
                    continue;

                RealBlendCreationCore.AddPatch(data, frames.Count, spanCount, (u, v) =>
                {
                    TunnelFrame frame = frames[u];
                    List<TunnelProfilePoint> profileList = profiles[u];
                    int profileIndex = Mathf.Min(span.x + v, profileList.Count - 1);
                    TunnelProfilePoint profile = profileList[profileIndex];
                    float thickness = outer ? frame.thickness : 0f;
                    Vector2 profilePoint = outer ? profile.point - profile.inwardNormal * thickness : profile.point;
                    Vector2 profileNormal = outer ? -profile.inwardNormal : profile.inwardNormal;
                    Vector3 worldNormal = (frame.right * profileNormal.x + Vector3.up * profileNormal.y).normalized;
                    Vector3 world = frame.position + frame.right * profilePoint.x + Vector3.up * profilePoint.y;

                    return new RealBlendPatchVertex(
                        worldToLocal.MultiplyPoint3x4(world),
                        inverseRotation * worldNormal,
                        new Vector2(frame.distance, GetProfileUVDistance(profileList, profileIndex)));
                }, settings, false);
            }
        }

        private List<TunnelProfilePoint>[] BuildProfiles(List<TunnelFrame> frames)
        {
            List<TunnelProfilePoint>[] profiles = new List<TunnelProfilePoint>[frames.Count];
            for (int i = 0; i < frames.Count; i++)
                profiles[i] = BuildProfile(frames[i]);
            return profiles;
        }

        private List<Vector2Int> BuildProfileSpans(List<TunnelProfilePoint> profile)
        {
            List<Vector2Int> spans = new List<Vector2Int>();
            if (profile == null || profile.Count < 2)
                return spans;

            int start = 0;
            for (int i = 1; i < profile.Count; i++)
            {
                float normalDot = Vector2.Dot(profile[i - 1].inwardNormal, profile[i].inwardNormal);
                if (normalDot >= 0.85f)
                    continue;

                int cornerIndex = Mathf.Max(start + 1, i - 1);
                if (cornerIndex - start >= 1)
                    spans.Add(new Vector2Int(start, cornerIndex));
                start = cornerIndex;
            }

            if (profile.Count - 1 - start >= 1)
                spans.Add(new Vector2Int(start, profile.Count - 1));

            if (spans.Count == 0)
                spans.Add(new Vector2Int(0, profile.Count - 1));

            return spans;
        }

        private int CountProfileSpanRows(List<Vector2Int> spans)
        {
            int count = 0;
            for (int i = 0; i < spans.Count; i++)
                count += Mathf.Max(0, spans[i].y - spans[i].x + 1);
            return Mathf.Max(2, count);
        }

        private int CountProfileSpanSegments(List<Vector2Int> spans)
        {
            int count = 0;
            for (int i = 0; i < spans.Count; i++)
                count += Mathf.Max(0, spans[i].y - spans[i].x);
            return Mathf.Max(1, count);
        }

        private void AddOpenRim(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            List<TunnelFrame> frames,
            Matrix4x4 worldToLocal,
            Quaternion inverseRotation,
            bool endRim)
        {
            RealBlendCreationCore.AddPatch(data, frames.Count, 2, (u, v) =>
            {
                TunnelFrame frame = frames[u];
                // Width/height curves can change the number of profile points.
                // Each rim must follow this frame's endpoint, not the first frame's index.
                List<TunnelProfilePoint> profilePoints = BuildProfile(frame);
                TunnelProfilePoint profile = profilePoints[endRim ? profilePoints.Count - 1 : 0];
                bool outer = v == 1;
                Vector2 point = outer ? profile.point - profile.inwardNormal * frame.thickness : profile.point;
                Vector2 normal2 = -profile.inwardNormal;
                Vector3 worldNormal = (frame.right * normal2.x + Vector3.up * normal2.y).normalized;
                Vector3 world = frame.position + frame.right * point.x + Vector3.up * point.y;
                return new RealBlendPatchVertex(
                    worldToLocal.MultiplyPoint3x4(world),
                    inverseRotation * worldNormal,
                    new Vector2(frame.distance, outer ? frame.thickness : 0f));
            }, settings, false);
        }

        private void AddEndCap(
            RealBlendMeshBuildData data,
            RealBlendCreationSettings settings,
            TunnelFrame frame,
            Matrix4x4 worldToLocal,
            Quaternion inverseRotation,
            float tangentSign)
        {
            List<TunnelProfilePoint> profile = BuildProfile(frame);
            Vector3 worldNormal = frame.tangent * tangentSign;
            RealBlendCreationCore.AddPatch(data, profile.Count, 2, (u, v) =>
            {
                TunnelProfilePoint point = profile[u];
                bool outer = v == 1;
                Vector2 profilePoint = outer ? point.point - point.inwardNormal * frame.thickness : point.point;
                Vector3 world = frame.position + frame.right * profilePoint.x + Vector3.up * profilePoint.y;
                return new RealBlendPatchVertex(
                    worldToLocal.MultiplyPoint3x4(world),
                    inverseRotation * worldNormal,
                    new Vector2(point.distance, outer ? frame.thickness : 0f));
            }, settings, false);
        }

        private bool TryBuildFrames(RealBlendCreationSettings settings, out List<TunnelFrame> frames, int previewSampleLimit)
        {
            frames = new List<TunnelFrame>();
            if (splineContainer == null || SplineCount <= 0)
                return false;

            splineIndex = Mathf.Clamp(splineIndex, 0, SplineCount - 1);
            int sampleCount = GetSampleCount(settings);
            if (previewSampleLimit > 0)
                sampleCount = Mathf.Min(sampleCount, previewSampleLimit);

            Vector3 previous = Vector3.zero;
            float cumulativeDistance = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount == 1 ? 0f : i / (float)(sampleCount - 1);
                if (!RealBlendSplinesBridge.TryEvaluate(splineContainer, splineIndex, t, out Vector3 position, out Vector3 tangent, out Vector3 up))
                    return false;

                if (tangent.sqrMagnitude < 0.000001f)
                    tangent = i > 0 ? position - previous : Vector3.forward;
                tangent.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, tangent);
                if (right.sqrMagnitude < 0.000001f)
                    right = Vector3.Cross(up, tangent);
                if (right.sqrMagnitude < 0.000001f)
                    right = Vector3.right;
                right.Normalize();

                if (i > 0)
                    cumulativeDistance += Vector3.Distance(previous, position);

                frames.Add(new TunnelFrame
                {
                    t = t,
                    position = position,
                    tangent = tangent,
                    right = right,
                    distance = cumulativeDistance,
                    width = Mathf.Max(0.001f, tunnelWidth * Mathf.Max(0.001f, SafeEvaluate(widthProfile, t, 1f))),
                    height = Mathf.Max(0.001f, tunnelHeight * Mathf.Max(0.001f, SafeEvaluate(heightProfile, t, 1f))),
                    thickness = Mathf.Max(0f, wallThickness * Mathf.Max(0f, SafeEvaluate(thicknessProfile, t, 1f)))
                });
                previous = position;
            }

            return frames.Count >= 2;
        }

        private List<TunnelProfilePoint> BuildProfile(TunnelFrame frame)
        {
            List<TunnelProfilePoint> points = new List<TunnelProfilePoint>();
            float halfWidth = frame.width * 0.5f;
            bool profileIncludesFloor = false;

            switch (tunnelProfile)
            {
                case RealBlendTunnelProfile.Round:
                    if (IncludeFloorInProfile)
                    {
                        AddRoundPipeProfile(points, halfWidth, frame.height, Mathf.Max(8, profileSegments));
                        profileIncludesFloor = true;
                    }
                    else
                    {
                        AddRoundProfile(points, halfWidth, frame.height, Mathf.Max(6, profileSegments));
                    }
                    break;
                case RealBlendTunnelProfile.Box:
                    AddBoxProfile(points, halfWidth, frame.height);
                    break;
                case RealBlendTunnelProfile.Gothic:
                    AddGothicProfile(points, halfWidth, frame.height, Mathf.Max(6, profileSegments));
                    break;
                case RealBlendTunnelProfile.LowCulvert:
                    AddRoundProfile(points, halfWidth, frame.height, Mathf.Max(6, profileSegments));
                    break;
                case RealBlendTunnelProfile.Custom:
                    AddCustomProfile(points, frame);
                    break;
                default:
                    AddBarrelVaultProfile(points, halfWidth, frame.height, Mathf.Max(6, profileSegments));
                    break;
            }

            if (IncludeFloorInProfile && !profileIncludesFloor)
                AddFloor(points, halfWidth, Mathf.Max(1, profileSegments / 4));

            return points;
        }

        private void AddBarrelVaultProfile(List<TunnelProfilePoint> points, float halfWidth, float height, int segments)
        {
            float radius = Mathf.Min(halfWidth, height);
            float springY = Mathf.Max(0f, height - radius);
            int sideSegments = Mathf.Max(1, Mathf.RoundToInt(springY * segments / Mathf.Max(0.01f, height)));
            int archSegments = Mathf.Max(4, segments);

            for (int i = 0; i <= sideSegments; i++)
            {
                float y = Mathf.Lerp(0f, springY, i / (float)sideSegments);
                AddProfilePoint(points, new Vector2(-halfWidth, y), Vector2.right);
            }

            Vector2 center = new Vector2(0f, springY);
            for (int i = 1; i <= archSegments; i++)
            {
                float angle = Mathf.Lerp(Mathf.PI, 0f, i / (float)archSegments);
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                AddProfilePoint(points, point, (center - point).normalized);
            }

            for (int i = 1; i <= sideSegments; i++)
            {
                float y = Mathf.Lerp(springY, 0f, i / (float)sideSegments);
                AddProfilePoint(points, new Vector2(halfWidth, y), Vector2.left);
            }
        }

        private void AddRoundProfile(List<TunnelProfilePoint> points, float halfWidth, float height, int segments)
        {
            Vector2 center = Vector2.zero;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(Mathf.PI, 0f, i / (float)segments);
                Vector2 point = new Vector2(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * height);
                Vector2 inward = (center - point).sqrMagnitude > 0.000001f ? (center - point).normalized : Vector2.down;
                AddProfilePoint(points, point, inward);
            }
        }

        private void AddRoundPipeProfile(List<TunnelProfilePoint> points, float halfWidth, float height, int segments)
        {
            Vector2 center = new Vector2(0f, height * 0.5f);
            float radiusY = height * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(Mathf.PI, -Mathf.PI, i / (float)segments);
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * radiusY);
                AddProfilePoint(points, point, (center - point).normalized);
            }
        }

        private void AddBoxProfile(List<TunnelProfilePoint> points, float halfWidth, float height)
        {
            int sideSegments = Mathf.Max(1, profileSegments / 4);
            int topSegments = Mathf.Max(1, profileSegments / 3);

            for (int i = 0; i <= sideSegments; i++)
                AddProfilePoint(points, new Vector2(-halfWidth, Mathf.Lerp(0f, height, i / (float)sideSegments)), Vector2.right);

            for (int i = 1; i <= topSegments; i++)
                AddProfilePoint(points, new Vector2(Mathf.Lerp(-halfWidth, halfWidth, i / (float)topSegments), height), Vector2.down);

            for (int i = 1; i <= sideSegments; i++)
                AddProfilePoint(points, new Vector2(halfWidth, Mathf.Lerp(height, 0f, i / (float)sideSegments)), Vector2.left);
        }

        private void AddGothicProfile(List<TunnelProfilePoint> points, float halfWidth, float height, int segments)
        {
            float springY = height * 0.32f;
            int sideSegments = Mathf.Max(1, profileSegments / 5);
            int archSegments = Mathf.Max(3, segments / 2);
            Vector2 top = new Vector2(0f, height);
            Vector2 focus = new Vector2(0f, springY * 0.35f);

            for (int i = 0; i <= sideSegments; i++)
                AddProfilePoint(points, new Vector2(-halfWidth, Mathf.Lerp(0f, springY, i / (float)sideSegments)), Vector2.right);

            Vector2 leftSpring = new Vector2(-halfWidth, springY);
            for (int i = 1; i <= archSegments; i++)
            {
                float t = i / (float)archSegments;
                Vector2 point = Quadratic(leftSpring, new Vector2(-halfWidth * 0.35f, height * 0.95f), top, t);
                AddProfilePoint(points, point, (focus - point).normalized);
            }

            Vector2 rightSpring = new Vector2(halfWidth, springY);
            for (int i = 1; i <= archSegments; i++)
            {
                float t = i / (float)archSegments;
                Vector2 point = Quadratic(top, new Vector2(halfWidth * 0.35f, height * 0.95f), rightSpring, t);
                AddProfilePoint(points, point, (focus - point).normalized);
            }

            for (int i = 1; i <= sideSegments; i++)
                AddProfilePoint(points, new Vector2(halfWidth, Mathf.Lerp(springY, 0f, i / (float)sideSegments)), Vector2.left);
        }

        private void AddFloor(List<TunnelProfilePoint> points, float halfWidth, int segments)
        {
            for (int i = 1; i <= segments; i++)
            {
                float x = Mathf.Lerp(halfWidth, -halfWidth, i / (float)segments);
                AddProfilePoint(points, new Vector2(x, 0f), Vector2.up);
            }
        }

        private void AddCustomProfile(List<TunnelProfilePoint> points, TunnelFrame frame)
        {
            List<Vector2> graph = GetScaledCustomProfile(frame);
            for (int i = 0; i < graph.Count - 1; i++)
            {
                Vector2 a = graph[i];
                Vector2 b = graph[i + 1];
                Vector2 tangent = b - a;
                float segmentLength = tangent.magnitude;
                if (segmentLength <= 0.0001f)
                    continue;

                int segmentCount = GetCustomSegmentCount(i);
                Vector2 inward = tangent.sqrMagnitude > 0.000001f
                    ? new Vector2(tangent.y, -tangent.x).normalized
                    : Vector2.down;

                for (int s = i == 0 ? 0 : 1; s <= segmentCount; s++)
                    AddProfilePoint(points, Vector2.Lerp(a, b, s / (float)segmentCount), inward);
            }
        }

        private void AddProfilePoint(List<TunnelProfilePoint> points, Vector2 point, Vector2 inwardNormal)
        {
            float distance = 0f;
            if (points.Count > 0)
            {
                TunnelProfilePoint previous = points[points.Count - 1];
                if ((previous.point - point).sqrMagnitude < 0.0000001f)
                    return;
                distance = previous.distance + Vector2.Distance(previous.point, point);
            }

            points.Add(new TunnelProfilePoint
            {
                point = point,
                inwardNormal = inwardNormal.sqrMagnitude > 0.000001f ? inwardNormal.normalized : Vector2.down,
                distance = distance
            });
        }

        private bool HasThickness(List<TunnelFrame> frames)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].thickness > 0.0001f)
                    return true;
            }

            return false;
        }

        private float GetProfileUVDistance(List<TunnelProfilePoint> profile, int index)
        {
            if (profile == null || profile.Count == 0)
                return 0f;

            index = Mathf.Clamp(index, 0, profile.Count - 1);
            if (profileUVMode == RealBlendTunnelProfileUVMode.Continuous)
                return profile[index].distance;

            float crownDistance = FindCrownDistance(profile);
            float sideEndDistance = FindSideEndDistance(profile, crownDistance);
            float distance = profile[index].distance;

            if (distance <= crownDistance)
                return distance;

            if (distance <= sideEndDistance)
                return Mathf.Max(0f, sideEndDistance - distance);

            return distance - sideEndDistance;
        }

        private float FindCrownDistance(List<TunnelProfilePoint> profile)
        {
            int crownIndex = 0;
            float bestY = float.NegativeInfinity;
            float bestCenterOffset = float.PositiveInfinity;
            for (int i = 0; i < profile.Count; i++)
            {
                float y = profile[i].point.y;
                float centerOffset = Mathf.Abs(profile[i].point.x);
                if (y > bestY + 0.0001f || (Mathf.Abs(y - bestY) <= 0.0001f && centerOffset < bestCenterOffset))
                {
                    crownIndex = i;
                    bestY = y;
                    bestCenterOffset = centerOffset;
                }
            }

            return profile[crownIndex].distance;
        }

        private float FindSideEndDistance(List<TunnelProfilePoint> profile, float crownDistance)
        {
            bool hasAppendedFloor = IncludeFloorInProfile && tunnelProfile != RealBlendTunnelProfile.Round;
            for (int i = 1; hasAppendedFloor && i < profile.Count; i++)
            {
                if (profile[i].distance <= crownDistance)
                    continue;

                if (Vector2.Dot(profile[i].inwardNormal, Vector2.up) > 0.95f && Mathf.Abs(profile[i].point.y) < 0.0001f)
                    return profile[i - 1].distance;
            }

            return profile[profile.Count - 1].distance;
        }

        private void AddCustomKey()
        {
            EnsureCustomProfile();
            Vector2 last = customProfile[customProfile.Count - 1];
            customProfile.Add(last + new Vector2(0f, newCustomKeyLength));
        }

        private void AddAngledCustomKey()
        {
            EnsureCustomProfile();
            Vector2 last = customProfile[customProfile.Count - 1];
            float radians = newCustomKeyAngle * Mathf.Deg2Rad;
            Vector2 delta = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * newCustomKeyLength;
            customProfile.Add(last + delta);
        }

        private void ResetCustomArchProfile()
        {
            customProfile = new List<Vector2>
            {
                new Vector2(-0.5f, 0f),
                new Vector2(-0.5f, 0.55f),
                new Vector2(-0.2f, 0.92f),
                new Vector2(0f, 1f),
                new Vector2(0.2f, 0.92f),
                new Vector2(0.5f, 0.55f),
                new Vector2(0.5f, 0f)
            };
        }

        private void ResetCustomBoxProfile()
        {
            customProfile = new List<Vector2>
            {
                new Vector2(-0.5f, 0f),
                new Vector2(-0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0f)
            };
        }

        private List<Vector2> GetScaledCustomProfile(TunnelFrame frame)
        {
            EnsureCustomProfile();
            float scale = Mathf.Max(0.01f, customProfileScale);
            List<Vector2> scaled = new List<Vector2>(customProfile.Count);
            for (int i = 0; i < customProfile.Count; i++)
            {
                Vector2 point = customProfile[i];
                scaled.Add(new Vector2(point.x * frame.width * scale, point.y * frame.height * scale));
            }

            return scaled;
        }

        private int GetCustomSegmentCount(int segmentIndex)
        {
            EnsureCustomProfile();
            float totalLength = 0f;
            for (int i = 0; i < customProfile.Count - 1; i++)
                totalLength += Vector2.Distance(customProfile[i], customProfile[i + 1]);

            float segmentLength = Vector2.Distance(customProfile[segmentIndex], customProfile[segmentIndex + 1]);
            return Mathf.Max(1, Mathf.RoundToInt((segmentLength / Mathf.Max(0.0001f, totalLength)) * Mathf.Max(2, profileSegments)));
        }

        private void EnsureCustomProfile()
        {
            if (customProfile == null)
                customProfile = new List<Vector2>();

            if (customProfile.Count >= 2)
                return;

            ResetCustomArchProfile();
        }

        private int GetSampleCount(RealBlendCreationSettings settings)
        {
            if (splineContainer == null || SplineCount <= 0)
                return 2;

            int safeIndex = Mathf.Clamp(splineIndex, 0, SplineCount - 1);
            float length = Mathf.Max(0.01f, RealBlendSplinesBridge.CalculateLength(splineContainer, safeIndex));
            return Mathf.Max(2, Mathf.RoundToInt(length * Mathf.Max(1, lengthDensity)) + 1);
        }

        private static Vector2 Quadratic(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            float inv = 1f - t;
            return inv * inv * a + 2f * inv * t * b + t * t * c;
        }

        private static float SafeEvaluate(AnimationCurve curve, float t, float fallback)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(t) : fallback;
        }

        private struct TunnelFrame
        {
            public float t;
            public Vector3 position;
            public Vector3 tangent;
            public Vector3 right;
            public float distance;
            public float width;
            public float height;
            public float thickness;
        }

        private struct TunnelProfilePoint
        {
            public Vector2 point;
            public Vector2 inwardNormal;
            public float distance;
        }
    }
}
