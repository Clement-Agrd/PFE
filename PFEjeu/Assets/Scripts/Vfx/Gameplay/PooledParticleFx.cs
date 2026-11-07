using Core.PoolingSystem;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Effet de particules generique recupere via le PoolManager : rejoue ses systemes a chaque OnSpawn
    /// et se rend au pool apres sa duree. Utilise pour le portail de spawn, l'impact et la mort.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledParticleFx : MonoBehaviour, IVfxPlayer, IPoolable
    {
        [SerializeField] private ParticleSystem[] systems = System.Array.Empty<ParticleSystem>();
        [Tooltip("Duree avant retour au pool si aucune duree n'est imposee a l'appel (s).")]
        [SerializeField, Min(0.1f)] private float defaultLifetime = 1f;

        private PooledObject _pooled;
        private float _lifetime;

        private void Awake()
        {
            _pooled = GetComponent<PooledObject>();
            _lifetime = defaultLifetime;
        }

        /// <summary>Impose la duree de vie de cette instance (portail de spawn).</summary>
        public void SetLifetime(float seconds)
        {
            _lifetime = Mathf.Max(0.1f, seconds);
            ScheduleRelease();
        }

        public void OnSpawn()
        {
            if (_pooled == null) _pooled = GetComponent<PooledObject>();
            _lifetime = defaultLifetime;
            Play();
            ScheduleRelease();
        }

        public void OnDespawn() => Stop();

        public void Play()
        {
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        public void Stop()
        {
            for (int i = 0; i < systems.Length; i++) systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetIntensity(float intensity)
        {
            float k = Mathf.Clamp01(intensity);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = k * Mathf.Max(0f, emission.rateOverTimeMultiplier);
            }
        }

        private void ScheduleRelease()
        {
            if (_pooled != null) _pooled.ReleaseAfter(_lifetime);
        }
    }
}
