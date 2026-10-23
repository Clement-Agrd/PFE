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
    /// Front d'énergie : bande lumineuse + anneau + étincelles + lumière, qui monte le long du bâtiment.
    public class UpgradeFront : MonoBehaviour
    {
        public Transform band, ring; public ParticleSystem sparks; public Light glow;
        Renderer bandR, ringR; MaterialPropertyBlock pb; float bandH, k = 1; Color team;
        static readonly int ColorId = Shader.PropertyToID("_Color"), FadeId = Shader.PropertyToID("_Fade");

        public void Setup(Vector3 center, Vector2 size, float yaw, bool circle, Color teamColor, float edgeWidth, float k)
        {
            team = teamColor; this.k = k; transform.SetPositionAndRotation(new Vector3(center.x, 0, center.z), Quaternion.Euler(0, yaw, 0));
            float f = circle ? 1.12f : 1.32f, sx = size.x * 0.5f * f, sz = size.y * 0.5f * f;
            bandH = Mathf.Max(1.2f, edgeWidth * 3.5f) * k;
            band.localScale = new Vector3(sx, bandH, sz); band.localPosition = new Vector3(0, -bandH, 0); ring.localScale = new Vector3(sx, 1f, sz);
            bandR = band.GetComponent<Renderer>(); ringR = ring.GetComponent<Renderer>(); pb = new MaterialPropertyBlock();
            pb.SetColor(ColorId, team); pb.SetFloat(FadeId, 1f); bandR.SetPropertyBlock(pb); ringR.SetPropertyBlock(pb);
            if (sparks)
            {
                var sh = sparks.shape; sh.scale = new Vector3(sx, sz, 1f); U.Tint(sparks, team);
                var e = sparks.emission; e.rateOverTimeMultiplier *= k * Mathf.Clamp((size.x + size.y) / 21f, 0.6f, 3f);
            }
            if (glow) { glow.color = Color.Lerp(new Color(1f, 0.85f, 0.65f), team, 0.5f); glow.range = Mathf.Max(size.x, size.y) * 1.3f; glow.intensity = 0f; }
            Show(false);
        }
        public void Show(bool v)
        {
            band.gameObject.SetActive(v); ring.gameObject.SetActive(v); if (glow) glow.enabled = v;
            if (!sparks) return; if (v) sparks.Play(true); else sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        public void SetY(float y)
        {
            var p = transform.position; p.y = y; transform.position = p;
            if (glow) glow.intensity = 6f * k * (0.85f + 0.15f * Mathf.Sin(Time.time * 30f));
        }
        public void Finish() { Show(false); Destroy(gameObject, 2f); }
    }
}
