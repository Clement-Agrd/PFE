using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

// Builds the Valheim-style misty mountain/forest backdrop in the MainMenu scene.
public static class MenuBackgroundBuilder
{
    const string Pack = "Assets/PackagesStore/Idyllic Fantasy Nature/";
    const string OutDir = "Assets/Scenes/MainMenuBackground";
    const float Size = 320f, MaxH = 110f;
    const int Res = 513;
    const float CamAboveGround = 3.5f;
    const float Lift = 15f; // heights are stored offset by Lift so valleys stay positive

    static float Fbm(float x, float z, int oct)
    {
        float v = 0, a = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { v += a * Mathf.PerlinNoise(x * f + 100f * i, z * f + 37f * i); a *= 0.5f; f *= 2f; }
        return v;
    }
    static float Smooth(float a, float b, float x) { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x)); }

    // Height in meters; x,z relative to the camera, which looks along +z.
    static float H(float x, float z)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        float clearing = 1f - Smooth(18f, 50f, d);
        float roll = (Fbm(x * 0.012f, z * 0.012f, 4) - 0.5f) * 18f;
        float mount = Smooth(70f, 150f, d);
        float ridged = 1f - Mathf.Abs(2f * Fbm(x * 0.008f + 5f, z * 0.008f + 9f, 5) - 1f);
        float front = 0.55f + 0.9f * Smooth(-60f, 120f, z);
        float detail = (Fbm(x * 0.05f, z * 0.05f, 3) - 0.5f) * 3f;
        return roll * (0.25f + 0.75f * (1f - clearing)) + mount * Mathf.Pow(ridged, 1.4f) * front * 55f + detail * (1f - clearing * 0.7f);
    }

    static T Load<T>(string p) where T : Object { return AssetDatabase.LoadAssetAtPath<T>(p); }
    // The imported campfire VFX materials reference a shader that is not in the project; rebuild them on URP particle shaders.
    static void FixPilotoMaterials()
    {
        var urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!urp) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PackagesStore/Piloto Studio/Materials" }))
        {
            var m = Load<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!m || m.name == "InfiniteBG") continue;
            Texture tex = null;
            var envs = new SerializedObject(m).FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; i < envs.arraySize && tex == null; i++)
                tex = envs.GetArrayElementAtIndex(i).FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            bool additive = m.name.Contains("Add");
            m.shader = urp;
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", additive ? 1 : 10); m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Menu/WheelGlow.png")); bool smoke = m.name.Contains("Smoke"); m.SetColor("_BaseColor", smoke ? new Color(0.5f, 0.5f, 0.5f, 0.25f) : new Color(1f, 0.55f, 0.18f, 0.85f));
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }


    [MenuItem("Tools/Rebuild Menu Background")]
    static void BuildMenu() { Debug.Log("BUILDG: " + Build()); }

    public static string Build()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var old = GameObject.Find("MenuBackground"); if (old) Object.DestroyImmediate(old);
        var root = new GameObject("MenuBackground");
        var cam = Camera.main; Vector3 cp = cam.transform.position;
        var plane = GameObject.Find("Plane"); if (plane) plane.SetActive(false);

        // ---- terrain ----
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Scenes", "MainMenuBackground");
        var td = new TerrainData { heightmapResolution = Res };
        td.size = new Vector3(Size, MaxH, Size);
        var hs = new float[Res, Res];
        float step = Size / (Res - 1);
        for (int zi = 0; zi < Res; zi++)
            for (int xi = 0; xi < Res; xi++)
                hs[zi, xi] = Mathf.Clamp01((H(xi * step - Size / 2, zi * step - Size / 2) + Lift) / MaxH);
        td.SetHeights(0, 0, hs);

        var layers = new[] { "Grass_Layer", "Forest_Layer", "Rock_Layer", "Dirt_Layer" };
        var tl = new TerrainLayer[layers.Length];
        for (int i = 0; i < tl.Length; i++) tl[i] = Load<TerrainLayer>(Pack + "Terrain Layer/" + layers[i] + ".terrainlayer");
        td.terrainLayers = tl;
        td.alphamapResolution = 512;
        int ar = td.alphamapResolution;
        var am = new float[ar, ar, tl.Length];
        for (int zi = 0; zi < ar; zi++)
            for (int xi = 0; xi < ar; xi++)
            {
                float u = xi / (float)(ar - 1), v = zi / (float)(ar - 1);
                float wx = u * Size - Size / 2, wz = v * Size - Size / 2;
                float slope = td.GetSteepness(u, v), hm = td.GetInterpolatedHeight(u, v) - Lift;
                float d = Mathf.Sqrt(wx * wx + wz * wz);
                float rock = Mathf.Clamp01(Smooth(26f, 40f, slope) + 0.7f * Smooth(35f, 55f, hm));
                float forest = (1f - rock) * Smooth(15f, 40f, d) * Mathf.Clamp01(Fbm(wx * 0.02f, wz * 0.02f, 3) * 1.6f);
                float dirt = (1f - rock) * Mathf.Clamp01((Fbm(wx * 0.04f + 50, wz * 0.04f, 3) - 0.62f) * 4f) * 0.7f;
                float grass = Mathf.Max(0.05f, 1f - rock - forest - dirt);
                float s = grass + forest + rock + dirt;
                am[zi, xi, 0] = grass / s; am[zi, xi, 1] = forest / s; am[zi, xi, 2] = rock / s; am[zi, xi, 3] = dirt / s;
            }
        td.SetAlphamaps(0, 0, am);
        AssetDatabase.DeleteAsset(OutDir + "/MenuTerrain.asset");
        AssetDatabase.CreateAsset(td, OutDir + "/MenuTerrain.asset");
        var tgo = Terrain.CreateTerrainGameObject(td);
        tgo.name = "MenuTerrain"; tgo.transform.SetParent(root.transform);
        // put ground height at the camera spot CamAboveGround below the camera
        tgo.transform.position = new Vector3(cp.x - Size / 2, cp.y - CamAboveGround - (H(0, 0) + Lift), cp.z - Size / 2);
        var terr = tgo.GetComponent<Terrain>(); terr.drawInstanced = true; terr.heightmapPixelError = 4;
        float GroundY(float x, float z) { return terr.SampleHeight(new Vector3(cp.x + x, 0, cp.z + z)) + tgo.transform.position.y; }

        // ---- props ----
        var rng = new System.Random(11);
        float R() { return (float)rng.NextDouble(); }
        GameObject Inst(string rel, Transform parent, Vector3 pos, float scale, bool tilt)
        {
            var pf = Load<GameObject>(Pack + "Prefabs/" + rel + ".prefab"); if (!pf) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            g.transform.position = pos; g.transform.localScale = Vector3.one * scale;
            g.transform.rotation = Quaternion.Euler(tilt ? (R() - .5f) * 6f : 0, R() * 360f, 0);
            return g;
        }
        var forestRoot = new GameObject("Forest").transform; forestRoot.SetParent(root.transform);
        int placed = 0;
        for (int i = 0; i < 12000 && placed < 550; i++)
        {
            float x = (R() - .5f) * 300f, z = (R() - .5f) * 300f;
            float d = Mathf.Sqrt(x * x + z * z);
            if (d < 24f || d > 150f) continue;
            if (z < -50f) continue; // behind the camera
            float dens = Fbm(x * 0.025f + 3, z * 0.025f + 8, 3);
            if (dens < 0.34f && d < 100f) continue;
            float gy = GroundY(x, z);
            if (gy - tgo.transform.position.y - Lift > 45f) continue;
            var n = terr.terrainData.GetInterpolatedNormal((x + Size / 2) / Size, (z + Size / 2) / Size);
            if (Vector3.Angle(n, Vector3.up) > 32f) continue;
            string name = R() < 0.82f ? "Fir_0" + (1 + rng.Next(5)) : "BroadleafTree_0" + (1 + rng.Next(5)) + "_Green";
            Inst(name, forestRoot, new Vector3(cp.x + x, gy, cp.z + z), 0.9f + R() * 0.8f, true); placed++;
        }
        var rocks = new GameObject("Rocks").transform; rocks.SetParent(root.transform);
        for (int i = 0; i < 22; i++)
        {
            float x = (R() - .5f) * 70f, z = -5f + R() * 60f;
            if (Mathf.Abs(x) < 4f && z < 15f) continue;
            string nm = R() < .5f ? "Rock_Big_0" + (1 + rng.Next(3)) : "Rock_Medium_0" + (1 + rng.Next(3));
            Inst(nm, rocks, new Vector3(cp.x + x, GroundY(x, z) - 0.2f, cp.z + z), 0.8f + R() * 1.2f, false);
        }
        var campSpots = new[] { new Vector3(-11f, 0, 25f), new Vector3(17f, 0, 42f) };
        var plants = new GameObject("Plants").transform; plants.SetParent(root.transform);
        string[] small = { "Grass_01", "Grass_02", "Grass_03", "Plant_01", "Plant_03", "Bush_01_01", "Bush_02_01", "FlowerMeadow_White", "FlowerMeadow_BluePurple", "FlowerMeadow_Orange" };
        for (int i = 0; i < 900; i++)
        {
            float x = (R() - .5f) * 60f, z = -4f + R() * 45f;
            bool nearCamp = false; foreach (var cs in campSpots) if ((new Vector2(x, z) - new Vector2(cs.x, cs.z)).magnitude < 8f) nearCamp = true;
            if (nearCamp) continue;
            Inst(small[rng.Next(small.Length)], plants, new Vector3(cp.x + x, GroundY(x, z), cp.z + z), 0.9f + R() * 0.6f, false);
        }


        // ---- campfires: warm focal points ----
        var camps = new GameObject("Campfires").transform; camps.SetParent(root.transform);
        var cfPack = "Assets/PackagesStore/Piloto Studio/Campfire And Torches Pack/Prefabs/";
        FixPilotoMaterials();
        var woodMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); woodMat.SetColor("_BaseColor", new Color(0.22f, 0.14f, 0.08f)); woodMat.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.DeleteAsset(OutDir + "/CampfireWood.mat"); AssetDatabase.CreateAsset(woodMat, OutDir + "/CampfireWood.mat");
        var campNames = new[] { "SM_campfire_001_AmbienceFX", "SM_campfire_003_AmbienceFX" };
        for (int i = 0; i < campSpots.Length; i++)
        {
            var pf = Load<GameObject>(cfPack + campNames[i] + ".prefab"); if (!pf) continue;
            var c = (GameObject)PrefabUtility.InstantiatePrefab(pf, camps);
            c.name = "Campfire_" + (i + 1);
            foreach (var mr in c.GetComponentsInChildren<MeshRenderer>()) { var sm = mr.sharedMaterials; for (int k = 0; k < sm.Length; k++) if (sm[k] == null) sm[k] = woodMat; mr.sharedMaterials = sm; }
            c.transform.position = new Vector3(cp.x + campSpots[i].x, GroundY(campSpots[i].x, campSpots[i].z), cp.z + campSpots[i].z);
            c.transform.localScale = Vector3.one * (i == 0 ? 2.6f : 2.0f);
            var lg = new GameObject("FireLight"); lg.transform.SetParent(c.transform, false); lg.transform.localPosition = new Vector3(0, 0.6f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.2f);
            l.range = i == 0 ? 30f : 22f; l.intensity = i == 0 ? 160f : 90f; l.shadows = LightShadows.None;
            lg.AddComponent<FireLightFlicker>();
        }
        // ---- sky, fog, light ----
        var sky = Load<Material>("Assets/PackagesStore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Day_03_Sunless.mat");
        if (sky) { var skyCopy = new Material(sky); skyCopy.SetColor("_Tint", new Color(0.38f, 0.46f, 0.58f)); skyCopy.SetFloat("_Exposure", 0.85f); AssetDatabase.DeleteAsset(OutDir + "/MenuSky.mat"); AssetDatabase.CreateAsset(skyCopy, OutDir + "/MenuSky.mat"); RenderSettings.skybox = skyCopy; }
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0085f; RenderSettings.fogColor = new Color(0.58f, 0.67f, 0.78f);
        RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.4f;
        var sun = GameObject.Find("Directional Light").GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(14f, 200f, 0f);
        sun.color = new Color(1f, 0.74f, 0.48f); sun.intensity = 3.4f; sun.shadows = LightShadows.Soft;
        RenderSettings.sun = sun;

        // ---- camera ----
        if (!cam.GetComponent<MenuCameraDrift>()) cam.gameObject.AddComponent<MenuCameraDrift>(); cam.farClipPlane = 1500f; cam.fieldOfView = 55f;
        cam.transform.rotation = Quaternion.Euler(-5f, 0f, 0f);
        var ad = cam.GetComponent<UniversalAdditionalCameraData>();
        ad.renderPostProcessing = true; ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        ad.antialiasingQuality = AntialiasingQuality.High;

        // ---- post processing ----
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.9f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.7f); bloom.tint.Override(new Color(1f, 0.9f, 0.8f));
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        var ca = prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.3f); ca.contrast.Override(14f); ca.saturation.Override(-8f); ca.colorFilter.Override(new Color(0.95f, 0.97f, 1f));
        var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.4f); vg.smoothness.Override(0.6f);
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(8f);
        AssetDatabase.DeleteAsset(OutDir + "/MenuVolumeProfile.asset");
        AssetDatabase.CreateAsset(prof, OutDir + "/MenuVolumeProfile.asset");
        foreach (var vc in prof.components) AssetDatabase.AddObjectToAsset(vc, prof); // sub-assets, otherwise the overrides are lost on reload
        EditorUtility.SetDirty(prof);
        var vgo = new GameObject("MenuPostProcess"); vgo.transform.SetParent(root.transform);
        var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof; vol.priority = 10;

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return "trees=" + placed + " terrainY=" + tgo.transform.position.y;
    }
}
