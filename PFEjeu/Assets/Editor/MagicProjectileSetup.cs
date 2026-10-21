using System.IO;
using UnityEditor;
using UnityEngine;
using ProfessionalTPS;

// Construit Assets/Prefabs/Projectiles/MagicProjectile.prefab (+ MagicProjectileImpact.prefab)
// à partir du modèle Assets/Arts/3D/magic_projectile.fbx.
// Menu : Tools/Magic Projectile/Build Prefab
public static class MagicProjectileSetup
{
    const string ModelPath = "Assets/Arts/3D/magic_projectile.fbx";
    const string MaterialDir = "Assets/Materials/MagicProjectile";
    const string PrefabPath = "Assets/Prefabs/Projectiles/MagicProjectile.prefab";
    const string ImpactPrefabPath = "Assets/Prefabs/Projectiles/MagicProjectileImpact.prefab";

    // Après import le modèle pointe déjà vers +Z (traîne vers -Z) : pas de correction.
    static readonly Quaternion ModelToForward = Quaternion.Euler(0f, 0f, 0f);
    const float VisualScale = 0.5f;
    // Épaisseur de la traîne (spirales + cônes), 1 = modèle d origine.
    const float TrailThickness = 1.4f;

    [MenuItem("Tools/Magic Projectile/Build Prefab")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError("[MagicProjectileSetup] Modèle introuvable : " + ModelPath);
            return;
        }

        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory("Assets/Prefabs/Projectiles");

        Texture2D gradient = CreateGradientTexture();

        Material core = CreateMaterial("MP_Core", Color.white, 1.5f, 1f, null);
        Material shell = CreateMaterial("MP_Shell", Hex("B06BFF"), 1f, 0.5f, null);
        Material glow = CreateMaterial("MP_Glow", Hex("6A2CFF"), 0.5f, 0.2f, null);
        Material spiral = CreateMaterial("MP_Spiral", Hex("FF5BE0"), 1.5f, 0.7f, gradient);
        Material cone = CreateMaterial("MP_Cone", Hex("5B7BFF"), 1f, 0.4f, gradient);
        Material spark = CreateMaterial("MP_Spark", Hex("FFD0F5"), 2f, 1f, null);

        // Racine du prefab + enfant 'Visual' (orientation vers +Z).
        var root = new GameObject("MagicProjectile");
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        // Clone simple (non lié au FBX) pour pouvoir réorganiser les pièces.
        var clone = Object.Instantiate(model);
        clone.transform.SetParent(visual.transform, false);

        var renderers = clone.GetComponentsInChildren<MeshRenderer>(true);

        // Centre de la tête, pour placer l'origine du prefab dessus.
        Vector3 head = Vector3.zero;
        foreach (var r in renderers)
            if (r.name == "orb_core") head = r.bounds.center;

        visual.transform.localRotation = ModelToForward;
        visual.transform.localScale = Vector3.one * VisualScale;
        visual.transform.localPosition = -(ModelToForward * (head * VisualScale));

        var spirals = new GameObject("spirals").transform;
        var sparks = new GameObject("sparks").transform;
        spirals.SetParent(visual.transform, false);
        sparks.SetParent(visual.transform, false);

        // Chaque pièce reçoit un pivot centré sur sa géométrie : les rotations
        // et scales se font autour de son centre, pas de l'origine du modèle.
        foreach (var r in renderers)
        {
            string n = r.name;
            Transform parent;
            Material mat;

            if (n.StartsWith("spiral_")) { parent = spirals; mat = spiral; }
            else if (n.StartsWith("spark_")) { parent = sparks; mat = spark; }
            else if (n == "tail_cone" || n == "tail_inner") { parent = visual.transform; mat = cone; }
            else if (n == "orb_core") { parent = visual.transform; mat = core; }
            else if (n == "orb_shell") { parent = visual.transform; mat = shell; }
            else if (n == "orb_glow") { parent = visual.transform; mat = glow; }
            else
            {
                Debug.LogWarning("[MagicProjectileSetup] Pièce ignorée : " + n);
                continue;
            }

            var pivot = new GameObject(n).transform;
            pivot.SetParent(parent, false);
            pivot.position = r.bounds.center;
            pivot.localRotation = Quaternion.identity;
            bool trail = n.StartsWith("spiral_") || n.StartsWith("tail_");
            pivot.localScale = Vector3.one;

            r.transform.SetParent(pivot, true);
            if (trail) pivot.localScale = new Vector3(TrailThickness, TrailThickness, 1f); // après reparent : la géométrie grossit
            r.name = n + "_mesh";
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // La coquille vide du FBX ne sert plus.
        Object.DestroyImmediate(clone);

        // Collider + Rigidbody (cinématique) pour le trigger / les scripts tiers.
        var sphere = root.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.3f;

        var body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        // Mouvement / dégâts : CombatProjectile (déjà utilisé par MagicCombatModule).
        var projectile = root.AddComponent<CombatProjectile>();
        GameObject impact = BuildImpactPrefab();

        var so = new SerializedObject(projectile);
        so.FindProperty("radius").floatValue = 0.25f;
        so.FindProperty("impactPrefab").objectReferenceValue = impact;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.AddComponent<MagicProjectileVisual>();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log("[MagicProjectileSetup] Prefab créé : " + PrefabPath);
    }

    // ---------- Impact (ParticleSystem généré par code) ----------

    static GameObject BuildImpactPrefab()
    {
        var go = new GameObject("MagicProjectileImpact");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.gravityModifier = 0f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.startColor = new ParticleSystem.MinMaxGradient(
            ColorHdr(Hex("FFD0F5"), 4f), ColorHdr(Hex("B06BFF"), 3f));

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = fade;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

        Material mat = CreateMaterial("MP_Impact", Color.white, 1f, 1f, CreateSoftDotTexture());
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, ImpactPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---------- Matériaux / textures ----------

    static Material CreateMaterial(string name, Color color, float intensity, float alpha, Texture2D map)
    {
        string path = MaterialDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }

        // Transparent + Additif.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        mat.SetColor("_BaseColor", ColorHdr(color, intensity, alpha));
        if (map != null)
            mat.SetTexture("_BaseMap", map);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Intensité HDR à la façon de l'Inspector Unity : rgb * 2^intensité.
    static Color ColorHdr(Color c, float intensity, float alpha = 1f)
    {
        float k = Mathf.Pow(2f, intensity);
        return new Color(c.r * k, c.g * k, c.b * k, alpha);
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    // Dégradé 256x4 : opaque au début de U, transparent à la fin.
    static Texture2D CreateGradientTexture()
    {
        var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
        {
            float a = 1f - x / 255f;
            for (int y = 0; y < 4; y++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return SaveTexture(tex, "MagicTrailGradient");
    }

    // Point doux (alpha radial) pour les particules d'impact.
    static Texture2D CreateSoftDotTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
            float a = Mathf.Clamp01(1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        return SaveTexture(tex, "MagicSoftDot");
    }

    static Texture2D SaveTexture(Texture2D tex, string name)
    {
        string path = MaterialDir + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
