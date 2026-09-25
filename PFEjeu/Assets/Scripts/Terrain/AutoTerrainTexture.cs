using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class AutoTerrainTexture : MonoBehaviour
{
    // =========================================================
    // HERBE
    // =========================================================

    [Header("Herbe - Motifs")]
    [Tooltip("Plus grand = motifs plus petits et plus nombreux.")]
    public float grassNoiseScale = 8f;

    [Tooltip("Plus grand = chaque type d'herbe domine davantage sa zone.")]
    public float grassContrast = 3f;

    [Tooltip("Permet de changer la disposition des motifs.")]
    public float grassNoiseSeed = 1234f;


    // =========================================================
    // TERRE
    // =========================================================

    [Header("Terre - Pentes légères")]

    [Tooltip("Pente à laquelle la terre commence à apparaître.")]
    public float dirtStartSlope = 5f;

    [Tooltip("Pente où la terre est la plus présente.")]
    public float dirtPeakSlope = 18f;

    [Tooltip("Pente à laquelle la terre disparaît pour laisser place à la roche.")]
    public float dirtEndSlope = 32f;

    [Range(0f, 1f)]
    [Tooltip("Quantité maximale de terre.")]
    public float dirtStrength = 0.8f;


    // =========================================================
    // ROCHE
    // =========================================================

    [Header("Roche - Pente")]

    [Tooltip("Début d'apparition de la roche.")]
    public float rockStartSlope = 30f;

    [Tooltip("À partir de cette pente, la roche est dominante.")]
    public float rockFullSlope = 45f;


    // =========================================================
    // VARIATIONS ROCHE
    // =========================================================

    [Header("Roche - Variations")]

    [Tooltip("Plus grand = changement d'orientation plus fréquent.")]
    public float rockVariationScale = 4f;

    [Tooltip("Plus grand = séparation plus forte entre les variantes de roche.")]
    public float rockVariationContrast = 4f;

    [Tooltip("Change la disposition des variantes.")]
    public float rockVariationSeed = 5432f;


    // =========================================================
    // NEIGE
    // =========================================================

    [Header("Neige - Hauteur")]

    [Tooltip("Hauteur à laquelle la neige commence.")]
    public float snowStartHeight = 60f;

    [Tooltip("Hauteur où la neige devient dominante.")]
    public float snowFullHeight = 80f;


    // =========================================================
    // GENERATION
    // =========================================================

    [ContextMenu("Appliquer les textures automatiques")]
    public void ApplyTextures()
    {
        Terrain terrain = GetComponent<Terrain>();

        if (terrain == null)
        {
            Debug.LogError("Aucun Terrain trouvé.");
            return;
        }

        TerrainData data = terrain.terrainData;


        // =====================================================
        // VERIFICATIONS
        // =====================================================

        if (data.alphamapLayers < 9)
        {
            Debug.LogError(
                "Il faut au minimum 9 Terrain Layers.\n\n" +

                "Ordre attendu :\n" +
                "0 = Herbe 1\n" +
                "1 = Herbe 2\n" +
                "2 = Herbe 3\n" +
                "3 = Terre\n" +
                "4 = Roche 0°\n" +
                "5 = Roche 90°\n" +
                "6 = Roche 180°\n" +
                "7 = Roche 270°\n" +
                "8 = Neige"
            );

            return;
        }


        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        int layerCount = data.alphamapLayers;


        // Tableau contenant les poids de chaque texture
        //
        // map[y, x, layer]

        float[,,] map =
            new float[height, width, layerCount];


        // =====================================================
        // PARCOURS DU TERRAIN
        // =====================================================

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // -------------------------------------------------
                // Coordonnées normalisées 0 -> 1
                // -------------------------------------------------

                float nx =
                    (float)x / (width - 1);

                float ny =
                    (float)y / (height - 1);


                // -------------------------------------------------
                // Hauteur
                // -------------------------------------------------

                float terrainHeight =
                    data.GetInterpolatedHeight(
                        nx,
                        ny
                    );


                // -------------------------------------------------
                // Pente
                // -------------------------------------------------

                float slope =
                    data.GetSteepness(
                        nx,
                        ny
                    );


                // =================================================
                // ROCHE
                // =================================================

                float rock =
                    Mathf.InverseLerp(
                        rockStartSlope,
                        rockFullSlope,
                        slope
                    );

                rock =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        rock
                    );


                // =================================================
                // NEIGE
                // =================================================

                float snow =
                    Mathf.InverseLerp(
                        snowStartHeight,
                        snowFullHeight,
                        terrainHeight
                    );

                snow =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        snow
                    );


                // Moins de neige sur les falaises
                snow *= 1f - rock;


                // =================================================
                // TERRE
                // =================================================

                // Apparition progressive de la terre

                float dirtGoingUp =
                    Mathf.InverseLerp(
                        dirtStartSlope,
                        dirtPeakSlope,
                        slope
                    );


                // Disparition progressive de la terre

                float dirtGoingDown =
                    1f -
                    Mathf.InverseLerp(
                        dirtPeakSlope,
                        dirtEndSlope,
                        slope
                    );


                // On prend la plus petite des deux valeurs
                // pour former une sorte de "montagne"
                // d'intensité.

                float dirt =
                    Mathf.Min(
                        dirtGoingUp,
                        dirtGoingDown
                    );


                dirt =
                    Mathf.Clamp01(dirt);


                dirt =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        dirt
                    );


                dirt *= dirtStrength;


                // La terre disparaît là où
                // roche/neige dominent.

                dirt *= 1f - rock;
                dirt *= 1f - snow;


                // =================================================
                // QUANTITE TOTALE D'HERBE
                // =================================================

                float grassAmount =
                    Mathf.Clamp01(
                        1f -
                        rock -
                        snow -
                        dirt
                    );


                // =================================================
                // MOTIFS D'HERBE
                // =================================================

                float grassNoiseX =
                    nx * grassNoiseScale;

                float grassNoiseY =
                    ny * grassNoiseScale;


                // -------------------------------------------------
                // Herbe 1
                // -------------------------------------------------

                float grassNoise1 =
                    Mathf.PerlinNoise(
                        grassNoiseX + grassNoiseSeed,
                        grassNoiseY + grassNoiseSeed
                    );


                // -------------------------------------------------
                // Herbe 2
                // -------------------------------------------------

                float grassNoise2 =
                    Mathf.PerlinNoise(
                        grassNoiseX + grassNoiseSeed + 100f,
                        grassNoiseY + grassNoiseSeed + 300f
                    );


                // -------------------------------------------------
                // Herbe 3
                // -------------------------------------------------

                float grassNoise3 =
                    Mathf.PerlinNoise(
                        grassNoiseX + grassNoiseSeed + 500f,
                        grassNoiseY + grassNoiseSeed + 700f
                    );


                // Accentuer les différences

                grassNoise1 =
                    Mathf.Pow(
                        grassNoise1,
                        grassContrast
                    );

                grassNoise2 =
                    Mathf.Pow(
                        grassNoise2,
                        grassContrast
                    );

                grassNoise3 =
                    Mathf.Pow(
                        grassNoise3,
                        grassContrast
                    );


                // -------------------------------------------------
                // Normalisation
                // -------------------------------------------------

                float totalGrassNoise =
                    grassNoise1 +
                    grassNoise2 +
                    grassNoise3;


                if (totalGrassNoise > 0f)
                {
                    grassNoise1 /= totalGrassNoise;
                    grassNoise2 /= totalGrassNoise;
                    grassNoise3 /= totalGrassNoise;
                }


                // -------------------------------------------------
                // Quantités finales d'herbe
                // -------------------------------------------------

                float grass1 =
                    grassNoise1 *
                    grassAmount;

                float grass2 =
                    grassNoise2 *
                    grassAmount;

                float grass3 =
                    grassNoise3 *
                    grassAmount;


                // =================================================
                // VARIATIONS DE ROCHE
                // =================================================

                float rockNoiseX =
                    nx * rockVariationScale;

                float rockNoiseY =
                    ny * rockVariationScale;


                // -------------------------------------------------
                // Variante 0°
                // -------------------------------------------------

                float rockNoise0 =
                    Mathf.PerlinNoise(
                        rockNoiseX + rockVariationSeed,
                        rockNoiseY + rockVariationSeed
                    );


                // -------------------------------------------------
                // Variante 90°
                // -------------------------------------------------

                float rockNoise90 =
                    Mathf.PerlinNoise(
                        rockNoiseX + rockVariationSeed + 100f,
                        rockNoiseY + rockVariationSeed + 200f
                    );


                // -------------------------------------------------
                // Variante 180°
                // -------------------------------------------------

                float rockNoise180 =
                    Mathf.PerlinNoise(
                        rockNoiseX + rockVariationSeed + 300f,
                        rockNoiseY + rockVariationSeed + 400f
                    );


                // -------------------------------------------------
                // Variante 270°
                // -------------------------------------------------

                float rockNoise270 =
                    Mathf.PerlinNoise(
                        rockNoiseX + rockVariationSeed + 500f,
                        rockNoiseY + rockVariationSeed + 600f
                    );


                // Accentuer les zones

                rockNoise0 =
                    Mathf.Pow(
                        rockNoise0,
                        rockVariationContrast
                    );

                rockNoise90 =
                    Mathf.Pow(
                        rockNoise90,
                        rockVariationContrast
                    );

                rockNoise180 =
                    Mathf.Pow(
                        rockNoise180,
                        rockVariationContrast
                    );

                rockNoise270 =
                    Mathf.Pow(
                        rockNoise270,
                        rockVariationContrast
                    );


                // -------------------------------------------------
                // Normalisation
                // -------------------------------------------------

                float totalRockNoise =
                    rockNoise0 +
                    rockNoise90 +
                    rockNoise180 +
                    rockNoise270;


                if (totalRockNoise > 0f)
                {
                    rockNoise0 /= totalRockNoise;
                    rockNoise90 /= totalRockNoise;
                    rockNoise180 /= totalRockNoise;
                    rockNoise270 /= totalRockNoise;
                }


                // -------------------------------------------------
                // Quantité finale de chaque roche
                // -------------------------------------------------

                float rock0 =
                    rock *
                    rockNoise0;

                float rock90 =
                    rock *
                    rockNoise90;

                float rock180 =
                    rock *
                    rockNoise180;

                float rock270 =
                    rock *
                    rockNoise270;


                // =================================================
                // APPLICATION DANS L'ALPHAMAP
                // =================================================

                // Herbes

                map[y, x, 0] = grass1;
                map[y, x, 1] = grass2;
                map[y, x, 2] = grass3;


                // Terre

                map[y, x, 3] = dirt;


                // Roches

                map[y, x, 4] = rock0;
                map[y, x, 5] = rock90;
                map[y, x, 6] = rock180;
                map[y, x, 7] = rock270;


                // Neige

                map[y, x, 8] = snow;
            }
        }


        // =====================================================
        // ENVOYER LES TEXTURES AU TERRAIN
        // =====================================================

        data.SetAlphamaps(
            0,
            0,
            map
        );


        Debug.Log(
            "Textures automatiques appliquées !"
        );
    }
}