using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Core.InventorySystem;
using Core.Village;

namespace Core.VillageEvents.Editor
{
    /// <summary>
    /// Génère une vingtaine d'événements supplémentaires dans Assets/ScriptableObjects/VillageEvents,
    /// construits sur les ressources, bâtiments et héros qui existent déjà dans le projet.
    /// Règles de design respectées : chaque choix a une concession ET un gain (sauf la sortie « gratuite »),
    /// et deux choix d'un même événement ne paient jamais sur le même axe.
    /// Menus : Village > Créer les événements supplémentaires, Village > Remplir le catalogue du manager.
    /// </summary>
    public static class VillageEventExpansionBuilder
    {
        private const string Folder = "Assets/ScriptableObjects/VillageEvents";

        [MenuItem("Village/Créer les événements supplémentaires")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/ScriptableObjects", "VillageEvents");

            ItemDefinition bois = Item("Bois"), pierre = Item("Pierre"), fer = Item("Fer"), nourriture = Item("Nourriture");
            BuildingDefinition taverne = Bld("Taverne"), mine = Bld("Mine"), ferme = Bld("Ferme"), forge = Bld("Forge"),
                scierie = Bld("Scierie"), caserne = Bld("Caserne"), camp = Bld("CampEntrainement"), temple = Bld("TempleChamanique");

            // ============================================================ ÉCONOMIE

            Make("MarchandAmbulant", "Un marchand ambulant",
                "Un chariot bringuebalant s'arrête devant les palissades. Le marchand jure qu'il ne reste qu'une nuit à l'abri.",
                new[]
                {
                    "Un marchand frappe à la herse, le dos courbé sous ses ballots. Il propose des prix qu'il ne tiendra pas deux fois.",
                    "Des clochettes tintent dans la brume : un colporteur a survécu à la route, et il veut commercer avant l'aube."
                },
                weight: 1.2f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { taverne }; },
                choices: new[]
                {
                    Choice("Troquer du bois contre des vivres", false, Res(bois, -40), Res(nourriture, 50)),
                    Choice("L'héberger à la Taverne", false, Down(taverne, 1), Res(pierre, 35)),
                    Choice("Louer son escorte", false, Hero(1), Res(fer, 30)),
                    Choice("Passer son chemin", true, Foes(5))
                });

            Make("TroupeauEgare", "Le troupeau s'est égaré",
                "Les bêtes ont fui pendant l'assaut. On entend leurs cloches dans le noir, du mauvais côté de la herse.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { ferme }; },
                choices: new[]
                {
                    Choice("Une battue nocturne", false, Hero(1), Res(nourriture, 45)),
                    Choice("Payer les bergers", false, Res(bois, -25), Res(nourriture, 35)),
                    Choice("Abattre les bêtes qui toussent", false, Cor(1), Res(nourriture, 60)),
                    Choice("Les laisser partir", true, Foes(5))
                });

            Make("SurplusDeBois", "Le bois s'entasse",
                "Les piles de grumes dépassent les toits. Trop de bois, et des mains qui ne savent plus où le mettre.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.stockAboveItem = bois; c.stockAbove = 150; },
                choices: new[]
                {
                    Choice("Doubler les palissades", false, Res(bois, -80), Wall(25)),
                    Choice("Grande corvée des guerriers", false, Hero(1), Wall(35)),
                    Choice("Laisser pourrir", true, Foes(10))
                });

            Make("LivraisonDeFer", "Les forgerons n'ont plus de fer",
                "Les marteaux se taisent. Sans minerai, la forge n'est qu'un grand feu inutile.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { forge }; c.stockItem = fer; c.stockBelow = 40; },
                choices: new[]
                {
                    Choice("Fondre les vieux outils", false, Down(forge, 1), Res(fer, 40)),
                    Choice("Acheter à prix d'or", false, Res(nourriture, -30), Res(fer, 35)),
                    Choice("Envoyer chercher du minerai", false, Hero(1), Res(fer, 45)),
                    Choice("Attendre la prochaine nuit", true, Foes(10))
                });

            Make("FilonLuisant", "Un filon luisant",
                "Un mineur remonte, les yeux écarquillés : la roche brille d'un éclat qu'il n'avait jamais vu.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { mine }; c.minNight = 2; },
                choices: new[]
                {
                    Choice("Creuser sans étais", false, Cor(1), Res(fer, 45)),
                    Choice("Étayer puis creuser", false, Res(bois, -35), Res(fer, 35)),
                    Choice("Y envoyer les héros", false, Hero(1), Res(pierre, 50)),
                    Choice("Reboucher la galerie", true, Foes(5))
                });

            Make("RecolteExceptionnelle", "Une récolte hors du commun",
                "Les champs ont donné plus que de raison, comme si la terre voulait se faire pardonner quelque chose.",
                null, weight: 0.8f, cooldown: 5, unique: false,
                cond: c => { c.requiredBuildings = new[] { ferme }; c.requiredBuildingMinLevel = 2; },
                choices: new[]
                {
                    Choice("Tout engranger de nuit", false, Hero(1), Res(nourriture, 70)),
                    Choice("Organiser un grand banquet", false, Down(taverne, 1), Res(nourriture, 40), Foes(-10)),
                    Choice("Offrir la moitié au chaman", false, Res(bois, -30), Cor(-1))
                });

            // ============================================================ REMPARTS & DÉFENSE

            Make("TempeteDeNuit", "Une tempête arrache les planches",
                "Le vent a fait ce que les ennemis n'ont pas pu. Des pans entiers de palissade pendent dans le vide.",
                new[]
                {
                    "La pluie a gonflé le bois, puis l'a fait éclater. Les remparts gémissent à chaque rafale.",
                },
                weight: 1.2f, cooldown: 3, unique: false,
                cond: c => { c.wallsBelowPercent = 70; c.minNight = 2; },
                choices: new[]
                {
                    Choice("Réparer sous l'orage", false, Hero(1), Wall(30)),
                    Choice("Démonter la Scierie pour du bois", false, Down(scierie, 1), Wall(35)),
                    Choice("Utiliser les pierres de réserve", false, Res(pierre, -40), Wall(30)),
                    Choice("Prier pour que ça tienne", true, Foes(15))
                });

            Make("RempartsSolides", "Les remparts tiennent bon",
                "Pas une fissure. Les anciens se demandent à voix basse si c'est de la chance ou un avertissement.",
                null, weight: 0.9f, cooldown: 4, unique: false,
                cond: c => { c.wallsAbovePercent = 90; c.minNight = 2; },
                choices: new[]
                {
                    Choice("Renforcer encore", false, Res(pierre, -30), Foes(-15)),
                    Choice("Entraîner la garde jusqu'à l'aube", false, Hero(1), Rit(1)),
                    Choice("Banquet de victoire", false, Down(taverne, 1), Foes(-10))
                });

            Make("HurlementsDansLaNuit", "Des hurlements dans la nuit",
                "Ce ne sont pas des loups. Les chiens du village refusent de sortir de leurs niches.",
                null, weight: 1.1f, cooldown: 3, unique: false,
                cond: c => { c.minNight = 2; },
                choices: new[]
                {
                    Choice("Allumer des torches partout", false, Res(bois, -30), Foes(-15)),
                    Choice("Sortir en patrouille", false, Hero(1), Foes(-20)),
                    Choice("Se terrer et attendre", true, Foes(10))
                });

            Make("GelPrecoce", "Un gel précoce",
                "L'eau des seaux est prise en glace avant minuit. Les doigts des sentinelles ne tiennent plus leur arme.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.minNight = 5; },
                choices: new[]
                {
                    Choice("Brûler du bois toute la nuit", false, Res(bois, -50), Foes(-15)),
                    Choice("Relever les sentinelles", false, Hero(1), Res(bois, 30)),
                    Choice("Serrer les rangs sans feu", true, Foes(15))
                });

            Make("DeserteurEnnemi", "Un déserteur ennemi",
                "Il est sorti des fourrés les mains en l'air, couvert de boue. Il dit savoir où l'ennemi frappera demain.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.minNight = 3; },
                choices: new[]
                {
                    Choice("L'accueillir et le nourrir", false, Res(nourriture, -20), Foes(-15)),
                    Choice("L'interroger sans ménagement", false, Cor(1), Foes(-25)),
                    Choice("L'enrôler dans la garde", false, Hero(1), Wall(15)),
                    Choice("Le renvoyer dans la nuit", true, Foes(5))
                });

            // ============================================================ VILLAGE & GENS

            Make("IncendieALaForge", "Le feu à la Forge",
                "Une étincelle, un tonneau d'huile, et la Forge flambe. Les enclumes rougeoient dans la nuit.",
                null, weight: 1f, cooldown: 5, unique: false,
                cond: c => { c.requiredBuildings = new[] { forge }; c.minNight = 2; },
                choices: new[]
                {
                    Choice("La chaîne de seaux des guerriers", false, Hero(1), Res(fer, 20)),
                    Choice("Laisser brûler, sauver les pierres", false, Down(forge, 2), Res(pierre, 25)),
                    Choice("Demander l'aide du chaman", false, Cor(1), Rit(1)),
                    Choice("Étouffer le feu et se taire", true, Foes(10))
                });

            Make("FievreAuVillage", "La fièvre court au village",
                "Elle est arrivée avec la brume. Des fronts brûlants, des regards vides, et des enfants qui ne chantent plus.",
                null, weight: 1f, cooldown: 5, unique: false,
                cond: c => { c.minNight = 4; c.minBuildingCount = 3; },
                choices: new[]
                {
                    Choice("Isoler un bâtiment entier", false, RDown(2), Cor(-1)),
                    Choice("Soigner à prix d'or", false, Res(nourriture, -35), Foes(-10)),
                    Choice("Laisser le mal suivre son cours", true, Hero(1))
                });

            Make("DisputeAuConseil", "Dispute au conseil",
                "Les bâtisseurs et les guerriers s'invectivent devant l'Hôtel de ville. L'un des deux camps partira vexé.",
                null, weight: 1f, cooldown: 5, unique: false,
                cond: c => { c.minTownHallLevel = 2; c.minBuildingCount = 3; },
                choices: new[]
                {
                    Choice("Donner raison aux bâtisseurs", false, Hero(1), RUp()),
                    Choice("Donner raison aux guerriers", false, Res(bois, -30), Foes(-15)),
                    Choice("Faire payer les deux camps", false, Cor(1), Res(pierre, 30)),
                    Choice("Ajourner la séance", true, Foes(10))
                });

            Make("SurvivantsAuxPortes", "Des survivants aux portes",
                "Une poignée d'hommes et de femmes, en haillons, supplient qu'on leur ouvre. Ils ont tout perdu.",
                null, weight: 1.3f, cooldown: 4, unique: false,
                cond: c => { c.maxNight = 5; },
                choices: new[]
                {
                    Choice("Les accueillir et les nourrir", false, Res(nourriture, -30), Wall(20)),
                    Choice("Leur faire jurer fidélité au rituel", false, Cor(1), Rit(1)),
                    Choice("Refuser, la mort dans l'âme", true, Foes(10))
                });

            Make("PelerinsAffames", "Des pèlerins affamés",
                "Ils marchent vers la montagne sainte et demandent le gîte. Les greniers sont pleins ; la question est ailleurs.",
                null, weight: 1f, cooldown: 5, unique: false,
                cond: c => { c.stockAboveItem = nourriture; c.stockAbove = 100; },
                choices: new[]
                {
                    Choice("Les nourrir sans compter", false, Res(nourriture, -50), Cor(-1)),
                    Choice("Les loger à la Caserne", false, Down(caserne, 1), Res(fer, 25)),
                    Choice("Les mettre aux remparts", false, Hero(1), Wall(25)),
                    Choice("Les éconduire", true, Foes(10))
                });

            // ============================================================ HÉROS & RITUEL

            Make("VisiteDuChaman", "Le chaman vous demande audience",
                "Il n'a pas dormi depuis trois nuits. Ses mains tremblent quand il parle de ce qui dort sous le temple.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { temple }; c.minNight = 3; },
                choices: new[]
                {
                    Choice("Écouter la prophétie", false, Cor(1), Rit(1)),
                    Choice("Lui faire une offrande", false, Res(nourriture, -30), Cor(-1)),
                    Choice("Faire bénir les remparts", false, Hero(1), Wall(25)),
                    Choice("Le congédier poliment", true, Foes(10))
                });

            Make("QuerelleDesRecrues", "Les recrues se querellent",
                "Au Camp d'entraînement, deux escouades en sont venues aux poings. Les sergents attendent un verdict.",
                null, weight: 1f, cooldown: 4, unique: false,
                cond: c => { c.requiredBuildings = new[] { camp }; c.minNight = 3; },
                choices: new[]
                {
                    Choice("Manœuvres communes jusqu'à l'aube", false, Hero(1), Up(camp)),
                    Choice("Punir les meneurs", false, Down(camp, 1), Foes(-15)),
                    Choice("Offrir un repas aux deux escouades", false, Res(nourriture, -30), Rit(1)),
                    Choice("Les laisser régler ça", true, Foes(10))
                });

            Make("ForgeInspiree", "Le maître forgeron a une idée",
                "Il dessine à la craie sur l'enclume, marmonne, efface, recommence. Quelque chose de neuf est en train de naître.",
                null, weight: 1f, cooldown: 5, unique: false,
                cond: c => { c.requiredBuildings = new[] { forge }; c.requiredBuildingMinLevel = 2; c.minNight = 4; },
                choices: new[]
                {
                    Choice("Commander des lames neuves", false, Res(fer, -40), Foes(-20)),
                    Choice("Libérer ses apprentis du guet", false, Hero(1), Up(forge)),
                    Choice("Forger de nuit, à la lueur étrange", false, Cor(1), Rit(1)),
                    Choice("Lui dire d'attendre", true, Foes(10))
                });

            Make("RituelVacille", "Le rituel vacille",
                "Les symboles gravés au sol saignent une lumière pâle. Quelqu'un doit décider du prix de la suite.",
                null, weight: 1.4f, cooldown: 4, unique: false,
                cond: c => { c.minNight = 6; c.minCorruptionTier = 1; },
                choices: new[]
                {
                    Choice("Offrir un sacrifice de vivres", false, Res(nourriture, -50), Cor(-1)),
                    Choice("Forcer le rythme", false, Cor(1), Rit(2)),
                    Choice("Laisser le village se reposer", false, Hero(1), Foes(-20)),
                    Choice("Ne rien toucher", true, Foes(20))
                });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VillageEvents] Événements supplémentaires créés dans {Folder}. " +
                      "Menu Village > Remplir le catalogue du manager pour les brancher.");
        }

        /// <summary>Met tous les événements du dossier dans le catalogue du/des VillageEventManager de la scène ouverte.</summary>
        [MenuItem("Village/Remplir le catalogue du manager")]
        public static void FillCatalog()
        {
            var all = AssetDatabase.FindAssets("t:VillageEventDefinition", new[] { Folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<VillageEventDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).OrderBy(a => a.name).ToList();

            var managers = Object.FindObjectsByType<VillageEventManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (managers.Length == 0) { Debug.LogWarning("[VillageEvents] Aucun VillageEventManager dans la scène ouverte."); return; }

            foreach (VillageEventManager m in managers)
            {
                var so = new SerializedObject(m);
                SerializedProperty cat = so.FindProperty("catalog");
                cat.arraySize = all.Count;
                for (int i = 0; i < all.Count; i++) cat.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(m);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[VillageEvents] Catalogue rempli avec {all.Count} événements ({managers.Length} manager(s)). Pensez à sauvegarder la scène.");
        }

        // ---------------------------------------------------------------- helpers

        private static ItemDefinition Item(string n) => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/ScriptableObjects/Inventory/{n}.asset");
        private static BuildingDefinition Bld(string n) => AssetDatabase.LoadAssetAtPath<BuildingDefinition>($"Assets/ScriptableObjects/Village/{n}.asset");

        private static EventEffect Res(ItemDefinition item, int n) => new EventEffect { type = EffectType.Resource, item = item, amount = n };
        private static EventEffect Wall(int pct) => new EventEffect { type = EffectType.WallRepair, amount = pct };
        private static EventEffect Down(BuildingDefinition b, int days) => new EventEffect { type = EffectType.BuildingDowntime, building = b, amount = days };
        private static EventEffect RDown(int days) => new EventEffect { type = EffectType.BuildingDowntime, randomBuilding = true, amount = days };
        private static EventEffect Up(BuildingDefinition b) => new EventEffect { type = EffectType.BuildingLevelUp, building = b, amount = 1 };
        private static EventEffect RUp() => new EventEffect { type = EffectType.BuildingLevelUp, randomBuilding = true, amount = 1 };
        private static EventEffect Hero(int cycles) => new EventEffect { type = EffectType.HeroUnavailable, amount = cycles };
        private static EventEffect Rit(int nights) => new EventEffect { type = EffectType.RitualAdvance, amount = nights };
        private static EventEffect Foes(int pct) => new EventEffect { type = EffectType.NextNightEnemies, amount = pct };
        private static EventEffect Cor(int tiers) => new EventEffect { type = EffectType.CorruptionTier, amount = tiers };

        private static EventChoice Choice(string label, bool free, params EventEffect[] effects)
            => new EventChoice { label = label, isFreeOption = free, effects = effects };

        private static void Make(string file, string title, string flavor, string[] variants, float weight, int cooldown,
            bool unique, System.Action<EventConditions> cond, EventChoice[] choices)
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
            asset.EditorSetup(title, flavor, weight, cooldown, unique, conditions, choices, variants);
            EditorUtility.SetDirty(asset);
        }
    }
}
