using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>
    /// Forme des chemins de terre sur le terrain : aplatit légèrement le relief le long du tracé, repeint le terrain
    /// avec le profil, puis pose la couche de chemin (largeur variable, bords organiques). Undo ; relancer régénère tout.
    /// Les tracés sont des points monde (x, z) ; la courbe est une spline de Catmull-Rom.
    /// </summary>
    public static class TerrainPathTool
    {
        private const string ProfilePath = "Assets/EasyTerrain/EasyTerrainProfile_VerticalSlice.asset";
        private const string PathLayerPath = "Assets/Scenes/SceneJeux/Defense/Defense_Dirt.terrainlayer";

        private const float HalfWidth = 2.5f;        // demi-largeur du chemin (m)
        private const float WidthWobble = 0.7f;      // variation de largeur (m)
        private const float EdgeSoftness = 1.6f;     // dégradé de bord de la texture (m)
        private const float FlattenFalloff = 6f;     // largeur de raccord du relief (m)
        private const float FlattenStrength = 0.9f;
        private const float SmoothWindow = 12f;      // lissage de la hauteur le long du chemin (m)
        private const float SampleStep = 1f;

        // Deux chemins qui traversent le bassin plat d'est en ouest et se rejoignent près du joueur.
        private static readonly Vector2[][] DefaultPaths =
        {
            new[] { new Vector2(215, 84), new Vector2(190, 92), new Vector2(160, 90), new Vector2(130, 96), new Vector2(105, 100), new Vector2(94, 102) },
            new[] { new Vector2(215, 56), new Vector2(190, 50), new Vector2(160, 56), new Vector2(130, 66), new Vector2(108, 86), new Vector2(94, 100) },
        };

        private sealed class Polyline
        {
            public Vector2[] Points;
            public float[] Height;
            public float[] Wobble;
        }

        [MenuItem("Tools/Easy Terrain/Former les 2 chemins")]
        public static void BuildDefault()
        {
            Terrain terrain = Terrain.activeTerrain;
            var profile = AssetDatabase.LoadAssetAtPath<EasyTerrainProfile>(ProfilePath);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(PathLayerPath);
            if (terrain == null || profile == null || layer == null) { Debug.LogError("[EasyTerrain] Terrain, profil ou couche de chemin introuvable."); return; }
            Debug.Log("[EasyTerrain] " + Build(terrain, profile, layer, DefaultPaths));
        }

        public static string Build(Terrain terrain, EasyTerrainProfile profile, TerrainLayer pathLayer, Vector2[][] paths)
        {
            TerrainData data = terrain.terrainData;
            var lines = new List<Polyline>();
            foreach (Vector2[] controls in paths) lines.Add(Sample(controls, terrain));

            Undo.RegisterCompleteObjectUndo(data, "Former les chemins");
            int flattened = Flatten(terrain, lines);
            SplatOps.AutoPaint(terrain, profile);
            int painted = PaintPaths(terrain, lines, pathLayer);
            terrain.Flush();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return $"{lines.Count} chemins : {flattened} points de relief aplanis, {painted} points de texture.";
        }

        #region Tracé
        private static Polyline Sample(Vector2[] c, Terrain terrain)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < c.Length - 1; i++)
            {
                Vector2 p0 = c[Mathf.Max(i - 1, 0)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(i + 2, c.Length - 1)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SampleStep));
                for (int k = 0; k < n; k++) pts.Add(CatmullRom(p0, p1, p2, p3, k / (float)n));
            }
            pts.Add(c[c.Length - 1]);

            var line = new Polyline { Points = pts.ToArray(), Height = new float[pts.Count], Wobble = new float[pts.Count] };
            var raw = new float[pts.Count];
            Vector3 origin = terrain.transform.position;
            for (int i = 0; i < pts.Count; i++)
            {
                raw[i] = terrain.SampleHeight(new Vector3(pts[i].x + origin.x, 0f, pts[i].y + origin.z));
                line.Wobble[i] = (Mathf.PerlinNoise(i * 0.09f, 3.7f) - 0.5f) * 2f * WidthWobble;
            }
            int w = Mathf.RoundToInt(SmoothWindow / SampleStep);
            for (int i = 0; i < pts.Count; i++)
            {
                float s = 0f; int cnt = 0;
                for (int j = Mathf.Max(0, i - w); j <= Mathf.Min(pts.Count - 1, i + w); j++) { s += raw[j]; cnt++; }
                line.Height[i] = s / cnt;
            }
            return line;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static float Nearest(Polyline line, Vector2 p, out int index)
        {
            float best = float.MaxValue; index = 0;
            for (int i = 0; i < line.Points.Length; i++)
            {
                float d = (line.Points[i] - p).sqrMagnitude;
                if (d < best) { best = d; index = i; }
            }
            return Mathf.Sqrt(best);
        }
        #endregion

        #region Relief
        private static int Flatten(Terrain terrain, List<Polyline> lines)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] h = data.GetHeights(0, 0, res, res);
            Vector3 size = data.size;
            float reach = HalfWidth + WidthWobble + 1f + FlattenFalloff;
            int count = 0;

            for (int z = 0; z < res; z++)
            {
                float wz = z / (float)(res - 1) * size.z;
                for (int x = 0; x < res; x++)
                {
                    float wx = x / (float)(res - 1) * size.x;
                    var p = new Vector2(wx, wz);
                    float weight = 0f, target = 0f;
                    foreach (Polyline line in lines)
                    {
                        float d = Nearest(line, p, out int i);
                        if (d > reach) continue;
                        float core = HalfWidth + line.Wobble[i] + 1f;
                        float w = d <= core ? 1f : Mathf.SmoothStep(1f, 0f, (d - core) / FlattenFalloff);
                        if (w > weight) { weight = w; target = line.Height[i]; }
                    }
                    if (weight <= 0f) continue;
                    float current = h[z, x] * size.y;
                    h[z, x] = Mathf.Lerp(current, target, weight * FlattenStrength) / size.y;
                    count++;
                }
            }
            data.SetHeights(0, 0, h);
            return count;
        }
        #endregion

        #region Texture
        private static int PaintPaths(Terrain terrain, List<Polyline> lines, TerrainLayer pathLayer)
        {
            TerrainData data = terrain.terrainData;
            int index = SplatOps.AddLayerToTerrain(terrain, pathLayer);
            if (index < 0) return 0;

            int w = data.alphamapWidth, h = data.alphamapHeight;
            float[,,] a = data.GetAlphamaps(0, 0, w, h);
            int layers = a.GetLength(2), count = 0;
            Vector3 size = data.size;

            for (int z = 0; z < h; z++)
            {
                float wz = z / (float)(h - 1) * size.z;
                for (int x = 0; x < w; x++)
                {
                    float wx = x / (float)(w - 1) * size.x;
                    var p = new Vector2(wx, wz);
                    float amount = 0f;
                    foreach (Polyline line in lines)
                    {
                        float d = Nearest(line, p, out int i);
                        float half = HalfWidth + line.Wobble[i];
                        if (d > half + EdgeSoftness) continue;
                        amount = Mathf.Max(amount, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, d - half) / EdgeSoftness));
                    }
                    if (amount <= 0.01f) continue;
                    for (int l = 0; l < layers; l++) a[z, x, l] *= 1f - amount;
                    a[z, x, index] = amount;
                    count++;
                }
            }
            data.SetAlphamaps(0, 0, a);
            return count;
        }
        #endregion
    }
}
