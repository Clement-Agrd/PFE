using UnityEngine;
using Core.HealthSystem;
using Core.UIBarSystem;

[RequireComponent(typeof(Health))]
public class HealthBarLink : MonoBehaviour
{
    [Tooltip("Glisse ici le composant UIBar de ton ennemi.")]
    [SerializeField] private UIBar healthBar;

    private Health _health;

    private void Awake()
    {
        // On récupère automatiquement le script Health sur le même objet
        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        // 1. On s'abonne à l'événement : à chaque changement, on met à jour la barre
        _health.OnHealthChanged += UpdateBar;

        // 2. On initialise la barre tout de suite au bon niveau (sans animation de remplissage)
        if (healthBar != null)
        {
            healthBar.SetValue(_health.CurrentHealth, _health.MaxHealth);
            healthBar.SnapToValue();
        }
    }

    private void OnDisable()
    {
        // Sécurité : on se désabonne quand l'objet est désactivé ou détruit
        _health.OnHealthChanged -= UpdateBar;
    }

    private void UpdateBar(int current, int max)
    {
        if (healthBar != null)
        {
            // Appelle la fonction de ton UIBar pour animer la descente/montée
            healthBar.SetValue(current, max);
        }
    }
}