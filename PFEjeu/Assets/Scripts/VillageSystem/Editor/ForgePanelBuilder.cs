using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Core.EquipmentSystem;
using Core.InventorySystem;
using Core.StatsSystem;
using Core.Village.UI;

namespace Core.Village.EditorTools
{
    using StatType = EnumStats.StatTypes;

    /// <summary>
    /// Met en place la Forge dans la scène ouverte : asset ForgeDefinition (valeurs par défaut),
    /// objets d'exemple, ForgeService, EquipmentUpgradeApplier sur le joueur, et panneau uGUI
    /// câblé. Relançable ; le panneau précédent est remplacé.
    /// Menu : Tools > Village > Construire la Forge
    /// </summary>
    public static class ForgePanelBuilder
    {
        private const string PanelName = "ForgePanel";
        private const string DefinitionPath = "Assets/ScriptableObjects/Village/ForgeDefinition.asset";
        private const string ItemsFolder = "Assets/ScriptableObjects/Inventory";
        private const string CostRowPath = "Assets/Prefabs/ForgeCostRow.prefab";
        private const string ItemRowPath = "Assets/Prefabs/ForgeItemRow.prefab";
        private const string IconsFolder = "Assets/Arts/2D/ArmorSlots";
        private const int UILayer = 5;

        private static readonly Color PanelBg = new(0.086f, 0.102f, 0.129f, 0.62f);
        private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
        private static readonly Color Divider = new(1f, 1f, 1f, 0.1f);
        private static readonly Color TextMain = new(0.95f, 0.95f, 0.96f);
        private static readonly Color TextMuted = new(0.62f, 0.66f, 0.72f);
        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color Green = new(0.45f, 0.85f, 0.45f);
        private static readonly Color ButtonGreen = new(0.27f, 0.62f, 0.32f);
        private static readonly Color ButtonRed = new(0.62f, 0.27f, 0.27f);

        private static Sprite _rounded;
        private static Sprite _circle;

        [MenuItem("Tools/Village/Construire la Forge")]
        public static void Build()
        {
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            _circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var view = Object.FindFirstObjectByType<VillageViewController>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<VillageManager>(FindObjectsInactive.Include);
            var buildingPanel = Object.FindFirstObjectByType<BuildingPanelUI>(FindObjectsInactive.Include);
            Building forgeBuilding = Object.FindObjectsByType<Building>(FindObjectsInactive.Include)
                .FirstOrDefault(b => b.Definition != null && b.Definition.name.Contains("Forge"));
            EntityStats playerStats = Object.FindObjectsByType<EntityStats>(FindObjectsInactive.Include)
                .FirstOrDefault(s => s.GetComponentInParent<InventoryHolder>() != null);
            InventoryHolder playerInv = playerStats != null ? playerStats.GetComponentInParent<InventoryHolder>() : null;

            if (view == null || manager == null || buildingPanel == null || forgeBuilding == null || playerInv == null)
            {
                EditorUtility.DisplayDialog("Forge",
                    "Introuvable dans la scène ouverte :\n" +
                    $"- VillageViewController : {(view != null ? "ok" : "MANQUANT")}\n" +
                    $"- VillageManager : {(manager != null ? "ok" : "MANQUANT")}\n" +
                    $"- BuildingPanelUI : {(buildingPanel != null ? "ok" : "MANQUANT")}\n" +
                    $"- Bâtiment Forge : {(forgeBuilding != null ? "ok" : "MANQUANT")}\n" +
                    $"- Joueur (EntityStats + InventoryHolder) : {(playerInv != null ? "ok" : "MANQUANT")}", "OK");
                return;
            }

            Canvas canvas = new SerializedObject(buildingPanel).FindProperty("panel").objectReferenceValue is GameObject bp
                ? bp.GetComponentInParent<Canvas>(true)?.rootCanvas
                : buildingPanel.GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Forge", "Canvas du village introuvable.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Construire la Forge");
            int undoGroup = Undo.GetCurrentGroup();

            CreateSampleItems();
            ForgeDefinition definition = EnsureDefinition();
            EnsureShop(definition);
            SetupArmorSlots(playerInv);

            // --- Service ---
            var service = Object.FindFirstObjectByType<ForgeService>(FindObjectsInactive.Include);
            if (service == null) service = Undo.AddComponent<ForgeService>(forgeBuilding.gameObject);
            var serviceSo = new SerializedObject(service);
            Set(serviceSo, "forgeBuilding", forgeBuilding);
            Set(serviceSo, "villageManager", manager);
            Set(serviceSo, "definition", definition);
            Set(serviceSo, "playerInventory", playerInv);
            serviceSo.ApplyModifiedProperties();

            // --- Bonus appliqués au joueur ---
            var applier = playerStats.GetComponent<EquipmentUpgradeApplier>();
            if (applier == null) applier = Undo.AddComponent<EquipmentUpgradeApplier>(playerStats.gameObject);
            var applierSo = new SerializedObject(applier);
            Set(applierSo, "playerInventory", playerInv);
            Set(applierSo, "stats", playerStats);
            Set(applierSo, "forge", definition);
            applierSo.ApplyModifiedProperties();

            // --- Panneau ---
            Transform previous = canvas.transform.Find(PanelName);
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);

            CostRowUI costRowPrefab = BuildCostRowPrefab();
            ForgeItemRowUI itemRowPrefab = BuildItemRowPrefab();

            var ui = Object.FindFirstObjectByType<ForgePanelUI>(FindObjectsInactive.Include);
            GameObject panel = BuildPanel(canvas.transform, out var r);
            // Le contrôleur reste sur un objet toujours actif : le panneau, lui, est désactivé au départ.
            if (ui == null) ui = Undo.AddComponent<ForgePanelUI>(service.gameObject);
            else Undo.RecordObject(ui, "Câbler la Forge");

            var so = new SerializedObject(ui);
            Set(so, "villageView", view);
            Set(so, "forge", service);
            Set(so, "panel", panel);
            Set(so, "titleLabel", r.Title);
            Set(so, "forgeLevelLabel", r.ForgeLevel);
            Set(so, "closeButton", r.Close);
            Set(so, "upgradeTabButton", r.UpgradeTab);
            Set(so, "shopTabButton", r.ShopTab);
            Set(so, "itemRowsParent", r.ListContent);
            Set(so, "itemRowPrefab", itemRowPrefab);
            Set(so, "emptyListLabel", r.EmptyList);
            Set(so, "detailSection", r.Detail);
            Set(so, "detailIcon", r.DetailIcon);
            Set(so, "detailName", r.DetailName);
            Set(so, "detailLevel", r.DetailLevel);
            Set(so, "statsPreview", r.Preview);
            Set(so, "costRowsParent", r.CostRows);
            Set(so, "costRowPrefab", costRowPrefab);
            Set(so, "maxLevelBanner", r.MaxBanner);
            Set(so, "upgradeButton", r.Upgrade);
            Set(so, "upgradeButtonImage", r.UpgradeImage);
            Set(so, "upgradeButtonLabel", r.UpgradeLabel);
            Set(so, "dismantleButton", r.Dismantle);
            Set(so, "dismantleLabel", r.DismantleLabel);
            Set(so, "feedbackLabel", r.Feedback);
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            Selection.activeGameObject = panel;
            Debug.Log("[ForgePanelBuilder] Forge construite et câblée. Pense à sauvegarder la scène.");
        }

        #region Données (assets)

        /// <summary>Crée le ForgeDefinition avec des valeurs d'équilibrage de départ s'il n'existe pas.</summary>
        private static ForgeDefinition EnsureDefinition()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ForgeDefinition>(DefinitionPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<ForgeDefinition>();
            AssetDatabase.CreateAsset(def, DefinitionPath);

            ItemDefinition wood = LoadItem("Bois"), stone = LoadItem("Pierre"), iron = LoadItem("Fer");
            var so = new SerializedObject(def);

            SerializedProperty tiers = so.FindProperty("tiers");
            tiers.arraySize = 9;
            for (int i = 0; i < 9; i++)
            {
                SerializedProperty cost = tiers.GetArrayElementAtIndex(i).FindPropertyRelative("cost");
                cost.arraySize = 3;
                SetCost(cost.GetArrayElementAtIndex(0), wood, 5 + i * 3);
                SetCost(cost.GetArrayElementAtIndex(1), stone, 4 + i * 3);
                SetCost(cost.GetArrayElementAtIndex(2), iron, 2 + i * 2);
            }

            SerializedProperty cats = so.FindProperty("categories");
            cats.arraySize = 3;
            SetCategory(cats.GetArrayElementAtIndex(0), ItemCategory.Weapon, true, wood, StatType.PhysicDamage, 2f, StatType.MagicDamage, 2f);
            SetCategory(cats.GetArrayElementAtIndex(1), ItemCategory.Armor, true, iron, StatType.DefensePhysic, 2f, StatType.DefenseMagic, 1f);
            SetCategory(cats.GetArrayElementAtIndex(2), ItemCategory.Trap, false, stone, StatType.PhysicDamage, 3f, StatType.AttackSpeed, 0.05f);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }

        /// <summary>Remplit la boutique de la Forge avec les objets d'exemple si elle est vide.</summary>
        private static void EnsureShop(ForgeDefinition def)
        {
            var so = new SerializedObject(def);
            SerializedProperty shop = so.FindProperty("shop");
            if (shop.arraySize > 0) return;

            ItemDefinition wood = LoadItem("Bois"), stone = LoadItem("Pierre"), iron = LoadItem("Fer");
            (string file, ItemDefinition a, int na, ItemDefinition b, int nb)[] entries =
            {
                ("Casque", iron, 8, wood, 4),
                ("Plastron", iron, 16, wood, 6),
                ("Jambieres", iron, 12, wood, 4),
                ("Bottes", iron, 8, wood, 4),
                ("Piege", stone, 6, iron, 3),
            };

            shop.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty e = shop.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("item").objectReferenceValue = LoadItem(entries[i].file);
                SerializedProperty cost = e.FindPropertyRelative("cost");
                cost.arraySize = 2;
                SetCost(cost.GetArrayElementAtIndex(0), entries[i].a, entries[i].na);
                SetCost(cost.GetArrayElementAtIndex(1), entries[i].b, entries[i].nb);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Met les 4 pièces d'armure dans l'inventaire de départ du joueur et crée une case
        /// d'armure par emplacement (tête, torse, jambes, pieds) dans le panneau de personnage.
        /// </summary>
        private static void SetupArmorSlots(InventoryHolder playerInv)
        {
            // Inventaire de départ
            var holderSo = new SerializedObject(playerInv);
            SerializedProperty starting = holderSo.FindProperty("startingItems");
            foreach (string file in new[] { "Casque", "Plastron", "Jambieres", "Bottes", "Epee", "Arc", "BatonMagique" })
            {
                ItemDefinition item = LoadItem(file);
                if (item == null) continue;

                bool present = false;
                for (int i = 0; i < starting.arraySize; i++)
                    if (starting.GetArrayElementAtIndex(i).FindPropertyRelative("definition").objectReferenceValue == item) present = true;
                if (present) continue;

                starting.arraySize++;
                SerializedProperty entry = starting.GetArrayElementAtIndex(starting.arraySize - 1);
                entry.FindPropertyRelative("definition").objectReferenceValue = item;
                entry.FindPropertyRelative("amount").intValue = 1;
            }
            holderSo.ApplyModifiedProperties();

            // Cases d'armure
            var slots = Object.FindObjectsByType<Core.InventorySystem.UI.ArmorSlotUI>(FindObjectsInactive.Include);
            if (slots.Length == 0) return;

            Core.InventorySystem.UI.ArmorSlotUI template = slots[0];
            Transform section = template.transform.parent;
            Transform container = section.Find("ArmorSlots");
            if (container == null)
            {
                var containerGo = NewUI("ArmorSlots", section);
                var layout = containerGo.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 6f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                ((RectTransform)containerGo.transform).sizeDelta = new Vector2(306f, 72f);
                GetLayout(containerGo).preferredWidth = 306f;
                GetLayout(containerGo).preferredHeight = 72f;
                container = containerGo.transform;
                Undo.RegisterCreatedObjectUndo(containerGo, "Cases d'armure");
            }

            var types = new[] { ArmorSlotType.Head, ArmorSlotType.Chest, ArmorSlotType.Legs, ArmorSlotType.Feet };
            var names = new[] { "Slot_Tete", "Slot_Torse", "Slot_Jambes", "Slot_Pieds" };

            for (int i = 0; i < types.Length; i++)
            {
                Core.InventorySystem.UI.ArmorSlotUI slot = container.Find(names[i])?.GetComponent<Core.InventorySystem.UI.ArmorSlotUI>();
                if (slot == null)
                {
                    GameObject go = i == 0 ? template.gameObject : Object.Instantiate(template.gameObject, container);
                    go.name = names[i];
                    go.transform.SetParent(container, false);
                    slot = go.GetComponent<Core.InventorySystem.UI.ArmorSlotUI>();
                    var rt = (RectTransform)go.transform;
                    rt.sizeDelta = new Vector2(72f, 72f);
                    var le = GetLayout(go);
                    le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = 72f;
                }
                var so = new SerializedObject(slot);
                so.FindProperty("slotType").enumValueIndex = (int)types[i];
                so.ApplyModifiedProperties();
            }

            // Largeur de la section d'armure : 4 cases au lieu d'une.
            var sectionLayout = section.GetComponent<LayoutElement>();
            if (sectionLayout != null)
            {
                sectionLayout.minWidth = sectionLayout.preferredWidth = 306f;
                ((RectTransform)section).sizeDelta = new Vector2(306f, ((RectTransform)section).sizeDelta.y);
            }
            Transform title = section.Find("Title");
            if (title != null) ((RectTransform)title).sizeDelta = new Vector2(306f, ((RectTransform)title).sizeDelta.y);
            var rowLayout = section.parent.GetComponent<HorizontalLayoutGroup>();
            if (rowLayout != null) rowLayout.spacing = 40f;
            EditorSceneManager.MarkSceneDirty(section.gameObject.scene);
        }

        private static void SetCost(SerializedProperty p, ItemDefinition item, int amount)
        {
            p.FindPropertyRelative("item").objectReferenceValue = item;
            p.FindPropertyRelative("amount").intValue = Mathf.Max(1, amount);
        }

        private static void SetCategory(SerializedProperty p, ItemCategory category, bool appliesToPlayer,
            ItemDefinition refund, StatType statA, float valueA, StatType statB, float valueB)
        {
            p.FindPropertyRelative("category").enumValueIndex = System.Array.IndexOf(System.Enum.GetValues(typeof(ItemCategory)), category);
            p.FindPropertyRelative("appliesToPlayerStats").boolValue = appliesToPlayer;

            SerializedProperty bonuses = p.FindPropertyRelative("bonuses");
            bonuses.arraySize = 2;
            SetBonus(bonuses.GetArrayElementAtIndex(0), statA, valueA);
            SetBonus(bonuses.GetArrayElementAtIndex(1), statB, valueB);

            SerializedProperty baseRefund = p.FindPropertyRelative("baseRefund");
            baseRefund.arraySize = 1;
            SetCost(baseRefund.GetArrayElementAtIndex(0), refund, 3);
        }

        private static void SetBonus(SerializedProperty p, StatType stat, float value)
        {
            p.FindPropertyRelative("stat").enumValueIndex = System.Array.IndexOf(System.Enum.GetValues(typeof(StatType)), stat);
            p.FindPropertyRelative("valuePerLevel").floatValue = value;
            p.FindPropertyRelative("modifierType").enumValueIndex = 0; // Flat
        }

        private static ItemDefinition LoadItem(string name)
            => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{name}.asset");

        /// <summary>Crée les objets améliorables d'exemple (armures avec leurs sprites, armes, piège).</summary>
        [MenuItem("Tools/Village/Créer les objets d'exemple de la Forge")]
        public static void CreateSampleItems()
        {
            CreateItem("Casque", "casque", "Casque", ItemCategory.Armor, 1, $"{IconsFolder}/casque.png", ArmorSlotType.Head);
            CreateItem("Plastron", "plastron", "Plastron", ItemCategory.Armor, 1, $"{IconsFolder}/armor.png", ArmorSlotType.Chest);
            CreateItem("Jambieres", "jambieres", "Jambières", ItemCategory.Armor, 1, $"{IconsFolder}/jambières.png", ArmorSlotType.Legs);
            CreateItem("Bottes", "bottes", "Bottes", ItemCategory.Armor, 1, $"{IconsFolder}/botte.png", ArmorSlotType.Feet);
            CreateItem("Epee", "epee", "Épée", ItemCategory.Weapon, 1, null, ArmorSlotType.None);
            CreateItem("Arc", "arc", "Arc", ItemCategory.Weapon, 1, null, ArmorSlotType.None);
            CreateItem("BatonMagique", "baton_magique", "Bâton de magie", ItemCategory.Weapon, 1, null, ArmorSlotType.None);
            CreateItem("Piege", "piege", "Piège", ItemCategory.Trap, 20, null, ArmorSlotType.None);
            AssetDatabase.SaveAssets();
            Debug.Log("[ForgePanelBuilder] Objets d'exemple créés dans " + ItemsFolder + " (ajoute-les à l'ItemDatabase).");
        }

        /// <summary>Certaines images sont découpées (Multiple) : le sprite est alors un sous-asset.</summary>
        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            return AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        private static void CreateItem(string file, string id, string displayName, ItemCategory category, int stack, string iconPath, ArmorSlotType armorSlot)
        {
            string path = $"{ItemsFolder}/{file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null)
            {
                // Objet déjà créé : on s'assure seulement de son emplacement d'armure.
                var existing = new SerializedObject(item);
                existing.FindProperty("armorSlot").enumValueIndex = (int)armorSlot;
                if (iconPath != null) existing.FindProperty("<Icon>k__BackingField").objectReferenceValue = LoadSprite(iconPath);
                existing.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                return;
            }

            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
            var so = new SerializedObject(item);
            so.FindProperty("armorSlot").enumValueIndex = (int)armorSlot;
            so.FindProperty("<Id>k__BackingField").stringValue = id;
            so.FindProperty("<DisplayName>k__BackingField").stringValue = displayName;
            so.FindProperty("<MaxStackSize>k__BackingField").intValue = stack;
            so.FindProperty("category").enumValueIndex = System.Array.IndexOf(System.Enum.GetValues(typeof(ItemCategory)), category);
            if (iconPath != null)
                so.FindProperty("<Icon>k__BackingField").objectReferenceValue = LoadSprite(iconPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }

        #endregion

        #region Panneau

        private struct Refs
        {
            public TMP_Text Title, ForgeLevel, DetailName, DetailLevel, Preview, UpgradeLabel, DismantleLabel, Feedback;
            public Button Close, Upgrade, Dismantle, UpgradeTab, ShopTab;
            public Image DetailIcon, UpgradeImage;
            public Transform ListContent, CostRows;
            public GameObject EmptyList, Detail, MaxBanner;
        }

        private static GameObject BuildPanel(Transform canvas, out Refs r)
        {
            r = new Refs();
            GameObject panel = NewUI(PanelName, canvas);
            Undo.RegisterCreatedObjectUndo(panel, "Créer le panneau Forge");

            // Les 3/4 de l'écran, centré, fond translucide pour voir le jeu derrière.
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = new Vector2(0.125f, 0.125f);
            rt.anchorMax = new Vector2(0.875f, 0.875f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var bg = AddImage(panel, PanelBg, _rounded);
            bg.raycastTarget = true; // bloque les clics vers le monde derrière
            var vlg = AddVertical(panel, 12f);
            vlg.padding = new RectOffset(28, 28, 22, 22);
            vlg.childForceExpandHeight = false;

            // --- En-tête ---
            GameObject header = NewUI("Header", panel.transform);
            var hlg = AddHorizontal(header, 12f);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            r.Title = AddText(NewUI("Title", header.transform), "Forge", 34f, TextMain, FontStyles.Bold);
            r.ForgeLevel = AddText(NewUI("Level", header.transform), "Niveau 1", 18f, Gold, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            Flexible(r.ForgeLevel.gameObject);
            GameObject close = NewUI("CloseButton", header.transform);
            Size(close, 44f, 44f);
            var closeImg = AddImage(close, CardBg, _rounded);
            closeImg.raycastTarget = true;
            r.Close = AddButton(close, closeImg);
            AddText(Stretch(NewUI("Label", close.transform), 0f), "X", 22f, TextMuted, FontStyles.Bold, TextAlignmentOptions.Center);

            // --- Onglets ---
            GameObject tabs = NewUI("Tabs", panel.transform);
            var tlg = AddHorizontal(tabs, 10f);
            tlg.childAlignment = TextAnchor.MiddleLeft;
            r.UpgradeTab = AddTab(tabs.transform, "UpgradeTab", "AMÉLIORER");
            r.ShopTab = AddTab(tabs.transform, "ShopTab", "BOUTIQUE");

            AddDivider(panel.transform);

            // --- Corps : liste à gauche, détail à droite ---
            GameObject body = NewUI("Body", panel.transform);
            var blg = AddHorizontal(body, 24f);
            blg.childForceExpandHeight = true;
            blg.childAlignment = TextAnchor.UpperLeft;
            var bodyLayout = GetLayout(body);
            bodyLayout.flexibleHeight = 1f;
            bodyLayout.minHeight = 200f;

            // Liste défilante
            GameObject listCol = NewUI("ListColumn", body.transform);
            AddVertical(listCol, 8f);
            var listColLayout = GetLayout(listCol);
            listColLayout.preferredWidth = 420f;
            listColLayout.flexibleWidth = 0f;
            AddLabel(listCol.transform, "OBJETS");

            GameObject scroll = NewUI("ItemList", listCol.transform);
            GetLayout(scroll).flexibleHeight = 1f;
            AddImage(scroll, CardBg, _rounded);
            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            GameObject viewport = NewUI("Viewport", scroll.transform);
            Stretch(viewport, 6f);
            viewport.AddComponent<RectMask2D>();
            GameObject content = NewUI("Content", viewport.transform);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = Vector2.zero;
            AddVertical(content, 6f);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = (RectTransform)viewport.transform;
            scrollRect.content = crt;
            r.ListContent = content.transform;

            GameObject empty = NewUI("EmptyLabel", scroll.transform);
            Stretch(empty, 12f);
            AddText(empty, "Rien à afficher", 18f, TextMuted, FontStyles.Italic, TextAlignmentOptions.Center);
            r.EmptyList = empty;

            // Détail
            GameObject detail = NewUI("Detail", body.transform);
            AddVertical(detail, 12f);
            GetLayout(detail).flexibleWidth = 1f;
            r.Detail = detail;

            GameObject top = NewUI("Top", detail.transform);
            var dlg = AddHorizontal(top, 16f);
            dlg.childAlignment = TextAnchor.MiddleLeft;
            GameObject frame = NewUI("IconFrame", top.transform);
            AddImage(frame, CardBg, _rounded);
            Size(frame, 96f, 96f);
            r.DetailIcon = AddImage(Stretch(NewUI("Icon", frame.transform), 8f), Color.white, null);
            r.DetailIcon.preserveAspect = true;
            GameObject dtitles = NewUI("Titles", top.transform);
            AddVertical(dtitles, 4f);
            Flexible(dtitles);
            r.DetailName = AddText(NewUI("Name", dtitles.transform), "Objet", 30f, TextMain, FontStyles.Bold);
            r.DetailLevel = AddText(NewUI("Level", dtitles.transform), "+0 → +1", 22f, Gold, FontStyles.Bold);

            AddLabel(detail.transform, "BONUS");
            r.Preview = AddText(NewUI("Preview", detail.transform), "", 18f, Green);
            r.Preview.textWrappingMode = TextWrappingModes.Normal;

            AddLabel(detail.transform, "COÛT (STOCK DE L'HÔTEL DE VILLE)");
            GameObject costRows = NewUI("CostRows", detail.transform);
            AddVertical(costRows, 6f);
            r.CostRows = costRows.transform;

            GameObject max = NewUI("MaxLevelBanner", detail.transform);
            AddImage(max, new Color(Gold.r, Gold.g, Gold.b, 0.15f), _rounded);
            Height(max, 44f);
            AddText(Stretch(NewUI("Label", max.transform), 0f), "Niveau d'amélioration maximum", 18f, Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            max.SetActive(false);
            r.MaxBanner = max;

            GameObject spacer = NewUI("Spacer", detail.transform);
            GetLayout(spacer).flexibleHeight = 1f;

            GameObject upgrade = NewUI("UpgradeButton", detail.transform);
            Height(upgrade, 58f);
            r.UpgradeImage = AddImage(upgrade, ButtonGreen, _rounded);
            r.UpgradeImage.raycastTarget = true;
            r.Upgrade = AddButton(upgrade, r.UpgradeImage);
            r.UpgradeLabel = AddText(Stretch(NewUI("Label", upgrade.transform), 0f), "Améliorer → +1", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            GameObject dismantle = NewUI("DismantleButton", detail.transform);
            Height(dismantle, 42f);
            var dImg = AddImage(dismantle, ButtonRed, _rounded);
            dImg.raycastTarget = true;
            r.Dismantle = AddButton(dismantle, dImg);
            r.DismantleLabel = AddText(Stretch(NewUI("Label", dismantle.transform), 0f), "Démonter", 18f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            // --- Message d'état ---
            GameObject feedback = NewUI("StatusLabel", panel.transform);
            r.Feedback = AddText(feedback, "", 18f, Green, FontStyles.Normal, TextAlignmentOptions.Center);
            r.Feedback.textWrappingMode = TextWrappingModes.Normal;
            GetLayout(feedback).minHeight = 26f;

            return panel;
        }

        private static Button AddTab(Transform parent, string name, string label)
        {
            GameObject tab = NewUI(name, parent);
            Size(tab, 180f, 40f);
            var img = AddImage(tab, new Color(1f, 1f, 1f, 0.1f), _rounded);
            img.raycastTarget = true;
            Button button = AddButton(tab, img);
            AddText(Stretch(NewUI("Label", tab.transform), 0f), label, 16f, TextMain, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        private static void AddLabel(Transform parent, string text)
        {
            TMP_Text label = AddText(NewUI("SectionTitle", parent), text, 14f, TextMuted, FontStyles.Bold);
            label.characterSpacing = 4f;
        }

        private static void AddDivider(Transform parent)
        {
            GameObject d = NewUI("Divider", parent);
            AddImage(d, Divider, null);
            Height(d, 2f);
        }

        #endregion

        #region Prefabs de lignes

        private static CostRowUI BuildCostRowPrefab()
        {
            GameObject row = NewRow("ForgeCostRow", out Image icon);
            TMP_Text name = AddText(NewUI("Name", row.transform), "Ressource", 18f, TextMain);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(name.gameObject);
            TMP_Text amount = AddText(NewUI("Amount", row.transform), "0 / 20", 18f, Green, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            GameObject dot = NewUI("StatusDot", row.transform);
            Size(dot, 12f, 12f);
            Image dotImage = AddImage(dot, Green, _circle);

            var ui = row.AddComponent<CostRowUI>();
            var so = new SerializedObject(ui);
            Set(so, "icon", icon);
            Set(so, "label", name);
            Set(so, "amountLabel", amount);
            Set(so, "statusDot", dotImage);
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(row, CostRowPath).GetComponent<CostRowUI>();
        }

        private static ForgeItemRowUI BuildItemRowPrefab()
        {
            GameObject row = NewRow("ForgeItemRow", out Image icon);
            Image bg = row.GetComponent<Image>();
            bg.raycastTarget = true;
            Button button = AddButton(row, bg);
            TMP_Text name = AddText(NewUI("Name", row.transform), "Objet", 18f, TextMain);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(name.gameObject);
            TMP_Text level = AddText(NewUI("Level", row.transform), "+3", 20f, Gold, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            var ui = row.AddComponent<ForgeItemRowUI>();
            var so = new SerializedObject(ui);
            Set(so, "button", button);
            Set(so, "icon", icon);
            Set(so, "nameLabel", name);
            Set(so, "levelLabel", level);
            Set(so, "background", bg);
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(row, ItemRowPath).GetComponent<ForgeItemRowUI>();
        }

        private static GameObject NewRow(string name, out Image icon)
        {
            GameObject row = NewUI(name, null);
            ((RectTransform)row.transform).sizeDelta = new Vector2(416f, 44f);
            AddImage(row, CardBg, _rounded);
            var hlg = AddHorizontal(row, 10f);
            hlg.padding = new RectOffset(10, 12, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var layout = row.AddComponent<LayoutElement>();
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

        private static Button AddButton(GameObject go, Image target)
        {
            var button = go.AddComponent<Button>();
            button.targetGraphic = target;
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
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

        private static void Flexible(GameObject go) => GetLayout(go).flexibleWidth = 1f;

        private static LayoutElement GetLayout(GameObject go)
            => go.TryGetComponent(out LayoutElement layout) ? layout : go.AddComponent<LayoutElement>();

        private static GameObject Stretch(GameObject go, float inset)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return go;
        }

        private static void Set(SerializedObject so, string property, Object value)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop == null) Debug.LogWarning($"[ForgePanelBuilder] Champ '{property}' introuvable sur {so.targetObject.GetType().Name}.");
            else prop.objectReferenceValue = value;
        }

        // Surcharge pour passer un TMP_Text / Image / Button / Component directement.
        private static void Set(SerializedObject so, string property, Component value) => Set(so, property, (Object)value);

        #endregion
    }
}
