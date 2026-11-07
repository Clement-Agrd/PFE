using System;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// P1 : peinture de roche + limite des arbres/herbe. Idempotent : repart toujours
    /// du snapshot de BASE (alphamaps, arbres, details). Undo disponible.
    /// Ne modifie ni la heightmap ni les objets de la scene.
    /// </summary>
    public static class MountainP1Menu
    {
        [MenuItem("Tools/Defense/Mountain/P1 - Appliquer roche et vegetation")]
        public static void Apply()
        {
            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Mountain] P1 interrompu : " + exception.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/Defense/Mountain/P1 - Restaurer la BASE")]
        public static void Restore()
        {
            MountainProfile profile = MountainProfile.LoadOrCreate();
            if (profile.BaseSnapshot == null)
            {
                Debug.LogWarning("[Mountain] Aucune BASE enregistree : rien a restaurer.");
                return;
            }
            Terrain terrain = DefenseReliefScene.Terrain;
            MountainSnapshot.RestoreBase(terrain.terrainData, profile.BaseSnapshot);
            terrain.Flush();
            AssetDatabase.SaveAssets();
            Debug.Log("[Mountain] Terrain restaure depuis la BASE de P1.");
        }

        private static void Run()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            MountainProfile profile = MountainProfile.LoadOrCreate();
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData live = terrain.terrainData;

            EditorUtility.DisplayProgressBar("Mountain P1", "Base et lanes", 0.05f);
            TerrainData baseData = MountainSnapshot.EnsureBase(profile, live);
            ReliefField field = DefenseReliefScene.BuildField(config);
            Vector2 herse = DefenseReliefScene.HersePosition;

            EditorUtility.DisplayProgressBar("Mountain P1", "Zonage", 0.25f);
            var maps = new ZoningMaps(live, terrain.transform.position);
            MountainProtectionBuilder.Fill(maps, profile, field, herse);
            MountainZoningBuilder.Fill(maps, profile, live, herse);

            Undo.RegisterCompleteObjectUndo(live, "Mountain P1 : roche et vegetation");

            EditorUtility.DisplayProgressBar("Mountain P1", "Peinture", 0.55f);
            MountainAlphaPainter.Stats paint = MountainAlphaPainter.Apply(maps, profile, live, baseData);

            EditorUtility.DisplayProgressBar("Mountain P1", "Arbres et herbe", 0.8f);
            MountainVegetationPass.Stats veg = MountainVegetationPass.Apply(maps, profile, live, baseData);

            EditorUtility.SetDirty(live);
            terrain.Flush();
            AssetDatabase.SaveAssets();
            Report(maps, paint, veg);
        }

        private static void Report(ZoningMaps maps, MountainAlphaPainter.Stats paint, MountainVegetationPass.Stats veg)
        {
            AltitudeSet a = maps.Altitudes;
            Debug.Log(
                "[Mountain] P1 termine.\n" +
                $"Seuils (m rel. vallee) : arbres clairsemes {a.treeThinStart:F1} / aucun {a.treeNone:F1} / eboulis {a.screeStart:F1} / " +
                $"falaise {a.cliffStart:F1} / sommet {a.summitStart:F1} / givre {a.frostStart:F1}\n" +
                $"Surface terrain : roche {paint.rockPercent:F1}% | eboulis {paint.screePercent:F1}% | givre {paint.frostPercent:F2}% | cellules modifiees {paint.changedCells}\n" +
                $"Arbres : {veg.treesBefore} -> {veg.treesAfter} (restants a < 4 m d'une lane : {veg.treesNearLane})\n" +
                $"Herbe (somme des densites) : {veg.grassBefore} -> {veg.grassAfter}");
        }
    }
}
