using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    public enum RockSize
    {
        Small,
        Medium,
        Big,
        Cliff,
        /// <summary>Pans de falaise modulaires du kit (habillage des parois).</summary>
        Panel,
        /// <summary>Eboulis du kit (pied de falaise).</summary>
        Scree,
        /// <summary>Dalles plates du kit (belvedere).</summary>
        Slab,
        /// <summary>Reperes uniques du kit (jonction A, ruine B) : hors tirage aleatoire.</summary>
        Landmark,
    }

    /// <summary>Un prefab de roche et ses dimensions (calculees sur une instance temporaire).</summary>
    public sealed class RockAsset
    {
        public string Name;
        public RockSize Size;
        public GameObject Prefab;
        /// <summary>Unites monde a l'echelle du prefab, relatives au pivot du prefab.</summary>
        public Vector3 BoundsCenter;
        public Vector3 BoundsSize;
        public float BottomY;
        public float Radius;
        /// <summary>Centre et taille dans l'espace local de la racine (pour un BoxCollider).</summary>
        public Vector3 LocalCenter;
        public Vector3 LocalSize;
        /// <summary>Agrandissement applique aux rochers du kit (leurs formes sont plus petites que celles du pack).</summary>
        public float ScaleBoost = 1f;
        /// <summary>Vrai pour les prefabs du kit : echelle non uniforme et teintes variees.</summary>
        public bool IsKit;
        public int Lod0Triangles;
        public int Lod0Renderers;
    }

    /// <summary>Charge les prefabs de roche par taille. Arret explicite si un asset manque.</summary>
    public sealed class MountainRockCatalog
    {
        private readonly Dictionary<RockSize, List<RockAsset>> _bySize = new Dictionary<RockSize, List<RockAsset>>();

        public IReadOnlyList<RockAsset> Get(RockSize size) => _bySize[size];

        public static MountainRockCatalog Build(MountainRockProfile profile)
        {
            var catalog = new MountainRockCatalog();
            var missing = new List<string>();
            catalog.Load(profile, RockSize.Cliff, profile.CliffNames, missing);
            catalog.Load(profile, RockSize.Big, profile.BigNames, missing);
            catalog.Load(profile, RockSize.Medium, Concat(profile.MediumNames, profile.KitMediumPaths), missing);
            catalog.Load(profile, RockSize.Small, Concat(profile.SmallNames, profile.KitSmallPaths), missing);
            catalog.Load(profile, RockSize.Panel, profile.KitPanelPaths, missing);
            catalog.Load(profile, RockSize.Scree, profile.KitScreePaths, missing);
            catalog.Load(profile, RockSize.Slab, profile.KitSlabPaths, missing);
            catalog.Load(profile, RockSize.Landmark, profile.KitLandmarkPaths, missing);
            if (missing.Count > 0)
                throw new InvalidOperationException("Prefabs de roche manquants : " + string.Join(", ", missing));
            return catalog;
        }

        public RockAsset Pick(RockSize size, System.Random rng)
        {
            List<RockAsset> list = _bySize[size];
            return list[rng.Next(list.Count)];
        }

        public bool Has(RockSize size) => _bySize.TryGetValue(size, out List<RockAsset> list) && list.Count > 0;

        private static string[] Concat(string[] a, string[] b)
        {
            var result = new string[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }

        public RockAsset Find(string prefabName)
        {
            foreach (List<RockAsset> list in _bySize.Values)
            {
                foreach (RockAsset asset in list)
                {
                    if (asset.Name == prefabName) return asset;
                }
            }
            return null;
        }

        private void Load(MountainRockProfile profile, RockSize size, string[] names, List<string> missing)
        {
            var list = new List<RockAsset>();
            foreach (string entry in names)
            {
                // Entree commencant par "Assets/" : chemin complet (kit de roches) ; sinon nom dans le dossier du pack.
                string prefabName = entry;
                bool fullPath = entry.StartsWith("Assets/", StringComparison.Ordinal);
                string path = fullPath ? entry : profile.PrefabFolder + prefabName + ".prefab";
                if (fullPath) prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabName);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    missing.Add(path);
                    continue;
                }
                RockAsset measured = Measure(prefab, size, prefabName);
                if (fullPath)
                {
                    measured.IsKit = true;
                    measured.ScaleBoost = profile.KitScale(size);
                }
                list.Add(measured);
            }
            _bySize[size] = list;
        }

        private static RockAsset Measure(GameObject prefab, RockSize size, string prefabName)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                LODGroup group = temp.GetComponentInChildren<LODGroup>();
                Renderer[] renderers = group != null && group.lodCount > 0
                    ? group.GetLODs()[0].renderers
                    : temp.GetComponentsInChildren<Renderer>();

                bool first = true;
                Bounds bounds = default;
                int triangles = 0, rendererCount = 0;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                    rendererCount++;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null) triangles += filter.sharedMesh.triangles.Length / 3;
                }

                Vector3 scale = temp.transform.localScale;
                Vector3 inverse = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
                return new RockAsset
                {
                    Name = prefabName,
                    Size = size,
                    Prefab = prefab,
                    BoundsCenter = bounds.center,
                    BoundsSize = bounds.size,
                    BottomY = bounds.min.y,
                    Radius = 0.5f * Mathf.Max(bounds.size.x, bounds.size.z),
                    LocalCenter = Vector3.Scale(bounds.center, inverse),
                    LocalSize = Vector3.Scale(bounds.size, inverse),
                    Lod0Triangles = triangles,
                    Lod0Renderers = rendererCount,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }
    }
}
