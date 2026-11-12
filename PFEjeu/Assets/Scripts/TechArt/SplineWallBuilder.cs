using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Génère un rempart modulaire le long d'une Spline (package com.unity.splines) :
/// - Découpe la spline en points réguliers (Tower Interval) et y place des tours — optionnel, voir spawnTowers
/// - Instancie des segments de mur entre chaque point de découpe, soit :
///     • étirés localement (rigide, rapide, mur reste droit visuellement)
///     • soit pliés pour suivre la courbure exacte de la spline (voir SplineMeshDeformer)
///
/// Le placement le long de la spline passe par SplineDistanceMap : une table de correspondance
/// distance → t construite une fois par régénération, plus fiable que l'API native sur des
/// splines avec peu de knots très espacés (évite les sauts/allers-retours).
///
/// Schéma :
///
///   Spline : o----------o----------o----------o
///           Tour      Mur        Tour        Mur
///          (T0)   (étiré/plié)   (T1)   (étiré/plié)
///
/// Pré-requis prefabs :
/// - wallSegmentPrefab : pivot au centre ou à une extrémité (voir wallPivotAtStart, mode rigide uniquement).
///   Vérifie le gizmo du prefab en Scene view pour savoir quel axe (X ou Z) correspond à sa longueur,
///   et renseigne wallLengthAxis en conséquence — sinon le mesh sera mal orienté/déformé.
///   Pour le mode "bend", le MeshFilter doit être sur la racine du prefab (pas sur un enfant décalé).
/// - towerPrefab       : pivot au centre de la base
/// </summary>
[ExecuteAlways]
public class SplineWallBuilder : MonoBehaviour
{
    #region Références

    [Header("Spline source")]
    [SerializeField] private SplineContainer splineContainer;

    [Header("Prefabs")]
    [SerializeField] private GameObject wallSegmentPrefab;
    [SerializeField] private GameObject towerPrefab;

    #endregion

    #region Paramètres

    [Header("Paramètres de génération")]
    [Tooltip("Si désactivé, aucune tour n'est instanciée : Tower Prefab devient optionnel et sert uniquement de " +
             "repère interne pour découper le mur en segments (utile pour garder une bonne précision sur une " +
             "grande spline), sans rien afficher à ces points.")]
    [SerializeField] private bool spawnTowers = true;

    [Tooltip("Distance en unités Unity entre deux points de découpe du mur (tours si Spawn Towers est activé, " +
             "sinon simples repères internes invisibles).")]
    [SerializeField, Min(0.1f)] private float towerInterval = 10f;

    [Tooltip("Longueur \"native\" du prefab de mur sur son axe de longueur (avant étirement). Utilisé en mode rigide uniquement.")]
    [SerializeField, Min(0.01f)] private float wallSegmentNativeLength = 1f;

    [Tooltip("Axe local du prefab de mur qui correspond à sa longueur (le sens qui doit suivre la spline). " +
             "Vérifie le gizmo du prefab en Scene view : la flèche alignée avec la grande dimension du panneau indique l'axe à choisir.")]
    [SerializeField] private SplineLengthAxis wallLengthAxis = SplineLengthAxis.Z;

    [Tooltip("Si activé, le pivot du prefab de mur est considéré à une extrémité plutôt qu'au centre. Mode rigide uniquement.")]
    [SerializeField] private bool wallPivotAtStart = false;

    [Tooltip("Place une tour à la toute fin de la spline même si elle ne tombe pas pile sur l'intervalle.")]
    [SerializeField] private bool forceTowerAtEnd = true;

    [Header("Mode de déformation")]
    [Tooltip("Si activé : le mesh du mur est plié pour suivre la courbure exacte de la spline (façon Spline Mesh Component Unreal). " +
             "Si désactivé : le mur reste droit et est simplement étiré/tourné entre les deux tours.")]
    [SerializeField] private bool bendMeshAlongSpline = false;

    [Tooltip("Suit la pente par marches : chaque module reste vertical et reçoit un BoxCollider ajusté.")]
    [SerializeField] private bool uprightSteppedSegments;

    [Header("Précision")]
    [Tooltip("Nombre d'échantillons utilisés pour mesurer la spline et convertir distance → position. " +
             "Augmente cette valeur si ta spline a peu de knots très espacés (grandes courbes) et que les segments " +
             "générés ne suivent pas bien le tracé. Valeur plus haute = plus précis mais un peu plus lent à régénérer.")]
    [SerializeField, Range(16, 1000)] private int distanceMapResolution = 200;

    [Header("Régénération auto (éditeur)")]
    [SerializeField] private bool autoRebuildInEditor = true;

    #endregion

    #region État interne

    private const string GeneratedRootName = "__Generated";
    private const string BentMeshSuffix = "_Bent";
    private Transform _generatedRoot;

    // -90° autour de Y : réaligne l'axe de longueur X du mesh sur l'axe canonique Z (= tangente),
    // sans réflexion (contrairement à un simple échange de composantes). Identité si l'axe est déjà Z.
    private static readonly Quaternion XAxisCorrection = Quaternion.Euler(0f, -90f, 0f);

    #endregion

    #region Cycle de vie Unity

    private void OnEnable()
    {
        if (autoRebuildInEditor && !Application.isPlaying)
            Rebuild();
    }

    private void OnValidate()
    {
        if (!autoRebuildInEditor || splineContainer == null) return;
        if (!Application.isPlaying)
            UnityEditor_DelayedRebuild();
    }

    #endregion

    #region API publique

    /// <summary>
    /// Point d'entrée principal : nettoie l'ancienne génération et reconstruit tout.
    /// </summary>
    [ContextMenu("Rebuild Wall")]
    public void Rebuild()
    {
        if (splineContainer == null || splineContainer.Spline == null)
        {
            Debug.LogWarning($"[{nameof(SplineWallBuilder)}] Aucune spline assignée sur {name}.", this);
            return;
        }

        if (wallSegmentPrefab == null)
        {
            Debug.LogWarning($"[{nameof(SplineWallBuilder)}] Wall Segment Prefab manquant sur {name}.", this);
            return;
        }

        if (spawnTowers && towerPrefab == null)
        {
            Debug.LogWarning($"[{nameof(SplineWallBuilder)}] Spawn Towers est activé mais Tower Prefab est vide sur {name}.", this);
            return;
        }

        ClearGenerated();

        Spline spline = splineContainer.Spline;
        var distanceMap = new SplineDistanceMap(spline, distanceMapResolution);

        if (distanceMap.TotalLength <= 0.001f) return;

        List<float> towerDistances = ComputeTowerDistances(distanceMap.TotalLength);

        if (spawnTowers)
        {
            foreach (float distance in towerDistances)
            {
                float t = distanceMap.DistanceToT(distance);
                SplineUtility.Evaluate(spline, t, out float3 localPos, out float3 tangent, out float3 upVector);

                Vector3 worldPos = splineContainer.transform.TransformPoint(localPos);
                Vector3 forward = splineContainer.transform.TransformDirection((Vector3)tangent);
                Quaternion rotation = uprightSteppedSegments
                    ? UprightRotation(forward)
                    : Quaternion.LookRotation(forward, splineContainer.transform.TransformDirection((Vector3)upVector));

                var tower = SpawnPrefab(towerPrefab, worldPos, rotation);
                if (uprightSteppedSegments) FitBoxCollider(tower, 0f);
            }
        }

        for (int i = 0; i < towerDistances.Count - 1; i++)
        {
            if (uprightSteppedSegments)
                BuildUprightWallSegment(spline, distanceMap, towerDistances[i], towerDistances[i + 1]);
            else if (bendMeshAlongSpline)
                BuildBentWallSegment(spline, distanceMap, towerDistances[i], towerDistances[i + 1]);
            else
                BuildStraightWallSegment(spline, distanceMap, towerDistances[i], towerDistances[i + 1]);
        }
    }

    #endregion

    #region Génération - logique interne

    private List<float> ComputeTowerDistances(float totalLength)
    {
        var distances = new List<float>();

        for (float d = 0f; d <= totalLength; d += towerInterval)
            distances.Add(d);

        if (forceTowerAtEnd)
        {
            float last = distances.Count > 0 ? distances[^1] : -1f;
            if (Mathf.Abs(last - totalLength) > 0.01f)
                distances.Add(totalLength);
        }

        return distances;
    }

    /// <summary>
    /// Mode rigide : segment droit, positionné au milieu de l'écart, étiré pour combler exactement la distance.
    /// </summary>
    private void BuildStraightWallSegment(Spline spline, SplineDistanceMap distanceMap, float distanceA, float distanceB)
    {
        float segmentLength = distanceB - distanceA;
        if (segmentLength <= 0.001f) return;

        float midDistance = (distanceA + distanceB) * 0.5f;
        float tMid = distanceMap.DistanceToT(midDistance);
        SplineUtility.Evaluate(spline, tMid, out float3 localPos, out float3 tangent, out float3 upVector);

        Vector3 worldPos = splineContainer.transform.TransformPoint(localPos);
        Vector3 worldTangentDir = splineContainer.transform.TransformDirection((Vector3)tangent).normalized;
        Vector3 worldUpDir = splineContainer.transform.TransformDirection((Vector3)upVector);

        Quaternion baseRotation = Quaternion.LookRotation(worldTangentDir, worldUpDir);
        Quaternion rotation = wallLengthAxis == SplineLengthAxis.X ? baseRotation * XAxisCorrection : baseRotation;

        GameObject instance = SpawnPrefab(wallSegmentPrefab, worldPos, rotation);

        float scaleAmount = segmentLength / wallSegmentNativeLength;
        Vector3 scale = instance.transform.localScale;
        if (wallLengthAxis == SplineLengthAxis.X) scale.x = scaleAmount;
        else scale.z = scaleAmount;
        instance.transform.localScale = scale;

        if (wallPivotAtStart)
        {
            // On utilise la tangente explicite plutôt que instance.transform.forward : ce dernier ne
            // pointe plus forcément le long de la spline une fois XAxisCorrection appliquée.
            instance.transform.position = worldPos - worldTangentDir * (segmentLength * 0.5f);
        }
    }

    private void BuildUprightWallSegment(Spline spline, SplineDistanceMap distanceMap, float distanceA, float distanceB)
    {
        Vector3 a = splineContainer.transform.TransformPoint((Vector3)SplineUtility.EvaluatePosition(spline, distanceMap.DistanceToT(distanceA)));
        Vector3 b = splineContainer.transform.TransformPoint((Vector3)SplineUtility.EvaluatePosition(spline, distanceMap.DistanceToT(distanceB)));
        Vector3 planar = Vector3.ProjectOnPlane(b - a, Vector3.up);
        if (planar.sqrMagnitude < .0001f) return;

        float length = planar.magnitude;
        Vector3 position = (a + b) * .5f;
        if (wallPivotAtStart) position -= planar * .5f;
        Quaternion rotation = UprightRotation(planar);
        if (wallLengthAxis == SplineLengthAxis.X) rotation *= XAxisCorrection;
        var segment = SpawnPrefab(wallSegmentPrefab, position, rotation);
        Vector3 scale = Vector3.one;
        float stretch = (length + .12f) / wallSegmentNativeLength;
        if (wallLengthAxis == SplineLengthAxis.X) scale.x = stretch;
        else scale.z = stretch;
        segment.transform.localScale = scale;
        FitBoxCollider(segment, Mathf.Abs(b.y - a.y) * .5f + .15f);
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(segment.transform);
#endif
    }

    private static Quaternion UprightRotation(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
    }

    private static void FitBoxCollider(GameObject segment, float depthBelow)
    {
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (var mesh in segment.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null) continue;
            Bounds local = mesh.sharedMesh.bounds;
            Matrix4x4 toRoot = segment.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z));
                if (hasBounds) bounds.Encapsulate(p);
                else { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
            }
        }
        if (!hasBounds) return;

        var box = segment.GetComponent<BoxCollider>();
        foreach (var collider in segment.GetComponentsInChildren<Collider>(true))
            if (collider != box) collider.enabled = false;
        if (box == null) box = segment.AddComponent<BoxCollider>();
        float localDepth = depthBelow / Mathf.Max(.001f, Mathf.Abs(segment.transform.lossyScale.y));
        box.center = bounds.center - Vector3.up * (localDepth * .5f);
        box.size = bounds.size + Vector3.up * localDepth;
        box.enabled = true;
        box.isTrigger = false;
    }

    /// <summary>
    /// Mode plié : le mesh du mur est déformé vertex par vertex pour suivre la courbure de la spline.
    /// Hypothèse : le SplineContainer a un scale de transform de (1,1,1) — sinon les vertices seront mal placés.
    /// </summary>
    private void BuildBentWallSegment(Spline spline, SplineDistanceMap distanceMap, float distanceA, float distanceB)
    {
        float segmentLength = distanceB - distanceA;
        if (segmentLength <= 0.001f) return;

        // L'objet est placé exactement au repère du SplineContainer : les vertices générés par le
        // déformeur sont déjà en espace local de la spline, donc cette transform les reproduit tels quels.
        GameObject instance = SpawnPrefab(
            wallSegmentPrefab,
            splineContainer.transform.position,
            splineContainer.transform.rotation);
        instance.transform.localScale = Vector3.one;

        MeshFilter meshFilter = instance.GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"[{nameof(SplineWallBuilder)}] wallSegmentPrefab n'a pas de MeshFilter valide sur sa racine, " +
                              "impossible de plier le mesh.", this);
            return;
        }

        Mesh bentMesh = SplineMeshDeformer.BendMeshAlongSpline(
            meshFilter.sharedMesh, spline, distanceMap, distanceA, distanceB, wallLengthAxis);
        if (bentMesh != null)
            meshFilter.mesh = bentMesh;
    }

    private GameObject SpawnPrefab(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject instance;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, GetOrCreateGeneratedRoot());
        else
            instance = Instantiate(prefab, GetOrCreateGeneratedRoot());
#else
        instance = Instantiate(prefab, GetOrCreateGeneratedRoot());
#endif

        instance.transform.SetPositionAndRotation(position, rotation);
        return instance;
    }

    private Transform GetOrCreateGeneratedRoot()
    {
        if (_generatedRoot != null) return _generatedRoot;

        Transform existing = transform.Find(GeneratedRootName);
        if (existing != null)
        {
            _generatedRoot = existing;
            return _generatedRoot;
        }

        var rootObj = new GameObject(GeneratedRootName);
        rootObj.transform.SetParent(transform, false);
        _generatedRoot = rootObj.transform;
        return _generatedRoot;
    }

    private void ClearGenerated()
    {
        Transform root = transform.Find(GeneratedRootName);
        if (root == null) return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;

            // Détruit aussi le mesh plié cloné (sinon fuite mémoire, il n'est pas rattaché à un asset).
            MeshFilter mf = child.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.EndsWith(BentMeshSuffix))
            {
                Mesh meshToDestroy = mf.sharedMesh;
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(meshToDestroy);
                else Destroy(meshToDestroy);
#else
                Destroy(meshToDestroy);
#endif
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (child != null) DestroyImmediate(child);
                };
                continue;
            }
#endif
            Destroy(child);
        }
    }

#if UNITY_EDITOR
    private bool _rebuildQueued;
    private void UnityEditor_DelayedRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            _rebuildQueued = false;
            if (this != null) Rebuild();
        };
    }
#endif

    #endregion
}
