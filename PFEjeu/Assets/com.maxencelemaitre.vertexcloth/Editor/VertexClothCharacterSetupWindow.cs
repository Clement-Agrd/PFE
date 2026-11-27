using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VertexCloth.EditorTools
{
    /// <summary>
    /// Setup en un clic d'un personnage : détecte les éléments souples, génère les poids depuis un preset,
    /// bake, ajoute les simulateurs et crée les colliders Humanoid.
    /// </summary>
    public sealed class VertexClothCharacterSetupWindow : EditorWindow
    {
        [System.Serializable]
        private sealed class Entry
        {
            public Renderer renderer;
            public bool enabled;
            public VertexClothPreset preset;
        }

        private static readonly string[] ClothKeywords =
        {
            "robe", "cape", "cloak", "skirt", "coat", "scarf", "hat", "hair", "flag", "banner",
            "dress", "tail", "ribbon", "cloth", "sash", "hood", "jupe", "cheveux", "drapeau",
            "echarpe", "écharpe", "chapeau", "manteau", "capuche", "queue"
        };

        [SerializeField] private GameObject character;
        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private VertexClothPreset defaultPreset;
        [SerializeField] private string outputFolder = "Assets/VertexCloth/Data";
        [SerializeField] private float colliderRadiusScale = 1f;
        [SerializeField] private bool includeHeadCollider;

        private Vector2 scroll;

        [MenuItem("Tools/Vertex Cloth/Character Setup")]
        private static void Open()
        {
            VertexClothCharacterSetupWindow window = GetWindow<VertexClothCharacterSetupWindow>("Vertex Cloth Setup");
            window.minSize = new Vector2(380f, 420f);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawCharacterSection();

            if (character != null)
            {
                DrawEntriesSection();
                DrawOptionsSection();
                DrawActionsSection();
            }

            EditorGUILayout.EndScrollView();
        }

        #region Sections GUI
        private void DrawCharacterSection()
        {
            EditorGUILayout.LabelField("Personnage", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            character = (GameObject)EditorGUILayout.ObjectField("Racine du personnage", character, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
                Analyze();

            if (GUILayout.Button("Utiliser la sélection"))
            {
                character = Selection.activeGameObject;
                Analyze();
            }

            if (character == null)
            {
                EditorGUILayout.HelpBox("Sélectionne la racine du personnage (celle qui porte l'Animator).", MessageType.Info);
                return;
            }

            Animator animator = character.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                EditorGUILayout.HelpBox(
                    "Pas d'Animator Humanoid détecté : les colliders automatiques ne seront pas créés (le reste fonctionne).",
                    MessageType.Warning);
            }
        }

        private void DrawEntriesSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Éléments souples (cocher ce qui doit être simulé)", EditorStyles.boldLabel);

            if (GUILayout.Button("Réanalyser"))
                Analyze();

            foreach (Entry entry in entries)
            {
                if (entry.renderer == null)
                    continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.enabled = EditorGUILayout.ToggleLeft(
                        entry.renderer.name + (entry.renderer is SkinnedMeshRenderer ? "  [Skinned]" : "  [Static]"),
                        entry.enabled, GUILayout.MinWidth(160f));
                    entry.preset = (VertexClothPreset)EditorGUILayout.ObjectField(entry.preset, typeof(VertexClothPreset), false);
                }
            }

            if (entries.Count == 0)
                EditorGUILayout.HelpBox("Aucun mesh trouvé sous cet objet.", MessageType.Info);
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            defaultPreset = (VertexClothPreset)EditorGUILayout.ObjectField("Preset par défaut", defaultPreset, typeof(VertexClothPreset), false);
            if (GUILayout.Button("Créer les presets par défaut (Robe, Cape, Hair, Banner)"))
            {
                VertexClothDefaultPresets.CreateAll();
                if (defaultPreset == null)
                    defaultPreset = VertexClothDefaultPresets.LoadOrCreate("Robe");
            }

            outputFolder = EditorGUILayout.TextField("Dossier de sortie", outputFolder);
            colliderRadiusScale = EditorGUILayout.Slider("Échelle des rayons colliders", colliderRadiusScale, 0.3f, 2f);
            includeHeadCollider = EditorGUILayout.Toggle("Collider de tête", includeHeadCollider);
        }

        private void DrawActionsSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Auto Setup (éléments cochés + colliders)", GUILayout.Height(30f)))
            {
                RunAutoSetup();
                CreateColliders();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Colliders Humanoid seulement"))
                    CreateColliders();
                if (GUILayout.Button("Créer / assigner le vent"))
                    CreateAndAssignWind();
            }

            if (GUILayout.Button("Retirer toute la simulation du personnage"))
                RemoveAll();
        }
        #endregion

        #region Actions
        private void Analyze()
        {
            entries.Clear();
            if (character == null)
                return;

            foreach (Renderer candidate in character.GetComponentsInChildren<Renderer>(true))
            {
                if (!VertexClothEditorUtility.IsSupported(candidate))
                    continue;

                bool likely = LooksLikeCloth(candidate.name) || candidate.GetComponent<VertexClothSimulator>() != null;
                entries.Add(new Entry { renderer = candidate, enabled = likely });
            }
        }

        private static bool LooksLikeCloth(string objectName)
        {
            string lower = objectName.ToLowerInvariant();
            foreach (string keyword in ClothKeywords)
            {
                if (lower.Contains(keyword))
                    return true;
            }

            return false;
        }

        private void RunAutoSetup()
        {
            VertexClothPreset fallback = defaultPreset != null ? defaultPreset : VertexClothDefaultPresets.LoadOrCreate("Robe");
            VertexClothEditorUtility.EnsureFolder(outputFolder);

            int done = 0;
            int total = 0;
            foreach (Entry entry in entries)
            {
                if (entry.enabled && entry.renderer != null)
                    total++;
            }

            try
            {
                foreach (Entry entry in entries)
                {
                    if (!entry.enabled || entry.renderer == null)
                        continue;

                    EditorUtility.DisplayProgressBar("Vertex Cloth", entry.renderer.name, total > 0 ? (float)done / total : 1f);
                    SetupEntry(entry, entry.preset != null ? entry.preset : fallback);
                    done++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[VertexCloth] Auto Setup terminé : {done} élément(s) configuré(s).");
        }

        private void SetupEntry(Entry entry, VertexClothPreset preset)
        {
            Renderer renderer = entry.renderer;
            Mesh mesh = VertexClothEditorUtility.GetMesh(renderer);

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[VertexCloth] '{mesh.name}' ignoré : active Read/Write dans l'import du modèle.", renderer);
                return;
            }

            ClothTopology topology = VertexClothTopologyBuilder.Build(mesh);
            float[] freedom = VertexClothAutoWeights.Compute(topology, mesh, preset);

            VertexClothSimulator existing = renderer.GetComponent<VertexClothSimulator>();
            VertexClothData previousData = existing != null ? existing.Data : null;

            string path = outputFolder + "/" + character.name + "_" + renderer.name + "_ClothData.asset";
            VertexClothData data = VertexClothEditorUtility.Bake(mesh, topology, freedom, previousData, path);

            VertexClothSettings settings = VertexClothEditorUtility.EnsureSettings(preset.settings, outputFolder);
            VertexClothEditorUtility.AttachSimulator(renderer, data, settings);
        }

        private void CreateColliders()
        {
            if (character == null)
                return;

            Animator animator = character.GetComponentInChildren<Animator>();
            int created = VertexClothColliderBuilder.AddHumanoidColliders(animator, colliderRadiusScale, includeHeadCollider);

            if (created == 0)
            {
                Debug.LogWarning("[VertexCloth] Aucun collider créé (Animator Humanoid requis).", character);
                return;
            }

            foreach (VertexClothSimulator simulator in character.GetComponentsInChildren<VertexClothSimulator>(true))
            {
                Undo.RecordObject(simulator, "Collect Cloth Colliders");
                simulator.CollectColliders();
                EditorUtility.SetDirty(simulator);
            }

            Debug.Log($"[VertexCloth] {created} collider(s) Humanoid créé(s). Ajuste-les avec leurs gizmos (cyan).", character);
        }

        private void CreateAndAssignWind()
        {
            WindZone zone = Object.FindFirstObjectByType<WindZone>();
            if (zone == null)
            {
                var go = new GameObject("Wind");
                Undo.RegisterCreatedObjectUndo(go, "Create Wind");
                zone = go.AddComponent<WindZone>();
                zone.mode = WindZoneMode.Directional;
                zone.windMain = 1f;
                zone.windPulseMagnitude = 0.3f;
                zone.windPulseFrequency = 0.2f;
            }

            foreach (VertexClothSimulator simulator in character.GetComponentsInChildren<VertexClothSimulator>(true))
            {
                var serialized = new SerializedObject(simulator);
                serialized.FindProperty("windZone").objectReferenceValue = zone;
                serialized.ApplyModifiedProperties();
            }

            Selection.activeGameObject = zone.gameObject;
        }

        private void RemoveAll()
        {
            if (character == null)
                return;

            if (!EditorUtility.DisplayDialog("Vertex Cloth", "Retirer les simulateurs et les colliders générés de ce personnage ?", "Retirer", "Annuler"))
                return;

            foreach (VertexClothSimulator simulator in character.GetComponentsInChildren<VertexClothSimulator>(true))
                Undo.DestroyObjectImmediate(simulator);

            VertexClothColliderBuilder.RemoveAutoColliders(character);
        }
        #endregion
    }
}
