using UnityEngine;
using UnityEngine.AI;
using Core.TowerDefense;

namespace Core.WaveSystem
{
    /// <summary>
    /// Zone dans laquelle les ennemis apparaissent.
    ///
    /// Chaque zone peut également définir la spline
    /// que les ennemis spawnés ici devront suivre.
    /// </summary>
    public sealed class SpawnZone : MonoBehaviour
    {
        [Header("Identification")]

        [SerializeField]
        private string id = "Zone";


        [Header("Spawn")]

        [SerializeField, Min(0.5f)]
        private float radius = 4f;

        [SerializeField, Min(0.1f)]
        private float navSampleRadius = 5f;


        [Header("Chemin des ennemis")]

        [Tooltip(
            "Spline que suivront les ennemis spawnés dans cette zone."
        )]
        [SerializeField]
        private EnemySplinePath enemyPath;


        public string Id =>
            id;


        public EnemySplinePath EnemyPath =>
            enemyPath;


        /// <summary>
        /// Retourne un point aléatoire dans la zone,
        /// replacé sur le NavMesh.
        /// </summary>
        public Vector3 GetRandomPoint()
        {
            Vector2 offset =
                Random.insideUnitCircle *
                radius;


            Vector3 point =
                transform.position +
                new Vector3(
                    offset.x,
                    0f,
                    offset.y
                );


            if (NavMesh.SamplePosition(
                    point,
                    out NavMeshHit hit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                return hit.position;
            }


            return transform.position;
        }


#if UNITY_EDITOR

        private void OnDrawGizmos()
        {
            Gizmos.color =
                new Color(
                    1f,
                    0.4f,
                    0.1f,
                    0.3f
                );


            Gizmos.DrawSphere(
                transform.position,
                radius
            );


            Gizmos.color =
                new Color(
                    1f,
                    0.4f,
                    0.1f,
                    1f
                );


            Gizmos.DrawWireSphere(
                transform.position,
                radius
            );


            if (enemyPath != null)
            {
                Gizmos.color =
                    Color.cyan;


                Gizmos.DrawLine(
                    transform.position,
                    enemyPath.transform.position
                );
            }
        }

#endif
    }
}