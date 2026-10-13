using System.Linq;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Réglages joueur, sauvegardés dans PlayerPrefs et appliqués dès le lancement du jeu.
    /// Les clés vidéo/audio sont celles de l'écran d'options du menu principal (MainMenu.OptionsPanelUI) :
    /// les deux écrans partagent donc les mêmes valeurs.
    /// </summary>
    public static class GameSettings
    {
        private const string PrefResolution = "opt_resolution_index";
        private const string PrefFullscreen = "opt_fullscreen";
        private const string PrefQuality = "opt_quality_index";
        private const string PrefVSync = "opt_vsync";
        private const string PrefMaster = "opt_master_volume";
        private const string PrefFpsCap = "opt_fps_cap";
        private const string PrefSensitivity = "opt_mouse_sensitivity";
        private const string PrefInvertY = "opt_invert_y";
        private const string PrefShowFps = "opt_show_fps";

        public static readonly int[] FpsCaps = { 0, 30, 60, 90, 120, 144, 240 };

        private static Resolution[] _resolutions;

        public static Resolution[] Resolutions
        {
            get
            {
                if (_resolutions != null) return _resolutions;
                _resolutions = Screen.resolutions
                    .Select(r => new Resolution { width = r.width, height = r.height, refreshRateRatio = r.refreshRateRatio })
                    .GroupBy(r => (r.width, r.height))
                    .Select(g => g.First())
                    .OrderBy(r => r.width * r.height)
                    .ToArray();
                if (_resolutions.Length == 0) _resolutions = new[] { Screen.currentResolution };
                return _resolutions;
            }
        }

        public static int ResolutionIndex
        {
            get
            {
                Resolution[] list = Resolutions;
                if (PlayerPrefs.HasKey(PrefResolution))
                {
                    int saved = PlayerPrefs.GetInt(PrefResolution);
                    if (saved >= 0 && saved < list.Length) return saved;
                }
                int current = System.Array.FindIndex(list,
                    r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height);
                return current >= 0 ? current : list.Length - 1;
            }
            set
            {
                Resolution[] list = Resolutions;
                value = Mathf.Clamp(value, 0, list.Length - 1);
                Screen.SetResolution(list[value].width, list[value].height, Screen.fullScreen);
                PlayerPrefs.SetInt(PrefResolution, value);
            }
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            set { Screen.fullScreen = value; PlayerPrefs.SetInt(PrefFullscreen, value ? 1 : 0); }
        }

        public static int Quality
        {
            get => PlayerPrefs.GetInt(PrefQuality, QualitySettings.GetQualityLevel());
            set
            {
                int count = QualitySettings.names.Length;
                if (count == 0) return;
                value = Mathf.Clamp(value, 0, count - 1);
                QualitySettings.SetQualityLevel(value, true);
                PlayerPrefs.SetInt(PrefQuality, value);
            }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(PrefVSync, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            set { QualitySettings.vSyncCount = value ? 1 : 0; PlayerPrefs.SetInt(PrefVSync, value ? 1 : 0); }
        }

        /// <summary>Plafond d'images par seconde (0 = illimité). Sans effet quand la VSync est active.</summary>
        public static int FpsCap
        {
            get => PlayerPrefs.GetInt(PrefFpsCap, 0);
            set { Application.targetFrameRate = value <= 0 ? -1 : value; PlayerPrefs.SetInt(PrefFpsCap, value); }
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(PrefMaster, AudioListener.volume);
            set { value = Mathf.Clamp01(value); AudioListener.volume = value; PlayerPrefs.SetFloat(PrefMaster, value); }
        }

        /// <summary>Multiplicateur de la sensibilité souris de la caméra (1 = réglage de base).</summary>
        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(PrefSensitivity, 1f);
            set => PlayerPrefs.SetFloat(PrefSensitivity, Mathf.Clamp(value, 0.1f, 3f));
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(PrefInvertY, 0) == 1;
            set => PlayerPrefs.SetInt(PrefInvertY, value ? 1 : 0);
        }

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(PrefShowFps, 0) == 1;
            set => PlayerPrefs.SetInt(PrefShowFps, value ? 1 : 0);
        }

        public static void Save() => PlayerPrefs.Save();

        /// <summary>Remet tous les réglages à leurs valeurs par défaut.</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[] { PrefResolution, PrefFullscreen, PrefQuality, PrefVSync, PrefMaster,
                                           PrefFpsCap, PrefSensitivity, PrefInvertY, PrefShowFps })
                PlayerPrefs.DeleteKey(key);

            Fullscreen = true;
            Quality = Mathf.Max(0, QualitySettings.names.Length - 1);
            VSync = true;
            FpsCap = 0;
            MasterVolume = 1f;
            ResolutionIndex = Resolutions.Length - 1;
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySaved()
        {
            _resolutions = null;
            if (PlayerPrefs.HasKey(PrefFullscreen)) Screen.fullScreen = Fullscreen;
            if (PlayerPrefs.HasKey(PrefQuality)) QualitySettings.SetQualityLevel(Quality, true);
            if (PlayerPrefs.HasKey(PrefVSync)) QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (PlayerPrefs.HasKey(PrefMaster)) AudioListener.volume = MasterVolume;
            if (PlayerPrefs.HasKey(PrefFpsCap)) Application.targetFrameRate = FpsCap <= 0 ? -1 : FpsCap;
        }
    }
}
