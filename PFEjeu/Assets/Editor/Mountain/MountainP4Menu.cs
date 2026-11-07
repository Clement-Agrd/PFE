using System;
using System.Text;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// P4 : active les cretes lointaines (zone exterieure > 250 m de la herse) dans l'outil de sculpt existant,
    /// puis recale, repose les objets et relance P1 (roche, givre pale, limite des arbres) sur le nouveau relief.
    /// Le massif existant n'est pas desactive : les cretes le prolongent.
    /// </summary>
    public static class MountainP4Menu
    {
        private const float ReportStepDegrees = 15f;

        [MenuItem("Tools/Defense/Mountain/P4 - Cretes lointaines (sculpt, recalage, P1)")]
        public static void ApplyRidges() => Run(true);

        [MenuItem("Tools/Defense/Mountain/P4 - Retirer les cretes lointaines (sculpt, recalage, P1)")]
        public static void RemoveRidges() => Run(false);

        [MenuItem("Tools/Defense/Mountain/P4 - Rapport des cretes")]
        public static void ReportOnly() =>
            Debug.Log(BuildReport(DefenseReliefScene.Terrain, DefenseReliefScene.HersePosition, MountainRidgeProfile.LoadOrCreate()));

        private static void Run(bool enable)
        {
            try
            {
                MountainRidgeProfile ridges = MountainRidgeProfile.LoadOrCreate();
                ridges.SetEnabled(enable);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("Mountain P4", "Sculpt du relief", 0.1f);
                DefenseReliefSculptTool.Sculpt();

                EditorUtility.DisplayProgressBar("Mountain P4", "Recalage", 0.45f);
                DefenseReliefAlignTool.Align();
                DefenseReliefGroundTool.SnapOnly();

                EditorUtility.DisplayProgressBar("Mountain P4", "Peinture et vegetation (P1)", 0.7f);
                MountainP1Menu.Apply();

                Debug.Log(BuildReport(DefenseReliefScene.Terrain, DefenseReliefScene.HersePosition, ridges));
            }
            catch (Exception exception)
            {
                Debug.LogError("[Mountain P4] interrompu : " + exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Hauteur de crete (relative a la vallee) par azimut dans la zone exterieure.</summary>
        private static string BuildReport(Terrain terrain, Vector2 herse, MountainRidgeProfile profile)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            float valleyY = MountainProfile.LoadOrCreate().ValleyY;
            var sb = new StringBuilder("[Mountain P4] Cretes " + (profile.Enabled ? "actives" : "inactives") + " : hauteur max (m / vallee) par azimut dans la zone > " + profile.MinDistanceFromHerse + " m\n");
            sb.Append("azimut : ");
            for (float az = 0f; az < 360f; az += ReportStepDegrees)
            {
                float best = 0f;
                for (float r = profile.MinDistanceFromHerse; r <= 560f; r += 4f)
                {
                    float x = herse.x + Mathf.Sin(az * Mathf.Deg2Rad) * r;
                    float z = herse.y + Mathf.Cos(az * Mathf.Deg2Rad) * r;
                    float u = (x - origin.x) / data.size.x;
                    float v = (z - origin.z) / data.size.z;
                    if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                    best = Mathf.Max(best, data.GetInterpolatedHeight(u, v) + origin.y - valleyY);
                }
                sb.Append(az.ToString("F0")).Append("=").Append(best.ToString("F0")).Append("  ");
            }
            return sb.ToString();
        }
    }
}
