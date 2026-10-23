using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Anime le shader FireBall : apparition (dissolve du noyau vers l'extérieur), pulsation, rotation du noyau.
    public class FireBallVisual : MonoBehaviour
    {
        [Header("Apparition")]
        public float spawnTime = 0.25f;
        [Header("Vie")]
        [Range(0f, 0.3f)] public float pulse = 0.1f;
        public float pulseSpeed = 14f;
        public Transform spinTarget;
        public Vector3 spinAxis = new Vector3(0.3f, 0.2f, 1f);
        public float spinSpeed = 220f;

        MeshRenderer[] rends; MaterialPropertyBlock mpb; Vector3 baseScale; float t0, seed;
        static readonly int Dis = Shader.PropertyToID("_Dissolve");

        void Awake() { rends = GetComponentsInChildren<MeshRenderer>(); mpb = new MaterialPropertyBlock(); baseScale = transform.localScale; }
        void OnEnable() { t0 = Time.time; seed = Random.value * 20f; SetDissolve(1f); }

        void SetDissolve(float d)
        {
            foreach (var r in rends) { r.GetPropertyBlock(mpb); mpb.SetFloat(Dis, d); r.SetPropertyBlock(mpb); }
        }

        void Update()
        {
            float k = spawnTime <= 0f ? 1f : Mathf.Clamp01((Time.time - t0) / spawnTime);
            if (k < 1f || Time.time - t0 < spawnTime + 0.05f) SetDissolve(1f - k * k * (3f - 2f * k));
            float n = Mathf.PerlinNoise(Time.time * pulseSpeed * 0.2f, seed) - 0.5f;
            transform.localScale = baseScale * (Mathf.Lerp(0.2f, 1f, k) * (1f + n * pulse * 2f));
            if (spinTarget) spinTarget.Rotate(spinAxis.normalized, spinSpeed * Time.deltaTime, Space.Self);
        }
    }
}
