using System.Collections.Generic;
using Defense.Relief;
using UnityEditor;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Enregistre les batiments / props de la scene dans le GroundSnapRegistry avec leur offset
    /// d'origine (mesure sur le terrain de BACKUP) puis les repose sur le terrain courant.
    /// </summary>
    public static class DefenseReliefGroundTool
    {
        private const string RegistryObjectName = "GroundSnapRegistry";
        private const float SearchMargin = 60f;

        // Racines a ne jamais toucher (muraille : consigne) ou deja gerees par un autre outil.
        private static readonly HashSet<string> Excluded = new HashSet<string>
        {
            "Terrain", "muraille", "EnemyPath", "TowerSlots_Temporary", "Torch", "WaveSystem",
            "Directional Light", "Global Volume (Valheim)", "Adaptive Probe Volume", "ProbeVolumePerSceneData",
            "GameCanvas", "VillageCanvas", "EventSystem", "DefenseNavigation", "VillageSystems",
            "GroundSnapRegistry",
        };

        // Objets dont l'offset d'origine est connu avec certitude (audit avant sculpt).
        private static readonly Dictionary<string, float> KnownOffsets = new Dictionary<string, float>
        {
            { "Player", 0f },
            { "NPC_Capitaine", 0.95f },
            { "herse", 0.24f },
        };

        [MenuItem("Tools/Defense/Relief/3b - Enregistrer et reposer les objets")]
        public static void RegisterAndSnap()
        {
            DefenseReliefConfig config = DefenseReliefConfig.LoadOrCreate();
            ReliefFieldBase baseField = ReliefFieldBase.Create(config);
            GroundSnapRegistry registry = FindOrCreateRegistry();
            Terrain terrain = DefenseReliefScene.Terrain;

            Undo.RecordObject(registry, "Defense Relief : registry");
            RegisterRoots(config, baseField, registry);
            SnapAll(registry, terrain);
            EditorUtility.SetDirty(registry);
            DefenseReliefScene.MarkSceneDirty();
        }

        /// <summary>Relance uniquement le recalage (apres un nouveau sculpt).</summary>
        public static void SnapOnly()
        {
            GroundSnapRegistry registry = FindOrCreateRegistry();
            SnapAll(registry, DefenseReliefScene.Terrain);
            DefenseReliefScene.MarkSceneDirty();
        }

        private static GroundSnapRegistry FindOrCreateRegistry()
        {
            GameObject host = GameObject.Find(RegistryObjectName);
            if (host == null)
            {
                host = new GameObject(RegistryObjectName);
                Undo.RegisterCreatedObjectUndo(host, "Defense Relief : registry");
            }
            return host.GetComponent<GroundSnapRegistry>() ?? Undo.AddComponent<GroundSnapRegistry>(host);
        }

        private static void RegisterRoots(DefenseReliefConfig config, ReliefFieldBase baseField, GroundSnapRegistry registry)
        {
            var area = new Rect(-SearchMargin, -220f, 340f + SearchMargin, 370f);
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (Excluded.Contains(root.name)) continue;
                Transform t = root.transform;
                if (root.name != "VillageBuildings") RegisterRoot(t, baseField, registry);
                // Les batiments du village sont des enfants directs de VillageBuildings.
                if (root.name == "VillageBuildings")
                {
                    foreach (Transform child in t)
                    {
                        if (area.Contains(new Vector2(child.position.x, child.position.z))) RegisterRoot(child, baseField, registry);
                    }
                }
            }
        }

        private static void RegisterRoot(Transform t, ReliefFieldBase baseField, GroundSnapRegistry registry)
        {
            if (t.GetComponentInChildren<Renderer>() == null && t.GetComponentInChildren<Collider>() == null) return;
            if (registry.IsRegistered(t)) return;
            Vector3 p = t.position;
            float offset = KnownOffsets.TryGetValue(t.name, out float known) ? known : p.y - baseField.BaseHeight(p.x, p.z);
            registry.Register(t, offset);
        }

        private static void SnapAll(GroundSnapRegistry registry, Terrain terrain)
        {
            foreach (GroundSnapRegistry.Entry entry in registry.Entries)
            {
                if (entry.Target != null) Undo.RecordObject(entry.Target, "Defense Relief : snap");
            }
            int moved = registry.Snap(terrain);
            Debug.Log("[DefenseRelief] Objets reposes sur le sol : " + moved + " / " + registry.Entries.Count);
        }
    }
}
