using UnityEditor;
using UnityEngine;
using static PostProcessWorldBuilder;

public static class PostProcessNatureBuilder
{
    const string Nat = "Assets/PackagesStore/Idyllic Fantasy Nature/Prefabs/";

    static GameObject P(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(Nat + n + ".prefab");

    static GameObject Spawn(string n, Transform parent, Vector3 pos, float yaw, float scale)
    {
        var prefab = P(n);
        if (prefab == null) { Debug.LogWarning("Prefab manquant : " + n); return null; }
        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        g.transform.localScale = Vector3.one * scale;
        return g;
    }

    static void BuildPond(Transform nature, Transform fx, System.Func<float, float, Vector3> ground, Vector3 center, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        var pc = center + new Vector3(PostProcessWorldBuilder.PondRel.x, 0, PostProcessWorldBuilder.PondRel.y);
        float waterY = ground(pc.x, pc.z).y + 0.9f; // le fond de la mare est creusé à ~0.3 m, la berge à 2 m
        var water = Spawn("Water", fx, new Vector3(pc.x, waterY, pc.z), 0, 3.4f);

        // berges : roseaux, massettes, nénuphars, pierres, fleurs
        string[] shore = { "Reeds_01", "Reeds_02", "Reeds_03", "Cattail_01", "Cattail_02", "Cattail_03" };
        for (int i = 0; i < 70; i++)
        {
            float a = R() * Mathf.PI * 2f, r = PostProcessWorldBuilder.PondRadius * (0.7f + R() * 0.45f);
            var p = ground(pc.x + Mathf.Cos(a) * r, pc.z + Mathf.Sin(a) * r);
            Spawn(shore[rng.Next(shore.Length)], nature, p, R() * 360f, 0.9f + R() * 0.6f);
        }
        for (int i = 0; i < 14; i++)
        {
            float a = R() * Mathf.PI * 2f, r = R() * PostProcessWorldBuilder.PondRadius * 0.6f;
            Spawn("LilyPads_0" + (1 + rng.Next(3)), nature, new Vector3(pc.x + Mathf.Cos(a) * r, waterY + 0.02f, pc.z + Mathf.Sin(a) * r), R() * 360f, 1f + R());
        }
        for (int i = 0; i < 12; i++)
        {
            float a = R() * Mathf.PI * 2f, r = PostProcessWorldBuilder.PondRadius * (0.85f + R() * 0.3f);
            Spawn("Stone_Medium_0" + (1 + rng.Next(3)), nature, ground(pc.x + Mathf.Cos(a) * r, pc.z + Mathf.Sin(a) * r), R() * 360f, 0.8f + R());
        }
        // rayons de lumière au-dessus de la mare
        Spawn("GodRays", fx, new Vector3(pc.x, waterY + 5f, pc.z), -35f, 1.5f);
    }

    public static void Build(Transform parent, Terrain terrain, Vector3 center, float flatR, GameObject env)
    {
        var nature = new GameObject("PP_Nature").transform;
        nature.SetParent(parent);
        var rng = new System.Random(11);
        float half = terrain.terrainData.size.x * 0.5f;
        float baseY = terrain.transform.position.y;
        Vector3 player = Vector3.zero;
        var pl = GameObject.Find("Player");
        if (pl != null) player = pl.transform.position;

        Vector3 Ground(float x, float z)
        {
            var p = new Vector3(x, 0, z);
            p.y = baseY + terrain.SampleHeight(p);
            return p;
        }
        float Steep(float x, float z) =>
            terrain.terrainData.GetSteepness((x - center.x + half) / (half * 2), (z - center.z + half) / (half * 2));
        float R() => (float)rng.NextDouble();

        string[] trees = { "BroadleafTree_01_Green", "BroadleafTree_02_Green", "BroadleafTree_03_Green", "BroadleafTree_04_Green", "BroadleafTree_05_Green",
                           "BroadleafTree_02_Red", "BroadleafTree_04_Red", "BlossomTree_01", "BlossomTree_03" };
        string[] firs = { "Fir_01", "Fir_02", "Fir_03", "Fir_04", "Fir_05" };

        // 1) remplace les arbres et rochers primitifs de la scène test par de vrais assets, aux mêmes positions
        if (env != null)
        {
            foreach (Transform t in env.transform)
            {
                if (t.name == "Crown") { t.gameObject.SetActive(false); continue; }
                if (t.name == "Trunk" && Vector2.Distance(new Vector2(t.position.x - center.x, t.position.z - center.z), PondRel) < PondRadius) { t.gameObject.SetActive(false); continue; }
                if (t.name == "Trunk")
                {
                    t.gameObject.SetActive(false);
                    var p = t.position; p.y = Ground(p.x, p.z).y;
                    Spawn(trees[rng.Next(trees.Length)], nature, p, R() * 360f, 0.9f + R() * 0.5f);
                }
                else if (t.name == "Rock")
                {
                    t.gameObject.SetActive(false);
                    var p = t.position; p.y = Ground(p.x, p.z).y - 0.1f;
                    Spawn("Rock_Medium_0" + (1 + rng.Next(3)), nature, p, R() * 360f, 1f + R() * 0.8f);
                }
            }
        }

        // 2) forêt lointaine : feuillus, sapins en altitude
        int placed = 0;
        for (int i = 0; i < 4000 && placed < 450; i++)
        {
            float x = center.x + (R() * 2 - 1) * (half - 10f), z = center.z + (R() * 2 - 1) * (half - 10f);
            var d = new Vector2(x - center.x, z - center.z).magnitude;
            if (d < flatR + 2f || Steep(x, z) > 30f) continue;
            var p = Ground(x, z);
            float rel = (p.y - baseY) / 70f;
            if (rel > 0.7f) continue;
            string n = rel > 0.35f || R() < 0.2f ? firs[rng.Next(firs.Length)] : trees[rng.Next(trees.Length)];
            Spawn(n, nature, p, R() * 360f, 0.8f + R() * 0.7f);
            placed++;
        }

        // 3) rochers sur les pentes
        for (int i = 0; i < 80; i++)
        {
            float x = center.x + (R() * 2 - 1) * (half - 10f), z = center.z + (R() * 2 - 1) * (half - 10f);
            if (new Vector2(x - center.x, z - center.z).magnitude < flatR) continue;
            string n = R() < 0.25f ? "Rock_Big_0" + (1 + rng.Next(3)) : R() < 0.5f ? "Rock_Medium_0" + (1 + rng.Next(3)) : "Stone_Big_0" + (1 + rng.Next(3));
            Spawn(n, nature, Ground(x, z) - Vector3.up * 0.2f, R() * 360f, 1f + R() * 1.5f);
        }

        // 4) sous-bois autour de la zone de jeu : herbes, plantes, buissons, fleurs
        string[] grass = { "Grass_01", "Grass_02", "Grass_03" };
        string[] plants = { "Plant_01", "Plant_02", "Plant_03", "Plant_04", "Plant_05", "Plant_06", "Plant_07", "Plant_08" };
        string[] bushes = { "Bush_01_01", "Bush_01_02", "Bush_02_01", "Bush_02_02", "Bush_03_01", "Bush_03_02" };
        string[] flowers = { "Flower_Red", "Flower_Yellow", "Flower_White", "Flower_Orange", "Flower_Purple", "Flower_Pink", "Flower_Blue_01", "Flower_YellowRed" };
        string[] meadows = { "FlowerMeadow_Orange", "FlowerMeadow_White", "FlowerMeadow_RedOrange", "FlowerMeadow_OrangePinkRedPurpleBlue", "FlowerMeadow_Pink" };

        float radius = Mathf.Min(flatR + 40f, 110f);
        int nGrass = 0, nOther = 0;
        for (int i = 0; i < 9000 && (nGrass < 2200 || nOther < 700); i++)
        {
            float a = R() * Mathf.PI * 2f, r = Mathf.Sqrt(R()) * radius;
            float x = center.x + Mathf.Cos(a) * r, z = center.z + Mathf.Sin(a) * r;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(player.x, player.z)) < 1.5f || Vector2.Distance(new Vector2(x - center.x, z - center.z), PondRel) < PondRadius * 0.85f) continue;
            if (Steep(x, z) > 35f) continue;
            var p = Ground(x, z);
            if (R() < 0.62f)
            {
                if (nGrass >= 2200) continue;
                for (int k = 0; k < 3; k++)
                    Spawn(grass[rng.Next(3)], nature, p + new Vector3(R() - .5f, 0, R() - .5f) * 0.9f, R() * 360f, 0.8f + R() * 0.7f);
                nGrass++;
            }
            else
            {
                if (nOther >= 700) continue;
                float k = R();
                string n = k < 0.3f ? plants[rng.Next(plants.Length)] : k < 0.5f ? bushes[rng.Next(bushes.Length)]
                         : k < 0.8f ? flowers[rng.Next(flowers.Length)] : meadows[rng.Next(meadows.Length)];
                float sc = n.StartsWith("Bush") ? 0.9f + R() * 0.7f : 0.8f + R() * 0.5f;
                Spawn(n, nature, p, R() * 360f, sc);
                nOther++;
            }
        }

        // 5) ambiance : vent, papillons, feuilles flottantes
        var fx = new GameObject("PP_NatureFX").transform;
        fx.SetParent(parent);
        foreach (var n in new[] { "Code Related/WindControl", "Code Related/VegetationBendControl" })
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Nat + n + ".prefab");
            if (pf == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, fx);
            var bend = inst.GetComponent<IdyllicFantasyNature.VegetationBendControl>();
            if (bend != null)
            {
                // la plante se courbe au passage du joueur ; sans origine le script lève une exception à chaque frame
                var so = new SerializedObject(bend);
                so.FindProperty("_bendOrigin").objectReferenceValue = pl != null ? pl.transform : null;
                so.FindProperty("_enableBendFeature").boolValue = pl != null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        BuildPond(nature, fx, Ground, center, rng);
        var butter = P("ButterflySpawnArea");
        if (butter != null)
            for (int i = 0; i < 4; i++)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(butter, fx);
                b.transform.position = Ground(center.x + (R() * 2 - 1) * 35f, center.z + (R() * 2 - 1) * 35f) + Vector3.up * 1.5f;
            }
        foreach (var n in new[] { "FloatingLeafs_Yellow", "FloatingLeafs_Green" })
            for (int i = 0; i < 3; i++)
                Spawn(n, fx, Ground(center.x + (R() * 2 - 1) * 40f, center.z + (R() * 2 - 1) * 40f) + Vector3.up * 3f, 0, 1f);
    }
}
