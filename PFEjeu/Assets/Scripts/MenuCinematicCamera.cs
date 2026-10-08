using UnityEngine;

// Slow cinematic dolly for the main menu backdrop. The camera glides along a
// smooth spline of waypoints while its focus glides along a second spline,
// easing in and out so the shot goes back and forth without any visible cut.
public class MenuCinematicCamera : MonoBehaviour
{
    [SerializeField] Transform[] positions;
    [SerializeField] Transform[] targets;
    [Tooltip("Seconds for one full trip from the first to the last waypoint.")]
    [SerializeField] float tripDuration = 55f;
    [SerializeField] float fieldOfView = 42f;
    [Tooltip("Tiny handheld breathing, in degrees. Keep it very low.")]
    [SerializeField] float breathing = 0.15f;

    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam != null) _cam.fieldOfView = fieldOfView;
        Apply(0f);
    }

    void LateUpdate()
    {
        if (positions == null || positions.Length < 2) return;
        // 0 -> 1 -> 0 with eased ends
        float phase = Time.time / (tripDuration * 2f);
        float u = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
        Apply(u);
    }

    void Apply(float u)
    {
        if (positions == null || positions.Length < 2 || targets == null || targets.Length < 2) return;
        Vector3 pos = Spline(positions, u);
        Vector3 look = Spline(targets, u);
        transform.position = pos;
        Quaternion rot = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
        float t = Time.time * 0.2f;
        rot *= Quaternion.Euler(Mathf.Sin(t) * breathing, Mathf.Sin(t * 0.77f + 1f) * breathing, 0f);
        transform.rotation = rot;
    }

    static Vector3 Spline(Transform[] pts, float u)
    {
        int segs = pts.Length - 1;
        float f = Mathf.Clamp01(u) * segs;
        int i = Mathf.Min(Mathf.FloorToInt(f), segs - 1);
        float t = f - i;
        Vector3 p0 = pts[Mathf.Max(i - 1, 0)].position;
        Vector3 p1 = pts[i].position;
        Vector3 p2 = pts[i + 1].position;
        Vector3 p3 = pts[Mathf.Min(i + 2, pts.Length - 1)].position;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
    }

    void OnDrawGizmosSelected()
    {
        if (positions == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < positions.Length - 1; i++)
            if (positions[i] != null && positions[i + 1] != null)
                Gizmos.DrawLine(positions[i].position, positions[i + 1].position);
    }
}
