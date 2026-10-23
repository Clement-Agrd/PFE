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
    /// Impact de fin de chantier : onde de choc, poussière, débris, lumière, secousse. Dimensionné pour un rayon de référence de 6 m.
    public class BuildImpact : MonoBehaviour
    {
        public ParticleSystem[] dust, team;
        public Light flash;
        public float refRadius = 6f, shake = 0.45f, shakeRadius = 45f, life = 10f, flashPeak = 5f;
        float age;
        public void Play(Vector3 pos, float radius, Color teamColor, Color dustColor, float shakeMul = 1f)
        {
            transform.position = pos; float s = Mathf.Max(0.2f, radius / refRadius); transform.localScale = Vector3.one * s;
            Tint(dust, dustColor); Tint(team, teamColor);
            foreach (var p in GetComponentsInChildren<ParticleSystem>()) { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); p.Play(true); }
            if (flash) { flash.color = Color.Lerp(new Color(1f, 0.85f, 0.65f), teamColor, 0.35f); flash.range = 18f * s; flash.intensity = flashPeak; }
            BuildCameraShake.Hit(pos, shake * shakeMul * (0.6f + 0.4f * s), shakeRadius * s);
            Destroy(gameObject, life);
        }
        static void Tint(ParticleSystem[] a, Color c)
        {
            if (a == null) return;
            foreach (var ps in a) { if (!ps) continue; var m = ps.main; var sc = m.startColor; m.startColor = new ParticleSystem.MinMaxGradient(BuildConstructFX.Mul(sc.colorMin, c), BuildConstructFX.Mul(sc.colorMax, c)); }
        }
        void Update() { age += Time.deltaTime; if (flash) flash.intensity = flashPeak * Mathf.Exp(-age * 7f); }
    }
}
