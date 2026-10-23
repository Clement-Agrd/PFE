using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace UpgradeVFX
{
    /// Phase de charge (particules aspirées, pulsation au sol) et chevrons ascendants pendant la montée.
    public class UpgradeAura : MonoBehaviour
    {
        public ParticleSystem baseDust, motesIn, chevrons, groundPulse; public Light glow;
        float k = 1, target;
        public void Setup(Vector3 center, float baseY, Vector2 size, float yaw, float height, Color team, Color dust, float k)
        {
            this.k = k; transform.SetPositionAndRotation(new Vector3(center.x, baseY + 0.05f, center.z), Quaternion.Euler(0, yaw, 0));
            float dim = Mathf.Max(size.x, size.y), kk = Mathf.Clamp((size.x + size.y) / 21f, 0.5f, 3f);
            if (baseDust)
            {
                var sh = baseDust.shape; sh.scale = new Vector3(size.x * 1.06f, size.y * 1.06f, 1f); U.Tint(baseDust, dust);
                var m = baseDust.main; m.startSizeMultiplier *= kk; var e = baseDust.emission; e.rateOverTimeMultiplier *= kk;
            }
            if (motesIn)
            {
                var sh = motesIn.shape; sh.scale = new Vector3(size.x * 0.75f, size.y * 0.75f, 1f); U.Tint(motesIn, team);
                float R = dim * 0.75f; var vo = motesIn.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.Local;
                vo.x = U.Z(); vo.y = U.Z(); vo.z = U.Z(); vo.orbitalX = U.Z(); vo.orbitalZ = U.Z();
                vo.orbitalY = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); vo.radial = new ParticleSystem.MinMaxCurve(-R / 0.7f, -R / 0.95f);
                var e = motesIn.emission; e.rateOverTimeMultiplier *= kk * k;
            }
            if (chevrons)
            {
                var sh = chevrons.shape; sh.scale = new Vector3(size.x * 0.5f * 1.3f, size.y * 0.5f * 1.3f, 1f); U.Tint(chevrons, team);
                var vo = chevrons.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
                vo.x = U.Z(); vo.z = U.Z(); vo.y = new ParticleSystem.MinMaxCurve(height / 1.9f, height / 1.4f);
                var m = chevrons.main; m.startSizeMultiplier *= Mathf.Clamp(dim / 12f, 0.6f, 2.4f) * (0.9f + 0.1f * k);
                var e = chevrons.emission; e.rateOverTimeMultiplier *= kk * k;
            }
            if (groundPulse)
            {
                U.Tint(groundPulse, team); var m = groundPulse.main; m.startSize = dim * 1.5f / (2f * U.MeshRadius(groundPulse));
            }
            if (glow) { glow.transform.localPosition = new Vector3(0, height * 0.4f, 0); glow.range = dim * 1.6f; glow.color = Color.Lerp(new Color(1f, 0.85f, 0.65f), team, 0.6f); glow.intensity = 0; }
        }
        public void BeginCharge() { Play(baseDust); Play(motesIn); Play(groundPulse); }
        public void SetCharge(float c) { target = 5f * k * c; }
        public void BeginAscend() { Stop(motesIn); Play(chevrons); }
        public void EndRise() { Stop(chevrons); Stop(groundPulse); Stop(baseDust); Stop(motesIn); }
        public void Finish() { EndRise(); target = 0f; Destroy(gameObject, 6f); }
        static void Play(ParticleSystem p) { if (p) p.Play(true); }
        static void Stop(ParticleSystem p) { if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        void Update() { if (glow) glow.intensity = Mathf.MoveTowards(glow.intensity, target * (0.88f + 0.12f * Mathf.Sin(Time.time * 20f)), 30f * Time.deltaTime); }
    }
}
