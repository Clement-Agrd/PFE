using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Menus du level design vertical du village. Le sculpt passe toujours par l'outil du relief
    /// (repart de la BASE, Undo sur le TerrainData) ; ce menu ne fait qu'enchainer les etapes.
    /// </summary>
    public static class VillageSculptMenu
    {
        private const string MenuRoot = "Tools/Defense/Relief/Village/";

        [MenuItem(MenuRoot + "0 - Capturer l'ecart manuel (une seule fois)")]
        public static void CaptureManualDelta()
        {
            if (VillageManualDelta.Exists)
            {
                Debug.LogError("[VillageLayout] Ecart manuel deja capture (" + VillageManualDelta.AssetPath +
                               "). Le recapturer apres un sculpt du village figerait les etages dans l'ecart : operation refusee.");
                return;
            }

            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            TerrainData data = DefenseReliefScene.Terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] current = data.GetHeights(0, 0, res, res);
            float[,] sculpted = DefenseReliefSculptTool.ComputeTargetHeights(config, false);
            int count = VillageManualDelta.Save(current, sculpted);
            Debug.Log("[VillageLayout] Ecart manuel capture : " + count + " echantillons differents du sculpt de BASE.");
        }

        [MenuItem(MenuRoot + "1 - Sculpter les etages (relief complet depuis la BASE)")]
        public static void SculptTerraces()
        {
            if (!VillageManualDelta.Exists)
            {
                Debug.LogError("[VillageLayout] Capturer d'abord l'ecart manuel (menu 0) : sans lui, la butte sculptee a la main serait perdue.");
                return;
            }

            VillageLayoutProfile.LoadOrCreate();
            DefenseReliefSculptTool.Sculpt();
            Debug.Log(VillageTerraceReport.Build(VillageLayoutProfile.LoadOrCreate()));
        }

        [MenuItem(MenuRoot + "Rapport des etages")]
        public static void Report() => Debug.Log(VillageTerraceReport.Build(VillageLayoutProfile.LoadOrCreate()));
    }
}
