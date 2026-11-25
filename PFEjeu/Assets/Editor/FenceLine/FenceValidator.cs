using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Controle les barrieres generees : references manquantes, flottement, enfoncement, blocage.</summary>
    internal static class FenceValidator
    {
        private const float MaxFloating = 0.05f;
        private const float MaxBuried = 0.3f;

        internal static string Validate(FenceLinePath path)
        {
            var terrain = Terrain.activeTerrain;
            var report = new StringBuilder();
            if (terrain == null) return "Aucun terrain actif.";

            var cache = new Dictionary<GameObject, Bounds>();
            for (int g = 0; g < path.Groups.Count; g++)
            {
                var group = path.Group(g);
                int missing = 0, floating = 0, buried = 0, noBlocker = 0;
                float maxFloat = 0f, maxBuried = 0f;

                foreach (var piece in group.Generated)
                {
                    if (piece == null) { missing++; continue; }
                    if (group.GenerateCollider && piece.GetComponent<Collider>() == null) noBlocker++;

                    var source = PrefabUtility.GetCorrespondingObjectFromSource(piece);
                    if (source == null) continue;
                    if (!cache.TryGetValue(source, out Bounds local)) cache[source] = local = FenceMetrics.MeasurePrefab(source);

                    Measure(piece.transform, local, terrain, out float up, out float down);
                    maxFloat = Mathf.Max(maxFloat, up);
                    maxBuried = Mathf.Max(maxBuried, down);
                    if (up > MaxFloating) floating++;
                    if (down > MaxBuried) buried++;
                }

                report.AppendLine($"[{g}] {group.Name} : {group.Generated.Count} barrieres | flottantes {floating} (max {maxFloat:F2} m) | " +
                                  $"enterrees {buried} (max {maxBuried:F2} m) | sans collider {noBlocker} | references perdues {missing}");
            }
            return report.Length == 0 ? "Aucun groupe." : report.ToString();
        }

        /// <summary>Compare la base du modele (3 points le long de sa longueur) au terrain.</summary>
        private static void Measure(Transform piece, Bounds local, Terrain terrain, out float maxUp, out float maxDown)
        {
            maxUp = float.MinValue;
            maxDown = float.MinValue;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 basePoint = new Vector3(local.center.x, local.min.y, local.center.z + k * local.size.z * 0.5f);
                Vector3 w = piece.TransformPoint(basePoint);
                float ground = FenceMetrics.Ground(terrain, w.x, w.z);
                maxUp = Mathf.Max(maxUp, w.y - ground);
                maxDown = Mathf.Max(maxDown, ground - w.y);
            }
            maxUp = Mathf.Max(0f, maxUp);
            maxDown = Mathf.Max(0f, maxDown);
        }
    }
}
