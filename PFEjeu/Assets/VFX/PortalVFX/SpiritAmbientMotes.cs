using UnityEngine;

namespace SpiritVFX
{
    /// Petites particules lumineuses (lucioles / spores) qui dérivent autour du portail tant qu'il est ouvert.
    [RequireComponent(typeof(SpiritGate))]
    public class SpiritAmbientMotes : MonoBehaviour
    {
        public Material material;
        [Range(1f, 80f)] public float rate = 22f;
        public Vector2 size = new Vector2(0.04f, 0.12f);
        [ColorUsage(true, true)] public Color gold = new Color(1.7f, 1.4f, 0.7f, 1f), green = new Color(0.4f, 1.6f, 0.9f, 1f);

        SpiritGate gate;
        ParticleSystem ps;
        ParticleSystem.EmissionModule em;

        void Start()
        {
            gate = GetComponent<SpiritGate>();
            var go = new GameObject("AmbientMotes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, gate.height * 0.5f, 0.15f);
            go.transform.localScale = new Vector3(gate.width / gate.height, 1f, 1f);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(gold, green);

            em = ps.emission; em.rateOverTime = 0f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = gate.height * 0.62f; sh.radiusThickness = 1f; sh.arc = 360f;

            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.55f); vel.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.35f; noise.scrollSpeed = 0.2f; noise.quality = ParticleSystemNoiseQuality.Low;

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (material) r.sharedMaterial = material;
            ps.Play();
        }

        void Update()
        {
            if (!ps) return;
            bool on = gate.State == SpiritState.Open || gate.State == SpiritState.Building;
            em.rateOverTime = Mathf.Lerp(em.rateOverTime.constant, on ? rate : 0f, Time.deltaTime * 3f);
        }
    }
}
