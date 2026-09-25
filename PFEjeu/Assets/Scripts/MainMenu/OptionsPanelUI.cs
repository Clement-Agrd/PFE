using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Core.MainMenu
{
    /// <summary>
    /// Écran d'options : vidéo (résolution, plein écran, qualité, vsync) et
    /// audio (volume général). Applique chaque changement immédiatement et le
    /// sauvegarde dans PlayerPrefs pour qu'il persiste au prochain lancement.
    /// Volume séparé musique/bruitages : nécessite un AudioMixer avec des buses
    /// dédiées, qui n'existe pas encore dans le projet — seul le volume général
    /// (AudioListener.volume) est câblé pour l'instant.
    /// </summary>
    public sealed class OptionsPanelUI : MonoBehaviour
    {
        private const string PrefResolutionIndex = "opt_resolution_index";
        private const string PrefFullscreen = "opt_fullscreen";
        private const string PrefQualityIndex = "opt_quality_index";
        private const string PrefVSync = "opt_vsync";
        private const string PrefMasterVolume = "opt_master_volume";

        [Header("Panneau")]
        [SerializeField] private GameObject panel;
        [Tooltip("Groupe des boutons du menu principal, masqué pendant que les options sont ouvertes.")]
        [SerializeField] private GameObject mainButtonsGroup;
        [SerializeField] private Button backButton;

        [Header("Vidéo")]
        [SerializeField] private TMP_Text resolutionLabel;
        [SerializeField] private Button resolutionPrev;
        [SerializeField] private Button resolutionNext;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private TMP_Text qualityLabel;
        [SerializeField] private Button qualityPrev;
        [SerializeField] private Button qualityNext;
        [SerializeField] private Toggle vsyncToggle;

        [Header("Audio")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private TMP_Text masterVolumeValueLabel;

        private Resolution[] _resolutions;
        private int _resolutionIndex;
        private int _qualityIndex;

        public bool IsShown => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(Hide);
            if (resolutionPrev != null) resolutionPrev.onClick.AddListener(() => StepResolution(-1));
            if (resolutionNext != null) resolutionNext.onClick.AddListener(() => StepResolution(1));
            if (qualityPrev != null) qualityPrev.onClick.AddListener(() => StepQuality(-1));
            if (qualityNext != null) qualityNext.onClick.AddListener(() => StepQuality(1));
            if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
            if (vsyncToggle != null) vsyncToggle.onValueChanged.AddListener(SetVSync);
            if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);

            BuildResolutionList();
            LoadAndApplySavedSettings();
        }

        private void Start() => HideImmediate();

        private void Update()
        {
            if (!IsShown) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Hide();
        }

        public void Show()
        {
            if (panel != null) panel.SetActive(true);
            if (mainButtonsGroup != null) mainButtonsGroup.SetActive(false);
            RefreshLabels();
        }

        public void Hide() => HideImmediate();

        private void HideImmediate()
        {
            if (panel != null) panel.SetActive(false);
            if (mainButtonsGroup != null) mainButtonsGroup.SetActive(true);
        }

        #region Résolution

        private void BuildResolutionList()
        {
            _resolutions = Screen.resolutions
                .Select(r => new Resolution { width = r.width, height = r.height, refreshRateRatio = r.refreshRateRatio })
                .GroupBy(r => (r.width, r.height))
                .Select(g => g.First())
                .OrderBy(r => r.width * r.height)
                .ToArray();

            if (_resolutions.Length == 0) _resolutions = new[] { Screen.currentResolution };

            _resolutionIndex = System.Array.FindIndex(_resolutions,
                r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height);
            if (_resolutionIndex < 0) _resolutionIndex = _resolutions.Length - 1;
        }

        private void StepResolution(int direction)
        {
            _resolutionIndex = (_resolutionIndex + direction + _resolutions.Length) % _resolutions.Length;
            ApplyResolution();
            RefreshLabels();
        }

        private void ApplyResolution()
        {
            Resolution r = _resolutions[_resolutionIndex];
            Screen.SetResolution(r.width, r.height, Screen.fullScreen);
            PlayerPrefs.SetInt(PrefResolutionIndex, _resolutionIndex);
        }

        #endregion

        #region Plein écran / Qualité / VSync

        private void SetFullscreen(bool value)
        {
            Screen.fullScreen = value;
            PlayerPrefs.SetInt(PrefFullscreen, value ? 1 : 0);
        }

        private void StepQuality(int direction)
        {
            int count = QualitySettings.names.Length;
            if (count == 0) return;

            _qualityIndex = (_qualityIndex + direction + count) % count;
            QualitySettings.SetQualityLevel(_qualityIndex, true);
            PlayerPrefs.SetInt(PrefQualityIndex, _qualityIndex);
            RefreshLabels();
        }

        private void SetVSync(bool value)
        {
            QualitySettings.vSyncCount = value ? 1 : 0;
            PlayerPrefs.SetInt(PrefVSync, value ? 1 : 0);
        }

        #endregion

        #region Audio

        private void SetMasterVolume(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(PrefMasterVolume, value);
            if (masterVolumeValueLabel != null) masterVolumeValueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        #endregion

        private void LoadAndApplySavedSettings()
        {
            if (PlayerPrefs.HasKey(PrefResolutionIndex))
            {
                int saved = PlayerPrefs.GetInt(PrefResolutionIndex);
                if (saved >= 0 && saved < _resolutions.Length) _resolutionIndex = saved;
            }

            bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            Screen.fullScreen = fullscreen;

            _qualityIndex = PlayerPrefs.GetInt(PrefQualityIndex, QualitySettings.GetQualityLevel());
            QualitySettings.SetQualityLevel(_qualityIndex, true);

            bool vsync = PlayerPrefs.GetInt(PrefVSync, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(vsync);
            QualitySettings.vSyncCount = vsync ? 1 : 0;

            float volume = PlayerPrefs.GetFloat(PrefMasterVolume, AudioListener.volume);
            AudioListener.volume = volume;
            if (masterVolumeSlider != null) masterVolumeSlider.SetValueWithoutNotify(volume);
            if (masterVolumeValueLabel != null) masterVolumeValueLabel.text = Mathf.RoundToInt(volume * 100f) + "%";

            ApplyResolution();
        }

        private void RefreshLabels()
        {
            if (resolutionLabel != null && _resolutions.Length > 0)
            {
                Resolution r = _resolutions[_resolutionIndex];
                resolutionLabel.text = $"{r.width} x {r.height}";
            }

            if (qualityLabel != null)
            {
                string[] names = QualitySettings.names;
                qualityLabel.text = names.Length > 0 ? names[_qualityIndex] : "-";
            }
        }
    }
}
