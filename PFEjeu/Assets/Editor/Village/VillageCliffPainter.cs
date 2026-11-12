using System.Collections.Generic;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Peint les parois du village dans la zone terrassee uniquement : roche sur les murs (variante choisie
    /// selon l'orientation de la paroi), eboulis terreux au pied, terre fine sous la crete, herbe ailleurs.
    /// Le seuil de roche est perturbe par du bruit pour que la limite ne soit jamais une ligne de niveau nette.
    /// Retire aussi herbe en detail et arbres sur les parois. Le chemin de defense n'est pas touche.
    /// Ordre des couches (AutoTerrainTexture) : 0-2 herbe, 3 terre, 4-7 roche 0/90/180/270, 8 neige.
    /// </summary>
    public static class VillageCliffPainter
    {
        /// <summary>Masque cote village (muraille) : tout ce qui est cote defense est laisse tel quel.</summary>
        private static VillageSide _side;

        private const int DirtLayer = 3;
        private const int RockFirstLayer = 4;
        private const int RockVariants = 4;
        private const string PathLayerName = "Defense_Dirt";

        private const float RockStartDegrees = 36f;
        private const float RockFullDegrees = 56f;
        private const float RockThresholdJitter = 7f;
        private const float RockNoiseScale = 9f;

        private const float ScreeStartDegrees = 14f;
        private const float ScreePeakDegrees = 26f;
        private const float ScreeStrength = 0.75f;
        private const float DetailClearDegrees = 44f;
        private const float TreeClearDegrees = 34f;
        private const float PathKeepWeight = 0.3f;
        private const string FlatBackupPath = "Assets/Scenes/GA/Backups/SceneJeux_DefenseTerrain_userFlatTRUE.asset";
        private const float MountainMinAboveFloor = 1.5f;
        private const float ValleyDepth = 1.5f;
        private const float NaturalRockStartDegrees = 30f;
        private const float NaturalRockFullDegrees = 45f;

        [MenuItem("Tools/Defense/Relief/Village/Peindre les parois (zone village)")]
        public static void Paint()
        {
            VillageLayoutProfile profile = VillageLayoutProfile.LoadOrCreate();
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            if (data.alphamapLayers < RockFirstLayer + RockVariants)
            {
                Debug.LogError("[VillageLayout] Il faut au moins 8 TerrainLayers (ordre AutoTerrainTexture).");
                return;
            }

            _side = new VillageSide(6f, 14f);
            Undo.RegisterCompleteObjectUndo(data, "Village : peindre les parois");
            int painted = PaintAlphamaps(profile, terrain, data);
            int details = ClearDetails(profile, terrain, data);
            int trees = ClearTrees(profile, terrain, data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[VillageLayout] Parois peintes : " + painted + " pixels de texture, " + details +
                      " cellules d'herbe retirees, " + trees + " arbres retires.");
        }

        #region Texture

        private static int PaintAlphamaps(VillageLayoutProfile profile, Terrain terrain, TerrainData data)
        {
            int res = data.alphamapResolution;
            int layers = data.alphamapLayers;
            float[,,] map = data.GetAlphamaps(0, 0, res, res);
            int pathLayer = FindLayer(data, PathLayerName);
            Vector3 origin = terrain.transform.position;
            // Pentes d'origine (avant etages) : la montagne naturelle garde sa roche.
            TerrainData original = AssetDatabase.LoadAssetAtPath<TerrainData>(FlatBackupPath);
            if (original != null && original.heightmapResolution != data.heightmapResolution) original = null;
            float[,,] originalMap = original != null && original.alphamapResolution == res && original.alphamapLayers == layers
                ? original.GetAlphamaps(0, 0, res, res) : null;
            int count = 0;

            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float u = (float)i / (res - 1);
                    float v = (float)j / (res - 1);
                    float worldX = origin.x + u * data.size.x;
                    float worldZ = origin.z + v * data.size.z;
                    float zone = Zone(profile, worldX, worldZ);
                    if (zone <= 0f) continue;
                    zone *= _side.Weight(worldX, worldZ);
                    if (zone <= 0f) continue;
                    if (IsDefenseValley(original, profile, origin, u, v))
                    {
                        // Hors village (vallee de defense, plus basse que le plat) : texture d'origine, jamais repeinte.
                        if (originalMap != null)
                            for (int l = 0; l < layers; l++) map[j, i, l] = originalMap[j, i, l];
                        continue;
                    }

                    if (pathLayer >= 0 && map[j, i, pathLayer] > PathKeepWeight) continue;

                    float slope = data.GetSteepness(u, v);
                    float jitter = (Mathf.PerlinNoise(worldX / RockNoiseScale + 41.3f, worldZ / RockNoiseScale + 17.9f) - 0.5f)
                                   * 2f * RockThresholdJitter;
                    float rock = Smooth(Mathf.InverseLerp(RockStartDegrees + jitter, RockFullDegrees + jitter, slope));
                    float scree = Mathf.Min(
                        Mathf.InverseLerp(ScreeStartDegrees, ScreePeakDegrees, slope),
                        1f - Mathf.InverseLerp(ScreePeakDegrees, RockFullDegrees, slope));
                    // Seulement la vraie montagne : les restes de murs de l'ancien plat (au niveau du sol) sont ignores.
                    if (original != null && original.GetInterpolatedHeight(u, v) + origin.y > profile.GroundFloorY + MountainMinAboveFloor)
                        rock = Mathf.Max(rock, Smooth(Mathf.InverseLerp(NaturalRockStartDegrees, NaturalRockFullDegrees, Mathf.Max(slope, original.GetSteepness(u, v)))));
                    scree = Smooth(Mathf.Clamp01(scree)) * ScreeStrength * (1f - rock);

                    rock *= zone;
                    scree *= zone;
                    // L'ancien eboulis (couche terre) est retire dans la zone avant d'etre repeint selon la pente.
                    map[j, i, DirtLayer] *= 1f - zone;
                    float keep = Mathf.Clamp01(1f - rock - scree);
                    float[] rockMix = RockVariantWeights(data.GetInterpolatedNormal(u, v));
                    float rockSum = 0f;
                    for (int l = 0; l < layers; l++)
                    {
                        float w = map[j, i, l];
                        if (l >= RockFirstLayer && l < RockFirstLayer + RockVariants) w = 0f;
                        else if (l == DirtLayer) w = w * keep + scree;
                        else w *= keep;
                        map[j, i, l] = w;
                        rockSum += w;
                    }

                    for (int variant = 0; variant < RockVariants; variant++)
                    {
                        map[j, i, RockFirstLayer + variant] += rock * rockMix[variant];
                        rockSum += rock * rockMix[variant];
                    }

                    if (rockSum > 1f)
                    {
                        for (int l = 0; l < layers; l++) map[j, i, l] /= rockSum;
                    }
                    else if (rockSum < 1f)
                    {
                        // Poids manquant (ancienne roche retiree) : comble avec de l'herbe, jamais de pixel vide.
                        float pick = Mathf.PerlinNoise(worldX / 6f + 7.7f, worldZ / 6f + 3.1f) * 3f;
                        int grass = Mathf.Min(2, (int)pick);
                        map[j, i, grass] += 1f - rockSum;
                    }
                    count++;
                }
            }

            data.SetAlphamaps(0, 0, map);
            return count;
        }

        /// <summary>Poids des 4 variantes de roche (0/90/180/270 deg) selon la direction vers laquelle fait face la paroi.</summary>
        private static float[] RockVariantWeights(Vector3 normal)
        {
            float angle = Mathf.Atan2(normal.x, normal.z);
            var weights = new float[RockVariants];
            float total = 0f;
            for (int k = 0; k < RockVariants; k++)
            {
                float target = k * Mathf.PI * 0.5f;
                float w = Mathf.Max(0f, Mathf.Cos(angle - target));
                w *= w;
                weights[k] = w;
                total += w;
            }

            for (int k = 0; k < RockVariants; k++) weights[k] = total > 0f ? weights[k] / total : 1f / RockVariants;
            return weights;
        }

        #endregion

        #region Details et arbres

        private static int ClearDetails(VillageLayoutProfile profile, Terrain terrain, TerrainData data)
        {
            int res = data.detailResolution;
            Vector3 origin = terrain.transform.position;
            TerrainData original = AssetDatabase.LoadAssetAtPath<TerrainData>(FlatBackupPath);
            if (original != null && (original.detailResolution != res || original.detailPrototypes.Length != data.detailPrototypes.Length)) original = null;
            int cleared = 0;
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                int[,] grid = data.GetDetailLayer(0, 0, res, res, layer);
                int[,] originalGrid = original != null ? original.GetDetailLayer(0, 0, res, res, layer) : null;
                bool changed = false;
                for (int j = 0; j < res; j++)
                {
                    for (int i = 0; i < res; i++)
                    {
                        float u = (float)i / (res - 1);
                        float v = (float)j / (res - 1);
                        if (originalGrid != null && grid[j, i] != originalGrid[j, i] &&
                            Zone(profile, origin.x + u * data.size.x, origin.z + v * data.size.z) > 0f &&
                            IsDefenseValley(original, profile, origin, u, v))
                        {
                            grid[j, i] = originalGrid[j, i];
                            changed = true;
                            continue;
                        }

                        if (grid[j, i] == 0) continue;
                        if (Zone(profile, origin.x + u * data.size.x, origin.z + v * data.size.z) < 0.5f) continue;
                        if (data.GetSteepness(u, v) < DetailClearDegrees) continue;
                        grid[j, i] = 0;
                        changed = true;
                        cleared++;
                    }
                }

                if (changed) data.SetDetailLayer(0, 0, layer, grid);
            }

            return cleared;
        }

        private static int ClearTrees(VillageLayoutProfile profile, Terrain terrain, TerrainData data)
        {
            Vector3 origin = terrain.transform.position;
            TreeInstance[] trees = data.treeInstances;
            TerrainData original = AssetDatabase.LoadAssetAtPath<TerrainData>(FlatBackupPath);
            var kept = new List<TreeInstance>(trees.Length);
            int restored = 0;
            // Vallee de defense : on repart des arbres d'origine (ceux retires par une passe precedente reviennent).
            if (original != null)
            {
                foreach (TreeInstance tree in original.treeInstances)
                {
                    float worldX = origin.x + tree.position.x * data.size.x;
                    float worldZ = origin.z + tree.position.z * data.size.z;
                    if (Zone(profile, worldX, worldZ) > 0f && IsDefenseValley(original, profile, origin, tree.position.x, tree.position.z))
                    {
                        kept.Add(tree);
                        restored++;
                    }
                }
            }

            int removedBefore = 0;
            foreach (TreeInstance tree in trees)
            {
                float worldX = origin.x + tree.position.x * data.size.x;
                float worldZ = origin.z + tree.position.z * data.size.z;
                bool inVillage = Zone(profile, worldX, worldZ) > 0f && !IsDefenseValley(original, profile, origin, tree.position.x, tree.position.z);
                bool onWall = inVillage && Zone(profile, worldX, worldZ) >= 0.5f &&
                              data.GetSteepness(tree.position.x, tree.position.z) >= TreeClearDegrees;
                bool inValley = original != null && Zone(profile, worldX, worldZ) > 0f && IsDefenseValley(original, profile, origin, tree.position.x, tree.position.z);
                if (inValley) continue;
                if (!onWall) kept.Add(tree);
                else removedBefore++;
            }

            data.SetTreeInstances(kept.ToArray(), true);
            Debug.Log("[VillageLayout] Arbres : " + removedBefore + " retires des parois, " + restored + " de la vallee de defense restaures depuis la sauvegarde.");
            return removedBefore;
        }

        #endregion

        #region Utilitaires

        /// <summary>Vrai ou le terrain d'origine est nettement sous le niveau bas du village : c'est la vallee de defense.</summary>
        private static bool IsDefenseValley(TerrainData original, VillageLayoutProfile profile, Vector3 origin, float u, float v)
        {
            return _side != null && !(_side.Weight(origin.x + u * original.size.x, origin.z + v * original.size.z) > 0f);
        }

        private static float Zone(VillageLayoutProfile profile, float worldX, float worldZ)
        {
            float dx = worldX - profile.ZoneCenter.x;
            float dz = worldZ - profile.ZoneCenter.y;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            return 1f - Smooth(Mathf.InverseLerp(profile.ZoneRadius, profile.ZoneRadius + profile.ZoneFade, distance));
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static int FindLayer(TerrainData data, string layerName)
        {
            TerrainLayer[] layers = data.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && layers[i].name == layerName) return i;
            return -1;
        }

        #endregion
    }
}
