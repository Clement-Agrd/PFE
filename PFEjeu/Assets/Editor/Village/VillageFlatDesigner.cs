using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Construit les etages du village sur le terrain ACTUEL (plat), sans repasser par le relief de BASE.
    /// Un relief guide synthetique (trois niveaux emboites, formes en rectangles arrondis) est ajoute au plat ;
    /// le champ d'etages existant (murs geologiques, rampes, escaliers) en tire ensuite les etages.
    /// L'etage bas est le plat existant : seule de la terre est ajoutee (etages Milieu et Haut).
    /// Repere (s, t) du profil : s le long de l'axe herse -> butte, t lateral.
    /// </summary>
    public static class VillageFlatDesigner
    {
        private const string MenuRoot = "Tools/Defense/Relief/Village/";

        /// <summary>Etage Milieu : centre s, centre t, demi-longueur, demi-largeur, rayon des coins.</summary>
        private static readonly float[] Mid = { 160f, 0f, 100f, 72f, 25f };
        /// <summary>Etage Haut, emboite dans le Milieu.</summary>
        private static readonly float[] High = { 148f, 0f, 42f, 52f, 20f };
        /// <summary>Demi-largeur (m) de la rampe de relief guide autour de chaque contour.</summary>
        private const float GuideHalfBand = 9f;
        /// <summary>Falaise adossee derriere l'etage Haut : region (s, t, demi-longueur, demi-largeur, rayon) en arc de cercle.</summary>
        private static readonly float[] Backdrop = { 342f, 0f, 150f, 105f, 40f };
        /// <summary>Hauteur (m) de la falaise au-dessus du sol local, puis longueur (m) du replat de crete avant retour au relief d'origine.</summary>
        private const float BackdropHeight = 18f;
        private const float BackdropTop = 12f;
        private const float BackdropFall = 28f;
        private const float BackdropLineWobble = 6f;
        private const float FlatSlopeDegrees = 3f;
        /// <summary>Sauvegarde du terrain plat d'origine : source unique, l'outil est ainsi rejouable sans cumul.</summary>
        private const string FlatBackupPath = "Assets/Scenes/GA/Backups/SceneJeux_DefenseTerrain_userFlatTRUE.asset";
        /// <summary>Distance (m) a la muraille ou le terrain reste intact, puis largeur (m) du raccord vers le village.</summary>
        private const float WallKeep = 6f;
        private const float WallBlend = 14f;
        /// <summary>Adoucissement (m) du raccord entre un etage et le flanc de montagne.</summary>
        private const float MountainBlend = 3f;
        /// <summary>Nombre de lissages successifs d'une paroi de gradin : plus il est grand, plus la paroi est raide et le plat net.</summary>
        private const int ScarpSharpness = 8;
        /// <summary>Hauteur (m) au-dessus du niveau bas sur laquelle un etage prend le dessus sur le terrain d'origine.</summary>
        private const float RaisedBlend = 1f;

        [MenuItem(MenuRoot + "A - Recommencer sur le plat (etages + parois)")]
        public static void Run()
        {
            Build();
            VillageCliffPainter.Paint();
        }

        public static void Build()
        {
            VillageLayoutProfile profile = VillageLayoutProfile.LoadOrCreate();
            Terrain terrain = DefenseReliefScene.Terrain;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int res = data.heightmapResolution;
            TerrainData flatSource = AssetDatabase.LoadAssetAtPath<TerrainData>(FlatBackupPath);
            if (flatSource == null || flatSource.heightmapResolution != res)
            {
                Debug.LogError("[VillageFlat] Sauvegarde du terrain plat introuvable ou de resolution differente : " + FlatBackupPath);
                return;
            }

            float[,] flat = flatSource.GetHeights(0, 0, res, res);
            float[,] current = data.GetHeights(0, 0, res, res);
            var frame = new VillageFrame(profile.AxisOrigin, profile.AxisTarget);
            VillageTerraceField field = CreateField(profile, terrain, out VillageHeightSampler underlying);
            var cliff = new VillageCliffProfile(profile);
            var side = new VillageSide(WallKeep, WallBlend);
            if (!side.IsValid)
            {
                Debug.LogError("[VillageFlat] Muraille introuvable : impossible de limiter le village a son cote.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(data, "Village : recommencer sur le plat");
            int written = 0;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + (float)i / (res - 1) * size.x;
                    float z = origin.z + (float)j / (res - 1) * size.z;
                    if (field.ZoneWeight(x, z) <= 0f) continue;
                    float zoneWeight = field.ZoneWeight(x, z);
                    float natural = flat[j, i] * size.y + origin.y;
                    float mountain = Mathf.Lerp(natural, SteepenScarp(natural, profile.GroundFloorY, profile.FloorStep, profile.UpperFloorLabels.Count), zoneWeight);
                    float inVillage = side.Weight(x, z);
                    if (inVillage <= 0f)
                    {
                        current[j, i] = flat[j, i];
                        continue;
                    }

                    float terraced = field.Evaluate(x, z, underlying.Sample(x, z));
                    // Seuls les etages SUREleves (au-dessus du niveau bas) s'ajoutent au terrain d'origine.
                    float raised = Mathf.Clamp01((terraced - profile.GroundFloorY) / RaisedBlend);
                    float merged = Mathf.Lerp(mountain, SmoothMax(terraced, mountain, MountainBlend), raised);
                    float withCliff = merged + BackdropRise(profile, cliff, frame, x, z) * field.ZoneWeight(x, z);
                    current[j, i] = (Mathf.Lerp(natural, withCliff, inVillage) - origin.y) / size.y;
                    written++;
                }
            }

            data.SetHeights(0, 0, current);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[VillageFlat] " + written + " echantillons ecrits.\n" + Measure(profile, field, terrain));
        }

        /// <summary>
        /// Champ d'etages du village : relief guide (niveau bas partout + niveaux emboites) passe au champ de terrasses.
        /// La montagne n'y figure pas : elle est recollee apres coup, les etages s'y encastrent.
        /// </summary>
        public static VillageTerraceField CreateField(VillageLayoutProfile profile, Terrain terrain, out VillageHeightSampler underlying)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int res = data.heightmapResolution;
            var frame = new VillageFrame(profile.AxisOrigin, profile.AxisTarget);
            float groundNormalized = (profile.GroundFloorY - origin.y) / size.y;
            var guide = new float[res, res];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    float x = origin.x + (float)i / (res - 1) * size.x;
                    float z = origin.z + (float)j / (res - 1) * size.z;
                    guide[j, i] = groundNormalized + GuideRelief(profile, frame, x, z) / size.y;
                }
            }

            underlying = new VillageHeightSampler(guide, origin, size);
            return new VillageTerraceField(profile, DefenseReliefScene.WallAnchors(), underlying);
        }

        /// <summary>
        /// Transforme les flancs d'origine en gradins calés sur les altitudes des etages : entre deux etages, la pente
        /// naturelle devient un plat suivi d'une paroi tres raide (lissage repete), puis un plat a l'altitude de l'etage.
        /// Meme une colline douce devient une suite de parois infranchissables. Au-dela du dernier etage, relief inchange.
        /// </summary>
        private static float SteepenScarp(float height, float floorY, float step, int upperFloors)
        {
            float h = height - floorY;
            if (h <= 0f || h >= step * upperFloors) return height;
            float band = Mathf.Floor(h / step);
            float f = h / step - band;
            for (int i = 0; i < ScarpSharpness; i++) f = f * f * (3f - 2f * f);
            return floorY + step * (band + f);
        }

        /// <summary>Maximum adouci : l'etage se raccorde au flanc de montagne sans arete vive.</summary>
        private static float SmoothMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        /// <summary>
        /// Hauteur ajoutee par la falaise de fond : paroi geologique a la limite de la region Backdrop, replat de crete,
        /// puis retour progressif au relief d'origine. Le tracé de la paroi ondule pour ne jamais etre une ligne droite.
        /// </summary>
        private static float BackdropRise(VillageLayoutProfile profile, VillageCliffProfile cliff, VillageFrame frame, float worldX, float worldZ)
        {
            Vector2 local = frame.ToLocal(worldX, worldZ);
            float wobble = (Mathf.PerlinNoise(local.y / 38f + 12.7f, local.x / 52f + 3.1f) - 0.5f) * 2f * BackdropLineWobble;
            float depth = SignedDepth(local, Backdrop) + wobble;
            float k = BackdropHeight / profile.FloorStep;
            float rise = k * cliff.AddedHeight(depth / k, new Vector2(worldX, worldZ));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - BackdropTop) / BackdropFall));
            return rise * fade;
        }

        #region Relief guide

        private static float GuideRelief(VillageLayoutProfile profile, VillageFrame frame, float worldX, float worldZ)
        {
            Vector2 local = frame.ToLocal(worldX, worldZ);
            float mid = Smooth(SignedDepth(local, Mid));
            float high = Smooth(SignedDepth(local, High));
            return profile.FloorStep * (mid + high);
        }

        /// <summary>Distance signee au bord d'un rectangle arrondi : positive a l'interieur.</summary>
        private static float SignedDepth(Vector2 p, float[] box)
        {
            float qx = Mathf.Abs(p.x - box[0]) - (box[2] - box[4]);
            float qy = Mathf.Abs(p.y - box[1]) - (box[3] - box[4]);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return -(outside + inside - box[4]);
        }

        private static float Smooth(float depth)
        {
            float k = Mathf.Clamp01((depth + GuideHalfBand) / (2f * GuideHalfBand));
            return k * k * (3f - 2f * k);
        }

        #endregion

        #region Mesures

        private static string Measure(VillageLayoutProfile profile, VillageTerraceField field, Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int floors = profile.UpperFloorLabels.Count + 1;
            var area = new int[floors];
            Rect bounds = field.WorldBounds;
            for (float z = bounds.yMin; z <= bounds.yMax; z += 1f)
            {
                for (float x = bounds.xMin; x <= bounds.xMax; x += 1f)
                {
                    if (field.ZoneWeight(x, z) < 1f) continue;
                    float u = (x - origin.x) / data.size.x;
                    float v = (z - origin.z) / data.size.z;
                    if (data.GetSteepness(u, v) > FlatSlopeDegrees) continue;
                    float y = data.GetInterpolatedHeight(u, v) + origin.y;
                    for (int f = 0; f < floors; f++)
                        if (Mathf.Abs(y - profile.FloorY(f)) <= 0.15f) area[f]++;
                }
            }

            var sb = new StringBuilder("Surface plane par etage (pente < " + FlatSlopeDegrees + " deg) :");
            for (int f = 0; f < floors; f++)
            {
                string label = f == 0 ? "Bas" : profile.UpperFloorLabels[f - 1];
                sb.Append("\n  ").Append(label).Append(" (").Append(profile.FloorY(f).ToString("F2", CultureInfo.InvariantCulture))
                  .Append(" m) : ").Append(area[f]).Append(" m2");
            }

            AppendRibbons(sb, "Rampes", field.Ramps, field, terrain);
            AppendRibbons(sb, "Escaliers", field.Stairs, field, terrain);
            return sb.ToString();
        }

        private static void AppendRibbons(StringBuilder sb, string title, IReadOnlyList<VillageRibbon> ribbons, VillageTerraceField field, Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            foreach (VillageRibbon ribbon in ribbons)
            {
                float max = 0f;
                float length = 0f;
                for (int k = 0; k < ribbon.PointCount; k++)
                {
                    Vector2 w = field.Frame.ToWorld(ribbon.Point(k).x, ribbon.Point(k).y);
                    max = Mathf.Max(max, data.GetSteepness((w.x - origin.x) / data.size.x, (w.y - origin.z) / data.size.z));
                    if (k > 0) length += Vector2.Distance(ribbon.Point(k - 1), ribbon.Point(k));
                }

                sb.Append("\n  ").Append(title).Append(' ').Append(ribbon.Label).Append(" : ")
                  .Append(length.ToString("F1", CultureInfo.InvariantCulture)).Append(" m, pente max ")
                  .Append(max.ToString("F1", CultureInfo.InvariantCulture)).Append(" deg");
            }
        }

        #endregion
    }
}
