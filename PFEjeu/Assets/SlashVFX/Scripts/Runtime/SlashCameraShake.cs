using UnityEngine;

namespace SlashVFX
{
    /// <summary>Additive camera shake, applied after every other camera script.</summary>
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Slash VFX/Camera Shake")]
    public sealed class SlashCameraShake : MonoBehaviour
    {
        [Min(0f)] public float maxOffset = 0.18f;
        [Min(0.01f)] public float decay = 7f;
        public float frequency = 38f;

        float trauma;
        Vector3 applied;

        public static void Kick(float amount)
        {
            Camera c = Camera.main;
            if (c == null || amount <= 0f) return;
            var s = c.GetComponent<SlashCameraShake>();
            if (s == null) s = c.gameObject.AddComponent<SlashCameraShake>();
            s.trauma = Mathf.Min(1f, s.trauma + amount);
        }

        void LateUpdate()
        {
            transform.position -= applied;
            applied = Vector3.zero;
            if (trauma <= 0.001f) { trauma = 0f; return; }

            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime * frequency;
            float amp = trauma * trauma * maxOffset;
            Vector3 local = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * amp);
            applied = transform.TransformVector(local);
            transform.position += applied;
            trauma = Mathf.Max(0f, trauma - decay * dt * 0.25f * (1f + trauma));
        }
    }
}
