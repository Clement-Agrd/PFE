using System;
using System.Collections.Generic;
using System.Text;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// P3 : rochers sur flancs, plaine (bords) et reperes. Generation par regles et graine fixe sous
    /// "Environment_Rocks" (regenerable). Les rochers hero sont sur une racine separee, jamais supprimee ici.
    /// </summary>
    public static class MountainP3Menu
    {
        [MenuItem("Tools/Defense/Mountain/P3 - Generer les rochers (Environment_Rocks)")]
        public static void Generate() => Guard(() =>
        {
            MountainRockSession s = MountainRockSession.Create(true);
            s.OccupyHeroRocks();

            EditorUtility.DisplayProgressBar("Mountain P3", "Planification des grappes", 0.3f);
            List<RockPlacement> plan = MountainRockPlanner.Plan(s.RockProfile, s.Maps, s.Context, s.Catalog);

            EditorUtility.DisplayProgressBar("Mountain P3", "Instanciation", 0.7f);
            Undo.IncrementCurrentGroup();
            MountainRockSpawner.Spawn(s.RockProfile.RootName, plan, out MountainRockSpawner.Stats stats);
            Undo.SetCurrentGroupName("Mountain P3 : rochers");
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log(Describe(stats));
        });

        [MenuItem("Tools/Defense/Mountain/P3 - Supprimer Environment_Rocks")]
        public static void Clear() => Guard(() =>
        {
            MountainRockSpawner.DestroyRoot(MountainRockProfile.LoadOrCreate().RootName);
            DefenseReliefScene.MarkSceneDirty();
            Debug.Log("[Mountain P3] Environment_Rocks supprime (la racine hero n'est pas touchee).");
        });

        [MenuItem("Tools/Defense/Mountain/P3 - Creer reperes A/B et belvedere (hero)")]
        public static void CreateHero() => Guard(() =>
        {
            MountainRockSession s = MountainRockSession.Create(true);
            MountainRockProfile rp = s.RockProfile;
            var log = new StringBuilder("[Mountain P3] Rochers hero :");

            for (int i = 0; i < rp.Landmarks.Length; i++)
            {
                List<RockPlacement> plan = MountainRockHero.PlanLandmark(rp, s.Context, s.Catalog, i);
                bool created = plan.Count > 0 && MountainRockSpawner.SpawnHeroGroup(rp.HeroRootName, rp.Landmarks[i].label, plan);
                log.Append("\n  ").Append(rp.Landmarks[i].label).Append(" : ").Append(created ? plan.Count + " rochers" : "conserve ou impossible");
            }

            float plateauRadius = s.ReliefConfig.Inverted.plateauRadius;
            List<RockPlacement> belvedere = MountainRockHero.PlanBelvedere(rp, s.MountainProfile, s.Context, s.Catalog, plateauRadius);
            bool belvedereCreated = belvedere.Count > 0 && MountainRockSpawner.SpawnHeroGroup(rp.HeroRootName, "Belvedere", belvedere);
            log.Append("\n  Belvedere : ").Append(belvedereCreated ? belvedere.Count + " rochers" : "conserve ou impossible");

            DefenseReliefScene.MarkSceneDirty();
            Debug.Log(log.ToString());
        });

        [MenuItem("Tools/Defense/Mountain/P3 - Rapport de validation")]
        public static void Validate() => Guard(() =>
        {
            MountainRockSession s = MountainRockSession.Create(true);
            Debug.Log(MountainRockValidator.Report(s));
        });

        private static void Guard(Action action)
        {
            try
            {
                // Materiaux froids : copies creees une fois, assignees aux instances de scene.
                MountainRockProfile rp = MountainRockProfile.LoadOrCreate();
                MountainRockSpawner.ColdMaterials = rp.UseColdMaterials ? MountainRockMaterials.Build(rp, MountainRockCatalog.Build(rp)) : null;
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Mountain P3] interrompu : " + exception.Message + "\n" + exception.StackTrace);
            }
            finally
            {
                MountainRockSpawner.ColdMaterials = null;
                EditorUtility.ClearProgressBar();
            }
        }

        private static string Describe(MountainRockSpawner.Stats stats)
        {
            var sb = new StringBuilder("[Mountain P3] Rochers generes : " + stats.count + " | LOD0 : " + stats.triangles + " triangles, " + stats.renderers + " renderers\n");
            foreach (KeyValuePair<RockZone, int> pair in stats.perZone) sb.Append("  zone ").Append(pair.Key).Append(" : ").Append(pair.Value).Append('\n');
            foreach (KeyValuePair<RockSize, int> pair in stats.perSize) sb.Append("  taille ").Append(pair.Key).Append(" : ").Append(pair.Value).Append('\n');
            return sb.ToString();
        }
    }
}
