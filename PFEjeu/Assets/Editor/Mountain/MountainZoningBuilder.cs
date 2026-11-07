using System.Collections.Generic;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Remplit les cartes de zonage : altitude, pente, roche, eboulis (au pied des falaises),
    /// givre (taches, sommets seulement), mousse et densites de vegetation.
    /// </summary>
    public static class MountainZoningBuilder
    {
        private const float AltitudeGateStart = 6f;
        private const float AltitudeGateEnd = 12f;
        private const float SummitRockCap = 0.9f;

        public static void Fill(ZoningMaps maps, MountainProfile profile, TerrainData data, Vector2 herse)
        {
            MeasureHeightAndSlope(maps, profile, data);
            maps.Altitudes = ResolveAltitudes(maps, profile, herse);

            var offsets = new NoiseOffsets(profile.Seed);
            float[] rockBySlope = ComputeSlopeRock(maps, profile, offsets);
            int footCells = Mathf.Max(1, Mathf.RoundToInt(profile.ScreeFootRadius / maps.CellX));
            float[] cliffFoot = MountainMath.BoxBlur(rockBySlope, maps.Res, footCells);

            ComposeMasks(maps, profile, offsets, rockBySlope, cliffFoot, herse);
            ComputeVegetation(maps, profile, herse);
        }

        #region Mesures

        private static void MeasureHeightAndSlope(ZoningMaps maps, MountainProfile profile, TerrainData data)
        {
            int res = maps.Res;
            for (int j = 0; j < res; j++)
            {
                float v = (j + 0.5f) / res;
                for (int i = 0; i < res; i++)
                {
                    float u = (i + 0.5f) / res;
                    int k = maps.Index(i, j);
                    maps.RelH[k] = data.GetInterpolatedHeight(u, v) + maps.Origin.y - profile.ValleyY;
                    maps.Slope[k] = data.GetSteepness(u, v);
                }
            }
        }

        private static AltitudeSet ResolveAltitudes(ZoningMaps maps, MountainProfile profile, Vector2 herse)
        {
            var flank = new List<float>();
            float radiusSqr = profile.PercentileRadiusFromHerse * profile.PercentileRadiusFromHerse;
            for (int j = 0; j < maps.Res; j++)
            {
                for (int i = 0; i < maps.Res; i++)
                {
                    int k = maps.Index(i, j);
                    if (maps.RelH[k] < profile.FlankMinRelHeight) continue;
                    if ((maps.CellCenter(i, j) - herse).sqrMagnitude > radiusSqr) continue;
                    flank.Add(maps.RelH[k]);
                }
            }
            flank.Sort();
            float[] sorted = flank.ToArray();
            ThresholdMode mode = profile.Mode;

            return new AltitudeSet
            {
                treeThinStart = profile.TreeThinStart.Resolve(mode, sorted),
                treeNone = profile.TreeNone.Resolve(mode, sorted),
                screeStart = profile.ScreeStart.Resolve(mode, sorted),
                cliffStart = profile.CliffStart.Resolve(mode, sorted),
                summitStart = profile.SummitStart.Resolve(mode, sorted),
                frostStart = profile.FrostStart.Resolve(mode, sorted),
            };
        }

        #endregion

        #region Roche par pente

        private readonly struct NoiseOffsets
        {
            public readonly float ax, az, bx, bz, cx, cz;

            public NoiseOffsets(int seed)
            {
                ax = Offset(seed, 1); az = Offset(seed, 2);
                bx = Offset(seed, 3); bz = Offset(seed, 4);
                cx = Offset(seed, 5); cz = Offset(seed, 6);
            }

            private static float Offset(int seed, int salt) => MountainMath.Hash01(salt, salt * 7, seed) * 500f;
        }

        private static float[] ComputeSlopeRock(ZoningMaps maps, MountainProfile profile, NoiseOffsets n)
        {
            var result = new float[maps.Res * maps.Res];
            for (int j = 0; j < maps.Res; j++)
            {
                for (int i = 0; i < maps.Res; i++)
                {
                    int k = maps.Index(i, j);
                    Vector2 p = maps.CellCenter(i, j);
                    float noise = MountainMath.Noise01(p.x, p.y, profile.NoiseScaleMeters, n.ax, n.az);
                    float threshold = profile.RockSlope + (noise - 0.5f) * 2f * profile.SlopeNoise;
                    float altitudeGate = MountainMath.Smooth(AltitudeGateStart, AltitudeGateEnd, maps.RelH[k]);
                    result[k] = MountainMath.Smooth(threshold - profile.RockSlopeBlend, threshold + profile.RockSlopeBlend, maps.Slope[k]) * altitudeGate;
                }
            }
            return result;
        }

        #endregion

        #region Masques

        private static void ComposeMasks(ZoningMaps maps, MountainProfile profile, NoiseOffsets n, float[] rockBySlope, float[] cliffFoot, Vector2 herse)
        {
            AltitudeSet a = maps.Altitudes;
            for (int j = 0; j < maps.Res; j++)
            {
                for (int i = 0; i < maps.Res; i++)
                {
                    int k = maps.Index(i, j);
                    Vector2 p = maps.CellCenter(i, j);
                    float h = maps.RelH[k];
                    float slope = maps.Slope[k];
                    float free = 1f - maps.Protect[k];

                    float noiseB = MountainMath.Noise01(p.x, p.y, profile.NoiseScaleMeters * 0.6f, n.bx, n.bz);
                    float noiseC = MountainMath.Noise01(p.x, p.y, profile.NoiseScaleMeters * 0.4f, n.cx, n.cz);

                    float rockByAltitude = MountainMath.Smooth(a.cliffStart, a.summitStart, h + (noiseB - 0.5f) * 12f)
                                           * MountainMath.Smooth(profile.SummitRockMinSlope, profile.SummitRockMinSlope + 10f, slope);
                    float rock = Mathf.Max(rockBySlope[k], rockByAltitude * SummitRockCap) * free;
                    maps.Rock[k] = rock;

                    float slopeBand = MountainMath.Smooth(profile.ScreeSlopeMin - 4f, profile.ScreeSlopeMin + 2f, slope)
                                      * (1f - MountainMath.Smooth(profile.ScreeSlopeMax, profile.ScreeSlopeMax + 4f, slope));
                    float altitudeBand = MountainMath.Smooth(a.screeStart - 8f, a.screeStart, h);
                    maps.Scree[k] = Mathf.Clamp01(cliffFoot[k] * 1.6f) * slopeBand * altitudeBand * (1f - rock) * free;

                    float frostAltitude = MountainMath.Smooth(a.frostStart, a.frostStart + 10f, h + (noiseB - 0.5f) * 14f);
                    float patch = MountainMath.Smooth(profile.FrostPatchThreshold, profile.FrostPatchThreshold + 0.12f, noiseC);
                    float slopeGate = 1f - MountainMath.Smooth(profile.FrostMaxSlope - 10f, profile.FrostMaxSlope, slope);
                    maps.Frost[k] = frostAltitude * patch * slopeGate * profile.FrostMaxShare * free;

                    // Perspective atmospherique : la roche lointaine s'eclaircit (Neige en voile, jamais continu).
                    float farT = MountainMath.Smooth(profile.FarPaleStart, profile.FarPaleEnd, Vector2.Distance(p, herse));
                    float pale = farT * profile.FarPaleMax * MountainMath.Smooth(0.35f, 0.7f, noiseC) * MountainMath.Smooth(0.3f, 0.8f, rock);
                    maps.Frost[k] = Mathf.Max(maps.Frost[k], pale * free);

                    // Roche sombre en bas, de plus en plus pale en altitude : degrade continu (leger bruit, pas de plaques).
                    float altitudeT = MountainMath.Smooth(profile.AltPaleStartRel, profile.AltPaleEndRel, h);
                    float altPale = altitudeT * profile.AltPaleMax * Mathf.Lerp(0.75f, 1f, noiseB) * MountainMath.Smooth(0.25f, 0.7f, rock);
                    maps.Frost[k] = Mathf.Max(maps.Frost[k], altPale * free);

                    float mossNoise = MountainMath.Smooth(0.35f, 0.7f, noiseB);
                    float mossAltitude = 1f - MountainMath.Smooth(a.screeStart - 5f, a.screeStart + profile.MossAltitudeFalloff, h);
                    maps.Moss[k] = profile.MossMaxShare * mossNoise * mossAltitude;
                }
            }
        }

        #endregion

        #region Vegetation

        private static void ComputeVegetation(ZoningMaps maps, MountainProfile profile, Vector2 herse)
        {
            AltitudeSet a = maps.Altitudes;
            for (int k = 0; k < maps.RelH.Length; k++)
            {
                float h = maps.RelH[k];
                int ci = k % maps.Res;
                int cj = k / maps.Res;
                float distance = Vector2.Distance(maps.CellCenter(ci, cj), herse);
                float thin = MountainMath.Smooth(a.treeThinStart, a.treeThinStart + profile.TreeThinRamp, h);
                float tree = 1f - (1f - profile.TreeSparseKeep) * thin;
                tree *= 1f - MountainMath.Smooth(a.treeNone - profile.TreeFadeWidth, a.treeNone, h);
                float bare = Mathf.Clamp01(maps.Rock[k] + maps.Scree[k]);
                tree *= 1f - MountainMath.Smooth(0.15f, 0.6f, bare);
                // Optimisation : arbres lointains amincis puis supprimes derriere les cretes (invisibles du jeu).
                tree *= 1f - (1f - profile.OuterTreeKeep) * MountainMath.Smooth(profile.OuterTreeThinStart, profile.OuterTreeThinStart + 40f, distance);
                tree *= 1f - MountainMath.Smooth(profile.OuterTreeCullStart, profile.OuterTreeCullEnd, distance);
                maps.TreeKeep[k] = Mathf.Lerp(tree, 1f, maps.Protect[k]);

                float grass = 1f - (1f - profile.GrassKeepHigh) * thin;
                grass *= 1f - MountainMath.Smooth(0.1f, 0.5f, maps.Rock[k]);
                grass *= 1f - MountainMath.Smooth(0.1f, 0.4f, maps.Scree[k]);
                grass *= 1f - MountainMath.Smooth(0.05f, 0.3f, maps.Frost[k]);
                maps.GrassKeep[k] = Mathf.Lerp(grass, 1f, maps.Protect[k]);
            }
        }

        #endregion
    }
}
