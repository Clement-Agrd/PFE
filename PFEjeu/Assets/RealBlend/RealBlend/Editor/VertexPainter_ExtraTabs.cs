using System;
using UnityEditor;
using UnityEngine;

namespace RealBlend
{
    public interface IVertexPainterTab
    {
        void OnEnable();
        void OnDisable();
        void OnGUI();
        void OnSceneGUI(SceneView sceneView);
        void OnUndoRedo();
        void OnSelectionChange();
        void OnReload();
    }

    [Serializable]
    public class VertexPainter_CreationTab : IVertexPainterTab
    {
        [SerializeField] private RealBlendCreationSettings _settings = new RealBlendCreationSettings();
        [SerializeField] private int _activeModeIndex = 0;
        [SerializeField] private RealBlendFloorCreateMode _floorMode = new RealBlendFloorCreateMode();
        [SerializeField] private RealBlendWallCreateMode _wallMode = new RealBlendWallCreateMode();
        [SerializeField] private RealBlendCurvedWallCreateMode _curvedWallMode = new RealBlendCurvedWallCreateMode();
        [SerializeField] private RealBlendBoxCreateMode _boxMode = new RealBlendBoxCreateMode();
        [SerializeField] private RealBlendSplineCreateMode _splineMode = new RealBlendSplineCreateMode();

        [NonSerialized] private EditorWindow _owner;
        private RealBlendCreationPreviewState _preview = new RealBlendCreationPreviewState();
        private IRealBlendCreationMode[] _modes;
        private string[] _modeNames;
        private Vector2 _scrollPos;

        public VertexPainter_CreationTab(EditorWindow owner)
        {
            RebindOwner(owner);
            EnsureModes();
        }

        public void RebindOwner(EditorWindow owner)
        {
            _owner = owner;
        }

        public void OnGUI()
        {
            EnsureModes();
            GUILayout.Label("Create Paintable Mesh", EditorStyles.boldLabel);

            GUIStyle modeStyle = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 28,
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 0, 0)
            };

            RecordUndoForInteractiveCreateInput();
            EditorGUI.BeginChangeCheck();
            _activeModeIndex = GUILayout.Toolbar(Mathf.Clamp(_activeModeIndex, 0, _modes.Length - 1), _modeNames, modeStyle, GUILayout.Height(28));
            if (EditorGUI.EndChangeCheck())
            {
                _preview.hasValidPreview = false;
                SceneView.RepaintAll();
                _owner?.Repaint();
            }

            EditorGUI.BeginChangeCheck();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            GUILayout.Space(10);
            IRealBlendCreationMode activeMode = _modes[_activeModeIndex];
            activeMode.OnGUI(_settings);
            RealBlendCreationCore.DrawSharedSettings(_settings);

            activeMode.GetStats(_settings, out int vertexCount, out int triangleCount);
            RealBlendCreationCore.DrawStats(vertexCount, triangleCount);

            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(!activeMode.CanGenerate(_settings)))
            {
                if (GUILayout.Button($"Generate {activeMode.DisplayName}", GUILayout.Height(40)))
                {
                    RealBlendMeshBuildData buildData = activeMode.BuildMesh(_settings);
                    RealBlendCreationCore.CreatePaintableObject(buildData, _settings.defaultMaterial, activeMode.GetPlacement(_preview));

                    if (activeMode is IRealBlendAdditionalCreationMode additionalMode)
                    {
                        foreach (RealBlendAdditionalMeshBuild additional in additionalMode.BuildAdditionalMeshes(_settings, _preview))
                        {
                            if (additional.data != null)
                                RealBlendCreationCore.CreatePaintableObject(additional.data, additional.material, additional.placement);
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck())
            {
                if (_owner != null) EditorUtility.SetDirty(_owner);
                SceneView.RepaintAll();
            }
        }

        public void OnSceneGUI(SceneView sceneView)
        {
            EnsureModes();
            if (!_settings.showPreview) return;

            _modes[Mathf.Clamp(_activeModeIndex, 0, _modes.Length - 1)].OnSceneGUI(sceneView, _settings, _preview);
            sceneView.Repaint();
        }

        public void OnEnable() { EnsureModes(); }
        public void OnDisable() { }
        public void OnUndoRedo()
        {
            _preview.hasValidPreview = false;
            SceneView.RepaintAll();
            _owner?.Repaint();
        }
        public void OnSelectionChange() { }
        public void OnReload() { EnsureModes(); }

        private void EnsureModes()
        {
            if (_settings == null) _settings = new RealBlendCreationSettings();
            if (_preview == null) _preview = new RealBlendCreationPreviewState();
            if (_floorMode == null) _floorMode = new RealBlendFloorCreateMode();
            if (_wallMode == null) _wallMode = new RealBlendWallCreateMode();
            if (_curvedWallMode == null) _curvedWallMode = new RealBlendCurvedWallCreateMode();
            if (_boxMode == null) _boxMode = new RealBlendBoxCreateMode();
            if (_splineMode == null) _splineMode = new RealBlendSplineCreateMode();

            _modes = new IRealBlendCreationMode[]
            {
                _floorMode,
                _wallMode,
                _curvedWallMode,
                _boxMode,
                _splineMode
            };

            _modeNames = new string[_modes.Length];
            for (int i = 0; i < _modes.Length; i++)
                _modeNames[i] = _modes[i].DisplayName;
        }

        private void RecordUndoForInteractiveCreateInput()
        {
            if (_owner == null) return;

            Event e = Event.current;
            if (e == null) return;
            if (e.type == EventType.ExecuteCommand && (e.commandName == "UndoRedoPerformed" || e.commandName == "Undo" || e.commandName == "Redo"))
                return;
            if (e.type == EventType.KeyDown && (e.control || e.command) && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
                return;

            if (e.type == EventType.MouseDown || e.type == EventType.KeyDown || e.type == EventType.ExecuteCommand)
                Undo.RecordObject(_owner, "Change RealBlend Create Settings");
        }
    }
}
