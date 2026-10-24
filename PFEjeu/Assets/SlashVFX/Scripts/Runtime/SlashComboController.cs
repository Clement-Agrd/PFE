using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SlashVFX
{
    /// <summary>
    /// Combo state machine + pooled slash playback. Attack() buffers an input, RegisterAttack() advances the
    /// combo (window after the previous swing) or restarts at step 1. PlayStep(i) plays a step directly, for
    /// gameplay code that already owns the combo (see SlashMeleeBridge).
    /// </summary>
    [AddComponentMenu("Slash VFX/Slash Combo Controller")]
    public sealed class SlashComboController : MonoBehaviour
    {
        [Header("Combo")]
        public SlashComboStep[] steps;
        [Tooltip("Time after the end of a swing during which the next input continues the combo.")]
        [Min(0.05f)] public float comboWindow = 0.6f;
        [Tooltip("An input pressed this long before the swing ends is kept.")]
        [Min(0f)] public float inputBuffer = 0.15f;
        [Tooltip("External multiplier (e.g. a streak of hits without damage) boosting the last step.")]
        [Range(1f, 3f)] public float comboMultiplier = 1f;

        [Header("Origin / Aim")]
        [Tooltip("Where slashes start (chest height). Defaults to this transform.")]
        public Transform origin;
        [Tooltip("Attack direction. Zero = origin forward.")]
        public Vector3 aim;
        public bool followBlade;
        [Tooltip("Sword tip: the slash follows it for a short time.")]
        public Transform bladeTip;
        [Range(0f, 0.2f)] public float followTime = 0.05f;

        [Header("Style")]
        public SlashStyle style = SlashStyle.None;

        [Header("Feel")]
        public bool enableHitStop = true;
        public bool enableShake = true;
        [Tooltip("Global volume whose Bloom is pulsed by the finisher.")]
        public Volume bloomVolume;
        [Range(0f, 3f)] public float bloomPulseAmount = 1.2f;

        [Header("Input")]
        public bool readMouse = true;

        public event Action<int> OnComboStep;
        public int CurrentStep { get; private set; } = -1;
        public SlashInstance LastInstance { get; private set; }

        readonly List<SlashInstance>[] pools = new List<SlashInstance>[8];
        float bufferedUntil = -1f;
        float swingEnd = -10f;
        int lastStep = -1;
        float hitStopUntil;
        float savedTimeScale = 1f;
        bool inHitStop;
        Bloom bloom;
        float bloomBase;
        float bloomPulseT = 1f;
        AudioSource audioSrc;

        void Awake()
        {
            if (origin == null) origin = transform;
            if (bloomVolume != null && bloomVolume.profile != null && bloomVolume.profile.TryGet(out bloom))
                bloomBase = bloom.intensity.value;
        }

        void OnDisable()
        {
            if (inHitStop) { Time.timeScale = savedTimeScale; inHitStop = false; }
            try { if (bloom != null) bloom.intensity.value = bloomBase; } catch (System.Exception) { }
        }

        // ------------------------------------------------------------------ input / state machine

        /// <summary>Public entry for animator or gameplay events.</summary>
        public void Attack()
        {
            bufferedUntil = Time.unscaledTime + inputBuffer;
        }

        void Update()
        {
            if (readMouse && MousePressed()) Attack();

            float now = Time.time;
            if (bufferedUntil >= Time.unscaledTime && now >= swingEnd)
            {
                bufferedUntil = -1f;
                RegisterAttack();
            }

            TickHitStop();
            TickBloom();
        }

        static bool MousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        /// <summary>Advances the combo when inside the window, else restarts at step 1. Returns the step played.</summary>
        public int RegisterAttack()
        {
            if (steps == null || steps.Length == 0) return -1;
            int next = 0;
            if (lastStep >= 0 && Time.time - swingEnd <= comboWindow)
                next = (lastStep + 1) % steps.Length;
            PlayStep(next);
            return next;
        }

        /// <summary>Plays one step now from the origin (also moves the state machine there).</summary>
        public void PlayStep(int index)
        {
            Vector3 a0 = aim.sqrMagnitude > 0.0001f ? aim : origin.forward;
            PlayStepAt(index, origin.position, a0);
        }

        /// <summary>Plays one step at a world position and direction.</summary>
        public void PlayStepAt(int index, Vector3 position, Vector3 direction)
        {
            if (steps == null || steps.Length == 0) return;
            index = Mathf.Clamp(index, 0, steps.Length - 1);
            SlashComboStep step = steps[index];
            if (step == null || step.prefab == null)
            {
                Debug.LogWarning($"[SlashVFX] Step {index} or its prefab is missing.", this);
                return;
            }

            lastStep = index;
            CurrentStep = index;
            swingEnd = Time.time + 0.033f + step.duration;

            SlashInstance inst = Acquire(index, step);
            if (inst == null) return;

            float boost = (index == steps.Length - 1) ? comboMultiplier : 1f;
            float t = steps.Length > 1 ? index / (float)(steps.Length - 1) : 0f;

            var pp = new SlashInstance.PlayParams
            {
                step = step,
                position = position,
                aim = direction,
                intensityMul = boost * Mathf.Lerp(0.9f, 1.1f, t),
                scaleMul = Mathf.Lerp(1f, 1.05f, t) * (boost > 1f ? 1f + (boost - 1f) * 0.2f : 1f),
                ramp = SlashSettings.Instance.RampFor(style),
                quality = SlashSettings.Instance.quality,
                follow = followBlade ? bladeTip : null,
                followTime = followBlade ? followTime : 0f
            };
            inst.Play(pp);
            LastInstance = inst;

            if (step.sfx != null)
            {
                if (audioSrc == null)
                {
                    audioSrc = gameObject.GetComponent<AudioSource>();
                    if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
                    audioSrc.playOnAwake = false;
                }
                audioSrc.PlayOneShot(step.sfx, step.sfxVolume);
            }

            OnComboStep?.Invoke(index);
        }

        // ------------------------------------------------------------------ pool

        SlashInstance Acquire(int index, SlashComboStep step)
        {
            if (index >= pools.Length) index = pools.Length - 1;
            List<SlashInstance> pool = pools[index] ?? (pools[index] = new List<SlashInstance>(4));
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && !pool[i].Active) return pool[i];

            if (pool.Count >= 6) // recycle the oldest instead of growing
            {
                SlashInstance old = pool[0];
                for (int i = 1; i < pool.Count; i++) if (pool[i].Age > old.Age) old = pool[i];
                old.Hide();
                return old;
            }

            SlashInstance inst = Instantiate(step.prefab);
            inst.name = step.prefab.name + "_" + pool.Count;
            inst.Impacted += OnImpact;
            inst.Hide();
            pool.Add(inst);
            return inst;
        }

        void OnImpact(SlashInstance inst)
        {
            SlashComboStep s = inst.Step;
            if (s == null) return;
            if (enableShake && s.shake > 0f) SlashCameraShake.Kick(s.shake);
            if (enableHitStop && s.hitStop > 0f) StartHitStop(s.hitStop);
            if (s.bloomPulse && bloom != null) bloomPulseT = 0f;
        }

        // ------------------------------------------------------------------ hit stop / bloom pulse

        void StartHitStop(float seconds)
        {
            if (Time.timeScale < 0.9f && !inHitStop) return; // paused or in slow-mo already
            if (!inHitStop) savedTimeScale = Time.timeScale;
            inHitStop = true;
            hitStopUntil = Time.unscaledTime + seconds;
            Time.timeScale = 0.03f;
        }

        void TickHitStop()
        {
            if (inHitStop && Time.unscaledTime >= hitStopUntil)
            {
                Time.timeScale = savedTimeScale;
                inHitStop = false;
            }
        }

        void TickBloom()
        {
            if (bloom == null || bloomPulseT >= 1f) return;
            bloomPulseT += Time.unscaledDeltaTime / 0.5f;
            float k = Mathf.Clamp01(bloomPulseT);
            float env = Mathf.Sin(Mathf.Clamp01(k * 3f) * Mathf.PI * 0.5f) * (1f - k);
            bloom.intensity.value = bloomBase + bloomPulseAmount * env;
            if (k >= 1f) bloom.intensity.value = bloomBase;
        }
    }
}
