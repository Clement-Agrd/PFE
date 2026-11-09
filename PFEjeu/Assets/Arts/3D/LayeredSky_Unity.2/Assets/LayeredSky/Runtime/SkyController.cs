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
                ApplyAll();
                FinishAll();
                _transition = null;
                PresetReached?.Invoke(preset);
                return;
            }

            _transition = StartCoroutine(TransitionRoutine(duration, preset));
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

        private IEnumerator TransitionRoutine(float duration, SkyPreset target)
        {
            int last = _waypoints.Count - 1;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                float position = k * last;
                int segment = Mathf.Min((int)position, last - 1);
                _current = SkyState.Lerp(_waypoints[segment], _waypoints[segment + 1], position - segment);
                ApplyAll();
                yield return null;
            }

            _current = _waypoints[last];
            ApplyAll();
            FinishAll();
            _transition = null;
            PresetReached?.Invoke(target);
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
