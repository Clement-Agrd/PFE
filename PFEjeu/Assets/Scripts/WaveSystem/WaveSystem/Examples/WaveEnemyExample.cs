using System;
using UnityEngine;
using Core.PoolingSystem;

namespace Core.WaveSystem.Examples
{
    /// <summary>
    /// Ennemi de démo : meurt après 'lifetime' secondes, crie Defeated, puis
    /// retourne au pool (ou se détruit). Réinitialisé à chaque spawn via OnEnable,
    /// donc compatible avec le pooling.
    ///
    /// En vrai jeu, tu remplaces le timer par : health.OnDeath += () => Die();
    /// </summary>
    public sealed class WaveEnemyExample : MonoBehaviour, IWaveEnemy
    {
        [SerializeField] private float lifetime = 3f;

        public event Action<IWaveEnemy> Defeated;

        private float _dieAt;
        private bool _dead;

        private void OnEnable()
        {
            // Reset à chaque (ré)apparition depuis le pool.
            _dead = false;
            _dieAt = Time.time + lifetime;
        }

        private void Update()
        {
            if (!_dead && Time.time >= _dieAt)
                Die();
        }

        private void Die()
        {
            if (_dead) return;
            _dead = true;

            Defeated?.Invoke(this); // prévient le spawner (décrémente les vivants)

            if (TryGetComponent(out PooledObject pooled))
                pooled.Release();   // retour au pool
            else
                Destroy(gameObject);
        }
    }
}
