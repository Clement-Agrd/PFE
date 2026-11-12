using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Ruban de circulation (volee d'escalier, chemin) : polyligne dans le repere (s, t) avec une altitude
    /// par sommet. Dans la demi-largeur le terrain prend l'altitude du ruban ; au-dela, des joues
    /// inclinees bornent le terrain (entaille dans un etage, ou langue de remblai).
    /// </summary>
    public sealed class VillageRibbon
    {
        public string Label { get; }
        public float HalfWidth { get; }

        private readonly Vector2[] _points;
        private readonly float[] _heights;
        private readonly float _sideTan;

        public VillageRibbon(string label, Vector2[] localPoints, float[] heights, float halfWidth, float sideAngleDegrees)
        {
            if (localPoints.Length < 2 || localPoints.Length != heights.Length)
                throw new System.ArgumentException("Un ruban demande au moins 2 points et autant d'altitudes.");

            Label = label;
            _points = localPoints;
            _heights = heights;
            HalfWidth = halfWidth;
            _sideTan = Mathf.Tan(sideAngleDegrees * Mathf.Deg2Rad);
        }

        public int PointCount => _points.Length;
        public Vector2 Point(int index) => _points[index];
        public float Height(int index) => _heights[index];

        /// <summary>Retourne la hauteur du terrain une fois le ruban applique.</summary>
        public float Apply(float s, float t, float currentHeight)
        {
            var p = new Vector2(s, t);
            float bestSqr = float.MaxValue;
            float bestY = 0f;
            for (int i = 0; i < _points.Length - 1; i++)
            {
                Vector2 a = _points[i];
                Vector2 ab = _points[i + 1] - a;
                float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float sqr = (p - (a + ab * k)).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                bestY = Mathf.Lerp(_heights[i], _heights[i + 1], k);
            }

            float outside = Mathf.Max(0f, Mathf.Sqrt(bestSqr) - HalfWidth);
            float reach = _sideTan * outside;
            return Mathf.Clamp(currentHeight, bestY - reach, bestY + reach);
        }
    }
}
