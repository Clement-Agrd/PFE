using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Génère un rempart modulaire le long d'une Spline (package com.unity.splines) :
/// - Place des tours à intervalle de distance régulier
/// - Instancie des segments de mur entre chaque tour, soit :
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
/// - wallSegmentPrefab : mesh orienté vers +Z (longueur), pivot au centre ou à une extrémité (voir wallPivotAtStart).
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
    [Tooltip("Distance en unités Unity entre deux tours consécutives.")]
    [SerializeField, Min(0.1f)] private float towerInterval = 10f;

    [Tooltip("Longueur \"native\" du prefab de mur sur son axe Z (avant étirement). Utilisé en mode rigide uniquement.")]
    [SerializeField, Min(0.01f)] private float wallSegmentNativeLength = 1f;

    [Tooltip("Si activé, le pivot du prefab de mur est considéré à une extrémité (Z=0) plutôt qu'au centre. Mode rigide uniquement.")]
    [SerializeField] private bool wallPivotAtStart = false;

    [Tooltip("Place une tour à la toute fin de la spline même si elle ne tombe pas pile sur l'intervalle.")]
    [SerializeField] private bool forceTowerAtEnd = true;

    [Header("Mode de déformation")]
    [Tooltip("Si activé : le mesh du mur est plié pour suivre la courbure exacte de la spline (façon Spline Mesh Component Unreal). " +
             "Si désactivé : le mur reste droit et est simplement étiré/tourné entre les deux tours.")]
    [SerializeField] private bool bendMeshAlongSpline = false;

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

        if (wallSegmentPrefab == null || towerPrefab == null)
        {
            Debug.LogWarning($"[{nameof(SplineWallBuilder)}] Prefabs manquants sur {name}.", this);
            return;
        }

        ClearGenerated();

        Spline spline = splineContainer.Spline;
        var distanceMap = new SplineDistanceMap(spline, distanceMapResolution);

        if (distanceMap.TotalLength <= 0.001f) return;

        List<float> towerDistances = ComputeTowerDistances(distanceMap.TotalLength);

        foreach (float distance in towerDistances)
        {
            float t = distanceMap.DistanceToT(distance);
            SplineUtility.Evaluate(spline, t, out float3 localPos, out float3 tangent, out float3 upVector);

            Vector3 worldPos = splineContainer.transform.TransformPoint(localPos);
            Quaternion rotation = Quaternion.LookRotation(
                splineContainer.transform.TransformDirection((Vector3)tangent),
                splineContainer.transform.TransformDirection((Vector3)upVector));

            SpawnPrefab(towerPrefab, worldPos, rotation);
        }

        for (int i = 0; i < towerDistances.Count - 1; i++)
        {
            if (bendMeshAlongSpline)
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
    /// Mode rigide : segment droit, positionné au milieu de l'écart, étiré en Z pour combler exactement la distance.
    /// </summary>
    private void BuildStraightWallSegment(Spline spline, SplineDistanceMap distanceMap, float distanceA, float distanceB)
    {
        float segmentLength = distanceB - distanceA;
        if (segmentLength <= 0.001f) return;

        float midDistance = (distanceA + distanceB) * 0.5f;
        float tMid = distanceMap.DistanceToT(midDistance);
        SplineUtility.Evaluate(spline, tMid, out float3 localPos, out float3 tangent, out float3 upVector);

        Vector3 worldPos = splineContainer.transform.TransformPoint(localPos);
        Quaternion rotation = Quaternion.LookRotation(
            splineContainer.transform.TransformDirection((Vector3)tangent),
            splineContainer.transform.TransformDirection((Vector3)upVector));

        GameObject instance = SpawnPrefab(wallSegmentPrefab, worldPos, rotation);

        float scaleZ = segmentLength / wallSegmentNativeLength;
        Vector3 scale = instance.transform.localScale;
        scale.z = scaleZ;
        instance.transform.localScale = scale;

        if (wallPivotAtStart)
        {
            instance.transform.position = worldPos - instance.transform.forward * (segmentLength * 0.5f);
        }
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

        Mesh bentMesh = SplineMeshDeformer.BendMeshAlongSpline(meshFilter.sharedMesh, spline, distanceMap, distanceA, distanceB);
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
