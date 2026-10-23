using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BuildVFX
{
    /// Particules pendant la construction : poussière au sol + particules incandescentes sur le front de révélation.
    public class BuildConstructFX : MonoBehaviour
    {
        public ParticleSystem baseDust, motes;
        public void Setup(Vector3 center, float baseY, Vector2 size, Color team, Color dust, float yaw = 0f)
        {
            transform.position = new Vector3(center.x, baseY + 0.1f, center.z);
            transform.rotation = Quaternion.Euler(0, yaw, 0) * transform.rotation;
            float k = Mathf.Clamp((size.x + size.y) / 21f, 0.5f, 3f);
            Prep(baseDust, size * 1.06f, dust, k); Prep(motes, size, team, k);
        }
        static void Prep(ParticleSystem ps, Vector2 size, Color c, float k)
        {
            if (!ps) return;
            var sh = ps.shape; sh.scale = new Vector3(size.x, size.y, 1f);
            var m = ps.main; var sc = m.startColor; m.startColor = new ParticleSystem.MinMaxGradient(Mul(sc.colorMin, c), Mul(sc.colorMax, c)); m.startSizeMultiplier *= k;
            var e = ps.emission; e.rateOverTimeMultiplier *= k;
            ps.Play(true);
        }
        internal static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);
        public void SetRevealY(float y) { if (motes) { var p = motes.transform.position; p.y = y; motes.transform.position = p; } }
        public void StopEmit() { foreach (var p in GetComponentsInChildren<ParticleSystem>()) p.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(gameObject, 8f); }
    }
}
