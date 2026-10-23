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

    static class U
    {
        public static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);
        public static ParticleSystem.MinMaxCurve Z() => new ParticleSystem.MinMaxCurve(0f, 0f);
        public static void Tint(ParticleSystem ps, Color c)
        {
            if (!ps) return; var m = ps.main; var sc = m.startColor;
            m.startColor = new ParticleSystem.MinMaxGradient(Mul(sc.colorMin, c), Mul(sc.colorMax, c));
        }
        public static void Tint(ParticleSystem[] a, Color c) { if (a != null) foreach (var p in a) Tint(p, c); }
        public static float MeshRadius(ParticleSystem ps)
        {
            if (!ps) return 1f; var m = ps.GetComponent<ParticleSystemRenderer>().mesh;
            return m ? Mathf.Max(m.bounds.extents.x, 0.01f) : 1f;
        }
    }
}
