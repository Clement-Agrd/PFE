using UnityEngine;

namespace Defense.Mountain
{
    /// <summary>
    /// Banc de nuages lointain : des "puffs" persistants (duree de vie infinie) poses une seule fois sur le terrain,
    /// avec une derive tres lente (module Noise). Meme technique et meme materiau que la brume existante, sans la modifier.
    /// Trois genres : bancs dans les vallees entre les cretes, voiles accroches aux cretes, mer de nuages au-dela de la
    /// derniere crete. Lisere argente cote lune. Zero allocation apres la construction.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class MountainCloudBank : MonoBehaviour
    {
        public enum BankKind
        {
            /// <summary>Cherche les points BAS entre les cretes : brume de vallee.</summary>
            ValleyBank,
            /// <summary>Cherche les points HAUTS : voiles qui s'accrochent aux cretes.</summary>
            CrestWisp,
            /// <summary>Plaine exterieure plate : mer de nuages qui masque le bord du terrain.</summary>
            CloudSea,
        }

        private const float InfiniteLifetime = 1e6f;
        private const int SeaRetries = 8;
        private const float BorderMargin = 6f;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] private BankKind kind = BankKind.ValleyBank;
        [SerializeField] private Material material;
        [Tooltip("Point de reference (herse) : les distances sont mesurees depuis lui.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private int seed = 7771;
        [SerializeField, Min(0)] private int count = 60;
        [SerializeField] private Vector2 distanceRange = new Vector2(305f, 420f);
        [SerializeField] private Vector2 sizeRange = new Vector2(110f, 240f);
        [SerializeField] private Vector2 alphaRange = new Vector2(0.22f, 0.38f);
        [Tooltip("Hauteur des puffs au-dessus du sol (m), min et max.")]
        [SerializeField] private Vector2 heightRange = new Vector2(14f, 30f);
        [Tooltip("Nombre de candidats testes par puff : on garde le plus bas (vallee) ou le plus haut (crete).")]
        [SerializeField, Range(1, 8)] private int candidateTries = 4;
        [Tooltip("Mer de nuages : altitude relative maximale du sol (m) ; au-dessus, pas de nuage.")]
        [SerializeField] private float seaMaxGroundRel = 40f;
        [SerializeField] private float valleyY = 5f;

        [Header("Couleur (froide, lisere argente cote lune)")]
        [SerializeField] private Color tint = new Color(0.40f, 0.50f, 0.70f, 1f);
        [SerializeField] private Color silver = new Color(0.76f, 0.85f, 1f, 1f);
        [Tooltip("Azimut de la lune (0 = nord, 90 = est) : oppose au sens de la lumiere directionnelle.")]
        [SerializeField] private float moonAzimuth = 338f;
        [SerializeField, Range(0f, 1f)] private float rimStrength = 0.85f;

        [Header("Repartition")]
        [Tooltip("Azimut a favoriser (prolongement du massif vers Spawn_Route_04).")]
        [SerializeField] private float focusAzimuth = 33f;
        [SerializeField] private float focusHalfWidth = 40f;
        [SerializeField, Range(0f, 1f)] private float focusShare = 0.3f;

        [Header("Mouvement")]
        [SerializeField] private float driftStrength = 2.5f;
        [SerializeField] private float sortingFudge = 80f;
        [SerializeField, Range(0f, 1f)] private float intensity = 1f;

        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ParticleSystem.Particle[] _buffer;

        private void OnEnable() => Build();

        private void OnDisable()
        {
            if (_system != null) _system.Clear();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && !Application.isPlaying) Build();
        }

        #region Construction

        private void Build()
        {
            if (material == null || anchor == null) return;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null || count <= 0) return;

            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();
            Configure();

            if (_buffer == null || _buffer.Length < count) _buffer = new ParticleSystem.Particle[count];
            int written = Fill(terrain);

            // Play avant SetParticles : un Play apres coup remet le systeme a zero.
            if (!_system.isPlaying) _system.Play();
            _system.SetParticles(_buffer, written);
            ApplyIntensity();
        }

        private void Configure()
        {
            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = InfiniteLifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            ParticleSystem.NoiseModule noise = _system.noise;
            noise.enabled = true;
            noise.strength = driftStrength;
            noise.frequency = 0.012f;
            noise.scrollSpeed = 0.03f;
            noise.positionAmount = 1f;
            noise.damping = true;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingFudge = sortingFudge;
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

        #region Placement

        private int Fill(Terrain terrain)
        {
            var rng = new System.Random(seed);
            Vector3 origin = anchor.position;
            Vector2 moon = new Vector2(Mathf.Sin(moonAzimuth * Mathf.Deg2Rad), Mathf.Cos(moonAzimuth * Mathf.Deg2Rad));
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                float azimuth = PickAzimuth(i, rng);
                if (!TryPick(terrain, origin, azimuth, rng, out Vector3 position)) continue;

                Vector2 direction = new Vector2(Mathf.Sin(azimuth * Mathf.Deg2Rad), Mathf.Cos(azimuth * Mathf.Deg2Rad));
                float rim = Mathf.Clamp01(Vector2.Dot(direction, moon)) * rimStrength;
                Color color = Color.Lerp(tint, silver, rim);
                color.a = Mathf.Clamp01(Mathf.Lerp(alphaRange.x, alphaRange.y, (float)rng.NextDouble()) * Mathf.Lerp(1f, 1.25f, rim));

                _buffer[written++] = new ParticleSystem.Particle
                {
                    position = position,
                    startSize = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble()),
                    startColor = color,
                    rotation = (float)rng.NextDouble() * 360f,
                    startLifetime = InfiniteLifetime,
                    remainingLifetime = InfiniteLifetime,
                };
            }
            return written;
        }

        /// <summary>Azimuts stratifies sur 360 deg (couverture reguliere), avec une part concentree sur le secteur favorise.</summary>
        private float PickAzimuth(int index, System.Random rng)
        {
            if (rng.NextDouble() < focusShare)
                return focusAzimuth + ((float)rng.NextDouble() * 2f - 1f) * focusHalfWidth;
            return (index + (float)rng.NextDouble()) / count * 360f;
        }

        private bool TryPick(Terrain terrain, Vector3 origin, float azimuth, System.Random rng, out Vector3 position)
        {
            position = default;
            int tries = kind == BankKind.CloudSea ? SeaRetries : candidateTries;
            bool found = false;
            float bestGround = kind == BankKind.CrestWisp ? float.MinValue : float.MaxValue;

            for (int t = 0; t < tries; t++)
            {
                float a = (azimuth + ((float)rng.NextDouble() - 0.5f) * 14f) * Mathf.Deg2Rad;
                float distance = Mathf.Lerp(distanceRange.x, distanceRange.y, (float)rng.NextDouble());
                float x = origin.x + Mathf.Sin(a) * distance;
                float z = origin.z + Mathf.Cos(a) * distance;
                if (!InTerrain(terrain, x, z)) continue;

                float ground = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                if (kind == BankKind.CloudSea)
                {
                    if (ground - valleyY > seaMaxGroundRel) continue;
                    position = new Vector3(x, ground + Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble()), z);
                    return true;
                }

                bool better = kind == BankKind.CrestWisp ? ground > bestGround : ground < bestGround;
                if (!better) continue;
                bestGround = ground;
                position = new Vector3(x, ground + Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble()), z);
                found = true;
            }
            return found;
        }

        private static bool InTerrain(Terrain terrain, float x, float z)
        {
            Vector3 o = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            return x > o.x + BorderMargin && x < o.x + size.x - BorderMargin && z > o.z + BorderMargin && z < o.z + size.z - BorderMargin;
        }

        #endregion
    }
}
