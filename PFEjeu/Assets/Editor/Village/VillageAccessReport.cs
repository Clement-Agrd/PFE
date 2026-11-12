using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Rapport d'acces : depuis le sol du village, quelles zones sont atteignables a pied (pente sous la limite du
    /// joueur et du NavMesh) SANS emprunter une rampe ou un escalier ? Toute zone atteinte ainsi (etage Milieu, etage
    /// Haut, relief hors village) est une fuite : un bord que l'on peut escalader. Lecture seule.
    /// </summary>
    public static class VillageAccessReport
    {
        private const float WalkableDegrees = 44f;
        private const float RibbonMargin = 2.5f;
        private const float SeedFloorTolerance = 0.2f;
        /// <summary>Un point ne compte comme "sur un etage" que s'il est plat : une pente qui traverse l'altitude de l'etage n'en est pas un.</summary>
        private const float FlatDegrees = 3f;
        /// <summary>Poids de zone minimal : on ne mesure que l'interieur du village, pas le tour des montagnes alentour.</summary>
        private const float InnerZone = 0.5f;

        [MenuItem("Tools/Defense/Relief/Village/Rapport d'acces (bords infranchissables)")]
        public static void Report() => Debug.Log(Build(VillageLayoutProfile.LoadOrCreate()));

        public static string Build(VillageLayoutProfile profile)
        {
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int res = data.heightmapResolution;
            VillageTerraceField field = VillageFlatDesigner.CreateField(profile, terrain, out _);
            var side = new VillageSide(6f, 14f);

            var walkable = new bool[res, res];
            var height = new float[res, res];
            var flatCell = new bool[res, res];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float u = (float)i / (res - 1);
                    float v = (float)j / (res - 1);
                    float x = origin.x + u * data.size.x;
                    float z = origin.z + v * data.size.z;
                    height[j, i] = data.GetHeight(i, j) + origin.y;
                    flatCell[j, i] = data.GetSteepness(u, v) <= FlatDegrees;
                    // Hors enclos (cote defense) : bloque, on ne mesure que ce qui fuit depuis le village.
                    walkable[j, i] = side.Weight(x, z) > 0f && field.ZoneWeight(x, z) >= InnerZone && data.GetSteepness(u, v) <= WalkableDegrees && !OnRibbon(field, x, z);
                }
            }

            if (!FindSeed(profile, terrain, side, height, out int si, out int sj))
                return "[VillageAccess] Aucun point de depart sur l'etage bas.";

            var reached = new bool[res, res];
            var parent = new int[res * res];
            int firstMid = -1;
            var queue = new Queue<int>();
            queue.Enqueue(sj * res + si);
            reached[sj, si] = true;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int ci = c % res;
                int cj = c / res;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= res || nj >= res || reached[nj, ni] || !walkable[nj, ni]) continue;
                    reached[nj, ni] = true;
                    parent[nj * res + ni] = c;
                    if (firstMid < 0 && flatCell[nj, ni] && Mathf.Abs(height[nj, ni] - profile.FloorY(1)) <= SeedFloorTolerance) firstMid = nj * res + ni;
                    queue.Enqueue(nj * res + ni);
                }
            }

            float cellArea = data.size.x / (res - 1) * (data.size.z / (res - 1));
            int floors = profile.UpperFloorLabels.Count + 1;
            var onFloor = new int[floors];
            int total = 0;
            int outside = 0;
            float highest = float.MinValue;
            Vector2 highestAt = Vector2.zero;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    if (!reached[j, i]) continue;
                    total++;
                    float h = height[j, i];
                    for (int f = 1; f < floors; f++)
                        if (flatCell[j, i] && Mathf.Abs(h - profile.FloorY(f)) <= SeedFloorTolerance) onFloor[f]++;
                    float x = origin.x + (float)i / (res - 1) * data.size.x;
                    float z = origin.z + (float)j / (res - 1) * data.size.z;
                    if (h > profile.FloorY(floors - 1) + 1f && h > highest)
                    {
                        highest = h;
                        highestAt = new Vector2(x, z);
                    }

                    if (field.ZoneWeight(x, z) <= 0f) outside++;
                }
            }

            var sb = new StringBuilder("[VillageAccess] Atteignable a pied depuis l'etage bas SANS rampe ni escalier (pente <= " + WalkableDegrees + " deg) :\n");
            sb.Append("  Zone totale : ").Append(F(total * cellArea)).Append(" m2\n");
            for (int f = 1; f < floors; f++)
                sb.Append("  Etage ").Append(profile.UpperFloorLabels[f - 1]).Append(" : ").Append(F(onFloor[f] * cellArea)).Append(" m2 ").Append(onFloor[f] == 0 ? "OK" : "FUITE").Append('\n');
            sb.Append("  Hors zone du village : ").Append(F(outside * cellArea)).Append(" m2 ").Append(outside == 0 ? "OK" : "FUITE").Append('\n');
            if (highest > float.MinValue)
                sb.Append("  Point le plus haut atteint : ").Append(F(highest)).Append(" m en (").Append(F(highestAt.x)).Append(", ").Append(F(highestAt.y)).Append(")\n");
            if (firstMid >= 0) AppendPath(sb, terrain, parent, firstMid, sj * res + si, res, height);
            AppendMap(sb, profile, terrain, reached, walkable, height, flatCell);
            return sb.ToString();
        }

        /// <summary>Chemin le plus court (BFS) du sol vers le premier point de l'etage Milieu : montre la breche.</summary>
        private static void AppendPath(StringBuilder sb, Terrain terrain, int[] parent, int end, int start, int res, float[,] height)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            var cells = new List<int>();
            for (int c = end; c != start; c = parent[c]) cells.Add(c);
            cells.Reverse();
            sb.Append("  Breche vers l'etage Milieu (").Append(cells.Count).Append(" cellules) :\n");
            for (int k = 0; k < cells.Count; k += Mathf.Max(1, cells.Count / 14))
            {
                int i = cells[k] % res;
                int j = cells[k] / res;
                float x = origin.x + (float)i / (res - 1) * data.size.x;
                float z = origin.z + (float)j / (res - 1) * data.size.z;
                sb.Append("    (").Append(F(x)).Append(", ").Append(F(z)).Append(") h=").Append(F(height[j, i]))
                  .Append(" pente=").Append(F(data.GetSteepness((float)i / (res - 1), (float)j / (res - 1)))).Append('\n');
            }
        }

        /// <summary>Carte (pas de 4 m en x, 6 m en z) : B/M/H = atteint sur l'etage bas/Milieu/Haut, + = atteint ailleurs, . = praticable non atteint, # = trop raide.</summary>
        private static void AppendMap(StringBuilder sb, VillageLayoutProfile profile, Terrain terrain, bool[,] reached, bool[,] walkable, float[,] height, bool[,] flatCell)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int res = height.GetLength(0);
            sb.Append("  Carte (x de -110 a 90 par 4 m, z de -20 a -230 par 6 m)\n");
            for (float z = -20f; z >= -230f; z -= 6f)
            {
                for (float x = -110f; x <= 90f; x += 4f)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt((x - origin.x) / data.size.x * (res - 1)), 0, res - 1);
                    int j = Mathf.Clamp(Mathf.RoundToInt((z - origin.z) / data.size.z * (res - 1)), 0, res - 1);
                    char c;
                    if (reached[j, i])
                    {
                        float h = height[j, i];
                        c = !flatCell[j, i] ? '+' : Mathf.Abs(h - profile.FloorY(0)) <= 0.2f ? 'B' : Mathf.Abs(h - profile.FloorY(1)) <= 0.2f ? 'M' : Mathf.Abs(h - profile.FloorY(2)) <= 0.2f ? 'H' : '+';
                    }
                    else c = walkable[j, i] ? '.' : '#';
                    sb.Append(c);
                }

                sb.Append(" z=").Append(z.ToString("F0", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private static bool OnRibbon(VillageTerraceField field, float x, float z)
        {
            Vector2 local = field.Frame.ToLocal(x, z);
            foreach (VillageRibbon ribbon in field.Ramps) if (Near(ribbon, local)) return true;
            foreach (VillageRibbon ribbon in field.Stairs) if (Near(ribbon, local)) return true;
            return false;
        }

        private static bool Near(VillageRibbon ribbon, Vector2 p)
        {
            float limit = ribbon.HalfWidth + RibbonMargin;
            for (int i = 0; i < ribbon.PointCount - 1; i++)
            {
                Vector2 a = ribbon.Point(i);
                Vector2 ab = ribbon.Point(i + 1) - a;
                float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((p - (a + ab * k)).sqrMagnitude <= limit * limit) return true;
            }

            return false;
        }

        private static bool FindSeed(VillageLayoutProfile profile, Terrain terrain, VillageSide side, float[,] height, out int si, out int sj)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int res = height.GetLength(0);
            for (float x = 70f; x >= 30f; x -= 2f)
            {
                for (float z = -100f; z >= -130f; z -= 2f)
                {
                    int i = Mathf.RoundToInt((x - origin.x) / data.size.x * (res - 1));
                    int j = Mathf.RoundToInt((z - origin.z) / data.size.z * (res - 1));
                    if (Mathf.Abs(height[j, i] - profile.FloorY(0)) > SeedFloorTolerance || side.Weight(x, z) < 1f) continue;
                    si = i;
                    sj = j;
                    return true;
                }
            }

            si = sj = 0;
            return false;
        }

        private static string F(float value) => value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
