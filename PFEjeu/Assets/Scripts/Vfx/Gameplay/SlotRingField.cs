using Core.TowerDefense;
using Core.WaveSystem;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Anneau teal au sol des slots constructibles : UN systeme de particules, une pastille horizontale par slot libre.
    /// Rafraichi sur les evenements de vague (la construction n'est possible qu'entre les vagues) :
    /// aucun polling. Les slots ne sont pas modifies.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class SlotRingField : MonoBehaviour, IVfxPlayer
    {
        private const float InfiniteLifetime = 1e6f;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        [SerializeField] private VfxProfile profile;
        [SerializeField] private Material material;
        [SerializeField] private WaveSpawner waveSpawner;
        [Tooltip("Transforms des slots (TowerSlot_*). Un DefenseBuildSlot eventuel est respecte (occupation).")]
        [SerializeField] private Transform[] slots = System.Array.Empty<Transform>();
        [SerializeField, Min(0.5f)] private float ringSize = 4.2f;
        [SerializeField, Range(0f, 1f)] private float alpha = 0.55f;
        [SerializeField, Min(0.1f)] private float pulseSpeed = 1.6f;
        [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.15f;

        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ParticleSystem.Particle[] _buffer;
        private bool _active = true;
        private float _intensity = 1f;

        private void OnEnable()
        {
            Setup();
            Refresh();
            if (waveSpawner != null && Application.isPlaying)
            {
                waveSpawner.OnWaveStarted += OnWaveEvent;
                waveSpawner.OnWaveCompleted += OnWaveEvent;
                waveSpawner.OnAllWavesCompleted += Refresh;
            }
        }

        private void OnDisable()
        {
            if (waveSpawner != null)
            {
                waveSpawner.OnWaveStarted -= OnWaveEvent;
                waveSpawner.OnWaveCompleted -= OnWaveEvent;
                waveSpawner.OnAllWavesCompleted -= Refresh;
            }
            if (_system != null) _system.Clear();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && !Application.isPlaying)
            {
                Setup();
                Refresh();
            }
        }

        private void Update()
        {
            if (_renderer == null || profile == null) return;
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, _active ? pulse * _intensity : 0f);
            _block.SetColor(TintId, profile.DefenseColor);
            _renderer.SetPropertyBlock(_block);
        }

        public void Play() => _active = true;
        public void Stop() => _active = false;
        public void SetIntensity(float intensity) => _intensity = Mathf.Clamp01(intensity);

        private void OnWaveEvent(int wave) => Refresh();

        /// <summary>Reconstruit la liste des anneaux : slots libres, actifs et constructibles.</summary>
        public void Refresh()
        {
            if (_system == null || slots.Length == 0) return;
            if (_buffer == null || _buffer.Length < slots.Length) _buffer = new ParticleSystem.Particle[slots.Length];

            bool buildable = waveSpawner == null || !waveSpawner.IsWaveActive;
            int count = 0;
            if (buildable)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    Transform slot = slots[i];
                    if (slot == null || !slot.gameObject.activeInHierarchy) continue;
                    Vector3 p = slot.position;
                    if (slot.TryGetComponent(out DefenseBuildSlot build))
                    {
                        if (build.IsOccupied) continue;
                        p = build.BuildAnchor.position;
                    }
                    _buffer[count++] = new ParticleSystem.Particle
                    {
                        position = new Vector3(p.x, p.y + 0.08f, p.z),
                        startSize = ringSize,
                        startColor = new Color(1f, 1f, 1f, alpha),
                        startLifetime = InfiniteLifetime,
                        remainingLifetime = InfiniteLifetime,
                    };
                }
            }
            if (!_system.isPlaying) _system.Play();
            _system.SetParticles(_buffer, count);
        }

        private void Setup()
        {
            if (material == null || slots.Length == 0) return;
            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();

            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = InfiniteLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = slots.Length;
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            _renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }
    }
}
