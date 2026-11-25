using System.Collections.Generic;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Un modele de barriere mesure : boite locale (longueur = axe Z) et poids.</summary>
    internal readonly struct FenceVariantInfo
    {
        public readonly GameObject Prefab;
        public readonly Bounds Local;
        public readonly float Weight;

        public FenceVariantInfo(GameObject prefab, Bounds local, float weight)
        {
            Prefab = prefab;
            Local = local;
            Weight = weight;
        }

        public float Length => Local.size.z;
        public float Width => Local.size.x;
    }

    internal static class FenceMetrics
    {
        /// <summary>Boite englobante des meshes du prefab dans l'espace local de sa racine.</summary>
        internal static Bounds MeasurePrefab(GameObject prefab)
        {
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Bounds total = default;
            bool first = true;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;
                Vector3 min = mesh.min, max = mesh.max;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (first) { total = new Bounds(p, Vector3.zero); first = false; }
                    else total.Encapsulate(p);
                }
            }
            return total;
        }

        /// <summary>Mesure les modeles valides d'un groupe (prefab non nul, poids > 0, mesh present).</summary>
        internal static List<FenceVariantInfo> MeasureVariants(FenceGroup group)
        {
            var result = new List<FenceVariantInfo>(group.Variants.Count);
            foreach (var v in group.Variants)
            {
                if (v == null || v.Prefab == null || v.Weight <= 0f) continue;
                Bounds b = MeasurePrefab(v.Prefab);
                if (b.size.z < 0.05f) continue;
                result.Add(new FenceVariantInfo(v.Prefab, b, v.Weight));
            }
            return result;
        }

        /// <summary>Hauteur du terrain sous (x, z).</summary>
        internal static float Ground(Terrain terrain, float x, float z) =>
            terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }
}
