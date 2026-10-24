using System;
using UnityEngine;

namespace SlashVFX
{
    public enum SlashLayerKind { Halo, Body, Core, After, Streak }

    [Serializable]
    public sealed class SlashLayer
    {
        public Renderer renderer;
        public SlashLayerKind kind = SlashLayerKind.Body;
        [Tooltip("Delay after the slash starts (afterimage 0.03 s, streaks 0.02 s).")]
        public float delay;
        [Tooltip(">1 sweeps faster (the core outruns the halo).")]
        public float revealSpeed = 1f;
        public float intensityMul = 1f;
        public float alphaMul = 1f;
        [Tooltip("Extra delay before the tail starts to dissolve.")]
        public float dissolveLag;
        [Tooltip("Belongs to the second (cross) blade of the finisher.")]
        public bool secondBlade;

        [NonSerialized] public MaterialPropertyBlock mpb;
    }

    /// <summary>
    /// One pooled slash: blade layers (halo/body/core), afterimage, streaks, sparks, impact flash + light and
    /// the finisher extras. Everything is driven procedurally from the age, so nothing pops.
    /// </summary>
    [AddComponentMenu("Slash VFX/Slash Instance")]
    public sealed class SlashInstance : MonoBehaviour
    {
        public struct PlayParams
        {
            public SlashComboStep step;
            public Vector3 position;
            public Vector3 aim;
            public float intensityMul;
            public float scaleMul;
            public Texture2D ramp;
            public SlashQuality quality;
            public Transform follow;
            public float followTime;
        }

        [Header("Layers")]
        public SlashLayer[] layers;
        public Transform bladeRoot;

        [Header("Impact")]
        public ParticleSystem sparks;
        public Transform flash;
        public Renderer flashRenderer;
        public Light impactLight;

        [Header("Finisher extras (optional)")]
        public Transform shockwave;
        public Renderer shockwaveRenderer;
        public Transform scorch;
        public Renderer scorchRenderer;
        public ParticleSystem debris;
        public Transform debrisPlane;

        public event Action<SlashInstance> Impacted;

        public bool Active { get; private set; }
        public float Age => Time.time - startTime;
        public SlashComboStep Step { get; private set; }

        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RampId = Shader.PropertyToID("_Ramp");
        static readonly int MaskId = Shader.PropertyToID("_MainTex");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        const float Anticipation = 2f / 60f; // 2 frames
        const float PeakTime = 0.04f;
        const float TailTime = 0.12f;

        float startTime, duration, intensity, scaleMul, impactAt, endAt;
        bool impacted, bound;
        Vector3 aim, contact, lastScale;
        Texture2D ramp;
        SlashQuality quality;
        Transform follow;
        float followUntil;
        Vector3 followOffset;
        Color flashColor;
        MaterialPropertyBlock auxBlock;
        Camera cam;

        void Bind()
        {
            if (bound) return;
            bound = true;
            auxBlock = new MaterialPropertyBlock();
            if (layers != null)
                foreach (SlashLayer l in layers)
                {
                    if (l == null) continue;
                    if (l.renderer == null)
                    {
                        Debug.LogWarning("[SlashVFX] Layer without renderer on " + name, this);
                        continue;
                    }
                    l.mpb = new MaterialPropertyBlock();
                }
            if (bladeRoot == null) Debug.LogWarning("[SlashVFX] bladeRoot missing on " + name, this);
        }

        public void Hide()
        {
            Bind();
            Active = false;
            if (layers != null)
                foreach (SlashLayer l in layers)
                    if (l != null && l.renderer != null) l.renderer.enabled = false;
            if (flashRenderer != null) flashRenderer.enabled = false;
            if (shockwaveRenderer != null) shockwaveRenderer.enabled = false;
            if (scorchRenderer != null) scorchRenderer.enabled = false;
            if (impactLight != null) impactLight.enabled = false;
            if (sparks != null) sparks.Clear(true);
            if (debris != null) debris.Clear(true);
            gameObject.SetActive(false);
        }

        public void Play(in PlayParams p)
        {
            Bind();
            Step = p.step;
            if (Step == null) return;

            gameObject.SetActive(true);
            startTime = Time.time;
            duration = Step.duration;
            intensity = Step.intensity * p.intensityMul;
            scaleMul = Step.scale * p.scaleMul;
            impactAt = Anticipation + duration * Step.impactFraction;
            impacted = false;
            quality = p.quality;
            ramp = p.ramp != null ? p.ramp : Step.ramp;
            flashColor = Step.flashColor;
            aim = p.aim.sqrMagnitude > 0.0001f ? p.aim.normalized : Vector3.forward;
            cam = Camera.main;

            float roll = Step.tiltDegrees + UnityEngine.Random.Range(-Step.randomRoll, Step.randomRoll);
            Vector3 basePos = p.position + Vector3.up * Step.yOffset;
            transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(aim, Vector3.up) * Quaternion.Euler(Step.pitchDegrees, 0f, roll));
            follow = p.follow;
            followUntil = Time.time + p.followTime;
            followOffset = follow != null ? basePos - follow.position : Vector3.zero;

            if (bladeRoot != null)
                bladeRoot.localScale = new Vector3(Step.mirror ? -scaleMul : scaleMul, scaleMul, scaleMul);

            float tail = Anticipation + duration + TailTime;
            endAt = tail + Step.secondBladeDelay * (HasSecondBlade() ? 1f : 0f) + 0.1f;
            if (Step.shockwave) endAt = Mathf.Max(endAt, impactAt + 1.5f);

            contact = basePos + (transform.rotation * Quaternion.Inverse(Quaternion.Euler(0f, 0f, roll))) * Vector3.forward * (1.5f * scaleMul);
            if (flashRenderer != null) flashRenderer.enabled = false;
            if (shockwaveRenderer != null) shockwaveRenderer.enabled = false;
            if (scorchRenderer != null) scorchRenderer.enabled = false;
            if (impactLight != null) impactLight.enabled = false;

            Active = true;
            Evaluate(0f);
        }

        bool HasSecondBlade()
        {
            if (layers == null) return false;
            foreach (SlashLayer l in layers) if (l != null && l.secondBlade) return true;
            return false;
        }

        void Update()
        {
            if (!Active) return;
            float t = Time.time - startTime;

            if (follow != null && Time.time < followUntil)
                transform.position = follow.position + followOffset;

            Evaluate(t);

            if (t >= endAt && (sparks == null || !sparks.IsAlive(true)) && (debris == null || !debris.IsAlive(true)))
                Hide();
        }

        void Evaluate(float t)
        {
            SlashSettings st = SlashSettings.Instance;
            bool low = quality == SlashQuality.Low;

            if (layers != null)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    SlashLayer l = layers[i];
                    if (l == null || l.renderer == null) continue;
                    bool off = low && (l.kind == SlashLayerKind.After || l.kind == SlashLayerKind.Streak);
                    EvaluateLayer(l, t, off);
                }
            }

            if (!impacted && t >= impactAt)
            {
                impacted = true;
                DoImpact();
            }

            if (impacted) UpdateImpact(t - impactAt);
        }

        void EvaluateLayer(SlashLayer l, float t, bool forceOff)
        {
            float lt = t - l.delay - (l.secondBlade ? Step.secondBladeDelay : 0f);
            float dEnd = Anticipation + duration + TailTime + l.dissolveLag;
            if (forceOff || lt < 0f || lt > dEnd + 0.02f)
            {
                l.renderer.enabled = false;
                return;
            }
            l.renderer.enabled = true;

            float sweepSpan = duration * 0.7f / Mathf.Max(0.2f, l.revealSpeed);
            float reveal;
            if (lt < Anticipation)
                reveal = 0.05f * (lt / Anticipation); // a glint before the strike
            else
                reveal = Step.sweepCurve.Evaluate(Mathf.Clamp01((lt - Anticipation) / sweepSpan));

            float dStart = Anticipation + duration * 0.45f + l.dissolveLag;
            float dis = Mathf.Clamp01((lt - dStart) / Mathf.Max(0.01f, dEnd - dStart));
            dis = Mathf.Pow(dis, 1.5f) * 1.18f;

            float pk = (lt - Anticipation - PeakTime) / 0.035f;
            float pulse = 1f + 0.7f * Mathf.Exp(-pk * pk);
            float ant = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(lt / Anticipation));
            float inten = intensity * l.intensityMul * ant * pulse;

            MaterialPropertyBlock b = l.mpb;
            l.renderer.GetPropertyBlock(b);
            b.SetFloat(RevealId, reveal);
            b.SetFloat(DissolveId, dis);
            b.SetFloat(IntensityId, inten);
            b.SetFloat(ScrollId, lt * 1.5f);
            b.SetColor(TintId, new Color(1f, 1f, 1f, l.alphaMul));
            if (ramp != null) b.SetTexture(RampId, ramp);
            if (Step.mask != null) b.SetTexture(MaskId, Step.mask);
            l.renderer.SetPropertyBlock(b);
        }

        void DoImpact()
        {
            if (sparks != null)
            {
                int n = Mathf.RoundToInt(Step.sparkCount * (quality == SlashQuality.Low ? 0.5f : 1f));
                if (n > 0)
                {
                    sparks.transform.SetPositionAndRotation(contact, Quaternion.LookRotation(aim, Vector3.up));
                    var ep = new ParticleSystem.EmitParams { startColor = flashColor };
                    sparks.Emit(ep, n);
                }
            }

            if (Step.shockwave && debris != null)
            {
                Vector3 g = GroundPoint(contact);
                if (debrisPlane != null) debrisPlane.position = g;
                debris.transform.SetPositionAndRotation(g + Vector3.up * 0.15f, Quaternion.identity);
                var ep = new ParticleSystem.EmitParams { startColor = Color.Lerp(flashColor, new Color(0.15f, 0.1f, 0.08f), 0.5f) };
                debris.Emit(ep, 12);
            }

            if (impactLight != null)
            {
                impactLight.color = flashColor;
                impactLight.enabled = true;
            }
            Impacted?.Invoke(this);
        }

        void UpdateImpact(float it)
        {
            // flash 0.08 s
            if (flash != null && flashRenderer != null)
            {
                const float fl = 0.08f;
                bool on = it >= 0f && it < fl;
                flashRenderer.enabled = on;
                if (on)
                {
                    float k = it / fl;
                    float env = (1f - k) * (1f - k);
                    flash.position = contact;
                    if (cam != null) flash.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                    flash.localScale = Vector3.one * (Step.hitFlashScale * 0.55f * Mathf.Lerp(0.8f, 1.4f, k));
                    flashRenderer.GetPropertyBlock(auxBlock);
                    auxBlock.SetColor(ColorId, new Color(flashColor.r, flashColor.g, flashColor.b, 1f) * 3.5f);
                    auxBlock.SetFloat(FadeId, env);
                    flashRenderer.SetPropertyBlock(auxBlock);
                }
            }

            if (impactLight != null)
            {
                const float ll = 0.1f;
                if (it < ll)
                {
                    float k = 1f - it / ll;
                    impactLight.transform.position = contact;
                    impactLight.intensity = 9f * k * k * Step.hitFlashScale;
                }
                else impactLight.enabled = false;
            }

            if (Step.shockwave)
            {
                Vector3 g = GroundPoint(contact);
                if (shockwave != null && shockwaveRenderer != null)
                {
                    const float sl = 0.5f;
                    bool on = it >= 0f && it < sl;
                    shockwaveRenderer.enabled = on;
                    if (on)
                    {
                        float k = it / sl;
                        float e = 1f - (1f - k) * (1f - k) * (1f - k);
                        shockwave.position = g + Vector3.up * 0.04f;
                        shockwave.localScale = Vector3.one * Mathf.Lerp(0.6f, 7f, e) * scaleMul;
                        shockwaveRenderer.GetPropertyBlock(auxBlock);
                        auxBlock.SetColor(ColorId, new Color(flashColor.r, flashColor.g, flashColor.b, 1f) * 4f);
                        auxBlock.SetFloat(FadeId, (1f - k) * (1f - k));
                        shockwaveRenderer.SetPropertyBlock(auxBlock);
                    }
                }
                if (scorch != null && scorchRenderer != null)
                {
                    const float sc = 1.4f;
                    bool on = it >= 0f && it < sc;
                    scorchRenderer.enabled = on;
                    if (on)
                    {
                        float k = it / sc;
                        scorch.position = g + Vector3.up * 0.02f;
                        scorch.localScale = Vector3.one * 2.6f * scaleMul;
                        scorchRenderer.GetPropertyBlock(auxBlock);
                        auxBlock.SetColor(ColorId, new Color(flashColor.r, flashColor.g, flashColor.b, 1f) * 3f);
                        auxBlock.SetFloat(FadeId, Mathf.Clamp01((1f - k) * 3f) * Mathf.Clamp01(it / 0.04f));
                        scorchRenderer.SetPropertyBlock(auxBlock);
                    }
                }
            }
        }

        static Vector3 GroundPoint(Vector3 from)
        {
            if (Physics.Raycast(from + Vector3.up * 0.5f, Vector3.down, out RaycastHit h, 4f, ~0, QueryTriggerInteraction.Ignore))
                return h.point;
            return new Vector3(from.x, from.y - 1f, from.z);
        }
    }
}
