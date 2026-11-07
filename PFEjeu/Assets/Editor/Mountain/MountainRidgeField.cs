using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Modele des crete lointaines : hauteur cible en un point du monde. Pur calcul, deterministe (graine fixe).
    /// N'agit que dans la zone exterieure ; le terrain existant plus haut est conserve (max adouci).
    /// </summary>
    public sealed class MountainRidgeField
    {
        private const float SmoothMaxMeters = 6f;
        private const float TwoPi = Mathf.PI * 2f;

        private readonly MountainRidgeProfile _profile;
        private readonly Vector2 _herse;
        private readonly Vector3 _origin;
        private readonly Vector3 _size;
        private readonly float _valleyY;
        private readonly float[] _offsets;

        public MountainRidgeField(MountainRidgeProfile profile, Vector2 herse, Vector3 terrainOrigin, Vector3 terrainSize, float valleyY)
        {
            _profile = profile;
            _herse = herse;
            _origin = terrainOrigin;
            _size = terrainSize;
            _valleyY = valleyY;
            _offsets = new float[profile.Ridges.Length * 4];
            for (int i = 0; i < _offsets.Length; i++) _offsets[i] = MountainMath.Hash01(i, i * 13 + 5, profile.Seed) * 400f;
        }

        /// <summary>Hauteur monde apres ajout des cretes : max adouci entre le terrain courant et la crete.</summary>
        public float Apply(float worldX, float worldZ, float currentY)
        {
            currentY = ApplyMassifDetail(worldX, worldZ, currentY);

            float dx = worldX - _herse.x;
            float dz = worldZ - _herse.y;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance < _profile.MinDistanceFromHerse) return currentY;

            float top = RidgeHeight(worldX, worldZ, dx, dz, distance);
            if (top <= 0f) return currentY;

            float gate = MountainMath.Smooth(_profile.MinDistanceFromHerse, _profile.MinDistanceFromHerse + _profile.RampInWidth, distance);
            float border = MountainMath.Smooth(0f, _profile.BorderFadeWidth, DistanceToBorder(worldX, worldZ));
            float target = _valleyY + top * gate * border;

            float hard = Mathf.Max(currentY, target);
            float diff = currentY - target;
            float soft = 0.5f * (currentY + target + Mathf.Sqrt(diff * diff + SmoothMaxMeters * SmoothMaxMeters));
            return Mathf.Lerp(hard, soft, MountainMath.Smooth(0f, 5f, top * gate * border));
        }

        private float RidgeHeight(float x, float z, float dx, float dz, float distance)
        {
            float angle = Mathf.Atan2(dx, dz);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            float azimuth = angle * Mathf.Rad2Deg;
            float boost = 1f + (_profile.SectorBoost - 1f)
                              * (1f - MountainMath.Smooth(0f, _profile.SectorWidth, Mathf.Abs(Mathf.DeltaAngle(azimuth, _profile.SectorAzimuth))));
            float big = Ridged(x, z, _profile.RidgedScaleBig, 31.1f, 7.7f);
            float fine = Ridged(x, z, _profile.RidgedScaleSmall, 53.9f, 91.3f);
            float gullyNoise = Ridged(x, z, _profile.GullyScale, 12.7f, 44.1f);
            // Jamais en dessous de 0,65 : le relief fin sculpte la crete sans la raser.
            float crestDetail = 0.65f + _profile.RidgedStrength * big + 0.5f * _profile.RidgedStrength * fine;

            float best = 0f;
            MountainRidgeProfile.Ridge[] ridges = _profile.Ridges;
            for (int i = 0; i < ridges.Length; i++)
            {
                MountainRidgeProfile.Ridge ridge = ridges[i];
                int o = i * 4;
                // Bruit periodique sur le cercle (cos, sin) : pas de raccord visible a 360 deg.
                float slow = Mathf.PerlinNoise(cos * _profile.AngularFrequency + _offsets[o], sin * _profile.AngularFrequency + _offsets[o + 1]);
                float fast = Mathf.PerlinNoise(cos * _profile.PeakFrequency + _offsets[o + 2], sin * _profile.PeakFrequency + _offsets[o + 3]);

                float radius = ridge.radius + (slow - 0.5f) * 2f * ridge.wobble;
                float u = Mathf.Abs(distance - radius) / ridge.halfWidth;
                if (u >= 1f) continue;

                float profile = Mathf.Pow(1f - u, _profile.CrestSharpness);
                float peaks = Mathf.Lerp(1f - _profile.PeakContrast, 1f + _profile.PeakContrast * 0.5f, fast);

                // Cols : la crete s'abaisse par endroits (anneau ouvert, profondeur entre les lignes).
                float passNoise = Mathf.PerlinNoise(cos * _profile.PassFrequency + _offsets[o + 2] * 0.5f, sin * _profile.PassFrequency + _offsets[o + 3] * 0.5f);
                float pass = Mathf.Lerp(_profile.PassMinHeight, 1f, MountainMath.Smooth(_profile.PassLow, _profile.PassHigh, passNoise));

                // Detail d'aretes surtout pres de la ligne de crete ; ravines surtout sur les flancs.
                float detail = Mathf.Lerp(1f, crestDetail, 1f - u);
                float gully = 1f - _profile.GullyStrength * u * gullyNoise;

                float h = ridge.height * peaks * boost * detail * gully * pass * profile * Mathf.Lerp(0.8f, 1.15f, slow);
                h = Mathf.Min(h, ridge.height * _profile.MaxHeightFactor);
                if (h > best) best = h;
            }
            return best;
        }

        #region Relief des montagnes existantes

        private const float WarpOffsetX = 211.7f;
        private const float WarpOffsetZ = 97.3f;
        private const float StrataPatchScale = 60f;
        private const float StrataPatchLow = 0.4f;
        private const float StrataPatchHigh = 0.6f;
        private const float RidgedBias = 0.4f;

        /// <summary>
        /// Ajoute aux montagnes existantes des eperons, des ravines et des strates, uniquement au-dessus de
        /// MassifStartRel (au-dessus du volume NavMesh) : la zone jouable n'est jamais touchee.
        /// </summary>
        private float ApplyMassifDetail(float x, float z, float y)
        {
            if (!_profile.MassifDetailEnabled) return y;
            float rel = y - _valleyY;
            float weight = MountainMath.Smooth(_profile.MassifStartRel, _profile.MassifFullRel, rel);
            if (weight <= 0f) return y;

            // Deformation du domaine : formes organiques, sans alignement sur la grille.
            float wx = x + (Mathf.PerlinNoise(x / 90f + WarpOffsetX, z / 90f) - 0.5f) * 2f * _profile.MassifWarp;
            float wz = z + (Mathf.PerlinNoise(x / 90f, z / 90f + WarpOffsetZ) - 0.5f) * 2f * _profile.MassifWarp;

            float displacement =
                _profile.MassifBigAmplitude * (Ridged(wx, wz, _profile.MassifBigScale, 5.3f, 61.7f) - RidgedBias) +
                _profile.MassifMidAmplitude * (Ridged(wx, wz, _profile.MassifMidScale, 77.1f, 13.9f) - RidgedBias) +
                _profile.MassifFineAmplitude * (Ridged(wx, wz, _profile.MassifFineScale, 29.5f, 83.3f) - RidgedBias);
            float result = y + displacement * weight;

            return Strata(x, z, result, rel);
        }

        /// <summary>Strates : marches douces tous les StrataHeight m, par plaques, sur les hautes altitudes.</summary>
        private float Strata(float x, float z, float y, float rel)
        {
            float altitude = MountainMath.Smooth(_profile.StrataStartRel, _profile.StrataFullRel, rel);
            float patch = MountainMath.Smooth(StrataPatchLow, StrataPatchHigh, MountainMath.Noise01(x, z, StrataPatchScale, 140.3f, 8.1f));
            float strength = _profile.StrataStrength * altitude * patch;
            if (strength <= 0f) return y;

            float t = y / _profile.StrataHeight;
            float whole = Mathf.Floor(t);
            float terraced = (whole + MountainMath.Smooth(0.25f, 0.75f, t - whole)) * _profile.StrataHeight;
            return Mathf.Lerp(y, terraced, strength);
        }

        #endregion

        /// <summary>Bruit d'aretes : 1 sur les lignes de crete du bruit, 0 dans les creux.</summary>
        private static float Ridged(float x, float z, float scaleMeters, float offsetX, float offsetZ) =>
            1f - Mathf.Abs(2f * MountainMath.Noise01(x, z, scaleMeters, offsetX, offsetZ) - 1f);

        private float DistanceToBorder(float x, float z)
        {
            float west = x - _origin.x;
            float east = _origin.x + _size.x - x;
            float south = z - _origin.z;
            float north = _origin.z + _size.z - z;
            return Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));
        }
    }
}
