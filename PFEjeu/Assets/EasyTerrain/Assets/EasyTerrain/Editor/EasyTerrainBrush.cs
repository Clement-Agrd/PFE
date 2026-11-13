using System;
using UnityEditor;
using UnityEngine;

namespace EasyTerrain.EditorTools
{
    /// <summary>
    /// Pinceau unique dans la Scene : relief, textures, overlay, herbe, arbres.
    /// Reçoit ses données par délégués (terrain, profil, réglages) : aucune dépendance directe à la fenêtre.
    ///
    /// SceneView.duringSceneGui -> raycast heightmap -> opération selon BrushMode
    /// </summary>
    public sealed class EasyTerrainBrush
    {
        private const double TreeStrokeInterval = 0.12;

        private readonly Func<Terrain> getTerrain;
        private readonly Func<EasyTerrainProfile> getProfile;
        private readonly Func<BrushSettings> getSettings;

        private bool attached;
        private bool stroking;
        private float strokeFlattenHeight;
        private double lastTreeTime;

        public EasyTerrainBrush(Func<Terrain> getTerrain, Func<EasyTerrainProfile> getProfile, Func<BrushSettings> getSettings)
        {
            this.getTerrain = getTerrain;
            this.getProfile = getProfile;
            this.getSettings = getSettings;
        }

        public bool IsAttached => attached;

        public void Attach()
        {
            if (attached) return;
            SceneView.duringSceneGui += OnSceneGui;
            attached = true;
            SceneView.RepaintAll();
        }

        public void Detach()
        {
            if (!attached) return;
            SceneView.duringSceneGui -= OnSceneGui;
            attached = false;
            stroking = false;
            SceneView.RepaintAll();
        }

        private void OnSceneGui(SceneView view)
        {
            Terrain terrain = getTerrain();
            BrushSettings settings = getSettings();
            if (terrain == null || settings == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);
            if (e.alt) return;                                      // navigation Scene intacte

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!TerrainRaycaster.TryHit(terrain, ray, out Vector3 hit)) return;

            if (e.type == EventType.Repaint) DrawCursor(terrain, hit, settings);
            if (e.type == EventType.MouseMove) view.Repaint();

            if (e.button != 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (HandleUtility.nearestControl != controlId) return;
                    GUIUtility.hotControl = controlId;
                    BeginStroke(terrain, settings, hit);
                    Apply(terrain, settings, hit);
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) return;
                    Apply(terrain, settings, hit);
                    e.Use();
                    view.Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId) return;
                    GUIUtility.hotControl = 0;
                    EndStroke(terrain, settings);
                    e.Use();
                    break;
            }
        }

        #region Coup de pinceau
        private void BeginStroke(Terrain terrain, BrushSettings s, Vector3 hit)
        {
            stroking = true;
            lastTreeTime = 0;
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Easy Terrain : " + s.mode);

            Vector3 origin = terrain.transform.position;
            strokeFlattenHeight = s.flattenHeight > 0f ? s.flattenHeight : hit.y - origin.y;
        }

        private void EndStroke(Terrain terrain, BrushSettings s)
        {
            if (!stroking) return;
            stroking = false;
            if (s.mode <= BrushMode.Flatten) HeightBrushOps.Finish(terrain);
            terrain.Flush();
            EditorUtility.SetDirty(terrain.terrainData);
        }

        private void Apply(Terrain terrain, BrushSettings s, Vector3 hit)
        {
            EasyTerrainProfile profile = getProfile();

            switch (s.mode)
            {
                case BrushMode.Raise:
                    HeightBrushOps.Apply(terrain, hit, s.radius, s.strength, HeightBrushKind.Raise, 0f); break;
                case BrushMode.Lower:
                    HeightBrushOps.Apply(terrain, hit, s.radius, s.strength, HeightBrushKind.Lower, 0f); break;
                case BrushMode.Smooth:
                    HeightBrushOps.Apply(terrain, hit, s.radius, s.strength, HeightBrushKind.Smooth, 0f); break;
                case BrushMode.Flatten:
                    HeightBrushOps.Apply(terrain, hit, s.radius, s.strength, HeightBrushKind.Flatten, strokeFlattenHeight); break;

                case BrushMode.PaintTexture:
                    SplatOps.Paint(terrain, hit, s.radius, s.strength, s.layerIndex, null); break;
                case BrushMode.Overlay:
                    SplatOps.Paint(terrain, hit, s.radius, s.strength, s.layerIndex,
                        new MaskParams { Scale = s.overlayScale, Amount = s.overlayAmount, Sharpness = s.overlaySharpness, Seed = s.overlaySeed });
                    break;

                case BrushMode.AddGrass:
                    if (TryGetRule(profile != null ? profile.details : null, s.detailRuleIndex, out DetailRule detail))
                        VegetationOps.PaintDetail(terrain, hit, s.radius, s.strength, detail, false);
                    break;
                case BrushMode.EraseGrass:
                    VegetationOps.PaintDetail(terrain, hit, s.radius, Mathf.Max(s.strength, 0.5f), null, true);
                    break;

                case BrushMode.AddTrees:
                    if (EditorApplication.timeSinceStartup - lastTreeTime < TreeStrokeInterval) break;
                    lastTreeTime = EditorApplication.timeSinceStartup;
                    if (TryGetRule(profile != null ? profile.trees : null, s.treeRuleIndex, out TreeRule tree))
                        VegetationOps.PlaceTrees(terrain, hit, s.radius, tree, s.treesPerStroke);
                    break;
                case BrushMode.EraseTrees:
                    VegetationOps.EraseTrees(terrain, hit, s.radius); break;
            }
        }

        private static bool TryGetRule<T>(System.Collections.Generic.List<T> list, int index, out T rule) where T : class
        {
            rule = null;
            if (list == null || index < 0 || index >= list.Count) return false;
            rule = list[index];
            return rule != null;
        }
        #endregion

        #region Curseur
        private static void DrawCursor(Terrain terrain, Vector3 hit, BrushSettings s)
        {
            Color color = ModeColor(s.mode);
            Handles.color = color;
            DrawProjectedCircle(terrain, hit, s.radius);
            Handles.color = new Color(color.r, color.g, color.b, 0.45f);
            DrawProjectedCircle(terrain, hit, s.radius * 0.5f);
        }

        // Cercle posé sur le relief (suit la hauteur du terrain).
        private static void DrawProjectedCircle(Terrain terrain, Vector3 center, float radius)
        {
            const int segments = 48;
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            var points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(a) * radius;
                float z = center.z + Mathf.Sin(a) * radius;
                float nx = Mathf.Clamp01((x - origin.x) / data.size.x);
                float nz = Mathf.Clamp01((z - origin.z) / data.size.z);
                points[i] = new Vector3(x, origin.y + data.GetInterpolatedHeight(nx, nz) + 0.15f, z);
            }
            Handles.DrawPolyLine(points);
        }

        private static Color ModeColor(BrushMode mode)
        {
            switch (mode)
            {
                case BrushMode.Raise: return new Color(0.4f, 1f, 0.5f);
                case BrushMode.Lower: return new Color(1f, 0.5f, 0.35f);
                case BrushMode.Smooth: return new Color(0.5f, 0.8f, 1f);
                case BrushMode.Flatten: return new Color(1f, 0.9f, 0.4f);
                case BrushMode.PaintTexture: return new Color(0.8f, 0.6f, 1f);
                case BrushMode.Overlay: return new Color(1f, 0.55f, 0.9f);
                case BrushMode.AddGrass: return new Color(0.5f, 1f, 0.3f);
                case BrushMode.AddTrees: return new Color(0.2f, 0.8f, 0.4f);
                default: return new Color(1f, 0.3f, 0.3f);
            }
        }
        #endregion
    }
}
