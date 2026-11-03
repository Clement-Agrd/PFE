using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DustVFX.EditorTools
{
    /// <summary>Builds materials, centred grain meshes and the pooled DustStep prefab.</summary>
    public static class DustPrefabBuilder
    {
        const string Root = "Assets/DustVFX";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string PrefabPath = Root + "/Prefabs/DustStep.prefab";
        const string GrainObj = "Assets/VFX/FootDustVFX/Unity/DustVFX/Meshes/Dust_grain.obj";

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Flipbooks/{name}.png");

        // ------------------------------------------------------------------ materials

        static Material DustMat(string name, string flipbook, System.Action<Material> tune)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("VFX/DustParticle"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("VFX/DustParticle");
            m.SetTexture("_MainTex", Tex(flipbook));
            tune?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials(out Material puffA, out Material puffB, out Material veil,
                                          out Material ringM, out Material wispM, out Material grain)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");

            puffA = DustMat("Mat_DustPuffA", "FB_DustPuff", m => { m.SetFloat("_Alpha", 0.5f); m.SetFloat("_Erosion", 0.85f); m.SetFloat("_FadeOut", 0.4f); m.SetFloat("_EdgeSoft", 0.35f); m.SetFloat("_LightMul", 0.8f); m.SetFloat("_AmbientMul", 1.15f); });
            puffB = DustMat("Mat_DustPuffB", "FB_DustPuffB", m => { m.SetFloat("_Alpha", 0.5f); m.SetFloat("_Erosion", 0.85f); m.SetFloat("_FadeOut", 0.4f); m.SetFloat("_EdgeSoft", 0.35f); m.SetFloat("_LightMul", 0.8f); m.SetFloat("_AmbientMul", 1.15f); });
            veil = DustMat("Mat_DustVeil", "FB_DustPuffB", m =>
            {
                m.SetFloat("_Alpha", 0.55f); m.SetFloat("_Erosion", 0.55f); m.SetFloat("_FadeIn", 0.12f);
                m.SetFloat("_FadeOut", 0.3f); m.SetFloat("_SoftDistance", 0.35f); m.SetFloat("_NearFadeStart", 0.15f); m.SetFloat("_NearFadeEnd", 0.6f);
            });
            ringM = DustMat("Mat_DustRing", "FB_DustRing", m =>
            {
                m.SetFloat("_Alpha", 0.9f); m.SetFloat("_Erosion", 0.7f); m.SetFloat("_FadeOut", 0.35f); m.SetFloat("_SoftDistance", 0.3f);
                m.SetFloat("_NearFadeStart", 0.15f); m.SetFloat("_NearFadeEnd", 0.6f);
            });
            wispM = DustMat("Mat_DustWisp", "FB_DustWisp", m =>
            {
                m.SetFloat("_Alpha", 0.8f); m.SetFloat("_Erosion", 0.8f); m.SetFloat("_FadeOut", 0.35f);
                m.SetFloat("_NormalStrength", 0.4f); m.SetFloat("_SoftDistance", 0.25f);
            });

            string gp = $"{MatDir}/Mat_DustGrain.mat";
            grain = AssetDatabase.LoadAssetAtPath<Material>(gp);
            if (grain == null)
            {
                grain = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
                AssetDatabase.CreateAsset(grain, gp);
            }
            grain.SetColor("_BaseColor", Color.white);
            grain.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(grain);
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Centred, unit-sized copies of grain_1..14 (the OBJ keeps them at their scene positions).</summary>
        public static Mesh[] BuildGrainMeshes()
        {
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(Root, "Meshes");
            var result = new List<Mesh>();
            // Unity merges the OBJ objects into one mesh, so read the 'o grain_N' blocks from the file itself.
            if (!System.IO.File.Exists(GrainObj))
            {
                Debug.LogWarning("[DustVFX] Grain OBJ not found: " + GrainObj);
                return result.ToArray();
            }

            var allV = new List<Vector3>();
            string curName = null;
            var curTris = new List<int>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            void Flush()
            {
                if (curName == null || curTris.Count < 3) { curTris.Clear(); return; }
                var remap = new Dictionary<int, int>();
                var verts = new List<Vector3>();
                var tris = new int[curTris.Count];
                for (int i = 0; i < curTris.Count; i++)
                {
                    int g = curTris[i];
                    if (!remap.TryGetValue(g, out int l)) { l = verts.Count; remap[g] = l; verts.Add(allV[g]); }
                    tris[i] = l;
                }
                // OBJ is right-handed: mirror X and reverse winding like Unity's importer
                for (int i = 0; i < verts.Count; i++) verts[i] = new Vector3(-verts[i].x, verts[i].y, verts[i].z);
                for (int i = 0; i < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }

                var b = new Bounds(verts[0], Vector3.zero);
                for (int i = 1; i < verts.Count; i++) b.Encapsulate(verts[i]);
                float s = 1f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                var arr = verts.ToArray();
                for (int i = 0; i < arr.Length; i++) arr[i] = (arr[i] - b.center) * s;

                var m = new Mesh { name = "Grain_" + curName };
                m.vertices = arr;
                m.triangles = tris;
                m.RecalculateNormals();
                m.RecalculateBounds();
                string path = $"{MeshDir}/{m.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                result.Add(AssetDatabase.LoadAssetAtPath<Mesh>(path));
                curTris.Clear();
            }

            foreach (string line in System.IO.File.ReadLines(GrainObj))
            {
                if (line.StartsWith("v "))
                {
                    string[] p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    allV.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                }
                else if (line.StartsWith("o "))
                {
                    Flush();
                    curName = line.Substring(2).Trim();
                }
                else if (line.StartsWith("f "))
                {
                    string[] p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    // fan triangulation of the (tri/quad/ngon) face
                    for (int i = 2; i + 1 < p.Length; i++)
                    {
                        curTris.Add(int.Parse(p[1].Split('/')[0]) - 1);
                        curTris.Add(int.Parse(p[i].Split('/')[0]) - 1);
                        curTris.Add(int.Parse(p[i + 1].Split('/')[0]) - 1);
                    }
                }
            }
            Flush();

            if (result.Count == 0)
                Debug.LogWarning("[DustVFX] No grain_* meshes parsed from " + GrainObj);
            return result.ToArray();
        }

        // ------------------------------------------------------------------ prefab

        public static DustStep BuildPrefab()
        {
            BuildMaterials(out var puffA, out var puffB, out var veilM, out var ringM, out var wispM, out var grainM);
            Mesh[] grainMeshes = BuildGrainMeshes();
            if (!AssetDatabase.IsValidFolder(Root + "/Prefabs")) AssetDatabase.CreateFolder(Root, "Prefabs");

            var root = new GameObject("DustStep");
            var step = root.AddComponent<DustStep>();

            Transform plane = new GameObject("GroundPlane").transform;
            plane.SetParent(root.transform, false);
            step.groundPlane = plane;

            var curvePuff = new AnimationCurve(new Keyframe(0f, 0.38f), new Keyframe(0.14f, 0.78f), new Keyframe(1f, 1f));
            var curveVeil = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.3f, 0.82f), new Keyframe(1f, 1f));
            var curveRing = new AnimationCurve(new Keyframe(0f, 0.18f), new Keyframe(0.22f, 0.72f), new Keyframe(1f, 1f));
            var curveWisp = new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1f));
            var curveGrain = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f));

            step.puffA = MakePS(root.transform, "Puff_A", puffA, 6, -0.03f, 2.4f, ParticleSystemRenderMode.Billboard, false, true, curvePuff, 0.35f);
            step.puffB = MakePS(root.transform, "Puff_B", puffB, 6, -0.03f, 2.4f, ParticleSystemRenderMode.Billboard, false, true, curvePuff, 0.35f);
            step.veil = MakePS(root.transform, "Veil", veilM, 4, 0f, 1.8f, ParticleSystemRenderMode.Billboard, true, true, curveVeil, 0.2f);
            step.ring = MakePS(root.transform, "Ring", ringM, 2, 0f, 0f, ParticleSystemRenderMode.Billboard, true, true, curveRing, 0.1f);
            step.wisp = MakePS(root.transform, "Wisp", wispM, 12, -0.02f, 1.3f, ParticleSystemRenderMode.Stretch, false, true, curveWisp, 0.2f);
            step.grains = MakePS(root.transform, "Grains", grainM, 24, 1f, 0f, ParticleSystemRenderMode.Mesh, false, false, curveGrain, 0f);

            // lying systems: quad normal = step normal
            step.veil.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            step.ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var wr = step.wisp.GetComponent<ParticleSystemRenderer>();
            wr.lengthScale = 4f;
            wr.velocityScale = 0.22f;
            wr.cameraVelocityScale = 0f;

            // grains: random centred meshes, tumbling, bouncing on the ground plane
            var gr = step.grains.GetComponent<ParticleSystemRenderer>();
            gr.renderMode = ParticleSystemRenderMode.Mesh;
            gr.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
            if (grainMeshes.Length > 0) gr.SetMeshes(grainMeshes);
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gr.receiveShadows = false;

            var rot = step.grains.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

            var col = step.grains.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.Planes;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.SetPlane(0, plane);
            col.dampen = 0.3f;
            col.bounce = 0.3f;
            col.lifetimeLoss = 0f;
            col.radiusScale = 0.5f;
            col.colliderForce = 0f;

            if (System.IO.File.Exists(PrefabPath)) AssetDatabase.DeleteAsset(PrefabPath);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var prefabStep = saved.GetComponent<DustStep>();
            var settings = AssetDatabase.LoadAssetAtPath<DustSettings>(DustSurfaceBuilder.SettingsPath);
            if (settings == null) settings = DustSurfaceBuilder.BuildAll();
            settings.stepPrefab = prefabStep;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[DustVFX] DustStep prefab built: " + PrefabPath);
            return prefabStep;
        }

        static readonly ParticleSystemVertexStream[] Streams =
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Tangent,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2,
            ParticleSystemVertexStream.AnimBlend, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
        };

        static ParticleSystem MakePS(Transform parent, string name, Material mat, int maxParticles, float gravity, float drag,
                                     ParticleSystemRenderMode mode, bool local, bool flipbook, AnimationCurve size, float rotSpeed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.maxParticles = maxParticles;
            main.gravityModifier = gravity;
            main.stopAction = ParticleSystemStopAction.None;

            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);

            if (drag > 0f)
            {
                var lvl = ps.limitVelocityOverLifetime;
                lvl.enabled = true;
                lvl.limit = 100f;
                lvl.dampen = 1f;
                lvl.drag = drag;
                lvl.multiplyDragByParticleSize = false;
                lvl.multiplyDragByParticleVelocity = false;
            }

            if (mode == ParticleSystemRenderMode.Billboard && rotSpeed > 0f)
            {
                var rol = ps.rotationOverLifetime;
                rol.enabled = true;
                rol.z = new ParticleSystem.MinMaxCurve(-rotSpeed, rotSpeed);
            }

            if (flipbook)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = 8;
                tsa.numTilesY = 8;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                tsa.startFrame = 0f;
                tsa.cycleCount = 1;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.alignment = local ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.allowRoll = true;
            if (flipbook)
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>(Streams));
            return ps;
        }

        // ------------------------------------------------------------------ everything

        [MenuItem("Tools/Dust VFX/Rebuild Everything")]
        public static void RebuildEverything()
        {
            DustSurfaceBuilder.BuildAll();
            DustFlipbookGenerator.GenerateAll();
            AssetDatabase.Refresh();
            BuildPrefab();
            DustSceneBuilder.Build();
            Debug.Log("[DustVFX] Rebuild Everything done.");
        }

        [MenuItem("Tools/Dust VFX/Build Prefab + Materials")]
        public static void BuildPrefabMenu() => BuildPrefab();
    }
}
