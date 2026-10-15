using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class PostProcessSceneBeautifier
{
    const string ProfilePath = "Assets/Settings/ValheimVolumeProfile.asset";

    [MenuItem("Tools/PostProcess/Golden Hour Look")]
    public static void Apply()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);

        var bloom = Get<Bloom>(profile);
        bloom.intensity.Override(0.9f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.75f);
        bloom.tint.Override(new Color(1f, 0.85f, 0.65f));
        bloom.highQualityFiltering.Override(true);
        var dirt = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Arts/Materials/noise1.png");
        if (dirt != null)
        {
            bloom.dirtTexture.Override(dirt);
            bloom.dirtIntensity.Override(2.5f);
        }

        var ca = Get<ColorAdjustments>(profile);
        ca.postExposure.Override(0.7f);
        ca.contrast.Override(8f);
        ca.saturation.Override(-8f);
        ca.colorFilter.Override(new Color(1f, 0.95f, 0.88f));

        var split = Get<SplitToning>(profile);
        split.shadows.Override(new Color(0.4f, 0.5f, 0.68f));
        split.highlights.Override(new Color(1f, 0.74f, 0.42f));
        split.balance.Override(-5f);

        var smh = Get<ShadowsMidtonesHighlights>(profile);
        smh.shadows.Override(new Vector4(1f, 1f, 1.1f, 0.1f));
        smh.midtones.Override(new Vector4(1f, 0.97f, 0.92f, 0.05f));
        smh.highlights.Override(new Vector4(1.15f, 1f, 0.8f, 0.15f));

        var vig = Get<Vignette>(profile);
        vig.intensity.Override(0.27f);
        vig.smoothness.Override(0.6f);
        vig.color.Override(new Color(0.04f, 0.03f, 0.07f));

        var grain = Get<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin2);
        grain.intensity.Override(0.18f);
        grain.response.Override(0.8f);

        var chroma = Get<ChromaticAberration>(profile);
        chroma.intensity.Override(0.12f);

        var lgg = Get<LiftGammaGain>(profile);
        lgg.lift.Override(new Vector4(1f, 1f, 1.04f, 0.03f));
        lgg.gain.Override(new Vector4(1.05f, 1f, 0.92f, 0.05f));

        var dof = Get<DepthOfField>(profile);
        dof.gaussianStart.Override(40f);
        dof.gaussianEnd.Override(220f);
        dof.gaussianMaxRadius.Override(1.2f);

        EditorUtility.SetDirty(profile);

        // Soleil bas, doré
        var sun = GameObject.Find("Directional Light");
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(24f, -35f, 0f);
            var l = sun.GetComponent<Light>();
            l.color = new Color(1f, 0.74f, 0.5f);
            l.intensity = 2.6f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;
        }

        // Brume chaude dans la distance
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.78f, 0.66f, 0.58f);
        RenderSettings.fogDensity = 0.011f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.66f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.8f, 0.68f, 0.58f);
        RenderSettings.ambientGroundColor = new Color(0.38f, 0.33f, 0.28f);

        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Arts/Materials/PostProcessTest/Valheim_Sky.mat");
        if (sky != null)
        {
            sky.SetColor("_SkyTint", new Color(0.6f, 0.62f, 0.85f));
            sky.SetFloat("_AtmosphereThickness", 1.2f);
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_SunSize", 0.06f);
            EditorUtility.SetDirty(sky);
        }

        var shafts = AssetDatabase.LoadAssetAtPath<Material>("Assets/Arts/Materials/PostProcessTest/SunShafts.mat");
        if (shafts != null)
        {
            shafts.SetFloat("_Intensity", 1.6f);
            shafts.SetFloat("_Threshold", 0.25f);
            shafts.SetFloat("_Decay", 0.97f);
            shafts.SetColor("_Tint", new Color(1f, 0.72f, 0.4f));
            EditorUtility.SetDirty(shafts);
        }

        BuildDustMotes();

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }

    static T Get<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet(out T c)) c = p.Add<T>(true);
        c.active = true;
        return c;
    }

    static void BuildDustMotes()
    {
        var old = GameObject.Find("GoldenDust");
        if (old != null) Object.DestroyImmediate(old);

        var go = new GameObject("GoldenDust");
        var cam = Camera.main;
        go.transform.position = cam != null ? cam.transform.position + cam.transform.forward * 12f : Vector3.up * 3f;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.55f, 0.9f), new Color(1f, 0.7f, 0.35f, 0.5f));
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 60f;

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(40f, 8f, 40f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        var mp = "Assets/Arts/Materials/PostProcessTest/DustMote.mat"; var dm = AssetDatabase.LoadAssetAtPath<Material>(mp); if (dm == null) { dm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(dm, mp); } r.sharedMaterial = dm;
        dm.SetFloat("_Surface", 1f);
        dm.SetFloat("_Blend", 2f);
        dm.SetColor("_BaseColor", new Color(1f, 0.8f, 0.5f, 1f));
        dm.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f) * 3f);
        dm.EnableKeyword("_EMISSION");
        dm.renderQueue = 3000; EditorUtility.SetDirty(dm);
        ps.Play();
    }
}
