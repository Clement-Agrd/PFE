using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Rochers "hero" : reperes aux jonctions A et B, belvedere au bord de la falaise.
    /// Tout est calcule a partir des lanes reelles (pas de position codee en dur au-dela de l'indication du profil).
    /// </summary>
    public static class MountainRockHero
    {
        private const float HeroBurial = 0.25f;
        private const float SearchStep = 2f;
        private const float MaxOffset = 50f;
        private const float CrestRadiusMargin = 0.8f;
        private const float CliffProbeLength = 6f;
        private const float GuardSetback = 2.5f;
        private const float KitLandmarkScale = 1.5f;
        private const float RuinPieceRadius = 8f;

        public static List<RockPlacement> PlanLandmark(MountainRockProfile rp, MountainRockContext ctx, MountainRockCatalog catalog, int index)
        {
            MountainRockProfile.Landmark landmark = rp.Landmarks[index];
            var rng = new System.Random(rp.Seed + 101 + index);
            var result = new List<RockPlacement>();

            // Point valide (toutes exclusions de distance) le plus proche de la position indiquee.
            Vector2 best = default;
            float bestDistance = float.MaxValue;
            bool found = false;
            for (float dz = -MaxOffset; dz <= MaxOffset; dz += SearchStep)
            {
                for (float dx = -MaxOffset; dx <= MaxOffset; dx += SearchStep)
                {
                    var offset = new Vector2(dx, dz);
                    if (offset.magnitude > MaxOffset) continue;
                    Vector2 p = landmark.position + offset;
                    if (offset.magnitude >= bestDistance || Vector2.Distance(p, ctx.Herse) < rp.PlateauKeepOut || !ctx.TryAccept(p, 6f, out _, true)) continue;
                    bestDistance = offset.magnitude;
                    best = p;
                    found = true;
                }
            }
            if (!found)
            {
                Debug.LogWarning("[Mountain] Repere " + landmark.label + " : aucun emplacement valide pres de " + landmark.position);
                return result;
            }

            if (index == 0) PlanBoulder(rp, ctx, catalog, rng, best, result);
            else PlanFormation(rp, ctx, catalog, rng, best, result);
            return result;
        }

        private static void PlanBoulder(MountainRockProfile rp, MountainRockContext ctx, MountainRockCatalog catalog, System.Random rng, Vector2 center, List<RockPlacement> output)
        {
            // Rocher repere unique du kit (jonction A) s'il existe.
            RockAsset landmark = FindLandmark(catalog, "RockLandmark_JunctionA");
            if (landmark != null)
            {
                output.Add(MountainRockPlanner.Pose(ctx, landmark, center, Yaw(rng), KitLandmarkScale, HeroBurial, 0.6f, RockZone.Falaise, 0));
                return;
            }
            RockAsset anchor = catalog.Pick(RockSize.Cliff, rng);
            output.Add(MountainRockPlanner.Pose(ctx, anchor, center, Yaw(rng), 1.1f, HeroBurial, rp.AlignToNormal, RockZone.Falaise, 0));
            for (int i = 0; i < 2; i++)
            {
                float angle = (i * 2.4f + (float)rng.NextDouble()) * Mathf.PI;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (anchor.Radius * 1.3f);
                RockAsset satellite = catalog.Pick(RockSize.Big, rng);
                if (ctx.TryAccept(p, satellite.Radius, out _, true))
                    output.Add(MountainRockPlanner.Pose(ctx, satellite, p, Yaw(rng), 1f, HeroBurial, rp.AlignToNormal, RockZone.Falaise, 0));
            }
        }

        /// <summary>Aucune ruine dans le projet : formation unique de 3 falaises distinctes + 2 rochers moyens.</summary>
        private static void PlanFormation(MountainRockProfile rp, MountainRockContext ctx, MountainRockCatalog catalog, System.Random rng, Vector2 center, List<RockPlacement> output)
        {
            if (PlanRuin(ctx, catalog, rng, center, output)) return;
            IReadOnlyList<RockAsset> cliffs = catalog.Get(RockSize.Cliff);
            for (int i = 0; i < 3; i++)
            {
                RockAsset asset = cliffs[(i * 2) % cliffs.Count];
                float angle = i * Mathf.PI * 2f / 3f + 0.4f;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.5f;
                output.Add(MountainRockPlanner.Pose(ctx, asset, p, Yaw(rng), 0.9f + 0.15f * i, HeroBurial, rp.AlignToNormal, RockZone.Falaise, 1));
            }
            for (int i = 0; i < 2; i++)
            {
                RockAsset asset = catalog.Pick(RockSize.Medium, rng);
                float angle = i * Mathf.PI + 1.1f;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 8.5f;
                if (ctx.TryAccept(p, asset.Radius, out _, true))
                    output.Add(MountainRockPlanner.Pose(ctx, asset, p, Yaw(rng), 1f, HeroBurial, rp.AlignToNormal, RockZone.Falaise, 1));
            }
        }

        #region Ruine (kit)

        private static readonly string[] RuinPieces = { "Ruin_Pillar", "Ruin_Wall", "Ruin_Arch", "Ruin_Pillar" };

        /// <summary>Ruine du kit : bloc principal au centre, pieces modulaires autour. Faux si le kit est absent.</summary>
        private static bool PlanRuin(MountainRockContext ctx, MountainRockCatalog catalog, System.Random rng, Vector2 center, List<RockPlacement> output)
        {
            RockAsset main = FindLandmark(catalog, "Ruin_Main");
            if (main == null) return false;
            output.Add(MountainRockPlanner.Pose(ctx, main, center, Yaw(rng), 1f, HeroBurial, 0.4f, RockZone.Falaise, 1));
            for (int i = 0; i < RuinPieces.Length; i++)
            {
                RockAsset piece = FindLandmark(catalog, RuinPieces[i]);
                if (piece == null) continue;
                float angle = i * Mathf.PI * 0.5f + 0.6f;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RuinPieceRadius;
                if (ctx.TryAccept(p, piece.Radius, out _, true))
                    output.Add(MountainRockPlanner.Pose(ctx, piece, p, Yaw(rng), 1f, HeroBurial, 0.4f, RockZone.Falaise, 1));
            }
            return true;
        }

        private static RockAsset FindLandmark(MountainRockCatalog catalog, string name)
        {
            RockAsset asset = catalog.Find(name);
            return asset != null && asset.Size == RockSize.Landmark ? asset : null;
        }

        #endregion

        #region Belvedere

        /// <summary>
        /// Belvedere : point de la crete de falaise (pente >= seuil) le plus proche de l'azimut cible (vue sur les lanes),
        /// flanque de rochers en garde-corps naturel ; l'espace central reste ouvert.
        /// </summary>
        public static List<RockPlacement> PlanBelvedere(MountainRockProfile rp, MountainProfile mp, MountainRockContext ctx, MountainRockCatalog catalog, float plateauRadius)
        {
            var result = new List<RockPlacement>();
            var rng = new System.Random(rp.Seed + 303);
            float bestScore = float.MaxValue;
            Vector2 bestDir = default;

            for (int az = 0; az < 360; az += 2)
            {
                Vector2 dir = new Vector2(Mathf.Sin(az * Mathf.Deg2Rad), Mathf.Cos(az * Mathf.Deg2Rad));
                if (!IsCliffAt(ctx, dir, plateauRadius, rp.BelvedereMinCliffSlope)) continue;
                Vector2 view = ctx.Herse + dir * (plateauRadius + CrestRadiusMargin);
                if (!ctx.TryAccept(view, 3f, out _, true)) continue;
                float score = Mathf.Abs(Mathf.DeltaAngle(az, rp.BelvedereTargetAzimuth));
                if (score < bestScore) { bestScore = score; bestDir = dir; }
            }

            if (bestScore == float.MaxValue)
            {
                Debug.LogWarning("[Mountain] Belvedere : aucune crete de falaise valide.");
                return result;
            }

            Vector2 viewPoint = ctx.Herse + bestDir * (plateauRadius - GuardSetback);
            Vector2 tangent = new Vector2(-bestDir.y, bestDir.x);
            // Garde-corps : ouverture centrale de demi-largeur BelvedereGuardOffset, puis grand, moyen, petit de chaque cote.
            float cursor = rp.BelvedereGuardOffset;
            // Dalles du kit en garde-corps naturel (a la place des rochers moyens) quand elles existent.
            RockSize middle = catalog.Has(RockSize.Slab) ? RockSize.Slab : RockSize.Medium;
            foreach (RockSize size in new[] { RockSize.Big, middle, RockSize.Small })
            {
                cursor = AddGuardPair(rp, ctx, catalog, rng, viewPoint, tangent, cursor, size, result);
            }
            return result;
        }

        /// <summary>Pose une roche de chaque cote ; retourne le curseur apres la paire (rayons pris en compte).</summary>
        private static float AddGuardPair(MountainRockProfile rp, MountainRockContext ctx, MountainRockCatalog catalog, System.Random rng,
            Vector2 view, Vector2 tangent, float cursor, RockSize size, List<RockPlacement> output)
        {
            RockAsset left = catalog.Pick(size, rng);
            RockAsset right = catalog.Pick(size, rng);
            float radius = Mathf.Max(left.Radius, right.Radius);
            float offset = cursor + radius;
            Place(rp, ctx, rng, left, view + tangent * offset, output);
            Place(rp, ctx, rng, right, view - tangent * offset, output);
            return offset + radius + 0.3f;
        }

        private static void Place(MountainRockProfile rp, MountainRockContext ctx, System.Random rng, RockAsset asset, Vector2 p, List<RockPlacement> output)
        {
            if (!ctx.TryAccept(p, asset.Radius, out _, true)) return;
            ctx.Occupy(p, asset.Radius);
            float scale = 1f + ((float)rng.NextDouble() * 2f - 1f) * rp.ScaleVariation * 0.6f;
            output.Add(MountainRockPlanner.Pose(ctx, asset, p, Yaw(rng), scale, HeroBurial, 0.5f, RockZone.Plateau, 2));
        }

        private static bool IsCliffAt(MountainRockContext ctx, Vector2 dir, float plateauRadius, float minSlope)
        {
            Vector3 o = ctx.Terrain.transform.position;
            TerrainData data = ctx.Terrain.terrainData;
            float max = 0f;
            for (float r = plateauRadius; r <= plateauRadius + CliffProbeLength; r += 0.5f)
            {
                Vector2 p = ctx.Herse + dir * r;
                max = Mathf.Max(max, data.GetSteepness((p.x - o.x) / data.size.x, (p.y - o.z) / data.size.z));
            }
            return max >= minSlope;
        }

        #endregion

        private static float Yaw(System.Random rng) => (float)rng.NextDouble() * 360f;
    }
}
