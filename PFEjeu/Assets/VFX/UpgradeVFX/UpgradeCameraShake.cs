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
    /// Secousse caméra par distance. Si BuildVFX.BuildCameraShake est déjà sur la caméra, il est utilisé à la place (pas de doublon).
    [DefaultExecutionOrder(1000)]
    public class UpgradeCameraShake : MonoBehaviour
    {
        public static event Action<Vector3, float, float> OnShake;
        static UpgradeCameraShake inst;
        public float maxOffset = 0.28f, maxRoll = 1.4f, decay = 1.5f, frequency = 24f;
        float trauma; Vector3 last; Quaternion lastRot = Quaternion.identity;
        void OnEnable() { inst = this; }
        public static void Hit(Vector3 pos, float amount, float radius)
        {
            OnShake?.Invoke(pos, amount, radius);
            if (inst != null)
            {
                float k = Mathf.Clamp01(1f - Vector3.Distance(inst.transform.position, pos) / Mathf.Max(radius, 0.01f));
                inst.trauma = Mathf.Min(1f, inst.trauma + amount * k * k);
                return;
            }
            try { var t = Type.GetType("BuildVFX.BuildCameraShake"); if (t != null) t.GetMethod("Hit")?.Invoke(null, new object[] { pos, amount, radius }); } catch { }
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
