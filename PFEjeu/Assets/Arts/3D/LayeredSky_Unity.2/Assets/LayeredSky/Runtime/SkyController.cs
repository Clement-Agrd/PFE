using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LayeredSky
{
    /// <summary>
    /// Chef d'orchestre du ciel : possède l'état courant, le fait évoluer vers un preset (transition animée)
    /// et le pousse vers les "appliers" (matériau, lumière, environnement).
    ///
    ///   Gameplay (vagues, boss, hub)
    ///        │  TransitionTo(preset, secondes)
    ///        ▼
    ///   SkyController ──▶ SkyState (lerp) ──┬─▶ SkyMaterialApplier     (shader du ciel)
    ///                                       ├─▶ SkyLightApplier        (Directional Light)
    ///                                       └─▶ SkyEnvironmentApplier  (ambiance SH, réflexions, brouillard)
    ///
    /// Fondu : un seul SmoothStep sur toute la durée, appliqué à la suite d'ambiances (timeline) → mouvement continu, sans à-coup.
    /// Le matériau du projet n'est JAMAIS modifié en Play : on travaille sur une instance.
    /// Cycle de vie : Awake (instances + preset initial) ; la transition tourne en coroutine ; OnDestroy nettoie.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyController : MonoBehaviour
    {
        #region Inspector
        [Header("Références")]
        [SerializeField, Tooltip("Matériau Skybox/LayeredFantasySky (asset du projet).")]
        private Material skyMaterial;
        [SerializeField, Tooltip("Directional Light principale (optionnelle mais recommandée).")]
        private Light mainLight;

        [Header("Ambiance")]
        [SerializeField] private SkyPreset initialPreset;
        [SerializeField, Tooltip("Ordre du cycle : Jour → Crépuscule → Nuit. Une transition entre deux ambiances NON adjacentes " +
                                 "(ex. Jour → Nuit) traverse celles du milieu : le ciel passe naturellement par le crépuscule. Vide = transition directe.")]
        private SkyPreset[] timeline;
        [SerializeField, Min(0f)] private float defaultTransitionSeconds = 6f;

        [Header("Options")]
        [SerializeField, Tooltip("Pilote aussi RenderSettings.fog (couleur + densité exponentielle²).")]
        private bool driveFog;
        [SerializeField, Min(0.05f), Tooltip("Intervalle min. entre deux DynamicGI.UpdateEnvironment() pendant une transition.")]
        private float giUpdateInterval = 0.5f;
        [SerializeField] private bool useUnscaledTime;
        #endregion

        #region API publique
        /// <summary>Déclenché au DÉBUT d'une transition (ex. pour jouer un son ou prévenir l'UI).</summary>
        public event Action<SkyPreset> PresetChanged;

        /// <summary>Déclenché quand la transition est terminée.</summary>
        public event Action<SkyPreset> PresetReached;

        public SkyPreset CurrentPreset { get; private set; }
        public SkyState CurrentState => _current;
        public bool IsTransitioning => _transition != null;

        /// <summary>Change d'ambiance immédiatement.</summary>
        public void SetPreset(SkyPreset preset)
        {
            TransitionTo(preset, 0f);
        }

        /// <summary>Transition animée vers un preset. seconds &lt; 0 = durée par défaut ; 0 = immédiat.</summary>
        public void TransitionTo(SkyPreset preset, float seconds = -1f)
        {
            StartTransition(preset, seconds, null);
        }

        public void TransitionAlongSunOrbit(SkyPreset preset, float seconds, float sunsetAzimuth,
            float targetAngle, bool returningToDay)
        {
            if (preset == null) return;
            EnsureInitialized();

            Vector3 west = SkyState.DirectionFromAngles(sunsetAzimuth, 0f);
            Vector3 sun = _current.SunDirection;
            float startAngle = Mathf.Atan2(-sun.y, Vector3.Dot(sun, west)) * Mathf.Rad2Deg;
            float travel = Mathf.DeltaAngle(startAngle, targetAngle);
            if (travel < -.001f) travel += 360f;
            var orbit = new SolarOrbit(sunsetAzimuth, startAngle, startAngle + Mathf.Max(0f, travel), returningToDay);
            StartTransition(preset, seconds, orbit);
        }

        private void StartTransition(SkyPreset preset, float seconds, SolarOrbit? orbit)
        {
            if (preset == null)
            {
                Debug.LogWarning("[SkyController] Preset nul : transition ignorée.", this);
                return;
            }

            EnsureInitialized();
            if (_transition != null) StopCoroutine(_transition);

            float duration = seconds < 0f ? defaultTransitionSeconds : seconds;
            BuildWaypoints(CurrentPreset, preset);
            CurrentPreset = preset;
            PresetChanged?.Invoke(preset);

            if (duration <= 0f || !isActiveAndEnabled)
            {
                _current = preset.State;
                if (orbit is SolarOrbit instantOrbit) instantOrbit.Apply(ref _current, instantOrbit.End);
                ApplyAll();
                FinishAll();
                _transition = null;
                PresetReached?.Invoke(preset);
                return;
            }

            _transition = StartCoroutine(TransitionRoutine(duration, preset, orbit));
        }
        #endregion

        #region État interne
        private readonly List<ISkyStateApplier> _appliers = new List<ISkyStateApplier>(3);
        private Material _runtimeMaterial;
        private SkyState _current;
        private readonly List<SkyState> _waypoints = new List<SkyState>(4);
        private Coroutine _transition;
        private bool _initialized;
        #endregion

        #region Cycle de vie Unity
        private void Awake()
        {
            EnsureInitialized();
            if (initialPreset != null) SetPreset(initialPreset);
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        }
        #endregion

        #region Implémentation
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (skyMaterial != null)
            {
                _runtimeMaterial = new Material(skyMaterial) { name = skyMaterial.name + " (runtime)" };
                RenderSettings.skybox = _runtimeMaterial;
                _appliers.Add(new SkyMaterialApplier(_runtimeMaterial));
            }
            else
            {
                Debug.LogWarning("[SkyController] skyMaterial non assigné : le ciel ne sera pas piloté.", this);
            }

            if (mainLight != null)
            {
                _appliers.Add(new SkyLightApplier(mainLight));
                RenderSettings.sun = mainLight;
            }

            _appliers.Add(new SkyEnvironmentApplier(driveFog, giUpdateInterval));

            if (initialPreset != null) _current = initialPreset.State;
        }

        /// <summary>Point de départ (état courant) → ambiances intermédiaires de la timeline → cible.</summary>
        private void BuildWaypoints(SkyPreset from, SkyPreset to)
        {
            _waypoints.Clear();
            _waypoints.Add(_current);

            int a = IndexInTimeline(from);
            int b = IndexInTimeline(to);
            if (a >= 0 && b >= 0 && Mathf.Abs(a - b) > 1)
            {
                int step = b > a ? 1 : -1;
                for (int i = a + step; i != b; i += step)
                    if (timeline[i] != null) _waypoints.Add(timeline[i].State);
            }

            _waypoints.Add(to.State);
        }

        private int IndexInTimeline(SkyPreset preset)
        {
            if (preset == null || timeline == null) return -1;
            for (int i = 0; i < timeline.Length; i++)
                if (timeline[i] == preset) return i;
            return -1;
        }

        private IEnumerator TransitionRoutine(float duration, SkyPreset target, SolarOrbit? orbit)
        {
            int last = _waypoints.Count - 1;
            float[] solarAngles = orbit?.GetWaypointAngles(_waypoints);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                float position = k * last;
                int segment = Mathf.Min((int)position, last - 1);
                float blend = position - segment;
                _current = SkyState.Lerp(_waypoints[segment], _waypoints[segment + 1], blend);
                if (orbit is SolarOrbit solar)
                    solar.Apply(ref _current, Mathf.Lerp(solarAngles[segment], solarAngles[segment + 1], blend));
                ApplyAll();
                yield return null;
            }

            _current = _waypoints[last];
            if (orbit is SolarOrbit endOrbit) endOrbit.Apply(ref _current, endOrbit.End);
            ApplyAll();
            FinishAll();
            _transition = null;
            PresetReached?.Invoke(target);
        }

        private readonly struct SolarOrbit
        {
            private readonly float _sunsetAzimuth;
            private readonly float _start;
            private readonly bool _returningToDay;
            public float End { get; }

            public SolarOrbit(float sunsetAzimuth, float start, float end, bool returningToDay)
            {
                _sunsetAzimuth = sunsetAzimuth;
                _start = start;
                End = end;
                _returningToDay = returningToDay;
            }

            public float[] GetWaypointAngles(List<SkyState> states)
            {
                var angles = new float[states.Count];
                angles[0] = _start;
                angles[angles.Length - 1] = End;
                for (int i = 1; i < angles.Length - 1; i++)
                {
                    // Le même preset d'ambiance sert au couchant et à l'aube, sur deux horizons opposés.
                    float angle = _returningToDay ? 180f + states[i].sunElevation : -states[i].sunElevation;
                    float distance = Mathf.Repeat(angle - _start, 360f);
                    if (distance > End - _start)
                        distance = (End - _start) * i / (angles.Length - 1);
                    angles[i] = Mathf.Clamp(_start + distance, angles[i - 1], End);
                }
                return angles;
            }

            public void Apply(ref SkyState state, float angle)
            {
                float radians = angle * Mathf.Deg2Rad;
                float height = -Mathf.Sin(radians);
                state.sunAzimuth = Mathf.Cos(radians) >= 0f ? _sunsetAzimuth : _sunsetAzimuth + 180f;
                state.sunElevation = Mathf.Asin(Mathf.Clamp(height, -1f, 1f)) * Mathf.Rad2Deg;

                // La lumière suit le soleil près de l'horizon et retrouve la direction nocturne en profondeur.
                float sunlight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-24f, 0f, state.sunElevation));
                if (sunlight <= 0f) return;
                var solarLight = SkyState.DirectionFromAngles(state.sunAzimuth, Mathf.Max(2f, state.sunElevation));
                var light = Vector3.Slerp(state.LightDirection, solarLight, sunlight);
                state.lightAzimuth = Mathf.Atan2(-light.z, light.x) * Mathf.Rad2Deg;
                state.lightElevation = Mathf.Asin(Mathf.Clamp(light.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        private void ApplyAll()
        {
            SkyState state = _current;
            for (int i = 0; i < _appliers.Count; i++) _appliers[i].Apply(in state);
        }

        private void FinishAll()
        {
            for (int i = 0; i < _appliers.Count; i++) _appliers[i].Finish();
        }
        #endregion

#if UNITY_EDITOR
        /// <summary>
        /// Aperçu hors Play. ATTENTION : écrit dans le matériau ASSET et dans la Directional Light de la scène
        /// (c'est le but : voir le ciel dans la Scene view).
        /// </summary>
        [ContextMenu("Prévisualiser le preset initial dans l'éditeur")]
        public void PreviewInEditor()
        {
            if (Application.isPlaying || initialPreset == null || skyMaterial == null) return;

            SkyState state = initialPreset.State;
            new SkyMaterialApplier(skyMaterial).Apply(in state);
            if (mainLight != null)
            {
                UnityEditor.Undo.RecordObject(mainLight.transform, "Sky preview");
                UnityEditor.Undo.RecordObject(mainLight, "Sky preview");
                new SkyLightApplier(mainLight).Apply(in state);
                RenderSettings.sun = mainLight;
            }

            RenderSettings.skybox = skyMaterial;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = state.ambientIntensity;
            UnityEditor.EditorUtility.SetDirty(skyMaterial);
            DynamicGI.UpdateEnvironment();
        }
#endif
    }
}
