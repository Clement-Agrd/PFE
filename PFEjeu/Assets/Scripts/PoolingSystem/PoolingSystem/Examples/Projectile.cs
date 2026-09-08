using UnityEngine;

namespace Core.PoolingSystem.Examples
{
    /// <summary>
    /// Exemple : un projectile qui avance et revient AU POOL après 'lifetime'.
    /// Montre les hooks IPoolable et le retour automatique via PooledObject.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class Projectile : MonoBehaviour, IPoolable
    {
        [SerializeField] private float speed = 12f;
        [SerializeField] private float lifetime = 3f;

        private PooledObject _pooled;

        private void Awake() => _pooled = GetComponent<PooledObject>();

        public void OnSpawn()
        {
            // Réinitialise l'état au besoin (vélocité, trail...) puis programme le retour.
            _pooled.ReleaseAfter(lifetime);
        }

        public void OnDespawn()
        {
            // Nettoyage à la remise au pool (couper un effet, reset physique...).
        }

        private void Update()
        {
            transform.position += transform.forward * (speed * Time.deltaTime);
        }
    }
}
