using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Core.TavernSystem.UI;

namespace Core.TavernSystem.EditorTools
{
    /// <summary>
    /// Construit (ou reconstruit) le panneau de taverne dans la scène ouverte :
    /// hiérarchie uGUI, prefab de ligne de plat et câblage complet du
    /// TavernPanelUI. Relançable ; tout passe par l'Undo.
    /// Même approche que BuildingPanelBuilder (vue village).
    /// Menu : Tools > Village > Construire le panneau de taverne
    /// </summary>
    public static class TavernPanelBuilder
    {
        private const string PanelName = "TavernPanel";
        private const string RowPrefabPath = "Assets/Prefabs/TavernDishRow.prefab";
        private const int UILayer = 5;

        // Palette (identique à BuildingPanelBuilder pour rester cohérent visuellement)
        private static readonly Color PanelBg = new(0.086f, 0.102f, 0.129f, 0.96f);
        private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
        private static readonly Color Divider = new(1f, 1f, 1f, 0.1f);
        private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
        private static readonly Color TextMuted = new(0.62f, 0.66f, 0.72f);
        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color Green = new(0.45f, 0.85f, 0.45f);
        private static readonly Color ButtonGreen = new(0.27f, 0.62f, 0.32f);
        private static readonly Color ButtonGreenDim = new(0.3f, 0.32f, 0.36f);

        private static Sprite _rounded;

        [MenuItem("Tools/Village/Construire le panneau de taverne")]
        public static void Build()
        {
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            TavernPanelUI controller = Object.FindObjectsByType<TavernPanelUI>(FindObjectsInactive.Include).FirstOrDefault();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Panneau de taverne", "Aucun TavernPanelUI trouvé dans la scène ouverte.", "OK");
                return;
            }

            var so = new SerializedObject(controller);
            GameObject oldPanel = so.FindProperty("panel").objectReferenceValue as GameObject;

            Canvas canvas = (oldPanel != null ? oldPanel.GetComponentInParent<Canvas>(true) : controller.GetComponentInParent<Canvas>(true))?.rootCanvas;
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Panneau de taverne", "Impossible de trouver le Canvas du panneau.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Construire le panneau de taverne");
            int undoGroup = Undo.GetCurrentGroup();

            // Relance : on supprime le panneau généré précédemment.
            Transform previous = canvas.transform.Find(PanelName);
            if (previous != null)
            {
                if (oldPanel == previous.gameObject) oldPanel = null;
                Undo.DestroyObjectImmediate(previous.gameObject);
            }

            // Ancien panneau (posé à la main ailleurs) : désactivé et renommé, pas supprimé.
            if (oldPanel != null)
            {
                Undo.RecordObject(oldPanel, "Désactiver l'ancien panneau");
                oldPanel.SetActive(false);
                if (!oldPanel.name.EndsWith("(ancien)")) oldPanel.name += " (ancien)";
            }

            TavernDishRowUI rowPrefab = BuildRowPrefab();
            var refs = BuildPanel(canvas.transform);

            so.Update();
            Set(so, "panel", refs.Panel);
            Set(so, "panelGroup", refs.Group);
            Set(so, "walletLabel", refs.Wallet);
            Set(so, "feedbackLabel", refs.Feedback);
            Set(so, "rowsParent", refs.Rows);
            Set(so, "rowPrefab", rowPrefab);
            Set(so, "closeButton", refs.CloseButton);
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeGameObject = refs.Panel;
            EditorGUIUtility.PingObject(refs.Panel);
            Debug.Log("[TavernPanelBuilder] Panneau de taverne construit et câblé. Pense à sauvegarder la scène.");
        }

        #region Panneau

        private struct PanelRefs
        {
            public GameObject Panel;
            public CanvasGroup Group;
            public TMP_Text Wallet, Feedback;
            public Transform Rows;
            public UnityEngine.UI.Button CloseButton;
        }

        private static PanelRefs BuildPanel(Transform canvas)
        {
            var r = new PanelRefs();

            // Racine : centrée à l'écran, hauteur = contenu (comme BuildingPanel mais centré,
            // puisqu'il n'y a pas de sélection de bâtiment à laisser visible derrière).
            GameObject panel = NewUI(PanelName, canvas);
            Undo.RegisterCreatedObjectUndo(panel, "Créer le panneau de taverne");
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(560f, 640f);

            var bg = AddImage(panel, PanelBg, _rounded);
            bg.raycastTarget = true; // bloque les clics vers le monde derrière le panneau
            var shadow = panel.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -6f);
            r.Group = panel.AddComponent<CanvasGroup>();

            var vlg = AddVertical(panel, 14f);
            vlg.padding = new RectOffset(24, 24, 22, 22);
            var fitter = panel.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            // --- En-tête : titre + solde | fermer ---
            GameObject header = NewUI("Header", panel.transform);
            var hlg = AddHorizontal(header, 14f);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            GameObject titles = NewUI("Titles", header.transform);
            var titlesLayout = AddVertical(titles, 2f);
            titlesLayout.childAlignment = TextAnchor.MiddleLeft;
            Flexible(titles);
            AddText(NewUI("Title", titles.transform), "Taverne", 30f, TextMain, FontStyles.Bold);
            r.Wallet = AddText(NewUI("Wallet", titles.transform), "Or : 0", 18f, Gold, FontStyles.Bold);

            GameObject close = NewUI("CloseButton", header.transform);
            Size(close, 40f, 40f);
            var closeImage = AddImage(close, CardBg, _rounded);
            closeImage.raycastTarget = true;
            r.CloseButton = AddButton(close, closeImage);
            GameObject closeLabel = NewUI("Label", close.transform);
            Stretch(closeLabel, 0f);
            AddText(closeLabel, "X", 20f, TextMuted, FontStyles.Bold, TextAlignmentOptions.Center);

            AddDivider(panel.transform);

            // --- Carte : une ligne par plat ---
            GameObject rows = NewUI("Rows", panel.transform);
            AddVertical(rows, 8f);
            r.Rows = rows.transform;

            AddDivider(panel.transform);

            // --- Message d'état (succès / erreur de commande) ---
            GameObject feedback = NewUI("FeedbackLabel", panel.transform);
            r.Feedback = AddText(feedback, "", 16f, Green, FontStyles.Normal, TextAlignmentOptions.Center);
            r.Feedback.textWrappingMode = TextWrappingModes.Normal;
            feedback.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 22f;

            r.Panel = panel;
            return r;
        }

        private static void AddDivider(Transform parent)
        {
            GameObject divider = NewUI("Divider", parent);
            AddImage(divider, Divider, null);
            Height(divider, 2f);
        }

        #endregion

        #region Prefab de ligne

        private static TavernDishRowUI BuildRowPrefab()
        {
            GameObject row = NewUI("TavernDishRow", null);
            ((RectTransform)row.transform).sizeDelta = new Vector2(512f, 72f);
            AddImage(row, CardBg, _rounded);
            var hlg = AddHorizontal(row, 12f);
            hlg.padding = new RectOffset(12, 12, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var rowLayout = row.AddComponent<UnityEngine.UI.LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = 72f;

            GameObject iconFrame = NewUI("IconFrame", row.transform);
            AddImage(iconFrame, new Color(1f, 1f, 1f, 0.04f), _rounded);
            Size(iconFrame, 56f, 56f);
            GameObject iconGo = NewUI("Icon", iconFrame.transform);
            Stretch(iconGo, 6f);
            var icon = AddImage(iconGo, Color.white, null);
            icon.preserveAspect = true;

            GameObject texts = NewUI("Texts", row.transform);
            var textsLayout = AddVertical(texts, 2f);
            textsLayout.childAlignment = TextAnchor.MiddleLeft;
            Flexible(texts);
            TMP_Text name = AddText(NewUI("Name", texts.transform), "Plat", 18f, TextMain, FontStyles.Bold);
            name.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text description = AddText(NewUI("Description", texts.transform), "Description", 14f, TextMuted);
            description.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text cost = AddText(NewUI("Cost", texts.transform), "0 Or — 0s", 14f, Gold, FontStyles.Bold);

            GameObject orderButton = NewUI("OrderButton", row.transform);
            Size(orderButton, 128f, 44f);
            var orderImage = AddImage(orderButton, ButtonGreen, _rounded);
            orderImage.raycastTarget = true;
            var button = AddButton(orderButton, orderImage);
            GameObject orderLabel = NewUI("Label", orderButton.transform);
            Stretch(orderLabel, 0f);
            AddText(orderLabel, "Commander", 16f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            var ui = row.AddComponent<TavernDishRowUI>();
            var so = new SerializedObject(ui);
            Set(so, "icon", icon);
            Set(so, "nameLabel", name);
            Set(so, "descriptionLabel", description);
            Set(so, "costLabel", cost);
            Set(so, "orderButton", button);
            Set(so, "orderButtonImage", orderImage);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(row, RowPrefabPath).GetComponent<TavernDishRowUI>();
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        #endregion

        #region Helpers uGUI (identiques à BuildingPanelBuilder)

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static UnityEngine.UI.Image AddImage(GameObject go, Color color, Sprite sprite)
        {
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text AddText(GameObject go, string text, float size, Color color,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static UnityEngine.UI.Button AddButton(GameObject go, UnityEngine.UI.Image target)
        {
            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = target;
            var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.85f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            return button;
        }

        private static UnityEngine.UI.VerticalLayoutGroup AddVertical(GameObject go, float spacing)
        {
            var layout = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private static UnityEngine.UI.HorizontalLayoutGroup AddHorizontal(GameObject go, float spacing)
        {
            var layout = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layout;
        }

        private static void Size(GameObject go, float width, float height)
        {
            var layout = GetLayout(go);
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = height;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
        }

        private static void Height(GameObject go, float height)
        {
            var layout = GetLayout(go);
            layout.minHeight = layout.preferredHeight = height;
        }

        private static void Flexible(GameObject go)
        {
            var layout = GetLayout(go);
            layout.flexibleWidth = 1f;
        }

        private static UnityEngine.UI.LayoutElement GetLayout(GameObject go)
            => go.TryGetComponent(out UnityEngine.UI.LayoutElement layout) ? layout : go.AddComponent<UnityEngine.UI.LayoutElement>();

        private static void Stretch(GameObject go, float inset)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Set(SerializedObject so, string property, Object value)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop == null) Debug.LogWarning($"[TavernPanelBuilder] Champ '{property}' introuvable sur {so.targetObject.GetType().Name}.");
            else prop.objectReferenceValue = value;
        }

        #endregion
    }
}
