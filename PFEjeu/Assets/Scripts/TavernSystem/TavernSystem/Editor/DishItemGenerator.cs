using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Core.InventorySystem;

namespace Core.TavernSystem.Editor
{
    /// <summary>
    /// Génère les plats de la taverne :
    /// - un ItemDefinition consommable (Inventory/Food) pour chaque plat existant sans objet ;
    /// - un plat + son objet pour chaque image de "Arts/2D/Food or Prepared Dishes" (variantes " 1"/" 2"
    ///   regroupées), ajouté à TavernMenu_Default.
    /// Idempotent : ne recrée jamais un asset existant. Menu : Village > Taverne > Générer les plats.
    /// </summary>
    public static class DishItemGenerator
    {
        private const string ImagesFolder = "Assets/Arts/2D/Food or Prepared Dishes";
        private const string ItemFolder = "Assets/ScriptableObjects/Inventory/Food";
        private const string DishFolder = "Assets/ScriptableObjects/Tavern";
        private const string MenuPath = "Assets/ScriptableObjects/Tavern/TavernMenu_Default.asset";

        // Indices de EnumStats.StatTypes
        private const int Life = 0, Stamina = 1, PhysDmg = 2, MagicDmg = 3, DefPhys = 4, DefMagic = 5, Speed = 6, AtkSpeed = 7;

        private readonly struct Spec
        {
            public readonly string French;
            public readonly int Cost;
            public readonly float Duration;
            public readonly float Regen; // % PV max / s
            public readonly (int stat, float value)[] Effects; // % additifs

            public Spec(string french, int cost, float duration, float regen, params (int, float)[] effects)
            {
                French = french; Cost = cost; Duration = duration; Regen = regen; Effects = effects;
            }
        }

        // Clé = nom du fichier sans suffixe " 1"/" 2".
        private static readonly Dictionary<string, Spec> Specs = new()
        {
            ["Baked salmon with potatoes"] = new("Saumon rôti aux pommes de terre", 14, 60, 0, (AtkSpeed, .08f), (Life, .08f)),
            ["Beef and ale pie with mashed potatoes"] = new("Tourte bœuf-bière et purée", 22, 60, 0, (Life, .15f), (PhysDmg, .10f)),
            ["Buttered lentils with onions"] = new("Lentilles beurrées aux oignons", 6, 45, 0, (DefPhys, .08f), (DefMagic, .08f)),
            ["Buttered turnips with bacon"] = new("Navets beurrés au lard", 7, 45, 0, (Life, .08f), (DefPhys, .05f)),
            ["Chicken and mushroom pie"] = new("Tourte poulet-champignons", 16, 60, 0, (Life, .12f), (MagicDmg, .08f)),
            ["Chicken soup"] = new("Soupe de poulet", 8, 30, 4f),
            ["Clam and bacon chowder"] = new("Chaudrée de palourdes au lard", 13, 45, 3f, (DefPhys, .05f)),
            ["Creamy fish pie"] = new("Tourte crémeuse au poisson", 15, 60, 0, (Life, .10f), (Speed, .05f)),
            ["Creamy mushroom risotto"] = new("Risotto crémeux aux champignons", 12, 45, 0, (Stamina, .20f), (MagicDmg, .05f)),
            ["Garlic roasted chicken"] = new("Poulet rôti à l'ail", 14, 60, 0, (PhysDmg, .10f), (Life, .05f)),
            ["Garlic roasted lamb with buttered carrots"] = new("Agneau rôti à l'ail et carottes", 20, 60, 0, (PhysDmg, .12f), (Life, .08f)),
            ["Garlic roasted lamb with carrots and mashed potatoes"] = new("Agneau rôti, carottes et purée", 24, 60, 0, (PhysDmg, .14f), (Life, .12f)),
            ["Grilled fish"] = new("Poisson grillé", 9, 45, 0, (AtkSpeed, .06f)),
            ["Grilled shrimp skewers"] = new("Brochettes de crevettes grillées", 11, 45, 0, (Speed, .08f), (AtkSpeed, .04f)),
            ["Honey glazed ham with roasted vegetables"] = new("Jambon au miel et légumes rôtis", 21, 60, 0, (Life, .14f), (DefPhys, .08f)),
            ["Honey glazed pork chops"] = new("Côtes de porc au miel", 15, 60, 0, (PhysDmg, .10f), (Stamina, .10f)),
            ["Oysters roasted with garlic butter"] = new("Huîtres rôties au beurre d'ail", 17, 45, 0, (MagicDmg, .12f), (DefMagic, .06f)),
            ["Pie"] = new("Tourte du boulanger", 6, 45, 0, (Life, .08f)),
            ["Potato and leek soup"] = new("Soupe pommes de terre-poireaux", 7, 30, 3.5f),
            ["Roast pork with cider sauce"] = new("Rôti de porc sauce cidre", 18, 60, 0, (PhysDmg, .12f), (DefMagic, .06f)),
            ["Roasted venison with carrots and mashed potatoes"] = new("Chevreuil rôti, carottes et purée", 26, 60, 0, (PhysDmg, .15f), (Life, .10f), (Speed, .03f)),
            ["Sautéed shrimp with garlic"] = new("Crevettes sautées à l'ail", 10, 45, 0, (Speed, .06f)),
            ["Seafood gumbo with okra"] = new("Gombo aux fruits de mer", 19, 60, 3f, (MagicDmg, .08f)),
            ["Shrimp and rice pilaf with saffron"] = new("Pilaf de crevettes au safran", 16, 60, 0, (Speed, .08f), (Stamina, .12f)),
            ["Shrimp scampi"] = new("Scampi aux crevettes", 15, 45, 0, (AtkSpeed, .08f), (MagicDmg, .06f)),
            ["Venison stew with root vegetables"] = new("Ragoût de chevreuil aux légumes", 23, 60, 1.5f, (Life, .16f), (DefPhys, .10f)),
        };

        // Icônes pour les plats d'origine (sans effet de gameplay modifié).
        private static readonly Dictionary<string, string> LegacyIcons = new()
        {
            ["Dish_SoupeChaude"] = ImagesFolder + "/Chicken soup.png",
            ["Dish_RagoutDuChef"] = ImagesFolder + "/Venison stew with root vegetables.png",
            ["Dish_FromageCharcuterie"] = "Assets/Arts/2D/Cooking Ingredients/Cheese.png",
            ["Dish_Hydromel"] = "Assets/Arts/2D/Cooking Ingredients/Honey.png",
        };

        [MenuItem("Village/Taverne/Générer les plats")]
        public static void Run()
        {
            EnsureFolder(ItemFolder);
            int itemsCreated = 0, dishesCreated = 0;

            // 1. Plats existants : on leur donne un objet consommable.
            foreach (string guid in AssetDatabase.FindAssets("t:TavernDishDefinition", new[] { DishFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var dish = AssetDatabase.LoadAssetAtPath<TavernDishDefinition>(path);
                if (LegacyIcons.TryGetValue(dish.name, out string iconPath))
                    SetObject(dish, "icon", LoadSprite(iconPath));
                if (dish.Item == null)
                {
                    SetObject(dish, "item", CreateItemFromDish(dish, Slug(dish.name.Replace("Dish_", "")), ref itemsCreated));
                }
            }

            // 2. Nouveaux plats depuis les images.
            var menu = AssetDatabase.LoadAssetAtPath<TavernMenuDefinition>(MenuPath);
            var added = new List<TavernDishDefinition>();
            var seen = new HashSet<string>();
            foreach (string file in Directory.GetFiles(ImagesFolder, "*.png"))
            {
                string baseName = Regex.Replace(Path.GetFileNameWithoutExtension(file).Normalize(System.Text.NormalizationForm.FormC), @" [12]$", "");
                if (!Specs.TryGetValue(baseName, out Spec spec) || !seen.Add(baseName)) continue;

                string slug = Slug(baseName);
                string dishPath = $"{DishFolder}/Dish_{slug}.asset";
                if (AssetDatabase.LoadAssetAtPath<TavernDishDefinition>(dishPath) != null) continue;

                string iconAsset = file.Replace('\\', '/'); // première variante rencontrée (" 1" avant " 2")
                Sprite icon = LoadSprite(iconAsset);

                var dish = ScriptableObject.CreateInstance<TavernDishDefinition>();
                AssetDatabase.CreateAsset(dish, dishPath);
                var so = new SerializedObject(dish);
                so.FindProperty("dishName").stringValue = spec.French;
                so.FindProperty("description").stringValue = Describe(spec);
                so.FindProperty("icon").objectReferenceValue = icon;
                so.FindProperty("cost").intValue = spec.Cost;
                so.FindProperty("duration").floatValue = spec.Duration;
                so.FindProperty("healPercentPerSecond").floatValue = spec.Regen;
                var list = so.FindProperty("statEffects");
                list.arraySize = spec.Effects.Length;
                for (int i = 0; i < spec.Effects.Length; i++)
                {
                    SerializedProperty e = list.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("type").enumValueIndex = spec.Effects[i].stat;
                    e.FindPropertyRelative("value").floatValue = spec.Effects[i].value;
                    e.FindPropertyRelative("modifierType").enumValueIndex = 1; // PercentAdditive
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                SetObject(dish, "item", CreateItemFromDish(dish, slug, ref itemsCreated));
                added.Add(dish);
                dishesCreated++;
            }

            if (menu != null && added.Count > 0)
            {
                var mso = new SerializedObject(menu);
                SerializedProperty dishes = mso.FindProperty("dishes");
                foreach (TavernDishDefinition d in added)
                {
                    dishes.arraySize++;
                    dishes.GetArrayElementAtIndex(dishes.arraySize - 1).objectReferenceValue = d;
                }
                mso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(menu);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Taverne] {itemsCreated} objet(s) et {dishesCreated} plat(s) créé(s).");
        }

        private static ItemDefinition CreateItemFromDish(TavernDishDefinition dish, string slug, ref int created)
        {
            string path = $"{ItemFolder}/Food_{slug}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (existing != null) return existing;

            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
            var so = new SerializedObject(item);
            so.FindProperty("<Id>k__BackingField").stringValue = "food_" + slug.ToLowerInvariant();
            so.FindProperty("<DisplayName>k__BackingField").stringValue = dish.DishName;
            so.FindProperty("<Icon>k__BackingField").objectReferenceValue = dish.Icon;
            so.FindProperty("<MaxStackSize>k__BackingField").intValue = 20;
            so.FindProperty("category").enumValueIndex = (int)ItemCategory.Consumable;
            so.FindProperty("interactionVerb").stringValue = "Ramasser";
            so.FindProperty("description").stringValue = dish.Description;

            // Les effets du plat (stats + durée) deviennent ceux de l'objet consommable.
            var effects = so.FindProperty("statEffects");
            effects.arraySize = dish.StatEffects.Count;
            for (int i = 0; i < dish.StatEffects.Count; i++)
            {
                SerializedProperty e = effects.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("type").enumValueIndex = (int)dish.StatEffects[i].type;
                e.FindPropertyRelative("value").floatValue = dish.StatEffects[i].value;
                e.FindPropertyRelative("modifierType").enumValueIndex = (int)dish.StatEffects[i].modifierType;
                e.FindPropertyRelative("duration").floatValue = dish.Duration;
            }
            so.FindProperty("regenPercentPerSecond").floatValue = dish.HealPercentPerSecond;
            so.FindProperty("regenDuration").floatValue = dish.HealPercentPerSecond > 0f ? dish.Duration : 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            created++;
            return item;
        }

        private static string Describe(Spec spec)
        {
            var parts = new List<string>();
            foreach ((int stat, float value) in spec.Effects)
                parts.Add($"+{value * 100f:0.#}% {StatName(stat)}");
            if (spec.Regen > 0f) parts.Add($"Régénération {spec.Regen:0.#}%/s");
            return string.Join(", ", parts);
        }

        private static string StatName(int stat) => stat switch
        {
            Life => "PV max",
            Stamina => "Endurance",
            PhysDmg => "Dégâts physiques",
            MagicDmg => "Dégâts magiques",
            DefPhys => "Défense physique",
            DefMagic => "Défense magique",
            Speed => "Vitesse",
            _ => "Vitesse d'attaque"
        };

        private static Sprite LoadSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void SetObject(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static string Slug(string name) => Regex.Replace(name, @"[^A-Za-z0-9]", "");

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
