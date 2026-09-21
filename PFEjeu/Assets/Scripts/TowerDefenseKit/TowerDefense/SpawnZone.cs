using UnityEngine;
using UnityEngine.AI;

namespace Core.WaveSystem
{
    /// <summary>
    /// Une aire de spawn (pas un point fixe) : les ennemis apparaissent dispersés
    /// dedans, façon "horde" plutôt qu'en file. Posée dans la scène, avec un Id
    /// que les SpawnEntry ciblent. Le point choisi est toujours posé sur le NavMesh.
    /// </summary>
    public sealed class SpawnZone : MonoBehaviour
    {
        [SerializeField] private string id = "Zone";
        [SerializeField, Min(0.5f)] private float radius = 4f;
        [SerializeField] private float navSampleRadius = 5f;

        public string Id => id;

        /// <summary>Un point aléatoire dans la zone, posé sur le NavMesh.</summary>
        public Vector3 GetRandomPoint()
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 point = transform.position + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(point, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                return hit.position;

            return transform.position;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.3f);
            Gizmos.DrawSphere(transform.position, radius);
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 1f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}