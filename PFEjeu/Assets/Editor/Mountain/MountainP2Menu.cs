using System;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// P2 : bascule le bord du plateau en "FalaiseAvecPassages", puis enchaine les etapes existantes du relief
    /// (sculpt, recalage, repose via GroundSnapRegistry, rebake NavMesh) et relance P1 sur le nouveau relief.
    /// Le mode glacis d'origine reste disponible et restaure l'ancien relief.
    /// </summary>
    public static class MountainP2Menu
    {
        [MenuItem("Tools/Defense/Mountain/P2 - Falaise avec passages (sculpt, recalage, NavMesh, P1)")]
        public static void ApplyCliff() => Run(DefenseReliefConfig.PlateauEdgeMode.FalaiseAvecPassages);

        [MenuItem("Tools/Defense/Mountain/P2 - Retour au glacis d'origine (sculpt, recalage, NavMesh, P1)")]
        public static void RestoreGlacis() => Run(DefenseReliefConfig.PlateauEdgeMode.Glacis);

        [MenuItem("Tools/Defense/Mountain/P2 - Rapport du bord du plateau")]
        public static void ReportOnly()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            Debug.Log(MountainP2Report.Build(config, DefenseReliefScene.Terrain, DefenseReliefScene.HersePosition));
        }

        private static void Run(DefenseReliefConfig.PlateauEdgeMode mode)
        {
            try
            {
                DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
                config.Inverted.edgeMode = mode;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("Mountain P2", "Sculpt du relief", 0.1f);
                DefenseReliefSculptTool.Sculpt();

                EditorUtility.DisplayProgressBar("Mountain P2", "Recalage des objets et splines", 0.4f);
                DefenseReliefAlignTool.Align();
                DefenseReliefGroundTool.SnapOnly();

                EditorUtility.DisplayProgressBar("Mountain P2", "Rebake du NavMesh", 0.6f);
                DefenseReliefPaintTool.RebakeNavMesh();

                EditorUtility.DisplayProgressBar("Mountain P2", "Peinture et vegetation (P1)", 0.8f);
                MountainP1Menu.Apply();

                Debug.Log(MountainP2Report.Build(config, DefenseReliefScene.Terrain, DefenseReliefScene.HersePosition));
            }
            catch (Exception exception)
            {
                Debug.LogError("[Mountain P2] interrompu : " + exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
