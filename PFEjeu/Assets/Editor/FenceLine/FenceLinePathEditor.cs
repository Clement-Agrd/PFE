using UnityEditor;
using UnityEngine;

namespace Core.TowerDefense.Fences.EditorTools
{
    /// <summary>Inspector : liste de groupes, reglages du groupe actif, points, actions de generation et validation.</summary>
    [CustomEditor(typeof(FenceLinePath))]
    public sealed class FenceLinePathEditor : Editor
    {
        private const string DefaultPrefabPath = "Assets/Arts/3D/Defense/Barrere_v1.fbx";

        private static readonly Color[] Palette =
        {
            new Color(1f, 0.75f, 0.1f), new Color(0.2f, 0.8f, 1f), new Color(0.5f, 1f, 0.4f),
            new Color(1f, 0.4f, 0.6f), new Color(0.8f, 0.5f, 1f), new Color(1f, 0.55f, 0.2f),
        };
        private static readonly string[] HiddenGroupFields = { "name", "enabled", "gizmoColor", "points", "generatedRoot", "generated" };

        private Terrain _terrain;
        private TerrainCollider _terrainCollider;
        private string _lastReport = string.Empty;
        private Vector2 _pointScroll;

        #region Lifecycle
        private void OnEnable()
        {
            _terrain = Terrain.activeTerrain;
            _terrainCollider = _terrain != null ? _terrain.GetComponent<TerrainCollider>() : null;
            var path = (FenceLinePath)target;
            if (!FenceEditState.IsActive(path)) FenceEditState.Select(path, 0, -1);
            FenceEditState.ClampSelection();
        }

        private void OnSceneGUI() =>
            FenceSceneTool.OnSceneGUI((FenceLinePath)target, _terrain, _terrainCollider, Repaint);
        #endregion

        #region Inspector
        public override void OnInspectorGUI()
        {
            var path = (FenceLinePath)target;
            FenceEditState.ClampSelection();
            serializedObject.Update();

            DrawGroupList(path);
            EditorGUILayout.Space();
            DrawActiveGroup(path);
            EditorGUILayout.Space();
            DrawGlobalActions(path);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGroupList(FenceLinePath path)
        {
            EditorGUILayout.LabelField($"Groupes ({path.Groups.Count})", EditorStyles.boldLabel);
            var groups = serializedObject.FindProperty("groups");
            for (int i = 0; i < path.Groups.Count; i++)
            {
                var el = groups.GetArrayElementAtIndex(i);
                bool active = i == FenceEditState.Group;
                using (new EditorGUILayout.HorizontalScope(active ? EditorStyles.helpBox : GUIStyle.none))
                {
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("enabled"), GUIContent.none, GUILayout.Width(18));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("gizmoColor"), GUIContent.none, GUILayout.Width(40));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("name"), GUIContent.none);
                    EditorGUILayout.LabelField($"{path.Group(i).Points.Count} pts | {path.Group(i).Generated.Count} barr.", GUILayout.Width(110));
                    if (GUILayout.Button(active ? "●" : "Editer", GUILayout.Width(48))) FenceEditState.Select(path, i, -1);
                    if (GUILayout.Button("▲", GUILayout.Width(24))) MoveGroup(path, i, i - 1);
                    if (GUILayout.Button("▼", GUILayout.Width(24))) MoveGroup(path, i, i + 1);
                    if (GUILayout.Button("⧉", GUILayout.Width(24))) DuplicateGroup(path, i);
                    if (GUILayout.Button("✕", GUILayout.Width(24)) && DeleteGroup(path, i)) { GUIUtility.ExitGUI(); }
                }
            }
            if (GUILayout.Button("+ Nouveau groupe")) AddGroup(path);
        }

        private void DrawActiveGroup(FenceLinePath path)
        {
            var group = FenceEditState.ActiveGroup();
            if (group == null) { EditorGUILayout.HelpBox("Aucun groupe : cree-en un avec « + Nouveau groupe ».", MessageType.Info); return; }

            EditorGUILayout.LabelField($"Groupe actif : {group.Name}", EditorStyles.boldLabel);
            var el = serializedObject.FindProperty("groups").GetArrayElementAtIndex(FenceEditState.Group);
            DrawGroupSettings(el);
            DrawEstimate(group);
            EditorGUILayout.Space();
            DrawPoints(path, group);
            EditorGUILayout.Space();
            DrawGroupActions(path, group);
        }

        private static void DrawGroupSettings(SerializedProperty el)
        {
            var it = el.Copy();
            var end = el.GetEndProperty();
            for (bool enter = true; it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end); enter = false)
            {
                if (System.Array.IndexOf(HiddenGroupFields, it.name) >= 0) continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }

        private static void DrawEstimate(FenceGroup group)
        {
            float length = FenceLayout.PolylineLength(group.Points, group.ClosedLoop);
            var variants = FenceMetrics.MeasureVariants(group);
            if (variants.Count == 0) { EditorGUILayout.HelpBox("Ajoute au moins un modele valide.", MessageType.Warning); return; }
            float avg = 0f, weights = 0f;
            foreach (var v in variants) { avg += v.Length * v.Weight; weights += v.Weight; }
            avg = avg / weights * group.PrefabScale;
            EditorGUILayout.HelpBox($"Longueur du trace : {length:F1} m  |  estimation : ~{Mathf.CeilToInt(length / Mathf.Max(0.1f, avg + group.SegmentGap))} barrieres", MessageType.None);
        }

        private void DrawPoints(FenceLinePath path, FenceGroup group)
        {
            EditorGUILayout.LabelField($"Points ({group.Points.Count})", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Ctrl+clic : ajouter | Ctrl+Shift+clic : inserer sur un segment | clic sphere : selectionner | Shift+Suppr : supprimer.", MessageType.Info);
            _pointScroll = EditorGUILayout.BeginScrollView(_pointScroll, GUILayout.MaxHeight(150));
            for (int i = 0; i < group.Points.Count; i++)
            {
                Vector3 p = group.Points[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool sel = i == FenceEditState.Point;
                    EditorGUILayout.LabelField($"{i}   x {p.x:F1}   z {p.z:F1}", sel ? EditorStyles.boldLabel : EditorStyles.label);
                    if (GUILayout.Button("Sel", GUILayout.Width(36))) { FenceEditState.Point = i; SceneView.RepaintAll(); }
                    if (GUILayout.Button("▲", GUILayout.Width(24))) SwapPoints(path, group, i, i - 1);
                    if (GUILayout.Button("▼", GUILayout.Width(24))) SwapPoints(path, group, i, i + 1);
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(FenceEditState.Point < 0 || FenceEditState.Point >= group.Points.Count))
                    if (GUILayout.Button("Supprimer le point")) RemovePoint(path, group);
                if (GUILayout.Button("Inverser l'ordre")) Mutate(path, "Inverser points", group.ReversePoints);
                if (GUILayout.Button("Coller au terrain")) SnapPoints(path, group);
            }
        }

        private void DrawGroupActions(FenceLinePath path, FenceGroup group)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generer ce groupe", GUILayout.Height(26)))
                    Report(FenceGenerator.Generate(path, FenceEditState.Group), group.Name);
                if (GUILayout.Button("Effacer ce groupe", GUILayout.Height(26)))
                    FenceGenerator.Clear(path, FenceEditState.Group);
            }
        }

        private void DrawGlobalActions(FenceLinePath path)
        {
            EditorGUILayout.LabelField("Tous les groupes", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generer les groupes actives")) Report(FenceGenerator.GenerateAll(path), "tous les groupes");
                if (GUILayout.Button("Effacer tout")) FenceGenerator.ClearAll(path);
                if (GUILayout.Button("Valider")) _lastReport = FenceValidator.Validate(path);
            }
            if (!string.IsNullOrEmpty(_lastReport)) EditorGUILayout.HelpBox(_lastReport, MessageType.None);
        }
        #endregion

        #region Mutations
        private void Report(GenerationResult r, string what)
        {
            _lastReport = $"{what} : {r.Placed} barrieres, {r.Skipped} ignorees, {r.Conflicts} conflits.";
            Debug.Log("[FenceLinePath] " + _lastReport, target);
        }

        private void Mutate(FenceLinePath path, string undoName, System.Action action)
        {
            Undo.RecordObject(path, undoName);
            action();
            EditorUtility.SetDirty(path);
            SceneView.RepaintAll();
        }

        private void SwapPoints(FenceLinePath path, FenceGroup group, int from, int to)
        {
            Mutate(path, "Reordonner point", () =>
            {
                if (group.SwapPoints(from, to) && FenceEditState.Point == from) FenceEditState.Point = to;
            });
        }

        private void RemovePoint(FenceLinePath path, FenceGroup group)
        {
            Mutate(path, "Supprimer point", () =>
            {
                group.RemovePointAt(FenceEditState.Point);
                FenceEditState.Point = Mathf.Min(FenceEditState.Point, group.Points.Count - 1);
            });
        }

        private void SnapPoints(FenceLinePath path, FenceGroup group)
        {
            if (_terrain == null) return;
            Mutate(path, "Coller les points au terrain", () =>
            {
                for (int i = 0; i < group.Points.Count; i++)
                {
                    Vector3 p = group.Points[i];
                    group.SetPoint(i, new Vector3(p.x, FenceMetrics.Ground(_terrain, p.x, p.z), p.z));
                }
            });
        }

        private void AddGroup(FenceLinePath path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            int n = path.Groups.Count;
            Undo.RecordObject(path, "Nouveau groupe de barrieres");
            path.AddGroup(new FenceGroup($"Groupe {n + 1}", Palette[n % Palette.Length], prefab));
            EditorUtility.SetDirty(path);
            FenceEditState.Select(path, n, -1);
        }

        private void DuplicateGroup(FenceLinePath path, int index)
        {
            Undo.RecordObject(path, "Dupliquer groupe");
            path.InsertGroup(index + 1, path.Group(index).CloneSettings(path.Group(index).Name + " (copie)"));
            EditorUtility.SetDirty(path);
            FenceEditState.Select(path, index + 1, -1);
        }

        private bool DeleteGroup(FenceLinePath path, int index)
        {
            var group = path.Group(index);
            string msg = $"Supprimer le groupe « {group.Name} » et ses {group.Generated.Count} barrieres ?";
            if (!EditorUtility.DisplayDialog("Supprimer le groupe", msg, "Supprimer", "Annuler")) return false;
            FenceGenerator.Clear(path, index);
            Undo.RecordObject(path, "Supprimer groupe");
            path.RemoveGroupAt(index);
            EditorUtility.SetDirty(path);
            FenceEditState.Select(path, Mathf.Max(0, index - 1), -1);
            return true;
        }

        private void MoveGroup(FenceLinePath path, int from, int to)
        {
            Mutate(path, "Reordonner groupe", () =>
            {
                if (path.SwapGroups(from, to) && FenceEditState.Group == from) FenceEditState.Group = to;
            });
        }
        #endregion
    }
}
