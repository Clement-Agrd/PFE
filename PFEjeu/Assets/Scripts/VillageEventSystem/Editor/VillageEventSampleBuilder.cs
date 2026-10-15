using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Core.InventorySystem;
using Core.Village;

namespace Core.VillageEvents.Editor
{
    /// <summary>
    /// Génère les 4 événements du catalogue de design dans Assets/ScriptableObjects/VillageEvents
    /// (valeurs indicatives à calibrer). Menu : Village > Créer les événements d'exemple.
    /// </summary>
    public static class VillageEventSampleBuilder
    {
        private const string Folder = "Assets/ScriptableObjects/VillageEvents";

        [MenuItem("Village/Créer les événements d'exemple")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/ScriptableObjects", "VillageEvents");

            ItemDefinition bois = Item("Bois"), pierre = Item("Pierre"), fer = Item("Fer"), nourriture = Item("Nourriture");
            BuildingDefinition taverne = Bld("Taverne"), mine = Bld("Mine"), ferme = Bld("Ferme"), temple = Bld("TempleChamanique");

            // --- Les remparts saignent ---
            Make("RempartsSaignent", "Les remparts saignent",
                "Les palissades ont tenu cette nuit, mais elles se fissurent. Il faut décider qui paie pour les remettre debout.",
                weight: 1f, cooldown: 3, unique: false,
                cond: c => c.wallsBelowPercent = 50,
                choices: new[]
                {
                    Choice("Réparation à marche forcée", false,
                        Fx(EffectType.WallRepair, 40), Fx(EffectType.Resource, -40, bois), Fx(EffectType.Resource, -20, pierre)),
                    Choice("Les guerriers montent la garde", false,
                        Fx(EffectType.WallRepair, 20), Fx(EffectType.HeroUnavailable, 1)),
                    Choice("Laisser tenir", true,
                        Fx(EffectType.NextNightEnemies, 15))
                });

            // --- Disette à la Ferme ---
            Make("DisetteFerme", "Disette à la Ferme",
                "Les greniers sonnent creux. Les enfants mangent moins que les guerriers, et tout le monde le voit.",
                weight: 1f, cooldown: 3, unique: false,
                cond: c =>
                {
                    c.stockItem = nourriture; c.stockBelow = 60;
                    c.requiredBuildings = new[] { ferme, taverne };
                },
                choices: new[]
                {
                    Choice("Rationner les habitants", false,
                        Fx(EffectType.Resource, 30, nourriture), Fx(EffectType.BuildingDowntime, 1, null, taverne)),
                    Choice("Battue de chasse", false,
                        Fx(EffectType.Resource, 40, nourriture), Fx(EffectType.HeroUnavailable, 1)),
                    Choice("Ne rien changer", true,
                        Fx(EffectType.NextNightEnemies, 10))
                });

            // --- La Mine s'effondre ---
            Make("MineEffondree", "La Mine s'effondre",
                "Les galeries ont tremblé avec chaque assaut. Un étai a lâché, et les mineurs attendent votre ordre.",
                weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { mine }; c.requiredBuildingMinLevel = 2; },
                choices: new[]
                {
                    Choice("Étayer à la hâte", false,
                        Fx(EffectType.Resource, -30, bois)),
                    Choice("Creuser plus profond", false,
                        Fx(EffectType.Resource, 25, fer), Fx(EffectType.CorruptionTier, 1)),
                    Choice("Fermer la galerie", true,
                        Fx(EffectType.BuildingDowntime, 2, null, mine))
                });

            // --- Murmures sous la terre ---
            Make("MurmuresSousLaTerre", "Murmures sous la terre",
                "Une voix monte du sol, juste sous le seuil de l'entente. Elle connaît vos noms.",
                weight: 1.5f, cooldown: 4, unique: false,
                cond: c => c.minNight = 3,
                choices: new[]
                {
                    Choice("Consulter l'ancien chaman", false,
                        Fx(EffectType.RitualAdvance, 1), Fx(EffectType.HeroUnavailable, 2)),
                    Choice("Écouter les voix", false,
                        Fx(EffectType.BuildingLevelUp, 1, null, temple), Fx(EffectType.CorruptionTier, 1)),
                    Choice("Purifier le village", false,
                        Fx(EffectType.CorruptionTier, -1), Fx(EffectType.Resource, -20, nourriture)),
                    Choice("Ignorer les voix", true,
                        Fx(EffectType.NextNightEnemies, 10))
                });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VillageEvents] Événements créés dans {Folder}. Glissez-les dans le catalogue du VillageEventManager.");
        }

        // ---------------------------------------------------------------- helpers

        private static ItemDefinition Item(string n) => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/ScriptableObjects/Inventory/{n}.asset");
        private static BuildingDefinition Bld(string n) => AssetDatabase.LoadAssetAtPath<BuildingDefinition>($"Assets/ScriptableObjects/Village/{n}.asset");

        private static EventEffect Fx(EffectType t, int amount, ItemDefinition item = null, BuildingDefinition b = null)
            => new EventEffect { type = t, amount = amount, item = item, building = b };

        private static EventChoice Choice(string label, bool free, params EventEffect[] effects)
            => new EventChoice { label = label, isFreeOption = free, effects = effects };

        private static void Make(string file, string title, string flavor, float weight, int cooldown, bool unique,
            System.Action<EventConditions> cond, EventChoice[] choices)
        {
            string path = $"{Folder}/{file}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<VillageEventDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VillageEventDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var conditions = new EventConditions();
            cond?.Invoke(conditions);

            asset.EditorSetup(title, flavor, weight, cooldown, unique, conditions, choices);
            EditorUtility.SetDirty(asset);
        }
    }
}
