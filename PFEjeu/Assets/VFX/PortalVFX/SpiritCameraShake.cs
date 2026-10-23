using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SpiritVFX
{
    /// Secousse caméra par distance. Retombe sur BuildVFX / UpgradeVFX si leur composant est déjà sur la caméra.
    [DefaultExecutionOrder(1000)]
    public class SpiritCameraShake : MonoBehaviour
    {
        public static event Action<Vector3, float, float> OnShake;
        static SpiritCameraShake inst;
        public float maxOffset = 0.32f, maxRoll = 1.6f, decay = 1.2f, frequency = 26f;
        float trauma; Vector3 last; Quaternion lastRot = Quaternion.identity;
        void OnEnable() { inst = this; }
        public static void Hit(Vector3 pos, float amount, float radius)
        {
            OnShake?.Invoke(pos, amount, radius);
            if (inst != null)
            {
                float k = Mathf.Clamp01(1f - Vector3.Distance(inst.transform.position, pos) / Mathf.Max(radius, 0.01f));
                inst.trauma = Mathf.Max(inst.trauma, Mathf.Min(1f, amount * k * k)); return;
            }
            foreach (var n in new[] { "UpgradeVFX.UpgradeCameraShake", "BuildVFX.BuildCameraShake" })
            { try { var t = Type.GetType(n); if (t != null) { t.GetMethod("Hit")?.Invoke(null, new object[] { pos, amount, radius }); return; } } catch { } }
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
