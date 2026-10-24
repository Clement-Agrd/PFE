using UnityEngine;

namespace SlashVFX
{
    /// <summary>Global slash configuration, loaded from Resources/SlashSettings.</summary>
    [CreateAssetMenu(menuName = "Slash VFX/Settings", fileName = "SlashSettings")]
    public sealed class SlashSettings : ScriptableObject
    {
        [Header("Quality")]
        [Tooltip("Low removes streaks, afterimage and trails and halves the sparks.")]
        public SlashQuality quality = SlashQuality.High;

        [Header("Style ramps (Ice, Void, Fire, Holy)")]
        public Texture2D rampIce;
        public Texture2D rampVoid;
        public Texture2D rampFire;
        public Texture2D rampHoly;

        static SlashSettings instance;

        public static SlashSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<SlashSettings>("SlashSettings");
                    if (instance == null)
                    {
                        instance = CreateInstance<SlashSettings>();
                        instance.hideFlags = HideFlags.DontSave;
                    }
                }
                return instance;
            }
        }

        public static SlashQuality Quality
        {
            get => Instance.quality;
            set => Instance.quality = value;
        }

        public Texture2D RampFor(SlashStyle style)
        {
            switch (style)
            {
                case SlashStyle.Ice: return rampIce;
                case SlashStyle.Void: return rampVoid;
                case SlashStyle.Fire: return rampFire;
                case SlashStyle.Holy: return rampHoly;
                default: return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
