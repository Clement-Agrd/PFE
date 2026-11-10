using System;
using System.Collections.Generic;
using System.IO;
using Core.GroundFog;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Core.EditorTools
{
    /// <summary>
    /// Génère une scène de paysage : montagne enneigée, rivière à cascades qui descend vers un lac,
    /// végétation Idyllic Fantasy Nature, brume au sol (GroundFog), ciel Valheim.
    /// Menu : Tools > Scene > Build River Lake Scene. Relançable (écrase la scène générée).
    /// </summary>
    public static class RiverLakeSceneBuilder
    {
        #region Constantes
        private const string Folder = "Assets/Scenes/Max";
        private const string ScenePath = Folder + "/SceneRiviereLac.unity";
        private const string TerrainDataPath = Folder + "/SceneRiviereLac_TerrainData.asset";
        private const string RiverMeshPath = Folder + "/SceneRiviereLac_River.asset";
        private const string NatureRoot = "Assets/PackagesStore/Idyllic Fantasy Nature";
        private const string SourceSkyPath = "Assets/Arts/Materials/PostProcessTest/Valheim_Sky.mat";
        private const string SourceVolumePath = "Assets/Settings/ValheimVolumeProfile.asset";

        private const int Res = 513;
        private static readonly Vector3 Size = new Vector3(320f, 120f, 320f);
        private static readonly Vector3 Origin = new Vector3(1000f, 0f, 0f); // décalé pour ne pas chevaucher la scène ouverte

        private const float WaterLevel = 8f;
        private static readonly Vector2 LakeCenter = new Vector2(160f, 70f);
        private static readonly Vector2 LakeRadius = new Vector2(60f, 45f);
        private const float RiverEnd = 8.2f;
        #endregion

        #region État de génération
        private static float[,] _h;                 // hauteurs en mètres [z, x]
        private static float[,] _riverDist;         // distance à la rivière
        private static float[,] _riverWidth;        // demi-largeur locale
        private static Vector2[] _cl;               // ligne centrale de la rivière
        private static float[] _clE;                // altitude de l'eau le long de la rivière
        private static float[] _clS;                // abscisse curviligne 0..1
        private static float[] _clW;                // demi-largeur
        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
        private static System.Random _rng;
        private static GameObject _sceneCamera;     // caméra de cadrage, désactivée quand le joueur (avec sa caméra) est posé
        #endregion

        [MenuItem("Tools/Scene/Build River Lake Scene")]
        public static void Build()
        {
            _rng = new System.Random(20260710);
            PrefabCache.Clear();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Scenes", "Max");

            // La scène générée remplace la scène ouverte : on refuse si une autre scène a des modifications non sauvegardées.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty && open.path != ScenePath)
                {
                    Debug.LogError("[RiverLake] Scène \"" + open.name + "\" modifiée non sauvegardée : sauvegarde-la avant de régénérer.");
                    return;
                }
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureWaterFx();
            GenerateHeights();
            Terrain terrain = CreateTerrain();
            BuildRiverMesh();
            BuildLake();
            PlaceVegetation(terrain);
            PlaceFog();
            SetupAtmosphere();
            SetupCamera(terrain);
            PlacePlayer(terrain);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[RiverLake] Scène générée : " + ScenePath);
        }

        #region Hauteurs
        private static float Fbm(float x, float z, int oct)
        {
            float a = 0.5f, sum = 0f, norm = 0f, f = 1f;
            for (int i = 0; i < oct; i++)
            {
                sum += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f + 17.3f * i);
                norm += a; a *= 0.5f; f *= 2.03f;
            }
            return sum / norm;
        }

        private static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        private static float Bump(float x, float z, float cx, float cz, float h, float r)
        {
            float dx = x - cx, dz = z - cz;
            return h * Mathf.Exp(-(dx * dx + dz * dz) / (2f * r * r));
        }

        private static float BaseHeight(float x, float z)
        {
            float h = 11f + 8f * (Fbm(x * 0.008f, z * 0.008f, 4) - 0.5f);
            h += 8f * Smooth(90f, 170f, z);
            // Sommets étroits + crêtes (ridged noise) pour un vrai relief de montagne.
            float b1 = Bump(x, z, 170f, 255f, 105f, 30f), b2 = Bump(x, z, 92f, 262f, 82f, 26f), b3 = Bump(x, z, 250f, 258f, 90f, 28f), b4 = Bump(x, z, 165f, 205f, 9f, 28f);
            float bMax = Mathf.Max(Mathf.Max(b1, b2), Mathf.Max(b3, b4));
            h += bMax + 0.2f * (b1 + b2 + b3 + b4 - bMax); // pics distincts séparés par des cols
            float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.032f + 5f, z * 0.032f + 9f) - 1f);
            float ridge2 = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.07f + 21f, z * 0.07f + 4f) - 1f);
            float m = Smooth(120f, 235f, z);
            h += m * (13f * ridge * ridge * ridge + 4f * ridge2 * ridge2);
            h += 2.2f * (Fbm(x * 0.06f, z * 0.06f, 3) - 0.5f);
            h = h < 70f ? h : 70f + 42f * (1f - Mathf.Exp(-(h - 70f) / 42f)); // plafond doux, pas de plateau

            // L'île s'enfonce dans la mer en bordure de carte.
            float edge = Mathf.Min(Mathf.Min(x, Size.x - x), Mathf.Min(z, Size.z - z));
            return Mathf.Lerp(3.5f, h, Smooth(0f, 30f, edge));
        }

        private static float LakeD(float x, float z)
        {
            float dx = (x - LakeCenter.x) / LakeRadius.x, dz = (z - LakeCenter.y) / LakeRadius.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return d + (Mathf.PerlinNoise(x * 0.05f, z * 0.05f) - 0.5f) * 0.3f; // rive irrégulière
        }

        private static float Bilinear(float[,] a, float x, float z)
        {
            float u = Mathf.Clamp(x / Size.x * (Res - 1), 0, Res - 1.001f);
            float v = Mathf.Clamp(z / Size.z * (Res - 1), 0, Res - 1.001f);
            int i = (int)u, j = (int)v; float fu = u - i, fv = v - j;
            return Mathf.Lerp(Mathf.Lerp(a[j, i], a[j, i + 1], fu), Mathf.Lerp(a[j + 1, i], a[j + 1, i + 1], fu), fv);
        }

        private static void GenerateHeights()
        {
            _h = new float[Res, Res];
            _riverDist = new float[Res, Res];
            _riverWidth = new float[Res, Res];
            float cell = Size.x / (Res - 1);

            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float x = i * cell, z = j * cell;
                    float h = BaseHeight(x, z);
                    float d = LakeD(x, z);
                    float bowl = 1f + 9f * Smooth(0.35f, 1f, d);
                    float w = 1f - Smooth(0.8f, 1.35f, d);
                    _h[j, i] = Mathf.Lerp(h, bowl, w);
                    _riverDist[j, i] = 9999f;
                }

            BuildCenterline();
            CarveRiver(cell);
            Blur();
        }

        private static void BuildCenterline()
        {
            Vector2[] ctrl =
            {
                new Vector2(165, 192), new Vector2(140, 178), new Vector2(130, 160), new Vector2(152, 146),
                new Vector2(180, 134), new Vector2(172, 118), new Vector2(152, 108), new Vector2(156, 98)
            };
            var pts = new List<Vector2>();
            for (int s = 0; s < ctrl.Length - 1; s++)
            {
                Vector2 p0 = ctrl[Mathf.Max(s - 1, 0)], p1 = ctrl[s], p2 = ctrl[s + 1], p3 = ctrl[Mathf.Min(s + 2, ctrl.Length - 1)];
                for (int k = 0; k < 30; k++)
                {
                    float t = k / 30f, t2 = t * t, t3 = t2 * t;
                    pts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            pts.Add(ctrl[ctrl.Length - 1]);
            _cl = pts.ToArray();
            int n = _cl.Length;

            _clS = new float[n];
            for (int k = 1; k < n; k++) _clS[k] = _clS[k - 1] + Vector2.Distance(_cl[k], _cl[k - 1]);
            float len = _clS[n - 1];
            for (int k = 0; k < n; k++) _clS[k] /= len;

            // Altitude du terrain lissée le long du tracé.
            var hs = new float[n];
            for (int k = 0; k < n; k++)
            {
                float sum = 0f; int c = 0;
                for (int m = -6; m <= 6; m++) { int q = Mathf.Clamp(k + m, 0, n - 1); sum += Bilinear(_h, _cl[q].x, _cl[q].y); c++; }
                hs[k] = sum / c;
            }

            float y0 = hs[0];
            _clE = new float[n];
            _clW = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = _clS[k];
                float steps = 0.5f * Smooth(0.36f, 0.41f, s) + 0.5f * Smooth(0.62f, 0.67f, s); // deux cascades
                float prof = y0 + (RiverEnd - y0) * (0.55f * s + 0.45f * steps);
                float e = Mathf.Min(prof, Mathf.Max(hs[k] - 1.2f, RiverEnd));
                if (k > 0) e = Mathf.Min(e, _clE[k - 1]);
                _clE[k] = Mathf.Max(e, RiverEnd);
                _clW[k] = Mathf.Lerp(1.8f, 3.4f, s) * (1f + 0.15f * Mathf.Sin(s * 17f));
            }
        }

        private static void CarveRiver(float cell)
        {
            int n = _cl.Length;
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    Vector2 p = new Vector2(i * cell, j * cell);
                    int best = 0; float bd = float.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        float d2 = (p - _cl[k]).sqrMagnitude;
                        if (d2 < bd) { bd = d2; best = k; }
                    }
                    float dist = float.MaxValue, e = _clE[best], w = _clW[best];
                    for (int seg = best - 1; seg <= best; seg++)
                    {
                        if (seg < 0 || seg >= n - 1) continue;
                        Vector2 a = _cl[seg], b = _cl[seg + 1], ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        float d = Vector2.Distance(p, a + ab * t);
                        if (d < dist) { dist = d; e = Mathf.Lerp(_clE[seg], _clE[seg + 1], t); w = Mathf.Lerp(_clW[seg], _clW[seg + 1], t); }
                    }
                    _riverDist[j, i] = dist; _riverWidth[j, i] = w;

                    float mask = 1f - Smooth(w * 2.2f, w * 4.2f, dist);
                    if (mask <= 0f) continue;
                    float h0 = _h[j, i];
                    float q = dist / w;
                    float bank = Mathf.Min(e - 0.5f + 0.7f * q * q, Mathf.Max(h0, e + 0.4f));
                    _h[j, i] = Mathf.Lerp(h0, bank, mask);
                }
        }

        private static void Blur()
        {
            var o = (float[,])_h.Clone();
            for (int j = 1; j < Res - 1; j++)
                for (int i = 1; i < Res - 1; i++)
                {
                    float s = 0f;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) s += o[j + dj, i + di];
                    _h[j, i] = Mathf.Lerp(o[j, i], s / 9f, 0.7f);
                }
        }
        #endregion

        #region Terrain
        private static TerrainLayer Layer(string path) => AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);

        private static Terrain CreateTerrain()
        {
            var td = new TerrainData { heightmapResolution = Res, size = Size };
            AssetDatabase.DeleteAsset(TerrainDataPath);
            AssetDatabase.CreateAsset(td, TerrainDataPath); // d abord l asset : les alphamaps sont ensuite écrites dans ses sous-assets
            var norm = new float[Res, Res];
            for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) norm[j, i] = Mathf.Clamp01(_h[j, i] / Size.y);
            td.SetHeights(0, 0, norm);

            string tl = NatureRoot + "/Terrain Layer/";
            TerrainLayer[] layers =
            {
                Layer("Assets/Arts/Terrain/Herbe 2.terrainlayer"), Layer("Assets/Arts/Terrain/Herbe 1.terrainlayer"), Layer("Assets/Arts/Terrain/Terre.terrainlayer"),
                Layer("Assets/Arts/Terrain/Roche.terrainlayer"), Layer(tl + "Sand_Layer.terrainlayer"), Layer("Assets/Arts/Terrain/Neige.terrainlayer")
            };
            td.terrainLayers = layers;

            const int ar = 512;
            td.alphamapResolution = ar;
            var map = new float[ar, ar, layers.Length];
            float cellH = Size.x / (Res - 1);
            for (int j = 0; j < ar; j++)
                for (int i = 0; i < ar; i++)
                {
                    float x = i / (float)(ar - 1) * Size.x, z = j / (float)(ar - 1) * Size.z;
                    int hi = Mathf.Clamp(Mathf.RoundToInt(i / (float)(ar - 1) * (Res - 1)), 1, Res - 2);
                    int hj = Mathf.Clamp(Mathf.RoundToInt(j / (float)(ar - 1) * (Res - 1)), 1, Res - 2);
                    float h = _h[hj, hi];
                    float gx = (_h[hj, hi + 1] - _h[hj, hi - 1]) / (2f * cellH), gz = (_h[hj + 1, hi] - _h[hj - 1, hi]) / (2f * cellH);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    float noise = Fbm(x * 0.05f, z * 0.05f, 3);
                    float dLake = LakeD(x, z), dRiver = _riverDist[hj, hi], wRiver = _riverWidth[hj, hi];

                    float rock = Mathf.Max(Smooth(30f, 46f, slope), 0.55f * Smooth(42f, 62f, h + noise * 8f));
                    float snow = Smooth(50f, 62f, h + (noise - 0.5f) * 12f) * (1f - Smooth(42f, 58f, slope));
                    float sand = (1f - Smooth(0.5f, 1.8f, Mathf.Abs(h - WaterLevel)));
                    float dirt = (1f - Smooth(wRiver * 1.1f, wRiver * 2.8f, dRiver)) * 0.9f + 0.18f * Smooth(0.55f, 0.8f, noise);
                    float forest = Smooth(13f, 22f, h) * (1f - Smooth(46f, 58f, h)) * Smooth(0.42f, 0.62f, noise) + 0.45f * Smooth(0.4f, 0.65f, Fbm(x * 0.03f + 40f, z * 0.03f, 3));
                    float grass = 0.7f;

                    float[] w = { grass, forest * 1.2f, dirt * 2.5f, rock * 3f, sand * 3f, snow * 4f };
                    float sum = 0f; for (int k = 0; k < w.Length; k++) sum += w[k];
                    for (int k = 0; k < w.Length; k++) map[j, i, k] = w[k] / sum;
                }
            td.SetAlphamaps(0, 0, map);
            EditorUtility.SetDirty(td);


            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain_RiviereLac";
            go.transform.position = Origin;
            Terrain t = go.GetComponent<Terrain>();
            t.drawInstanced = true;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 400f;
            return t;
        }
        #endregion

        #region Eau
        private static void BuildRiverMesh()
        {
            int n = _cl.Length;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var tris = new List<int>();
            float run = 0f;
            for (int k = 0; k < n; k++)
            {
                Vector2 dir = (k < n - 1 ? _cl[k + 1] - _cl[k] : _cl[k] - _cl[k - 1]).normalized;
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                float half = _clW[k] * 0.9f;
                float y = _clE[k] + 0.02f;
                verts[k * 2] = new Vector3(_cl[k].x + nrm.x * half, y, _cl[k].y + nrm.y * half);
                verts[k * 2 + 1] = new Vector3(_cl[k].x - nrm.x * half, y, _cl[k].y - nrm.y * half);
                if (k > 0) run += Vector2.Distance(_cl[k], _cl[k - 1]);
                uvs[k * 2] = new Vector2(0f, run * 0.25f);
                uvs[k * 2 + 1] = new Vector2(1f, run * 0.25f);
                if (k < n - 1)
                {
                    int a = k * 2;
                    tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            var mesh = new Mesh { name = "RiverMesh", vertices = verts, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(RiverMeshPath);
            AssetDatabase.CreateAsset(mesh, RiverMeshPath);

            var go = new GameObject("River");
            go.transform.position = Origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = WaterMaterial("Water_River");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private const string WaterFxPrefabs = "Assets/WaterFx/Prefabs/";
        private const string WaterFxTextures = "Assets/VFX/WaterFx_Unity/Assets/WaterFx/Textures/";

        /// <summary>Installe WaterFx si besoin (matériaux + prefabs) et branche les textures à leur emplacement réel.</summary>
        private static void EnsureWaterFx()
        {
            if (!AssetDatabase.IsValidFolder("Assets/WaterFx")) AssetDatabase.CreateFolder("Assets", "WaterFx");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WaterFxPrefabs + "Water_Lake.prefab") == null)
                WaterFx.Editor.WaterFxMenu.Install();

            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterFxTextures + "T_WaterNormal.png");
            var foam = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterFxTextures + "T_WaterFoam.png");
            foreach (string n in new[] { "Water_Lake", "Water_River", "Water_Ocean" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/WaterFx/Materials/M_" + n + ".mat");
                if (m == null) continue;
                if (normal != null) m.SetTexture("_NormalMap", normal);
                if (foam != null) m.SetTexture("_FoamMap", foam);
                if (n == "Water_Ocean") m.SetFloat("_WaveScale", 0.3f); // mer calme autour de l île (les grosses vagues noyaient le décor)
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        private static Material WaterMaterial(string preset) =>
            AssetDatabase.LoadAssetAtPath<Material>("Assets/WaterFx/Materials/M_" + preset + ".mat");

        private static GameObject PlaceWater(string preset, string objName, Vector3 pos, Vector3 scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterFxPrefabs + preset + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = objName;
            go.transform.position = pos;
            go.transform.localScale = scale;
            return go;
        }

        private static void BuildLake()
        {
            // Mer tout autour de l île : cache le bord de carte et la skybox.
            PlaceWater("Water_Ocean", "Sea", Origin + new Vector3(Size.x * 0.5f, WaterLevel - 0.3f, Size.z * 0.5f), new Vector3(900f, 1f, 900f));
            // Lac : plan WaterFx dimensionné sur l ellipse (plus un débord caché sous le terrain).
            PlaceWater("Water_Lake", "Lake", Origin + new Vector3(LakeCenter.x, WaterLevel, LakeCenter.y), new Vector3(LakeRadius.x * 2.4f, 1f, LakeRadius.y * 2.4f));
        }
        #endregion

        #region Végétation
        private static GameObject Prefab(string name)
        {
            if (PrefabCache.TryGetValue(name, out var cached)) return cached;
            GameObject found = null;
            foreach (string g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { NatureRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) { found = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
            PrefabCache[name] = found;
            return found;
        }

        private static float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private static T Pick<T>(IList<T> l) => l[_rng.Next(l.Count)];

        private static void NearestRiver(float x, float z, out float dist, out float width)
        {
            dist = float.MaxValue; width = 2f;
            for (int k = 0; k < _cl.Length; k++)
            {
                float d = Vector2.Distance(new Vector2(x, z), _cl[k]);
                if (d < dist) { dist = d; width = _clW[k]; }
            }
        }

        private delegate bool Rule(float x, float z, float h, float slope, float dLake, float dRiver, float wRiver);

        /// <summary>Tire des positions au hasard et instancie les prefabs quand la règle est satisfaite.</summary>
        private static void Scatter(Terrain t, Transform parent, string[] names, int count, float scaleMin, float scaleMax,
            Rule rule, float sink = 0f, bool tilt = false, bool onWater = false)
        {
            var td = t.terrainData;
            int placed = 0, tries = 0;
            while (placed < count && tries < count * 40)
            {
                tries++;
                float x = Rand(4f, Size.x - 4f), z = Rand(4f, Size.z - 4f);
                float h = td.GetInterpolatedHeight(x / Size.x, z / Size.z);
                float slope = td.GetSteepness(x / Size.x, z / Size.z);
                NearestRiver(x, z, out float dR, out float wR);
                if (new Vector2(x - 158f, z - 34f).magnitude < 14f) continue; // dégage la vue de la caméra
                if (!rule(x, z, h, slope, LakeD(x, z), dR, wR)) continue;

                GameObject prefab = Prefab(Pick(names));
                if (prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                float y = onWater ? WaterLevel + 0.02f : h - sink;
                inst.transform.position = Origin + new Vector3(x, y, z);
                Quaternion rot = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                if (tilt) rot = Quaternion.Euler(Rand(-8f, 8f), Rand(0f, 360f), Rand(-8f, 8f));
                inst.transform.rotation = rot;
                inst.transform.localScale = Vector3.one * Rand(scaleMin, scaleMax);
                placed++;
            }
        }

        private static string[] Names(string prefix, int from, int to, string suffix = "")
        {
            var l = new List<string>();
            for (int i = from; i <= to; i++) l.Add(prefix + i.ToString("00") + suffix);
            return l.ToArray();
        }

        private static void PlaceVegetation(Terrain t)
        {
            Transform root = new GameObject("Vegetation").transform;
            Transform Group(string n) { var g = new GameObject(n).transform; g.SetParent(root, false); return g; }

            // Sapins : pentes moyennes, altitude moyenne à haute, loin de l'eau.
            Scatter(t, Group("Sapins"), Names("Fir_", 1, 5), 420, 1.5f, 2.8f,
                (x, z, h, s, dl, dr, wr) => h > 20f && h < 54f && s < 36f && dl > 1.5f && dr > wr * 3f && Mathf.PerlinNoise(x * 0.04f, z * 0.04f) > 0.38f);

            // Feuillus : vallée, autour du lac.
            var broad = new List<string>();
            foreach (string c in new[] { "Red", "Red", "Purple", "Purple", "Green" })
                for (int i = 1; i <= 5; i++) broad.Add("BroadleafTree_" + i.ToString("00") + "_" + c);
            Scatter(t, Group("Feuillus"), broad.ToArray(), 60, 1.4f, 2.2f,
                (x, z, h, s, dl, dr, wr) => h > 9f && h < 20f && s < 22f && dl > 1.25f && dr > wr * 3f);

            Scatter(t, Group("Cerisiers"), Names("BlossomTree_", 1, 5), 12, 1.5f, 2.2f,
                (x, z, h, s, dl, dr, wr) => h > 8.5f && h < 16f && s < 18f && dl > 1.2f && dl < 1.9f && dr > wr * 3f);

            // Sous-bois.
            Scatter(t, Group("Buissons"), new[] { "Bush_01_01", "Bush_01_02", "Bush_02_01", "Bush_02_02", "Bush_03_01", "Bush_03_02" }, 170, 1.4f, 2.4f,
                (x, z, h, s, dl, dr, wr) => h > 9f && h < 40f && s < 32f && dl > 1.2f && dr > wr * 1.6f);
            Scatter(t, Group("Plantes"), Names("Plant_", 1, 8), 200, 1.2f, 2.2f,
                (x, z, h, s, dl, dr, wr) => h > 9f && h < 36f && s < 30f && dl > 1.15f);
            Scatter(t, Group("Herbes"), Names("Grass_", 1, 3), 420, 1.6f, 2.8f,
                (x, z, h, s, dl, dr, wr) => h > 8.6f && h < 44f && s < 34f && dl > 1.05f);
            Scatter(t, Group("Fleurs"), new[] { "Flower_Blue_01", "Flower_Blue_02", "Flower_Orange", "Flower_Pink", "Flower_Purple", "Flower_Red", "Flower_White", "Flower_Yellow", "Flower_YellowRed" }, 260, 1.4f, 2.4f,
                (x, z, h, s, dl, dr, wr) => h > 8.6f && h < 22f && s < 24f && dl > 1.05f && Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f) > 0.45f);
            Scatter(t, Group("Prairies"), new[] { "FlowerMeadow_Blue", "FlowerMeadow_Pink", "FlowerMeadow_White", "FlowerMeadow_Purple", "FlowerMeadow_RedOrange", "FlowerMeadow_OrangePinkRedPurpleBlue" }, 120, 3f, 6f,
                (x, z, h, s, dl, dr, wr) => h > 8.6f && h < 20f && s < 20f && dl > 1.05f);

            // Roches : berges, rives du lac, cascades.
            Scatter(t, Group("RochersBerges"), new[] { "Rock_Medium_01", "Rock_Medium_02", "Rock_Medium_03", "Rock_Small_01", "Rock_Small_02", "Rock_Small_03", "Stone_Medium_01", "Stone_Medium_02", "Stone_Medium_03", "Stones_01", "Stones_02" }, 80, 1.1f, 2.4f,
                (x, z, h, s, dl, dr, wr) => dr > wr * 0.95f && dr < wr * 3f && h > 8f, sink: 0.15f, tilt: true);
            Scatter(t, Group("GrosRochers"), new[] { "Rock_Big_01", "Rock_Big_02", "Rock_Big_03", "Stone_Big_01", "Stone_Big_02", "Stone_Big_03" }, 26, 1.2f, 2.4f,
                (x, z, h, s, dl, dr, wr) => (dr > wr * 2.6f && dr < wr * 6f && h > 9f) || (dl > 0.86f && dl < 1.15f && h > 7.5f), sink: 0.5f, tilt: true);

            // Falaises sur les pentes raides de la montagne.
            Scatter(t, Group("Falaises"), Names("Cliff_", 1, 5), 14, 1.0f, 1.8f,
                (x, z, h, s, dl, dr, wr) => h > 24f && h < 60f && s > 40f && dr > wr * 5f, sink: 1.8f, tilt: true);

            // Plantes aquatiques.
            Scatter(t, Group("Roseaux"), Names("Reeds_", 1, 3), 230, 1.4f, 2.4f,
                (x, z, h, s, dl, dr, wr) => dl > 0.74f && dl < 0.95f && h > 6.5f && h < 8.8f, sink: 0.1f);
            Scatter(t, Group("Massettes"), Names("Cattail_", 1, 3), 110, 1.3f, 2.2f,
                (x, z, h, s, dl, dr, wr) => dl > 0.78f && dl < 0.97f && h > 6.8f && h < 8.9f, sink: 0.1f);
            Scatter(t, Group("Nenuphars"), Names("LilyPads_", 1, 3), 90, 1.0f, 1.8f,
                (x, z, h, s, dl, dr, wr) => dl < 0.7f, onWater: true);
            Scatter(t, Group("FeuillesFlottantes"), new[] { "FloatingLeafs_Green", "FloatingLeafs_Pink", "FloatingLeafs_Yellow", "FloatingLeafs_Red" }, 40, 1.0f, 1.6f,
                (x, z, h, s, dl, dr, wr) => dl < 0.78f, onWater: true);

            // Vie et lumière.
            AddAmbient("ButterflySpawnArea", new Vector3(150f, 12f, 40f), root);
            AddAmbient("ButterflySpawnArea", new Vector3(215f, 12f, 85f), root);
            AddAmbient("GodRays", new Vector3(160f, 30f, 120f), root);
        }

        private static void AddAmbient(string prefabName, Vector3 local, Transform parent)
        {
            GameObject p = Prefab(prefabName);
            if (p == null) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            inst.transform.position = Origin + local;
        }
        #endregion

        #region Joueur
        /// <summary>Pose le prefab Player sur l herbe, en bord de lac, tourné vers l eau ; sa caméra remplace celle de la scène.</summary>
        private static void PlacePlayer(Terrain t)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
            if (prefab == null) { Debug.LogWarning("[RiverLake] Player.prefab introuvable : joueur ignoré."); return; }

            var td = t.terrainData;
            Vector2 best = new Vector2(110f, 60f);
            float bestScore = float.MaxValue;
            for (float x = 60f; x < 130f; x += 2f)
                for (float z = 40f; z < 100f; z += 2f)
                {
                    float h = td.GetInterpolatedHeight(x / Size.x, z / Size.z);
                    float s = td.GetSteepness(x / Size.x, z / Size.z);
                    float d = LakeD(x, z);
                    if (h < 9f || h > 12f || s > 10f || d < 1.2f || d > 1.5f) continue;
                    float score = Mathf.Abs(d - 1.3f) + Mathf.Abs(x - 110f) * 0.01f;
                    if (score < bestScore) { bestScore = score; best = new Vector2(x, z); }
                }

            float y = td.GetInterpolatedHeight(best.x / Size.x, best.y / Size.z);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = Origin + new Vector3(best.x, y + 0.1f, best.y);
            player.transform.rotation = Quaternion.LookRotation(new Vector3(LakeCenter.x - best.x, 0f, LakeCenter.y - best.y));
            if (player.GetComponent<GroundFogClearer>() == null) player.AddComponent<GroundFogClearer>();

            if (_sceneCamera != null) _sceneCamera.SetActive(false);
        }
        #endregion

        #region Brume, ciel, caméra
        private static void PlaceFog()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/GroundFog.mat");
            var preset = AssetDatabase.LoadAssetAtPath<GroundFogPreset>("Assets/ScriptableObjects/GroundFogPreset_Cour.asset");
            if (mat == null || preset == null) { Debug.LogWarning("[RiverLake] Matériau ou préréglage GroundFog introuvable : brume ignorée."); return; }

            Transform root = new GameObject("Brumes").transform;
            // (centre local, taille) : nappe sur le lac, rive nord, pied des cascades.
            for (int k = 0; k < _cl.Length; k++)
            {
                float s = _clS[k];
                if (Mathf.Abs(s - 0.43f) < 0.006f || Mathf.Abs(s - 0.69f) < 0.006f)
                    Fog(root, mat, preset, new Vector3(_cl[k].x, _clE[k] + 1.4f, _cl[k].y), new Vector3(20f, 3.2f, 20f), 0.5f);
            }
        }

        /// <summary>Copie du préréglage de brume, beaucoup plus légère pour ne pas masquer l eau.</summary>
        private static GroundFogPreset BuildLakePreset(GroundFogPreset src)
        {
            const string path = Folder + "/GroundFogPreset_Lac.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path);
            var p = AssetDatabase.LoadAssetAtPath<GroundFogPreset>(path);
            var so = new SerializedObject(p);
            so.FindProperty("day.density").floatValue = 0.14f;
            so.FindProperty("night.density").floatValue = 0.14f;
            so.FindProperty("selfLight").floatValue = 0.5f;
            so.FindProperty("waveAmplitude").floatValue = 0.04f;
            so.ApplyModifiedProperties();
            return p;
        }

        private static void Fog(Transform parent, Material mat, GroundFogPreset preset, Vector3 center, Vector3 size, float night)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GroundFogZone";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(parent, false);
            go.transform.position = Origin + center;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var zone = go.AddComponent<GroundFogZone>();
            zone.SetPreset(preset);
            zone.SetNight(night);
        }

        private static void SetupAtmosphere()
        {
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.8f); // heure dorée douce
            sun.intensity = 2.1f;
            sun.transform.rotation = Quaternion.Euler(24f, 325f, 0f);
            sun.shadows = LightShadows.Soft;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            RenderSettings.skybox = BuildSky(AssetDatabase.LoadAssetAtPath<Material>(SourceSkyPath));
            RenderSettings.ambientMode = AmbientMode.Skybox; // ambiance bleutée du ciel, contraste avec le soleil doré
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0013f; // voile léger pour la profondeur atmosphérique
            RenderSettings.fogColor = new Color(0.68f, 0.78f, 0.9f);

            var srcProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SourceVolumePath);
            if (srcProfile != null)
            {
                var vg = new GameObject("Global Volume");
                var v = vg.AddComponent<Volume>();
                v.isGlobal = true;
                v.sharedProfile = BuildVolumeProfile(srcProfile);
            }
        }

        /// <summary>Copie du ciel de la scène source, réglée pour un ciel bleu de jour dégagé.</summary>
        private static Material BuildSky(Material src)
        {
            const string path = Folder + "/SceneRiviereLac_Sky.mat";
            AssetDatabase.DeleteAsset(path);
            var mat = src != null ? new Material(src) : new Material(Shader.Find("Skybox/Procedural"));
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", new Color(0.46f, 0.6f, 0.85f));
            if (mat.HasProperty("_GroundColor")) mat.SetColor("_GroundColor", new Color(0.42f, 0.5f, 0.55f));
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.25f);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 0.9f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Copie du profil post-process source, avec une colorimétrie plus claire et plus saturée.</summary>
        private static VolumeProfile BuildVolumeProfile(VolumeProfile src)
        {
            const string path = Folder + "/SceneRiviereLac_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path);
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p.TryGet(out ColorAdjustments ca))
            {
                ca.postExposure.value = 0.15f;
                ca.saturation.value = 14f;
                ca.contrast.value = 12f;
                ca.colorFilter.value = Color.white;
            }
            if (p.TryGet(out WhiteBalance wb)) { wb.temperature.value = 2f; wb.tint.value = 0f; }
            if (p.TryGet(out SplitToning st)) st.highlights.value = new Color(1f, 0.88f, 0.7f);
            if (p.TryGet(out Bloom bl)) { bl.intensity.value = 0.45f; bl.tint.value = new Color(1f, 0.93f, 0.82f); }
            EditorUtility.SetDirty(p);
            return p;
        }

        private static void SetupCamera(Terrain t)
        {
            float x = 158f, z = 34f;
            float y = t.terrainData.GetInterpolatedHeight(x / Size.x, z / Size.z) + 13f;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            _sceneCamera = go;
            go.transform.position = Origin + new Vector3(x, y, z);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.farClipPlane = 1500f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.LookAt(Origin + new Vector3(168f, 52f, 240f));
        }
        #endregion
    }
}
