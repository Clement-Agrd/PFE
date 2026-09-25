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

        [Tooltip("Nombre maximal de tentatives pour trouver un point valide sur le NavMesh.")]
        [SerializeField, Min(1)]
        private int maxSpawnAttempts = 12;

        [Header("Chemin des ennemis")]
        [Tooltip("Spline que suivront les ennemis spawnés dans cette zone.")]
        [SerializeField]
        private EnemySplinePath enemyPath;

        public string Id => id;
        public EnemySplinePath EnemyPath => enemyPath;

        /// <summary>
        /// Cherche un point aléatoire valide sur le NavMesh.
        /// Retourne false si aucun point valide n'a pu être trouvé.
        /// </summary>
        public bool TryGetRandomPoint(out Vector3 position)
        {
            int attempts = Mathf.Max(1, maxSpawnAttempts);

            for (int i = 0; i < attempts; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;

                Vector3 candidate =
                    transform.position +
                    new Vector3(
                        offset.x,
                        0f,
                        offset.y
                    );

                if (NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit hit,
                        navSampleRadius,
                        NavMesh.AllAreas))
                {
                    position = hit.position;
                    return true;
                }
            }

            // Dernière tentative au centre de la zone.
            if (NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit centerHit,
                    navSampleRadius,
                    NavMesh.AllAreas))
            {
                position = centerHit.position;
                return true;
            }

            position = default;

            Debug.LogError(
                $"[SpawnZone] Aucun NavMesh valide trouvé autour de '{name}'. " +
                $"Vérifie le NavMesh, l'Agent Type et la position de la SpawnZone.",
                this
            );

            return false;
        }

        /// <summary>
        /// Compatibilité avec l'ancien code.
        /// Préférer TryGetRandomPoint pour pouvoir gérer un échec proprement.
        /// </summary>
        public Vector3 GetRandomPoint()
        {
            return TryGetRandomPoint(out Vector3 position)
                ? position
                : transform.position;
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
