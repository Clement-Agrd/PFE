using UnityEngine;

namespace DustVFX
{
    /// <summary>Global dust configuration, loaded from Resources/DustSettings.</summary>
    [CreateAssetMenu(menuName = "Dust VFX/Settings", fileName = "DustSettings")]
    public sealed class DustSettings : ScriptableObject
    {
        [Header("Global")]
        [Tooltip("Global dose of the effect (0 = off, 1 = authored, 2 = double).")]
        [Range(0f, 2f)] public float intensity = 1f;

        public DustQuality quality = DustQuality.High;

        [Tooltip("Hard limit of simultaneously playing steps. The oldest is hurried to a fade-out.")]
        [Range(1, 16)] public int maxActiveSteps = 6;

        [Tooltip("0 = dry, 1 = soaked (rain): scales dust down globally.")]
        [Range(0f, 1f)] public float wetness;

        [Header("Assets")]
        public DustStep stepPrefab;
        public DustSurface defaultSurface;
        public DustSurface[] surfaces;

        [Header("Audio")]
        [Tooltip("Needs the DUSTVFX_SFX scripting define to be compiled in.")]
        public bool sfx = true;

        static DustSettings instance;

        public static DustSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<DustSettings>("DustSettings");
                    if (instance == null)
                    {
                        Debug.LogWarning("[DustVFX] Resources/DustSettings not found - using defaults without prefab. Run Tools/Dust VFX/Rebuild Everything.");
                        instance = CreateInstance<DustSettings>();
                        instance.hideFlags = HideFlags.DontSave;
                    }
                }
                return instance;
            }
        }

        /// <summary>DustSettings.Intensity, 0..2.</summary>
        public static float Intensity
        {
            get => Instance.intensity;
            set => Instance.intensity = Mathf.Clamp(value, 0f, 2f);
        }

        public static DustQuality Quality
        {
            get => Instance.quality;
            set => Instance.quality = value;
        }

#if UNITY_EDITOR
        void OnValidate() => intensity = Mathf.Clamp(intensity, 0f, 2f);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
