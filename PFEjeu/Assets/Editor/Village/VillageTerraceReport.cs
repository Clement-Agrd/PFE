using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Defense.Relief.EditorTools;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Mesure le terrain reel (pas le design) : surface et planeite des etages, angle des murs,
    /// pente des rampes et des escaliers. Lecture seule.
    /// </summary>
    public static class VillageTerraceReport
    {
        private const float SampleStep = 1f;
        /// <summary>Un point compte comme "plat d'etage" si le design vaut l'altitude de l'etage a cette tolerance.</summary>
        private const float DesignEpsilon = 0.01f;
        /// <summary>Pente (deg) au-dessus de laquelle ni le joueur ni le NavMesh ne passent.</summary>
        private const float ImpassableDegrees = 46f;
        private const float WallMinDegrees = 60f;
        /// <summary>Distance (m) des voisins qui doivent aussi etre plats : couvre la maille de heightmap autour du point.</summary>
        private const float NeighbourReach = 1.5f;

        public static string Build(VillageLayoutProfile profile)
        {
            List<Vector2> wallAnchors = DefenseReliefScene.WallAnchors();
            var field = new VillageTerraceField(profile, wallAnchors, VillageHeightSampler.BuildUnderlying());
            Terrain terrain = DefenseReliefScene.Terrain;

            var sb = new StringBuilder("[VillageLayout] Rapport des etages (denivele " + F(profile.FloorStep) + " m)\n");
            AppendFloorsAndWalls(sb, profile, field, terrain);
            foreach (VillageRibbon ramp in field.Ramps) AppendRibbon(sb, field, ramp, profile.MaxRampSlopeDegrees);
            foreach (VillageRibbon stair in field.Stairs) AppendRibbon(sb, field, stair, profile.MaxStairSlopeDegrees);
            return sb.ToString();
        }

        #region Mesures

        private static void AppendFloorsAndWalls(StringBuilder sb, VillageLayoutProfile profile, VillageTerraceField field, Terrain terrain)
        {
            int floorCount = profile.UpperFloorLabels.Count + 1;
            var area = new int[floorCount];
            var deviation = new float[floorCount];
            var wallSamples = new int[floorCount];
            var wallSteep = new int[floorCount];
            var wallBlocking = new int[floorCount];
            var wallSum = new float[floorCount];

            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Rect bounds = field.WorldBounds;
            for (float z = bounds.yMin; z <= bounds.yMax; z += SampleStep)
            {
                for (float x = bounds.xMin; x <= bounds.xMax; x += SampleStep)
                {
                    if (field.ZoneWeight(x, z) < 1f) continue;
                    float design = field.TerracedHeight(x, z);
                    // Les rampes et escaliers s'ecartent volontairement du design des etages.
                    if (Mathf.Abs(field.Evaluate(x, z, design) - design) > DesignEpsilon) continue;

                    int floor = FlatFloorIndex(profile, field, x, z, design);
                    if (floor >= 0)
                    {
                        area[floor]++;
                        deviation[floor] = Mathf.Max(deviation[floor], Mathf.Abs(DefenseReliefScene.TerrainY(x, z) - profile.FloorY(floor)));
                        continue;
                    }

                    int wall = WallIndex(profile, design);
                    if (wall < 0) continue;
                    float steepness = data.GetSteepness((x - origin.x) / data.size.x, (z - origin.z) / data.size.z);
                    wallSamples[wall]++;
                    wallSum[wall] += steepness;
                    if (steepness >= WallMinDegrees) wallSteep[wall]++;
                    if (steepness > ImpassableDegrees) wallBlocking[wall]++;
                }
            }

            for (int floor = 0; floor < floorCount; floor++)
            {
                string label = floor == 0 ? "bas" : profile.UpperFloorLabels[floor - 1];
                sb.AppendLine("Etage " + label + " : altitude " + F(profile.FloorY(floor)) + " m, surface plane " + area[floor] +
                              " m2, ecart max " + F(deviation[floor]) + " m (tolerance " + F(profile.FlatnessTolerance) + ") " +
                              Verdict(deviation[floor] <= profile.FlatnessTolerance));
                if (floor == 0 || wallSamples[floor] == 0) continue;
                float count = wallSamples[floor];
                sb.AppendLine("  Mur sous " + label + " : pente moyenne " + F(wallSum[floor] / count) + " deg, " +
                              F(100f * wallSteep[floor] / count) + " % a 60 deg ou plus, " +
                              F(100f * wallBlocking[floor] / count) + " % infranchissable (> 46 deg)");
            }
        }

        /// <summary>Indice de l'etage si le point et ses 8 voisins sont sur son plat, sinon -1.</summary>
        private static int FlatFloorIndex(VillageLayoutProfile profile, VillageTerraceField field, float x, float z, float design)
        {
            for (int floor = 0; floor <= profile.UpperFloorLabels.Count; floor++)
            {
                float y = profile.FloorY(floor);
                if (Mathf.Abs(design - y) > DesignEpsilon) continue;
                bool flat = true;
                for (int dz = -1; dz <= 1 && flat; dz++)
                    for (int dx = -1; dx <= 1 && flat; dx++)
                        flat = IsAt(field, x + dx * NeighbourReach, z + dz * NeighbourReach, y);
                return flat ? floor : -1;
            }
            return -1;
        }

        private static bool IsAt(VillageTerraceField field, float x, float z, float y)
        {
            float design = field.TerracedHeight(x, z);
            return Mathf.Abs(design - y) <= DesignEpsilon && Mathf.Abs(field.Evaluate(x, z, design) - design) <= DesignEpsilon;
        }

        /// <summary>Indice de l'etage dont ce point est le mur d'acces (tiers central de la hauteur), sinon -1.</summary>
        private static int WallIndex(VillageLayoutProfile profile, float design)
        {
            for (int floor = 1; floor <= profile.UpperFloorLabels.Count; floor++)
            {
                float lower = profile.FloorY(floor - 1);
                float third = profile.FloorStep / 3f;
                if (design > lower + third && design < lower + 2f * third) return floor;
            }
            return -1;
        }

        private static void AppendRibbon(StringBuilder sb, VillageTerraceField field, VillageRibbon ribbon, float limitDegrees)
        {
            float maxSlope = 0f;
            float length = 0f;
            for (int i = 0; i < ribbon.PointCount - 1; i++)
            {
                Vector2 a = ribbon.Point(i);
                Vector2 b = ribbon.Point(i + 1);
                float segment = Vector2.Distance(a, b);
                if (segment < 0.01f) continue;
                length += segment;
                int steps = Mathf.Max(1, Mathf.RoundToInt(segment / SampleStep));
                float previous = HeightAt(field, a);
                for (int k = 1; k <= steps; k++)
                {
                    float y = HeightAt(field, Vector2.Lerp(a, b, k / (float)steps));
                    maxSlope = Mathf.Max(maxSlope, Mathf.Atan(Mathf.Abs(y - previous) / (segment / steps)) * Mathf.Rad2Deg);
                    previous = y;
                }
            }
            float rise = Mathf.Abs(HeightAt(field, ribbon.Point(ribbon.PointCount - 1)) - HeightAt(field, ribbon.Point(0)));
            sb.AppendLine("  " + ribbon.Label + " : longueur " + F(length) + " m, denivele " + F(rise) + " m, pente max " +
                          F(maxSlope) + " deg (limite " + F(limitDegrees) + ") " + Verdict(maxSlope <= limitDegrees));
        }

        #endregion

        #region Helpers

        private static float HeightAt(VillageTerraceField field, Vector2 local)
        {
            Vector2 w = field.Frame.ToWorld(local.x, local.y);
            return DefenseReliefScene.TerrainY(w.x, w.y);
        }

        private static string Verdict(bool ok) => ok ? "OK" : "ECHEC";

        private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

        #endregion
    }
}
