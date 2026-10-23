using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FireVFX
{
    /// Orchestre l'impact : flash, lueur résiduelle, hit stop, screen shake, qualité. Instance poolée.
    public class FireImpact : MonoBehaviour
    {
        [Header("Lumière")]
        public Light flashLight;
        public float peakIntensity = 170f;
        public float flashTime = 0.45f;
        public float embersGlow = 24f;
        public float embersTime = 3.5f;
        [Header("Feedback")]
        [Range(0f, 0.8f)] public float cameraShake = 0.25f;
        [Range(0f, 0.1f)] public float hitStop = 0.05f;
        [Header("Cycle")]
        public float life = 8f;

        static bool stopping, shaking;
        ParticleSystem[] systems; bool qualityApplied; float t0;

        void Awake() { systems = GetComponentsInChildren<ParticleSystem>(true); ApplyQuality(); }

        void ApplyQuality()
        {
            if (qualityApplied) return; qualityApplied = true;
            var q = FireSettings.quality; if (q == FireQuality.High) return;
            float k = q == FireQuality.Low ? 0.4f : 0.7f;
            foreach (var ps in systems)
            {
                if (q == FireQuality.Low && ps.name.StartsWith("Distortion")) { ps.gameObject.SetActive(false); continue; }
                var m = ps.main; m.maxParticles = Mathf.Max(8, Mathf.RoundToInt(m.maxParticles * k));
                var e = ps.emission; var bursts = new ParticleSystem.Burst[e.burstCount]; e.GetBursts(bursts);
                for (int i = 0; i < bursts.Length; i++) bursts[i].count = Mathf.Max(1, Mathf.RoundToInt(bursts[i].count.constantMax * k));
                e.SetBursts(bursts); e.rateOverTimeMultiplier *= k;
                if (q == FireQuality.Low) { var t = ps.trails; t.enabled = false; }
            }
        }

        void OnEnable()
        {
            t0 = Time.time;
            foreach (var ps in systems) { ps.Clear(true); ps.Play(true); }
            StartCoroutine(Flash());
            if (FireSettings.feedback && cameraShake > 0f && !shaking && Camera.main) StartCoroutine(Shake(Camera.main.transform));
            if (FireSettings.feedback && hitStop > 0f && !stopping && Time.timeScale > 0.99f) StartCoroutine(HitStop());
        }

        void Update()
        {
            if (flashLight) FireLights.Report(flashLight.transform.position, flashLight.color, flashLight.intensity, flashLight.range);
            if (Time.time - t0 > life) FirePool.Release(gameObject);
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (stopping) { Time.timeScale = 1f; stopping = false; }
            shaking = false;
        }

        IEnumerator Flash()
        {
            for (float t = 0; t < flashTime; t += Time.unscaledDeltaTime)
            {
                float k = 1f - t / flashTime;
                if (flashLight) flashLight.intensity = peakIntensity * k * k * k;
                yield return null;
            }
            // braises : lueur orange vacillante
            for (float t = 0; t < embersTime; t += Time.deltaTime)
            {
                float k = 1f - t / embersTime;
                float f = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, 3.3f);
                if (flashLight) flashLight.intensity = embersGlow * k * k * f;
                yield return null;
            }
            if (flashLight) flashLight.intensity = 0f;
        }

        IEnumerator Shake(Transform cam)
        {
            shaking = true; Vector3 o = cam.localPosition; const float D = 0.35f;
            for (float t = 0; t < D; t += Time.unscaledDeltaTime)
            {
                float k = 1f - t / D;
                cam.localPosition = o + Random.insideUnitSphere * cameraShake * k * k;
                yield return null;
            }
            cam.localPosition = o; shaking = false;
        }

        IEnumerator HitStop()
        {
            stopping = true; Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(hitStop);
            Time.timeScale = 1f; stopping = false;
        }
    }
}
