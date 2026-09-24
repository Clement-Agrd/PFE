using System;
using UnityEngine;

namespace Core.DayNightSystem
{
    /// <summary>
    /// Cycle jour/nuit : fait avancer l'heure, oriente le soleil (Directional Light),
    /// applique sa couleur/intensité et l'ambiante depuis un profil, et émet des
    /// events (heure, phase, jour).
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        [Header("Temps")]
        [Tooltip("Durée réelle (s) d'une journée complète de 24 h.")]
        [SerializeField, Min(1f)] private float dayLengthSeconds = 120f;
        [Tooltip("Heure de départ, 0 → 1 (0.25 = 6h, 0.5 = midi).")]
        [SerializeField, Range(0f, 1f)] private float startTime01 = 0.25f;
        [SerializeField] private bool paused;

        [Header("Visuel")]
        [SerializeField] private Light sun;
        [SerializeField] private DayNightProfile profile;

        [Header("Phases (fractions de journée, croissantes)")]
        [SerializeField, Range(0f, 1f)] private float dawnStart = 0.22f;
        [SerializeField, Range(0f, 1f)] private float dayStart = 0.30f;
        [SerializeField, Range(0f, 1f)] private float duskStart = 0.72f;
        [SerializeField, Range(0f, 1f)] private float nightStart = 0.80f;

        private float _time01;
        private int _dayCount;
        private int _lastHour = -1;
        private DayPhase _phase;

        public float Time01 => _time01;
        public float Hour => _time01 * 24f;
        public int DayCount => _dayCount;
        public DayPhase Phase => _phase;
        public bool Paused { get => paused; set => paused = value; }

        public event Action<int> OnHourChanged;       // heure entière 0..23
        public event Action<int> OnDayPassed;         // numéro du nouveau jour
        public event Action<DayPhase> OnPhaseChanged;

        private void Start()
        {
            _time01 = startTime01;
            _phase = ComputePhase(_time01);
            _lastHour = Mathf.FloorToInt(Hour) % 24;

            ApplyVisual();
            OnPhaseChanged?.Invoke(_phase);
            OnHourChanged?.Invoke(_lastHour);
        }

        private void Update()
        {
            if (paused) return;

            _time01 += Time.deltaTime / dayLengthSeconds;
            while (_time01 >= 1f)
            {
                _time01 -= 1f;
                _dayCount++;
                OnDayPassed?.Invoke(_dayCount);
            }

            ApplyVisual();
            DetectHour();
            DetectPhase();
        }

        /// <summary>Fixe l'heure (0..24). Applique aussitôt le visuel et les events.</summary>
        public void SetTime(float hour) => SetTime01(Mathf.Repeat(hour / 24f, 1f));

        public void SetTime01(float t)
        {
            _time01 = Mathf.Clamp01(t);
            ApplyVisual();
            DetectHour();
            DetectPhase();
        }

        private void ApplyVisual()
        {
            if (sun != null)
            {
                // 0.25 (6h) → soleil à l'horizon ; 0.5 (midi) → au zénith.
                sun.transform.rotation = Quaternion.Euler(_time01 * 360f - 90f, 170f, 0f);

                if (profile != null)
                {
                    sun.color = profile.EvaluateSunColor(_time01);
                    sun.intensity = profile.EvaluateSunIntensity(_time01);
                }
            }

            if (profile != null && profile.DriveAmbient)
                RenderSettings.ambientLight = profile.EvaluateAmbient(_time01);
        }

        private void DetectHour()
        {
            int h = Mathf.FloorToInt(Hour) % 24;
            if (h != _lastHour)
            {
                _lastHour = h;
                OnHourChanged?.Invoke(h);
            }
        }

        private void DetectPhase()
        {
            DayPhase p = ComputePhase(_time01);
            if (p != _phase)
            {
                _phase = p;
                OnPhaseChanged?.Invoke(p);
            }
        }

        private DayPhase ComputePhase(float t)
        {
            if (t >= nightStart || t < dawnStart) return DayPhase.Night;
            if (t < dayStart) return DayPhase.Dawn;
            if (t < duskStart) return DayPhase.Day;
            return DayPhase.Dusk;
        }

        // --- Sauvegarde ---
        public void Capture(out float time01, out int day)
        {
            time01 = _time01;
            day = _dayCount;
        }

        public void Restore(float time01, int day)
        {
            _time01 = Mathf.Clamp01(time01);
            _dayCount = day;
            ApplyVisual();
            DetectHour();
            DetectPhase();
        }
    }
}
