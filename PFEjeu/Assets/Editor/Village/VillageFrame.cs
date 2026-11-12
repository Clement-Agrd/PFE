using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Repere local du village : s le long de l'axe central (depuis la herse), t lateral.
    /// </summary>
    public readonly struct VillageFrame
    {
        public readonly Vector2 Origin;
        public readonly Vector2 Axis;
        public readonly Vector2 Lateral;

        public VillageFrame(Vector2 origin, Vector2 target)
        {
            Origin = origin;
            Axis = (target - origin).normalized;
            Lateral = new Vector2(-Axis.y, Axis.x);
        }

        /// <summary>XZ monde -> (s, t).</summary>
        public Vector2 ToLocal(float worldX, float worldZ)
        {
            var d = new Vector2(worldX - Origin.x, worldZ - Origin.y);
            return new Vector2(Vector2.Dot(d, Axis), Vector2.Dot(d, Lateral));
        }

        /// <summary>(s, t) -> XZ monde.</summary>
        public Vector2 ToWorld(float s, float t) => Origin + Axis * s + Lateral * t;
    }
}
