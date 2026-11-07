using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Controles propres au profil "Doux modifie" : plaine plate apres les spawns, pente croissante
    /// vers la herse, surelevation des plateformes de slots selon la distance a la base.
    /// Appele par DefenseReliefValidator quand le preset actif est SoftInverted.
    /// </summary>
    public static class DefenseReliefInvertedReport
    {
        private const float AfterSpawnLength = 40f;
        private const float AfterSpawnMaxMeanDegrees = 1f;
        private const float LaneMaxDegrees = 12f;
        private const float PlatformEdgeMaxDegrees = 25f;
        private const float SliceLength = 20f;
        private const float RaiseTolerance = 0.15f;

        public static int Append(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            failures += AfterSpawn(config, report);
            failures += SlopeSlices(config, report);
            failures += SlotPlatforms(config, report);
            failures += Misc(config, report);
            return failures;
        }

        /// <summary>Points XZ de la lane, un par metre depuis le spawn jusqu'a la fin du spline.</summary>
        private static List<Vector2> Resample(Vector2[] line)
        {
            var result = new List<Vector2> { line[0] };
            float carried = 0f;
            for (int i = 1; i < line.Length; i++)
            {
                Vector2 a = line[i - 1], b = line[i];
                float seg = Vector2.Distance(a, b);
                float cursor = 1f - carried;
                while (cursor <= seg)
                {
                    result.Add(Vector2.Lerp(a, b, cursor / seg));
                    cursor += 1f;
                }
                carried = seg - (cursor - 1f);
            }
            return result;
        }

        private static float[] Heights(List<Vector2> pts)
        {
            var y = new float[pts.Count];
            for (int i = 0; i < y.Length; i++) y[i] = DefenseReliefScene.TerrainY(pts[i].x, pts[i].y);
            return y;
        }

        private static float SlopeDeg(float y0, float y1) => Mathf.Atan2(Mathf.Abs(y1 - y0), 1f) * Mathf.Rad2Deg;

        private static int AfterSpawn(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            report.AppendLine("\n## Plaine apres chaque spawn (40 premiers metres de la lane)\n");
            report.AppendLine("| Route | Y spawn | Y a 40 m | Pente moy. (deg) | Pente max (deg) |");
            report.AppendLine("|---|---|---|---|---|");
            foreach (DefenseReliefConfig.RouteSettings route in config.Routes)
            {
                List<Vector2> pts = Resample(route.centerline);
                float[] y = Heights(pts);
                int n = Mathf.Min((int)AfterSpawnLength, y.Length - 1);
                float sum = 0f, max = 0f;
                for (int i = 1; i <= n; i++)
                {
                    float s = SlopeDeg(y[i - 1], y[i]);
                    sum += s;
                    max = Mathf.Max(max, s);
                }
                float mean = sum / Mathf.Max(1, n);
                bool ok = mean <= AfterSpawnMaxMeanDegrees;
                if (!ok) failures++;
                report.AppendLine($"| {route.splineName} {(ok ? "" : "(ECHEC)")} | {y[0]:F2} | {y[n]:F2} | {mean:F2} | {max:F2} |");
            }
            return failures;
        }

        private static int SlopeSlices(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            int routeIndex = config.Routes.Count - 1; // Spline (3) = Spawn_Route_04, la plus longue
            DefenseReliefConfig.RouteSettings route = config.Routes[routeIndex];
            List<Vector2> pts = Resample(route.centerline);
            float[] y = Heights(pts);
            int last = y.Length - 1;

            float laneMax = 0f;
            foreach (DefenseReliefConfig.RouteSettings r in config.Routes)
            {
                float[] ry = Heights(Resample(r.centerline));
                for (int i = 1; i < ry.Length; i++) laneMax = Mathf.Max(laneMax, SlopeDeg(ry[i - 1], ry[i]));
            }
            if (laneMax > LaneMaxDegrees + 0.5f) failures++;

            report.AppendLine($"\n## Pente par tranche de 20 m, {route.splineName} (route 04), d = distance a la fin du spline cote herse\n");
            report.AppendLine("| d (m) | Y debut (spawn) -> fin (herse) | Pente moy. (deg) | Pente moy. hors plat (deg) | Pente max (deg) |");
            report.AppendLine("|---|---|---|---|---|");

            var means = new List<float>();
            var slopedMeans = new List<float>();
            for (float d1 = last; d1 > 0f; d1 -= SliceLength)
            {
                // d1 : bord cote spawn de la tranche ; d0 : bord cote herse.
                float d0 = Mathf.Max(0f, d1 - SliceLength);
                int i1 = last - Mathf.RoundToInt(d0);
                int i0 = last - Mathf.RoundToInt(d1);
                float sum = 0f, max = 0f, slopedSum = 0f;
                int count = 0, sloped = 0;
                for (int i = i0 + 1; i <= i1; i++)
                {
                    float s = SlopeDeg(y[i - 1], y[i]);
                    sum += s;
                    max = Mathf.Max(max, s);
                    count++;
                    if (s > 1f) { slopedSum += s; sloped++; }
                }
                float mean = count > 0 ? sum / count : 0f;
                float slopedMean = sloped > 0 ? slopedSum / sloped : 0f;
                means.Add(mean);
                slopedMeans.Add(slopedMean);
                report.AppendLine($"| {d0:F0} - {d1:F0} | {y[i0]:F2} -> {y[i1]:F2} | {mean:F1} | {slopedMean:F1} | {max:F1} |");
            }

            // slopedMeans[0] = tranche cote spawn. La pente (hors paliers/plaine, plats par consigne) doit croitre vers la herse.
            // On verifie la croissance jusqu'a la tranche la plus raide ; au-dela, l'adoucissement avant le plateau est voulu.
            int steepest = 0;
            for (int i = 1; i < slopedMeans.Count; i++)
            {
                if (slopedMeans[i] > slopedMeans[steepest]) steepest = i;
            }
            bool monotone = true;
            float previous = -1f;
            for (int i = 0; i <= steepest; i++)
            {
                if (slopedMeans[i] <= 0f) continue;
                if (previous >= 0f && slopedMeans[i] < previous - 1f) monotone = false;
                previous = slopedMeans[i];
            }
            report.AppendLine();
            report.AppendLine($"- Pente max sur les 4 lanes : {laneMax:F1} deg (limite {LaneMaxDegrees:F0}) {(laneMax <= LaneMaxDegrees + 0.5f ? "OK" : "ECHEC")}");
            report.AppendLine($"- Pente croissante vers la herse (pente moyenne hors plat, tolerance 1 deg) : {(monotone ? "OK" : "NON")}");
            return failures;
        }

        private static int SlotPlatforms(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            ReliefField field = DefenseReliefScene.BuildField(config);
            Vector2 herse = DefenseReliefScene.HersePosition;
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            Vector3 o = terrain.transform.position;
            float half = config.SlotPlatformHalfSize + config.Inverted.slotPlatformBlend;

            var rows = new List<(float fromSpawn, string text, bool ok)>();
            foreach (Transform slot in DefenseReliefScene.ExistingSlots())
            {
                var xz = new Vector2(slot.position.x, slot.position.z);
                field.Query(xz, out _, out int index, out _);
                float fromSpawn = field.Samples[index].distanceFromSpawn;
                float toBase = Vector2.Distance(xz, herse);
                float terrainY = DefenseReliefScene.TerrainY(xz.x, xz.y);
                float raise = terrainY - field.SurfaceWithoutPlatforms(xz);

                float maxCut = 0f, maxAngle = 0f;
                for (float dz = -half; dz <= half; dz += 1f)
                {
                    for (float dx = -half; dx <= half; dx += 1f)
                    {
                        float x = xz.x + dx, z = xz.y + dz;
                        float cur = DefenseReliefScene.TerrainY(x, z);
                        maxCut = Mathf.Max(maxCut, field.SurfaceWithoutPlatforms(new Vector2(x, z)) - cur);
                        float u = (x - o.x) / data.size.x, v = (z - o.z) / data.size.z;
                        // Les falaises deja presentes dans le terrain d'origine ne sont pas imputees au sculpt.
                        if (Vector3.Angle(config.BaseTerrainData.GetInterpolatedNormal(u, v), Vector3.up) > PlatformEdgeMaxDegrees) continue;
                        maxAngle = Mathf.Max(maxAngle, Vector3.Angle(data.GetInterpolatedNormal(u, v), Vector3.up));
                    }
                }

                bool active = fromSpawn > config.Inverted.slotNoRaiseDistance;
                // Cible = surelevation voulue x part atteinte compte tenu de la distance du cube au bord de lane.
                float laneShare = Mathf.Clamp01(field.DistanceOutsideLane(xz) / Mathf.Max(0.1f, config.Inverted.slotLaneRamp));
                float expected = laneShare * config.Inverted.slotRaiseNear * ReliefField.Smooth(
                    config.Inverted.slotNoRaiseDistance, config.Inverted.slotNoRaiseDistance + config.Inverted.slotRaiseRamp, fromSpawn);
                bool ok = Mathf.Abs(raise - expected) <= RaiseTolerance + 0.3f * Mathf.Abs(expected)
                          && (!active || (maxCut <= config.Inverted.slotMaxCut + config.Inverted.undulationMax + 0.1f && maxAngle <= PlatformEdgeMaxDegrees));
                if (!active) ok = ok && Mathf.Abs(raise) <= RaiseTolerance;
                if (!ok) failures++;
                rows.Add((fromSpawn, $"| {slot.name} {(ok ? "" : "(ECHEC)")} | {fromSpawn:F0} | {toBase:F0} | {(active ? "oui" : "non (a plat)")} | {raise:F2} | {expected:F2} | {maxCut:F2} | {maxAngle:F1} |", ok));
            }

            rows.Sort((a, b) => b.fromSpawn.CompareTo(a.fromSpawn));
            report.AppendLine("\n## Plateformes de slots : surelevation selon la distance (tri : du plus pres de la base au plus pres des spawns)\n");
            report.AppendLine("| Slot | Dist. depuis spawn (m) | Dist. a la herse (m) | Plateforme | Surelevation mesuree (m) | Cible (m) | Deblai max (m) | Pente max zone (deg) |");
            report.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var row in rows) report.AppendLine(row.text);
            report.AppendLine($"\nCriteres : deblai <= {config.Inverted.slotMaxCut:F1} m (+ ondulation), pente de bord <= {PlatformEdgeMaxDegrees:F0} deg, 0 m (pas de plateforme) a moins de {config.Inverted.slotNoRaiseDistance:F0} m du spawn. La surelevation mesuree inclut l'ondulation (<= {config.Inverted.undulationMax:F1} m).");
            return failures;
        }

        private static int Misc(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            TerrainData data = DefenseReliefScene.Terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] h = data.GetHeights(0, 0, res, res);
            float min = float.MaxValue;
            foreach (float v in h) min = Mathf.Min(min, v);
            float minY = min * data.size.y + DefenseReliefScene.Terrain.transform.position.y;
            if (minY < 0f) failures++;

            report.AppendLine("\n## Divers\n");
            report.AppendLine($"- Altitude minimale du terrain : {minY:F2} m {(minY >= 0f ? "OK" : "ECHEC")}");

            GameObject wall = GameObject.Find("muraille");
            if (wall != null)
            {
                float terrainY = DefenseReliefScene.TerrainY(wall.transform.position.x, wall.transform.position.z);
                report.AppendLine($"- muraille (non modifiee) : Y objet {wall.transform.position.y:F2}, Y terrain sous le pivot {terrainY:F2}, ecart {wall.transform.position.y - terrainY:F2} m");
            }
            return failures;
        }
    }
}
