using UnityEditor;
using UnityEngine;

namespace ZoneFx.Editor
{
    /// <summary>Inspector de ZoneFxController : presets en un clic (édition ou Play), boutons de test Show / Hide / Pulse (Play).</summary>
    [CustomEditor(typeof(ZoneFxController))]
    public sealed class ZoneFxControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (ZoneFxController)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (ZoneFxPreset preset in System.Enum.GetValues(typeof(ZoneFxPreset)))
                {
                    if (!GUILayout.Button(preset.ToString())) continue;
                    Undo.RecordObject(controller, "Preset de zone");
                    controller.SetPreset(preset);
                    EditorUtility.SetDirty(controller);
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.LabelField("Test (en Play)", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Show")) controller.Show();
                    if (GUILayout.Button("Hide")) controller.Hide();
                    if (GUILayout.Button("Pulse")) controller.Pulse();
                }
            }
            if (!Application.isPlaying) EditorGUILayout.HelpBox("Show / Hide / Pulse sont actifs en mode Play. Les presets et le rayon se voient aussi en édition.", MessageType.Info);
        }
    }
}
