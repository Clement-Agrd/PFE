using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class TerrainEnvironmentGenerator : MonoBehaviour
{
    // ========================================================================
    // GENERAL
    // ========================================================================

    [Header("Général")]

    [Tooltip("Même seed = même génération.")]
    [SerializeField] private int seed = 12345;


    // ========================================================================
    // TERRAIN LAYERS
    // ========================================================================

    [Header("Indices des Terrain Layers")]

    [Tooltip("Herbe 1 / Herbe 2 / Herbe 3")]
    [SerializeField] private int[] grassTextureLayers = { 0, 1, 2 };

    [Tooltip("Terre")]
    [SerializeField] private int dirtTextureLayer = 3;

    [Tooltip("Les différentes variantes de roche.")]
    [SerializeField] private int[] rockTextureLayers = { 4, 5, 6, 7 };

    [Tooltip("Neige")]
    [SerializeField] private int snowTextureLayer = 8;


    // ========================================================================
    // DETAIL PROTOTYPES
    // ========================================================================

    [Header("Terrain Details - indices")]

    [Tooltip("Indices des Detail Prototypes utilisés pour les herbes.")]
    [SerializeField] private int[] grassDetailPrototypes = { 0 };

    [Tooltip("Indices des Detail Prototypes utilisés pour les fleurs.")]
    [SerializeField] private int[] flowerDetailPrototypes = { 1 };

    [Tooltip("Indices des Detail Prototypes utilisés pour les branches.")]
    [SerializeField] private int[] branchDetailPrototypes = { 2 };

    [Tooltip("Indices des Detail Prototypes utilisés pour les petits cailloux.")]
    [SerializeField] private int[] smallRockDetailPrototypes = { 3 };


    // ========================================================================
    // HERBE
    // ========================================================================

    [Header("Herbe")]

    [Min(0)]
    [SerializeField] private int grassMaxDensity = 12;

    [Tooltip("Taille des variations de densité.")]
    [Min(0.01f)]
    [SerializeField] private float grassNoiseScale = 15f;

    [Tooltip("Pente maximale pour l'herbe.")]
    [Range(0f, 90f)]
    [SerializeField] private float grassMaxSlope = 35f;

    [Tooltip("Quantité minimale de texture herbe pour autoriser l'herbe 3D.")]
    [Range(0f, 1f)]
    [SerializeField] private float grassSurfaceThreshold = 0.15f;


    // ========================================================================
    // FLEURS
    // ========================================================================

    [Header("Fleurs")]

    [Min(0)]
    [SerializeField] private int flowerMaxDensity = 3;

    [Min(0.01f)]
    [SerializeField] private float flowerPatchScale = 7f;

    [Tooltip("Plus grand = fleurs plus rares.")]
    [Range(0f, 1f)]
    [SerializeField] private float flowerPatchThreshold = 0.68f;

    [Range(0f, 90f)]
    [SerializeField] private float flowerMaxSlope = 25f;

    [Range(0f, 1f)]
    [SerializeField] private float flowerMinGrass = 0.45f;
    
    [Header("Variantes fleurs")]

    [SerializeField]
    private float flowerColorVariationScale = 35f;


    // ========================================================================
    // BRANCHES
    // ========================================================================

    [Header("Branches")]

    [Min(0)]
    [SerializeField] private int branchMaxDensity = 2;

    [Min(0.01f)]
    [SerializeField] private float branchNoiseScale = 8f;

    [Range(0f, 1f)]
    [SerializeField] private float branchThreshold = 0.62f;

    [Range(0f, 90f)]
    [SerializeField] private float branchMaxSlope = 32f;

    [Tooltip("Les branches apparaissent davantage dans les zones de forêt.")]
    [Range(0f, 2f)]
    [SerializeField] private float branchForestInfluence = 1.2f;


    // ========================================================================
    // PETITS CAILLOUX
    // ========================================================================

    [Header("Petits cailloux")]

    [Min(0)]
    [SerializeField] private int smallRockMaxDensity = 3;

    [Min(0.01f)]
    [SerializeField] private float smallRockNoiseScale = 11f;

    [Range(0f, 1f)]
    [SerializeField] private float smallRockThreshold = 0.58f;

    [Range(0f, 90f)]
    [SerializeField] private float smallRockMaxSlope = 55f;


    // ========================================================================
    // FORET
    // ========================================================================

    [Header("Forêt")]

    [Tooltip("Taille des grandes zones forestières.")]
    [Min(0.01f)]
    [SerializeField] private float forestNoiseScale = 3.5f;

    [Tooltip("Plus haut = forêt plus rare.")]
    [Range(0f, 1f)]
    [SerializeField] private float forestThreshold = 0.56f;


    // ========================================================================
    // ARBRES
    // ========================================================================

    [Header("Arbres")]

    [Tooltip("Indices dans la liste Tree Prototypes du Terrain.")]
    [SerializeField] private int[] treePrototypeIndices = { 0 };

    [Min(0)]
    [SerializeField] private int targetTreeCount = 500;

    [Tooltip("Distance minimale approximative entre deux arbres.")]
    [Min(0f)]
    [SerializeField] private float minimumTreeSpacing = 6f;

    [Range(0f, 90f)]
    [SerializeField] private float treeMaxSlope = 27f;

    [Range(0f, 1f)]
    [SerializeField] private float treeMinGrass = 0.35f;

    [Range(0f, 1f)]
    [SerializeField] private float treeMaxRock = 0.25f;

    [Range(0f, 1f)]
    [SerializeField] private float treeMaxSnow = 0.15f;

    [Tooltip("Nombre maximal de tentatives pour placer les arbres.")]
    [Min(100)]
    [SerializeField] private int treePlacementAttempts = 20000;

    [Header("Arbres - taille")]

    [SerializeField] private Vector2 treeWidthScale =
        new Vector2(0.8f, 1.2f);

    [SerializeField] private Vector2 treeHeightScale =
        new Vector2(0.9f, 1.35f);

    [Tooltip("Si activé, les arbres déjà présents sont conservés.")]
    [SerializeField] private bool preserveExistingTrees = false;


    // ========================================================================
    // VARIABLES INTERNES
    // ========================================================================

    private Terrain _terrain;
    private TerrainData _data;

    private float[,,] _alphamaps;

    private int _alphaWidth;
    private int _alphaHeight;

    private System.Random _random;


    // ========================================================================
    // GENERATION
    // ========================================================================

    [ContextMenu("Générer environnement")]
    public void GenerateEnvironment()
    {
        _terrain = GetComponent<Terrain>();

        if (_terrain == null)
        {
            Debug.LogError("Terrain introuvable.");
            return;
        }

        _data = _terrain.terrainData;

        if (_data == null)
        {
            Debug.LogError("TerrainData introuvable.");
            return;
        }

        if (!ValidateTerrain())
            return;


#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCompleteObjectUndo(
            _data,
            "Generate Terrain Environment"
        );
#endif


        _random = new System.Random(seed);


        // ================================================================
        // Charger l'alphamap UNE SEULE FOIS
        // ================================================================

        _alphaWidth = _data.alphamapWidth;
        _alphaHeight = _data.alphamapHeight;

        _alphamaps = _data.GetAlphamaps(
            0,
            0,
            _alphaWidth,
            _alphaHeight
        );


        // ================================================================
        // DETAILS
        // ================================================================

        GenerateDetails();


        // ================================================================
        // ARBRES
        // ================================================================

        GenerateTrees();


#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(_data);
#endif


        Debug.Log(
            "Environnement généré : herbes, fleurs, branches, cailloux et arbres."
        );
    }


    // ========================================================================
    // DETAILS
    // ========================================================================

    private void GenerateDetails()
    {
        int width = _data.detailWidth;
        int height = _data.detailHeight;


        // Un tableau de densité par Detail Prototype.
        Dictionary<int, int[,]> maps =
            new Dictionary<int, int[,]>();


        CreateMaps(
            maps,
            grassDetailPrototypes,
            width,
            height
        );

        CreateMaps(
            maps,
            flowerDetailPrototypes,
            width,
            height
        );

        CreateMaps(
            maps,
            branchDetailPrototypes,
            width,
            height
        );

        CreateMaps(
            maps,
            smallRockDetailPrototypes,
            width,
            height
        );


        // ================================================================
        // PARCOURIR TOUT LE TERRAIN
        // ================================================================

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx =
                    (x + 0.5f) / width;

                float nz =
                    (y + 0.5f) / height;


                // --------------------------------------------------------
                // TEXTURES DU TERRAIN
                // --------------------------------------------------------

                float grass =
                    SampleLayers(
                        nx,
                        nz,
                        grassTextureLayers
                    );

                float dirt =
                    SampleLayer(
                        nx,
                        nz,
                        dirtTextureLayer
                    );

                float rock =
                    SampleLayers(
                        nx,
                        nz,
                        rockTextureLayers
                    );

                float snow =
                    SampleLayer(
                        nx,
                        nz,
                        snowTextureLayer
                    );


                // --------------------------------------------------------
                // PENTE
                // --------------------------------------------------------

                float slope =
                    _data.GetSteepness(
                        nx,
                        nz
                    );


                // --------------------------------------------------------
                // MASQUE DE FORET
                // --------------------------------------------------------

                float forest =
                    GetForestMask(
                        nx,
                        nz
                    );


                // ========================================================
                // HERBE
                // ========================================================

                if (grassDetailPrototypes.Length > 0)
                {
                    float slopeMask =
                        GetSlopeMask(
                            slope,
                            grassMaxSlope
                        );

                    float surfaceMask =
                        Mathf.InverseLerp(
                            grassSurfaceThreshold,
                            1f,
                            grass
                        );


                    float noise =
                        Mathf.PerlinNoise(
                            nx * grassNoiseScale + SeedX(10),
                            nz * grassNoiseScale + SeedY(10)
                        );


                    // Toujours un peu d'herbe mais avec
                    // des variations naturelles.
                    float noiseDensity =
                        Mathf.Lerp(
                            0.35f,
                            1f,
                            noise
                        );


                    float density =
                        surfaceMask *
                        slopeMask *
                        (1f - snow) *
                        noiseDensity;


                    int finalDensity =
                        Mathf.RoundToInt(
                            density *
                            grassMaxDensity
                        );


                    SetRandomVariant(
                        maps,
                        grassDetailPrototypes,
                        x,
                        y,
                        nx,
                        nz,
                        finalDensity,
                        100
                    );
                }


                // ========================================================
                // FLEURS
                // ========================================================

                if (flowerDetailPrototypes.Length > 0)
                {
                    float flowerNoise =
                        Mathf.PerlinNoise(
                            nx * flowerPatchScale + SeedX(20),
                            nz * flowerPatchScale + SeedY(20)
                        );


                    float patch =
                        Mathf.InverseLerp(
                            flowerPatchThreshold,
                            1f,
                            flowerNoise
                        );


                    patch =
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            patch
                        );


                    float grassMask =
                        Mathf.InverseLerp(
                            flowerMinGrass,
                            1f,
                            grass
                        );


                    float slopeMask =
                        GetSlopeMask(
                            slope,
                            flowerMaxSlope
                        );


                    float density =
                        patch *
                        grassMask *
                        slopeMask *
                        (1f - snow);


                    int finalDensity =
                        Mathf.RoundToInt(
                            density *
                            flowerMaxDensity
                        );


                    SetFlowerVariant(
                        maps,
                        flowerDetailPrototypes,
                        x,
                        y,
                        nx,
                        nz,
                        finalDensity
                    );
                }


                // ========================================================
                // BRANCHES
                // ========================================================

                if (branchDetailPrototypes.Length > 0)
                {
                    float noise =
                        Mathf.PerlinNoise(
                            nx * branchNoiseScale + SeedX(30),
                            nz * branchNoiseScale + SeedY(30)
                        );


                    float patch =
                        Mathf.InverseLerp(
                            branchThreshold,
                            1f,
                            noise
                        );


                    float terrainMask =
                        Mathf.Clamp01(
                            grass * 0.7f +
                            dirt * 0.7f
                        );


                    float forestMask =
                        Mathf.Clamp01(
                            forest *
                            branchForestInfluence
                        );


                    float slopeMask =
                        GetSlopeMask(
                            slope,
                            branchMaxSlope
                        );


                    float density =
                        patch *
                        terrainMask *
                        forestMask *
                        slopeMask *
                        (1f - snow);


                    int finalDensity =
                        Mathf.RoundToInt(
                            density *
                            branchMaxDensity
                        );


                    SetRandomVariant(
                        maps,
                        branchDetailPrototypes,
                        x,
                        y,
                        nx,
                        nz,
                        finalDensity,
                        300
                    );
                }


                // ========================================================
                // PETITS CAILLOUX
                // ========================================================

                if (smallRockDetailPrototypes.Length > 0)
                {
                    float noise =
                        Mathf.PerlinNoise(
                            nx * smallRockNoiseScale + SeedX(40),
                            nz * smallRockNoiseScale + SeedY(40)
                        );


                    float patch =
                        Mathf.InverseLerp(
                            smallRockThreshold,
                            1f,
                            noise
                        );


                    // Favorise :
                    // terre
                    // transition roche
                    // un tout petit peu les zones herbeuses

                    float surface =
                        Mathf.Clamp01(
                            dirt * 0.75f +
                            rock * 0.85f +
                            grass * 0.10f
                        );


                    float slopeMask =
                        GetSlopeMask(
                            slope,
                            smallRockMaxSlope
                        );


                    float density =
                        patch *
                        surface *
                        slopeMask *
                        (1f - snow);


                    int finalDensity =
                        Mathf.RoundToInt(
                            density *
                            smallRockMaxDensity
                        );


                    SetRandomVariant(
                        maps,
                        smallRockDetailPrototypes,
                        x,
                        y,
                        nx,
                        nz,
                        finalDensity,
                        400
                    );
                }
            }
        }


        // ================================================================
        // ENVOYER LES CARTES DE DENSITE AU TERRAIN
        // ================================================================

        foreach (var pair in maps)
        {
            int detailPrototype = pair.Key;

            if (!IsValidDetailPrototype(detailPrototype))
                continue;


            _data.SetDetailLayer(
                0,
                0,
                detailPrototype,
                pair.Value
            );
        }
    }


    // ========================================================================
    // ARBRES
    // ========================================================================

    private void GenerateTrees()
    {
        if (treePrototypeIndices == null ||
            treePrototypeIndices.Length == 0 ||
            targetTreeCount <= 0)
        {
            return;
        }


        List<TreeInstance> trees =
            new List<TreeInstance>();


        // Positions en mètres utilisées pour éviter que
        // deux arbres soient trop proches.
        List<Vector2> occupiedPositions =
            new List<Vector2>();


        // ================================================================
        // CONSERVER LES ARBRES EXISTANTS
        // ================================================================

        if (preserveExistingTrees)
        {
            TreeInstance[] existing =
                _data.treeInstances;


            for (int i = 0; i < existing.Length; i++)
            {
                trees.Add(existing[i]);


                Vector2 worldPosition =
                    new Vector2(
                        existing[i].position.x *
                        _data.size.x,

                        existing[i].position.z *
                        _data.size.z
                    );


                occupiedPositions.Add(
                    worldPosition
                );
            }
        }


        // ================================================================
        // PLACEMENT
        // ================================================================

        int addedTrees = 0;

        int attempts = 0;


        while (
            addedTrees < targetTreeCount &&
            attempts < treePlacementAttempts
        )
        {
            attempts++;


            float nx =
                NextFloat();

            float nz =
                NextFloat();


            // ------------------------------------------------------------
            // FORET
            // ------------------------------------------------------------

            float forest =
                GetForestMask(
                    nx,
                    nz
                );


            // Random supplémentaire :
            // même une bonne zone de forêt n'accepte pas
            // automatiquement chaque candidat.

            if (NextFloat() > forest)
                continue;


            // ------------------------------------------------------------
            // TEXTURES
            // ------------------------------------------------------------

            float grass =
                SampleLayers(
                    nx,
                    nz,
                    grassTextureLayers
                );


            float rock =
                SampleLayers(
                    nx,
                    nz,
                    rockTextureLayers
                );


            float snow =
                SampleLayer(
                    nx,
                    nz,
                    snowTextureLayer
                );


            if (grass < treeMinGrass)
                continue;

            if (rock > treeMaxRock)
                continue;

            if (snow > treeMaxSnow)
                continue;


            // ------------------------------------------------------------
            // PENTE
            // ------------------------------------------------------------

            float slope =
                _data.GetSteepness(
                    nx,
                    nz
                );


            if (slope > treeMaxSlope)
                continue;


            // ------------------------------------------------------------
            // DISTANCE ENTRE ARBRES
            // ------------------------------------------------------------

            Vector2 positionMeters =
                new Vector2(
                    nx * _data.size.x,
                    nz * _data.size.z
                );


            if (!IsFarEnoughFromTrees(
                positionMeters,
                occupiedPositions
            ))
            {
                continue;
            }


            // ------------------------------------------------------------
            // CHOIX DU TREE PROTOTYPE
            // ------------------------------------------------------------

            int prototypeIndex =
                GetRandomValidTreePrototype();


            if (prototypeIndex < 0)
                continue;


            // ------------------------------------------------------------
            // HAUTEUR
            // ------------------------------------------------------------

            float terrainHeight =
                _data.GetInterpolatedHeight(
                    nx,
                    nz
                );


            float normalizedHeight =
                _data.size.y > 0f
                ? terrainHeight / _data.size.y
                : 0f;


            // ------------------------------------------------------------
            // TREE INSTANCE
            // ------------------------------------------------------------

            TreeInstance tree =
                new TreeInstance();


            tree.position =
                new Vector3(
                    nx,
                    normalizedHeight,
                    nz
                );


            tree.prototypeIndex =
                prototypeIndex;


            tree.widthScale =
                Mathf.Lerp(
                    treeWidthScale.x,
                    treeWidthScale.y,
                    NextFloat()
                );


            tree.heightScale =
                Mathf.Lerp(
                    treeHeightScale.x,
                    treeHeightScale.y,
                    NextFloat()
                );


            tree.rotation =
                NextFloat() *
                Mathf.PI *
                2f;


            tree.color =
                Color.white;


            tree.lightmapColor =
                Color.white;


            // ------------------------------------------------------------
            // AJOUT
            // ------------------------------------------------------------

            trees.Add(tree);

            occupiedPositions.Add(
                positionMeters
            );

            addedTrees++;
        }


        // ================================================================
        // APPLIQUER LES ARBRES
        // ================================================================

        _data.SetTreeInstances(
            trees.ToArray(),
            true
        );


        Debug.Log(
            "Arbres ajoutés : " +
            addedTrees +
            " / " +
            targetTreeCount +
            " après " +
            attempts +
            " tentatives."
        );
    }


    // ========================================================================
    // FOREST MASK
    // ========================================================================

    private float GetForestMask(
        float nx,
        float nz
    )
    {
        float noise =
            Mathf.PerlinNoise(
                nx * forestNoiseScale + SeedX(500),
                nz * forestNoiseScale + SeedY(500)
            );


        float forest =
            Mathf.InverseLerp(
                forestThreshold,
                1f,
                noise
            );


        forest =
            Mathf.SmoothStep(
                0f,
                1f,
                forest
            );


        return forest;
    }


    // ========================================================================
    // SLOPE MASK
    // ========================================================================

    private float GetSlopeMask(
        float slope,
        float maximumSlope
    )
    {
        const float fadeRange = 7f;


        float startFade =
            Mathf.Max(
                0f,
                maximumSlope - fadeRange
            );


        return
            1f -
            Mathf.InverseLerp(
                startFade,
                maximumSlope,
                slope
            );
    }


    // ========================================================================
    // ALPHAMAP SAMPLING
    // ========================================================================

    private float SampleLayers(
        float nx,
        float nz,
        int[] layers
    )
    {
        if (layers == null)
            return 0f;


        float total = 0f;


        for (int i = 0; i < layers.Length; i++)
        {
            total +=
                SampleLayer(
                    nx,
                    nz,
                    layers[i]
                );
        }


        return Mathf.Clamp01(total);
    }


    private float SampleLayer(
        float nx,
        float nz,
        int layer
    )
    {
        if (layer < 0 ||
            layer >= _data.alphamapLayers)
        {
            return 0f;
        }


        // ------------------------------------------------------------
        // Position dans l'alphamap
        // ------------------------------------------------------------

        float px =
            nx *
            (_alphaWidth - 1);


        float py =
            nz *
            (_alphaHeight - 1);


        int x0 =
            Mathf.FloorToInt(px);

        int y0 =
            Mathf.FloorToInt(py);


        int x1 =
            Mathf.Min(
                x0 + 1,
                _alphaWidth - 1
            );

        int y1 =
            Mathf.Min(
                y0 + 1,
                _alphaHeight - 1
            );


        float tx =
            px - x0;

        float ty =
            py - y0;


        // ------------------------------------------------------------
        // Bilinear interpolation
        // ------------------------------------------------------------

        float a =
            _alphamaps[
                y0,
                x0,
                layer
            ];


        float b =
            _alphamaps[
                y0,
                x1,
                layer
            ];


        float c =
            _alphamaps[
                y1,
                x0,
                layer
            ];


        float d =
            _alphamaps[
                y1,
                x1,
                layer
            ];


        float top =
            Mathf.Lerp(
                a,
                b,
                tx
            );


        float bottom =
            Mathf.Lerp(
                c,
                d,
                tx
            );


        return Mathf.Lerp(
            top,
            bottom,
            ty
        );
    }


    // ========================================================================
    // DETAIL MAP HELPERS
    // ========================================================================

    private void CreateMaps(
        Dictionary<int, int[,]> maps,
        int[] prototypes,
        int width,
        int height
    )
    {
        if (prototypes == null)
            return;


        for (int i = 0; i < prototypes.Length; i++)
        {
            int prototype =
                prototypes[i];


            if (!IsValidDetailPrototype(prototype))
                continue;


            if (!maps.ContainsKey(prototype))
            {
                maps.Add(
                    prototype,
                    new int[height, width]
                );
            }
        }
    }


    private void SetRandomVariant(
        Dictionary<int, int[,]> maps,
        int[] prototypes,
        int x,
        int y,
        float nx,
        float nz,
        int density,
        int salt
    )
    {
        if (density <= 0)
            return;

        if (prototypes == null)
            return;

        if (prototypes.Length == 0)
            return;


        int index =
            PickVariant(
                nx,
                nz,
                prototypes.Length,
                salt
            );


        int prototype =
            prototypes[index];


        if (!maps.TryGetValue(
            prototype,
            out int[,] map
        ))
        {
            return;
        }


        map[y, x] =
            Mathf.Max(
                map[y, x],
                density
            );
    }


    private int PickVariant(
        float nx,
        float nz,
        int count,
        int salt
    )
    {
        if (count <= 1)
            return 0;


        // Bruit assez gros :
        // crée des groupes utilisant le même type
        // au lieu d'un changement à chaque pixel.

        float noise =
            Mathf.PerlinNoise(
                nx * 10f + SeedX(salt),
                nz * 10f + SeedY(salt)
            );


        int index =
            Mathf.FloorToInt(
                noise *
                count
            );


        return Mathf.Clamp(
            index,
            0,
            count - 1
        );
    }


    // ========================================================================
    // TREE HELPERS
    // ========================================================================

    private bool IsFarEnoughFromTrees(
        Vector2 position,
        List<Vector2> occupied
    )
    {
        if (minimumTreeSpacing <= 0f)
            return true;


        float sqrDistance =
            minimumTreeSpacing *
            minimumTreeSpacing;


        for (int i = 0; i < occupied.Count; i++)
        {
            if (
                (occupied[i] - position).sqrMagnitude
                <
                sqrDistance
            )
            {
                return false;
            }
        }


        return true;
    }


    private int GetRandomValidTreePrototype()
    {
        if (
            treePrototypeIndices == null ||
            treePrototypeIndices.Length == 0
        )
        {
            return -1;
        }


        int start =
            _random.Next(
                0,
                treePrototypeIndices.Length
            );


        // On tourne dans la liste si l'index choisi
        // n'est pas valide.

        for (
            int i = 0;
            i < treePrototypeIndices.Length;
            i++
        )
        {
            int arrayIndex =
                (start + i) %
                treePrototypeIndices.Length;


            int prototype =
                treePrototypeIndices[
                    arrayIndex
                ];


            if (
                prototype >= 0 &&
                prototype <
                _data.treePrototypes.Length
            )
            {
                return prototype;
            }
        }


        return -1;
    }


    // ========================================================================
    // RANDOM
    // ========================================================================

    private float NextFloat()
    {
        return (float)_random.NextDouble();
    }


    private float SeedX(int salt)
    {
        float value =
            seed * 0.12345f +
            salt * 31.417f;


        return Mathf.Repeat(
            value,
            10000f
        );
    }


    private float SeedY(int salt)
    {
        float value =
            seed * 0.54321f +
            salt * 71.133f;


        return Mathf.Repeat(
            value,
            10000f
        );
    }


    // ========================================================================
    // VALIDATION
    // ========================================================================

    private bool ValidateTerrain()
    {
        if (_data.alphamapLayers <= 0)
        {
            Debug.LogError(
                "Le Terrain ne possède aucun Terrain Layer."
            );

            return false;
        }


        if (_data.detailPrototypes == null)
        {
            Debug.LogError(
                "Aucun Detail Prototype sur le Terrain."
            );

            return false;
        }


        return true;
    }


    private bool IsValidDetailPrototype(
        int index
    )
    {
        return
            index >= 0 &&
            index <
            _data.detailPrototypes.Length;
    }


    // ========================================================================
    // NETTOYAGE DETAILS
    // ========================================================================

    [ContextMenu("Effacer les détails configurés")]
    public void ClearConfiguredDetails()
    {
        Terrain terrain =
            GetComponent<Terrain>();


        if (terrain == null)
            return;


        TerrainData data =
            terrain.terrainData;


#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCompleteObjectUndo(
            data,
            "Clear Terrain Details"
        );
#endif


        ClearDetailGroup(
            data,
            grassDetailPrototypes
        );

        ClearDetailGroup(
            data,
            flowerDetailPrototypes
        );

        ClearDetailGroup(
            data,
            branchDetailPrototypes
        );

        ClearDetailGroup(
            data,
            smallRockDetailPrototypes
        );


#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(data);
#endif


        Debug.Log(
            "Détails configurés effacés."
        );
    }


    private void ClearDetailGroup(
        TerrainData data,
        int[] prototypes
    )
    {
        if (prototypes == null)
            return;


        for (
            int i = 0;
            i < prototypes.Length;
            i++
        )
        {
            int index =
                prototypes[i];


            if (
                index < 0 ||
                index >=
                data.detailPrototypes.Length
            )
            {
                continue;
            }


            int[,] empty =
                new int[
                    data.detailHeight,
                    data.detailWidth
                ];


            data.SetDetailLayer(
                0,
                0,
                index,
                empty
            );
        }
    }
    
    private void SetFlowerVariant(
        Dictionary<int, int[,]> maps,
        int[] prototypes,
        int x,
        int y,
        float nx,
        float nz,
        int density
    )
    {
        if (density <= 0)
            return;

        if (prototypes == null || prototypes.Length == 0)
            return;


        float noise =
            Mathf.PerlinNoise(
                nx * flowerColorVariationScale + SeedX(800),
                nz * flowerColorVariationScale + SeedY(800)
            );


        int index =
            Mathf.FloorToInt(
                noise * prototypes.Length
            );


        index =
            Mathf.Clamp(
                index,
                0,
                prototypes.Length - 1
            );


        int prototype =
            prototypes[index];


        if (!maps.TryGetValue(
                prototype,
                out int[,] map
            ))
        {
            return;
        }


        map[y, x] =
            Mathf.Max(
                map[y, x],
                density
            );
    }


    // ========================================================================
    // NETTOYAGE ARBRES
    // ========================================================================

    [ContextMenu("Effacer tous les arbres")]
    public void ClearTrees()
    {
        Terrain terrain =
            GetComponent<Terrain>();


        if (terrain == null)
            return;


        TerrainData data =
            terrain.terrainData;


#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCompleteObjectUndo(
            data,
            "Clear Terrain Trees"
        );
#endif


        data.SetTreeInstances(
            Array.Empty<TreeInstance>(),
            true
        );


#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(data);
#endif


        Debug.Log(
            "Tous les arbres ont été supprimés."
        );
    }
}