using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Applies the dark / parchment-gold look to the MainMenu uGUI and tidies the Options panel layout.
// Only edits properties of the existing hierarchy; nothing is recreated.
public static class MenuUiStyler
{
    static readonly Color Gold = new Color(0.82f, 0.66f, 0.34f, 1f);
    static readonly Color Parchment = new Color(0.94f, 0.88f, 0.72f, 1f);
    static readonly Color Dark = new Color(0.06f, 0.07f, 0.09f, 0.94f);

    static void Outline(GameObject g, Color c, float d)
    {
        var o = g.GetComponent<UnityEngine.UI.Outline>();
        if (!o) o = g.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = c; o.effectDistance = new Vector2(d, -d);
    }

    static void StyleButton(UnityEngine.UI.Button b, float fontSize, bool danger = false)
    {
        var img = b.GetComponent<UnityEngine.UI.Image>(); img.color = Color.white;
        var cb = b.colors;
        cb.normalColor = danger ? new Color(0.30f, 0.12f, 0.11f, 0.92f) : new Color(0.14f, 0.13f, 0.11f, 0.92f);
        cb.highlightedColor = danger ? new Color(0.55f, 0.20f, 0.17f, 1f) : new Color(0.45f, 0.36f, 0.20f, 1f);
        cb.pressedColor = new Color(0.30f, 0.24f, 0.14f, 1f);
        cb.selectedColor = cb.normalColor; cb.colorMultiplier = 1f; cb.fadeDuration = 0.1f;
        b.colors = cb;
        Outline(b.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1.5f);
        var t = b.GetComponentInChildren<TMP_Text>(true);
        if (t) { t.color = Parchment; t.fontSize = fontSize; t.fontStyle = FontStyles.Bold; t.characterSpacing = 3f; t.alignment = TextAlignmentOptions.Center; }
    }

    static void Layout(GameObject g, float prefW = -1, float prefH = -1, float flexW = -1, float minW = -1)
    {
        var le = g.GetComponent<LayoutElement>() ?? g.AddComponent<LayoutElement>();
        le.preferredWidth = prefW; le.preferredHeight = prefH; le.flexibleWidth = flexW; le.minWidth = minW;
    }

    static void Row(Transform row)
    {
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 16; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        Layout(row.gameObject, -1, 52, 1);
    }

    static void RowLabel(TMP_Text t, string text, float w)
    {
        t.text = text; t.color = Parchment; t.fontSize = 26; t.alignment = TextAlignmentOptions.MidlineLeft;
        Layout(t.gameObject, w, 44, 0, w);
    }

    static void ValueLabel(TMP_Text t)
    {
        t.color = Gold; t.fontSize = 26; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        Layout(t.gameObject, 150, 44, 1);
    }

    static void StyleToggle(UnityEngine.UI.Toggle tg)
    {
        Layout(tg.gameObject, 44, 44, 0, 44);
        var bg = tg.transform.Find("Background").GetComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.14f, 0.13f, 0.11f, 1f); Outline(bg.gameObject, Gold, 1.5f);
        var ck = tg.transform.Find("Background/Checkmark").GetComponent<UnityEngine.UI.Image>(); ck.color = Gold;
        tg.graphic = ck;
    }

    static void StyleSlider(UnityEngine.UI.Slider s)
    {
        Layout(s.gameObject, 320, 30, 1, 160);
        var bg = s.transform.Find("Background").GetComponent<UnityEngine.UI.Image>(); bg.color = new Color(0.14f, 0.13f, 0.11f, 1f);
        var fill = s.transform.Find("Fill Area/Fill").GetComponent<UnityEngine.UI.Image>(); fill.color = Gold;
        var handle = s.transform.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>(); handle.color = Parchment;
        handle.rectTransform.sizeDelta = new Vector2(16, 36);
    }

    [MenuItem("Tools/Style Menu UI")]
    public static void Apply()
    {
        var cv = GameObject.Find("Canvas").transform;
        var root = cv.Find("MenuRoot");

        // ---- main menu ----
        var title = root.Find("Title").GetComponent<TMP_Text>();
        title.color = Parchment; title.fontSize = 110; title.fontStyle = FontStyles.Bold; title.characterSpacing = 14f;
        title.rectTransform.anchoredPosition = new Vector2(0, -110);

        var buttons = root.Find("MainButtonsGroup/Buttons");
        foreach (var n in new[] { "PlayButton", "QuitButton" })
        {
            var b = buttons.Find(n);
            StyleButton(b.GetComponent<UnityEngine.UI.Button>(), 34, n == "QuitButton");
            var le = b.GetComponent<LayoutElement>(); le.preferredWidth = 380; le.preferredHeight = 72;
        }
        buttons.GetComponent<VerticalLayoutGroup>().spacing = 18;
        var play = buttons.Find("PlayButton/Label").GetComponent<TMP_Text>(); play.text = "JOUER";
        var quit = buttons.Find("QuitButton/Label").GetComponent<TMP_Text>(); quit.text = "QUITTER";

        var optBtn = root.Find("MainButtonsGroup/OptionsButton");
        StyleButton(optBtn.GetComponent<UnityEngine.UI.Button>(), 28);
        optBtn.Find("Label").GetComponent<TMP_Text>().text = "OPTIONS";

        // ---- options panel ----
        var panel = cv.Find("OptionsPanel");
        var pimg = panel.GetComponent<UnityEngine.UI.Image>(); pimg.color = Dark; Outline(panel.gameObject, Gold, 2f);
        ((RectTransform)panel).sizeDelta = new Vector2(820, 760);
        panel.SetAsLastSibling();

        var content = panel.Find("Content");
        var cr = (RectTransform)content; cr.offsetMin = new Vector2(48, 40); cr.offsetMax = new Vector2(-48, -36);
        var vl = content.GetComponent<VerticalLayoutGroup>();
        vl.spacing = 14; vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

        // direct-child headings
        foreach (Transform c in content)
        {
            var t = c.GetComponent<TMP_Text>(); if (!t) continue;
            string s = t.text.Trim();
            if (s == "Options") { t.text = "OPTIONS"; t.color = Parchment; t.fontSize = 48; t.fontStyle = FontStyles.Bold; t.characterSpacing = 8; t.alignment = TextAlignmentOptions.Center; Layout(c.gameObject, -1, 72, 1); }
            else if (s == "Video" || s == "Vidéo") { t.text = "VIDÉO"; t.color = Gold; t.fontSize = 28; t.fontStyle = FontStyles.Bold; t.characterSpacing = 6; t.alignment = TextAlignmentOptions.MidlineLeft; Layout(c.gameObject, -1, 44, 1); }
            else if (s == "Audio") { t.text = "AUDIO"; t.color = Gold; t.fontSize = 28; t.fontStyle = FontStyles.Bold; t.characterSpacing = 6; t.alignment = TextAlignmentOptions.MidlineLeft; Layout(c.gameObject, -1, 44, 1); }
        }

        var res = content.Find("ResolutionRow"); Row(res);
        RowLabel(res.GetChild(0).GetComponent<TMP_Text>(), "Résolution", 240);
        foreach (var n in new[] { 1, 3 }) { var b = res.GetChild(n); StyleButton(b.GetComponent<UnityEngine.UI.Button>(), 30); Layout(b.gameObject, 52, 44, 0, 52); }
        ValueLabel(res.GetChild(2).GetComponent<TMP_Text>());

        var full = content.Find("FullscreenRow"); Row(full);
        RowLabel(full.GetChild(0).GetComponent<TMP_Text>(), "Plein écran", 240);
        StyleToggle(full.Find("Toggle").GetComponent<UnityEngine.UI.Toggle>());

        var qual = content.Find("QualityRow"); Row(qual);
        RowLabel(qual.GetChild(0).GetComponent<TMP_Text>(), "Qualité", 240);
        foreach (var n in new[] { 1, 3 }) { var b = qual.GetChild(n); StyleButton(b.GetComponent<UnityEngine.UI.Button>(), 30); Layout(b.gameObject, 52, 44, 0, 52); }
        ValueLabel(qual.GetChild(2).GetComponent<TMP_Text>());

        var vs = content.Find("VSyncRow"); Row(vs);
        RowLabel(vs.GetChild(0).GetComponent<TMP_Text>(), "Synchro verticale", 240);
        StyleToggle(vs.Find("Toggle").GetComponent<UnityEngine.UI.Toggle>());

        var vol = content.Find("MasterVolumeRow"); Row(vol);
        RowLabel(vol.GetChild(0).GetComponent<TMP_Text>(), "Volume général", 240);
        StyleSlider(vol.Find("Slider").GetComponent<UnityEngine.UI.Slider>());
        var vv = vol.GetChild(2).GetComponent<TMP_Text>(); ValueLabel(vv); Layout(vv.gameObject, 90, 44, 0, 90);

        var spacer = content.Find("Spacer"); Layout(spacer.gameObject, -1, 10, 1); spacer.GetComponent<LayoutElement>().flexibleHeight = 1;
        var back = content.Find("BackButton");
        StyleButton(back.GetComponent<UnityEngine.UI.Button>(), 30);
        Layout(back.gameObject, 360, 64, 0); back.GetComponent<LayoutElement>().minHeight = 64;
        back.Find("Label").GetComponent<TMP_Text>().text = "RETOUR";
        content.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;

        LayoutRebuilder.ForceRebuildLayoutImmediate(cr);
        panel.gameObject.SetActive(false); root.Find("MainButtonsGroup").gameObject.SetActive(true);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("STYLED: ok");
    }
}
