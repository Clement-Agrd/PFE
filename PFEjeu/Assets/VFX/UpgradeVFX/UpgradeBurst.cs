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
    /// Impact final : flash, pilier de lumière, onde de choc, poussière, braises, chevrons, secousse. Référence : rayon 6 m.
    public class UpgradeBurst : MonoBehaviour
    {
        public ParticleSystem[] dust, team; public ParticleSystem chevrons;
        public Light flash; public Transform pillar;
        public float refRadius = 6f, shake = 0.35f, shakeRadius = 40f, life = 8f, flashPeak = 6f, pillarLife = 1.6f;
        Renderer pillarR; MaterialPropertyBlock pb; float age, scale = 1, height, intensity = 1; Vector2 size; Color teamC;
        static readonly int ColorId = Shader.PropertyToID("_Color"), FadeId = Shader.PropertyToID("_Fade");

        public void Play(Vector3 pos, Vector2 size, float height, Color teamColor, Color dustColor, float intensity, float shakeMul)
        {
            transform.position = pos; this.size = size; this.height = height; this.intensity = intensity; teamC = teamColor;
            float radius = Mathf.Max(size.x, size.y) * 0.5f; scale = Mathf.Max(0.2f, radius / refRadius); transform.localScale = Vector3.one * scale;
            U.Tint(dust, dustColor); U.Tint(team, teamColor);
            if (chevrons)
            {
                U.Tint(chevrons, teamColor); var vo = chevrons.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
                vo.x = U.Z(); vo.z = U.Z(); vo.y = new ParticleSystem.MinMaxCurve(height / 1.5f, height / 1.1f);
                var sh = chevrons.shape; sh.scale = new Vector3(size.x * 0.5f * 1.2f / scale, size.y * 0.5f * 1.2f / scale, 1f);
            }
            foreach (var p in GetComponentsInChildren<ParticleSystem>()) { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); p.Play(true); }
            if (flash) { flash.color = Color.Lerp(new Color(1f, 0.9f, 0.7f), teamColor, 0.4f); flash.range = 20f * scale; flash.intensity = flashPeak * intensity; }
            if (pillar) { pillarR = pillar.GetComponent<Renderer>(); pb = new MaterialPropertyBlock(); pillar.gameObject.SetActive(true); }
            UpgradeCameraShake.Hit(pos, shake * shakeMul * intensity * (0.6f + 0.4f * scale), shakeRadius * scale);
            Destroy(gameObject, life);
        }
        void Update()
        {
            age += Time.deltaTime;
            if (flash) flash.intensity = flashPeak * intensity * Mathf.Exp(-age * 6f);
            if (!pillar || pb == null) return;
            float p = age / pillarLife;
            if (p >= 1f) { pillar.gameObject.SetActive(false); return; }
            float grow = 1f - (1f - Mathf.Clamp01(p * 3f)) * (1f - Mathf.Clamp01(p * 3f));
            float w = Mathf.Lerp(0.55f, 1f, grow), H = Mathf.Max(height * 2.2f, 8f) * intensity;
            pillar.localScale = new Vector3(size.x * 0.5f * 0.9f * w / scale, H / scale, size.y * 0.5f * 0.9f * w / scale);
            pb.SetColor(ColorId, teamC); pb.SetFloat(FadeId, Mathf.Pow(1f - p, 1.5f) * Mathf.Clamp01(age * 25f)); pillarR.SetPropertyBlock(pb);
        }
    }
}
