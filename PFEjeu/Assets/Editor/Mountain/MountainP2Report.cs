using System.Text;
using Defense.Relief.EditorTools;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Analyse du bord du plateau : pour chaque azimut autour de la herse, pente maximale et denivele
    /// sur la bande qui suit le bord. Lecture seule.
    /// </summary>
    public static class MountainP2Report
    {
        private const float AzimuthStep = 5f;
        private const float RadialStep = 0.5f;
        private const float BandInside = 2f;
        private const float BandOutside = 14f;
        private const float CliffMinSlope = 35f;
        private const float CliffMaxSlope = 50f;
        private const float RampMaxSlope = 12.5f;
        private const float MinDropMeters = 3f;

        public struct Arc
        {
            public float azimuth;
            public float maxSlope;
            public float drop;
            public char kind;
        }

        public static string Build(DefenseReliefConfig config, Terrain terrain, Vector2 herse)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            float edge = config.Inverted.plateauRadius;
            float topY = config.Inverted.valleyY + config.Inverted.plateauRelHeight;

            var strip = new StringBuilder();
            int cliff = 0, tooSteep = 0, ramp = 0, glacis = 0, flush = 0, total = 0;
            float tallest = 0f;

            for (float az = 0f; az < 360f; az += AzimuthStep)
            {
                Arc arc = Measure(data, origin, herse, edge, topY, az);
                total++;
                strip.Append(arc.kind);
                switch (arc.kind)
                {
                    case 'C': cliff++; tallest = Mathf.Max(tallest, arc.drop); break;
                    case '!': tooSteep++; break;
                    case 'R': ramp++; break;
                    case 'g': glacis++; break;
                    default: flush++; break;
                }
            }

            return
                "[Mountain P2] Bord du plateau (azimut 0..355 par pas de " + AzimuthStep + " deg ; 0 = nord, 90 = est)\n" +
                "C=falaise 35-50 deg, !=falaise > 50 deg, R=rampe <= 12.5 deg, g=pente 12.5-35 deg, .=terrain de niveau (< 3 m de denivele)\n" +
                strip + "\n" +
                "Falaise " + Pct(cliff, total) + " | trop raide " + Pct(tooSteep, total) + " | rampe " + Pct(ramp, total) +
                " | intermediaire " + Pct(glacis, total) + " | niveau " + Pct(flush, total) +
                " | falaise la plus haute " + tallest.ToString("F1") + " m";
        }

        private static Arc Measure(TerrainData data, Vector3 origin, Vector2 herse, float edge, float topY, float azimuth)
        {
            var dir = new Vector2(Mathf.Sin(azimuth * Mathf.Deg2Rad), Mathf.Cos(azimuth * Mathf.Deg2Rad));
            float maxSlope = 0f;
            float lowest = float.MaxValue;
            for (float r = edge - BandInside; r <= edge + BandOutside; r += RadialStep)
            {
                float x = herse.x + dir.x * r;
                float z = herse.y + dir.y * r;
                float u = (x - origin.x) / data.size.x;
                float v = (z - origin.z) / data.size.z;
                if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                maxSlope = Mathf.Max(maxSlope, data.GetSteepness(u, v));
                lowest = Mathf.Min(lowest, data.GetInterpolatedHeight(u, v) + origin.y);
            }

            float drop = Mathf.Max(0f, topY - lowest);
            char kind;
            if (drop < MinDropMeters && maxSlope < CliffMinSlope) kind = '.';
            else if (maxSlope > CliffMaxSlope) kind = '!';
            else if (maxSlope >= CliffMinSlope) kind = 'C';
            else if (maxSlope <= RampMaxSlope) kind = 'R';
            else kind = 'g';
            return new Arc { azimuth = azimuth, maxSlope = maxSlope, drop = drop, kind = kind };
        }

        private static string Pct(int count, int total) => (100f * count / total).ToString("F0") + "%";
    }
}
