using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using Core.Minimap;
using Core.InventorySystem.UI;

/// <summary>
/// Construit (ou reconstruit) la minimap HUD, l'onglet "Carte" de l'inventaire
/// et les marqueurs dans la scène ouverte. Relançable sans doublons.
/// </summary>
public static class MinimapSceneBuilder
{
    [MenuItem("Tools/Minimap/Build in open scene")]
    public static void MenuBuild() => Debug.Log(Build());

    public static string Build()
    {
        var sb = new System.Text.StringBuilder();
        var canvas = GameObject.Find("Canvas");
        var invPanel = GameObject.Find("InventoryPanel");
        if (canvas == null || invPanel == null) return "Canvas ou InventoryPanel introuvable.";

        RectTransform NewRect(string n, Transform parent)
        {
            var g = new GameObject(n, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            return (RectTransform)g.transform;
        }
        void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }
        TextMeshProUGUI Txt(string n, Transform p, string text, float fs, Color c, TextAlignmentOptions al, Vector2 pos, Vector2 size)
        {
            var r = NewRect(n, p);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = fs; t.color = c; t.alignment = al; t.raycastTarget = false;
            return t;
        }

        // nettoyage d'un build précédent
        foreach (var n in new[] { "MapContent", "MapTabButton", "MinimapHUD", "MinimapHUDFrame", "MinimapSystem" })
        {
            var o = GameObject.Find(n);
            if (o != null) Object.DestroyImmediate(o);
        }
        foreach (var m in Object.FindObjectsByType<MinimapMarker>(FindObjectsInactive.Include))
            Object.DestroyImmediate(m);
        var oldOp = canvas.GetComponent<MinimapOpener>();
        if (oldOp != null) Object.DestroyImmediate(oldOp);

        // marqueurs
        void Mark(string go, MarkerType t, string label)
        {
            var g = GameObject.Find(go);
            if (g == null) { sb.Append("introuvable: " + go + "; "); return; }
            var m = g.AddComponent<MinimapMarker>();
            var so = new SerializedObject(m);
            so.FindProperty("type").enumValueIndex = (int)t;
            so.FindProperty("label").stringValue = label;
            so.ApplyModifiedProperties();
        }
        Mark("Ferme", MarkerType.Building, "Ferme");
        Mark("Mine", MarkerType.Building, "Mine");
        Mark("Scierie", MarkerType.Building, "Scierie");
        Mark("Taverne", MarkerType.Tavern, "Taverne");
        Mark("Poste_Expedition", MarkerType.Expedition, "Expéditions");
        Mark("herse", MarkerType.Gate, "Herse");
        Mark("Proto_Table_", MarkerType.Building, "Table du village");
        Mark("maison_de_flavio", MarkerType.Building, "Maison");
        Mark("maison_de_flavio (1)", MarkerType.Building, "Maison");
        Mark("maison_de_flavio (2)", MarkerType.Building, "Maison");
        Mark("Tower", MarkerType.Tower, null);
        Mark("Tower (1)", MarkerType.Tower, null);
        Mark("Tower (2)", MarkerType.Tower, null);
        Mark("SpawnZone_1", MarkerType.EnemySpawn, "Zone ennemie");
        Mark("SpawnZone_2", MarkerType.EnemySpawn, "Zone ennemie");

        // système
        var sysGo = new GameObject("MinimapSystem");
        var sys = sysGo.AddComponent<MinimapSystem>();
        var player = GameObject.Find("Player");
        var sso = new SerializedObject(sys);
        if (player != null) sso.FindProperty("player").objectReferenceValue = player.transform;
        sso.ApplyModifiedProperties();

        // onglets
        var invTab = GameObject.Find("InventoryTabButton");
        var vilTab = GameObject.Find("VillageTabButton");
        ((RectTransform)invTab.transform).anchoredPosition = new Vector2(-240, -24);
        ((RectTransform)vilTab.transform).anchoredPosition = new Vector2(0, -24);
        var mapTab = Object.Instantiate(vilTab, vilTab.transform.parent);
        mapTab.name = "MapTabButton";
        ((RectTransform)mapTab.transform).anchoredPosition = new Vector2(240, -24);
        mapTab.GetComponentInChildren<TMP_Text>().text = "Carte";
        mapTab.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
        mapTab.transform.localScale = Vector3.one;

        // contenu de la grande carte
        var mc = NewRect("MapContent", invPanel.transform);
        Stretch(mc);
        mc.gameObject.SetActive(false);
        var grey = new Color(0.62f, 0.66f, 0.72f);
        Txt("MapTitle", mc, "CARTE DU MONDE", 20, grey, TextAlignmentOptions.MidlineLeft, new Vector2(-462, 340), new Vector2(600, 30));

        var frame = NewRect("MapFrame", mc);
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition = new Vector2(-250, -40); frame.sizeDelta = new Vector2(1224, 699);
        var fimg = frame.gameObject.AddComponent<Image>();
        fimg.color = new Color(0.30f, 0.27f, 0.18f); fimg.raycastTarget = false;

        var viewR = NewRect("MapView", frame);
        viewR.anchorMin = viewR.anchorMax = new Vector2(0.5f, 0.5f);
        viewR.anchoredPosition = Vector2.zero; viewR.sizeDelta = new Vector2(1200, 675);
        var vimg = viewR.gameObject.AddComponent<Image>();
        vimg.color = new Color(0, 0, 0, 0.01f); // reçoit glisser / molette
        viewR.gameObject.AddComponent<RectMask2D>();
        var raw = NewRect("MapImage", viewR); Stretch(raw);
        var rawImg = raw.gameObject.AddComponent<RawImage>(); rawImg.raycastTarget = false; rawImg.color = new Color(0.56f, 0.66f, 0.6f);
        var layer = NewRect("Markers", viewR); Stretch(layer);
        var worldView = viewR.gameObject.AddComponent<MinimapView>();
        var wso = new SerializedObject(worldView);
        wso.FindProperty("mode").enumValueIndex = (int)MinimapView.ViewMode.World;
        wso.FindProperty("image").objectReferenceValue = rawImg;
        wso.FindProperty("markerLayer").objectReferenceValue = layer;
        wso.ApplyModifiedProperties();
        Txt("North", viewR, "N", 30, new Color(0.95f, 0.78f, 0.35f), TextAlignmentOptions.Center, new Vector2(0, 310), new Vector2(40, 40));

        // légende
        var col = NewRect("LegendColumn", mc);
        col.anchorMin = col.anchorMax = new Vector2(0.5f, 0.5f);
        col.anchoredPosition = new Vector2(650, -40); col.sizeDelta = new Vector2(420, 699);
        var cimg = col.gameObject.AddComponent<Image>();
        cimg.color = new Color(0.16f, 0.165f, 0.2f); cimg.raycastTarget = false;
        Txt("LegendTitle", col, "LÉGENDE", 20, grey, TextAlignmentOptions.MidlineLeft, new Vector2(0, 320), new Vector2(380, 30));
        var rows = NewRect("Rows", col);
        rows.anchorMin = rows.anchorMax = new Vector2(0.5f, 1f); rows.pivot = new Vector2(0.5f, 1f);
        rows.anchoredPosition = new Vector2(0, -60); rows.sizeDelta = new Vector2(380, 340);
        var vl = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 2; vl.childControlWidth = true; vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        var legend = rows.gameObject.AddComponent<MinimapLegendUI>();
        var lso = new SerializedObject(legend);
        lso.FindProperty("rowsParent").objectReferenceValue = rows;
        lso.ApplyModifiedProperties();
        Txt("Help", col, "Molette : zoom\nGlisser : déplacer la carte\nM : ouvrir / fermer", 20, grey, TextAlignmentOptions.TopLeft, new Vector2(0, -110), new Vector2(380, 110));

        Button MakeBtn(string n, string label, Vector2 pos, Vector2 size, float fs)
        {
            var r = NewRect(n, col);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            var im = r.gameObject.AddComponent<Image>();
            im.color = new Color(0.3f, 0.32f, 0.36f);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = im;
            var t = Txt("Label", r, label, fs, new Color(0.85f, 0.87f, 0.9f), TextAlignmentOptions.Center, Vector2.zero, size);
            Stretch((RectTransform)t.transform);
            return b;
        }
        var bPlus = MakeBtn("ZoomInButton", "+", new Vector2(-135, -235), new Vector2(110, 56), 34);
        var bMinus = MakeBtn("ZoomOutButton", "−", new Vector2(0, -235), new Vector2(110, 56), 34);
        var bCenter = MakeBtn("CenterButton", "Moi", new Vector2(135, -235), new Vector2(110, 56), 26);
        UnityEventTools.AddPersistentListener(bPlus.onClick, worldView.ZoomIn);
        UnityEventTools.AddPersistentListener(bMinus.onClick, worldView.ZoomOut);
        UnityEventTools.AddPersistentListener(bCenter.onClick, worldView.CenterOnPlayer);

        // minimap HUD (coin haut-droit)
        var hud = NewRect("MinimapHUD", canvas.transform);
        hud.anchorMin = hud.anchorMax = new Vector2(1, 1); hud.pivot = new Vector2(1, 1);
        hud.anchoredPosition = new Vector2(-30, -30); hud.sizeDelta = new Vector2(240, 240);
        var back = hud.gameObject.AddComponent<Image>();
        hud.gameObject.AddComponent<MinimapSpriteAssigner>(); back.sprite = MinimapSprites.Circle; back.color = new Color(0.10f, 0.16f, 0.12f);
        var msk = hud.gameObject.AddComponent<Mask>(); msk.showMaskGraphic = true;
        var mraw = NewRect("MiniImage", hud); Stretch(mraw);
        var mrawImg = mraw.gameObject.AddComponent<RawImage>(); mrawImg.raycastTarget = false; mrawImg.color = new Color(0.56f, 0.66f, 0.6f);
        var mlayer = NewRect("Markers", hud); Stretch(mlayer);
        var miniView = hud.gameObject.AddComponent<MinimapView>();
        var mso = new SerializedObject(miniView);
        mso.FindProperty("mode").enumValueIndex = (int)MinimapView.ViewMode.Mini;
        mso.FindProperty("image").objectReferenceValue = mrawImg;
        mso.FindProperty("markerLayer").objectReferenceValue = mlayer;
        mso.FindProperty("edgeRadius").floatValue = 106f;
        mso.ApplyModifiedProperties();

        var frameH = NewRect("MinimapHUDFrame", canvas.transform);
        frameH.anchorMin = frameH.anchorMax = new Vector2(1, 1); frameH.pivot = new Vector2(1, 1);
        frameH.anchoredPosition = new Vector2(-30, -30); frameH.sizeDelta = new Vector2(240, 240);
        var ring = frameH.gameObject.AddComponent<Image>();
        var ringAssign = frameH.gameObject.AddComponent<MinimapSpriteAssigner>(); new SerializedObject(ringAssign).FindProperty("kind").enumValueIndex = (int)MinimapSpriteAssigner.Kind.Ring; ring.sprite = MinimapSprites.Ring; ring.color = new Color(0.95f, 0.78f, 0.35f); ring.raycastTarget = false;
        Txt("N", frameH, "N", 22, new Color(0.95f, 0.78f, 0.35f), TextAlignmentOptions.Center, new Vector2(0, 100), new Vector2(30, 30));
        Txt("Hint", frameH, "[M] Carte", 18, new Color(0.85f, 0.87f, 0.9f), TextAlignmentOptions.Center, new Vector2(0, -135), new Vector2(200, 26));
        frameH.SetSiblingIndex(hud.GetSiblingIndex() + 1);

        // branchements
        var opener = canvas.AddComponent<MinimapOpener>();
        var oso = new SerializedObject(opener);
        oso.FindProperty("inventoryToggle").objectReferenceValue = Object.FindAnyObjectByType<InventoryToggle>(FindObjectsInactive.Include);
        var tc = invPanel.GetComponent<InventoryTabController>();
        oso.FindProperty("tabController").objectReferenceValue = tc;
        oso.ApplyModifiedProperties();

        var clicked = new SerializedObject(miniView).FindProperty("onClicked");
        var evField = typeof(MinimapView).GetField("onClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        UnityEventTools.AddPersistentListener((UnityEngine.Events.UnityEvent)evField.GetValue(miniView), opener.Open);

        // la minimap se masque quand un autre panneau est ouvert (pas le futur panneau de pause)
        var hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
        var frameGroup = frameH.gameObject.AddComponent<CanvasGroup>();
        var vis = hud.gameObject.AddComponent<MinimapHUDVisibility>();
        var vso = new SerializedObject(vis);
        var tg = vso.FindProperty("targets");
        tg.arraySize = 2;
        tg.GetArrayElementAtIndex(0).objectReferenceValue = hudGroup;
        tg.GetArrayElementAtIndex(1).objectReferenceValue = frameGroup;
        vso.FindProperty("inventoryToggle").objectReferenceValue = Object.FindAnyObjectByType<InventoryToggle>(FindObjectsInactive.Include);
        vso.FindProperty("villageView").objectReferenceValue = Object.FindAnyObjectByType<Core.Village.VillageViewController>(FindObjectsInactive.Include);
        var panelNames = new[] { "BuildingPanel", "TavernPanel", "ExplorationPanel" };
        var found = new System.Collections.Generic.List<GameObject>();
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.gameObject.scene.IsValid() && System.Array.IndexOf(panelNames, t.name) >= 0 && t.IsChildOf(canvas.transform))
                found.Add(t.gameObject);
        var bp = vso.FindProperty("blockingPanels");
        bp.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++) bp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        vso.ApplyModifiedProperties();
        sb.Append("panneaux bloquants: " + found.Count + "; ");

        var tso = new SerializedObject(tc);
        tso.FindProperty("mapContent").objectReferenceValue = mc.gameObject;
        tso.FindProperty("mapTabButton").objectReferenceValue = mapTab.GetComponent<Button>();
        tso.ApplyModifiedProperties();

        EditorUtility.SetDirty(miniView);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return "Minimap construite. " + sb;
    }
}
