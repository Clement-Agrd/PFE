using UnityEngine;

namespace SlashVFX
{
    /// <summary>One hit of the combo: prefab, timing and feel.</summary>
    [CreateAssetMenu(menuName = "Slash VFX/Combo Step", fileName = "SlashComboStep")]
    public sealed class SlashComboStep : ScriptableObject
    {
        [Header("Visual")]
        public SlashInstance prefab;
        [Tooltip("Slash colour ramp (256x1). Overridden by the controller style when set.")]
        public Texture2D ramp;
        [Tooltip("Slash mask texture (streaks / thick / torn).")]
        public Texture2D mask;
        [Tooltip("Spark and flash colour.")]
        [ColorUsage(false)] public Color flashColor = new Color(0.45f, 0.85f, 1f);

        [Header("Timing")]
        [Min(0.05f)] public float duration = 0.18f;
        [Tooltip("Sweep progress over time (ease-out by default).")]
        public AnimationCurve sweepCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 3.2f), new Keyframe(1f, 1f, 0.1f, 0f));
        [Tooltip("Impact moment as a fraction of the duration.")]
        [Range(0.1f, 0.9f)] public float impactFraction = 0.35f;

        [Header("Shape")]
        [Range(0.2f, 12f)] public float intensity = 5f;
        [Range(0.3f, 3f)] public float scale = 1f;
        [Tooltip("Return stroke: the sweep runs the other way.")]
        public bool mirror;
        [Tooltip("Roll around the strike axis (diagonal rising slash).")]
        [Range(-90f, 90f)] public float tiltDegrees;
        [Tooltip("Pitch of the swing plane (negative raises the far side so the arc faces a high camera).")]
        [Range(-60f, 60f)] public float pitchDegrees = -18f;
        [Tooltip("World Y offset applied to the origin (the pitched arc rises, this lowers it back onto the target).")]
        [Range(-2f, 1f)] public float yOffset = -1f;
        [Tooltip("Random extra roll, degrees.")]
        [Range(0f, 30f)] public float randomRoll = 4f;

        [Header("Impact")]
        [Min(0f)] public float hitStop = 0.02f;
        [Range(0f, 1f)] public float shake = 0.05f;
        [Range(0, 40)] public int sparkCount = 8;
        [Range(0f, 4f)] public float hitFlashScale = 1f;

        [Header("Finisher extras")]
        [Tooltip("Delay of the second (cross) blade.")]
        [Min(0f)] public float secondBladeDelay = 0.06f;
        public bool shockwave;
        public bool bloomPulse;

        [Header("Audio")]
        public AudioClip sfx;
        [Range(0f, 1f)] public float sfxVolume = 0.7f;
    }
}
