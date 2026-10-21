using UnityEngine;

// Small touches that keep the main menu backdrop alive: slowly turning sky
// (clouds drift with it) and passing cloud shadows dimming the sun.
public class MenuAmbience : MonoBehaviour
{
    [SerializeField] float skyDegreesPerSecond = 0.4f;
    [SerializeField] Light sun;
    [SerializeField] float cloudShadowDepth = 0.25f;
    [SerializeField] float cloudShadowSpeed = 0.05f;

    Material _sky;
    float _baseSunIntensity;

    void Awake()
    {
        // Instance so the shared asset is not modified in the project.
        if (RenderSettings.skybox != null)
        {
            _sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = _sky;
        }
        if (sun != null) _baseSunIntensity = sun.intensity;
    }

    void Update()
    {
        if (_sky != null && _sky.HasProperty("_Rotation"))
            _sky.SetFloat("_Rotation", Time.time * skyDegreesPerSecond % 360f);

        if (sun != null)
        {
            float n = Mathf.PerlinNoise(Time.time * cloudShadowSpeed, 3.7f);
            sun.intensity = _baseSunIntensity * (1f - cloudShadowDepth * Mathf.SmoothStep(0.35f, 0.75f, n));
        }
    }

    void OnDestroy()
    {
        if (_sky != null) Destroy(_sky);
    }
}
