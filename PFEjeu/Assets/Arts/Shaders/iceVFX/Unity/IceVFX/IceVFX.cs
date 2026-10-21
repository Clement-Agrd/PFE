using UnityEngine;

namespace IceVFX
{
    /// Niveau de qualité des effets.
    /// Low : pas de distorsion, bursts réduits, trails désactivés, budget de particules plafonné.
    public enum IceQuality { Low, Medium, High }

    /// Helpers partagés (qualité, redémarrage des particules). Appelés à l'Awake / au respawn, jamais par frame.
    public static class IceVFXUtil
    {
        public const string DistortionShader = "IceVFX/IceDistortion";

        public static bool IsDistortion(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var m = r ? r.sharedMaterial : null;
            return m && m.shader && m.shader.name == DistortionShader;
        }

        /// Adapte les systèmes de particules au niveau de qualité.
        /// Low : budget (somme des maxParticles) réparti au prorata du pic théorique de chaque système.
        public static void ApplyQuality(ParticleSystem[] systems, IceQuality q, int lowBudget)
        {
            if (q == IceQuality.High) return;
            float burstK = q == IceQuality.Low ? 0.35f : 0.7f;
            float rateK = q == IceQuality.Low ? 0.4f : 0.8f;
            var nominal = new float[systems.Length];
            float sum = 0f;

            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (q == IceQuality.Low && IsDistortion(ps)) { ps.gameObject.SetActive(false); continue; }

                var em = ps.emission;
                float peak = 0f;
                var bursts = new ParticleSystem.Burst[em.burstCount];
                em.GetBursts(bursts);
                for (int b = 0; b < bursts.Length; b++)
                {
                    float n = Mathf.Max(1f, Mathf.Round(bursts[b].count.constantMax * burstK));
                    bursts[b].count = new ParticleSystem.MinMaxCurve(n);
                    peak += n;
                }
                em.SetBursts(bursts);
                em.rateOverTime = em.rateOverTime.constantMax * rateK;
                em.rateOverDistance = em.rateOverDistance.constantMax * rateK;

                float life = ps.main.startLifetime.constantMax;
                peak += (em.rateOverTime.constantMax + em.rateOverDistance.constantMax * 4f) * life;
                nominal[i] = Mathf.Max(peak, 1f);
                sum += nominal[i];

                if (q == IceQuality.Low) { var tr = ps.trails; tr.enabled = false; }
            }

            if (q != IceQuality.Low || sum <= 0f) return;
            for (int i = 0; i < systems.Length; i++)
            {
                if (nominal[i] <= 0f) continue;
                var m = systems[i].main;
                m.maxParticles = Mathf.Max(1, Mathf.FloorToInt(lowBudget * nominal[i] / sum));
            }
        }

        /// Repart de zéro (instance recyclée).
        public static void Restart(ParticleSystem[] systems)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].Play(false);
            }
        }

        public static float EaseOutBack(float t, float overshoot)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + (overshoot + 1f) * t * t * t + overshoot * t * t;
        }
    }
}
