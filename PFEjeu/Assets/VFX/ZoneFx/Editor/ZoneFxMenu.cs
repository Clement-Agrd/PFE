using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneFx.Editor
{
    /// <summary>
    /// Installeur :
    ///   Tools > Zone FX > 1 Installer        : matériaux + prefabs ZoneFx_Flat et ZoneFx_Projected dans Assets/ZoneFx
    ///   Tools > Zone FX > 2 Scène de démo     : sol + les 6 presets, en mode Flat (rangée du fond) et Projected (rangée devant)
    ///   Tools > Zone FX > 3 Vérifier URP      : Depth Texture (mode Projected), HDR (bloom)
    /// Relancer l'installeur ÉCRASE les prefabs ZoneFx_* (GUID conservé) et ré-applique les valeurs des matériaux.
    /// </summary>
    public static class ZoneFxMenu
    {
        #region Constantes
        private const string Root = "Assets/VFX/ZoneFx";
        private const string MaterialsDir = Root + "/Materials";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string TexturesDir = Root + "/Textures";
        private const string MeshesDir = Root + "/Meshes";
        private const string FlatPrefabName = "ZoneFx_Flat";
        private const string ProjectedPrefabName = "ZoneFx_Projected";
        private const float DemoRadius = 4f;
        private const float DemoSpacing = 10f;
        private const float DemoRowGap = 12f;
        private const float TowerHeight = 3f;
        #endregion

        #region Menu
        [MenuItem("Tools/Zone FX/1 Installer (matériaux + prefabs)")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(MeshesDir);

            Mesh veilMesh = LoadOrCreateVeilMesh();
            Material veil = LoadOrCreateMaterial("M_ZoneVeil", "ZoneFx/Veil");
            Material flat = LoadOrCreateMaterial("M_ZoneRing_Flat", "ZoneFx/RingFlat");
            Material projected = LoadOrCreateMaterial("M_ZoneRing_Projected", "ZoneFx/RingProjected");
            Material sparks = CreateParticleMaterial("M_ZoneSparks", "T_ZoneSpark", 2.2f);
            Material motes = CreateParticleMaterial("M_ZoneMotes", "T_ZoneSoft", 1.5f);

            BuildPrefab(FlatPrefabName, ZoneRingMode.Flat, PrimitiveType.Quad, flat, veilMesh, veil, sparks, motes);
            BuildPrefab(ProjectedPrefabName, ZoneRingMode.Projected, PrimitiveType.Cube, projected, veilMesh, veil, sparks, motes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ZoneFx] Prefabs créés dans " + PrefabsDir + ". Lance aussi 'Vérifier URP'.");
        }

        [MenuItem("Tools/Zone FX/2 Scène de démo (6 presets, Flat + Projected)")]
        public static void BuildDemo()
        {
            var flatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/" + FlatPrefabName + ".prefab");
            var projectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/" + ProjectedPrefabName + ".prefab");
            if (flatPrefab == null || projectedPrefab == null)
            {
                Debug.LogError("[ZoneFx] Prefabs manquants. Lance d'abord Tools > Zone FX > 1 Installer.");
                return;
            }

            var group = new GameObject("Zone FX démo");
            Undo.RegisterCreatedObjectUndo(group, "Zone FX demo");

            int count = System.Enum.GetValues(typeof(ZoneFxPreset)).Length;
            float width = (count - 1) * DemoSpacing;
            BuildGround(group.transform, width + DemoSpacing * 2f, DemoRowGap + DemoSpacing * 2f);

            for (int i = 0; i < count; i++)
            {
                float x = i * DemoSpacing - width * 0.5f;
                SpawnZone(group.transform, flatPrefab, (ZoneFxPreset)i, new Vector3(x, 0f, DemoRowGap * 0.5f));
                SpawnZone(group.transform, projectedPrefab, (ZoneFxPreset)i, new Vector3(x, 0f, -DemoRowGap * 0.5f));
            }
            Debug.Log("[ZoneFx] Démo créée : rangée du fond = Flat, rangée devant = Projected. Recule la caméra (~25 m de haut, 40° d'inclinaison).");
        }

        [MenuItem("Tools/Zone FX/3 Vérifier URP (depth, HDR)")]
        public static void CheckUrp()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                Debug.LogError("[ZoneFx] Aucun Render Pipeline Asset actif : le kit cible URP.");
                return;
            }

            var so = new SerializedObject(asset);
            Report(so, "m_RequireDepthTexture", "Depth Texture", "le mode Projected (l'anneau épouse le relief)");
            Report(so, "m_SupportsHDR", "HDR", "les intensités > 1 -> bloom sur l'anneau et les traînées");
            Debug.Log("[ZoneFx] Pour le bloom : ajoute un Volume avec l'override Bloom (seuil ~1) et active Post Processing sur la caméra.");
        }
        #endregion

        #region Vérification URP
        private static void Report(SerializedObject so, string property, string label, string usage)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null) Debug.LogWarning("[ZoneFx] " + label + " : réglage introuvable dans cette version d'URP, vérifie-le à la main (" + usage + ").");
            else if (p.boolValue) Debug.Log("[ZoneFx] " + label + " : OK.");
            else Debug.LogWarning("[ZoneFx] " + label + " est DÉSACTIVÉ dans l'asset URP : active-le pour " + usage + ".");
        }
        #endregion

        #region Prefabs
        private static void BuildPrefab(string prefabName, ZoneRingMode mode, PrimitiveType ringShape,
                                        Material ringMaterial, Mesh veilMesh, Material veilMaterial, Material sparksMaterial, Material motesMaterial)
        {
            var root = new GameObject(prefabName);

            // Anneau : primitive Quad (Flat) ou Cube (Projected), sans collider. Position / échelle posées par ZoneFxController.
            GameObject ring = GameObject.CreatePrimitive(ringShape);
            ring.name = "Ring";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            var ringRenderer = ring.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMaterial;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;

            // Voile : cylindre ouvert unitaire, mis à l'échelle par ZoneFxController.
            var veil = new GameObject("Veil");
            veil.transform.SetParent(root.transform, false);
            veil.AddComponent<MeshFilter>().sharedMesh = veilMesh;
            var veilRenderer = veil.AddComponent<MeshRenderer>();
            veilRenderer.sharedMaterial = veilMaterial;
            veilRenderer.shadowCastingMode = ShadowCastingMode.Off;
            veilRenderer.receiveShadows = false;

            ParticleSystem sparks = BuildParticles("Sparks", ZoneParticleKind.Sparks, root.transform, sparksMaterial);
            ParticleSystem motes = BuildParticles("Motes", ZoneParticleKind.Motes, root.transform, motesMaterial);

            var controller = root.AddComponent<ZoneFxController>();
            var so = new SerializedObject(controller);
            so.FindProperty("mode").enumValueIndex = (int)mode;
            so.FindProperty("bindings.ring").objectReferenceValue = ringRenderer;
            so.FindProperty("bindings.ringTransform").objectReferenceValue = ring.transform;
            so.FindProperty("bindings.veil").objectReferenceValue = veilRenderer;
            so.FindProperty("bindings.veilTransform").objectReferenceValue = veil.transform;
            so.FindProperty("bindings.sparks").objectReferenceValue = sparks;
            so.FindProperty("bindings.motes").objectReferenceValue = motes;
            so.ApplyModifiedPropertiesWithoutUndo();
            controller.Refresh();       // pose la taille / la couleur initiales dans le prefab

            PrefabUtility.SaveAsPrefabAsset(root, PrefabsDir + "/" + prefabName + ".prefab");
            Object.DestroyImmediate(root);
        }

        private static ParticleSystem BuildParticles(string name, ZoneParticleKind kind, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            ZoneParticleSetup.Configure(system, kind);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }
        #endregion

        #region Démo
        private static void SpawnZone(Transform parent, GameObject prefab, ZoneFxPreset preset, Vector3 position)
        {
            // Petite tour (cylindre) au centre pour l'échelle ; la zone est posée en ENFANT, pivot au sol.
            GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tower.name = "Tour " + preset;
            tower.transform.SetParent(parent, false);
            tower.transform.localPosition = position + new Vector3(0f, TowerHeight * 0.5f, 0f);
            tower.transform.localScale = new Vector3(1f, TowerHeight * 0.5f, 1f);    // le cylindre fait 2 m de haut à l'échelle 1
            Object.DestroyImmediate(tower.GetComponent<Collider>());

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = prefab.name + " " + preset;
            instance.transform.localPosition = position;

            var controller = instance.GetComponent<ZoneFxController>();
            var so = new SerializedObject(controller);
            so.FindProperty("preset").enumValueIndex = (int)preset;
            so.FindProperty("radius").floatValue = DemoRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            controller.Refresh();
        }

        private static void BuildGround(Transform parent, float sizeX, float sizeZ)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 x 10 m à l'échelle 1
            ground.name = "Sol";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(sizeX / 10f, 1f, sizeZ / 10f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;                                               // pas d'URP : on laisse le matériau par défaut
            var material = new Material(lit) { name = "M_ZoneDemoGround" };
            material.SetColor("_BaseColor", new Color(0.12f, 0.16f, 0.12f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        #endregion

        #region Matériaux
        private static Mesh LoadOrCreateVeilMesh()
        {
            string path = MeshesDir + "/ZoneVeil.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = ZoneVeilMesh.Build();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material CreateParticleMaterial(string name, string textureName, float intensity)
        {
            Material material = LoadOrCreateMaterial(name, "ZoneFx/Particle");
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/" + textureName + ".png"));
            material.SetFloat("_Intensity", intensity);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);                   // additif
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException("[ZoneFx] Shader introuvable : " + shaderName + ". Regarde la Console (erreur de compilation ? URP installé ?).");

            string path = MaterialsDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
