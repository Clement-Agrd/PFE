using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Capture, une seule fois et avant toute modification, les centres de lane (XZ)
    /// et les offsets Y des objets par rapport au terrain d'origine (backup).
    /// Les outils suivants ne lisent plus que ces donnees : ils sont idempotents.
    /// </summary>
    public static class DefenseReliefCaptureTool
    {
        private const int SamplesPerSpline = 400;

        [MenuItem("Tools/Defense/Relief/1 - Capture routes et offsets")]
        public static void Capture()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            ReliefFieldBase baseField = ReliefFieldBase.Create(config);

            CaptureRoutes(config);
            CaptureOffsets(config, baseField);

            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] Capture terminee (routes + offsets). Existants conserves.");
        }

        private static void CaptureRoutes(DefenseReliefConfig config)
        {
            if (config.HasCapturedRoutes)
            {
                Debug.Log("[DefenseRelief] Routes deja capturees : conservees (aucune ecrasement).");
                return;
            }

            for (int i = 0; i < config.Routes.Count; i++)
            {
                SplineContainer container = DefenseReliefScene.FindRouteSpline(config.Routes[i].splineName);
                var points = new Vector2[SamplesPerSpline + 1];
                for (int k = 0; k <= SamplesPerSpline; k++)
                {
                    Vector3 world = container.EvaluatePosition(k / (float)SamplesPerSpline);
                    points[k] = new Vector2(world.x, world.z);
                }
                config.SetCenterline(i, points);
            }
        }

        private static void CaptureOffsets(DefenseReliefConfig config, ReliefFieldBase baseField)
        {
            var targets = new List<Transform>();
            targets.AddRange(DefenseReliefScene.ExistingSlots());
            targets.AddRange(DefenseReliefScene.Torches());
            targets.AddRange(DefenseReliefScene.SpawnZones());

            foreach (Transform t in targets)
            {
                string path = DefenseReliefScene.HierarchyPath(t);
                if (config.TryGetOffset(path, out _)) continue;
                float offset = t.position.y - baseField.BaseHeight(t.position.x, t.position.z);
                config.SetOffset(path, offset);
            }
        }
    }

    /// <summary>Lecture seule de la hauteur du terrain d'origine (backup).</summary>
    public sealed class ReliefFieldBase
    {
        private readonly TerrainData _data;
        private readonly float[,] _heights;
        private readonly Vector3 _origin;

        private ReliefFieldBase(TerrainData data, Vector3 origin)
        {
            _data = data;
            _origin = origin;
            int res = data.heightmapResolution;
            _heights = data.GetHeights(0, 0, res, res);
        }

        public static ReliefFieldBase Create(DefenseReliefConfig config)
        {
            if (config.BaseTerrainData == null)
                throw new System.InvalidOperationException("baseTerrainData manquant dans DefenseReliefConfig.");
            return new ReliefFieldBase(config.BaseTerrainData, DefenseReliefScene.Terrain.transform.position);
        }

        public float BaseHeight(float worldX, float worldZ)
        {
            int res = _data.heightmapResolution;
            float u = Mathf.Clamp01((worldX - _origin.x) / _data.size.x) * (res - 1);
            float v = Mathf.Clamp01((worldZ - _origin.z) / _data.size.z) * (res - 1);
            int x0 = Mathf.Min(res - 2, (int)u);
            int z0 = Mathf.Min(res - 2, (int)v);
            float fx = u - x0;
            float fz = v - z0;
            float h = Mathf.Lerp(
                Mathf.Lerp(_heights[z0, x0], _heights[z0, x0 + 1], fx),
                Mathf.Lerp(_heights[z0 + 1, x0], _heights[z0 + 1, x0 + 1], fx),
                fz);
            return h * _data.size.y + _origin.y;
        }
    }
}
