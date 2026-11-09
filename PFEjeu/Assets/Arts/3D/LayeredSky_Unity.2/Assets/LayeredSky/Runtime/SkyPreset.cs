using UnityEngine;

namespace LayeredSky
{
    /// <summary>
    /// Une ambiance de ciel (ex. Jour, Crépuscule, Nuit). ScriptableObject : partageable entre scènes,
    /// référencé par le SkyController ou par votre gestionnaire de vagues.
    /// </summary>
    [CreateAssetMenu(menuName = "Layered Sky/Sky Preset", fileName = "SkyPreset_New")]
    public sealed class SkyPreset : ScriptableObject
    {
        [SerializeField] private string displayName = "Nouvelle ambiance";
        [SerializeField] private SkyState state;

        public string DisplayName => displayName;
        public SkyState State => state;

#if UNITY_EDITOR
        /// <summary>Utilisé uniquement par les menus d'éditeur pour créer les presets par défaut.</summary>
        public void EditorInitialize(string presetName, in SkyState initialState)
        {
            displayName = presetName;
            state = initialState;
        }
#endif
    }
}
