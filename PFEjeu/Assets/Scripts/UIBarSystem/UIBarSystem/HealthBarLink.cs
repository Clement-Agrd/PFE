using UnityEngine;
using Core.HealthSystem;
using Core.TowerDefense;
using Core.UIBarSystem;

[RequireComponent(typeof(Health))]
public class HealthBarLink : MonoBehaviour
{
    [SerializeField] private UIBar healthBar;

    private Health _health;
    private EnemyHealthBars _sharedBars;
    private bool _useSharedBar;

    public Transform BarAnchor => healthBar != null ? healthBar.transform : null;
    public bool HasBarAnchor => BarAnchor != null && (_useSharedBar || BarAnchor.gameObject.activeInHierarchy);

    private void Awake()
    {
        _health = GetComponent<Health>();
        if (healthBar == null) return;
        var canvas = healthBar.GetComponent<Canvas>();
        _useSharedBar = canvas != null && canvas.renderMode == RenderMode.WorldSpace &&
            canvas.gameObject != gameObject && canvas.transform.IsChildOf(transform) &&
            (GetComponentInParent<EnemyController>() != null || GetComponentInParent<NavEnemy>() != null);

        if (_useSharedBar) canvas.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (_useSharedBar)
        {
            _sharedBars = EnemyHealthBars.ForScene(gameObject.scene);
            _sharedBars.Register(this, _health, BarAnchor);
        }
        else if (healthBar != null)
        {
            healthBar.SetValue(_health.CurrentHealth, _health.MaxHealth);
            healthBar.SnapToValue();
        }

        _health.OnHealthChanged += UpdateBar;
    }

    private void OnDisable()
    {
        _health.OnHealthChanged -= UpdateBar;
        if (_sharedBars != null) _sharedBars.Unregister(this);
        _sharedBars = null;
    }

    private void UpdateBar(int current, int max)
    {
        if (_sharedBars != null) _sharedBars.SetValue(this, current, max);
        else if (!_useSharedBar && healthBar != null) healthBar.SetValue(current, max);
    }
}
