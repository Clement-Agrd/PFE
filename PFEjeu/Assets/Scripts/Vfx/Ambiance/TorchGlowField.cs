using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Lueur simulee des torches : UN systeme de particules (un draw call) avec une pastille additive
    /// persistante par torche, pulsee par un sinus regulier (pas de bruit) via la propriete _Intensity.
    /// Remplace visuellement la Light reelle des torches lointaines (voir TorchLightBudget).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class TorchGlowField : MonoBehaviour, IVfxPlayer
    {
        private const float InfiniteLifetime = 1e6f;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] private VfxProfile profile;
        [SerializeField] private Material material;
        [Tooltip("Socles de flamme des torches (positions de la lueur).")]
        [SerializeField] private Transform[] sockets = System.Array.Empty<Transform>();

        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ParticleSystem.Particle[] _buffer;
        private float _intensity = 1f;
        private bool _playing;

        private void OnEnable()
        {
            Build();
            _playing = true;
        }

        private void OnDisable()
        {
            _playing = false;
            if (_system != null) _system.Clear();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && !Application.isPlaying) Build();
        }

        private void Update()
        {
            if (!_playing || _renderer == null || profile == null) return;
            float pulse = 1f + Mathf.Sin(Time.time * profile.TorchPulseSpeed) * profile.TorchPulseAmount;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, pulse * _intensity);
            _renderer.SetPropertyBlock(_block);
        }

        public void Play() => _playing = true;

        public void Stop()
        {
            _playing = false;
            if (_renderer != null)
            {
                _block ??= new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_block);
                _block.SetFloat(IntensityId, 0f);
                _renderer.SetPropertyBlock(_block);
            }
        }

        public void SetIntensity(float intensity) => _intensity = Mathf.Clamp01(intensity);

        private void Build()
        {
            if (profile == null || material == null || sockets.Length == 0) return;

            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();

            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = InfiniteLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = sockets.Length;
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            if (_buffer == null || _buffer.Length < sockets.Length) _buffer = new ParticleSystem.Particle[sockets.Length];
            Color color = profile.TorchColor;
            color.a = profile.TorchGlowAlpha;
            int count = 0;
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i] == null) continue;
                _buffer[count++] = new ParticleSystem.Particle
                {
                    position = sockets[i].position,
                    startSize = profile.TorchGlowSize,
                    startColor = color,
                    startLifetime = InfiniteLifetime,
                    remainingLifetime = InfiniteLifetime,
                };
            }
            if (!_system.isPlaying) _system.Play();
            _system.SetParticles(_buffer, count);
        }
    }
}
