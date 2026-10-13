#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Core.InventorySystem;
using Core.Village;

namespace Core.TrapSystem.EditorTools
{
    /// <summary>
    /// Crée les 4 pièges (prefabs provisoires + ItemDefinition de catégorie Trap) et les ajoute
    /// à la boutique de la Forge. Menu : Tools > Village > Créer les pièges et les ajouter à la Forge.
    /// </summary>
    public static class TrapSetupBuilder
    {
        private const string ItemsFolder = "Assets/ScriptableObjects/Inventory";
        private const string PrefabFolder = "Assets/Prefabs/Traps";
        private const string ForgePath = "Assets/ScriptableObjects/Village/ForgeDefinition.asset";

        private struct Spec
        {
            public string file, id, display;
            public PrimitiveType shape;
            public Vector3 scale;
            public Color color;
            public System.Type trapType;
            public int damage;
            public (string item, int amount)[] cost;
        }

        [MenuItem("Tools/Village/Créer les pièges et les ajouter à la Forge")]
        public static void Build()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);

            Spec[] specs =
            {
                new Spec { file = "SacocheExplosive", id = "sacoche_explosive", display = "Sacoche explosive",
                    shape = PrimitiveType.Cube, scale = new Vector3(0.5f, 0.4f, 0.3f), color = new Color(0.45f, 0.3f, 0.15f),
                    trapType = typeof(ExplosiveSatchelTrap), damage = 80, cost = new[] { ("Fer", 4), ("Pierre", 4) } },
                new Spec { file = "PiegeInflammable", id = "piege_inflammable", display = "Piège inflammable",
                    shape = PrimitiveType.Cylinder, scale = new Vector3(0.6f, 0.05f, 0.6f), color = new Color(0.9f, 0.35f, 0.05f),
                    trapType = typeof(FlammableTrap), damage = 8, cost = new[] { ("Bois", 6), ("Pierre", 2) } },
                new Spec { file = "PiegeAPointes", id = "piege_a_pointes", display = "Piège à pointes",
                    shape = PrimitiveType.Cube, scale = new Vector3(3f, 0.1f, 3f), color = new Color(0.5f, 0.5f, 0.55f),
                    trapType = typeof(SpikeTrap), damage = 3, cost = new[] { ("Bois", 6), ("Fer", 3) } },
                new Spec { file = "PiegeALoup", id = "piege_a_loup", display = "Piège à loup",
                    shape = PrimitiveType.Cylinder, scale = new Vector3(0.7f, 0.05f, 0.7f), color = new Color(0.3f, 0.3f, 0.3f),
                    trapType = typeof(WolfTrap), damage = 60, cost = new[] { ("Fer", 6), ("Pierre", 2) } },
            };

            ForgeDefinition forge = AssetDatabase.LoadAssetAtPath<ForgeDefinition>(ForgePath);
            if (forge == null)
                Debug.LogWarning("[TrapSetupBuilder] ForgeDefinition introuvable : lance d'abord le ForgePanelBuilder. " +
                                 "Les objets et prefabs sont créés quand même.");

            foreach (Spec s in specs)
            {
                GameObject prefab = CreatePrefab(s);
                ItemDefinition item = CreateItem(s, prefab);
                if (forge != null) AddToShop(forge, item, s.cost);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TrapSetupBuilder] 4 pièges créés. Ajoute les ItemDefinition à l'ItemDatabase, " +
                      "et un TrapPlacer sur le joueur (touche T = poser, Y = changer de piège).");
        }

        private static GameObject CreatePrefab(Spec s)
        {
            string path = $"{PrefabFolder}/{s.file}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject go = GameObject.CreatePrimitive(s.shape);
            go.name = s.file;
            go.transform.localScale = s.scale;

            var renderer = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                   ?? Shader.Find("Standard")) { color = s.color };
            string matPath = $"{PrefabFolder}/{s.file}.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            renderer.sharedMaterial = mat;

            bool needsTrigger = s.trapType == typeof(SpikeTrap) || s.trapType == typeof(WolfTrap);
            var col = go.GetComponent<Collider>();
            if (needsTrigger)
            {
                col.isTrigger = true;
                // Un trigger plat est difficile à traverser : on le rend plus haut.
                if (col is BoxCollider box) box.size = new Vector3(box.size.x, 10f, box.size.z);
                else if (col is CapsuleCollider cap) cap.height = 10f;
            }
            else
            {
                Object.DestroyImmediate(col);
            }

            go.AddComponent(s.trapType);
            var so = new SerializedObject(go.GetComponent(s.trapType));
            so.FindProperty("baseDamage").intValue = s.damage;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved;
        }

        private static ItemDefinition CreateItem(Spec s, GameObject prefab)
        {
            string path = $"{ItemsFolder}/{s.file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }

            var so = new SerializedObject(item);
            so.FindProperty("<Id>k__BackingField").stringValue = s.id;
            so.FindProperty("<DisplayName>k__BackingField").stringValue = s.display;
            so.FindProperty("<MaxStackSize>k__BackingField").intValue = 20;
            so.FindProperty("<WorldPrefab>k__BackingField").objectReferenceValue = prefab;
            so.FindProperty("category").enumValueIndex =
                System.Array.IndexOf(System.Enum.GetValues(typeof(ItemCategory)), ItemCategory.Trap);
            so.FindProperty("interactionVerb").stringValue = "Poser";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void AddToShop(ForgeDefinition forge, ItemDefinition item, (string item, int amount)[] cost)
        {
            var so = new SerializedObject(forge);
            SerializedProperty shop = so.FindProperty("shop");

            for (int i = 0; i < shop.arraySize; i++)
                if (shop.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == item)
                    return;

            shop.arraySize++;
            SerializedProperty entry = shop.GetArrayElementAtIndex(shop.arraySize - 1);
            entry.FindPropertyRelative("item").objectReferenceValue = item;
            SerializedProperty costProp = entry.FindPropertyRelative("cost");
            costProp.arraySize = cost.Length;
            for (int i = 0; i < cost.Length; i++)
            {
                SerializedProperty c = costProp.GetArrayElementAtIndex(i);
                c.FindPropertyRelative("item").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{cost[i].item}.asset");
                c.FindPropertyRelative("amount").intValue = cost[i].amount;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(forge);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
