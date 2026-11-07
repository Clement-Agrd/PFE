using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Instancie les roches dans la scene (instances de prefab, assets inchanges) : colliders simplifies,
    /// ombres coupees sur les LOD lointains, flags statiques. Un sous-groupe par zone.
    /// </summary>
    public static class MountainRockSpawner
    {
        public struct Stats
        {
            public int count;
            public int triangles;
            public int renderers;
            public readonly Dictionary<RockZone, int> perZone;
            public readonly Dictionary<RockSize, int> perSize;

            public Stats(bool _)
            {
                count = 0; triangles = 0; renderers = 0;
                perZone = new Dictionary<RockZone, int>();
                perSize = new Dictionary<RockSize, int>();
            }
        }

        /// <summary>Materiaux froids a assigner aux instances (renseigne par le menu avant Spawn ; null = materiaux d'origine).</summary>
        public static MountainRockMaterials.Set ColdMaterials;

        private const int FirstLodWithoutShadow = 2;
        private const float BoxColliderShrink = 0.9f;

        /// <summary>Cree (ou recree) la racine et instancie les placements. Retourne la racine.</summary>
        public static GameObject Spawn(string rootName, IReadOnlyList<RockPlacement> placements, out Stats stats)
        {
            DestroyRoot(rootName);
            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Mountain : rochers");

            stats = new Stats(true);
            var groups = new Dictionary<RockZone, Transform>();
            for (int i = 0; i < placements.Count; i++)
            {
                RockPlacement placement = placements[i];
                Transform parent = GroupFor(root.transform, groups, placement.zone);
                Instantiate(placement, parent, i);
                Count(ref stats, placement);
            }
            return root;
        }

        /// <summary>
        /// Racine "hero" : creee si absente, jamais videe ni recreee. Un groupe deja present est conserve tel quel.
        /// Retourne false si le groupe existait deja (rien n'est instancie).
        /// </summary>
        public static bool SpawnHeroGroup(string rootName, string groupName, IReadOnlyList<RockPlacement> placements)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
                Undo.RegisterCreatedObjectUndo(root, "Mountain : racine hero");
            }
            if (root.transform.Find(groupName) != null) return false;

            var group = new GameObject(groupName);
            group.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(group, "Mountain : groupe hero");
            for (int i = 0; i < placements.Count; i++) Instantiate(placements[i], group.transform, i);
            return true;
        }

        public static void DestroyRoot(string rootName)
        {
            GameObject existing = GameObject.Find(rootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);
        }

        private static Transform GroupFor(Transform root, Dictionary<RockZone, Transform> groups, RockZone zone)
        {
            if (groups.TryGetValue(zone, out Transform group)) return group;
            var go = new GameObject(zone.ToString());
            go.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(go, "Mountain : groupe de rochers");
            groups[zone] = go.transform;
            return go.transform;
        }

        public static GameObject Instantiate(RockPlacement placement, Transform parent, int index)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(placement.asset.Prefab, parent);
            go.name = placement.zone + "_" + index.ToString("D3") + "_" + placement.asset.Name;
            Transform t = go.transform;
            t.SetPositionAndRotation(placement.position, placement.rotation);
            t.localScale = Vector3.Scale(placement.asset.Prefab.transform.localScale * placement.scale, placement.stretch == Vector3.zero ? Vector3.one : placement.stretch);
            Undo.RegisterCreatedObjectUndo(go, "Mountain : roche");

            MountainRockMaterials.Apply(go, ColdMaterials, placement.position.y, index * 7919 + (index >> 2));
            SimplifyColliders(go, placement.asset);
            CutFarShadows(go);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>
        /// Falaises : MeshCollider d'origine. Grands et moyens : BoxCollider. Petits : aucun collider.
        /// Le MeshCollider du prefab ne peut pas etre supprime sur une instance : il est desactive.
        /// </summary>
        private static void SimplifyColliders(GameObject go, RockAsset asset)
        {
            // Falaises, pans, dalles et reperes du kit : collider simplifie fourni par le prefab.
            if (asset.Size == RockSize.Cliff || asset.Size == RockSize.Panel || asset.Size == RockSize.Slab || asset.Size == RockSize.Landmark) return;

            foreach (MeshCollider mesh in go.GetComponentsInChildren<MeshCollider>(true)) mesh.enabled = false;
            if (asset.Size == RockSize.Small || asset.Size == RockSize.Scree) return;

            var box = go.AddComponent<BoxCollider>();
            box.center = asset.LocalCenter;
            box.size = asset.LocalSize * BoxColliderShrink;
        }

        private static void CutFarShadows(GameObject go)
        {
            var group = go.GetComponentInChildren<LODGroup>();
            if (group == null) return;
            LOD[] lods = group.GetLODs();
            for (int i = FirstLodWithoutShadow; i < lods.Length; i++)
            {
                foreach (Renderer renderer in lods[i].renderers)
                {
                    if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private static void Count(ref Stats stats, RockPlacement placement)
        {
            stats.count++;
            stats.triangles += placement.asset.Lod0Triangles;
            stats.renderers += placement.asset.Lod0Renderers;
            stats.perZone.TryGetValue(placement.zone, out int z);
            stats.perZone[placement.zone] = z + 1;
            stats.perSize.TryGetValue(placement.asset.Size, out int s);
            stats.perSize[placement.asset.Size] = s + 1;
        }
    }
}
