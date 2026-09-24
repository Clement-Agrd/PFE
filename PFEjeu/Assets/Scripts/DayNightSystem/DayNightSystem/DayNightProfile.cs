using UnityEngine;

namespace Core.DayNightSystem
{
    /// <summary>
    /// Aspect visuel d'un cycle jour/nuit : évolution de la couleur et de
    /// l'intensité du soleil, et de la lumière ambiante, sur la journée (0 → 1).
    /// Créé et réglé par un designer (gradients + courbe), sans code.
    /// Création : Assets > Create > Environment > Day Night Profile
    /// </summary>
    [CreateAssetMenu(fileName = "DayNightProfile", menuName = "Environment/Day Night Profile")]
    public sealed class DayNightProfile : ScriptableObject
    {
        [Tooltip("Couleur du soleil selon l'heure (0 = minuit, 0.5 = midi, 1 = minuit).")]
        [SerializeField] private Gradient sunColor;

        [Tooltip("Intensité du soleil selon l'heure (0 → 1 sur la journée).")]
        [SerializeField] private AnimationCurve sunIntensity = AnimationCurve.EaseInOut(0f, 0f, 0.5f, 1f);

        [Tooltip("Couleur de la lumière ambiante selon l'heure.")]
        [SerializeField] private Gradient ambientColor;

        [SerializeField] private bool driveAmbient = true;

        public bool DriveAmbient => driveAmbient;

        public Color EvaluateSunColor(float t) => sunColor.Evaluate(t);
        public float EvaluateSunIntensity(float t) => sunIntensity.Evaluate(t);
        public Color EvaluateAmbient(float t) => ambientColor.Evaluate(t);
    }
}
