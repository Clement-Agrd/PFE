using UnityEngine;

// Slow, subtle camera sway so the main menu backdrop feels alive.
public class MenuCameraDrift : MonoBehaviour
{
    [SerializeField] float swayDegrees = 1.2f;
    [SerializeField] float bobMeters = 0.15f;
    [SerializeField] float speed = 0.12f;

    Vector3 _startPos;
    Quaternion _startRot;

    void Awake()
    {
        _startPos = transform.position;
        _startRot = transform.rotation;
    }

    void Update()
    {
        float t = Time.time * speed;
        float yaw = Mathf.Sin(t) * swayDegrees;
        float pitch = Mathf.Sin(t * 0.7f + 1f) * swayDegrees * 0.4f;
        transform.rotation = _startRot * Quaternion.Euler(pitch, yaw, 0f);
        transform.position = _startPos + new Vector3(Mathf.Sin(t * 0.5f) * bobMeters, Mathf.Sin(t * 0.8f) * bobMeters * 0.5f, 0f);
    }
}
