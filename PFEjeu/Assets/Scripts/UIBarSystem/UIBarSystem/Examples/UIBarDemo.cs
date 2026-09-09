using UnityEngine;

namespace Core.UIBarSystem.Examples
{
    /// <summary>
    /// Démo : 1 = perdre 15, 2 = gagner 10, 3 = plein. Montre le remplissage animé
    /// et la traînée de dégâts. Pose ce composant à côté d'un UIBar.
    /// </summary>
    [RequireComponent(typeof(UIBar))]
    public sealed class UIBarDemo : MonoBehaviour
    {
        [SerializeField] private float max = 100f;

        private UIBar _bar;
        private float _current;

        private void Awake() => _bar = GetComponent<UIBar>();

        private void Start()
        {
            _current = max;
            _bar.SetValue(_current, max);
            _bar.SnapToValue();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetCurrent(_current - 15f);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetCurrent(_current + 10f);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetCurrent(max);
        }

        private void SetCurrent(float value)
        {
            _current = Mathf.Clamp(value, 0f, max);
            _bar.SetValue(_current, max);
        }
    }
}
