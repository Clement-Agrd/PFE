using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterFx.Editor
{
    /// <summary>
    /// Installeur WaterFx.
    ///   Tools > Water FX > 1 Installer       : maillage + matériaux + prefabs (Assets/WaterFx)
    ///   Tools > Water FX > 2 Scène de démo   : pose tous les prefabs côte à côte dans la scène ouverte
    ///   Tools > Water FX > 3 Vérifier URP    : contrôle Depth Texture / Opaque Texture / HDR
    /// Relancer l'installeur ÉCRASE les prefabs Water_* (GUID conservé) ; matériaux : valeurs réappliquées depuis les préréglages.
    /// Duplique un matériau/prefab avant de le modifier à la main si tu veux le garder.
    ///
    /// Water_Ocean (prefab)
    /// └── MeshRenderer (WaterFx/Water) + WaterFxSurface   (échelle = taille du préréglage)
    /// </summary>
    public static class WaterFxMenu
    {
        #region Chemins
        private const string Root = "Assets/WaterFx";
        private const string MaterialsDir = Root + "/Materials";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string MeshesDir = Root + "/Meshes";
        private const string TexturesDir = Root + "/Textures";
        private const string GridMeshPath = MeshesDir + "/WaterGrid.asset";
        private const int GridResolution = 128;          // 128x128 quads = 33 k triangles, assez pour des vagues de 8 m
        private const float DemoGap = 12f;
        #endregion

        #region Menu
        [MenuItem("Tools/Water FX/1 Installer (maillage + matériaux + prefabs)")]
        public static void Install()
        {
            AssetDatabase.Refresh();
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(MeshesDir);

            Shader shader = Shader.Find("WaterFx/Water");
            if (shader == null)
            {
                Debug.LogError("[WaterFx] Shader 'WaterFx/Water' introuvable ou en erreur : regarde la Console (URP installé ?).");
                return;
            }

            Mesh grid = GetOrCreateGrid();
            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/T_WaterNormal.png");
            var foamMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesDir + "/T_WaterFoam.png");
            if (normalMap == null || foamMap == null)
                Debug.LogWarning("[WaterFx] Textures manquantes dans " + TexturesDir + " : l'eau sera plate et sans écume.");

            foreach (WaterPresetSpec spec in WaterFxPresets.All)
            {
                Material material = BuildMaterial(spec, shader, normalMap, foamMap);
                BuildPrefab(spec, grid, material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WaterFx] " + WaterFxPresets.All.Length + " prefabs créés dans " + PrefabsDir + ". Pense à lancer 'Tools > Water FX > 3 Vérifier URP'.");
        }

        [MenuItem("Tools/Water FX/2 Scène de démo (tous les prefabs)")]
        public static void BuildDemo()
        {
            var group = new GameObject("Water FX démo");
            Undo.RegisterCreatedObjectUndo(group, "Water FX demo");

            float x = 0f;
            foreach (WaterPresetSpec spec in WaterFxPresets.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabsDir + "/" + spec.PrefabName + ".prefab");
                if (prefab == null)
                {
                    Debug.LogError("[WaterFx] Prefab manquant : " + spec.PrefabName + ". Lance d'abord Tools > Water FX > 1 Installer.");
                    return;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                float half = Mathf.Max(spec.Size.x, spec.Size.y) * 0.5f;
                x += half;
                instance.transform.localPosition = new Vector3(x, 0f, 0f);
                x += half + DemoGap;
            }
        }

        [MenuItem("Tools/Water FX/3 Vérifier URP (Depth / Opaque / HDR)")]
        public static void CheckUrp()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
            {
                Debug.LogError("[WaterFx] Aucun Render Pipeline actif : ce kit ne fonctionne qu'avec URP.");
                return;
            }

            var so = new SerializedObject(asset);
            bool depth = ReadBool(so, "m_RequireDepthTexture");
            bool opaque = ReadBool(so, "m_RequireOpaqueTexture");
            bool hdr = ReadBool(so, "m_SupportsHDR");

            Debug.Log("[WaterFx] URP '" + asset.name + "' — Depth Texture : " + Flag(depth) + " | Opaque Texture : " + Flag(opaque) + " | HDR : " + Flag(hdr));
            if (!depth || !opaque)
                Debug.LogWarning("[WaterFx] Active Depth Texture et Opaque Texture dans l'asset URP (Inspector > Rendering) : sans eux, profondeur/réfraction/écume de rivage ne fonctionnent pas. Pense aussi à la caméra : Depth/Opaque Texture sur 'Use Pipeline Settings'.");
        }
        #endregion

        #region Construction
        private static Material BuildMaterial(WaterPresetSpec spec, Shader shader, Texture2D normalMap, Texture2D foamMap)
        {
            string path = MaterialsDir + "/M_" + spec.PrefabName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "M_" + spec.PrefabName };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_NormalMap", normalMap);
            material.SetTexture("_FoamMap", foamMap);

            foreach (FloatProp p in spec.Floats) if (material.HasProperty(p.Name)) material.SetFloat(p.Name, p.Value);
            foreach (ColorProp p in spec.Colors) if (material.HasProperty(p.Name)) material.SetColor(p.Name, p.Value);
            foreach (VectorProp p in spec.Vectors) if (material.HasProperty(p.Name)) material.SetVector(p.Name, p.Value);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildPrefab(WaterPresetSpec spec, Mesh grid, Material material)
        {
            var root = new GameObject(spec.PrefabName);
            root.AddComponent<MeshFilter>().sharedMesh = grid;
            var meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            root.AddComponent<WaterFxSurface>();
            root.transform.localScale = new Vector3(spec.Size.x, 1f, spec.Size.y);   // le maillage fait 1 x 1 m

            PrefabUtility.SaveAsPrefabAsset(root, PrefabsDir + "/" + spec.PrefabName + ".prefab");
            Object.DestroyImmediate(root);
        }

        // Grille 1 x 1 m centrée, plan XZ. Les vagues sont calculées en monde : l'échelle du prefab ne déforme pas leur taille,
        // mais une grande surface (Ocean 200 m) = quads de ~1,6 m : suffisant pour des longueurs d'onde >= 8 m.
        private static Mesh GetOrCreateGrid()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(GridMeshPath);
            if (existing != null) return existing;

            int n = GridResolution;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var triangles = new int[n * n * 6];
            for (int z = 0, v = 0; z <= n; z++)
                for (int x = 0; x <= n; x++, v++)
                    vertices[v] = new Vector3(x / (float)n - 0.5f, 0f, z / (float)n - 0.5f);

            for (int z = 0, t = 0, v = 0; z < n; z++, v++)
                for (int x = 0; x < n; x++, v++, t += 6)
                {
                    triangles[t] = v;
                    triangles[t + 1] = v + n + 1;
                    triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1;
                    triangles[t + 4] = v + n + 1;
                    triangles[t + 5] = v + n + 2;
                }

            var mesh = new Mesh { name = "WaterGrid", indexFormat = IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.05f, 1f));   // marge verticale : l'amplitude réelle est ajoutée par le décalage d'échelle
            mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, GridMeshPath);
            return mesh;
        }
        #endregion

        #region Utilitaires
        private static bool ReadBool(SerializedObject so, string property)
        {
            SerializedProperty p = so.FindProperty(property);
            return p != null && p.boolValue;
        }

        private static string Flag(bool value) => value ? "OUI" : "NON";

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
