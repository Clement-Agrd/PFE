using System.Collections.Generic;
using UnityEngine;

namespace Core.TowerDefense
{
    /// <summary>
    /// Le chemin que suivent les ennemis, du départ (point 0) jusqu'à la base
    /// (dernier point). Ajoute des GameObjects enfants comme points, ou assigne-les
    /// dans la liste. Un gizmo dessine le chemin dans la scène.
    /// </summary>
    public sealed class WaypointPath : MonoBehaviour
    {
        [Tooltip("Les points du chemin, dans l'ordre. Le dernier = la base.")]
        [SerializeField] private List<Transform> points = new();

        public int Count => points.Count;

        public Vector3 GetPoint(int index) => points[index].position;

        public Vector3 StartPoint => points.Count > 0 ? points[0].position : transform.position;
        public Vector3 EndPoint => points.Count > 0 ? points[points.Count - 1].position : transform.position;

        private void OnDrawGizmos()
        {
            if (points == null || points.Count < 2) return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null) continue;
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
                Gizmos.DrawWireSphere(points[i].position, 0.2f);
            }
            if (points[points.Count - 1] != null)
            {
                Gizmos.color = Color.red; // la base
                Gizmos.DrawWireSphere(points[points.Count - 1].position, 0.35f);
            }
        }
    }
}
