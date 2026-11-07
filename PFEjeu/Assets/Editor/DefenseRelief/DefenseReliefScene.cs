using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Acces centralise aux objets de la scene utilises par les outils de relief.
    /// Les recherches par nom ne se font jamais dans une boucle chaude.
    /// </summary>
    public static class DefenseReliefScene
    {
        public const string EnemyPathName = "EnemyPath";
        public const string SlotsRootName = "TowerSlots_Temporary";
        public const string TorchRootName = "Torch";
        public const string HerseName = "herse";
        public const string BarracksName = "Prefabs_Barracks";
        public const string SlotPrefix = "TowerSlot_";
        public const string TorchPrefix = "Torch_Realistic";
        public const string SpawnPrefix = "Spawn_Route_";

        public static Terrain Terrain => Terrain.activeTerrain;

        public static Vector2 HersePosition => PlanarPosition(Require(HerseName));
        public static Vector2 BarracksPosition => PlanarPosition(Require(BarracksName));

        public static Vector2 PlanarPosition(GameObject go) =>
            new Vector2(go.transform.position.x, go.transform.position.z);

        public static GameObject Require(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null) throw new System.InvalidOperationException("Objet introuvable dans la scene : " + objectName);
            return go;
        }

        public static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        public static SplineContainer FindRouteSpline(string splineName)
        {
            Transform root = Require(EnemyPathName).transform;
            Transform child = root.Find(splineName);
            if (child == null) throw new System.InvalidOperationException("Spline introuvable : " + splineName);
            return child.GetComponent<SplineContainer>();
        }

        public static List<Transform> ExistingSlots()
        {
            var result = new List<Transform>();
            Transform root = Require(SlotsRootName).transform;
            foreach (Transform child in root)
            {
                if (child.name.StartsWith(SlotPrefix)) result.Add(child);
            }
            return result;
        }

        public static List<Transform> Torches()
        {
            var result = new List<Transform>();
            Transform root = Require(TorchRootName).transform;
            foreach (Transform child in root)
            {
                if (child.name.StartsWith(TorchPrefix)) result.Add(child);
            }
            return result;
        }

        public static List<Transform> SpawnZones()
        {
            var result = new List<Transform>();
            Transform root = Require("WaveSystem").transform;
            foreach (Transform child in root)
            {
                if (child.name.StartsWith(SpawnPrefix)) result.Add(child);
            }
            return result;
        }

        /// <summary>Positions XZ des slots existants (hors nouveaux) plus ceux de la config.</summary>
        public static List<Vector2> AllSlotPositions(DefenseReliefConfig config)
        {
            var result = new List<Vector2>();
            var seen = new HashSet<string>();
            foreach (Transform slot in ExistingSlots())
            {
                result.Add(new Vector2(slot.position.x, slot.position.z));
                seen.Add(slot.name);
            }
            foreach (DefenseReliefConfig.NewSlot entry in config.NewSlots)
            {
                if (!seen.Contains(entry.slotName)) result.Add(entry.position);
            }
            return result;
        }

        public static ReliefField BuildField(DefenseReliefConfig config)
        {
            if (config.BaseTerrainData == null)
                throw new System.InvalidOperationException("DefenseReliefConfig.baseTerrainData est vide (backup du TerrainData).");
            if (!config.HasCapturedRoutes)
                throw new System.InvalidOperationException("Routes non capturees : lancer Tools/Defense/Relief/1 - Capture.");

            return new ReliefField(
                config,
                Terrain.transform.position,
                HersePosition,
                BarracksPosition,
                AllSlotPositions(config),
                WallAnchors());
        }

        /// <summary>Points XZ le long des splines de la muraille (lecture seule, la muraille n'est jamais modifiee).</summary>
        public static List<Vector2> WallAnchors()
        {
            const float spacing = 4f;
            var anchors = new List<Vector2>();
            GameObject wall = GameObject.Find("muraille");
            if (wall == null) return anchors;

            foreach (SplineContainer container in wall.GetComponentsInChildren<SplineContainer>())
            {
                int count = Mathf.Max(2, Mathf.CeilToInt(container.CalculateLength() / spacing));
                for (int i = 0; i <= count; i++)
                {
                    Vector3 p = container.EvaluatePosition(i / (float)count);
                    anchors.Add(new Vector2(p.x, p.z));
                }
            }
            return anchors;
        }

        /// <summary>Hauteur actuelle du terrain (Y monde).</summary>
        public static float TerrainY(float worldX, float worldZ)
        {
            Terrain t = Terrain;
            return t.SampleHeight(new Vector3(worldX, 0f, worldZ)) + t.transform.position.y;
        }

        public static void MarkSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
