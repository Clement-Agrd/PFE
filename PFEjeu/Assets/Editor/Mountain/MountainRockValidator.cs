using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Defense.Mountain.EditorTools
{
    /// <summary>Validation des rochers : lanes, slots, NavMesh, enterre/flottant, budget. Lecture seule.</summary>
    public static class MountainRockValidator
    {
        private const float LaneCenterMin = 4f;
        private const float SlotMin = 6f;
        private const float FloatingMax = 0.3f;
        private const float BuriedMaxFraction = 0.5f;
        private const float NavProbe = 0.3f;
        private const int MaxListed = 6;

        public static string Report(MountainRockSession s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Mountain P3] Validation des rochers");
            Section(sb, s, s.RockProfile.RootName, "generes (Environment_Rocks)", s.RockProfile.RespectNavMesh);
            Section(sb, s, s.RockProfile.HeroRootName, "hero (reperes + belvedere, exemptes NavMesh/plateau)", false);
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, MountainRockSession s, string rootName, string label, bool strictNavMesh)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) { sb.AppendLine("- " + label + " : racine absente"); return; }

            int total = 0, nearLane = 0, nearSlot = 0, onNav = 0, floating = 0, buried = 0, triangles = 0, renderers = 0;
            float burialSum = 0f;
            var issues = new List<string>();

            foreach (Transform group in root.transform)
            {
                foreach (Transform rock in group)
                {
                    total++;
                    var renderersLod0 = Lod0Renderers(rock);
                    if (renderersLod0.Count == 0) continue;
                    Bounds b = renderersLod0[0].bounds;
                    foreach (Renderer r in renderersLod0) b.Encapsulate(r.bounds);
                    renderers += renderersLod0.Count;
                    triangles += CountTriangles(renderersLod0);

                    Vector2 c = new Vector2(b.center.x, b.center.z);
                    float laneDistance = MinDistance(c, s.Context.LanePoints);
                    float slotDistance = MinDistance(c, s.Context.Slots);
                    if (laneDistance < LaneCenterMin) { nearLane++; Note(issues, rock.name + " lane " + laneDistance.ToString("F1") + " m"); }
                    if (slotDistance < SlotMin) { nearSlot++; Note(issues, rock.name + " slot " + slotDistance.ToString("F1") + " m"); }

                    float ground = s.Context.GroundY(c);
                    if (NavMesh.SamplePosition(new Vector3(c.x, ground, c.y), out _, NavProbe, NavMesh.AllAreas))
                    {
                        onNav++;
                        Note(issues, rock.name + " sur NavMesh");
                    }

                    LowestAndHighestGround(s, c, Mathf.Max(b.extents.x, b.extents.z) * 0.7f, out float low, out _);
                    if (b.min.y > low + FloatingMax) { floating++; Note(issues, rock.name + " flottant " + (b.min.y - low).ToString("F2") + " m"); }
                    float fraction = Mathf.Clamp01((ground - b.min.y) / Mathf.Max(0.01f, b.size.y));
                    burialSum += fraction;
                    if (fraction > BuriedMaxFraction) { buried++; Note(issues, rock.name + " enterre " + (100f * fraction).ToString("F0") + "%"); }
                }
            }

            sb.AppendLine("- " + label + " : " + total + " rochers | LOD0 : " + triangles + " triangles, " + renderers + " renderers");
            sb.AppendLine("    a < " + LaneCenterMin + " m du centre d'une lane : " + nearLane + " | a < " + SlotMin + " m d'un slot : " + nearSlot +
                          " | sur NavMesh : " + onNav + (strictNavMesh ? "" : " (exempte)") + " | flottants > " + FloatingMax + " m : " + floating +
                          " | enfoncement moyen " + (total > 0 ? (100f * burialSum / total).ToString("F0") : "0") + "% (> " + (100 * BuriedMaxFraction) + "% : " + buried + ")");
            foreach (string issue in issues) sb.AppendLine("    ! " + issue);
        }

        private static void Note(List<string> issues, string text)
        {
            if (issues.Count < MaxListed) issues.Add(text);
        }

        private static List<Renderer> Lod0Renderers(Transform rock)
        {
            var list = new List<Renderer>();
            var group = rock.GetComponentInChildren<LODGroup>();
            if (group != null && group.lodCount > 0)
            {
                foreach (Renderer r in group.GetLODs()[0].renderers) if (r != null) list.Add(r);
            }
            else
            {
                list.AddRange(rock.GetComponentsInChildren<Renderer>());
            }
            return list;
        }

        private static int CountTriangles(List<Renderer> renderers)
        {
            int count = 0;
            foreach (Renderer r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) count += filter.sharedMesh.triangles.Length / 3;
            }
            return count;
        }

        private static float MinDistance(Vector2 p, IReadOnlyList<Vector2> points)
        {
            float best = float.MaxValue;
            foreach (Vector2 q in points) best = Mathf.Min(best, (q - p).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        private static void LowestAndHighestGround(MountainRockSession s, Vector2 c, float radius, out float low, out float high)
        {
            low = s.Context.GroundY(c);
            high = low;
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                float y = s.Context.GroundY(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
        }
    }
}
