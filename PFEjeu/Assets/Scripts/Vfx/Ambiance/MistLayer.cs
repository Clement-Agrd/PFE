using UnityEngine;
using UnityEngine.Splines;

namespace Core.Vfx
{
    /// <summary>
    /// Couche de brume : des "puffs" persistants (particules a duree de vie infinie) places une seule fois
    /// sur le terrain, avec derive douce par le module Noise. Zero allocation apres la construction.
    ///
    ///   VfxProfile --> MistLayer (kind, index) --> ParticleSystem (SetParticles une fois)
    ///                     |-- evite les lanes (EnemyPath) et elargit le couloir pres de la base
    ///                     '-- qualite : multiplicateur de particules / nombre de couches lointaines
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class MistLayer : MonoBehaviour, IVfxPlayer
    {
        public enum LayerKind
        {
            Ground,
            Far,
        }

        private const float InfiniteLifetime = 1e6f;
        private const int LaneSamples = 80;
        private const int MaxAttemptsPerPuff = 12;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] private VfxProfile profile;
        [SerializeField] private LayerKind kind = LayerKind.Ground;
        [Tooltip("Indice de la couche lointaine dans le profil (ignore pour Ground).")]
        [SerializeField, Min(0)] private int farIndex;
        [SerializeField] private Material material;
        [SerializeField] private SplineContainer[] lanes = System.Array.Empty<SplineContainer>();
        [Tooltip("Point de reference de la base (herse).")]
        [SerializeField] private Transform baseAnchor;
        [SerializeField] private int seed = 1337;
        [SerializeField, Range(0f, 1f)] private float intensity = 1f;

        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ParticleSystem.Particle[] _buffer;
        private Vector2[] _lanePoints;

        #region Cycle de vie

        private void OnEnable()
        {
            Build();
            ApplyIntensity();
        }

        private void OnDisable()
        {
            if (_system != null) _system.Clear();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && !Application.isPlaying) Build();
        }

        #endregion

        #region IVfxPlayer

        public void Play()
        {
            if (_system != null && !_system.isPlaying) _system.Play();
            ApplyIntensity();
        }

        public void Stop()
        {
            if (_system != null) _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            ApplyIntensity();
        }

        private void ApplyIntensity()
        {
            if (_renderer == null) return;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(IntensityId, intensity);
            _renderer.SetPropertyBlock(_block);
        }

        #endregion

        #region Construction

        private void Build()
        {
            if (profile == null || material == null) return;

            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) return;

            int target = TargetCount();
            if (target <= 0)
            {
                _system.Clear();
                return;
            }

            ConfigureSystem(target);
            CacheLanePoints();

            if (_buffer == null || _buffer.Length < target) _buffer = new ParticleSystem.Particle[target];
            int count = kind == LayerKind.Ground ? FillGround(terrain, target) : FillFar(terrain, target);

            // Play avant SetParticles : un Play apres coup remet le systeme a zero.
            if (!_system.isPlaying) _system.Play();
            _system.SetParticles(_buffer, count);
            ApplyIntensity();
        }

        private int TargetCount()
        {
            VfxProfile.QualitySettings q = profile.Quality;
            if (kind == LayerKind.Ground) return Mathf.RoundToInt(profile.GroundMistCount * q.particleMultiplier);

            VfxProfile.FarMistLayer[] layers = profile.FarLayers;
            if (farIndex >= layers.Length || farIndex >= q.farMistLayers) return 0;
            return Mathf.RoundToInt(layers[farIndex].count * q.particleMultiplier);
        }

        private void ConfigureSystem(int maxParticles)
        {
            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = InfiniteLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            ParticleSystem.NoiseModule noise = _system.noise;
            noise.enabled = true;
            noise.strength = profile.DriftStrength * (kind == LayerKind.Far ? 2f : 1f);
            noise.frequency = 0.04f;
            noise.scrollSpeed = 0.05f;
            noise.positionAmount = 1f;
            noise.damping = true;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingFudge = kind == LayerKind.Far ? 50f : 0f;
        }

        private void CacheLanePoints()
        {
            if (lanes == null || lanes.Length == 0)
            {
                _lanePoints = System.Array.Empty<Vector2>();
                return;
            }

            _lanePoints = new Vector2[lanes.Length * LaneSamples];
            int k = 0;
            foreach (SplineContainer lane in lanes)
            {
                for (int i = 0; i < LaneSamples; i++)
                {
                    Vector3 world = lane != null ? (Vector3)lane.EvaluatePosition(i / (float)(LaneSamples - 1)) : Vector3.zero;
                    _lanePoints[k++] = new Vector2(world.x, world.z);
                }
            }
        }

        #endregion

        #region Placement

        private int FillGround(Terrain terrain, int target)
        {
            var rng = new System.Random(seed);
            Color baseColor = profile.MistColor;
            Vector2 center = profile.MistCenter;
            Vector2 extent = profile.MistExtent;
            int count = 0;

            for (int n = 0; n < target; n++)
            {
                for (int attempt = 0; attempt < MaxAttemptsPerPuff; attempt++)
                {
                    float x = center.x + ((float)rng.NextDouble() * 2f - 1f) * extent.x;
                    float z = center.y + ((float)rng.NextDouble() * 2f - 1f) * extent.y;
                    float visibility = LaneVisibility(x, z);
                    if (visibility <= 0.02f) continue;

                    float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + profile.GroundMistHeight;
                    float size = Mathf.Lerp(profile.GroundMistSize.x, profile.GroundMistSize.y, (float)rng.NextDouble());
                    WritePuff(count++, new Vector3(x, y, z), size, baseColor, profile.GroundMistAlpha * visibility, rng);
                    break;
                }
            }
            return count;
        }

        private int FillFar(Terrain terrain, int target)
        {
            VfxProfile.FarMistLayer layer = profile.FarLayers[farIndex];
            var rng = new System.Random(seed + 101 * (farIndex + 1));
            Vector3 origin = baseAnchor != null ? baseAnchor.position : transform.position;
            Vector2 centroid = LaneCentroid(origin);
            float baseAngle = Mathf.Atan2(centroid.y - origin.z, centroid.x - origin.x);
            float half = profile.FarMistSectorDegrees * 0.5f * Mathf.Deg2Rad;
            int count = 0;

            for (int n = 0; n < target; n++)
            {
                float angle = baseAngle + ((float)rng.NextDouble() * 2f - 1f) * half;
                float dist = Mathf.Lerp(layer.distanceRange.x, layer.distanceRange.y, (float)rng.NextDouble());
                float x = origin.x + Mathf.Cos(angle) * dist;
                float z = origin.z + Mathf.Sin(angle) * dist;
                float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + layer.heightAboveGround;
                float size = Mathf.Lerp(layer.sizeRange.x, layer.sizeRange.y, (float)rng.NextDouble());
                WritePuff(count++, new Vector3(x, y, z), size, layer.tint, layer.alpha, rng);
            }
            return count;
        }

        private void WritePuff(int index, Vector3 position, float size, Color color, float alpha, System.Random rng)
        {
            color.a = Mathf.Clamp01(alpha);
            _buffer[index] = new ParticleSystem.Particle
            {
                position = position,
                startSize = size,
                startColor = color,
                rotation = (float)rng.NextDouble() * 360f,
                startLifetime = InfiniteLifetime,
                remainingLifetime = InfiniteLifetime,
            };
        }

        /// <summary>0 sur la lane, 1 loin de la lane. Le couloir libre est elargi pres de la base.</summary>
        private float LaneVisibility(float x, float z)
        {
            if (_lanePoints.Length == 0) return 1f;

            float best = float.MaxValue;
            for (int i = 0; i < _lanePoints.Length; i++)
            {
                float dx = x - _lanePoints[i].x;
                float dz = z - _lanePoints[i].y;
                float sqr = dx * dx + dz * dz;
                if (sqr < best) best = sqr;
            }
            float distance = Mathf.Sqrt(best);

            float clear = profile.LaneClearRadius;
            if (baseAnchor != null)
            {
                Vector3 b = baseAnchor.position;
                float toBase = Mathf.Sqrt((x - b.x) * (x - b.x) + (z - b.z) * (z - b.z));
                float near = 1f - Mathf.Clamp01((toBase - profile.BaseNearDistance) / profile.BaseNearDistance);
                clear *= Mathf.Lerp(1f, profile.BaseNearClearMultiplier, near);
            }
            return Mathf.Clamp01((distance - clear) / Mathf.Max(0.01f, profile.LaneFadeRadius));
        }

        private Vector2 LaneCentroid(Vector3 fallback)
        {
            if (_lanePoints.Length == 0) return new Vector2(fallback.x + 100f, fallback.z + 100f);
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < _lanePoints.Length; i++) sum += _lanePoints[i];
            return sum / _lanePoints.Length;
        }

        #endregion
    }
}
