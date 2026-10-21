using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IceVFX
{
    /// Orchestre l'impact. Timeline :
    ///   0.00  flash blanc (lumière + sprite) ~0.1 s, bris du cristal (débris physiques), hit stop + shake
    ///   0.05  onde de choc (anneau + distorsion)
    ///   0.10+ pics de glace du sol (jaillissent en ~0.12 s avec overshoot), puis se dissolvent
    ///   0.10+ brume et givre qui retombent
    /// Les particules sont dans le prefab (délais via burst time). Recyclé via IcePool.
    public class IceImpact : MonoBehaviour
    {
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        [Header("Flash")]
        public Light flashLight;
        public float peakIntensity = 12f;
        [Tooltip("Durée du flash blanc (s, temps réel)")] [Range(0.03f, 0.3f)] public float flashTime = 0.1f;

        [Header("Feedback caméra")]
        [Range(0f, 0.5f)] public float cameraShake = 0.12f;
        [Range(0f, 0.1f)] public float hitStop = 0.04f;

        [Header("Pics de glace")]
        [Tooltip("6 à 9 cristaux enfants (désactivés en trop selon le tirage / la qualité)")]
        public Transform[] spikes;
        [Tooltip("Délai avant la première éruption (s)")] [Range(0f, 0.4f)] public float spikeDelay = 0.1f;
        [Tooltip("Durée de la pousse, de 0 à 1 (s)")] [Range(0.05f, 0.4f)] public float spikeGrow = 0.12f;
        [Tooltip("Dépassement (EaseOutBack) : 0 = aucun")] [Range(0f, 4f)] public float spikeOvershoot = 2.2f;
        [Range(0f, 4f)] public float spikeHold = 1.6f;
        [Range(0.1f, 2f)] public float spikeDissolve = 0.9f;
        [Tooltip("Rayon de dispersion min/max autour du point d'impact")] public Vector2 spikeRadius = new Vector2(0.55f, 2.1f);
        [Tooltip("Hauteur min/max (m)")] public Vector2 spikeHeight = new Vector2(0.9f, 1.9f);

        [Header("Givre (decal optionnel)")]
        [Tooltip("Rempli par le builder si le Decal Renderer Feature est actif, sinon le quad GroundFrost est utilisé")]
        public DecalProjector frostDecal;
        public float frostLife = 4.5f;

        [Header("Qualité")]
        public IceQuality quality = IceQuality.High;
        [Tooltip("Plafond de particules actives en Low (projectile + impact ≈ 150)")]
        [Range(10, 150)] public int lowParticleBudget = 90;

        public float life = 6f;

        // pic de glace : état par instance (tableaux alloués une fois à l'Awake)
        ParticleSystem[] systems;
        Renderer[] spikeRenderers;
        float[] sDelay, sHeight, sWidth;
        Quaternion[] sRot;
        bool[] sOn;
        MaterialPropertyBlock block;

        float age, unscaledAge;
        bool started, shaking, stopping;
        float shakeStart;

        // état partagé entre impacts simultanés
        static Transform shakeCam; static Vector3 shakeOrigin; static int shakeCount;
        static bool hitStopActive;

        void Awake()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            block = new MaterialPropertyBlock();
            int n = spikes != null ? spikes.Length : 0;
            spikeRenderers = new Renderer[n]; sDelay = new float[n]; sHeight = new float[n]; sWidth = new float[n];
            sRot = new Quaternion[n]; sOn = new bool[n];
            for (int i = 0; i < n; i++) spikeRenderers[i] = spikes[i].GetComponent<Renderer>();
            IceVFXUtil.ApplyQuality(systems, quality, lowParticleBudget);
        }

        void OnEnable()
        {
            age = 0f; unscaledAge = 0f; started = false;
            IceVFXUtil.Restart(systems);
            if (flashLight) flashLight.intensity = peakIntensity;

            // tirage des pics : 6..9 (6 en Low), répartis autour du point d'impact, l'axe de pousse suit la normale (+Z local)
            int count = quality == IceQuality.Low ? 6 : Random.Range(6, 10);
            int n = spikes != null ? spikes.Length : 0;
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                sOn[i] = i < count;
                spikes[i].gameObject.SetActive(sOn[i]);
                if (!sOn[i]) continue;
                float a = a0 + (i + Random.value * 0.5f) / count * Mathf.PI * 2f;
                float r = Mathf.Lerp(spikeRadius.x, spikeRadius.y, Mathf.Sqrt(Random.value));
                float far = Mathf.InverseLerp(spikeRadius.x, spikeRadius.y, r);
                Vector3 outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                spikes[i].localPosition = outward * r;
                sRot[i] = Quaternion.FromToRotation(Vector3.up, (Vector3.forward + outward * Random.Range(0.1f, 0.5f)).normalized);
                spikes[i].localRotation = sRot[i];
                sHeight[i] = Mathf.Lerp(spikeHeight.y, spikeHeight.x, far) * Random.Range(0.8f, 1.15f);
                sWidth[i] = Random.Range(0.18f, 0.3f) * (1.1f - 0.3f * far);
                sDelay[i] = spikeDelay + far * 0.05f + Random.value * 0.04f;
                spikes[i].localScale = Vector3.zero;
                SetSpikeDissolve(i, 0f);
            }
            if (frostDecal) frostDecal.fadeFactor = 0f;
        }

        void OnDisable()
        {
            EndShake();
            if (stopping) { Time.timeScale = 1f; stopping = false; hitStopActive = false; }
        }

        void SetSpikeDissolve(int i, float d)
        {
            var r = spikeRenderers[i];
            if (!r) return;
            r.GetPropertyBlock(block);
            block.SetFloat(DissolveId, d);
            r.SetPropertyBlock(block);
        }

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            age += Time.deltaTime; unscaledAge += udt;

            if (!started)
            {
                started = true;
                if (cameraShake > 0f && Camera.main) StartShake();
                if (hitStop > 0f && !hitStopActive && Time.timeScale > 0.99f) { hitStopActive = true; stopping = true; Time.timeScale = 0.05f; }
            }
            if (stopping && unscaledAge >= hitStop) { Time.timeScale = 1f; stopping = false; hitStopActive = false; }
            if (shaking)
            {
                float t = unscaledAge - shakeStart;
                if (t >= 0.28f) EndShake();
                else { float k = 1f - t / 0.28f; shakeCam.localPosition = shakeOrigin + Random.insideUnitSphere * (cameraShake * k * k); }
            }

            // lumière : flash blanc bref, puis lueur résiduelle du givre
            if (flashLight)
            {
                if (unscaledAge < flashTime) { float k = 1f - unscaledAge / flashTime; flashLight.intensity = peakIntensity * k * k; }
                else flashLight.intensity = Mathf.Max(0f, 1.2f * (1f - age / 2.2f));
            }

            // pics : pousse avec overshoot, maintien, puis dissolve
            for (int i = 0; i < sOn.Length; i++)
            {
                if (!sOn[i]) continue;
                float t = (age - sDelay[i]) / spikeGrow;
                if (t < 0f) continue;
                float e = IceVFXUtil.EaseOutBack(t, spikeOvershoot);
                float w = Mathf.Clamp01(t * 1.6f);
                spikes[i].localScale = new Vector3(sWidth[i] * w, sHeight[i] * e, sWidth[i] * w);
                float td = (age - sDelay[i] - spikeGrow - spikeHold) / spikeDissolve;
                if (td > 0f) SetSpikeDissolve(i, Mathf.Clamp01(td));
            }

            if (frostDecal)
            {
                float f = age < 0.15f ? age / 0.15f : Mathf.Clamp01((frostLife - age) / (frostLife * 0.4f));
                frostDecal.fadeFactor = Mathf.Clamp01(f);
            }

            if (age >= life) IcePool.Release(gameObject);
        }

        void StartShake()
        {
            if (shakeCount == 0) { shakeCam = Camera.main.transform; shakeOrigin = shakeCam.localPosition; }
            shakeCount++; shaking = true; shakeStart = unscaledAge;
        }

        void EndShake()
        {
            if (!shaking) return;
            shaking = false;
            if (--shakeCount <= 0) { shakeCount = 0; if (shakeCam) shakeCam.localPosition = shakeOrigin; }
        }
    }
}
