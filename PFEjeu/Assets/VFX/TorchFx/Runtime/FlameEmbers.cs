using UnityEngine;

namespace TorchFx
{
    /// <summary>
    /// Module de braises : construit un ParticleSystem au démarrage (aucun asset à créer à la main),
    /// l'émet vers le haut depuis la base de la flamme, le courbe avec le vent et le coupe à l'extinction.
    /// Nécessite un matériau utilisant le shader TorchFx/Ember.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlameEmbers : MonoBehaviour, IFlameModule
    {
        #region Inspector
        [Header("Rendu")]
        [Tooltip("Matériau utilisant le shader TorchFx/Ember.")]
        [SerializeField] private Material emberMaterial;
        [SerializeField] private Gradient colorOverLifetime = CreateDefaultGradient();

        [Header("Emission")]
        [Tooltip("Braises par seconde à pleine puissance.")]
        [SerializeField, Min(0f)] private float emissionRate = 14f;
        [SerializeField, Min(8)] private int maxParticles = 64;
        [SerializeField, Min(0f)] private float spawnRadius = 0.05f;

        [Header("Mouvement")]
        [SerializeField] private Vector2 lifetimeRange = new Vector2(1.2f, 2.4f);
        [SerializeField] private Vector2 riseSpeedRange = new Vector2(0.25f, 0.7f);
        [SerializeField] private Vector2 sizeRange = new Vector2(0.012f, 0.03f);
        [Tooltip("Poussée vers le haut (gravité négative).")]
        [SerializeField, Min(0f)] private float buoyancy = 0.04f;
        [SerializeField, Min(0f)] private float turbulence = 0.35f;
        [Tooltip("Part du vent de la flamme appliquée aux braises.")]
        [SerializeField, Min(0f)] private float windInfluence = 0.8f;
        #endregion

        private ParticleSystem system;
        private Transform systemTransform;
        private Vector3 lastWind;

        #region Cycle Unity
        private void Awake()
        {
            if (emberMaterial == null)
            {
                Debug.LogError($"[{nameof(FlameEmbers)}] Matériau de braises manquant sur '{name}'.", this);
                return;
            }

            BuildSystem();
        }
        #endregion

        #region IFlameModule
        public void Tick(in FlameState state)
        {
            if (system == null) return;

            bool shouldEmit = state.Intensity01 > 0.05f;

            systemTransform.position = state.Position;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = shouldEmit ? emissionRate * state.Intensity01 : 0f;

            if (shouldEmit && !system.isEmitting) system.Play();
            else if (!shouldEmit && system.isEmitting) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            ApplyWind(state.Wind);
        }
        #endregion

        #region Construction du ParticleSystem
        private void BuildSystem()
        {
            GameObject host = new GameObject("FlameEmbers (runtime)");
            systemTransform = host.transform;
            systemTransform.SetParent(transform, false);
            systemTransform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // le cône d'émission part vers +Z : on le relève vers le haut

            system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // obligatoire avant de modifier la durée

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(riseSpeedRange.x, riseSpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-buoyancy);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = spawnRadius;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(turbulence);
            noise.frequency = 0.9f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.6f);
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(colorOverLifetime);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(0f);
            force.y = new ParticleSystem.MinMaxCurve(0f);
            force.z = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystemRenderer particleRenderer = host.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.material = emberMaterial;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
        }

        // Le module Force n'est réécrit que si le vent a réellement changé.
        private void ApplyWind(Vector3 wind)
        {
            if ((wind - lastWind).sqrMagnitude < 0.0001f) return;

            lastWind = wind;

            ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
            force.x = new ParticleSystem.MinMaxCurve(wind.x * windInfluence);
            force.z = new ParticleSystem.MinMaxCurve(wind.z * windInfluence);
        }

        private static Gradient CreateDefaultGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.45f),
                    new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
        #endregion
    }
}
