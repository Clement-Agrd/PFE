using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Core.Village.Exploration;
using Core.Village.UI;

namespace Core.Village.EditorTools
{
    /// <summary>
    /// Construit (ou reconstruit) le contenu de l'onglet "Village" de l'inventaire :
    /// titre + barre de ressources de l'HDV + une carte par bâtiment. Récupère
    /// le VillageOverviewPanelUI, la ResourceBarUI et l'InventoryTabController
    /// déjà présents dans la scène ouverte et recâble tout. Relançable ; tout
    /// passe par l'Undo.
    /// Menu : Tools > Village > Construire l'onglet Village
    /// </summary>
    public static class VillageOverviewBuilder
    {
        private const string ChipPath = "Assets/Prefabs/VillageResourceChip.prefab";
        private const string RowPath = "Assets/Prefabs/BuildingSummaryRow.prefab";
        private const string ExpeditionRowPath = "Assets/Prefabs/ExpeditionStatusRow.prefab";
        private const int UILayer = 5;

        private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
        private static readonly Color IconBg = new(0f, 0f, 0f, 0.28f);
        private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
        private static readonly Color TextMuted = new(0.62f, 0.66f, 0.72f);
        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color Green = new(0.55f, 0.88f, 0.5f);

        private static Sprite _rounded;

        [MenuItem("Tools/Village/Construire l'onglet Village")]
        public static void Build()
        {
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            VillageOverviewPanelUI overview = Object.FindObjectsByType<VillageOverviewPanelUI>(FindObjectsInactive.Include).FirstOrDefault();
            if (overview == null)
            {
                EditorUtility.DisplayDialog("Onglet Village", "Aucun VillageOverviewPanelUI trouvé dans la scène ouverte.", "OK");
                return;
            }

            Transform inventoryPanel = overview.transform.parent;
            Component tabController = inventoryPanel.GetComponent("InventoryTabController");
            if (tabController == null)
            {
                EditorUtility.DisplayDialog("Onglet Village", "InventoryTabController introuvable sur le parent du contenu Village.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Construire l'onglet Village");
            int undoGroup = Undo.GetCurrentGroup();

            // On garde ce qui est câblé : manager, barre de ressources, boutons d'onglet.
            VillageManager manager = new SerializedObject(overview).FindProperty("villageManager").objectReferenceValue as VillageManager;
            ResourceBarUI resourceBar = overview.GetComponentInChildren<ResourceBarUI>(true);

            ResourceBarRowUI chipPrefab = BuildChipPrefab();
            BuildingSummaryRowUI rowPrefab = BuildRowPrefab();
            ExpeditionStatusRowUI expeditionRowPrefab = BuildExpeditionRowPrefab();

            // Le contenu doit être un RectTransform plein cadre : l'ancien était un simple Transform,
            // ce qui faussait tous les ancrages (tout tombait en bas de l'écran).
            bool wasActive = overview.gameObject.activeSelf;
            GameObject content = NewUI("VillageContent", inventoryPanel);
            Undo.RegisterCreatedObjectUndo(content, "Créer VillageContent");
            Stretch(content, 0f);
            content.transform.SetSiblingIndex(overview.transform.GetSiblingIndex());

            GameObject column = NewUI("Column", content.transform);
            var colRt = (RectTransform)column.transform;
            colRt.anchorMin = colRt.anchorMax = new Vector2(0.5f, 1f);
            colRt.pivot = new Vector2(0.5f, 1f);
            colRt.sizeDelta = new Vector2(1100f, 0f);
            colRt.anchoredPosition = new Vector2(0f, -120f);
            AddVertical(column, 10f);
            column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddTitle(column.transform, "ResourceTitle", "RESSOURCES DE L'HÔTEL DE VILLE");

            GameObject barGo;
            if (resourceBar != null)
            {
                barGo = resourceBar.gameObject;
                Undo.SetTransformParent(barGo.transform, column.transform, "Déplacer la barre de ressources");
                foreach (var grid in barGo.GetComponents<GridLayoutGroup>()) Undo.DestroyObjectImmediate(grid);
            }
            else barGo = NewUI("ResourceBarRoot", column.transform);

            if (!barGo.TryGetComponent(out HorizontalLayoutGroup bar)) bar = Undo.AddComponent<HorizontalLayoutGroup>(barGo);
            bar.spacing = 12f;
            bar.childAlignment = TextAnchor.MiddleLeft;
            bar.childControlWidth = bar.childControlHeight = true;
            bar.childForceExpandWidth = bar.childForceExpandHeight = false;
            Height(barGo, 64f);

            Height(NewUI("Gap", column.transform), 14f);

            BuildExpeditionSection(column.transform, expeditionRowPrefab);

            Height(NewUI("Gap", column.transform), 14f);

            AddTitle(column.transform, "BuildingsTitle", "BÂTIMENTS DU VILLAGE");

            GameObject rows = NewUI("BuildingsRows", column.transform);
            AddVertical(rows, 8f);

            // Câblage
            VillageOverviewPanelUI newOverview = Undo.AddComponent<VillageOverviewPanelUI>(content);
            var so = new SerializedObject(newOverview);
            Set(so, "villageManager", manager);
            Set(so, "buildingRowsParent", rows.transform);
            Set(so, "rowPrefab", rowPrefab);
            so.ApplyModifiedProperties();

            if (resourceBar != null)
            {
                var barSo = new SerializedObject(resourceBar);
                Set(barSo, "rowsParent", barGo.transform);
                Set(barSo, "rowPrefab", chipPrefab);
                barSo.ApplyModifiedProperties();
            }

            var tabSo = new SerializedObject(tabController);
            Set(tabSo, "villageContent", content);
            Set(tabSo, "villageOverviewPanel", newOverview);
            tabSo.ApplyModifiedProperties();

            FixTabButton(tabSo.FindProperty("inventoryTabButton").objectReferenceValue as Button, new Vector2(-120f, -24f), "Inventaire");
            FixTabButton(tabSo.FindProperty("villageTabButton").objectReferenceValue as Button, new Vector2(120f, -24f), "Village");

            content.SetActive(wasActive);
            Undo.DestroyObjectImmediate(overview.gameObject);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(inventoryPanel.gameObject.scene);
            Selection.activeGameObject = content;
            Debug.Log("[VillageOverviewBuilder] Onglet Village construit et câblé. Pense à sauvegarder la scène.");
        }

        #region Prefabs

        /// <summary>Pastille de ressource : icône + quantité, largeur selon le contenu.</summary>
        private static ResourceBarRowUI BuildChipPrefab()
        {
            GameObject root = NewUI("VillageResourceChip", null);
            ((RectTransform)root.transform).sizeDelta = new Vector2(160f, 64f);
            AddImage(root, CardBg, _rounded);
            HorizontalLayoutGroup hlg = AddHorizontal(root, 10f);
            hlg.padding = new RectOffset(14, 18, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            Height(root, 64f);

            GameObject iconGo = NewUI("Icon", root.transform);
            Size(iconGo, 40f, 40f);
            Image icon = AddImage(iconGo, Color.white, null);
            icon.preserveAspect = true;

            GameObject valueGo = NewUI("Value", root.transform);
            TMP_Text value = AddText(valueGo, "0", 28f, TextMain, FontStyles.Bold);

            ResourceBarRowUI row = root.AddComponent<ResourceBarRowUI>();
            var so = new SerializedObject(row);
            Set(so, "icon", icon);
            Set(so, "valueLabel", value);
            so.ApplyModifiedProperties();

            return SavePrefab(root, ChipPath).GetComponent<ResourceBarRowUI>();
        }

        /// <summary>Titre + résumé (« Aucune expédition » / « N en cours »), carte « vide » et lignes d'expédition.</summary>
        private static void BuildExpeditionSection(Transform column, ExpeditionStatusRowUI rowPrefab)
        {
            GameObject section = NewUI("ExpeditionSection", column);
            AddVertical(section, 10f);

            GameObject header = NewUI("Header", section.transform);
            AddHorizontal(header, 12f).childAlignment = TextAnchor.MiddleLeft;
            Height(header, 30f);
            GameObject titleGo = NewUI("ExpeditionTitle", header.transform);
            TMP_Text title = AddText(titleGo, "EXPÉDITIONS", 20f, TextMuted, FontStyles.Bold);
            title.characterSpacing = 4f;
            Flexible(titleGo);
            TMP_Text summary = AddText(NewUI("Summary", header.transform), "Aucune expédition", 22f, TextMuted, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            GameObject empty = NewUI("EmptyCard", section.transform);
            AddImage(empty, CardBg, _rounded);
            Height(empty, 64f);
            HorizontalLayoutGroup emptyLayout = AddHorizontal(empty, 0f);
            emptyLayout.padding = new RectOffset(20, 20, 8, 8);
            emptyLayout.childAlignment = TextAnchor.MiddleLeft;
            AddText(NewUI("Label", empty.transform), "Aucun héros n'est parti en expédition.", 24f, TextMuted);

            GameObject rows = NewUI("ExpeditionRows", section.transform);
            AddVertical(rows, 8f);

            ExpeditionStatusUI status = Undo.AddComponent<ExpeditionStatusUI>(section);
            ExplorationManager manager = Object.FindObjectsByType<ExplorationManager>(FindObjectsInactive.Include).FirstOrDefault();
            var so = new SerializedObject(status);
            Set(so, "manager", manager);
            Set(so, "summaryLabel", summary);
            Set(so, "emptyCard", empty);
            Set(so, "rowsParent", rows.transform);
            Set(so, "rowPrefab", rowPrefab);
            so.ApplyModifiedProperties();
            if (manager == null) Debug.LogWarning("[VillageOverviewBuilder] Aucun ExplorationManager dans la scène : la section Expéditions restera vide.");
        }

        /// <summary>Carte d'une expédition : [héros → mission] ........ [état].</summary>
        private static ExpeditionStatusRowUI BuildExpeditionRowPrefab()
        {
            GameObject root = NewUI("ExpeditionStatusRow", null);
            ((RectTransform)root.transform).sizeDelta = new Vector2(1100f, 64f);
            AddImage(root, CardBg, _rounded);
            HorizontalLayoutGroup hlg = AddHorizontal(root, 18f);
            hlg.padding = new RectOffset(20, 28, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            Height(root, 64f);

            GameObject titleGo = NewUI("Title", root.transform);
            Flexible(titleGo);
            TMP_Text title = AddText(titleGo, "Héros → Mission", 26f, TextMain, FontStyles.Bold);
            TMP_Text status = AddText(NewUI("Status", root.transform), "État", 24f, Gold, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            ExpeditionStatusRowUI row = root.AddComponent<ExpeditionStatusRowUI>();
            var so = new SerializedObject(row);
            Set(so, "titleLabel", title);
            Set(so, "statusLabel", status);
            so.ApplyModifiedProperties();

            return SavePrefab(root, ExpeditionRowPath).GetComponent<ExpeditionStatusRowUI>();
        }

        /// <summary>Carte d'un bâtiment : [icône] [nom / niveau] ........ [production].</summary>
        private static BuildingSummaryRowUI BuildRowPrefab()
        {
            GameObject root = NewUI("BuildingSummaryRow", null);
            ((RectTransform)root.transform).sizeDelta = new Vector2(1100f, 80f);
            AddImage(root, CardBg, _rounded);
            HorizontalLayoutGroup hlg = AddHorizontal(root, 18f);
            hlg.padding = new RectOffset(14, 28, 10, 10);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            Height(root, 80f);

            GameObject frame = NewUI("IconFrame", root.transform);
            Size(frame, 60f, 60f);
            AddImage(frame, IconBg, _rounded);
            GameObject iconGo = NewUI("Icon", frame.transform);
            Stretch(iconGo, 6f);
            Image icon = AddImage(iconGo, Color.white, null);
            icon.preserveAspect = true;

            GameObject texts = NewUI("Texts", root.transform);
            AddVertical(texts, 2f).childAlignment = TextAnchor.MiddleLeft;
            GetLayout(texts).minWidth = 340f;
            TMP_Text name = AddText(NewUI("Name", texts.transform), "Bâtiment", 30f, TextMain, FontStyles.Bold);
            TMP_Text level = AddText(NewUI("Level", texts.transform), "Niveau 1 / 3", 22f, TextMuted);

            GameObject prodGo = NewUI("Production", root.transform);
            Flexible(prodGo);
            TMP_Text production = AddText(prodGo, "Aucune production", 26f, Green, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            BuildingSummaryRowUI row = root.AddComponent<BuildingSummaryRowUI>();
            var so = new SerializedObject(row);
            Set(so, "icon", icon);
            Set(so, "nameLabel", name);
            Set(so, "levelLabel", level);
            Set(so, "productionLabel", production);
            so.ApplyModifiedProperties();

            return SavePrefab(root, RowPath).GetComponent<BuildingSummaryRowUI>();
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        #endregion

        #region Onglets

        private static void FixTabButton(Button button, Vector2 position, string label)
        {
            if (button == null) return;

            var rt = (RectTransform)button.transform;
            Undo.RecordObject(rt, "Onglet");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(200f, 56f);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = position;

            if (button.TryGetComponent(out Image image))
            {
                Undo.RecordObject(image, "Onglet");
                image.sprite = _rounded;
                image.type = Image.Type.Sliced;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null) return;
            var textRt = (RectTransform)text.transform;
            Undo.RecordObject(textRt, "Onglet");
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            Undo.RecordObject(text, "Onglet");
            text.text = label;
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        #endregion

        #region Helpers uGUI

        private static void AddTitle(Transform parent, string name, string text)
        {
            GameObject go = NewUI(name, parent);
            TMP_Text label = AddText(go, text, 20f, TextMuted, FontStyles.Bold);
            label.characterSpacing = 4f;
            Height(go, 30f);
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(GameObject go, Color color, Sprite sprite)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
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

        private static VerticalLayoutGroup AddVertical(GameObject go, float spacing)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private static HorizontalLayoutGroup AddHorizontal(GameObject go, float spacing)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layout;
        }

        private static void Size(GameObject go, float width, float height)
        {
            LayoutElement layout = GetLayout(go);
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = height;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
        }

        private static void Height(GameObject go, float height)
        {
            LayoutElement layout = GetLayout(go);
            layout.minHeight = layout.preferredHeight = height;
        }

        private static void Flexible(GameObject go) => GetLayout(go).flexibleWidth = 1f;

        private static LayoutElement GetLayout(GameObject go)
            => go.TryGetComponent(out LayoutElement layout) ? layout : go.AddComponent<LayoutElement>();

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
            if (prop == null) Debug.LogWarning($"[VillageOverviewBuilder] Champ '{property}' introuvable sur {so.targetObject.GetType().Name}.");
            else prop.objectReferenceValue = value;
        }

        #endregion
    }
}
