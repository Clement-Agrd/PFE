using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Core.TowerDefense.Fences.EditorTools
{
    internal readonly struct GenerationResult
    {
        public readonly int Placed, Skipped, Conflicts;
        public GenerationResult(int placed, int skipped, int conflicts) { Placed = placed; Skipped = skipped; Conflicts = conflicts; }
        public static GenerationResult operator +(GenerationResult a, GenerationResult b) =>
            new GenerationResult(a.Placed + b.Placed, a.Skipped + b.Skipped, a.Conflicts + b.Conflicts);
    }

    /// <summary>
    /// Genere / efface les barrieres d'un groupe (Undo-able, idempotent). Chaque groupe ne nettoie que ses
    /// propres instances (references gardees sur le groupe, aucune recherche par nom).
    /// </summary>
    internal static class FenceGenerator
    {
        private const float MaxSlopeDegrees = 35f;
        private const int MaxWarnings = 25;
        private const int SeedSalt = 7919;
        private const int FitSamples = 5;
        private const float FitHalfWidth = 0.3f;
        private const float FitBury = 0.05f;

        #region Clear
        internal static void Clear(FenceLinePath path, int groupIndex)
        {
            var group = path.Group(groupIndex);
            Undo.RecordObject(path, "Effacer barrieres");
            if (group.GeneratedRoot != null) Undo.DestroyObjectImmediate(group.GeneratedRoot.gameObject);
            var leftovers = group.Generated;
            for (int i = 0; i < leftovers.Count; i++)
                if (leftovers[i] != null) Undo.DestroyObjectImmediate(leftovers[i]);
            group.SetGenerated(null, new List<GameObject>());

            var root = path.GeneratedRoot;
            if (root != null && root.childCount == 0)
            {
                Undo.DestroyObjectImmediate(root.gameObject);
                path.SetGeneratedRoot(null);
            }
            EditorUtility.SetDirty(path);
        }

        internal static void ClearAll(FenceLinePath path)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Effacer toutes les barrieres");
            for (int i = 0; i < path.Groups.Count; i++) Clear(path, i);
        }
        #endregion

        #region Generate
        internal static GenerationResult GenerateAll(FenceLinePath path)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generer toutes les barrieres");
            var scanner = new FenceConflictScanner();
            GenerationResult total = default;
            for (int i = 0; i < path.Groups.Count; i++)
                if (path.Group(i).Enabled) total += GenerateCore(path, i, scanner);
            return total;
        }

        internal static GenerationResult Generate(FenceLinePath path, int groupIndex)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generer barrieres");
            return GenerateCore(path, groupIndex, new FenceConflictScanner());
        }

        private static GenerationResult GenerateCore(FenceLinePath path, int groupIndex, FenceConflictScanner scanner)
        {
            var group = path.Group(groupIndex);
            var terrain = Terrain.activeTerrain;
            var variants = FenceMetrics.MeasureVariants(group);
            if (variants.Count == 0) { Debug.LogError($"[FenceLinePath] Groupe '{group.Name}' : aucun modele valide.", path); return default; }
            if (terrain == null) { Debug.LogError("[FenceLinePath] Aucun terrain actif.", path); return default; }
            if (group.Points.Count < 2) { Debug.LogWarning($"[FenceLinePath] Groupe '{group.Name}' : au moins 2 points requis.", path); return default; }

            Clear(path, groupIndex);
            Transform groupRoot = CreateGroupRoot(path, group, groupIndex);
            var rng = new System.Random(group.Seed + SeedSalt * groupIndex);

            var plans = new List<PiecePlan>(128);
            PlanGroup(group, variants, rng, plans);

            var created = new List<GameObject>(plans.Count);
            int skipped = 0, conflicts = 0, warnings = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                Vector3 pos = PiecePosition(plans[i], terrain, group, rng, out Quaternion rot);
                bool hit = scanner.Check(pos, out bool onLane, out string what);
                if (hit) conflicts++;
                if (hit && ShouldSkip(group.ConflictPolicy, onLane)) { skipped++; continue; }

                var go = CreatePiece(plans[i], variants[plans[i].Variant], pos, rot, group, groupRoot);
                created.Add(go);
                if (hit && warnings++ < MaxWarnings)
                    Debug.LogWarning($"[FenceLinePath] '{group.Name}' barriere en {pos:F1} recoupe {what}.", go);
            }
            if (conflicts > MaxWarnings)
                Debug.LogWarning($"[FenceLinePath] '{group.Name}' : {conflicts - MaxWarnings} conflits supplementaires non detailles.", path);
            if (skipped > 0)
                Debug.Log($"[FenceLinePath] '{group.Name}' : {skipped} barrieres ignorees (politique de conflit).", path);

            Undo.RecordObject(path, "Generer barrieres");
            group.SetGenerated(groupRoot, created);
            EditorUtility.SetDirty(path);
            return new GenerationResult(created.Count, skipped, conflicts);
        }

        private static bool ShouldSkip(FenceConflictPolicy policy, bool onLane) =>
            policy == FenceConflictPolicy.SkipOnLanesAndSlots || (policy == FenceConflictPolicy.SkipOnLanes && onLane);

        private static void PlanGroup(FenceGroup group, List<FenceVariantInfo> variants, System.Random rng, List<PiecePlan> plans)
        {
            var pts = group.Points;
            bool closed = group.ClosedLoop && pts.Count > 2;
            int segments = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
                bool startInterior = closed || i > 0;
                bool endInterior = closed || i < segments - 1;
                FenceLayout.PlanSegment(a, b, startInterior, endInterior, variants, group, rng, plans);
            }
        }
        #endregion

        #region Pieces
        private static Transform CreateGroupRoot(FenceLinePath path, FenceGroup group, int index)
        {
            Transform root = path.GeneratedRoot;
            if (root == null)
            {
                var rootGo = new GameObject(FenceLinePath.GeneratedRootName);
                Undo.RegisterCreatedObjectUndo(rootGo, "Fences_Generated");
                rootGo.transform.SetParent(path.transform, false);
                path.SetGeneratedRoot(rootGo.transform);
                root = rootGo.transform;
            }
            var go = new GameObject($"Group_{index:00}_{group.Name}");
            Undo.RegisterCreatedObjectUndo(go, "Group root");
            go.transform.SetParent(root, false);
            return go.transform;
        }

        private static Vector3 PiecePosition(PiecePlan plan, Terrain terrain, FenceGroup group, System.Random rng, out Quaternion rotation)
        {
            Vector3 dir = plan.Direction;
            Vector3 line = plan.LinePoint;
            if (group.LateralJitter > 0f)
            {
                var perp = new Vector3(-dir.z, 0f, dir.x);
                line += perp * (((float)rng.NextDouble() * 2f - 1f) * group.LateralJitter);
            }

            float half = plan.Length * 0.5f;
            float groundA = FenceMetrics.Ground(terrain, line.x - dir.x * half, line.z - dir.z * half);
            float groundB = FenceMetrics.Ground(terrain, line.x + dir.x * half, line.z + dir.z * half);
            Vector3 forward = dir;
            float slope = 0f; // montee par metre le long de la barriere
            if (group.AlignToSlope)
            {
                float maxRise = plan.Length * Mathf.Tan(MaxSlopeDegrees * Mathf.Deg2Rad);
                float rise = Mathf.Clamp(groundB - groundA, -maxRise, maxRise);
                forward = new Vector3(dir.x * plan.Length, rise, dir.z * plan.Length).normalized;
                slope = rise / plan.Length;
            }
            float y = FitHeight(terrain, line, dir, half, slope);

            rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (group.YawJitter > 0f)
                rotation *= Quaternion.AngleAxis(((float)rng.NextDouble() * 2f - 1f) * group.YawJitter, Vector3.up);
            return new Vector3(line.x, y + group.VerticalOffset, line.z);
        }

        /// <summary>
        /// Hauteur au centre qui minimise l'ecart max (flottement / enfoncement) entre la base de la barriere
        /// et le terrain, echantillonne le long de la barriere et sur ses deux bords (terrain irregulier).
        /// </summary>
        private static float FitHeight(Terrain terrain, Vector3 line, Vector3 dir, float half, float slope)
        {
            var perp = new Vector3(-dir.z, 0f, dir.x) * FitHalfWidth;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < FitSamples; i++)
            {
                float s = Mathf.Lerp(-half, half, i / (FitSamples - 1f));
                Vector3 p = line + dir * s;
                for (int side = -1; side <= 1; side += 2)
                {
                    float ground = FenceMetrics.Ground(terrain, p.x + perp.x * side, p.z + perp.z * side);
                    float residual = ground - slope * s;
                    if (residual < min) min = residual;
                    if (residual > max) max = residual;
                }
            }
            return (min + max) * 0.5f - FitBury; // legere preference pour l enfoncement (jamais de flottement visible)
        }

        private static GameObject CreatePiece(PiecePlan plan, FenceVariantInfo variant, Vector3 linePos, Quaternion rot,
                                              FenceGroup group, Transform parent)
        {
            // Centre la boite du modele sur la ligne et pose sa base au sol (le pivot peut etre decale, ex. Barrere_v3).
            Vector3 centerOffset = new Vector3(variant.Local.center.x, variant.Local.min.y, variant.Local.center.z) * plan.Scale;
            Vector3 pos = linePos - rot * centerOffset;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(variant.Prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Barriere");
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * plan.Scale;
            ApplyOutputSettings(go, group);
            AddBlockers(go, variant.Local, group);
            return go;
        }

        private static void ApplyOutputSettings(GameObject go, FenceGroup group)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>())
            {
                t.gameObject.layer = group.Layer;
                if (group.MarkBatchingStatic)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject) | StaticEditorFlags.BatchingStatic);
            }
        }

        /// <summary>
        /// BoxCollider plein (bloque joueur et ennemis) et un NavMeshObstacle par instance : la boite suit chaque
        /// barriere orientee et reste coherente avec la herse (Box, carving, carveOnlyStationary=false).
        /// </summary>
        private static void AddBlockers(GameObject go, Bounds local, FenceGroup group)
        {
            if (group.GenerateCollider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size;
            }
            if (!group.GenerateNavMeshObstacles) return;
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = local.center;
            obstacle.size = local.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
        }
        #endregion
    }
}
