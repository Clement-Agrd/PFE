using UnityEngine;
using Core.HealthSystem;

public class TestDegats : MonoBehaviour
{
    private Health _health;

    private void Awake() => _health = GetComponent<Health>();

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            _health.TakeDamage(25);
    }
}