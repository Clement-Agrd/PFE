using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Une barriere planifiee sur un segment (avant instanciation).</summary>
    internal readonly struct PiecePlan
    {
        public readonly Vector3 LinePoint;   // sur la ligne (Y = celui du point de depart)
        public readonly Vector3 Direction;   // horizontale, normalisee
        public readonly float Length;        // longueur reelle (echelle et jitter inclus)
        public readonly int Variant;
        public readonly float Scale;         // echelle totale a appliquer

        public PiecePlan(Vector3 linePoint, Vector3 direction, float length, int variant, float scale)
        {
            LinePoint = linePoint; Direction = direction; Length = length; Variant = variant; Scale = scale;
        }
    }

    /// <summary>
    /// Calcul pur (sans scene) de la repartition des barrieres sur un segment.
    /// Aux sommets interieurs le segment est prolonge d'une demi-epaisseur : la derniere barriere d'un
    /// segment et la premiere du suivant se recouvrent et bouchent l'angle sans piece dediee.
    /// Les barrieres sont tirees tant qu'elles tiennent entiere ; le reste est reparti en petits ecarts
    /// (si faibles) sinon une barriere de plus est ajoutee et le tout se recouvre uniformement.
    /// </summary>
    internal static class FenceLayout
    {
        private const float MaxStretchGap = 0.35f;
        private const float Epsilon = 1e-4f;
        private const float MinSegmentLength = 0.01f;

        internal static void PlanSegment(Vector3 a, Vector3 b, bool startInterior, bool endInterior,
                                         IReadOnlyList<FenceVariantInfo> variants, FenceGroup group,
                                         System.Random rng, List<PiecePlan> output)
        {
            Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float length = flat.magnitude;
            if (length < MinSegmentLength) return;
            Vector3 dir = flat / length;

            float baseScale = group.PrefabScale;
            float ext = MaxWidth(variants) * baseScale * 0.5f;
            float spanStart = startInterior ? -ext : 0f;
            float spanEnd = length + (endInterior ? ext : 0f);
            float spanLen = spanEnd - spanStart;

            var picks = new List<(int variant, float scale, float len)>(16);
            float used = 0f;
            int last = -1;
            while (true)
            {
                (int v, float s, float len) = Pick(variants, baseScale, group.ScaleJitter, rng, ref last);
                float need = (picks.Count == 0 ? 0f : group.SegmentGap) + len;
                if (used + need > spanLen + Epsilon) break;
                used += need;
                picks.Add((v, s, len));
            }

            float joint, startOffset = 0f;
            if (picks.Count == 0)
            {
                (int v, float s, float len) = Pick(variants, baseScale, 0f, rng, ref last);
                picks.Add((v, s, len));
                joint = 0f;
                startOffset = (spanLen - len) * 0.5f;
            }
            else
            {
                float rest = spanLen - used;
                int n = picks.Count;
                if (n >= 2 && rest / (n - 1) <= MaxStretchGap) joint = group.SegmentGap + rest / (n - 1);
                else if (n == 1 && rest <= MaxStretchGap) { joint = 0f; startOffset = rest * 0.5f; }
                else
                {
                    int shortest = Shortest(variants);
                    float sLen = variants[shortest].Length * baseScale;
                    picks.Add((shortest, baseScale, sLen));
                    float sum = 0f;
                    for (int i = 0; i < picks.Count; i++) sum += picks[i].len;
                    joint = (spanLen - sum) / (picks.Count - 1);
                }
            }

            float cursor = spanStart + startOffset;
            for (int i = 0; i < picks.Count; i++)
            {
                float center = cursor + picks[i].len * 0.5f;
                output.Add(new PiecePlan(a + dir * center, dir, picks[i].len, picks[i].variant, picks[i].scale));
                cursor += picks[i].len + joint;
            }
        }

        private static (int variant, float scale, float len) Pick(IReadOnlyList<FenceVariantInfo> variants, float baseScale,
                                                                  float scaleJitter, System.Random rng, ref int last)
        {
            int v = PickVariant(variants, rng, last);
            last = v;
            float factor = scaleJitter > 0f ? 1f + ((float)rng.NextDouble() * 2f - 1f) * scaleJitter : 1f;
            float scale = baseScale * factor;
            return (v, scale, variants[v].Length * scale);
        }

        private static int PickVariant(IReadOnlyList<FenceVariantInfo> variants, System.Random rng, int last)
        {
            if (variants.Count == 1) return 0;
            float total = 0f;
            for (int i = 0; i < variants.Count; i++) if (i != last) total += variants[i].Weight;
            float roll = (float)rng.NextDouble() * total;
            for (int i = 0; i < variants.Count; i++)
            {
                if (i == last) continue;
                roll -= variants[i].Weight;
                if (roll <= 0f) return i;
            }
            return last == 0 ? 1 : 0;
        }

        private static float MaxWidth(IReadOnlyList<FenceVariantInfo> variants)
        {
            float w = 0f;
            for (int i = 0; i < variants.Count; i++) w = Mathf.Max(w, variants[i].Width);
            return w;
        }

        private static int Shortest(IReadOnlyList<FenceVariantInfo> variants)
        {
            int best = 0;
            for (int i = 1; i < variants.Count; i++) if (variants[i].Length < variants[best].Length) best = i;
            return best;
        }

        /// <summary>Longueur horizontale du trace (boucle incluse) : sert a l'estimation affichee.</summary>
        internal static float PolylineLength(IReadOnlyList<Vector3> points, bool closed)
        {
            float total = 0f;
            for (int i = 0; i + 1 < points.Count; i++) total += Horizontal(points[i], points[i + 1]);
            if (closed && points.Count > 2) total += Horizontal(points[points.Count - 1], points[0]);
            return total;
        }

        private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;
    }
}
