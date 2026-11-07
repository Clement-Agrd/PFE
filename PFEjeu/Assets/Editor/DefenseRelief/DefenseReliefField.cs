using System.Collections.Generic;
using UnityEngine;

namespace Defense.Relief.EditorTools
{
    /// <summary>
    /// Modele mathematique du relief : profil d'altitude des lanes, rives,
    /// plateformes de slots, zones protegees. Pur calcul, aucune ecriture.
    /// </summary>
    public sealed class ReliefField
    {
        public struct LaneSample
        {
            public Vector2 position;
            public float y;
            public float halfWidth;
            public float distanceFromEnd;
            public Vector2 tangent;
            public float bendDegrees;
            public int route;
            public float distanceFromSpawn;
        }

        public readonly struct SlotPlatform
        {
            public readonly Vector2 position;
            public readonly float targetY;
            public readonly bool outerBank;
            public readonly bool active;
            public readonly float raise;
            public readonly float distanceFromSpawn;

            public SlotPlatform(Vector2 position, float targetY, bool outerBank, bool active = true, float raise = 0f, float distanceFromSpawn = 0f)
            {
                this.position = position;
                this.targetY = targetY;
                this.outerBank = outerBank;
                this.active = active;
                this.raise = raise;
                this.distanceFromSpawn = distanceFromSpawn;
            }
        }

        private const float BendWindowMeters = 10f;

        private readonly DefenseReliefConfig _config;
        private readonly TerrainData _baseData;
        private readonly float[,] _baseHeights;
        private readonly Vector3 _origin;
        private readonly Vector2 _herse;
        private readonly Vector2 _barracks;
        private float _barracksY;
        private readonly List<Vector2> _access = new List<Vector2>();
        private bool _hasAccess;
        private readonly float _idwRadius;
        private readonly DefenseReliefConfig.InvertedProfile _inv;
        private readonly bool _inverted;
        private readonly List<LaneSample> _samples = new List<LaneSample>();
        private readonly List<SlotPlatform> _slots = new List<SlotPlatform>();
        // Positions de tous les slots, disponibles avant la construction des plateformes (masque falaise).
        private readonly List<Vector2> _slotPositions = new List<Vector2>();
        // Points a ne pas transformer en falaise (ex. splines de la muraille).
        private readonly List<Vector2> _glacisAnchors = new List<Vector2>();

        public IReadOnlyList<LaneSample> Samples => _samples;
        public IReadOnlyList<SlotPlatform> Slots => _slots;
        public Rect Bounds { get; private set; }

        public ReliefField(
            DefenseReliefConfig config,
            Vector3 terrainOrigin,
            Vector2 herse,
            Vector2 barracks,
            IReadOnlyList<Vector2> slotPositions,
            IReadOnlyList<Vector2> glacisAnchors = null)
        {
            if (glacisAnchors != null) _glacisAnchors.AddRange(glacisAnchors);
            _config = config;
            _baseData = config.BaseTerrainData;
            int res = _baseData.heightmapResolution;
            _baseHeights = _baseData.GetHeights(0, 0, res, res);
            _origin = terrainOrigin;
            _herse = herse;
            _barracks = barracks;
            _inverted = config.IsInverted;
            _inv = config.Inverted;
            _slotPositions.AddRange(slotPositions);
            _idwRadius = _inverted ? _inv.idwRadius : config.IdwRadius;
            _barracksY = _inverted ? _inv.valleyY + _inv.plateauRelHeight : BaseHeight(barracks.x, barracks.y);

            for (int i = 0; i < config.Routes.Count; i++) BuildRoute(i);
            if (_inverted) MergeOverlappingLaneHeights();
            ComputeBounds();
            ChooseBarracksAccess();
            // Casernes : pad pose a la hauteur locale du plateau/glacis (pas a une cote fixe) => pas de marche contre les lanes.
            if (_inverted) _barracksY = BeforePlatforms(barracks, BaseHeight(barracks.x, barracks.y), false, false, out _, out _);
            foreach (Vector2 slot in slotPositions) _slots.Add(BuildSlot(slot));
        }

        #region Profil

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
            return t * t * (3f - 2f * t);
        }

        private float Ease(float t) => _config.ActiveProfile.sCurve ? t * t * (3f - 2f * t) : t;

        private float RampLength(float rise, float fixedLength)
        {
            float slope = _config.ActiveProfile.averageRampSlopeDegrees;
            return slope > 0f ? Mathf.Abs(rise) / Mathf.Tan(slope * Mathf.Deg2Rad) : fixedLength;
        }

        public float ProfileY(DefenseReliefConfig.RouteSettings route, float distanceFromEnd, float totalLength)
        {
            if (_inverted) return InvertedProfileY(route, distanceFromEnd, totalLength);
            DefenseReliefConfig c = _config;
            DefenseReliefConfig.PresetProfile p = c.ActiveProfile;
            float herseY = c.HerseLevelY;
            float r0 = c.HerseFlatLength;
            float bRamp = RampLength(c.JunctionBY - herseY, c.JunctionBRampLength);
            float r1 = r0 + bRamp;
            float r2 = r1 + c.JunctionBFlatLength;

            if (distanceFromEnd <= r0) return herseY;
            if (distanceFromEnd <= r1) return Mathf.Lerp(herseY, c.JunctionBY, Ease((distanceFromEnd - r0) / bRamp));
            if (distanceFromEnd <= r2) return c.JunctionBY;

            float plateauEnd = r2;
            float plateauY = c.JunctionBY;

            if (route.hasJunctionA)
            {
                float aRamp = RampLength(c.JunctionAY - c.JunctionBY, c.JunctionARampLength);
                float r3 = r2 + aRamp;
                float r4 = r3 + c.JunctionAFlatLength;
                if (distanceFromEnd <= r3) return Mathf.Lerp(c.JunctionBY, c.JunctionAY, Ease((distanceFromEnd - r2) / aRamp));
                if (distanceFromEnd <= r4) return c.JunctionAY;
                plateauEnd = r4;
                plateauY = c.JunctionAY;
            }

            float spawnStart = totalLength - c.SpawnFlatLength;
            float available = spawnStart - plateauEnd;
            float spawnY = p.spawnY > 0f ? p.spawnY : route.spawnY;
            if (p.averageRampSlopeDegrees > 0f && available > 0f)
            {
                // Route courte : on abaisse le plateau de depart plutot que de depasser la pente cible.
                spawnY = Mathf.Min(spawnY, plateauY + available * Mathf.Tan(p.averageRampSlopeDegrees * Mathf.Deg2Rad));
            }
            if (distanceFromEnd >= spawnStart || available <= 0f) return spawnY;
            return Mathf.Lerp(plateauY, spawnY, Ease((distanceFromEnd - plateauEnd) / available));
        }

        private float InvEase(float t)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(t, t * t * (3f - 2f * t), _inv.climbEase);
        }

        /// <summary>
        /// Profil "Doux modifie" : plateau, montee, palier B, [rampe B-A, palier A, rampe douce en ease-in]
        /// ou [rampe B vers la plaine], puis plaine plate jusqu'au spawn. Paliers raccourcis si la route est courte.
        /// </summary>
        public float InvertedProfileY(DefenseReliefConfig.RouteSettings route, float d, float total)
        {
            DefenseReliefConfig.InvertedProfile p = _inv;
            float top = p.plateauRelHeight, b = p.palierBRel, a = p.palierARel;
            float pb = p.palierBLength;
            float pa = route.hasJunctionA ? p.palierALength : 0f;
            float fixedLen = p.plateauLaneLength + p.climbLength
                             + (route.hasJunctionA ? p.rampBALength + p.softRampLength : p.rampBToPlainLength);
            float deficit = fixedLen + pb + pa + p.spawnFlatLength - total;
            if (deficit > 0f)
            {
                float paliers = pb + pa;
                float cut = Mathf.Min(paliers, deficit);
                if (paliers > 0f)
                {
                    float k = 1f - cut / paliers;
                    pb *= k;
                    pa *= k;
                }
            }

            float y0 = p.valleyY;
            float s = p.plateauLaneLength;
            if (d <= s) return y0 + top;
            float e = s + p.climbLength;
            if (d <= e) return y0 + Mathf.Lerp(top, b, InvEase((d - s) / (e - s)));
            s = e; e = s + pb;
            if (d <= e) return y0 + b;
            s = e;
            if (route.hasJunctionA)
            {
                e = s + p.rampBALength;
                if (d <= e) return y0 + Mathf.Lerp(b, a, InvEase((d - s) / (e - s)));
                s = e; e = s + pa;
                if (d <= e) return y0 + a;
                s = e; e = s + p.softRampLength;
                if (d <= e)
                {
                    float u = 1f - (d - s) / (e - s);
                    return y0 + a * u * u;
                }
                return y0;
            }
            e = s + p.rampBToPlainLength;
            if (d <= e) return y0 + Mathf.Lerp(b, 0f, InvEase((d - s) / (e - s)));
            return y0;
        }

        private float HalfWidth(float distanceFromEnd)
        {
            float t = Smooth(_config.NarrowUntilDistance, _config.OpenFromDistance, distanceFromEnd);
            return Mathf.Lerp(_config.NarrowHalfWidth, _config.OpenHalfWidth, t);
        }

        #endregion

        #region Construction

        private void BuildRoute(int routeIndex)
        {
            DefenseReliefConfig.RouteSettings route = _config.Routes[routeIndex];
            Vector2[] line = route.centerline;
            float total = 0f;
            for (int i = 1; i < line.Length; i++) total += Vector2.Distance(line[i - 1], line[i]);

            float spacing = Mathf.Max(0.5f, _config.SampleSpacing);
            int count = Mathf.Max(2, Mathf.CeilToInt(total / spacing) + 1);
            var points = new Vector2[count];
            int seg = 1;
            float segStart = 0f;
            for (int k = 0; k < count; k++)
            {
                float s = Mathf.Min(total, k * spacing);
                while (seg < line.Length - 1 && segStart + Vector2.Distance(line[seg - 1], line[seg]) < s)
                {
                    segStart += Vector2.Distance(line[seg - 1], line[seg]);
                    seg++;
                }
                float segLen = Mathf.Max(1e-4f, Vector2.Distance(line[seg - 1], line[seg]));
                points[k] = Vector2.Lerp(line[seg - 1], line[seg], Mathf.Clamp01((s - segStart) / segLen));
            }

            int window = Mathf.Max(1, Mathf.RoundToInt(BendWindowMeters / spacing));
            for (int k = 0; k < count; k++)
            {
                float s = Mathf.Min(total, k * spacing);
                float fromEnd = total - s;
                Vector2 tan = (points[Mathf.Min(count - 1, k + 1)] - points[Mathf.Max(0, k - 1)]).normalized;
                Vector2 before = (points[k] - points[Mathf.Max(0, k - window)]).normalized;
                Vector2 after = (points[Mathf.Min(count - 1, k + window)] - points[k]).normalized;
                _samples.Add(new LaneSample
                {
                    position = points[k],
                    y = ProfileY(route, fromEnd, total),
                    halfWidth = HalfWidth(fromEnd),
                    distanceFromEnd = fromEnd,
                    tangent = tan,
                    bendDegrees = Vector2.SignedAngle(before, after),
                    route = routeIndex,
                    distanceFromSpawn = s,
                });
            }
        }

        /// <summary>
        /// Les routes partagent les memes couloirs mais avec des distances a la herse legerement differentes :
        /// sans fusion, la lane aurait des marches de ~1 m. Moyenne gaussienne des Y des echantillons voisins
        /// (toutes routes confondues) : un meme point physique a une seule altitude, la pente reste bornee.
        /// </summary>
        private void MergeOverlappingLaneHeights()
        {
            const float sigma = 3f;
            const float cutoffSqr = 9f * 9f;
            var merged = new float[_samples.Count];
            for (int i = 0; i < _samples.Count; i++)
            {
                float sum = 0f, weight = 0f;
                for (int j = 0; j < _samples.Count; j++)
                {
                    float sqr = (_samples[i].position - _samples[j].position).sqrMagnitude;
                    if (sqr > cutoffSqr) continue;
                    float w = Mathf.Exp(-sqr / (sigma * sigma));
                    sum += w * _samples[j].y;
                    weight += w;
                }
                merged[i] = sum / weight;
            }
            for (int i = 0; i < _samples.Count; i++)
            {
                LaneSample s = _samples[i];
                s.y = merged[i];
                _samples[i] = s;
            }
        }

        private void ComputeBounds()
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (LaneSample s in _samples)
            {
                min = Vector2.Min(min, s.position);
                max = Vector2.Max(max, s.position);
            }
            float margin = _config.BlendRadius + _config.OpenHalfWidth + 10f;
            if (_inverted) margin = Mathf.Max(_config.BankWidth + _inv.plateauRelHeight * _inv.flankRunPerMeter + _config.OpenHalfWidth, _inv.plateauRadius + _inv.plateauRelHeight * _inv.flankRunPerMeter) + 10f;
            Bounds = new Rect(min - Vector2.one * margin, (max - min) + Vector2.one * margin * 2f);
        }

        /// <summary>
        /// Couloir plat a l'altitude des casernes (inchangee) jusqu'au bord de la zone plate
        /// de la herse : les soldats restent relies a la lane par la base, sans escalader la berge.
        /// </summary>
        private void ChooseBarracksAccess()
        {
            _access.Clear();
            _access.Add(_barracks);
            foreach (Vector2 waypoint in _config.BarracksAccessWaypoints) _access.Add(waypoint);

            Vector2 last = _access[_access.Count - 1];
            Vector2 fromHerse = last - _herse;
            if (fromHerse.sqrMagnitude < 1e-4f) return;
            _access.Add(_herse + fromHerse.normalized * (_config.HerseFlatRadius + _config.BarracksAccessReach));
            _hasAccess = true;
        }

        private float DistanceToAccess(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < _access.Count; i++)
            {
                Vector2 a = _access[i - 1];
                Vector2 axis = _access[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, axis) / Mathf.Max(1e-4f, axis.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + axis * t));
            }
            return best;
        }

        private void RefineOnSegment(Vector2 p, int i, out float dist, out float y)
        {
            dist = float.MaxValue;
            y = _samples[i].y;
            for (int k = -1; k <= 0; k++)
            {
                int a = i + k, b = i + k + 1;
                if (a < 0 || b >= _samples.Count || _samples[a].route != _samples[b].route) continue;
                Vector2 pa = _samples[a].position;
                Vector2 ab = _samples[b].position - pa;
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, pa + ab * t);
                if (d >= dist) continue;
                dist = d;
                y = Mathf.Lerp(_samples[a].y, _samples[b].y, t);
            }
            if (dist == float.MaxValue) dist = Vector2.Distance(p, _samples[i].position);
        }

        /// <summary>Surface lane + berges + fondu vers le terrain d'origine, avant plateformes.</summary>
        private float Surface(Vector2 p, float baseY, out float outside) => SurfaceFull(p, baseY, out outside, out _);

        private float SurfaceFull(Vector2 p, float baseY, out float outside, out float laneYOut)
        {
            Query(p, out float dMin, out int index, out float idwY);
            outside = dMin - _samples[index].halfWidth;
            laneYOut = idwY;
            if (_inverted)
            {
                // Distance et altitude de lane continues (projection sur le segment) : pas de marches de Voronoi.
                RefineOnSegment(p, index, out float segDist, out float laneY);
                outside = segDist - _samples[index].halfWidth;
                // Altitude de reference = IDW (continue d'une lane a l'autre) : la distance de fondu ne saute plus aux frontieres de Voronoi.
                laneYOut = idwY;
                return SurfaceInverted(baseY, outside, idwY, idwY);
            }
            float bank = outside > 0f ? _config.BankHeight * Smooth(0f, _config.BankWidth, outside) : 0f;
            float linear = Mathf.Clamp01((outside - _config.BankWidth) / Mathf.Max(1e-4f, _config.BlendRadius - _config.BankWidth));
            float fade = Mathf.Lerp(linear, linear * linear * (3f - 2f * linear), _config.FlankEase);
            return Mathf.Lerp(idwY + bank, baseY, fade);
        }

        private float SurfaceInverted(float baseY, float outside, float nearestLaneY, float idwY)
        {
            float rel = Mathf.Max(0f, nearestLaneY - baseY);
            float bankW = _config.BankWidth;
            float blendR = bankW + Mathf.Max(_inv.minFlankBlend, rel * _inv.flankRunPerMeter);
            float bank = outside > 0f
                ? _config.BankHeight * Smooth(0f, bankW, outside) * Smooth(0f, _inv.bankFullRel, rel)
                : 0f;
            float linear = Mathf.Clamp01((outside - bankW) / Mathf.Max(1e-4f, blendR - bankW));
            float fade = Mathf.Lerp(linear, linear * linear * (3f - 2f * linear), _config.FlankEase);
            return Mathf.Lerp(idwY + bank, baseY, fade);
        }

        /// <summary>Surface lane + glacis sans plateformes ni ondulation (pour mesurer les surelevations).</summary>
        public float SurfaceWithoutPlatforms(Vector2 p) => _inverted ? SurfaceBeforePlatforms(p) : Surface(p, BaseHeight(p.x, p.y), out _);

        private SlotPlatform BuildSlot(Vector2 position)
        {
            Query(position, out _, out int index, out _);
            LaneSample near = _samples[index];
            Vector2 toSlot = position - near.position;
            float side = near.tangent.x * toSlot.y - near.tangent.y * toSlot.x;
            bool outer = Mathf.Abs(near.bendDegrees) >= _config.MinBendForOuterDegrees
                         && side * near.bendDegrees < 0f;

            // La plateforme suit la hauteur moyenne locale : peu de deblai, peu de remblai.
            float half = _config.SlotPlatformHalfSize;
            float sum = 0f;
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = new Vector2(position.x + dx * half, position.y + dz * half);
                    sum += _inverted ? BeforePlatforms(q, BaseHeight(q.x, q.y), false, true, out _, out _) : Surface(q, BaseHeight(q.x, q.y), out _);
                    count++;
                }
            }
            if (_inverted)
            {
                float fromSpawn = near.distanceFromSpawn;
                float raise = _inv.slotRaiseNear * Smooth(_inv.slotNoRaiseDistance, _inv.slotNoRaiseDistance + _inv.slotRaiseRamp, fromSpawn);
                bool active = fromSpawn > _inv.slotNoRaiseDistance;
                return new SlotPlatform(position, sum / count + raise, false, active, raise, fromSpawn);
            }
            return new SlotPlatform(position, sum / count + (outer ? _config.SlotOuterBoost : 0f), outer);
        }

        #endregion

        #region Requetes

        /// <summary>Hauteur du terrain d'origine (backup) en Y monde.</summary>
        public float BaseHeight(float worldX, float worldZ)
        {
            int res = _baseData.heightmapResolution;
            float u = Mathf.Clamp01((worldX - _origin.x) / _baseData.size.x) * (res - 1);
            float v = Mathf.Clamp01((worldZ - _origin.z) / _baseData.size.z) * (res - 1);
            int x0 = Mathf.Min(res - 2, (int)u);
            int z0 = Mathf.Min(res - 2, (int)v);
            float fx = u - x0;
            float fz = v - z0;
            float h00 = _baseHeights[z0, x0];
            float h10 = _baseHeights[z0, x0 + 1];
            float h01 = _baseHeights[z0 + 1, x0];
            float h11 = _baseHeights[z0 + 1, x0 + 1];
            float h = Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
            return h * _baseData.size.y + _origin.y;
        }

        public void Query(Vector2 p, out float distanceToLane, out int nearestIndex, out float idwY)
        {
            float best = float.MaxValue;
            int bestIndex = 0;
            float weightSum = 0f;
            float ySum = 0f;
            float radiusSqr = _idwRadius * _idwRadius;

            for (int i = 0; i < _samples.Count; i++)
            {
                LaneSample s = _samples[i];
                float dx = p.x - s.position.x;
                float dz = p.y - s.position.y;
                float sqr = dx * dx + dz * dz;
                if (sqr < best)
                {
                    best = sqr;
                    bestIndex = i;
                }
                if (sqr > radiusSqr) continue;
                float w = _inverted ? Mathf.Pow(sqr + 1f, -_inv.idwExponent) : 1f / (sqr + 1f);
                weightSum += w;
                ySum += w * s.y;
            }

            distanceToLane = Mathf.Sqrt(best);
            nearestIndex = bestIndex;
            idwY = weightSum > 0f ? ySum / weightSum : _samples[bestIndex].y;
        }

        /// <summary>Distance au bord de lane (negative a l'interieur).</summary>
        public float DistanceOutsideLane(Vector2 p)
        {
            Query(p, out float d, out int index, out _);
            return d - _samples[index].halfWidth;
        }

        public bool InsideSlotPlatform(Vector2 p, float extraMargin)
        {
            float half = _config.SlotPlatformHalfSize + extraMargin;
            foreach (SlotPlatform slot in _slots)
            {
                if (Mathf.Abs(p.x - slot.position.x) <= half && Mathf.Abs(p.y - slot.position.y) <= half) return true;
            }
            return false;
        }

        /// <summary>Altitude cible avant lissage. 'protect' vaut 1 la ou le lissage ne doit pas agir.</summary>
        public float Evaluate(float worldX, float worldZ, out float protect)
        {
            var p = new Vector2(worldX, worldZ);
            float baseY = BaseHeight(worldX, worldZ);
            protect = 0f;

            if (!Bounds.Contains(p)) return baseY;
            if (_inverted) return EvaluateInverted(p, baseY, out protect);

            float h = Surface(p, baseY, out float outside);
            if (_config.UndulationAmplitude > 0f)
            {
                float wave = (Mathf.PerlinNoise(worldX / _config.UndulationWavelength + 31.7f, worldZ / _config.UndulationWavelength + 12.3f) - 0.5f) * 2f;
                h += wave * _config.UndulationAmplitude * Smooth(1f, 8f, outside) * (1f - Smooth(_config.BlendRadius * 0.5f, _config.BlendRadius, outside));
            }
            protect = 1f - Smooth(1.5f, 4f, outside);

            float padDist = Mathf.Max(Mathf.Abs(worldX - _barracks.x), Mathf.Abs(worldZ - _barracks.y));
            // Les plateformes ne doivent jamais entamer la lane : porte progressive au bord de lane.
            float laneGate = Smooth(0f, 2f, outside);
            float padMask = laneGate * (1f - Smooth(_config.BarracksPadHalfSize, _config.BarracksPadHalfSize + _config.BarracksPadBlend, padDist));
            if (padMask > 0f)
            {
                h = Mathf.Lerp(h, _barracksY, padMask);
                protect = Mathf.Max(protect, padMask);
            }

            if (_hasAccess)
            {
                float d = DistanceToAccess(p);
                float m = laneGate * (1f - Smooth(_config.BarracksAccessHalfWidth, _config.BarracksAccessHalfWidth + _config.BarracksAccessFade, d));
                if (m > 0f)
                {
                    h = Mathf.Lerp(h, _barracksY, m);
                    protect = Mathf.Max(protect, m);
                }
            }

            foreach (SlotPlatform slot in _slots)
            {
                float c = Mathf.Max(Mathf.Abs(worldX - slot.position.x), Mathf.Abs(worldZ - slot.position.y));
                float m = laneGate * (1f - Smooth(_config.SlotPlatformHalfSize, _config.SlotPlatformHalfSize + _config.SlotPlatformBlend, c));
                // Le coeur de la plateforme des casernes garde la priorite : la sortie des soldats doit rester ouverte.
                m *= Smooth(_config.BarracksPadHalfSize, _config.BarracksPadHalfSize + _config.SlotPlatformBlend, padDist);
                if (m <= 0f) continue;
                float shaped = Mathf.Lerp(h, slot.targetY, m);
                h = Mathf.Clamp(shaped, h - _config.SlotMaxCut, h + _config.SlotMaxFill);
                protect = Mathf.Max(protect, m);
            }

            float herseDist = Vector2.Distance(p, _herse);
            float herseMask = laneGate * (1f - Smooth(_config.HerseFlatRadius, _config.HerseFlatRadius + _config.HerseBlend, herseDist));
            if (herseMask > 0f)
            {
                h = Mathf.Lerp(h, baseY, herseMask);
                protect = Mathf.Max(protect, herseMask);
            }

            return h;
        }

        /// <summary>Surface lane + glacis + plateau + casernes, sans plateformes de slots ni ondulation.</summary>
        public float SurfaceBeforePlatforms(Vector2 p) => BeforePlatforms(p, BaseHeight(p.x, p.y), false, true, out _, out _);

        private float BeforePlatforms(Vector2 p, float baseY, bool withWave, bool includePads, out float protect, out float outside)
        {
            float h = SurfaceFull(p, baseY, out outside, out float laneY);
            float topY = _inv.valleyY + _inv.plateauRelHeight;

            // Ondulation >= 0 (jamais de cuvette), proportionnelle a la proximite de la base.
            float relH = Mathf.Max(0f, h - baseY);
            if (withWave && _inv.undulationMax > 0f)
            {
                float wave = Mathf.PerlinNoise(p.x / _config.UndulationWavelength + 31.7f, p.y / _config.UndulationWavelength + 12.3f);
                h += wave * _inv.undulationMax * Smooth(1f, 8f, outside) * Smooth(0.5f, _inv.undulationFullRel, relH);
            }
            protect = 1f - Smooth(1.5f, 4f, outside);

            // Plateau de la base : plat, la lane garde son propre profil.
            // Le plateau ne monte au-dessus de la lane voisine qu'avec une pente bornee (pas de marche le long des lanes).
            // Disque plat de rayon plateauRadius + glacis (pente <= ~11 deg) vers le terrain d'origine, calcule depuis le BORD du plateau.
            // max/min de champs lipschitziens : jamais de marche ; la lane n'est jamais enterree ni surplombee.
            float diskDist = Vector2.Distance(p, _herse) - _inv.plateauRadius;
            float glacis = Mathf.Max(1f, _inv.plateauRelHeight * _inv.flankRunPerMeter);
            float diskLinear = Mathf.Clamp01(diskDist / glacis);
            float diskFade = Mathf.Lerp(diskLinear, diskLinear * diskLinear * (3f - 2f * diskLinear), _config.FlankEase);
            float diskH = PlateauEdgeHeight(p, topY, diskDist, Mathf.Lerp(topY, baseY, diskFade));
            float capped = Mathf.Min(diskH, laneY + Mathf.Max(0f, outside) * _inv.plateauMaxSlope);
            if (capped > h)
            {
                h = _cliffMode ? SoftMax(h, capped, diskDist, outside) : capped;
                protect = Mathf.Max(protect, Smooth(0f, 2f, outside));
            }
            else if (_cliffMode)
            {
                h = SoftMax(h, capped, diskDist, outside);
            }

            if (!includePads) return h;

            float laneGate = Smooth(0f, 2f, outside);
            float padDist = Mathf.Max(Mathf.Abs(p.x - _barracks.x), Mathf.Abs(p.y - _barracks.y));
            float padMask = laneGate * (1f - Smooth(_config.BarracksPadHalfSize, _config.BarracksPadHalfSize + _config.BarracksPadBlend, padDist));
            if (padMask > 0f)
            {
                h = Mathf.Lerp(h, _barracksY, padMask);
                protect = Mathf.Max(protect, padMask);
            }

            if (_hasAccess && !_inverted)
            {
                float d = DistanceToAccess(p);
                float m = laneGate * (1f - Smooth(_config.BarracksAccessHalfWidth, _config.BarracksAccessHalfWidth + _config.BarracksAccessFade, d));
                if (m > 0f)
                {
                    h = Mathf.Lerp(h, _barracksY, m);
                    protect = Mathf.Max(protect, m);
                }
            }

            return h;
        }

        #region Bord du plateau (falaise)

        private bool _cliffMode => _inverted && _inv.edgeMode == DefenseReliefConfig.PlateauEdgeMode.FalaiseAvecPassages;

        /// <summary>
        /// Hauteur du bord du plateau. Mode glacis : valeur d'origine. Mode falaise : ligne raide
        /// (arete arrondie) sauf aux passages et autour des slots ou le glacis est conserve.
        /// </summary>
        private float PlateauEdgeHeight(Vector2 p, float topY, float diskDist, float glacisH)
        {
            if (!_cliffMode) return glacisH;
            float keepGlacis = KeepGlacisMask(p);
            return Mathf.Lerp(CliffLineHeight(topY, diskDist), glacisH, keepGlacis);
        }

        private float CliffLineHeight(float topY, float diskDist)
        {
            if (diskDist <= 0f) return topY;
            float tan = Mathf.Tan(_inv.cliffSlopeDegrees * Mathf.Deg2Rad);
            float round = Mathf.Max(0.1f, _inv.cliffCrestRound);
            float drop = diskDist < round ? tan * diskDist * diskDist / (2f * round) : tan * (diskDist - round * 0.5f);
            return topY - drop;
        }

        /// <summary>Max adouci au pied de falaise ; nul dans le disque plat (pas de bosse sur le plateau).</summary>
        private float SoftMax(float a, float b, float diskDist, float outsideLane)
        {
            float k = _inv.cliffFootSoftness;
            float hard = Mathf.Max(a, b);
            if (k <= 0f) return hard;
            float diff = a - b;
            float soft = 0.5f * (a + b + Mathf.Sqrt(diff * diff + k * k));
            // Jamais d'adoucissement sur la lane ni sur son bord : son profil doit rester exact.
            float weight = Smooth(0f, 3f, diskDist) * Smooth(2f, 6f, outsideLane);
            return Mathf.Lerp(hard, soft, weight);
        }

        /// <summary>1 = conserver le glacis (passage ou slot proche), 0 = falaise.</summary>
        private float KeepGlacisMask(Vector2 p)
        {
            float mask = 0f;
            float slotFull = _inv.cliffSlotRadius;
            float slotEnd = slotFull + _inv.cliffSlotBlend;
            float slotEndSqr = slotEnd * slotEnd;
            foreach (Vector2 slotPosition in _slotPositions)
            {
                float sqr = (p - slotPosition).sqrMagnitude;
                if (sqr >= slotEndSqr) continue;
                mask = Mathf.Max(mask, 1f - Smooth(slotFull, slotEnd, Mathf.Sqrt(sqr)));
            }

            if (_glacisAnchors.Count > 0)
            {
                float wallFull = _inv.cliffWallRadius;
                float wallEnd = wallFull + _inv.cliffWallBlend;
                float best = wallEnd * wallEnd;
                foreach (Vector2 anchor in _glacisAnchors)
                {
                    float sqr = (p - anchor).sqrMagnitude;
                    if (sqr < best) best = sqr;
                }
                if (best < wallEnd * wallEnd) mask = Mathf.Max(mask, 1f - Smooth(wallFull, wallEnd, Mathf.Sqrt(best)));
            }

            Vector2 fromHerse = p - _herse;
            foreach (DefenseReliefConfig.EdgePassage passage in _inv.passages)
            {
                Vector2 axis = passage.center - _herse;
                if (axis.sqrMagnitude < 1e-4f) continue;
                axis.Normalize();
                float along = Vector2.Dot(fromHerse, axis);
                float lateral = Mathf.Abs(fromHerse.x * axis.y - fromHerse.y * axis.x);
                float m = (1f - Smooth(passage.halfWidth, passage.halfWidth + passage.blend, lateral)) * Smooth(0f, 8f, along);
                mask = Mathf.Max(mask, m);
            }
            return mask;
        }

        #endregion

        private float EvaluateInverted(Vector2 p, float baseY, out float protect)
        {
            float h = BeforePlatforms(p, baseY, true, true, out protect, out float outside);
            float laneGate = Smooth(0f, 2f, outside);
            float padDist = Mathf.Max(Mathf.Abs(p.x - _barracks.x), Mathf.Abs(p.y - _barracks.y));

            foreach (SlotPlatform slot in _slots)
            {
                if (!slot.active) continue;
                float c = Mathf.Max(Mathf.Abs(p.x - slot.position.x), Mathf.Abs(p.y - slot.position.y));
                float slotGate = Mathf.Clamp01(outside / Mathf.Max(0.1f, _inv.slotLaneRamp));
                float m = slotGate * (1f - Smooth(_config.SlotPlatformHalfSize, _config.SlotPlatformHalfSize + _inv.slotPlatformBlend, c));
                m *= Smooth(_config.BarracksPadHalfSize, _config.BarracksPadHalfSize + _inv.slotPlatformBlend, padDist);
                if (m <= 0f) continue;
                float shaped = Mathf.Lerp(h, slot.targetY, m);
                h = Mathf.Clamp(shaped, h - _inv.slotMaxCut, h + _inv.slotMaxFill);
                protect = Mathf.Max(protect, m);
            }

            // Pads de batiments : plateforme plane, raccord doux (deblai ou remblai selon le cas).
            foreach (DefenseReliefConfig.BuildingPad pad in _inv.pads)
            {
                float dBox = Mathf.Max(Mathf.Abs(p.x - pad.center.x) - pad.halfSize.x, Mathf.Abs(p.y - pad.center.y) - pad.halfSize.y);
                float pm = 1f - Smooth(0f, Mathf.Max(0.1f, pad.blend), Mathf.Max(0f, dBox));
                if (pm <= 0f) continue;
                h = Mathf.Lerp(h, pad.y, pm);
                protect = Mathf.Max(protect, pm);
            }
            return h;
        }

        #endregion
    }
}
