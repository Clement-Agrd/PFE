using UnityEditor;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Calcule la heightmap cible a partir du backup et l'applique au terrain.
    /// Idempotent : la base est toujours le TerrainData de backup, jamais l'etat courant.
    /// </summary>
    public static class DefenseReliefSculptTool
    {
        /// <summary>
        /// Point d'extension : une fabrique retourne (x, z, y) -> y pour modifier la hauteur finale
        /// (ex. cretes lointaines hors zone de jeu). Null ou retour null = sculpt d'origine.
        /// </summary>
        public static System.Func<System.Func<float, float, float, float>> OuterModifierFactory;

        /// <summary>
        /// Point d'extension : traitement de la heightmap complete une fois calculee (acces aux voisins,
        /// donc a la pente). Arguments : hauteurs normalisees [j, i], origine monde, taille du terrain.
        /// </summary>
        public static System.Action<float[,], Vector3, Vector3> HeightmapPostProcessor;

        [MenuItem("Tools/Defense/Relief/2 - Sculpter le relief")]
        public static void Sculpt()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            ReliefField field = DefenseReliefScene.BuildField(config);
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;

            int res = config.TargetHeightmapResolution;
            Vector3 size = config.BaseTerrainData.size;
            float[,] target = ComputeHeights(config, field, size, terrain.transform.position, res);

            Undo.RegisterCompleteObjectUndo(data, "Defense Relief : sculpt");
            SetResolutionKeepingSize(data, res, size);
            data.SetHeights(0, 0, target);
            terrain.Flush();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] Relief applique (resolution " + res + ").");
        }

        [MenuItem("Tools/Defense/Relief/Restaurer le terrain d'origine")]
        public static void RestoreBase()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            TerrainData backup = config.BaseTerrainData;
            TerrainData data = DefenseReliefScene.Terrain.terrainData;
            int res = backup.heightmapResolution;

            Undo.RegisterCompleteObjectUndo(data, "Defense Relief : restore");
            SetResolutionKeepingSize(data, res, backup.size);
            data.SetHeights(0, 0, backup.GetHeights(0, 0, res, res));
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[DefenseRelief] Heightmap d'origine restauree.");
        }

        /// <summary>
        /// Changer heightmapResolution conserve l'echelle par pixel et fait donc
        /// varier la taille du terrain : on la restaure explicitement.
        /// </summary>
        private static void SetResolutionKeepingSize(TerrainData data, int res, Vector3 size)
        {
            if (data.heightmapResolution != res) data.heightmapResolution = res;
            data.size = size;
        }

        private static float[,] ComputeHeights(
            DefenseReliefConfig config, ReliefField field, Vector3 size, Vector3 origin, int res)
        {
            var heights = new float[res, res];
            System.Func<float, float, float, float> outerModifier = OuterModifierFactory?.Invoke();
            var protect = new float[res, res];
            var world = new float[res, res];
            float stepX = size.x / (res - 1);
            float stepZ = size.z / (res - 1);

            for (int j = 0; j < res; j++)
            {
                float z = origin.z + j * stepZ;
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + i * stepX;
                    world[j, i] = field.Evaluate(x, z, out protect[j, i]);
                }
            }

            float[,] smoothed = world;
            for (int pass = 0; pass < config.SmoothingPasses; pass++) smoothed = BoxBlur(smoothed, res, field.Bounds, origin, stepX, stepZ);

            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + i * stepX;
                    float z = origin.z + j * stepZ;
                    float baseY = field.BaseHeight(x, z);
                    float y = Mathf.Lerp(smoothed[j, i], world[j, i], protect[j, i]);
                    // Le lissage ne doit pas alterer le relief naturel : poids selon l'ecart au terrain d'origine.
                    y = Mathf.Lerp(baseY, y, ReliefField.Smooth(0f, 0.3f, Mathf.Abs(world[j, i] - baseY)));
                    if (outerModifier != null) y = outerModifier(x, z, y);
                    heights[j, i] = Mathf.Clamp01((y - origin.y) / size.y);
                }
            }
            HeightmapPostProcessor?.Invoke(heights, origin, size);
            return heights;
        }

        private static float[,] BoxBlur(float[,] src, int res, Rect bounds, Vector3 origin, float stepX, float stepZ)
        {
            var dst = (float[,])src.Clone();
            int i0 = Mathf.Max(1, Mathf.FloorToInt((bounds.xMin - origin.x) / stepX));
            int i1 = Mathf.Min(res - 2, Mathf.CeilToInt((bounds.xMax - origin.x) / stepX));
            int j0 = Mathf.Max(1, Mathf.FloorToInt((bounds.yMin - origin.z) / stepZ));
            int j1 = Mathf.Min(res - 2, Mathf.CeilToInt((bounds.yMax - origin.z) / stepZ));

            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    float sum = 0f;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                            sum += src[j + dj, i + di];
                    dst[j, i] = sum / 9f;
                }
            }
            return dst;
        }
    }
}
