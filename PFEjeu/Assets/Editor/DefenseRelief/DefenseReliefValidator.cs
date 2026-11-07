using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Controle du resultat : pentes des splines, Y des points, fins de route,
    /// slots / torches, connexion NavMesh. Ecrit un rapport texte a la racine du projet.
    /// </summary>
    public static class DefenseReliefValidator
    {
        private const float SoftMaxSlopeDegrees = 12f;
        private const float CurrentMaxSlopeDegrees = 15f;
        private const float MaxEndDistanceToHerse = 10f;
        private const float SnapTolerance = 1.5f;
        private const float WindowMeters = 1f;
        private const float OnGroundTolerance = 0.06f;
        private const string ReportFile = "DefenseRelief_ValidationReport.md";

        [MenuItem("Tools/Defense/Relief/6 - Valider")]
        public static void Validate()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            var report = new StringBuilder("# Validation du relief\n\n");
            int failures = 0;

            failures += CheckSplines(config, report);
            failures += CheckObjects(config, report);
            failures += CheckGlobal(config, report);
            failures += CheckGroundedObjects(report);
            failures += CheckNavMesh(config, report);
            if (config.IsInverted) failures += DefenseReliefInvertedReport.Append(config, report);

            report.AppendLine().AppendLine(failures == 0 ? "**Resultat : OK**" : "**Resultat : " + failures + " echec(s)**");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportFile), report.ToString());
            Debug.Log("[DefenseRelief] Validation terminee, echecs = " + failures + " (" + ReportFile + ").");
        }

        #region Splines

        private static int CheckSplines(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            Vector3 herse = DefenseReliefScene.Require(DefenseReliefScene.HerseName).transform.position;
            report.AppendLine("## Splines (pente echantillonnee tous les 1 m)\n");
            report.AppendLine("| Route | Longueur 3D (m) | Pente max (deg) | Pente moy. des segments > 5 deg | Ecart max au terrain (m) | Fin -> herse (m) |");
            report.AppendLine("|---|---|---|---|---|---|");

            foreach (DefenseReliefConfig.RouteSettings route in config.Routes)
            {
                SplineContainer container = DefenseReliefScene.FindRouteSpline(route.splineName);
                const int steps = 2000;
                var pts = new List<Vector3>(steps + 1);
                for (int i = 0; i <= steps; i++) pts.Add((Vector3)container.EvaluatePosition(i / (float)steps));

                float length = 0f, maxSlope = 0f, maxGap = 0f, slopeSum = 0f;
                int slopeCount = 0;
                Vector3 anchor = pts[0];
                for (int i = 1; i < pts.Count; i++)
                {
                    length += Vector3.Distance(pts[i - 1], pts[i]);
                    maxGap = Mathf.Max(maxGap, Mathf.Abs(pts[i].y - DefenseReliefScene.TerrainY(pts[i].x, pts[i].z)));
                    float planar = Vector2.Distance(new Vector2(anchor.x, anchor.z), new Vector2(pts[i].x, pts[i].z));
                    if (planar < WindowMeters) continue;
                    float slope = Mathf.Atan2(Mathf.Abs(pts[i].y - anchor.y), planar) * Mathf.Rad2Deg;
                    maxSlope = Mathf.Max(maxSlope, slope);
                    if (slope > 5f) { slopeSum += slope; slopeCount++; }
                    anchor = pts[i];
                }

                float endDist = Vector3.Distance(new Vector3(pts[pts.Count - 1].x, 0f, pts[pts.Count - 1].z), new Vector3(herse.x, 0f, herse.z));
                float meanSlope = slopeCount > 0 ? slopeSum / slopeCount : 0f;
                bool ok = maxSlope <= (config.ActiveProfile.sCurve ? SoftMaxSlopeDegrees : CurrentMaxSlopeDegrees) && endDist <= MaxEndDistanceToHerse && maxGap <= SnapTolerance;
                if (!ok) failures++;
                report.AppendLine($"| {route.splineName} {(ok ? "" : "(ECHEC)")} | {length:F1} | {maxSlope:F1} | {meanSlope:F1} | {maxGap:F2} | {endDist:F1} |");
            }
            return failures;
        }

        #endregion

        #region Carte globale, hauteur continue, objets au sol

        private const float GlobalMaxPercentOver20 = 2f;
        private const float BoundaryStepTolerance = 0.2f;
        private const float BuriedTolerance = 0.05f;
        private const float FloatingTolerance = 0.3f;

        private static int CheckGlobal(DefenseReliefConfig config, StringBuilder report)
        {
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            Vector3 o = terrain.transform.position;
            ReliefField field = DefenseReliefScene.BuildField(config);
            Rect r = field.Bounds;

            int n = 0, over15 = 0, over20 = 0, over30 = 0, nBase = 0, base20 = 0;
            TerrainData baseData = config.BaseTerrainData;
            float max = 0f;
            Vector2 maxAt = Vector2.zero;
            for (float z = r.yMin; z <= r.yMax; z += 1f)
            {
                for (float x = r.xMin; x <= r.xMax; x += 1f)
                {
                    float u = (x - o.x) / data.size.x;
                    float v = (z - o.z) / data.size.z;
                    float curY = data.GetInterpolatedHeight(u, v);
                    float baseYAt = baseData.GetInterpolatedHeight(u, v);
                    if (Mathf.Abs(curY - baseYAt) <= 0.05f) continue; // seule la surface reellement modifiee compte
                    Vector3 normal = data.GetInterpolatedNormal(u, v);
                    float baseAngle = Vector3.Angle(baseData.GetInterpolatedNormal(u, v), Vector3.up);
                    if (baseAngle > 20f) base20++;
                    nBase++;
                    float angle = Vector3.Angle(normal, Vector3.up);
                    n++;
                    // Les pentes deja presentes dans le terrain d'origine ne sont pas imputees au sculpt.
                    if (angle > 15f && baseAngle <= 15f) over15++;
                    if (angle > 20f && baseAngle <= 20f) over20++;
                    if (angle > 30f && baseAngle <= 30f) over30++;
                    if (angle > max) { max = angle; maxAt = new Vector2(x, z); }
                }
            }

            // Marche a la frontiere de la zone : ecart entre terrain courant et d'origine, sur le bord du rectangle.
            ReliefFieldBase baseField = ReliefFieldBase.Create(config);
            float step = 0f;
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                step = Mathf.Max(step, Edge(baseField, r.xMin + t * r.width, r.yMin), Edge(baseField, r.xMin + t * r.width, r.yMax),
                                 Edge(baseField, r.xMin, r.yMin + t * r.height), Edge(baseField, r.xMax, r.yMin + t * r.height));
            }

            float p20 = 100f * over20 / n;
            if (n == 0) n = 1;
            bool ok = p20 < GlobalMaxPercentOver20 && over30 == 0 && step <= BoundaryStepTolerance;
            report.AppendLine("\n## Carte de pentes globale (zone sculptee + marge)\n");
            report.AppendLine($"- Surface modifiee : {n} m2 ; part > 20 deg dans le terrain d'origine sur ces memes cellules : {100f * base20 / Mathf.Max(1, nBase):F1} %");
            report.AppendLine($"- Nouvelle surface > 15 deg : {100f * over15 / n:F1} %, > 20 deg : {p20:F1} %, > 30 deg : {100f * over30 / n:F1} %");
            report.AppendLine($"- Pente max : {max:F1} deg en {maxAt}");
            report.AppendLine($"- Marche max a la frontiere (courant vs origine) : {step:F2} m");
            if (!ok) report.AppendLine("- **ECHEC global** (criteres : < 2 % > 20 deg, 0 % > 30 deg, marche <= 0,2 m)");

            int holes = CountHoles(data);
            report.AppendLine($"- Cellules de trou (Terrain Holes) : {holes}");
            return (ok ? 0 : 1) + (holes == 0 ? 0 : 1);
        }

        private static float Edge(ReliefFieldBase baseField, float x, float z)
        {
            float current = DefenseReliefScene.TerrainY(x, z);
            return Mathf.Abs(current - baseField.BaseHeight(x, z));
        }

        private static int CountHoles(TerrainData data)
        {
            int res = data.holesResolution;
            bool[,] holes = data.GetHoles(0, 0, res, res);
            int count = 0;
            foreach (bool solid in holes)
            {
                if (!solid) count++;
            }
            return count;
        }

        private static int CheckGroundedObjects(StringBuilder report)
        {
            var host = GameObject.Find("GroundSnapRegistry");
            report.AppendLine("\n## Objets enregistres (enterres > 0,05 m / flottants > 0,3 m)\n");
            if (host == null) { report.AppendLine("- Registry absent."); return 1; }

            Terrain terrain = DefenseReliefScene.Terrain;
            int failures = 0;
            foreach (Defense.Relief.GroundSnapRegistry.Entry entry in host.GetComponent<Defense.Relief.GroundSnapRegistry>().Entries)
            {
                if (entry.Target == null) continue;
                float diff = entry.Target.position.y - (DefenseReliefScene.TerrainY(entry.Target.position.x, entry.Target.position.z) + entry.Offset);
                bool ok = diff <= FloatingTolerance && diff >= -BuriedTolerance;
                if (!ok) failures++;
                report.AppendLine($"- {entry.Target.name} : ecart a l'offset {diff:F2} m {(ok ? "OK" : "ECHEC")}");
            }
            return failures;
        }

        #endregion

        #region Objets

        private static int CheckObjects(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            int slotsOff = 0, torchesOff = 0;

            foreach (Transform slot in DefenseReliefScene.ExistingSlots())
            {
                float gap = slot.position.y - DefenseReliefScene.TerrainY(slot.position.x, slot.position.z);
                if (Mathf.Abs(gap) > OnGroundTolerance) slotsOff++;
            }
            foreach (Transform torch in DefenseReliefScene.Torches())
            {
                if (!config.TryGetOffset(DefenseReliefScene.HierarchyPath(torch), out float offset)) { torchesOff++; continue; }
                float gap = torch.position.y - DefenseReliefScene.TerrainY(torch.position.x, torch.position.z) - offset;
                if (Mathf.Abs(gap) > OnGroundTolerance) torchesOff++;
            }

            failures += slotsOff + torchesOff;
            report.AppendLine("\n## Objets\n");
            report.AppendLine($"- Slots hors sol (> {OnGroundTolerance} m) : {slotsOff}");
            report.AppendLine($"- Torches enterrees ou flottantes : {torchesOff}");
            return failures;
        }

        #endregion

        #region NavMesh

        private static int CheckNavMesh(DefenseReliefConfig config, StringBuilder report)
        {
            int failures = 0;
            report.AppendLine("\n## NavMesh (chemin complet spawn -> herse)\n");
            Vector3 herse = DefenseReliefScene.Require(DefenseReliefScene.HerseName).transform.position;

            foreach (Transform spawn in DefenseReliefScene.SpawnZones())
            {
                failures += ReportPath(report, spawn.name, spawn.position, herse);
            }

            Transform barracksSpawn = DefenseReliefScene.Require(DefenseReliefScene.BarracksName).transform.Find("SpawnPoint");
            if (barracksSpawn != null)
            {
                DefenseReliefConfig.RouteSettings first = config.Routes[0];
                Vector2 lanePoint = first.centerline[first.centerline.Length * 3 / 4];
                var laneWorld = new Vector3(lanePoint.x, DefenseReliefScene.TerrainY(lanePoint.x, lanePoint.y), lanePoint.y);
                failures += ReportPath(report, "Caserne -> lane R01", barracksSpawn.position, laneWorld);
            }
            return failures;
        }

        private static int ReportPath(StringBuilder report, string label, Vector3 from, Vector3 to)
        {
            bool a = NavMesh.SamplePosition(from, out NavMeshHit hitA, 6f, NavMesh.AllAreas);
            bool b = NavMesh.SamplePosition(to, out NavMeshHit hitB, 6f, NavMesh.AllAreas);
            var path = new NavMeshPath();
            bool ok = a && b && NavMesh.CalculatePath(hitA.position, hitB.position, NavMesh.AllAreas, path)
                      && path.status == NavMeshPathStatus.PathComplete;
            report.AppendLine($"- {label} : {(ok ? "OK" : "ECHEC (" + (a && b ? path.status.ToString() : "hors NavMesh") + ")")}");
            return ok ? 0 : 1;
        }

        #endregion
    }
}
