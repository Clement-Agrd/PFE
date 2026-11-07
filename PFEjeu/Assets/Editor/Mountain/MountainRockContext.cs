using System.Collections.Generic;
using Defense.Relief.EditorTools;
using UnityEngine;
using UnityEngine.AI;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Contexte de placement : terrain, zonage, exclusions strictes (lanes, slots, spawns, couloir camera,
    /// zones habitees, plateau, NavMesh) et occupation. Aucune ecriture dans la scene.
    /// </summary>
    public sealed class MountainRockContext
    {
        public readonly ZoningMaps Maps;
        public readonly Terrain Terrain;
        public readonly MountainRockProfile Profile;
        public readonly Vector2 Herse;
        public readonly bool CheckNavMesh;

        private readonly List<Vector2> _lanePoints = new List<Vector2>();
        private readonly List<Vector2> _slots = new List<Vector2>();
        private readonly List<Vector2> _spawns = new List<Vector2>();
        private readonly List<KeyValuePair<Vector2, Vector2>> _corridors = new List<KeyValuePair<Vector2, Vector2>>();
        private readonly List<Vector3> _occupied = new List<Vector3>();
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private const float CellSize = 10f;

        public IReadOnlyList<Vector2> LanePoints => _lanePoints;
        public IReadOnlyList<Vector2> Slots => _slots;

        private readonly List<DefenseReliefConfig.EdgePassage> _passages = new List<DefenseReliefConfig.EdgePassage>();
        private const float PassageMargin = 3f;
        private const float PassageMaxAlong = 160f;

        public MountainRockContext(
            MountainRockProfile profile, ZoningMaps maps, Terrain terrain, ReliefField field, Vector2 herse,
            IEnumerable<Vector2> spawns, bool checkNavMesh, IEnumerable<DefenseReliefConfig.EdgePassage> passages = null)
        {
            if (passages != null) _passages.AddRange(passages);
            Profile = profile;
            Maps = maps;
            Terrain = terrain;
            Herse = herse;
            CheckNavMesh = checkNavMesh;
            _spawns.AddRange(spawns);
            foreach (ReliefField.SlotPlatform slot in field.Slots) _slots.Add(slot.position);
            foreach (ReliefField.LaneSample sample in field.Samples) _lanePoints.Add(sample.position);
            BuildCorridors(field);
        }

        #region Couloirs

        private void BuildCorridors(ReliefField field)
        {
            var bestDelta = new Dictionary<int, float>();
            var bestPoint = new Dictionary<int, Vector2>();
            foreach (ReliefField.LaneSample s in field.Samples)
            {
                float delta = Mathf.Abs(Vector2.Distance(s.position, Herse) - Profile.CorridorDistance);
                if (bestDelta.TryGetValue(s.route, out float d) && d <= delta) continue;
                bestDelta[s.route] = delta;
                bestPoint[s.route] = s.position;
            }
            foreach (KeyValuePair<int, Vector2> pair in bestPoint)
                _corridors.Add(new KeyValuePair<Vector2, Vector2>(Herse, pair.Value));
        }

        #endregion

        #region Terrain

        public bool InTerrain(Vector2 p)
        {
            Vector3 o = Terrain.transform.position;
            Vector3 size = Terrain.terrainData.size;
            return p.x > o.x + 2f && p.x < o.x + size.x - 2f && p.y > o.z + 2f && p.y < o.z + size.z - 2f;
        }

        public float GroundY(Vector2 p) =>
            Terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + Terrain.transform.position.y;

        public Vector3 Normal(Vector2 p)
        {
            Vector3 o = Terrain.transform.position;
            TerrainData d = Terrain.terrainData;
            return d.GetInterpolatedNormal((p.x - o.x) / d.size.x, (p.y - o.z) / d.size.z);
        }

        public int CellIndex(Vector2 p)
        {
            int i = Mathf.Clamp((int)((p.x - Maps.Origin.x) / Maps.CellX), 0, Maps.Res - 1);
            int j = Mathf.Clamp((int)((p.y - Maps.Origin.z) / Maps.CellZ), 0, Maps.Res - 1);
            return Maps.Index(i, j);
        }

        #endregion

        #region Exclusions

        /// <summary>Teste toutes les exclusions strictes. 'reason' vaut null si accepte.</summary>
        public bool TryAccept(Vector2 p, float radius, out string reason, bool hero = false)
        {
            reason = null;
            if (!InTerrain(p)) { reason = "hors terrain"; return false; }
            if (Vector2.Distance(p, Herse) > Profile.MaxRadiusFromHerse) { reason = "trop loin"; return false; }
            // Les rochers "hero" (belvedere, reperes) sont poses a la main : ni plateau ni NavMesh ne les excluent.
            if (!hero && Vector2.Distance(p, Herse) < Profile.PlateauKeepOut) { reason = "plateau"; return false; }

            int k = CellIndex(p);
            // Le masque de protection sert a la peinture : les hero respectent les distances reelles (lane, slot, spawn, couloir).
            if (!hero && Maps.Protect[k] >= Profile.ProtectThreshold) { reason = "zone protegee"; return false; }

            float laneMin = Maps.RelH[k] < 10f ? Profile.PlainLaneEdgeMin : Profile.LaneEdgeMin;
            if (Maps.LaneOutside[k] < laneMin + radius * 0.6f) { reason = "lane"; return false; }

            float slotLimit = Profile.SlotMin + radius * 0.5f;
            foreach (Vector2 slot in _slots)
            {
                if ((slot - p).sqrMagnitude < slotLimit * slotLimit) { reason = "slot"; return false; }
            }
            foreach (Vector2 spawn in _spawns)
            {
                if ((spawn - p).sqrMagnitude < Profile.SpawnKeepOut * Profile.SpawnKeepOut) { reason = "spawn"; return false; }
            }
            foreach (KeyValuePair<Vector2, Vector2> corridor in _corridors)
            {
                if (DistanceToSegment(p, corridor.Key, corridor.Value) < Profile.CorridorHalfWidth + radius * 0.5f)
                {
                    reason = "couloir camera";
                    return false;
                }
            }
            if (OnPassage(p, radius)) { reason = "passage"; return false; }
            if (IsOccupied(p, radius)) { reason = "chevauchement"; return false; }

            if (CheckNavMesh && !hero)
            {
                float reach = Mathf.Min(radius * 0.6f + Profile.NavMeshPadding, 4f);
                var probe = new Vector3(p.x, GroundY(p), p.y);
                if (NavMesh.SamplePosition(probe, out _, reach, NavMesh.AllAreas)) { reason = "NavMesh"; return false; }
            }
            return true;
        }

        /// <summary>Rampes plateau -> vallee : jamais de rocher dessus (axe herse -> centre du passage).</summary>
        private bool OnPassage(Vector2 p, float radius)
        {
            foreach (DefenseReliefConfig.EdgePassage passage in _passages)
            {
                Vector2 axis = passage.center - Herse;
                if (axis.sqrMagnitude < 1e-4f) continue;
                axis.Normalize();
                Vector2 v = p - Herse;
                float along = Vector2.Dot(v, axis);
                float lateral = Mathf.Abs(v.x * axis.y - v.y * axis.x);
                if (along > 0f && along < PassageMaxAlong && lateral < passage.halfWidth + PassageMargin + radius * 0.5f) return true;
            }
            return false;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        #endregion

        #region Occupation

        public void Occupy(Vector2 p, float radius)
        {
            int index = _occupied.Count;
            _occupied.Add(new Vector3(p.x, p.y, radius));
            long key = Key(p);
            if (!_cells.TryGetValue(key, out List<int> list)) _cells[key] = list = new List<int>();
            list.Add(index);
        }

        private bool IsOccupied(Vector2 p, float radius)
        {
            int cx = Mathf.FloorToInt(p.x / CellSize);
            int cz = Mathf.FloorToInt(p.y / CellSize);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cz + dz), out List<int> list)) continue;
                    foreach (int i in list)
                    {
                        Vector3 o = _occupied[i];
                        float limit = o.z + radius + Profile.OverlapPadding;
                        if ((new Vector2(o.x, o.y) - p).sqrMagnitude < limit * limit) return true;
                    }
                }
            }
            return false;
        }

        public int OccupiedCount => _occupied.Count;

        /// <summary>Retire les dernieres occupations pour ne garder que 'count' entrees.</summary>
        public void TruncateOccupancy(int count)
        {
            if (count >= _occupied.Count) return;
            _occupied.RemoveRange(Mathf.Max(0, count), _occupied.Count - Mathf.Max(0, count));
            _cells.Clear();
            for (int i = 0; i < _occupied.Count; i++)
            {
                long key = Pack(Mathf.FloorToInt(_occupied[i].x / CellSize), Mathf.FloorToInt(_occupied[i].y / CellSize));
                if (!_cells.TryGetValue(key, out List<int> list)) _cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        private static long Key(Vector2 p) => Pack(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;

        #endregion
    }
}
