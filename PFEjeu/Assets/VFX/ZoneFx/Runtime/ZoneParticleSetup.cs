using UnityEngine;
using UnityEngine.Rendering;

namespace ZoneFx
{
    /// <summary>Les 2 couches de particules d'une zone (le voile de lumière est un mesh, voir ZoneVeilMesh).</summary>
    public enum ZoneParticleKind
    {
        Sparks,     // petites étincelles qui montent depuis l'anneau
        Motes       // poussières de lumière dans toute la zone
    }

    /// <summary>
    /// Réglage des ParticleSystem par le code (partagé entre l'installateur et ZoneFxController, aucun asset à régler à la main).
    /// Les débits et le rayon de la forme sont recalculés quand la zone change de taille.
    /// </summary>
    public static class ZoneParticleSetup
    {
        #region Constantes
        public const float SparksPerMeter = 4f;         // étincelles / s, par mètre de rayon
        public const float MotesPerSquareMeter = 0.8f;  // poussières / s, par m² de rayon (surface de la zone)
        public const float MaxSupportedRadius = 12f;    // dimensionne maxParticles
        private const float MotesRadiusFactor = 0.95f;  // les poussières restent à l'intérieur de l'anneau
        #endregion

        #region Configuration (une fois, à l'installation)
        /// <summary>Applique tous les réglages d'une couche. Le matériau reste à assigner par l'appelant.</summary>
        public static void Configure(ParticleSystem system, ZoneParticleKind kind)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // les modules ne se règlent pas pendant la lecture

            bool isMotes = kind == ZoneParticleKind.Motes;
            float area = MaxSupportedRadius * MaxSupportedRadius;

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;    // la zone suit son propriétaire
            main.scalingMode = ParticleSystemScalingMode.Local;            // l'échelle des parents ne déforme pas les particules
            main.startSpeed = 0f;                                          // tout le mouvement passe par Velocity over Lifetime
            main.startColor = Color.white;                                 // la teinte est posée par SetTint
            if (isMotes)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                main.maxParticles = Mathf.CeilToInt(MotesPerSquareMeter * area * 3.0f * 1.3f);
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
                main.maxParticles = Mathf.CeilToInt(SparksPerMeter * MaxSupportedRadius * 1.4f * 1.3f);
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;                                    // fixé par Apply()

            // Forme : disque posé à plat. Étincelles : sur le bord uniquement ; poussières : sur toute la surface.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.arc = 360f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            shape.radiusThickness = isMotes ? 1f : 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Mouvement : les 3 axes DOIVENT être dans le même mode (TwoConstants), sinon Unity lève une erreur.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = isMotes ? new ParticleSystem.MinMaxCurve(0.15f, 0.5f) : new ParticleSystem.MinMaxCurve(0.6f, 1.4f);

            if (isMotes)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.4f;
                noise.quality = ParticleSystemNoiseQuality.Low;
            }

            // Opacité sur la vie : naît, plafonne, s'éteint.
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(BuildFade(0.3f));

            var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private static Gradient BuildFade(float peak)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, peak), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }
        #endregion

        #region Exécution (à chaque changement de rayon / visibilité / couleur)
        /// <summary>Règle forme + débit. 'amount' : 0..1+ (visibilité x intensité).</summary>
        public static void Apply(ParticleSystem system, ZoneParticleKind kind, float radius, float amount)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.radius = kind == ZoneParticleKind.Motes ? radius * MotesRadiusFactor : radius;

            float rate = kind == ZoneParticleKind.Motes ? MotesPerSquareMeter * radius * radius : SparksPerMeter * radius;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate * Mathf.Max(0f, amount);
        }

        public static void SetTint(ParticleSystem system, Color tint)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = tint;
        }
        #endregion
    }
}
