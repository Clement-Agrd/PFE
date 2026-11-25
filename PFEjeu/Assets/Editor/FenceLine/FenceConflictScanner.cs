using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Detecte les barrieres qui recoupent une lane ennemie (spline) ou un slot de tour. Construit une fois par generation.</summary>
    internal sealed class FenceConflictScanner
    {
        #region Constants
        private const float LaneDistance = 1f;
        private const float SlotDistance = 2.2f;
        private const float SplineSampleStep = 0.002f;
        private const string IgnoredSplineContainer = "zone village";

        private readonly struct LanePoint
        {
            public readonly Vector2 Xz; public readonly string Name;
            public LanePoint(Vector3 world, string name) { Xz = new Vector2(world.x, world.z); Name = name; }
        }
        #endregion

        private readonly List<LanePoint> _lanes = new List<LanePoint>(2048);
        private readonly DefenseBuildSlot[] _slots;

        internal FenceConflictScanner()
        {
            foreach (var container in Object.FindObjectsByType<SplineContainer>())
            {
                if (container.name == IgnoredSplineContainer) continue;
                foreach (var spline in container.Splines)
                {
                    if (spline.Count < 2) continue;
                    for (float t = 0f; t <= 1.0001f; t += SplineSampleStep)
                        _lanes.Add(new LanePoint(container.transform.TransformPoint(SplineUtility.EvaluatePosition(spline, t)), container.name));
                }
            }
            _slots = Object.FindObjectsByType<DefenseBuildSlot>();
        }

        /// <summary>True si la position est en conflit ; <paramref name="onLane"/> distingue lane et slot.</summary>
        internal bool Check(Vector3 position, out bool onLane, out string description)
        {
            var xz = new Vector2(position.x, position.z);
            float best = float.MaxValue;
            string laneName = null;
            for (int i = 0; i < _lanes.Count; i++)
            {
                float d = (_lanes[i].Xz - xz).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                laneName = _lanes[i].Name;
            }
            float laneDist = Mathf.Sqrt(best);
            onLane = laneDist < LaneDistance;
            if (onLane) { description = $"la spline '{laneName}' ({laneDist:F1} m)"; return true; }

            best = float.MaxValue;
            DefenseBuildSlot nearest = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                Vector3 s = _slots[i].transform.position;
                float d = (new Vector2(s.x, s.z) - xz).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = _slots[i];
            }
            float slotDist = Mathf.Sqrt(best);
            bool hit = nearest != null && slotDist < SlotDistance;
            description = hit ? $"le slot '{nearest.name}' ({slotDist:F1} m)" : null;
            return hit;
        }
    }
}
