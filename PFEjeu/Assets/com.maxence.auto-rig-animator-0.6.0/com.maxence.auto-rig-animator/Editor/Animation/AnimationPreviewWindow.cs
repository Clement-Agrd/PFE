using UnityEditor;
using UnityEngine;

namespace Maxence.AutoRig.Editor
{
    /// <summary>
    /// Previews any AnimationClip — premade Humanoid clips included — directly on a rig built by
    /// this tool, with no AnimatorController needed: it samples the clip straight onto the rig via
    /// AnimationMode, the same mechanism the built-in Animation window uses for its own preview.
    /// A Humanoid clip stores muscle curves (Avatar space), not plain bone rotations, so the target
    /// must have an Animator with its Avatar assigned — exactly what Phase 3 produced.
    /// </summary>
    internal sealed class AnimationPreviewWindow : EditorWindow
    {
        #region Constants

        private const string WindowTitle = "Auto Rig – Preview Anim";
        private const float TransportButtonWidth = 60f;

        #endregion

        #region Serialized State

        [SerializeField] private GameObject _rigRoot;
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private float _time;
        [SerializeField] private bool _isPlaying;

        #endregion

        #region Runtime State

        private double _lastUpdateTime;
        private bool _isSampling;

        // Feedback from the last "Corriger automatiquement" click (ClipHumanoidFixer), shown until the
        // clip field changes again. Not serialized: it's transient UI feedback, not rig/clip data.
        private HumanoidFixResult? _lastFixResult;

        #endregion

        [MenuItem("Tools/Auto Rig/Preview Animation")]
        public static void Open()
        {
            GetWindow<AnimationPreviewWindow>(WindowTitle);
        }

        #region Unity Lifecycle

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            EditorApplication.update += OnEditorUpdate;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopSampling();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawTargetFields();

            if (_rigRoot == null || _clip == null)
            {
                EditorGUILayout.HelpBox("Choisis un rig (le <nom>_Rig généré en Phase 3, avec Animator + Avatar) et un clip à prévisualiser.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            DrawTransportControls();
        }

        private void DrawTargetFields()
        {
            EditorGUI.BeginChangeCheck();
            _rigRoot = (GameObject)EditorGUILayout.ObjectField("Rig (Animator)", _rigRoot, typeof(GameObject), true);
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", _clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                _time = 0f;
                _isPlaying = false;
                _lastFixResult = null;
                StopSampling();
            }

            if (_rigRoot != null && _rigRoot.GetComponent<Animator>() == null)
            {
                EditorGUILayout.HelpBox("Ce GameObject n'a pas d'Animator (choisis le <nom>_Rig généré en Phase 3, pas le mesh seul).", MessageType.Warning);
            }

            if (_clip != null && !_clip.isHumanMotion)
            {
                DrawNonHumanoidClipWarning();
            }
        }

        /// <summary>
        /// Explains why a Generic clip won't retarget correctly onto an auto-rigged skeleton, and
        /// offers to fix the clip's SOURCE FILE import settings automatically (ClipHumanoidFixer).
        /// That fix changes import settings on disk, which Unity's Undo does not cover — the button
        /// says so up front rather than after the fact.
        /// </summary>
        private void DrawNonHumanoidClipWarning()
        {
            EditorGUILayout.HelpBox("Ce clip n'est pas marqué Humanoid : il ne se retargettera correctement que si les noms d'os correspondent exactement.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Corriger automatiquement (import → Humanoid)"))
                {
                    _lastFixResult = ClipHumanoidFixer.TryFixToHumanoid(_clip);
                }

                GUILayout.Label("Modifie les réglages d'import du fichier source (non annulable par Ctrl+Z).", EditorStyles.miniLabel);
            }

            if (_lastFixResult.HasValue)
            {
                MessageType messageType = _lastFixResult.Value == HumanoidFixResult.FixedSuccessfully || _lastFixResult.Value == HumanoidFixResult.AlreadyHumanoid
                    ? MessageType.Info
                    : MessageType.Warning;
                EditorGUILayout.HelpBox(ClipHumanoidFixer.Describe(_lastFixResult.Value), messageType);
            }
        }

        private void DrawTransportControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string playLabel = _isPlaying ? "Pause" : "Play";
                if (GUILayout.Button(playLabel, GUILayout.Width(TransportButtonWidth)))
                {
                    _isPlaying = !_isPlaying;
                    _lastUpdateTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("Stop", GUILayout.Width(TransportButtonWidth)))
                {
                    _isPlaying = false;
                    _time = 0f;
                    Sample();
                }
            }

            EditorGUI.BeginChangeCheck();
            _time = EditorGUILayout.Slider("Temps", _time, 0f, _clip.length);
            if (EditorGUI.EndChangeCheck())
            {
                Sample();
            }
        }

        #endregion

        #region Playback

        private void OnEditorUpdate()
        {
            if (!_isPlaying || _rigRoot == null || _clip == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(now - _lastUpdateTime);
            _lastUpdateTime = now;

            _time += deltaTime;
            if (_clip.length > 0f)
            {
                _time %= _clip.length;
            }

            Sample();
            Repaint();
        }

        private void Sample()
        {
            if (_rigRoot == null || _clip == null)
            {
                return;
            }

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            _isSampling = true;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(_rigRoot, _clip, _time);
            AnimationMode.EndSampling();

            SceneView.RepaintAll();
        }

        private void StopSampling()
        {
            if (_isSampling && AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            _isSampling = false;
        }

        #endregion
    }
}
