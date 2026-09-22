using UnityEngine;

public sealed class AutoDestroyVFX : MonoBehaviour
{
    [SerializeField, Min(0.01f)]
    private float lifetime = 2f;

    private void Start()
    {
        Destroy(
            gameObject,
            lifetime
        );
    }
}