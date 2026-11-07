using System.Collections.Generic;
using Defense.Relief.EditorTools;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>Prepare tout ce qu'il faut pour placer ou valider des rochers (terrain, zonage, catalogue, exclusions).</summary>
    public sealed class MountainRockSession
    {
        public MountainRockProfile RockProfile;
        public MountainProfile MountainProfile;
        public DefenseReliefConfig ReliefConfig;
        public Terrain Terrain;
        public ReliefField Field;
        public ZoningMaps Maps;
        public MountainRockCatalog Catalog;
        public MountainRockContext Context;
        public Vector2 Herse;

        private const string SpawnPrefix = "Spawn_Route_";
        private const string WaveSystemName = "WaveSystem";

        public static MountainRockSession Create(bool checkNavMesh)
        {
            var s = new MountainRockSession
            {
                RockProfile = MountainRockProfile.LoadOrCreate(),
                MountainProfile = MountainProfile.LoadOrCreate(),
                ReliefConfig = DefenseReliefConfig.LoadOrCreate(),
                Terrain = DefenseReliefScene.Terrain,
            };
            s.Herse = DefenseReliefScene.HersePosition;
            s.Field = DefenseReliefScene.BuildField(s.ReliefConfig);
            s.Catalog = MountainRockCatalog.Build(s.RockProfile);

            TerrainData data = s.Terrain.terrainData;
            s.Maps = new ZoningMaps(data, s.Terrain.transform.position);
            MountainProtectionBuilder.Fill(s.Maps, s.MountainProfile, s.Field, s.Herse, false);
            MountainZoningBuilder.Fill(s.Maps, s.MountainProfile, data, s.Herse);

            s.Context = new MountainRockContext(
                s.RockProfile, s.Maps, s.Terrain, s.Field, s.Herse, FindSpawns(),
                checkNavMesh && s.RockProfile.RespectNavMesh, s.ReliefConfig.Inverted.passages);
            return s;
        }

        /// <summary>Les rochers hero existants occupent leur emplacement : la generation les evite.</summary>
        public void OccupyHeroRocks()
        {
            GameObject hero = GameObject.Find(RockProfile.HeroRootName);
            if (hero == null) return;
            foreach (Transform group in hero.transform)
            {
                foreach (Transform rock in group)
                {
                    float radius = 3f;
                    GameObject source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(rock.gameObject);
                    RockAsset asset = source != null ? Catalog.Find(source.name) : null;
                    if (asset != null) radius = asset.Radius * rock.localScale.x / Mathf.Max(0.001f, asset.Prefab.transform.localScale.x);
                    Context.Occupy(new Vector2(rock.position.x, rock.position.z), radius);
                }
            }
        }

        private static List<Vector2> FindSpawns()
        {
            var spawns = new List<Vector2>();
            GameObject wave = GameObject.Find(WaveSystemName);
            if (wave == null) return spawns;
            foreach (Transform child in wave.transform)
            {
                if (child.name.StartsWith(SpawnPrefix)) spawns.Add(new Vector2(child.position.x, child.position.z));
            }
            return spawns;
        }
    }
}
