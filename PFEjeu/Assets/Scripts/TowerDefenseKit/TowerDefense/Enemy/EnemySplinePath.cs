using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace Core.TowerDefense
{
    /// <summary>
    /// Transforme une Spline Unity en une série de points
    /// que les NavMeshAgent peuvent suivre.
    ///
    /// Plus Sample Count est élevé, plus l'ennemi suit
    /// précisément la forme de la spline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySplinePath : MonoBehaviour
    {
        [Header("Spline")]

        [SerializeField]
        private SplineContainer splineContainer;


        [Header("Sampling")]

        [Tooltip(
            "Nombre de points générés le long de la spline."
        )]
        [SerializeField, Range(2, 300)]
        private int sampleCount = 80;


        [Header("NavMesh")]

        [Tooltip(
            "Replace les points de la spline sur le NavMesh."
        )]
        [SerializeField]
        private bool snapSamplesToNavMesh = true;

        [SerializeField, Min(0.05f)]
        private float navMeshSampleRadius = 1.5f;


        [Header("Debug")]

        [SerializeField]
        private bool drawGizmos = true;


        private Vector3[] _points =
            Array.Empty<Vector3>();


        public int Count =>
            _points != null
                ? _points.Length
                : 0;


        public bool IsValid =>
            splineContainer != null &&
            Count >= 2;


        private void Awake()
        {
            Rebuild();
        }


        // ============================================================
        // BUILD
        // ============================================================

        public void Rebuild()
        {
            if (splineContainer == null)
            {
                _points =
                    Array.Empty<Vector3>();

                return;
            }


            int count =
                Mathf.Max(
                    2,
                    sampleCount
                );


            _points =
                new Vector3[count];


            for (int i = 0;
                 i < count;
                 i++)
            {
                float t =
                    i /
                    (float)(count - 1);


                float3 evaluatedPosition =
                    splineContainer
                        .EvaluatePosition(t);


                Vector3 point =
                    new Vector3(
                        evaluatedPosition.x,
                        evaluatedPosition.y,
                        evaluatedPosition.z
                    );


                if (snapSamplesToNavMesh &&
                    NavMesh.SamplePosition(
                        point,
                        out NavMeshHit navHit,
                        navMeshSampleRadius,
                        NavMesh.AllAreas))
                {
                    point =
                        navHit.position;
                }


                _points[i] =
                    point;
            }
        }


        // ============================================================
        // QUERY
        // ============================================================

        public Vector3 GetPoint(
            int index)
        {
            if (_points == null ||
                _points.Length == 0)
            {
                return transform.position;
            }


            index =
                Mathf.Clamp(
                    index,
                    0,
                    _points.Length - 1
                );


            return _points[index];
        }


        public int FindClosestPointIndex(
            Vector3 worldPosition)
        {
            if (_points == null ||
                _points.Length == 0)
            {
                return 0;
            }


            int bestIndex = 0;

            float bestDistance =
                float.PositiveInfinity;


            for (int i = 0;
                 i < _points.Length;
                 i++)
            {
                float sqrDistance =
                    (
                        _points[i] -
                        worldPosition
                    ).sqrMagnitude;


                if (sqrDistance >=
                    bestDistance)
                {
                    continue;
                }


                bestDistance =
                    sqrDistance;

                bestIndex =
                    i;
            }


            return bestIndex;
        }


        // ============================================================
        // DEBUG
        // ============================================================

#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos ||
                splineContainer == null)
            {
                return;
            }


            int count =
                Mathf.Max(
                    2,
                    sampleCount
                );


            Vector3 previous =
                EvaluateEditorPoint(
                    0f
                );


            Gizmos.color =
                Color.cyan;


            for (int i = 1;
                 i < count;
                 i++)
            {
                float t =
                    i /
                    (float)(count - 1);


                Vector3 point =
                    EvaluateEditorPoint(t);


                Gizmos.DrawLine(
                    previous,
                    point
                );


                Gizmos.DrawWireSphere(
                    point,
                    0.08f
                );


                previous =
                    point;
            }
        }


        private Vector3 EvaluateEditorPoint(
            float t)
        {
            float3 position =
                splineContainer
                    .EvaluatePosition(t);


            return new Vector3(
                position.x,
                position.y,
                position.z
            );
        }

#endif
    }
}