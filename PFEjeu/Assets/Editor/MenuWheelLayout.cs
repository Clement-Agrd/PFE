using Core.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Lays the main menu buttons (Jouer / Options / Quitter) on an arc on the left side of the screen,
// like a wheel, with two decorative rings and a glowing selector, then wires MenuWheelAnimator.
public static class MenuWheelLayout
{
    const string Dir = "Assets/UI/Menu/";
    const float Radius = 520f;        // distance from wheel center to button centers (canvas px)
    const float CenterX = 250f;       // wheel center, measured past the left screen edge
    const float AngleStep = 17f;      // degrees between buttons
    const int N = 1024;
    static readonly Vector2 ButtonSize = new Vector2(360, 72);

    static readonly Color Gold = new Color(0.82f, 0.66f, 0.34f, 1f);

    static float Cov(float dist, float halfWidth) { return Mathf.Clamp01(halfWidth + 0.5f - dist); }

    // Texture-space radius of the button ring (the ring image is Radius+50 canvas px in half-size).
    static float RingTexRadius { get { return Radius / (Radius + 50f) * (N / 2f); } }

    static void Save(Texture2D tex, string name)
    {
        tex.Apply();
        System.IO.Directory.CreateDirectory(Dir.TrimEnd('/'));
        System.IO.File.WriteAllBytes(Dir + name, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(Dir + name);
        var imp = (TextureImporter)AssetImporter.GetAtPath(Dir + name);
        imp.textureType = TextureImporterType.Sprite; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
        imp.SaveAndReimport();
    }

    // Main ring with graduated tick marks pointing inwards, so that rotation is visible.
    static void MakeOuterRing()
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        float c = (N - 1) / 2f, R = RingTexRadius;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
                float a = Cov(Mathf.Abs(d - R), 2.6f);                       // the ring line
                float tickDist = Mathf.Abs(Mathf.Repeat(ang + 1.5f, 3f) - 1.5f) * Mathf.Deg2Rad * d; // px from nearest 3° tick
                float inward = R - d;
                float len = 9f;
                if (Mathf.Abs(Mathf.Repeat(ang + 7.5f, 15f) - 7.5f) * Mathf.Deg2Rad * d < 3f) len = 20f;
                if (Mathf.Abs(Mathf.Repeat(ang + 22.5f, 45f) - 22.5f) * Mathf.Deg2Rad * d < 4f) len = 36f;
                if (inward > 0f && inward < len) a = Mathf.Max(a, Cov(tickDist, 1.6f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        Save(tex, "WheelRingOuter.png");
    }

    // Inner ring: long dashes with small dots between them.
    static void MakeInnerRing()
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        float c = (N - 1) / 2f, R = RingTexRadius - 62f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
                float seg = Mathf.Repeat(ang, 20f);                            // 20° pattern: 14° dash, gap, dot
                float a = 0f;
                if (seg < 14f) a = Cov(Mathf.Abs(d - R), 1.2f) * 0.9f;
                float dotDist = Mathf.Sqrt(Mathf.Pow((seg - 17f) * Mathf.Deg2Rad * d, 2f) + (d - R) * (d - R));
                a = Mathf.Max(a, Cov(dotDist, 2.6f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        Save(tex, "WheelRingInner.png");
    }

    static void MakeGlow()
    {
        const int G = 128;
        var tex = new Texture2D(G, G, TextureFormat.RGBA32, false);
        float c = (G - 1) / 2f;
        for (int y = 0; y < G; y++)
            for (int x = 0; x < G; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                a = Mathf.Max(a, Cov(d * c, 7f));                              // bright core
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        Save(tex, "WheelGlow.png");
    }

    static RectTransform MakeImage(Transform parent, string name, string sprite, Color color, Vector2 pos, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = Vector2.one * size;
        var img = go.GetComponent<UnityEngine.UI.Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Dir + sprite); img.color = color; img.raycastTarget = false;
        return rt;
    }

    [MenuItem("Tools/Wheel Menu Layout")]
    public static void Apply()
    {
        var group = GameObject.Find("Canvas").transform.Find("MenuRoot/MainButtonsGroup");
        var oldButtons = group.Find("Buttons");
        Transform Find(string n) { var t = group.Find(n); return t ? t : (oldButtons ? oldButtons.Find(n) : null); }
        var play = Find("PlayButton"); var quit = Find("QuitButton"); var opts = Find("OptionsButton");

        foreach (var t in new[] { play, opts, quit }) t.SetParent(group, false);
        if (oldButtons) Object.DestroyImmediate(oldButtons.gameObject);

        MakeOuterRing(); MakeInnerRing(); MakeGlow();
        foreach (var n in new[] { "WheelRing", "WheelRingOuter", "WheelRingInner", "WheelSelector" })
        { var o = group.Find(n); if (o) Object.DestroyImmediate(o.gameObject); }

        var center = new Vector2(-CenterX, 0f);
        float size = (Radius + 50f) * 2f;
        var outer = MakeImage(group, "WheelRingOuter", "WheelRingOuter.png", new Color(1f, 0.82f, 0.45f, 1f), center, size);
        var inner = MakeImage(group, "WheelRingInner", "WheelRingInner.png", new Color(Gold.r, Gold.g, Gold.b, 0.75f), center, size);
        var selector = MakeImage(group, "WheelSelector", "WheelGlow.png", new Color(1f, 0.82f, 0.45f, 0.95f), center, 84f);
        inner.SetSiblingIndex(0); outer.SetSiblingIndex(1);   // rings behind the buttons
        selector.SetSiblingIndex(2);

        var items = new[] { play, opts, quit };
        float[] angles = { AngleStep, 0f, -AngleStep };
        for (int i = 0; i < items.Length; i++)
        {
            var rt = (RectTransform)items[i];
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            float a = angles[i] * Mathf.Deg2Rad;
            rt.anchoredPosition = new Vector2(-CenterX + Radius * Mathf.Cos(a), Radius * Mathf.Sin(a));
            rt.sizeDelta = ButtonSize;
            var le = rt.GetComponent<UnityEngine.UI.LayoutElement>(); if (le) le.ignoreLayout = true;
            rt.Find("Label").GetComponent<TMPro.TMP_Text>().fontSize = 32;
        }

        var anim = group.GetComponent<MenuWheelAnimator>();
        if (!anim) anim = group.gameObject.AddComponent<MenuWheelAnimator>();
        var so = new SerializedObject(anim);
        so.FindProperty("outerRing").objectReferenceValue = outer;
        so.FindProperty("innerRing").objectReferenceValue = inner;
        so.FindProperty("selector").objectReferenceValue = selector;
        var arr = so.FindProperty("items"); arr.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("WHEEL: ok");
    }
}
