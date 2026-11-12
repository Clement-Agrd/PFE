using System.Collections.Generic;
using Defense.Relief.EditorTools;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Masque "cote village" : intérieur de l'enclos delimite par la muraille (herse comprise), a l'ouest et
    /// au sud-ouest. Le poids est 0 sur la muraille et du cote defense, puis monte progressivement
    /// jusqu'a 1 en s'eloignant de la muraille vers le village. Rien du cote defense n'est jamais modifie.
    /// </summary>
    public sealed class VillageSide
    {
        private readonly List<Vector2> _wall;
        private readonly List<Vector2> _polygon;
        private readonly float _keep;
        private readonly float _blend;

        /// <param name="keep">Distance (m) a la muraille en deca de laquelle le terrain n'est pas touche.</param>
        /// <param name="blend">Largeur (m) du raccord entre le terrain d'origine et le village.</param>
        public VillageSide(float keep, float blend)
        {
            _keep = keep;
            _blend = blend;
            _wall = DefenseReliefScene.WallAnchors();
            _polygon = new List<Vector2>(_wall);
            if (_wall.Count > 0)
            {
                // Ferme l'enclos vers l'ouest et le sud, bien au-dela de la zone du village.
                Vector2 end = _wall[_wall.Count - 1];
                Vector2 start = _wall[0];
                _polygon.Add(new Vector2(-400f, end.y));
                _polygon.Add(new Vector2(-400f, -400f));
                _polygon.Add(new Vector2(start.x, -400f));
            }
        }

        public bool IsValid => _wall.Count > 2;

        public float Weight(float worldX, float worldZ)
        {
            if (!IsValid || !Contains(worldX, worldZ)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < _wall.Count; i++)
            {
                float dx = _wall[i].x - worldX;
                float dz = _wall[i].y - worldZ;
                float sqr = dx * dx + dz * dz;
                if (sqr < best) best = sqr;
            }

            float k = Mathf.Clamp01((Mathf.Sqrt(best) - _keep) / _blend);
            return k * k * (3f - 2f * k);
        }

        private bool Contains(float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = _polygon.Count - 1; i < _polygon.Count; j = i++)
            {
                Vector2 a = _polygon[i];
                Vector2 b = _polygon[j];
                if ((a.y > z) == (b.y > z)) continue;
                if (x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
}
