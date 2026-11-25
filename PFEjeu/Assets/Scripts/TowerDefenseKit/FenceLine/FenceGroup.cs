using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.TowerDefense.Fences
{
    /// <summary>Que faire quand une barriere tombe sur une lane ennemie ou un slot de tour.</summary>
    public enum FenceConflictPolicy
    {
        WarnOnly = 0,
        SkipOnLanes = 1,
        SkipOnLanesAndSlots = 2,
    }

    /// <summary>Un modele de barriere et son poids de tirage.</summary>
    [Serializable]
    public sealed class FenceVariant
    {
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0f)] private float weight = 1f;

        public GameObject Prefab => prefab;
        public float Weight => weight;

        public FenceVariant() { }
        public FenceVariant(GameObject prefab, float weight)
        {
            this.prefab = prefab;
            this.weight = weight;
        }
    }

    /// <summary>
    /// Un trace independant (polyligne ouverte ou boucle) avec ses propres reglages et ses propres
    /// barrieres generees. Donnees seulement : la logique vit dans l'assembly Editor.
    /// </summary>
    [Serializable]
    public sealed class FenceGroup
    {
        [SerializeField] private string name = "Groupe";
        [SerializeField] private bool enabled = true;
        [SerializeField] private Color gizmoColor = new Color(1f, 0.75f, 0.1f);
        [SerializeField] private bool closedLoop;

        [Header("Modeles (tirage pondere, sans repetition consecutive)")]
        [SerializeField] private List<FenceVariant> variants = new List<FenceVariant>();
        [SerializeField, Min(0.1f)] private float prefabScale = 1f;

        [Header("Placement")]
        [Tooltip("Decalage vertical du pivot (corrige enfoncement / flottement).")]
        [SerializeField] private float verticalOffset;
        [Tooltip("Ecart entre barrieres (m). Positif = trou, negatif = chevauchement.")]
        [SerializeField] private float segmentGap;
        [Tooltip("Incline chaque barriere pour suivre la pente (max 35 deg).")]
        [SerializeField] private bool alignToSlope = true;

        [Header("Variation (deterministe via la graine)")]
        [SerializeField, Range(0f, 15f)] private float yawJitter;
        [SerializeField, Range(0f, 0.3f)] private float scaleJitter;
        [SerializeField, Range(0f, 0.5f)] private float lateralJitter;
        [SerializeField] private int seed = 1234;

        [Header("Blocage")]
        [SerializeField] private bool generateCollider = true;
        [Tooltip("Un NavMeshObstacle (carve) par barriere. Desactiver pour previsualiser sans toucher au NavMesh.")]
        [SerializeField] private bool generateNavMeshObstacles = true;
        [SerializeField] private FenceConflictPolicy conflictPolicy = FenceConflictPolicy.WarnOnly;

        [Header("Sortie")]
        [SerializeField, Range(0, 31)] private int layer;
        [SerializeField] private bool markBatchingStatic = true;

        [SerializeField, HideInInspector] private List<Vector3> points = new List<Vector3>();
        [SerializeField, HideInInspector] private Transform generatedRoot;
        [SerializeField, HideInInspector] private List<GameObject> generated = new List<GameObject>();

        #region Read access
        public string Name => name;
        public bool Enabled => enabled;
        public Color GizmoColor => gizmoColor;
        public bool ClosedLoop => closedLoop;
        public IReadOnlyList<FenceVariant> Variants => variants;
        public float PrefabScale => prefabScale;
        public float VerticalOffset => verticalOffset;
        public float SegmentGap => segmentGap;
        public bool AlignToSlope => alignToSlope;
        public float YawJitter => yawJitter;
        public float ScaleJitter => scaleJitter;
        public float LateralJitter => lateralJitter;
        public int Seed => seed;
        public bool GenerateCollider => generateCollider;
        public bool GenerateNavMeshObstacles => generateNavMeshObstacles;
        public FenceConflictPolicy ConflictPolicy => conflictPolicy;
        public int Layer => layer;
        public bool MarkBatchingStatic => markBatchingStatic;
        public IReadOnlyList<Vector3> Points => points;
        public Transform GeneratedRoot => generatedRoot;
        public IReadOnlyList<GameObject> Generated => generated;
        #endregion

        #region Construction / points
        public FenceGroup() { }

        public FenceGroup(string groupName, Color color, GameObject defaultPrefab)
        {
            name = groupName;
            gizmoColor = color;
            if (defaultPrefab != null) variants.Add(new FenceVariant(defaultPrefab, 1f));
        }

        public FenceGroup CloneSettings(string newName)
        {
            var copy = new FenceGroup { name = newName, gizmoColor = gizmoColor, closedLoop = closedLoop,
                prefabScale = prefabScale, verticalOffset = verticalOffset, segmentGap = segmentGap,
                alignToSlope = alignToSlope, yawJitter = yawJitter, scaleJitter = scaleJitter,
                lateralJitter = lateralJitter, seed = seed, generateCollider = generateCollider,
                generateNavMeshObstacles = generateNavMeshObstacles, conflictPolicy = conflictPolicy,
                layer = layer, markBatchingStatic = markBatchingStatic };
            foreach (var v in variants) copy.variants.Add(new FenceVariant(v.Prefab, v.Weight));
            return copy;
        }

        public void AddPoint(Vector3 world) => points.Add(world);
        public void InsertPoint(int index, Vector3 world) => points.Insert(index, world);
        public void SetPoint(int index, Vector3 world) => points[index] = world;
        public void RemovePointAt(int index) => points.RemoveAt(index);
        public void ReversePoints() => points.Reverse();

        public bool SwapPoints(int a, int b)
        {
            if (a < 0 || b < 0 || a >= points.Count || b >= points.Count) return false;
            (points[a], points[b]) = (points[b], points[a]);
            return true;
        }
        #endregion

        #region Generated references
        public void SetGenerated(Transform root, List<GameObject> instances)
        {
            generatedRoot = root;
            generated = instances;
        }
        #endregion
    }
}
