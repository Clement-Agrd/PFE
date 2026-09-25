using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Core.Village.UI;

namespace Core.Village.EditorTools
{
    /// <summary>
    /// Construit (ou reconstruit) le panneau de bâtiment de la vue village dans la
    /// scène ouverte : hiérarchie uGUI, prefabs de lignes (coût / production) et
    /// câblage complet du BuildingPanelUI. Relançable ; tout passe par l'Undo.
    /// Menu : Tools > Village > Construire le panneau de bâtiment
    /// </summary>
    public static class BuildingPanelBuilder
    {
        private const string PanelName = "BuildingPanel";
        private const string CostRowPath = "Assets/Prefabs/BuildingCostRow.prefab";
        private const string ProductionRowPath = "Assets/Prefabs/BuildingProductionRow.prefab";
        private const int UILayer = 5;

        // Palette
        private static readonly Color PanelBg = new(0.086f, 0.102f, 0.129f, 0.96f);
        private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
        private static readonly Color Divider = new(1f, 1f, 1f, 0.1f);
        private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
        private static readonly Color TextMuted = new(0.62f, 0.66f, 0.72f);
        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color Green = new(0.45f, 0.85f, 0.45f);
        private static readonly Color ButtonGreen = new(0.27f, 0.62f, 0.32f);

        private static Sprite _rounded;
        private static Sprite _circle;

        [MenuItem("Tools/Village/Construire le panneau de bâtiment")]
        public static void Build()
        {
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            _circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            BuildingPanelUI controller = Object.FindObjectsByType<BuildingPanelUI>(FindObjectsInactive.Include).FirstOrDefault();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Panneau de bâtiment", "Aucun BuildingPanelUI trouvé dans la scène ouverte.", "OK");
                return;
            }

            var so = new SerializedObject(controller);
            GameObject oldPanel = so.FindProperty("panel").objectReferenceValue as GameObject;

            Canvas canvas = (oldPanel != null ? oldPanel.GetComponentInParent<Canvas>(true) : controller.GetComponentInParent<Canvas>(true))?.rootCanvas;
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Panneau de bâtiment", "Impossible de trouver le Canvas du panneau.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Construire le panneau de bâtiment");
            int undoGroup = Undo.GetCurrentGroup();

            // Barre de stock existante : on la récupère avant de toucher aux panneaux.
            ResourceBarUI resourceBar = canvas.GetComponentsInChildren<ResourceBarUI>(true).FirstOrDefault();
            if (resourceBar != null) Undo.SetTransformParent(resourceBar.transform, canvas.transform, "Déplacer la barre de stock");

            // Relance : on supprime le panneau généré précédemment.
            Transform previous = canvas.transform.Find(PanelName);
            if (previous != null)
            {
                if (oldPanel == previous.gameObject) oldPanel = null;
                Undo.DestroyObjectImmediate(previous.gameObject);
            }

            // Ancien panneau : désactivé et renommé, pas supprimé.
            if (oldPanel != null)
            {
                Undo.RecordObject(oldPanel, "Désactiver l'ancien panneau");
                oldPanel.SetActive(false);
                if (!oldPanel.name.EndsWith("(ancien)")) oldPanel.name += " (ancien)";
            }

            CostRowUI costRowPrefab = BuildCostRowPrefab();
            ProductionRowUI productionRowPrefab = BuildProductionRowPrefab();

            var refs = BuildPanel(canvas.transform, resourceBar);

            so.Update();
            Set(so, "panel", refs.Panel);
            Set(so, "panelGroup", refs.Group);
            Set(so, "iconImage", refs.Icon);
            Set(so, "nameLabel", refs.Name);
            Set(so, "levelLabel", refs.Level);
            Set(so, "levelPipsParent", refs.PipsParent);
            Set(so, "levelPipTemplate", refs.PipTemplate);
            Set(so, "productionSection", refs.ProductionSection);
            Set(so, "productionRowsParent", refs.ProductionRows);
            Set(so, "productionRowPrefab", productionRowPrefab);
            Set(so, "costSection", refs.CostSection);
            Set(so, "costRowsParent", refs.CostRows);
            Set(so, "costRowPrefab", costRowPrefab);
            Set(so, "requirementRow", refs.RequirementRow);
            Set(so, "requirementDot", refs.RequirementDot);
            Set(so, "requirementLabel", refs.RequirementLabel);
            Set(so, "maxLevelBanner", refs.MaxBanner);
            Set(so, "upgradeButton", refs.UpgradeButton);
            Set(so, "upgradeButtonImage", refs.UpgradeImage);
            Set(so, "upgradeButtonLabel", refs.UpgradeLabel);
            Set(so, "feedbackLabel", refs.Feedback);
            Set(so, "closeButton", refs.CloseButton);
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeGameObject = refs.Panel;
            EditorGUIUtility.PingObject(refs.Panel);
            Debug.Log("[BuildingPanelBuilder] Panneau de bâtiment construit et câblé. Pense à sauvegarder la scène.");
        }

        #region Panneau

        private struct PanelRefs
        {
            public GameObject Panel, ProductionSection, CostSection, RequirementRow, MaxBanner;
            public CanvasGroup Group;
            public UnityEngine.UI.Image Icon, PipTemplate, RequirementDot, UpgradeImage;
            public TMP_Text Name, Level, RequirementLabel, UpgradeLabel, Feedback;
            public Transform PipsParent, ProductionRows, CostRows;
            public UnityEngine.UI.Button UpgradeButton, CloseButton;
        }

        private static PanelRefs BuildPanel(Transform canvas, ResourceBarUI resourceBar)
        {
            var r = new PanelRefs();

            // Racine : ancrée à droite, centrée verticalement, hauteur = contenu.
            GameObject panel = NewUI(PanelName, canvas);
            Undo.RegisterCreatedObjectUndo(panel, "Créer le panneau de bâtiment");
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-48f, 0f);
            rt.sizeDelta = new Vector2(460f, 600f);

            var bg = AddImage(panel, PanelBg, _rounded);
            bg.raycastTarget = true; // bloque les clics vers le monde derrière le panneau
            var shadow = panel.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -6f);
            r.Group = panel.AddComponent<CanvasGroup>();

            var vlg = AddVertical(panel, 14f);
            vlg.padding = new RectOffset(22, 22, 20, 22);
            var fitter = panel.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            // --- En-tête : icône | nom + niveau | fermer ---
            GameObject header = NewUI("Header", panel.transform);
            var hlg = AddHorizontal(header, 14f);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            GameObject iconFrame = NewUI("IconFrame", header.transform);
            AddImage(iconFrame, CardBg, _rounded);
            Size(iconFrame, 72f, 72f);
            GameObject icon = NewUI("Icon", iconFrame.transform);
            Stretch(icon, 8f);
            r.Icon = AddImage(icon, Color.white, null);
            r.Icon.preserveAspect = true;

            GameObject titles = NewUI("Titles", header.transform);
            var titlesLayout = AddVertical(titles, 2f);
            titlesLayout.childAlignment = TextAnchor.MiddleLeft;
            Flexible(titles);
            r.Name = AddText(NewUI("Name", titles.transform), "Nom du bâtiment", 30f, TextMain, FontStyles.Bold);
            r.Name.overflowMode = TextOverflowModes.Ellipsis;
            r.Level = AddText(NewUI("Level", titles.transform), "Niveau 1 / 3", 18f, Gold, FontStyles.Bold);

            GameObject close = NewUI("CloseButton", header.transform);
            Size(close, 40f, 40f);
            var closeImage = AddImage(close, CardBg, _rounded);
            closeImage.raycastTarget = true;
            r.CloseButton = AddButton(close, closeImage);
            GameObject closeLabel = NewUI("Label", close.transform);
            Stretch(closeLabel, 0f);
            AddText(closeLabel, "X", 20f, TextMuted, FontStyles.Bold, TextAlignmentOptions.Center);

            // --- Pastilles de niveau ---
            GameObject pips = NewUI("LevelPips", panel.transform);
            var pipsLayout = AddHorizontal(pips, 6f);
            pipsLayout.childForceExpandWidth = true;
            Height(pips, 8f);
            r.PipsParent = pips.transform;
            GameObject pip = NewUI("PipTemplate", pips.transform);
            r.PipTemplate = AddImage(pip, Gold, _rounded);
            var pipLayout = pip.AddComponent<UnityEngine.UI.LayoutElement>();
            pipLayout.flexibleWidth = 1f;
            pipLayout.preferredHeight = 8f;
            pip.SetActive(false);

            AddDivider(panel.transform);

            // --- Production ---
            r.ProductionSection = AddSection(panel.transform, "ProductionSection", "PRODUCTION", out r.ProductionRows);

            // --- Coût ---
            r.CostSection = AddSection(panel.transform, "CostSection", "COÛT D'AMÉLIORATION", out r.CostRows);

            // --- Prérequis (Hôtel de Ville) ---
            GameObject req = NewUI("RequirementRow", panel.transform);
            AddImage(req, CardBg, _rounded);
            var reqLayout = AddHorizontal(req, 10f);
            reqLayout.padding = new RectOffset(12, 12, 10, 10);
            reqLayout.childAlignment = TextAnchor.MiddleLeft;
            GameObject dot = NewUI("Dot", req.transform);
            Size(dot, 12f, 12f);
            r.RequirementDot = AddImage(dot, Green, _circle);
            r.RequirementLabel = AddText(NewUI("Label", req.transform), "Hôtel de Ville niveau 2 requis (actuel : 1)", 16f, TextMain);
            r.RequirementLabel.textWrappingMode = TextWrappingModes.Normal;
            Flexible(r.RequirementLabel.gameObject);
            r.RequirementRow = req;

            // --- Bandeau niveau max ---
            GameObject max = NewUI("MaxLevelBanner", panel.transform);
            AddImage(max, new Color(Gold.r, Gold.g, Gold.b, 0.15f), _rounded);
            Height(max, 48f);
            GameObject maxLabel = NewUI("Label", max.transform);
            Stretch(maxLabel, 0f);
            AddText(maxLabel, "Niveau maximum atteint", 20f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            max.SetActive(false);
            r.MaxBanner = max;

            // --- Stock HDV (barre existante réutilisée) ---
            if (resourceBar != null)
            {
                GameObject stock = AddSection(panel.transform, "StockSection", "STOCK DE L'HÔTEL DE VILLE", out Transform stockContent);
                Undo.SetTransformParent(resourceBar.transform, stockContent, "Déplacer la barre de stock");
                ConvertStockToGrid(resourceBar.gameObject);
                stock.transform.SetAsLastSibling();
            }

            AddDivider(panel.transform);

            // --- Bouton d'amélioration + message d'état ---
            GameObject upgrade = NewUI("UpgradeButton", panel.transform);
            Height(upgrade, 56f);
            r.UpgradeImage = AddImage(upgrade, ButtonGreen, _rounded);
            r.UpgradeImage.raycastTarget = true;
            r.UpgradeButton = AddButton(upgrade, r.UpgradeImage);
            GameObject upgradeLabel = NewUI("Label", upgrade.transform);
            Stretch(upgradeLabel, 0f);
            r.UpgradeLabel = AddText(upgradeLabel, "Améliorer → Niveau 2", 22f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            GameObject feedback = NewUI("StatusLabel", panel.transform);
            r.Feedback = AddText(feedback, "Prêt à être amélioré", 16f, Green, FontStyles.Normal, TextAlignmentOptions.Center);
            r.Feedback.textWrappingMode = TextWrappingModes.Normal;
            feedback.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 22f;

            r.Panel = panel;
            return r;
        }

        private static GameObject AddSection(Transform parent, string name, string title, out Transform rows)
        {
            GameObject section = NewUI(name, parent);
            AddVertical(section, 8f);
            TMP_Text label = AddText(NewUI("Title", section.transform), title, 14f, TextMuted, FontStyles.Bold);
            label.characterSpacing = 4f;
            GameObject rowsGo = NewUI("Rows", section.transform);
            AddVertical(rowsGo, 6f);
            rows = rowsGo.transform;
            return section;
        }

        private static void AddDivider(Transform parent)
        {
            GameObject divider = NewUI("Divider", parent);
            AddImage(divider, Divider, null);
            Height(divider, 2f);
        }

        /// <summary>La barre de stock était une colonne : on la passe en grille 3 colonnes pour tenir dans le panneau.</summary>
        private static void ConvertStockToGrid(GameObject bar)
        {
            foreach (var group in bar.GetComponents<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>())
                Undo.DestroyObjectImmediate(group);

            if (!bar.TryGetComponent(out UnityEngine.UI.GridLayoutGroup grid))
                grid = Undo.AddComponent<UnityEngine.UI.GridLayoutGroup>(bar);
            Undo.RecordObject(grid, "Grille de stock");
            grid.cellSize = new Vector2(130f, 32f);
            grid.spacing = new Vector2(8f, 6f);
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        #endregion

        #region Prefabs de lignes

        private static CostRowUI BuildCostRowPrefab()
        {
            GameObject row = NewRow("BuildingCostRow", out UnityEngine.UI.Image icon);
            TMP_Text name = AddText(NewUI("Name", row.transform), "Ressource", 18f, TextMain);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(name.gameObject);
            TMP_Text amount = AddText(NewUI("Amount", row.transform), "0 / 20", 18f, Green, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            GameObject dot = NewUI("StatusDot", row.transform);
            Size(dot, 12f, 12f);
            var dotImage = AddImage(dot, Green, _circle);

            var ui = row.AddComponent<CostRowUI>();
            var so = new SerializedObject(ui);
            Set(so, "icon", icon);
            Set(so, "label", name);
            Set(so, "amountLabel", amount);
            Set(so, "statusDot", dotImage);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(row, CostRowPath).GetComponent<CostRowUI>();
        }

        private static ProductionRowUI BuildProductionRowPrefab()
        {
            GameObject row = NewRow("BuildingProductionRow", out UnityEngine.UI.Image icon);
            TMP_Text name = AddText(NewUI("Name", row.transform), "Ressource", 18f, TextMain);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(name.gameObject);
            TMP_Text current = AddText(NewUI("Current", row.transform), "30 /min", 18f, TextMuted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            TMP_Text next = AddText(NewUI("Next", row.transform), "→ 48 /min", 18f, Green, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            var ui = row.AddComponent<ProductionRowUI>();
            var so = new SerializedObject(ui);
            Set(so, "icon", icon);
            Set(so, "nameLabel", name);
            Set(so, "currentLabel", current);
            Set(so, "nextLabel", next);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(row, ProductionRowPath).GetComponent<ProductionRowUI>();
        }

        private static GameObject NewRow(string name, out UnityEngine.UI.Image icon)
        {
            GameObject row = NewUI(name, null);
            ((RectTransform)row.transform).sizeDelta = new Vector2(416f, 44f);
            AddImage(row, CardBg, _rounded);
            var hlg = AddHorizontal(row, 10f);
            hlg.padding = new RectOffset(10, 12, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var layout = row.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = layout.preferredHeight = 44f;

            GameObject iconGo = NewUI("Icon", row.transform);
            Size(iconGo, 30f, 30f);
            icon = AddImage(iconGo, Color.white, null);
            icon.preserveAspect = true;
            return row;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        #endregion

        #region Helpers uGUI

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
            if (prop == null) Debug.LogWarning($"[BuildingPanelBuilder] Champ '{property}' introuvable sur {so.targetObject.GetType().Name}.");
            else prop.objectReferenceValue = value;
        }

        #endregion
    }
}
