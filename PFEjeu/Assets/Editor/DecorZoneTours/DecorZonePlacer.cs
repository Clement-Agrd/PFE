using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;

namespace Core.TowerDefense.EditorTools
{
    /// <summary>Echantillonne les lanes et disperse le petit decor en respectant les zones d'exclusion.</summary>
    internal static class DecorZonePlacer
    {
        #region Constants
        private const int Seed = 20260;
        private static readonly Vector2 ZoneCenter = new Vector2(90f, -76f);
        private const float ZoneRadius = 26f;
        private static readonly Vector2 HerseXZ = new Vector2(80.7f, -88.4f);
        private const float HerseExclusion = 9f;
        private const float LaneExclusion = 2.5f;     // du centre de la lane
        private const float LaneBandMax = 6f;         // bordure de lane
        private const float SlotExclusion = 3.2f;     // cercle de selection (r=2,1) + marge
        private const float SlotClusterMax = 6.5f;
        private const float MinSpacing = 1.4f;
        private const float LaneSampleStep = 0.002f; // splines jusqu'a ~180 m : pas ~0,35 m
        private const int MaxAttempts = 4000;

        private const string ScreePileA = "Assets/Arts/3D/ScreePileKit/Assets/ScreePile/Prefabs/ScreePile_A.prefab";
        private const string ScreePileB = "Assets/Arts/3D/ScreePileKit/Assets/ScreePile/Prefabs/ScreePile_B.prefab";
        private const string RockFbx = "Assets/Arts/3D/Rock.fbx";
        private const string Souche = "Assets/Arts/3D/Defense/souche_arbre.fbx";
        private const string TroncA = "Assets/Arts/3D/Defense/tronc_v1.fbx";
        private const string TroncB = "Assets/Arts/3D/Defense/tronc_v2.fbx";

        private readonly struct Kind
        {
            public readonly string Path; public readonly float MinScale, MaxScale; public readonly bool Cluster;
            public Kind(string p, float a, float b, bool c) { Path = p; MinScale = a; MaxScale = b; Cluster = c; }
        }

        // Bordure de lane (petits elements) et groupes autour des slots (souches/troncs/amas).
        private static readonly Kind[] LaneKinds =
        {
            new Kind(RockFbx, 0.07f, 0.12f, false), new Kind(ScreePileA, 0.22f, 0.34f, false),
            new Kind(ScreePileB, 0.22f, 0.34f, false),
        };
        private static readonly Kind[] ClusterKinds =
        {
            new Kind(Souche, 0.9f, 1.1f, true), new Kind(TroncA, 0.9f, 1.1f, true),
            new Kind(TroncB, 0.9f, 1.1f, true), new Kind(ScreePileA, 0.3f, 0.45f, true),
            new Kind(RockFbx, 0.09f, 0.15f, true),
        };
        private const int LaneCount = 16, ClusterCount = 14;
        #endregion

        #region Lanes
        /// <summary>Points XZ des splines ennemies (hors boucle "zone village") dans la zone.</summary>
        public static List<Vector3> SampleLanes()
        {
            var points = new List<Vector3>(512);
            foreach (var sc in Object.FindObjectsByType<SplineContainer>(FindObjectsSortMode.None))
            {
                if (sc.name == "zone village") continue;
                foreach (var spline in sc.Splines)
                {
                    if (spline.Count < 2) continue;
                    for (float t = 0f; t <= 1.0001f; t += LaneSampleStep)
                    {
                        Vector3 w = sc.transform.TransformPoint(SplineUtility.EvaluatePosition(spline, t));
                        if (Vector2.Distance(new Vector2(w.x, w.z), ZoneCenter) < ZoneRadius + 20f) points.Add(w);
                    }
                }
            }
            return points;
        }

        /// <summary>Comme <see cref="SampleLanes"/> mais sur toute la map (sert a adapter la marge des dalles).</summary>
        public static List<Vector3> SampleAllLanes()
        {
            var points = new List<Vector3>(4096);
            foreach (var sc in Object.FindObjectsByType<SplineContainer>(FindObjectsSortMode.None))
            {
                if (sc.name == "zone village") continue;
                foreach (var spline in sc.Splines)
                {
                    if (spline.Count < 2) continue;
                    for (float t = 0f; t <= 1.0001f; t += LaneSampleStep)
                        points.Add(sc.transform.TransformPoint(SplineUtility.EvaluatePosition(spline, t)));
                }
            }
            return points;
        }

        public static float DistanceToLanes(IReadOnlyList<Vector3> lanes, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < lanes.Count; i++)
            {
                float dx = lanes[i].x - p.x, dz = lanes[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }
        #endregion

        #region Scatter
        public static int Scatter(Transform root, IReadOnlyList<Transform> slots, Terrain terrain, IReadOnlyList<Vector3> lanes)
        {
            var rng = new System.Random(Seed);
            var placed = new List<Vector2>();
            int count = Fill(root, LaneKinds, LaneCount, false, slots, terrain, lanes, rng, placed);
            count += Fill(root, ClusterKinds, ClusterCount, true, slots, terrain, lanes, rng, placed);
            return count;
        }

        private static int Fill(Transform root, Kind[] kinds, int target, bool nearSlots, IReadOnlyList<Transform> slots,
                                Terrain terrain, IReadOnlyList<Vector3> lanes, System.Random rng, List<Vector2> placed)
        {
            int done = 0;
            for (int attempt = 0; attempt < MaxAttempts && done < target; attempt++)
            {
                Vector2 p = ZoneCenter + RandomInDisc(rng) * ZoneRadius;
                if (!IsValid(p, nearSlots, slots, lanes, placed)) continue;
                Vector3 pos = new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y, p.y);
                if (NavMesh.SamplePosition(pos, out _, 0.4f, NavMesh.AllAreas)) continue;

                Kind k = kinds[rng.Next(kinds.Length)];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k.Path);
                if (prefab == null) { Debug.LogWarning("[DecorZoneTours] asset manquant: " + k.Path); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                Undo.RegisterCreatedObjectUndo(go, "Decor");
                float scale = Mathf.Lerp(k.MinScale, k.MaxScale, (float)rng.NextDouble());
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                go.transform.localScale = go.transform.localScale * scale;
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                placed.Add(p);
                done++;
            }
            return done;
        }

        private static bool IsValid(Vector2 p, bool nearSlots, IReadOnlyList<Transform> slots, IReadOnlyList<Vector3> lanes, List<Vector2> placed)
        {
            if (Vector2.Distance(p, HerseXZ) < HerseExclusion) return false;
            float lane = DistanceToLanes(lanes, new Vector3(p.x, 0, p.y));
            if (lane < LaneExclusion) return false;

            float nearest = float.MaxValue;
            for (int i = 0; i < slots.Count; i++)
                nearest = Mathf.Min(nearest, Vector2.Distance(p, new Vector2(slots[i].position.x, slots[i].position.z)));
            if (nearest < SlotExclusion) return false;

            if (nearSlots) { if (nearest > SlotClusterMax) return false; }
            else if (lane > LaneBandMax) return false;

            for (int i = 0; i < placed.Count; i++)
                if (Vector2.Distance(p, placed[i]) < MinSpacing) return false;
            return true;
        }

        private static Vector2 RandomInDisc(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        #endregion
    }
}
