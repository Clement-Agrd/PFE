using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Core.InventorySystem.UI;

namespace Core.InventorySystem.EditorTools
{
    /// <summary>
    /// Construit (ou reconstruit) la case d'armure et la barre de nourriture
    /// dans le panneau de personnage ouvert (/Canvas/InventoryUI/InventoryPanel
    /// habituellement) : agrandit le panneau pour leur faire de la place,
    /// crée leur hiérarchie uGUI et câble ArmorSlotUI / FoodBarUI sur
    /// l'InventoryHolder et les stats du joueur. Relançable ; tout passe par
    /// l'Undo. Même approche que BuildingPanelBuilder / TavernPanelBuilder.
    /// Menu : Tools > Inventaire > Construire armure et nourriture
    /// </summary>
    public static class EquipmentRowBuilder
    {
        private const string RowName = "EquipmentRow";
        private const string FoodSlotPrefabPath = "Assets/Prefabs/FoodSlot.prefab";
        private const int UILayer = 5;

        // La hauteur du panneau AVANT toute exécution de ce builder (constatée dans la scène).
        // Le builder recalcule toujours la hauteur cible à partir de cette base, ce qui le
        // rend relançable sans agrandir le panneau à chaque fois.
        private const float BasePanelHeight = 495.04f;
        private const float ExtraHeight = 170f;

        private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
        private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
        private static readonly Color TextMuted = new(0.62f, 0.66f, 0.72f);

        private static Sprite _rounded;

        [MenuItem("Tools/Inventaire/Construire armure et nourriture")]
        public static void Build()
        {
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            InventoryUI invUI = Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include).FirstOrDefault();
            if (invUI == null)
            {
                EditorUtility.DisplayDialog("Armure & Nourriture", "Aucun InventoryUI trouvé dans la scène ouverte.", "OK");
                return;
            }

            var invUISO = new SerializedObject(invUI);
            var panelGO = invUISO.FindProperty("panel").objectReferenceValue as GameObject;
            var inventoryHolder = invUISO.FindProperty("inventoryHolder").objectReferenceValue as Core.InventorySystem.InventoryHolder;
            if (panelGO == null || inventoryHolder == null)
            {
                EditorUtility.DisplayDialog("Armure & Nourriture", "InventoryUI n'a pas de panel / inventoryHolder assigné.", "OK");
                return;
            }

            GameObject playerGO = inventoryHolder.gameObject;
            var playerStats = playerGO.GetComponent<Core.StatsSystem.EntityStats>();
            var playerHealth = playerGO.GetComponent<Core.HealthSystem.Health>();

            var panelRect = (RectTransform)panelGO.transform;

            Undo.SetCurrentGroupName("Construire armure et nourriture");
            int undoGroup = Undo.GetCurrentGroup();

            Transform previous = panelRect.Find(RowName);
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);

            Undo.RecordObject(panelRect, "Agrandir le panneau de personnage");
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, BasePanelHeight + ExtraHeight);

            FoodSlotUI foodSlotPrefab = BuildFoodSlotPrefab();
            var refs = BuildRow(panelRect);

            var armorSO = new SerializedObject(refs.ArmorSlotUI);
            Set(armorSO, "inventoryHolder", inventoryHolder);
            Set(armorSO, "icon", refs.ArmorIcon);
            Set(armorSO, "amountText", refs.ArmorAmount);
            armorSO.ApplyModifiedProperties();

            var foodSO = new SerializedObject(refs.FoodBarUI);
            Set(foodSO, "inventoryHolder", inventoryHolder);
            Set(foodSO, "playerStats", playerStats);
            Set(foodSO, "playerHealth", playerHealth);
            Set(foodSO, "slotsParent", refs.FoodRows);
            Set(foodSO, "slotPrefab", foodSlotPrefab);
            foodSO.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(panelGO.scene);
            Selection.activeGameObject = refs.Row;
            EditorGUIUtility.PingObject(refs.Row);
            Debug.Log("[EquipmentRowBuilder] Case d'armure et barre de nourriture construites et câblées. Pense à sauvegarder la scène.");
        }

        #region Rangée

        private struct RowRefs
        {
            public GameObject Row;
            public UnityEngine.UI.Image ArmorIcon;
            public TMP_Text ArmorAmount;
            public Transform FoodRows;
            public ArmorSlotUI ArmorSlotUI;
            public FoodBarUI FoodBarUI;
        }

        private static RowRefs BuildRow(Transform panel)
        {
            var r = new RowRefs();

            GameObject row = NewUI(RowName, panel);
            Undo.RegisterCreatedObjectUndo(row, "Créer la rangée équipement");
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(-50f, -276f);
            rowRect.sizeDelta = new Vector2(700f, 106f);
            var rowLayout = AddHorizontal(row, 40f);
            rowLayout.childAlignment = TextAnchor.MiddleCenter;

            // --- Armure : un seul slot générique ---
            GameObject armorSection = NewUI("ArmorSection", row.transform);
            var armorLayout = AddVertical(armorSection, 6f);
            armorLayout.childAlignment = TextAnchor.UpperCenter;
            Size(armorSection, 100f, 106f);

            TMP_Text armorTitle = AddText(NewUI("Title", armorSection.transform), "ARMURE", 14f, TextMuted, FontStyles.Bold, TextAlignmentOptions.Center);
            armorTitle.characterSpacing = 2f;
            armorTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 18f);

            GameObject armorSlot = NewUI("Slot", armorSection.transform);
            Size(armorSlot, 80f, 80f);
            AddImage(armorSlot, CardBg, _rounded);
            GameObject armorIconGo = NewUI("Icon", armorSlot.transform);
            Stretch(armorIconGo, 8f);
            r.ArmorIcon = AddImage(armorIconGo, Color.clear, null);
            r.ArmorIcon.preserveAspect = true;
            r.ArmorIcon.enabled = false;
            GameObject armorAmountGo = NewUI("Amount", armorSlot.transform);
            var amtRect = (RectTransform)armorAmountGo.transform;
            amtRect.anchorMin = amtRect.anchorMax = new Vector2(1f, 0f);
            amtRect.pivot = new Vector2(1f, 0f);
            amtRect.anchoredPosition = new Vector2(-4f, 4f);
            amtRect.sizeDelta = new Vector2(40f, 20f);
            r.ArmorAmount = AddText(armorAmountGo, "", 14f, TextMain, FontStyles.Bold, TextAlignmentOptions.BottomRight);
            r.ArmorAmount.enabled = false;

            r.ArmorSlotUI = armorSlot.AddComponent<ArmorSlotUI>();

            // --- Nourriture : rangée de cases consommables ---
            GameObject foodSection = NewUI("FoodSection", row.transform);
            var foodLayout = AddVertical(foodSection, 6f);
            foodLayout.childAlignment = TextAnchor.UpperCenter;
            Size(foodSection, 452f, 106f);

            TMP_Text foodTitle = AddText(NewUI("Title", foodSection.transform), "NOURRITURE", 14f, TextMuted, FontStyles.Bold, TextAlignmentOptions.Center);
            foodTitle.characterSpacing = 2f;
            foodTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(452f, 18f);

            GameObject foodRows = NewUI("Slots", foodSection.transform);
            var foodRowsLayout = AddHorizontal(foodRows, 8f);
            foodRowsLayout.childAlignment = TextAnchor.MiddleCenter;
            Size(foodRows, 452f, 80f);
            r.FoodRows = foodRows.transform;

            r.FoodBarUI = row.AddComponent<FoodBarUI>();

            r.Row = row;
            return r;
        }

        #endregion

        #region Prefab de case de nourriture

        private static FoodSlotUI BuildFoodSlotPrefab()
        {
            GameObject slot = NewUI("FoodSlot", null);
            ((RectTransform)slot.transform).sizeDelta = new Vector2(80f, 80f);
            var layout = slot.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minWidth = layout.preferredWidth = layout.minHeight = layout.preferredHeight = 80f;
            AddImage(slot, CardBg, _rounded);

            GameObject iconGo = NewUI("Icon", slot.transform);
            Stretch(iconGo, 8f);
            var icon = AddImage(iconGo, Color.clear, null);
            icon.preserveAspect = true;

            GameObject amountGo = NewUI("Amount", slot.transform);
            var amtRect = (RectTransform)amountGo.transform;
            amtRect.anchorMin = amtRect.anchorMax = new Vector2(1f, 0f);
            amtRect.pivot = new Vector2(1f, 0f);
            amtRect.anchoredPosition = new Vector2(-4f, 4f);
            amtRect.sizeDelta = new Vector2(40f, 20f);
            TMP_Text amount = AddText(amountGo, "", 14f, TextMain, FontStyles.Bold, TextAlignmentOptions.BottomRight);

            var button = slot.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = slot.GetComponent<UnityEngine.UI.Image>();

            var ui = slot.AddComponent<FoodSlotUI>();
            var so = new SerializedObject(ui);
            Set(so, "icon", icon);
            Set(so, "amountText", amount);
            Set(so, "consumeButton", button);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(slot, FoodSlotPrefabPath).GetComponent<FoodSlotUI>();
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

        private static UnityEngine.UI.VerticalLayoutGroup AddVertical(GameObject go, float spacing)
        {
            var layout = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layout;
        }

        private static UnityEngine.UI.HorizontalLayoutGroup AddHorizontal(GameObject go, float spacing)
        {
            var layout = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = false;
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
            if (prop == null) Debug.LogWarning($"[EquipmentRowBuilder] Champ '{property}' introuvable sur {so.targetObject.GetType().Name}.");
            else prop.objectReferenceValue = value;
        }

        #endregion
    }
}
