using UnityEditor;
using UnityEngine;

public static class PostProcessWorldBuilder
{
    const string Mats = "Assets/Arts/Materials/PostProcessTest/";
    const string Fbx = "Assets/Arts/3D/";
    public static readonly Vector2 PondRel = new Vector2(32f, -32f);
    public const float PondRadius = 15f;

    [MenuItem("Tools/PostProcess/Build World")]
    public static void Build()
    {
        var root = GameObject.Find("PP_World");
        if (root != null) Object.DestroyImmediate(root);
        root = new GameObject("PP_World");

        var env = GameObject.Find("PP_TestEnvironment");
        float groundY = 0f, flatR = 60f;
        Vector3 center = Vector3.zero;
        if (env != null)
        {
            var ground = env.transform.Find("Ground");
            if (ground != null)
            {
                var gr = ground.GetComponent<Renderer>();
                groundY = gr.bounds.max.y;
                center = new Vector3(gr.bounds.center.x, 0, gr.bounds.center.z);
                ground.gameObject.SetActive(false);
            }
            var path = env.transform.Find("Path");
            if (path != null) path.gameObject.SetActive(false);
            foreach (Transform t in env.transform)
            {
                if (t.name != "Trunk" && t.name != "Crown" && t.name != "Rock") continue;
                var p = t.position - center; p.y = 0;
                flatR = Mathf.Max(flatR, p.magnitude + 8f);
            }
        }
        flatR = Mathf.Min(flatR, 140f);

        var terrain = BuildTerrain(root.transform, center, groundY, flatR);
        PostProcessNatureBuilder.Build(root.transform, terrain, center, flatR, env);
        BuildBuildings(root.transform, terrain, center);


        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }

    static Terrain BuildTerrain(Transform parent, Vector3 center, float groundY, float flatR)
    {
        const int res = 513;
        float size = Mathf.Max(400f, flatR * 3f), height = 70f;
        var data = new TerrainData { heightmapResolution = res };
        data.size = new Vector3(size, height, size);

        var h = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = (x / (float)(res - 1) - 0.5f) * size, wz = (y / (float)(res - 1) - 0.5f) * size;
                float d = Mathf.Sqrt(wx * wx + wz * wz);
                float hill = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatR, flatR + 90f, d));
                float n = Mathf.PerlinNoise(wx * 0.012f + 40f, wz * 0.012f + 40f) * 0.7f
                        + Mathf.PerlinNoise(wx * 0.04f, wz * 0.04f) * 0.25f
                        + Mathf.PerlinNoise(wx * 0.12f, wz * 0.12f) * 0.05f;
                float gentle = Mathf.PerlinNoise(wx * 0.03f + 9f, wz * 0.03f + 9f) * 0.6f * Mathf.Clamp01(1f - hill);
                float v = 2f + gentle + hill * (0.15f + n * 0.85f) * 55f; // 2 m de marge pour creuser la mare
                float pd = Vector2.Distance(new Vector2(wx, wz), PondRel);
                float pond = 1f - Sm(PondRadius * 0.5f, PondRadius, pd);
                h[y, x] = Mathf.Lerp(v, 0.3f, pond) / height;
            }
        data.SetHeights(0, 0, h);

        string[] layerNames = { "Herbe 1", "Herbe 2", "Terre", "Roche", "Neige" };
        var layers = new TerrainLayer[layerNames.Length];
        for (int i = 0; i < layers.Length; i++)
            layers[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/" + layerNames[i] + ".terrainlayer");
        data.terrainLayers = layers;

        int ares = 512;
        data.alphamapResolution = ares;
        var a = new float[ares, ares, layers.Length];
        for (int y = 0; y < ares; y++)
            for (int x = 0; x < ares; x++)
            {
                float nx = x / (float)(ares - 1), ny = y / (float)(ares - 1);
                float ht = data.GetInterpolatedHeight(nx, ny) / height;
                float slope = data.GetSteepness(nx, ny) / 90f;
                float v = Mathf.PerlinNoise(nx * 18f, ny * 18f);
                float flat = Mathf.Clamp01(1.2f - slope * 4f) * (1f - Sm(0.45f, 0.65f, ht));
                float w0 = flat * v;
                float w1 = flat * (1f - v);
                float w2 = Mathf.Clamp01(slope * 3f - 0.3f) * (1f - Sm(0.5f, 0.7f, ht))
                         + (Mathf.PerlinNoise(nx * 40f, ny * 40f) > 0.72f ? 0.5f : 0f) * (1f - ht);
                float w3 = Sm(0.35f, 0.6f, ht) * 0.6f + Mathf.Clamp01(slope * 5f - 1f);
                float w4 = Sm(0.62f, 0.8f, ht) * Mathf.Clamp01(1.3f - slope * 3f);
                float s = w0 + w1 + w2 + w3 + w4 + 0.0001f;
                a[y, x, 0] = w0 / s; a[y, x, 1] = w1 / s; a[y, x, 2] = w2 / s; a[y, x, 3] = w3 / s; a[y, x, 4] = w4 / s;
            }
        data.SetAlphamaps(0, 0, a);

        AssetDatabase.CreateAsset(data, "Assets/Scenes/PP_TerrainData.asset");
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "PP_Terrain";
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(center.x - size / 2f, groundY - 2f, center.z - size / 2f);
        var t = go.GetComponent<Terrain>();
        t.drawInstanced = true;
        t.heightmapPixelError = 4;
        return t;
    }

    static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Mats + n + ".mat");


    static void BuildBuildings(Transform parent, Terrain terrain, Vector3 center)
    {
        var b = new GameObject("PP_Buildings").transform;
        b.SetParent(parent);
        var wood = M("PP_Wood"); var stone = M("PP_Stone");
        Place("maison_de_flavio", 7f, new Vector3(-22, 0, 28), 150f, wood);
        Place("Tour_Modu", 16f, new Vector3(34, 0, 38), 200f, stone);
        for (int i = 0; i < 5; i++)
            Place("Muraille_modu", 5f, new Vector3(-10 + i * 9.5f, 0, 55), 0f, stone);

        void Place(string fbx, float targetH, Vector3 rel, float yaw, Material mat)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx + fbx + ".fbx");
            if (prefab == null) { Debug.LogWarning("Manquant : " + fbx); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, b);
            var rds = inst.GetComponentsInChildren<Renderer>();
            var bounds = MeshBounds(inst);
            float sc = targetH / Mathf.Max(0.01f, bounds.size.y);
            inst.transform.localScale = Vector3.one * sc;
            var p = center + rel;
            p.y = terrain.transform.position.y + terrain.SampleHeight(p);
            inst.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            bounds = MeshBounds(inst);
            inst.transform.position += Vector3.up * (p.y - bounds.min.y - 0.15f);
            foreach (var r in rds)
            {
                bool hasMat = r.sharedMaterial != null && r.sharedMaterial.name != "Lit" && r.sharedMaterial.name != "Default-Material";
                if (!hasMat) r.sharedMaterial = mat;
            }
        }
    }


    // bornes monde calculées depuis les maillages (Renderer.bounds est périmé juste après l'instanciation)
    static Bounds MeshBounds(GameObject go)
    {
        bool init = false; var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1)));
                if (!init) { b = new Bounds(c, Vector3.zero); init = true; } else b.Encapsulate(c);
            }
        }
        return b;
    }
}
