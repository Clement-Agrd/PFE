using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    public enum RockZone
    {
        PlaineBord,
        FlancBas,
        Eboulis,
        Falaise,
        Sommet,
        Plateau,
        Paroi,
    }

    /// <summary>Une roche a poser : asset, transform complet et zone (sous-groupe de la hierarchie).</summary>
    public struct RockPlacement
    {
        public RockAsset asset;
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
        /// <summary>Etirement par axe (zero = aucun). Applique en plus de scale.</summary>
        public Vector3 stretch;
        public RockZone zone;
        public int cluster;
    }

    /// <summary>
    /// Calcule les grappes de rochers (3 a 5, impair) par regles : zone, pente, altitude, distance au plateau.
    /// Deterministe : graine fixe et parcours de grille ordonne. Ne touche pas a la scene.
    /// </summary>
    public static class MountainRockPlanner
    {
        public static List<RockPlacement> Plan(MountainRockProfile rp, ZoningMaps maps, MountainRockContext ctx, MountainRockCatalog catalog)
        {
            var rng = new System.Random(rp.Seed);
            var result = new List<RockPlacement>();
            float radius = rp.MaxRadiusFromHerse;
            float spacing = rp.GridSpacing;
            Vector2 herse = ctx.Herse;
            int clusterId = 0;

            // Candidats tires dans un ordre fixe (reproductible), puis melanges : si le plafond MaxRocks est
            // atteint, il coupe de facon uniforme et non du sud vers le nord.
            var candidates = new List<KeyValuePair<Vector2, double>>();
            for (float gz = herse.y - radius; gz <= herse.y + radius; gz += spacing)
            {
                for (float gx = herse.x - radius; gx <= herse.x + radius; gx += spacing)
                {
                    var center = new Vector2(
                        gx + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f,
                        gz + (float)(rng.NextDouble() - 0.5) * spacing * 0.9f);
                    candidates.Add(new KeyValuePair<Vector2, double>(center, rng.NextDouble()));
                }
            }
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                KeyValuePair<Vector2, double> swap = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = swap;
            }

            foreach (KeyValuePair<Vector2, double> candidate in candidates)
            {
                if (result.Count >= rp.MaxRocks) break;
                Vector2 center = candidate.Key;
                if (!ctx.InTerrain(center) || Vector2.Distance(center, herse) > radius) continue;

                int k = ctx.CellIndex(center);
                RockZone zone = Classify(maps, rp, k);
                if (candidate.Value > Density(rp, maps, k, center, herse)) continue;

                PlaceCluster(rp, ctx, catalog, rng, center, zone, clusterId++, result);
            }

            PlanWalls(rp, maps, ctx, catalog, rng, clusterId, result);
            return result;
        }

        #region Parois

        /// <summary>
        /// Habille les parois tres raides de formations de falaise serrees, collees a la normale et enfoncees :
        /// elles masquent la texture etiree du terrain et brisent la silhouette droite de la paroi.
        /// </summary>
        private static void PlanWalls(
            MountainRockProfile rp, ZoningMaps maps, MountainRockContext ctx, MountainRockCatalog catalog,
            System.Random rng, int clusterId, List<RockPlacement> output)
        {
            Vector2 herse = ctx.Herse;
            float radius = rp.WallMaxRadius;
            var candidates = new List<Vector2>();
            for (float gz = herse.y - radius; gz <= herse.y + radius; gz += rp.WallSpacing)
            {
                for (float gx = herse.x - radius; gx <= herse.x + radius; gx += rp.WallSpacing)
                {
                    candidates.Add(new Vector2(
                        gx + (float)(rng.NextDouble() - 0.5) * rp.WallSpacing,
                        gz + (float)(rng.NextDouble() - 0.5) * rp.WallSpacing));
                }
            }
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                Vector2 swap = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = swap;
            }

            int placed = 0;
            foreach (Vector2 p in candidates)
            {
                if (placed >= rp.WallMaxRocks) break;
                if (!ctx.InTerrain(p) || Vector2.Distance(p, herse) > radius) continue;
                int k = ctx.CellIndex(p);
                if (maps.Slope[k] < rp.WallMinSlope || maps.RelH[k] < rp.WallMinRel) continue;

                bool usePanel = catalog.Has(RockSize.Panel) && rng.NextDouble() < rp.WallPanelShare;
                RockAsset asset = catalog.Pick(usePanel ? RockSize.Panel : RockSize.Cliff, rng);
                float scale = Mathf.Lerp(rp.WallScaleMin, rp.WallScaleMax, (float)rng.NextDouble()) * AltitudeScale(rp, maps.RelH[k]) * FarScale(rp, ctx, p);
                float footprint = asset.Radius * scale * rp.WallOverlap;
                if (!ctx.TryAccept(p, footprint, out _)) continue;

                ctx.Occupy(p, footprint);
                float yaw = (float)rng.NextDouble() * 360f;
                float burial = Burial(rp, ctx, p, rng) + 0.05f;
                if (asset.Size == RockSize.Panel)
                {
                    // Pan de falaise : debout (leger penchement), face tournee vers l'exterieur de la paroi, enfonce dans la pente.
                    Vector3 n = ctx.Normal(p);
                    float facing = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 30f;
                    output.Add(Pose(ctx, asset, p, facing, scale, 0.18f + 0.12f * (float)rng.NextDouble(), PanelLean, RockZone.Paroi, clusterId, KitStretch(rp, rng)));
                }
                else output.Add(Pose(ctx, asset, p, yaw, scale, burial, 1f, RockZone.Paroi, clusterId));
                placed++;
            }
        }

        #endregion

        #region Zones et densites

        public static RockZone Classify(ZoningMaps maps, MountainRockProfile rp, int k)
        {
            float h = maps.RelH[k];
            float slope = maps.Slope[k];
            AltitudeSet a = maps.Altitudes;
            if (h < 10f) return RockZone.PlaineBord;
            if (h >= a.summitStart) return RockZone.Sommet;
            if (slope >= rp.CliffZoneMinSlope || h >= a.cliffStart) return RockZone.Falaise;
            if (h >= a.screeStart - 5f) return RockZone.Eboulis;
            return RockZone.FlancBas;
        }

        /// <summary>
        /// Densite continue : courbe de l'altitude, modulee en douceur par la pente, la roche peinte, un bruit de plaques
        /// peu contraste et la distance. Aucune limite nette : l'apparition des rochers est progressive.
        /// </summary>
        private static float Density(MountainRockProfile rp, ZoningMaps maps, int k, Vector2 p, Vector2 herse)
        {
            float d = Mathf.Max(0f, rp.DensityByAltitude.Evaluate(maps.RelH[k]));
            d *= 0.7f + 0.6f * MountainMath.Smooth(10f, 45f, maps.Slope[k]);
            d *= 0.8f + 0.4f * Mathf.Max(maps.Rock[k], maps.Scree[k]);

            float noise = MountainMath.Noise01(p.x, p.y, rp.PatchNoiseScale, 37.1f, 12.9f);
            d *= Mathf.Lerp(rp.PatchMin, rp.PatchMax, noise);

            float far = MountainMath.Smooth(45f, rp.MaxRadiusFromHerse, Vector2.Distance(p, herse));
            d *= Mathf.Lerp(rp.NearPlateauBoost, rp.FarPlateauFactor, far);
            return d * rp.DensityMultiplier;
        }

        /// <summary>Taille de la pierre d'ancrage selon l'altitude : moyens en bas, gros blocs au milieu, formations en haut.</summary>
        private static RockSize AnchorSize(MountainRockProfile rp, float rel, System.Random rng)
        {
            double rollCliff = rng.NextDouble();
            double rollBig = rng.NextDouble();
            float cliffP = MountainMath.Smooth(rp.CliffAnchorStartRel, rp.CliffAnchorFullRel, rel) * rp.CliffAnchorMax;
            if (rollCliff < cliffP) return RockSize.Cliff;

            float bigP = MountainMath.Smooth(rp.BigAnchorStartRel, rp.BigAnchorFullRel, rel);
            if (rollBig < bigP * 0.8f) return RockSize.Big;
            return rollBig < 0.5 + 0.5 * bigP ? RockSize.Medium : RockSize.Small;
        }

        /// <summary>Les rochers du pourtour d'une grappe sont d'une taille plus petite que l'ancrage.</summary>
        private static RockSize SatelliteSize(RockSize anchor, System.Random rng)
        {
            double r = rng.NextDouble();
            switch (anchor)
            {
                case RockSize.Cliff: return r < 0.5 ? RockSize.Big : r < 0.85 ? RockSize.Medium : RockSize.Cliff;
                case RockSize.Big: return r < 0.55 ? RockSize.Medium : RockSize.Small;
                case RockSize.Medium: return r < 0.8 ? RockSize.Small : RockSize.Medium;
                default: return RockSize.Small;
            }
        }

        /// <summary>Echelle progressive : plus l'altitude est haute, plus les rochers sont grands.</summary>
        private static float AltitudeScale(MountainRockProfile rp, float rel) =>
            Mathf.Lerp(rp.AltScaleMin, rp.AltScaleMax, MountainMath.Smooth(rp.AltScaleStartRel, rp.AltScaleEndRel, rel));

        /// <summary>Enfoncement de base, plus profond sur pente raide.</summary>
        private static float Burial(MountainRockProfile rp, MountainRockContext ctx, Vector2 p, System.Random rng)
        {
            float slopeT = MountainMath.Smooth(25f, 55f, ctx.Maps.Slope[ctx.CellIndex(p)]);
            return Mathf.Lerp(rp.BurialMin, rp.BurialMax, (float)rng.NextDouble()) + rp.SlopeBurialBonus * slopeT;
        }

        #endregion

        #region Grappes

        private static void PlaceCluster(
            MountainRockProfile rp, MountainRockContext ctx, MountainRockCatalog catalog, System.Random rng,
            Vector2 center, RockZone zone, int clusterId, List<RockPlacement> output)
        {
            int wanted = rng.NextDouble() < rp.FiveRockChance ? 5 : 3;
            var accepted = new List<RockPlacement>(wanted);
            var occupied = new List<KeyValuePair<Vector2, float>>(wanted);

            float rel = ctx.Maps.RelH[ctx.CellIndex(center)];
            RockSize anchorSize = AnchorSize(rp, rel, rng);
            // Pied de falaise : une partie des ancrages devient un eboulis du kit.
            if (zone == RockZone.Eboulis && catalog.Has(RockSize.Scree) && rng.NextDouble() < rp.ScreeAnchorShare) anchorSize = RockSize.Scree;
            RockAsset anchor = catalog.Pick(anchorSize, rng);
            if (!TryPlace(rp, ctx, rng, anchor, center, zone, clusterId, accepted, occupied)) { Rollback(ctx, occupied); return; }

            float anchorRadius = anchor.Radius * accepted[0].scale;
            // Satellites groupes sur un arc (un cote de l'ancrage, a distances irregulieres) : pas d'anneau regulier.
            float angle0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            float arc = Mathf.Lerp(1.6f, 3.4f, (float)rng.NextDouble());
            for (int i = 1; i < wanted; i++)
            {
                RockAsset satellite = catalog.Pick(SatelliteSize(anchor.Size, rng), rng);
                float angle = angle0 + arc * ((i - 1f) / Mathf.Max(1, wanted - 2)) + ((float)rng.NextDouble() - 0.5f) * 0.7f;
                float distance = anchorRadius * Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble()) + satellite.Radius * Mathf.Lerp(0.5f, 1.6f, (float)rng.NextDouble());
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                TryPlace(rp, ctx, rng, satellite, p, zone, clusterId, accepted, occupied);
            }

            // Nombre impair, 3 a 5.
            if (accepted.Count == 4)
            {
                accepted.RemoveAt(3);
                ctx.TruncateOccupancy(ctx.OccupiedCount - 1);
                occupied.RemoveAt(occupied.Count - 1);
            }
            if (accepted.Count < 3) { Rollback(ctx, occupied); return; }
            output.AddRange(accepted);
        }

        private static bool TryPlace(
            MountainRockProfile rp, MountainRockContext ctx, System.Random rng, RockAsset asset, Vector2 p, RockZone zone,
            int clusterId, List<RockPlacement> accepted, List<KeyValuePair<Vector2, float>> occupied)
        {
            float rel = ctx.Maps.RelH[ctx.CellIndex(p)];
            float variation = asset.IsKit ? rp.KitScaleVariation : rp.ScaleVariation;
            float scale = (1f + ((float)rng.NextDouble() * 2f - 1f) * variation) * AltitudeScale(rp, rel) * asset.ScaleBoost * FarScale(rp, ctx, p);
            Vector3 stretch = asset.IsKit ? KitStretch(rp, rng) : Vector3.one;
            // Miroir aleatoire : double l'apparente variete des formes (pack et kit).
            if (rng.NextDouble() < rp.MirrorChance) stretch.x = -stretch.x;
            var tilt = new Vector2(((float)rng.NextDouble() * 2f - 1f) * rp.TiltDegrees, ((float)rng.NextDouble() * 2f - 1f) * rp.TiltDegrees);
            float footprint = asset.Radius * scale * Mathf.Max(Mathf.Abs(stretch.x), stretch.z);
            float yaw = (float)rng.NextDouble() * 360f;
            float burial = Burial(rp, ctx, p, rng);
            if (!ctx.TryAccept(p, footprint, out _)) return false;

            ctx.Occupy(p, footprint);
            occupied.Add(new KeyValuePair<Vector2, float>(p, footprint));
            accepted.Add(Pose(ctx, asset, p, yaw, scale, burial, rp.AlignToNormal, zone, clusterId, stretch, tilt));
            return true;
        }

        private static void Rollback(MountainRockContext ctx, List<KeyValuePair<Vector2, float>> occupied)
        {
            ctx.TruncateOccupancy(ctx.OccupiedCount - occupied.Count);
            occupied.Clear();
        }

        #endregion

        private const float PanelLean = 0.25f;

        /// <summary>Agrandit les rochers lointains pour qu'ils se lisent sur les montagnes de l'horizon.</summary>
        private static float FarScale(MountainRockProfile rp, MountainRockContext ctx, Vector2 p) =>
            Mathf.Lerp(1f, rp.FarScaleMax, MountainMath.Smooth(rp.FarScaleStart, rp.FarScaleEnd, Vector2.Distance(p, ctx.Herse)));

        private static Vector3 KitStretch(MountainRockProfile rp, System.Random rng)
        {
            float xz = rp.KitStretchXZ, y = rp.KitStretchY;
            return new Vector3(
                1f + ((float)rng.NextDouble() * 2f - 1f) * xz,
                1f + ((float)rng.NextDouble() * 2f - 1f) * y,
                1f + ((float)rng.NextDouble() * 2f - 1f) * xz);
        }

        private const float ContactFootprint = 0.9f;
        private const float ContactStrength = 1.0f;
        private const float MaxContactSink = 0.42f;
        private const int ContactSamples = 8;

        /// <summary>
        /// Sur pente raide, le cote aval de l'empreinte n'a pas de sol : on descend la roche le long de son axe
        /// jusqu'au point le plus bas du terrain sous l'empreinte (valeur <= 0).
        /// </summary>
        private static float LowestContactOffset(MountainRockContext ctx, RockAsset asset, Vector2 xz, float scale, Vector3 up, Vector3 ground)
        {
            float radius = asset.Radius * scale * ContactFootprint;
            float lowest = 0f;
            for (int i = 0; i < ContactSamples; i++)
            {
                float angle = i * Mathf.PI * 2f / ContactSamples;
                Vector2 q = xz + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                var sample = new Vector3(q.x, ctx.GroundY(q), q.y);
                lowest = Mathf.Min(lowest, Vector3.Dot(sample - ground, up));
            }
            // Plafond : une roche ne s'enfonce jamais de plus de 40 % de sa hauteur pour suivre la pente.
            return Mathf.Max(lowest * ContactStrength, -asset.BoundsSize.y * scale * MaxContactSink);
        }

        /// <summary>
        /// Pose une roche : axe aligne sur la normale (melange avec la verticale), rotation aleatoire autour de
        /// cet axe, enfoncement d'une fraction de la hauteur le long de l'axe.
        /// </summary>
        public static RockPlacement Pose(
            MountainRockContext ctx, RockAsset asset, Vector2 xz, float yawDegrees, float scale, float burial,
            float alignToNormal, RockZone zone, int clusterId, Vector3 stretch = default, Vector2 tilt = default)
        {
            if (stretch == Vector3.zero) stretch = Vector3.one;
            Vector3 up = Vector3.Slerp(Vector3.up, ctx.Normal(xz), alignToNormal).normalized;
            Quaternion rotation = Quaternion.AngleAxis(yawDegrees, up) * Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(tilt.x, 0f, tilt.y);
            var anchor = Vector3.Scale(new Vector3(asset.BoundsCenter.x, asset.BottomY + burial * asset.BoundsSize.y, asset.BoundsCenter.z), stretch);
            var ground = new Vector3(xz.x, ctx.GroundY(xz), xz.y);
            if (zone != RockZone.Plateau) ground += up * LowestContactOffset(ctx, asset, xz, scale * Mathf.Max(Mathf.Abs(stretch.x), stretch.z), up, ground);
            return new RockPlacement
            {
                asset = asset,
                position = ground - rotation * (anchor * scale),
                rotation = rotation,
                scale = scale,
                stretch = stretch,
                zone = zone,
                cluster = clusterId,
            };
        }
    }
}
