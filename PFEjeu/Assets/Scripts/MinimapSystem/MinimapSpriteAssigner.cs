using UnityEngine;
using UnityEngine.UI;

namespace Core.Minimap
{
    /// <summary>
    /// Les sprites de la minimap sont générés en mémoire (non sauvegardables
    /// dans la scène). Ce composant les rattache à l'Image au lancement.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public sealed class MinimapSpriteAssigner : MonoBehaviour
    {
        public enum Kind { Circle, Ring, Square, Diamond, Triangle }

        [SerializeField] private Kind kind = Kind.Circle;

        private void Awake() => Apply();
        private void OnEnable() => Apply();

        private void Apply()
        {
            var img = GetComponent<Image>();
            img.sprite = kind switch
            {
                Kind.Ring => MinimapSprites.Ring,
                Kind.Square => MinimapSprites.Square,
                Kind.Diamond => MinimapSprites.Diamond,
                Kind.Triangle => MinimapSprites.Triangle,
                _ => MinimapSprites.Circle
            };
        }
    }
}
