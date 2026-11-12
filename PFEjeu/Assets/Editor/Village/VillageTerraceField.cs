using System.Collections.Generic;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Champ de hauteur du village. Les etages suivent le relief sous-jacent (sculpte a la main) :
    /// chaque tranche d'altitude naturelle est aplanie a l'altitude de son etage, les murs se forment
    /// sur les courbes de niveau. Rampes et escaliers sont ensuite traces sur ces memes courbes.
    /// Construit une fois ; l'evaluation ne touche ni a la scene ni aux assets.
    /// </summary>
    public sealed class VillageTerraceField
    {
        private const float TraceStep = 1f;
        private const float AxisSearchStep = 0.25f;
        private const float AxisSearchLength = 220f;
        private const float GradientProbe = 1f;
        private const int NewtonIterations = 3;
        /// <summary>Pente minimale (m/m) du relief guide : evite des distances infinies sur les replats.</summary>
        private const float MinContourSlope = 0.14f;
        /// <summary>En deca de cette fraction du pas, le trace est considere bloque.</summary>
        private const float MinAdvanceRatio = 0.3f;
        /// <summary>Marge (m) du palier de rampe de part et d'autre de l'escalier.</summary>
        private const float LandingMargin = 1f;
        /// <summary>Une rampe est interrompue si la courbe de niveau fait demi-tour (cosinus par rapport au cap initial).</summary>
        private const float MaxBacktrackCosine = -0.2f;
        private static readonly Vector2[] SmoothingTaps = BuildSmoothingTaps();

        private readonly VillageLayoutProfile _profile;
        private readonly IReadOnlyList<Vector2> _wallAnchors;
        private readonly VillageHeightSampler _underlying;
        private readonly List<VillageRibbon> _ramps = new List<VillageRibbon>();
        private readonly List<VillageRibbon> _stairs = new List<VillageRibbon>();
        private readonly float _zoneOuterRadius;
        private readonly VillageCliffProfile _cliff;

        public VillageFrame Frame { get; }
        public IReadOnlyList<VillageRibbon> Ramps => _ramps;
        public IReadOnlyList<VillageRibbon> Stairs => _stairs;
        /// <summary>Rectangle XZ monde hors duquel le champ ne modifie rien.</summary>
        public Rect WorldBounds { get; }

        /// <param name="wallAnchors">Points XZ le long de la muraille : le terrain y est conserve.</param>
        /// <param name="underlying">Terrain avant les etages (BASE + sculpt manuel).</param>
        public VillageTerraceField(
            VillageLayoutProfile profile, IReadOnlyList<Vector2> wallAnchors, VillageHeightSampler underlying)
        {
            _profile = profile;
            _wallAnchors = wallAnchors;
            _underlying = underlying;
            _zoneOuterRadius = profile.ZoneRadius + profile.ZoneFade;
            Frame = new VillageFrame(profile.AxisOrigin, profile.AxisTarget);
            _cliff = new VillageCliffProfile(profile);

            BuildCirculations();
            WorldBounds = ComputeWorldBounds();
        }

        #region Evaluation

        /// <summary>Hauteur finale (Y monde) a partir de la hauteur sous-jacente au meme point.</summary>
        public float Evaluate(float worldX, float worldZ, float underlyingY)
        {
            Vector2 local = Frame.ToLocal(worldX, worldZ);
            float y = TerracedHeight(worldX, worldZ, local, underlyingY);
            foreach (VillageRibbon ramp in _ramps) y = ramp.Apply(local.x, local.y, y);
            foreach (VillageRibbon stair in _stairs) y = stair.Apply(local.x, local.y, y);
            return y;
        }

        /// <summary>Altitude de design des etages seuls (sans rampes ni escaliers).</summary>
        public float TerracedHeight(float worldX, float worldZ)
        {
            return TerracedHeight(worldX, worldZ, Frame.ToLocal(worldX, worldZ), _underlying.Sample(worldX, worldZ));
        }

        /// <summary>Poids des etages : 1 dans la zone, 0 au-dela du fondu.</summary>
        public float ZoneWeight(float worldX, float worldZ)
        {
            float dx = worldX - _profile.ZoneCenter.x;
            float dz = worldZ - _profile.ZoneCenter.y;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            return 1f - SmoothStep(_profile.ZoneRadius, _zoneOuterRadius, distance);
        }

        private float TerracedHeight(float worldX, float worldZ, Vector2 local, float underlyingY)
        {
            float grounded = ApplyGroundFloor(local.x, local.y, worldX, worldZ, underlyingY);
            float weight = ZoneWeight(worldX, worldZ);
            if (weight <= 0f) return grounded;

            // Distance horizontale a chaque crete = ecart d'altitude du relief guide / sa pente locale.
            var world = new Vector2(worldX, worldZ);
            float contourY = ContourHeight(world);
            float slope = Mathf.Max(Gradient(world).magnitude, MinContourSlope);
            float y = Mathf.Min(grounded, _profile.GroundFloorY);
            for (int k = 1; k <= _profile.UpperFloorLabels.Count; k++)
                y += _cliff.AddedHeight((contourY - _profile.Threshold(k)) / slope, world);
            return Mathf.Lerp(grounded, y, weight);
        }

        private float ApplyGroundFloor(float s, float t, float worldX, float worldZ, float y)
        {
            float ds = Mathf.Max(0f, Mathf.Max(_profile.GroundFloorStart - s, s - _profile.GroundFloorEnd));
            float dt = Mathf.Max(0f, Mathf.Abs(t) - _profile.GroundFloorHalfWidth);
            float outside = Mathf.Sqrt(ds * ds + dt * dt);
            if (outside >= _profile.GroundFloorBlend) return y;

            float weight = 1f - SmoothStep(0f, _profile.GroundFloorBlend, outside);
            float wall = DistanceToWall(worldX, worldZ);
            weight *= SmoothStep(_profile.WallKeepRadius, _profile.WallKeepRadius + _profile.WallKeepBlend, wall);
            return Mathf.Lerp(y, _profile.GroundFloorY, weight);
        }

        private float DistanceToWall(float worldX, float worldZ)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _wallAnchors.Count; i++)
            {
                float dx = _wallAnchors[i].x - worldX;
                float dz = _wallAnchors[i].y - worldZ;
                float sqr = dx * dx + dz * dz;
                if (sqr < best) best = sqr;
            }
            return Mathf.Sqrt(best);
        }

        private static float SmoothStep(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        #endregion

        #region Circulations

        /// <summary>
        /// Pour chaque mur : une rampe qui suit sa courbe de niveau (centree sur l'axe, donc croisee a
        /// mi-hauteur par l'escalier) et un escalier droit dans l'axe.
        /// </summary>
        private void BuildCirculations()
        {
            float searchFrom = 0f;
            bool towardPositive = _profile.FirstRampClimbsTowardPositiveT;
            for (int k = 1; k <= _profile.UpperFloorLabels.Count; k++)
            {
                string label = _profile.UpperFloorLabels[k - 1];
                float threshold = _profile.Threshold(k);
                float lowerY = _profile.FloorY(k - 1);
                float upperY = _profile.FloorY(k);
                if (!TryFindAxisCrossing(threshold, searchFrom, out float crossingS))
                {
                    Debug.LogWarning("[VillageLayout] L'axe central ne croise pas le mur de l'etage " + label + " : pas de rampe ni d'escalier.");
                    continue;
                }

                if (_profile.RampsEnabled) _ramps.Add(BuildRamp(label, threshold, crossingS, lowerY, upperY, towardPositive));
                if (_profile.StairsEnabled) _stairs.Add(BuildStair(label, crossingS, lowerY, upperY));
                searchFrom = crossingS + AxisSearchStep;
                towardPositive = !towardPositive;
            }
        }

        private VillageRibbon BuildRamp(string label, float threshold, float crossingS, float lowerY, float upperY, bool towardPositive)
        {
            // Palier plat au croisement avec l'escalier : les deux circulations y partagent la meme altitude.
            float landingHalf = _profile.StairsEnabled ? _profile.StairHalfWidth + LandingMargin : 0f;
            float slopeLength = _profile.FloorStep / Mathf.Tan(_profile.RampSlopeDegrees * Mathf.Deg2Rad);
            float half = slopeLength * 0.5f + landingHalf;
            Vector2 start = Frame.ToWorld(crossingS, _profile.StairLateralOffset);
            Vector2 headDirection = towardPositive ? Frame.Lateral : -Frame.Lateral;

            // Si la courbe de niveau tourne court d'un cote, l'autre cote compense pour garder la pente.
            List<Vector2> footSide = TraceContour(start, threshold, -headDirection, half);
            float footLength = PolylineLength(start, footSide);
            List<Vector2> headSide = TraceContour(start, threshold, headDirection, half + Mathf.Max(0f, half - footLength));
            float headLength = PolylineLength(start, headSide);
            if (footLength + headLength < 2f * half - TraceStep)
            {
                Debug.LogWarning("[VillageLayout] Rampe_" + label + " raccourcie par le relief (" +
                                 (footLength + headLength).ToString("F1") + " m) : pente plus forte que prevu.");
            }

            var world = new List<Vector2>(footSide.Count + headSide.Count + 1);
            for (int i = footSide.Count - 1; i >= 0; i--) world.Add(footSide[i]);
            world.Add(start);
            world.AddRange(headSide);

            float total = footLength + headLength;
            float footEnd = Mathf.Max(0.01f, footLength - landingHalf);
            float headStart = Mathf.Min(total - 0.01f, footLength + landingHalf);
            float midY = (lowerY + upperY) * 0.5f;

            var local = new Vector2[world.Count];
            var heights = new float[world.Count];
            float arc = 0f;
            for (int i = 0; i < world.Count; i++)
            {
                if (i > 0) arc += Vector2.Distance(world[i - 1], world[i]);
                local[i] = Frame.ToLocal(world[i].x, world[i].y);
                if (arc <= footEnd) heights[i] = Mathf.Lerp(lowerY, midY, arc / footEnd);
                else if (arc >= headStart) heights[i] = Mathf.Lerp(midY, upperY, (arc - headStart) / (total - headStart));
                else heights[i] = midY;
            }
            return new VillageRibbon("Rampe_" + label, local, heights, _profile.RampHalfWidth, _profile.RampSideAngleDegrees);
        }

        /// <summary>Deux volees droites dans l'axe, separees par le palier que traverse la rampe.</summary>
        private VillageRibbon BuildStair(string label, float crossingS, float lowerY, float upperY)
        {
            float landingHalf = _profile.RampsEnabled ? _profile.RampHalfWidth : 0f;
            float flightRun = _profile.FloorStep * 0.5f / Mathf.Tan(_profile.StairSlopeDegrees * Mathf.Deg2Rad);
            float midY = (lowerY + upperY) * 0.5f;
            float t = _profile.StairLateralOffset;
            return new VillageRibbon(
                "Escalier_" + label,
                new[]
                {
                    new Vector2(crossingS - landingHalf - flightRun, t),
                    new Vector2(crossingS - landingHalf, t),
                    new Vector2(crossingS + landingHalf, t),
                    new Vector2(crossingS + landingHalf + flightRun, t),
                },
                new[] { lowerY, midY, midY, upperY },
                _profile.StairHalfWidth,
                _profile.StairSideAngleDegrees);
        }

        /// <summary>Premier point de l'axe (a partir de fromS) ou le relief lisse atteint le seuil.</summary>
        private bool TryFindAxisCrossing(float threshold, float fromS, out float crossingS)
        {
            for (float s = fromS; s <= AxisSearchLength; s += AxisSearchStep)
            {
                Vector2 w = Frame.ToWorld(s, _profile.StairLateralOffset);
                if (ZoneWeight(w.x, w.y) < 1f || ContourHeight(w) < threshold) continue;
                crossingS = s;
                return true;
            }
            crossingS = 0f;
            return false;
        }

        /// <summary>
        /// Suit la courbe de niveau "threshold" du relief lisse sur la longueur demandee.
        /// S'arrete si la courbe fait demi-tour ou si l'avance devient nulle (replat).
        /// </summary>
        private List<Vector2> TraceContour(Vector2 start, float threshold, Vector2 direction, float length)
        {
            var points = new List<Vector2>();
            Vector2 p = start;
            Vector2 heading = direction;
            float travelled = 0f;
            while (travelled < length)
            {
                Vector2 gradient = Gradient(p);
                if (gradient.sqrMagnitude < 1e-6f) break;

                var tangent = new Vector2(-gradient.y, gradient.x).normalized;
                if (Vector2.Dot(tangent, heading) < 0f) tangent = -tangent;
                if (Vector2.Dot(tangent, direction) < MaxBacktrackCosine) break;

                Vector2 next = ProjectOnContour(p + tangent * TraceStep, threshold);
                float advance = Vector2.Distance(p, next);
                if (advance < TraceStep * MinAdvanceRatio) break;

                travelled += advance;
                points.Add(next);
                heading = tangent;
                p = next;
            }
            return points;
        }

        private static float PolylineLength(Vector2 start, List<Vector2> points)
        {
            float length = 0f;
            Vector2 previous = start;
            foreach (Vector2 point in points)
            {
                length += Vector2.Distance(previous, point);
                previous = point;
            }
            return length;
        }

        /// <summary>Ramene un point sur la courbe de niveau (quelques pas de Newton le long du gradient).</summary>
        private Vector2 ProjectOnContour(Vector2 p, float threshold)
        {
            for (int i = 0; i < NewtonIterations; i++)
            {
                Vector2 gradient = Gradient(p);
                if (gradient.sqrMagnitude < 1e-6f) break;
                Vector2 correction = gradient * ((ContourHeight(p) - threshold) / gradient.sqrMagnitude);
                p -= Vector2.ClampMagnitude(correction, TraceStep);
            }
            return p;
        }

        private Vector2 Gradient(Vector2 p)
        {
            float dx = ContourHeight(new Vector2(p.x + GradientProbe, p.y)) - ContourHeight(new Vector2(p.x - GradientProbe, p.y));
            float dz = ContourHeight(new Vector2(p.x, p.y + GradientProbe)) - ContourHeight(new Vector2(p.x, p.y - GradientProbe));
            return new Vector2(dx, dz) / (2f * GradientProbe);
        }

        /// <summary>Terrain sous-jacent une fois l'etage bas aplani : c'est lui que les murs suivent.</summary>
        private float NaturalHeight(Vector2 world)
        {
            Vector2 local = Frame.ToLocal(world.x, world.y);
            return ApplyGroundFloor(local.x, local.y, world.x, world.y, _underlying.Sample(world.x, world.y));
        }

        /// <summary>
        /// Relief guide : relief naturel lisse (moyenne sur un disque) plus l'ondulation geologique.
        /// C'est sur lui que sont traces les murs et les rampes.
        /// </summary>
        private float ContourHeight(Vector2 world)
        {
            float radius = _profile.ContourSmoothingRadius;
            float warp = _cliff.ContourWarp(world);
            if (radius <= 0f) return NaturalHeight(world) + warp;

            float sum = NaturalHeight(world);
            for (int i = 0; i < SmoothingTaps.Length; i++) sum += NaturalHeight(world + SmoothingTaps[i] * radius);
            return sum / (SmoothingTaps.Length + 1) + warp;
        }

        private static Vector2[] BuildSmoothingTaps()
        {
            const int outer = 8;
            const int inner = 4;
            var taps = new Vector2[outer + inner];
            for (int i = 0; i < outer; i++)
            {
                float angle = i * Mathf.PI * 2f / outer;
                taps[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            for (int i = 0; i < inner; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI * 2f / inner;
                taps[outer + i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
            }
            return taps;
        }

        #endregion

        #region Emprise

        private Rect ComputeWorldBounds()
        {
            Vector2 c = _profile.ZoneCenter;
            Vector2 min = c - Vector2.one * _zoneOuterRadius;
            Vector2 max = c + Vector2.one * _zoneOuterRadius;

            float blend = _profile.GroundFloorBlend;
            float half = _profile.GroundFloorHalfWidth + blend;
            foreach (float s in new[] { _profile.GroundFloorStart - blend, _profile.GroundFloorEnd + blend })
            {
                foreach (float t in new[] { -half, half })
                {
                    Vector2 w = Frame.ToWorld(s, t);
                    min = Vector2.Min(min, w);
                    max = Vector2.Max(max, w);
                }
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        #endregion
    }
}
