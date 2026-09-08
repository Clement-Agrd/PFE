using UnityEngine;

namespace Core.PoolingSystem.Examples
{
    /// <summary>
    /// Exemple : tire un projectile depuis le pool à chaque appui sur Espace.
    /// (Ancien Input Manager ; adapte à l'Input System si besoin.)
    /// </summary>
    public sealed class ProjectileSpawner : MonoBehaviour
    {
        [SerializeField] private PoolManager poolManager;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform muzzle;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Space)) return;
            if (poolManager == null || projectilePrefab == null) return;

            Transform origin = muzzle != null ? muzzle : transform;
            poolManager.Spawn(projectilePrefab, origin.position, origin.rotation);
        }
    }
}
