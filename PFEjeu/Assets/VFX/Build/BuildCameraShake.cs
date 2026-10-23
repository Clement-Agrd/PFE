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
    /// Secousse caméra par distance. À poser sur la caméra (3e personne). Avec Cinemachine : s'abonner à OnShake et appeler un Impulse Source.
    [DefaultExecutionOrder(1000)]
    public class BuildCameraShake : MonoBehaviour
    {
        public static event Action<Vector3, float, float> OnShake;
        static BuildCameraShake inst;
        public float maxOffset = 0.28f, maxRoll = 1.4f, decay = 1.5f, frequency = 24f;
        float trauma; Vector3 last; Quaternion lastRot = Quaternion.identity;
        void OnEnable() { inst = this; }
        public static void Hit(Vector3 pos, float amount, float radius)
        {
            OnShake?.Invoke(pos, amount, radius);
            if (inst == null) return;
            float k = Mathf.Clamp01(1f - Vector3.Distance(inst.transform.position, pos) / Mathf.Max(radius, 0.01f));
            inst.trauma = Mathf.Min(1f, inst.trauma + amount * k * k);
        }
        void LateUpdate()
        {
            transform.position -= last; transform.rotation *= Quaternion.Inverse(lastRot);
            trauma = Mathf.Max(0f, trauma - decay * Time.deltaTime);
            float s = trauma * trauma, t = Time.time * frequency;
            last = (transform.right * (Mathf.PerlinNoise(t, 0.1f) - 0.5f) + transform.up * (Mathf.PerlinNoise(0.3f, t) - 0.5f)) * 2f * maxOffset * s;
            lastRot = Quaternion.AngleAxis((Mathf.PerlinNoise(t, 0.7f) - 0.5f) * 2f * maxRoll * s, Vector3.forward);
            transform.position += last; transform.rotation *= lastRot;
        }
    }
}
