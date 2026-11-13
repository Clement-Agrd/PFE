using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>
    /// Fenêtre unique : Tools > Easy Terrain > Ouvrir.
    ///
    /// EasyTerrainWindow
    /// ├── Terrain    : création + "tout générer depuis le profil"
    /// ├── Relief     : presets (collines, montagnes...), lissage, érosion
    /// ├── Textures   : couches, peinture auto, casser la répétition
    /// ├── Végétation : arbres + herbe (dispersion auto)
    /// └── Pinceau    : relief / texture / overlay / herbe / arbres dans la Scene
    /// </summary>
    public sealed class EasyTerrainWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Terrain", "Relief", "Textures", "Végétation", "Pinceau" };
        private static readonly string[] BrushLabels =
        {
            "Relief : monter", "Relief : creuser", "Relief : lisser", "Relief : aplatir",
            "Texture : peindre", "Texture : overlay (posée en taches)",
            "Herbe : ajouter", "Herbe : effacer", "Arbres : ajouter", "Arbres : effacer"
        };

        [SerializeField] private Terrain targetTerrain;
        [SerializeField] private EasyTerrainProfile profile;
        [SerializeField] private int tab;
        [SerializeField] private bool brushEnabled;
        [SerializeField] private TerrainCreateSettings create = new TerrainCreateSettings();
        [SerializeField] private GeneratorSettings generator = new GeneratorSettings();
        [SerializeField] private BrushSettings brush = new BrushSettings();

        // Ajout rapide d'une couche
        [SerializeField] private Texture2D newAlbedo;
        [SerializeField] private Texture2D newNormal;
        [SerializeField] private float newTileSize = 10f;
        [SerializeField] private bool newAddToProfile = true;

        // Retouches globales
        [SerializeField] private int smoothIterations = 2;
        [SerializeField] private int erosionIterations = 8;
        [SerializeField] private float talusDegrees = 38f;
        [SerializeField] private float flattenMeters = 10f;

        private SerializedObject self;
        private SerializedObject profileObject;
        private Vector2 scroll;
        private EasyTerrainBrush brushTool;

        [MenuItem("Tools/Easy Terrain/Ouvrir")]
        public static void Open()
        {
            var window = GetWindow<EasyTerrainWindow>("Easy Terrain");
            window.minSize = new Vector2(360f, 420f);
        }

        #region Cycle de vie
        private void OnEnable()
        {
            brushTool = new EasyTerrainBrush(() => targetTerrain, () => profile, () => brush);
            if (brushEnabled) brushTool.Attach();
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            brushTool?.Detach();
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out Terrain selected))
            {
                targetTerrain = selected;
                Repaint();
            }
        }
        #endregion

        private void OnGUI()
        {
            self = self ?? new SerializedObject(this);
            self.Update();

            DrawHeader();
            tab = GUILayout.Toolbar(tab, TabNames);
            EditorGUILayout.Space(4f);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: DrawTerrainTab(); break;
                case 1: DrawReliefTab(); break;
                case 2: DrawTexturesTab(); break;
                case 3: DrawVegetationTab(); break;
                case 4: DrawBrushTab(); break;
            }
            EditorGUILayout.EndScrollView();

            self.ApplyModifiedProperties();
        }

        #region En-tête
        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            targetTerrain = (Terrain)EditorGUILayout.ObjectField("Terrain cible", targetTerrain, typeof(Terrain), true);

            EditorGUILayout.BeginHorizontal();
            profile = (EasyTerrainProfile)EditorGUILayout.ObjectField("Profil (biome)", profile, typeof(EasyTerrainProfile), false);
            if (GUILayout.Button("Nouveau", GUILayout.Width(64f))) CreateProfile();
            EditorGUILayout.EndHorizontal();

            bool enabled = GUILayout.Toggle(brushEnabled, brushEnabled ? "Pinceau Scene : ACTIF (clic gauche sur le terrain)" : "Pinceau Scene : désactivé", "Button");
            if (enabled != brushEnabled)
            {
                brushEnabled = enabled;
                if (brushEnabled) brushTool.Attach(); else brushTool.Detach();
            }
            EditorGUILayout.EndVertical();
        }

        private void CreateProfile()
        {
            LayerFactory.EnsureFolder(LayerFactory.RootFolder);
            var created = CreateInstance<EasyTerrainProfile>();
            string path = AssetDatabase.GenerateUniqueAssetPath(LayerFactory.RootFolder + "/EasyTerrainProfile.asset");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            profile = created;
            profileObject = null;
            EditorGUIUtility.PingObject(created);
        }

        private bool RequireTerrain()
        {
            if (targetTerrain != null) return true;
            EditorGUILayout.HelpBox("Sélectionne un terrain dans la hiérarchie (ou crée-en un dans l'onglet Terrain).", MessageType.Info);
            return false;
        }

        private bool RequireProfile()
        {
            if (profile != null) return true;
            EditorGUILayout.HelpBox("Crée ou assigne un profil (bouton Nouveau) pour définir textures, arbres et herbe.", MessageType.Info);
            return false;
        }

        private SerializedObject ProfileObject()
        {
            if (profileObject == null || profileObject.targetObject != profile) profileObject = new SerializedObject(profile);
            profileObject.Update();
            return profileObject;
        }
        #endregion

        #region Onglet Terrain
        private void DrawTerrainTab()
        {
            EditorGUILayout.LabelField("Créer un terrain", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(self.FindProperty("create"), true);
            if (GUILayout.Button("Créer le terrain")) targetTerrain = TerrainFactory.Create(create);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Tout générer depuis le profil", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Enchaîne : peinture auto des textures (avec variantes anti-répétition), arbres, herbe. Relief non modifié.", MessageType.None);
            if (RequireTerrain() && RequireProfile() && GUILayout.Button("Appliquer le profil au terrain"))
            {
                SplatOps.AutoPaint(targetTerrain, profile);
                VegetationOps.ScatterTrees(targetTerrain, profile);
                VegetationOps.ScatterDetails(targetTerrain, profile);
                targetTerrain.Flush();
            }
        }
        #endregion

        #region Onglet Relief
        private void DrawReliefTab()
        {
            if (!RequireTerrain()) return;

            EditorGUILayout.LabelField("Générer un relief", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(self.FindProperty("generator"), true);
            if (GUILayout.Button("Générer"))
            {
                Undo.RegisterCompleteObjectUndo(targetTerrain.terrainData, "Générer relief");
                TerrainGenerator.Generate(targetTerrain, generator);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Retouches globales", EditorStyles.boldLabel);

            smoothIterations = EditorGUILayout.IntSlider("Itérations", smoothIterations, 1, 10);
            if (GUILayout.Button("Lisser tout le terrain")) RunGlobal("Lisser", () => TerrainGenerator.Smooth(targetTerrain, smoothIterations));

            erosionIterations = EditorGUILayout.IntSlider("Itérations d'érosion", erosionIterations, 1, 60);
            talusDegrees = EditorGUILayout.Slider("Angle de talus (°)", talusDegrees, 10f, 70f);
            if (GUILayout.Button("Éroder (éboulis sur les pentes raides)")) RunGlobal("Éroder", () => TerrainGenerator.ErodeThermal(targetTerrain, erosionIterations, talusDegrees));

            flattenMeters = EditorGUILayout.FloatField("Hauteur (m)", flattenMeters);
            if (GUILayout.Button("Aplatir à cette hauteur")) RunGlobal("Aplatir", () => TerrainGenerator.Flatten(targetTerrain, flattenMeters));

            EditorGUILayout.HelpBox("Le relief change : relance la peinture auto et la végétation (onglet Terrain).", MessageType.None);
        }

        private void RunGlobal(string label, System.Action action)
        {
            Undo.RegisterCompleteObjectUndo(targetTerrain.terrainData, label);
            action();
            targetTerrain.terrainData.SyncHeightmap();
        }
        #endregion

        #region Onglet Textures
        private void DrawTexturesTab()
        {
            if (!RequireProfile()) return;
            SerializedObject so = ProfileObject();

            EditorGUILayout.LabelField("Ajouter une texture au profil", EditorStyles.boldLabel);
            newAlbedo = (Texture2D)EditorGUILayout.ObjectField("Albedo", newAlbedo, typeof(Texture2D), false);
            newNormal = (Texture2D)EditorGUILayout.ObjectField("Normale (optionnel)", newNormal, typeof(Texture2D), false);
            newTileSize = EditorGUILayout.FloatField("Taille de tuile (m)", newTileSize);
            using (new EditorGUI.DisabledScope(newAlbedo == null))
            {
                if (GUILayout.Button("Créer la couche + règle"))
                {
                    TerrainLayer layer = LayerFactory.Create(newAlbedo, newNormal, null, newTileSize);
                    Undo.RecordObject(profile, "Ajouter couche");
                    profile.layers.Add(new LayerRule { label = newAlbedo.name, layer = layer });
                    EditorUtility.SetDirty(profile);
                    if (targetTerrain != null) SplatOps.AddLayerToTerrain(targetTerrain, layer);   // disponible tout de suite dans le pinceau, sans repeindre
                    newAlbedo = null;
                    newNormal = null;
                    so.Update();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.PropertyField(so.FindProperty("seed"));
            EditorGUILayout.PropertyField(so.FindProperty("layers"), new GUIContent("Règles de textures"), true);
            so.ApplyModifiedProperties();

            EditorGUILayout.HelpBox(
                "Casser la répétition : chaque règle peut mélanger une variante par taches de bruit.\n" +
                "Mets une AUTRE texture dans 'Variant Layer' pour poser une texture sur une autre.",
                MessageType.None);

            if (RequireTerrain())
            {
                if (GUILayout.Button("Ajouter les couches du profil au terrain (sans repeindre)"))
                {
                    int added = SplatOps.SyncProfileLayers(targetTerrain, profile);
                    Debug.Log("[EasyTerrain] " + added + " couche(s) ajoutée(s) au terrain. Elles sont disponibles dans le pinceau.");
                }
                if (GUILayout.Button("Peinture automatique (altitude / pente / bruit)")) SplatOps.AutoPaint(targetTerrain, profile);
                if (GUILayout.Button("Recréer les variantes (nouvelle taille de tuile)"))
                {
                    SplatOps.ResetVariants(profile);
                    SplatOps.AutoPaint(targetTerrain, profile);
                }
            }
        }
        #endregion

        #region Onglet Végétation
        private void DrawVegetationTab()
        {
            if (!RequireProfile()) return;
            SerializedObject so = ProfileObject();

            EditorGUILayout.PropertyField(so.FindProperty("trees"), new GUIContent("Arbres"), true);
            EditorGUILayout.PropertyField(so.FindProperty("details"), new GUIContent("Herbe / détails"), true);
            so.ApplyModifiedProperties();

            if (!RequireTerrain()) return;
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Dispersez APRÈS la peinture auto : les règles 'Only On Layer' lisent les textures déjà peintes.", MessageType.None);

            if (GUILayout.Button("Disperser les arbres")) VegetationOps.ScatterTrees(targetTerrain, profile);
            if (GUILayout.Button("Disperser l'herbe / les détails")) VegetationOps.ScatterDetails(targetTerrain, profile);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Effacer tous les arbres")) VegetationOps.ClearTrees(targetTerrain);
            if (GUILayout.Button("Effacer toute l'herbe")) VegetationOps.ClearDetails(targetTerrain);
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Onglet Pinceau
        private void DrawBrushTab()
        {
            if (!RequireTerrain()) return;

            int modeIndex = EditorGUILayout.Popup("Mode", (int)brush.mode, BrushLabels);
            brush.mode = (BrushMode)modeIndex;
            brush.radius = EditorGUILayout.Slider("Rayon (m)", brush.radius, 1f, 80f);
            brush.strength = EditorGUILayout.Slider("Force", brush.strength, 0.02f, 1f);

            if (brush.mode == BrushMode.PaintTexture || brush.mode == BrushMode.Overlay) DrawOwnTextureForm();

            switch (brush.mode)
            {
                case BrushMode.Flatten:
                    brush.flattenHeight = EditorGUILayout.FloatField("Hauteur cible (m, 0 = 1er clic)", brush.flattenHeight);
                    break;

                case BrushMode.PaintTexture:
                    brush.layerIndex = LayerPopup("Couche", brush.layerIndex);
                    break;

                case BrushMode.Overlay:
                    brush.layerIndex = LayerPopup("Couche à poser", brush.layerIndex);
                    brush.overlayScale = EditorGUILayout.Slider("Taille des taches (m)", brush.overlayScale, 0.5f, 60f);
                    brush.overlayAmount = EditorGUILayout.Slider("Couverture", brush.overlayAmount, 0f, 1f);
                    brush.overlaySharpness = EditorGUILayout.Slider("Netteté des bords", brush.overlaySharpness, 0f, 1f);
                    brush.overlaySeed = EditorGUILayout.IntField("Graine", brush.overlaySeed);
                    break;

                case BrushMode.AddGrass:
                    brush.detailRuleIndex = RulePopup("Herbe", profile != null ? profile.details : null, r => r.label, brush.detailRuleIndex);
                    break;

                case BrushMode.AddTrees:
                    brush.treeRuleIndex = RulePopup("Arbre", profile != null ? profile.trees : null, r => r.label, brush.treeRuleIndex);
                    brush.treesPerStroke = EditorGUILayout.IntSlider("Arbres par coup", brush.treesPerStroke, 1, 12);
                    break;
            }

            if (!brushEnabled) EditorGUILayout.HelpBox("Active le bouton « Pinceau Scene » en haut, puis clique-glisse sur le terrain. Alt = naviguer.", MessageType.Info);
        }

        // Ajout d'une texture personnelle directement depuis le pinceau : pas besoin de profil ni de peinture auto.
        private void DrawOwnTextureForm()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Ajouter ma propre texture au terrain", EditorStyles.boldLabel);
            newAlbedo = (Texture2D)EditorGUILayout.ObjectField("Albedo", newAlbedo, typeof(Texture2D), false);
            newNormal = (Texture2D)EditorGUILayout.ObjectField("Normale (optionnel)", newNormal, typeof(Texture2D), false);
            newTileSize = EditorGUILayout.FloatField("Taille de tuile (m)", newTileSize);
            using (new EditorGUI.DisabledScope(profile == null))
            {
                newAddToProfile = EditorGUILayout.ToggleLeft("Garder dans le profil (poids 0 : la peinture auto ne la retire pas du terrain)", newAddToProfile && profile != null);
            }
            using (new EditorGUI.DisabledScope(newAlbedo == null))
            {
                if (GUILayout.Button("Ajouter au terrain et la sélectionner")) AddOwnTexture();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private void AddOwnTexture()
        {
            TerrainLayer layer = LayerFactory.Create(newAlbedo, newNormal, null, newTileSize);
            int index = SplatOps.AddLayerToTerrain(targetTerrain, layer);
            if (index >= 0) brush.layerIndex = index;

            if (newAddToProfile && profile != null)
            {
                // Poids 0 et pas de variante : la règle sert uniquement à garder la couche sur le terrain lors d'une peinture auto.
                Undo.RecordObject(profile, "Ajouter couche");
                profile.layers.Add(new LayerRule { label = newAlbedo.name, layer = layer, weight = 0f, breakRepetition = false });
                EditorUtility.SetDirty(profile);
            }
            newAlbedo = null;
            newNormal = null;
        }

        private int LayerPopup(string label, int current)
        {
            TerrainLayer[] layers = targetTerrain.terrainData.terrainLayers;
            if (layers.Length == 0)
            {
                EditorGUILayout.HelpBox("Le terrain n'a pas de couche : ajoute ta texture dans le cadre ci-dessus, ou lance la peinture auto (onglet Textures).", MessageType.Warning);
                return 0;
            }
            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                names[i] = i + " : " + (layers[i] != null ? layers[i].name : "(vide)");
            return EditorGUILayout.Popup(label, Mathf.Clamp(current, 0, layers.Length - 1), names);
        }

        private static int RulePopup<T>(string label, List<T> rules, System.Func<T, string> name, int current)
        {
            if (rules == null || rules.Count == 0)
            {
                EditorGUILayout.HelpBox("Aucune règle dans le profil (onglet Végétation).", MessageType.Warning);
                return 0;
            }
            var names = new string[rules.Count];
            for (int i = 0; i < rules.Count; i++) names[i] = i + " : " + name(rules[i]);
            return EditorGUILayout.Popup(label, Mathf.Clamp(current, 0, rules.Count - 1), names);
        }
        #endregion
    }
}
