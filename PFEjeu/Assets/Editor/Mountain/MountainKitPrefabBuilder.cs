#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mountain.Editor
{
    /// <summary>
    /// P0 : l'importeur OBJ d'Unity fusionne les objets "_LOD0/_LOD1/_LOD2" d'un meme fichier en un seul mesh.
    /// Cet outil relit les OBJ du kit (sans les modifier), cree un mesh par LOD dans le dossier "Meshes" du kit,
    /// puis construit les prefabs (LODGroup + materiau partage + collider simplifie) dans "Prefabs".
    /// Idempotent : relancable, il met a jour les assets existants en conservant leurs GUID.
    /// </summary>
    public static class MountainKitPrefabBuilder
    {
        private enum ColliderMode { None, Box, MeshLod2 }

        private sealed class KitSpec
        {
            public string ModelFolder;
            public string ModelPrefix;
            public string[] Variants;
            public float[] LodHeights;
            public ColliderMode Collider;
        }

        private const string MenuPath = "Tools/Mountain/P0 Build Kit LOD Prefabs";

        private static readonly KitSpec[] Kits =
        {
            new KitSpec { ModelFolder = "Assets/Arts/3D/CliffPanKit/Assets/CliffPan/Models", ModelPrefix = "CliffPan_", Variants = new[] { "A", "B", "C" }, LodHeights = new[] { 0.30f, 0.10f, 0.03f }, Collider = ColliderMode.Box },
            new KitSpec { ModelFolder = "Assets/Arts/3D/ScreePileKit/Assets/ScreePile/Models", ModelPrefix = "ScreePile_", Variants = new[] { "A", "B" }, LodHeights = new[] { 0.10f, 0.04f, 0.015f }, Collider = ColliderMode.None },
            new KitSpec { ModelFolder = "Assets/Arts/3D_Rocks/RockBlocksKit/RockBlocksKit/Assets/RockBlocks/Models", ModelPrefix = "RockBlock_", Variants = new[] { "A", "B", "C", "D", "E" }, LodHeights = new[] { 0.40f, 0.14f, 0.045f }, Collider = ColliderMode.MeshLod2 },
            new KitSpec { ModelFolder = "Assets/Arts/3D_Rocks/SmallRocksKit/SmallRocksKit/Assets/SmallRocks/Models", ModelPrefix = "SmallRock_", Variants = new[] { "A", "B", "C", "D" }, LodHeights = new[] { 0.25f, 0.08f, 0.025f }, Collider = ColliderMode.None },
            new KitSpec { ModelFolder = "Assets/Arts/3D_Rocks/RockSlabKit/RockSlabKit/Assets/RockSlab/Models", ModelPrefix = "RockSlab_", Variants = new[] { "A", "B" }, LodHeights = new[] { 0.30f, 0.12f, 0.04f }, Collider = ColliderMode.MeshLod2 },
            new KitSpec { ModelFolder = "Assets/Arts/3D_Rocks/RockLandmarkKit/RockLandmarkKit/Assets/RockLandmark/Models", ModelPrefix = "RockLandmark_", Variants = new[] { "JunctionA" }, LodHeights = new[] { 0.20f, 0.08f, 0.03f }, Collider = ColliderMode.MeshLod2 },
            new KitSpec { ModelFolder = "Assets/Arts/3D_Rocks/RuinKit/RuinKit/Assets/Ruin/Models", ModelPrefix = "Ruin_", Variants = new[] { "Main", "Arch", "Pillar", "Wall" }, LodHeights = new[] { 0.22f, 0.09f, 0.03f }, Collider = ColliderMode.MeshLod2 },
        };

        [MenuItem(MenuPath)]
        private static void Build()
        {
            var report = new List<string>();
            foreach (KitSpec kit in Kits)
            {
                foreach (string variant in kit.Variants) BuildVariant(kit, variant, report);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MountainKit] P0 termine :\n" + string.Join("\n", report));
        }

        #region Construction d'un prefab
        private static void BuildVariant(KitSpec kit, string variant, List<string> report)
        {
            string name = kit.ModelPrefix + variant;
            string objPath = $"{kit.ModelFolder}/{name}.obj";
            string kitRoot = Path.GetDirectoryName(kit.ModelFolder).Replace('\\', '/');
            if (!File.Exists(objPath)) { report.Add($"{name}: OBJ introuvable"); return; }

            Material material = FindMaterial(kitRoot, name);
            if (material == null) { report.Add($"{name}: materiau introuvable dans {kitRoot}/Materials"); return; }

            List<Mesh> lods = ParseObj(objPath, name);
            if (lods.Count < 3) { report.Add($"{name}: {lods.Count} LOD trouves (3 attendus)"); return; }

            string meshFolder = EnsureFolder(kitRoot, "Meshes");
            string prefabFolder = EnsureFolder(kitRoot, "Prefabs");
            var savedMeshes = new Mesh[lods.Count];
            for (int i = 0; i < lods.Count; i++) savedMeshes[i] = SaveMesh(lods[i], $"{meshFolder}/{name}_LOD{i}.asset");

            var root = new GameObject(name);
            try
            {
                var lodArray = new LOD[3];
                for (int i = 0; i < 3; i++)
                {
                    var child = new GameObject("LOD" + i);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = savedMeshes[i];
                    var mr = child.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = material;
                    mr.shadowCastingMode = i < 2 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    lodArray[i] = new LOD(kit.LodHeights[i], new Renderer[] { mr });
                }

                var group = root.AddComponent<LODGroup>();
                group.SetLODs(lodArray);
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();
                AddCollider(root, kit.Collider, savedMeshes[2]);

                PrefabUtility.SaveAsPrefabAsset(root, $"{prefabFolder}/{name}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            Bounds b = savedMeshes[0].bounds;
            report.Add($"{name}: tris {Tris(savedMeshes[0])}/{Tris(savedMeshes[1])}/{Tris(savedMeshes[2])} taille {b.size:F1} minY {b.min.y:F2} collider {kit.Collider}");
        }

        private static void AddCollider(GameObject root, ColliderMode mode, Mesh lod2)
        {
            switch (mode)
            {
                case ColliderMode.Box:
                    var box = root.AddComponent<BoxCollider>();
                    box.center = lod2.bounds.center;
                    box.size = lod2.bounds.size;
                    break;
                case ColliderMode.MeshLod2:
                    root.AddComponent<MeshCollider>().sharedMesh = lod2;
                    break;
            }
        }

        private static Material FindMaterial(string kitRoot, string modelName)
        {
            string folder = kitRoot + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            Material first = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m.name == "M_" + modelName) return m;
                if (first == null) first = m;
            }
            return first;
        }
        #endregion

        #region Lecture OBJ
        private static List<Mesh> ParseObj(string path, string baseName)
        {
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var nor = new List<Vector3>();
            var result = new List<Mesh>();
            List<Vector3> vp = null, vn = null; List<Vector2> vu = null; List<int> idx = null;
            Dictionary<(int, int, int), int> map = null;
            string current = null;

            void Flush()
            {
                if (current == null || idx.Count == 0) return;
                var mesh = new Mesh { name = $"{baseName}_LOD{result.Count}" };
                if (vp.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vp); mesh.SetNormals(vn); mesh.SetUVs(0, vu); mesh.SetTriangles(idx, 0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                result.Add(mesh);
            }

            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                if (raw.StartsWith("v ")) pos.Add(ReadVec3(raw));
                else if (raw.StartsWith("vt ")) { string[] t = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries); uv.Add(new Vector2(F(t[1]), F(t[2]))); }
                else if (raw.StartsWith("vn ")) nor.Add(ReadVec3(raw));
                else if (raw.StartsWith("o "))
                {
                    Flush();
                    current = raw.Substring(2).Trim();
                    vp = new List<Vector3>(); vn = new List<Vector3>(); vu = new List<Vector2>(); idx = new List<int>();
                    map = new Dictionary<(int, int, int), int>();
                }
                else if (raw.StartsWith("f ") && current != null)
                {
                    string[] t = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var corner = new int[t.Length - 1];
                    for (int i = 1; i < t.Length; i++) corner[i - 1] = Vertex(t[i], pos, uv, nor, vp, vn, vu, map);
                    for (int i = 1; i < corner.Length - 1; i++) { idx.Add(corner[0]); idx.Add(corner[i + 1]); idx.Add(corner[i]); } // X inverse => ordre inverse
                }
            }
            Flush();
            return result;
        }

        private static int Vertex(string token, List<Vector3> pos, List<Vector2> uv, List<Vector3> nor,
            List<Vector3> vp, List<Vector3> vn, List<Vector2> vu, Dictionary<(int, int, int), int> map)
        {
            string[] p = token.Split('/');
            int a = int.Parse(p[0], CultureInfo.InvariantCulture) - 1;
            int b = p.Length > 1 && p[1].Length > 0 ? int.Parse(p[1], CultureInfo.InvariantCulture) - 1 : -1;
            int c = p.Length > 2 && p[2].Length > 0 ? int.Parse(p[2], CultureInfo.InvariantCulture) - 1 : -1;
            var key = (a, b, c);
            if (map.TryGetValue(key, out int found)) return found;
            Vector3 position = pos[a]; position.x = -position.x;                       // OBJ droitier -> Unity gaucher
            Vector3 normal = c >= 0 ? nor[c] : Vector3.up; normal.x = -normal.x;
            vp.Add(position); vn.Add(normal.normalized); vu.Add(b >= 0 ? uv[b] : Vector2.zero);
            map[key] = vp.Count - 1;
            return vp.Count - 1;
        }

        private static Vector3 ReadVec3(string line)
        {
            string[] t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new Vector3(F(t[1]), F(t[2]), F(t[3]));
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        #endregion

        #region Utilitaires
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        private static int Tris(Mesh m) => (int)m.GetIndexCount(0) / 3;

        private static string EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
            return path;
        }
        #endregion
    }
}
#endif
