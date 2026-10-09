using UnityEngine;

// Gentle flame flicker for a campfire point light.
[RequireComponent(typeof(Light))]
public class FireLightFlicker : MonoBehaviour
{
    [SerializeField] float amount = 0.18f;
    [SerializeField] float speed = 6f;

    Light _light;
    float _baseIntensity;
    float _seed;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light.intensity;
        _seed = Random.value * 100f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(_seed, Time.time * speed);
        _light.intensity = _baseIntensity * (1f - amount + n * amount * 2f);
    }
}
