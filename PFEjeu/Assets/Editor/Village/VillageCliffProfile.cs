using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Geologie d'un mur entre deux etages, lue en coupe : de haut en bas une crete erodee (arrondie),
    /// une paroi rocheuse raide, puis un cone d'eboulis a l'angle de repos. La part d'eboulis varie le long
    /// du mur, et le trace du mur ondule (banc de roche plus ou moins resistant). Bruit deterministe.
    /// </summary>
    public sealed class VillageCliffProfile
    {
        private const float NoiseCenter = 0.5f;

        private readonly VillageLayoutProfile _profile;
        private readonly float _step;
        private readonly float _faceTan;
        private readonly float _talusTan;

        public VillageCliffProfile(VillageLayoutProfile profile)
        {
            _profile = profile;
            _step = profile.FloorStep;
            _faceTan = Mathf.Tan(profile.CliffFaceAngleDegrees * Mathf.Deg2Rad);
            _talusTan = Mathf.Tan(profile.TalusAngleDegrees * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Ondulation (m d'altitude naturelle) ajoutee au relief avant de tracer les murs :
        /// une grande longueur d'onde pour les promontoires et rentrants, une petite pour le detail.
        /// </summary>
        public float ContourWarp(Vector2 world)
        {
            Vector2 seed = _profile.NoiseOffset;
            float broad = Noise(world, _profile.ContourNoiseWavelength, seed) - NoiseCenter;
            float detail = Noise(world, _profile.ContourDetailWavelength, seed * 1.7f) - NoiseCenter;
            return 2f * (broad * _profile.ContourNoiseAmplitude + detail * _profile.ContourDetailAmplitude);
        }

        /// <summary>
        /// Hauteur ajoutee par un mur (0 = etage du bas, FloorStep = etage du haut).
        /// signedDistance : distance horizontale a la crete, positive vers l'etage du haut.
        /// </summary>
        public float AddedHeight(float signedDistance, Vector2 world)
        {
            float crestY = _step - _profile.CrestDrop;
            if (signedDistance >= 0f)
            {
                float k = 1f - Mathf.Clamp01(signedDistance / _profile.CrestWidth);
                return _step - _profile.CrestDrop * k * k;
            }

            float share = Mathf.Lerp(
                _profile.TalusShareMin, _profile.TalusShareMax,
                Noise(world, _profile.TalusNoiseWavelength, _profile.NoiseOffset * 2.3f));
            float talusHeight = _step * share;
            float faceRun = (crestY - talusHeight) / _faceTan;
            float below = -signedDistance;
            if (below <= faceRun) return Mathf.Lerp(crestY, talusHeight, below / faceRun);

            float talusRun = talusHeight / _talusTan;
            float along = (below - faceRun) / talusRun;
            if (along >= 1f) return 0f;
            // Cone concave : plus raide sous la paroi, il s'etale en pied.
            float remaining = 1f - along;
            return talusHeight * Mathf.Pow(remaining, _profile.TalusConcavity);
        }

        private static float Noise(Vector2 world, float wavelength, Vector2 offset) =>
            Mathf.PerlinNoise(world.x / wavelength + offset.x, world.y / wavelength + offset.y);
    }
}
